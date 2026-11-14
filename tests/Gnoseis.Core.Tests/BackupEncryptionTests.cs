/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Security.Cryptography;
using Gnoseis.Core.Backup;
using Gnoseis.Core.Data;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>What Back up does with encryption on, off, or set up elsewhere; setting up; keys for restore.</summary>
public class BackupEncryptionTests : IDisposable
{
    private const string Passphrase = "a passphrase for these tests";

    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;
    private readonly StoredBackupKey _store;
    private readonly BackupEncryption _encryption;

    public BackupEncryptionTests()
    {
        _data = _test.Open();
        _store = new StoredBackupKey(_test.PathFor(StoredBackupKey.FileName), new XorProtector(0x5A));
        _encryption = new BackupEncryption(_data.Backup, _store);
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task With_encryption_off_back_up_writes_a_plain_file()
    {
        var path = _test.PathFor("plain.backup");

        Assert.Equal(BackupEncryptionState.Off, _encryption.State);
        Assert.False(await _encryption.BackUpAsync(path));

        Assert.False(EncryptedBackupFormat.IsEncryptedFile(path));
    }

    [Fact]
    public async Task With_encryption_on_back_up_writes_an_encrypted_file_and_without_encryption_a_plain_one()
    {
        Assert.True(await _encryption.SetPassphraseAsync(Passphrase));
        var encrypted = _test.PathFor("encrypted.backup");
        var plain = _test.PathFor("plain.backup");

        Assert.Equal(BackupEncryptionState.On, _encryption.State);
        Assert.True(await _encryption.BackUpAsync(encrypted));
        await _encryption.BackUpWithoutEncryptionAsync(plain);

        Assert.True(EncryptedBackupFormat.IsEncryptedFile(encrypted));
        Assert.False(EncryptedBackupFormat.IsEncryptedFile(plain));
        Assert.Equal(DatabaseSchema.RoomIdentityHash, TestDatabase.ScalarRaw(plain, "SELECT identity_hash FROM room_master_table;"));
    }

    [Fact]
    public async Task With_a_key_set_up_elsewhere_back_up_writes_nothing()
    {
        Assert.True(await _encryption.SetPassphraseAsync(Passphrase));
        var elsewhere = new BackupEncryption(_data.Backup, new StoredBackupKey(_store.FilePath, new XorProtector(0x77)));
        var path = _test.PathFor("refused.backup");

        Assert.Equal(BackupEncryptionState.KeyUnavailable, elsewhere.State);
        await Assert.ThrowsAsync<BackupKeyUnavailableException>(() => elsewhere.BackUpAsync(path));

        Assert.False(File.Exists(path));
        await elsewhere.BackUpWithoutEncryptionAsync(path); // still possible, chosen knowingly
        Assert.False(EncryptedBackupFormat.IsEncryptedFile(path));
    }

    [Fact]
    public async Task Turning_off_goes_back_to_plain_backups_and_old_files_still_open()
    {
        Assert.True(await _encryption.SetPassphraseAsync(Passphrase));
        var old = _test.PathFor("old.backup");
        await _encryption.BackUpAsync(old);

        _encryption.TurnOff();

        Assert.Equal(BackupEncryptionState.Off, _encryption.State);
        Assert.False(await _encryption.BackUpAsync(_test.PathFor("new.backup")));
        using var staged = await BackupRepository.StageAsync(old, header =>
            BackupEncryption.TryPassphraseAsync(old, header, Passphrase));
        Assert.True(staged.Inspection.IsValid);
    }

    [Fact]
    public async Task Changing_the_passphrase_keeps_old_files_on_their_old_passphrase()
    {
        Assert.True(await _encryption.SetPassphraseAsync(Passphrase));
        var old = _test.PathFor("old.backup");
        await _encryption.BackUpAsync(old);

        Assert.True(await _encryption.SetPassphraseAsync("a different passphrase now"));
        var header = ReadHeader(old);

        Assert.Null(_encryption.StoredKeyFor(header));
        using var key = await BackupEncryption.TryPassphraseAsync(old, header, Passphrase);
        Assert.NotNull(key);
    }

    [Fact]
    public async Task A_passphrase_that_cant_be_used_is_not_set_up()
    {
        Assert.False(await _encryption.SetPassphraseAsync("abc" + (char)0xD800 + " long enough"));
        Assert.Equal(BackupEncryptionState.Off, _encryption.State);
    }

    [Fact]
    public void Suggested_file_names_say_whether_the_backup_is_encrypted()
    {
        var now = new DateTime(2026, 11, 10, 14, 30, 5);

        Assert.Equal("gnoseis_data_2026-11-10_14-30-05", BackupEncryption.SuggestedFileName(now, encrypted: false));
        Assert.Equal("gnoseis_data_2026-11-10_14-30-05.encrypted", BackupEncryption.SuggestedFileName(now, encrypted: true));
    }

    // ---- Keys for restore

    [Fact]
    public async Task The_stored_key_is_used_only_for_files_it_made()
    {
        Assert.True(await _encryption.SetPassphraseAsync(Passphrase));
        var mine = _test.PathFor("mine.backup");
        await _encryption.BackUpAsync(mine);
        var other = _test.PathFor("other.backup");
        using (var otherKey = BackupMasterKey.Create(BackupPassphrase.ToKeyBytes(Passphrase)!))
        {
            await _data.Backup.ExportAsync(other, otherKey); // same passphrase, other KDF salt
        }

        using var forMine = _encryption.StoredKeyFor(ReadHeader(mine));
        using var forOther = _encryption.StoredKeyFor(ReadHeader(other));

        Assert.NotNull(forMine);
        Assert.Null(forOther);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    public async Task A_typed_passphrase_is_checked_on_the_first_chunk(int notes)
    {
        for (var i = 0; i < notes; i++)
        {
            await _data.Notes.AddNoteAsync(new Note { Title = $"Note {i}", TextContents = new string('x', 2000) });
        }
        Assert.True(await _encryption.SetPassphraseAsync(Passphrase));
        var path = _test.PathFor("file.backup");
        await _encryption.BackUpAsync(path);
        var header = ReadHeader(path);

        using var right = await BackupEncryption.TryPassphraseAsync(path, header, "  a passphrase for\nthese tests ");
        using var wrong = await BackupEncryption.TryPassphraseAsync(path, header, "not the passphrase");
        using var unusable = await BackupEncryption.TryPassphraseAsync(path, header, Passphrase + (char)0xDC00);

        Assert.NotNull(right);
        Assert.Null(wrong);
        Assert.Null(unusable);
    }

    [Fact]
    public async Task The_first_chunk_check_fails_for_a_damaged_first_chunk()
    {
        Assert.True(await _encryption.SetPassphraseAsync(Passphrase));
        var path = _test.PathFor("file.backup");
        await _encryption.BackUpAsync(path);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[EncryptedBackupFormat.HeaderSize + 3] ^= 1;
        await File.WriteAllBytesAsync(path, bytes);
        using var key = _store.Load()!;

        Assert.False(EncryptedBackupFormat.KeyOpens(path, key));
    }

    [Fact]
    public async Task A_key_kept_after_a_restore_turns_encryption_on_with_that_passphrase()
    {
        using var other = BackupMasterKey.Create(BackupPassphrase.ToKeyBytes(Passphrase)!);
        var path = _test.PathFor("from-elsewhere.backup");
        await _data.Backup.ExportAsync(path, other);

        using (var typed = await BackupEncryption.TryPassphraseAsync(path, ReadHeader(path), Passphrase))
        {
            _encryption.KeepKey(typed!);
        }

        Assert.Equal(BackupEncryptionState.On, _encryption.State);
        using var stored = _encryption.StoredKeyFor(ReadHeader(path));
        Assert.NotNull(stored);
    }

    private static EncryptedBackupHeader ReadHeader(string path)
    {
        using var input = File.OpenRead(path);
        return EncryptedBackupFormat.ReadHeader(input);
    }

    /// <summary>Stands for DPAPI: another "user" can't unprotect.</summary>
    private sealed class XorProtector(byte user) : IKeyProtector
    {
        public byte[] Protect(byte[] data) => [user, .. data.Select(b => (byte)(b ^ user))];

        public byte[] Unprotect(byte[] data) =>
            data.Length > 0 && data[0] == user
                ? data[1..].Select(b => (byte)(b ^ user)).ToArray()
                : throw new CryptographicException("Not this user.");
    }
}
