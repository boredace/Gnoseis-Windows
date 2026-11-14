/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Security.Cryptography;
using Gnoseis.Core.Backup;

namespace Gnoseis.Core.Tests;

/// <summary>The backup master key kept on the device, protected for the current user.</summary>
public class StoredBackupKeyTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly FakeProtector _thisUser = new(0x5A);

    public void Dispose() => _test.Dispose();

    private string KeyFile => _test.PathFor(StoredBackupKey.FileName);

    private static BackupMasterKey SomeKey(byte fill = 0x11, BackupKdfParameters? parameters = null) =>
        BackupMasterKey.FromKey(Filled(32, fill), Filled(32, 0x22), parameters ?? BackupKdfParameters.Written);

    [Fact]
    public void A_saved_key_loads_with_its_salt_and_settings()
    {
        var store = new StoredBackupKey(KeyFile, _thisUser);
        using var key = SomeKey(parameters: new BackupKdfParameters(32768, 4, 2));

        store.Save(key);
        using var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(key.Key.ToArray(), loaded.Key.ToArray());
        Assert.Equal(key.KdfSalt.ToArray(), loaded.KdfSalt.ToArray());
        Assert.Equal(key.Parameters, loaded.Parameters);
    }

    [Fact]
    public void No_file_means_no_stored_key()
    {
        var store = new StoredBackupKey(KeyFile, _thisUser);

        Assert.False(store.FileExists);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Changing_the_passphrase_replaces_the_stored_key()
    {
        var store = new StoredBackupKey(KeyFile, _thisUser);
        using var first = SomeKey(0x11);
        using var second = SomeKey(0x33);

        store.Save(first);
        store.Save(second);

        using var loaded = store.Load();
        Assert.Equal(second.Key.ToArray(), loaded!.Key.ToArray());
        Assert.False(File.Exists(KeyFile + ".tmp"));
    }

    [Fact]
    public void Turning_off_deletes_the_stored_key()
    {
        var store = new StoredBackupKey(KeyFile, _thisUser);
        using var key = SomeKey();
        store.Save(key);

        store.Delete();
        store.Delete();

        Assert.False(File.Exists(KeyFile));
        Assert.Null(store.Load());
    }

    [Fact]
    public void A_key_file_from_another_user_or_pc_counts_as_no_key_and_is_kept()
    {
        using var key = SomeKey();
        new StoredBackupKey(KeyFile, _thisUser).Save(key);
        var bytes = File.ReadAllBytes(KeyFile);

        var elsewhere = new StoredBackupKey(KeyFile, new FakeProtector(0x77));

        Assert.True(elsewhere.FileExists);
        Assert.Null(elsewhere.Load());
        Assert.Equal(bytes, File.ReadAllBytes(KeyFile));
        using var backHome = new StoredBackupKey(KeyFile, _thisUser).Load();
        Assert.NotNull(backHome);
    }

    [Fact]
    public void The_key_file_holds_only_what_the_protector_returned()
    {
        var store = new StoredBackupKey(KeyFile, _thisUser);
        using var key = SomeKey();

        store.Save(key);

        var bytes = File.ReadAllBytes(KeyFile);
        Assert.Equal(_thisUser.LastProtected, bytes);
        Assert.False(Contains(bytes, key.Key.ToArray()));
        Assert.Equal(77, _thisUser.LastPlainLength);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("version")]
    [InlineData("settings")]
    public void A_damaged_or_unknown_key_file_counts_as_no_key(string damage)
    {
        using var key = SomeKey();
        new StoredBackupKey(KeyFile, _thisUser).Save(key);
        var plain = _thisUser.Unprotect(File.ReadAllBytes(KeyFile));
        switch (damage)
        {
            case "short": plain = plain[..40]; break;
            case "version": plain[0] = 2; break;
            case "settings": plain[1] = 0xFF; break; // memory far over the limit
        }
        File.WriteAllBytes(KeyFile, _thisUser.Protect(plain));

        Assert.Null(new StoredBackupKey(KeyFile, _thisUser).Load());
    }

    [Fact]
    public async Task A_stored_key_opens_the_backups_it_made()
    {
        var store = new StoredBackupKey(KeyFile, _thisUser);
        using (var key = BackupMasterKey.Create(BackupPassphrase.ToKeyBytes("a passphrase kept on this device")!))
        {
            store.Save(key);
        }
        var data = _test.Open();
        var path = _test.PathFor("encrypted.backup");
        using (var key = store.Load()!)
        {
            await data.Backup.ExportAsync(path, key);
        }

        using var stored = store.Load()!;
        using var staged = await Data.BackupRepository.StageAsync(path, header =>
            Task.FromResult(stored.Fits(header) ? store.Load() : null));

        Assert.True(staged.Inspection.IsValid);
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    private static byte[] Filled(int length, byte value) => Enumerable.Repeat(value, length).ToArray();

    /// <summary>Stands for DPAPI: a different "user" can't unprotect.</summary>
    private sealed class FakeProtector(byte user) : IKeyProtector
    {
        public byte[]? LastProtected { get; private set; }
        public int LastPlainLength { get; private set; }

        public byte[] Protect(byte[] data)
        {
            LastPlainLength = data.Length;
            LastProtected = [user, .. data.Select(b => (byte)(b ^ user))];
            return LastProtected;
        }

        public byte[] Unprotect(byte[] data)
        {
            if (data.Length == 0 || data[0] != user)
            {
                throw new CryptographicException("The data can't be unprotected by this user.");
            }
            return data[1..].Select(b => (byte)(b ^ user)).ToArray();
        }
    }
}
