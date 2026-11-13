/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Gnoseis.Core.Backup;
using Gnoseis.Core.Data;
using Gnoseis.Core.Models;
using Xunit.Abstractions;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Gnoseis-Android's shared encrypted backup, encrypted-v1-android.backup, restored through BackupRepository.
/// The file is a byte-for-byte copy of the one in Gnoseis-Android's tests; every refusal is built from it here
/// and must leave the live database unchanged.
/// </summary>
public class SharedAndroidBackupTests : IDisposable
{
    private const string AndroidFile = "encrypted-v1-android.backup";
    private const string AndroidSha256 = "8d1f0eec77f44a9c4d4ce5bc2d29104f24652f8df21035498e996549a2216cb4";
    private const string AndroidPassphrase = "gnoseis shared test backup made on android";
    private const string WrongDamaged = "Wrong passphrase, or the file is damaged";

    private static readonly string[] AndroidTitles = ["Shared backup note one", "Shared backup note two", "Shared backup note three"];

    private readonly ITestOutputHelper _output;
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;
    private readonly byte[] _shared = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SharedBackups", AndroidFile));

    public SharedAndroidBackupTests(ITestOutputHelper output)
    {
        _output = output;
        _data = _test.Open();
        // Something of this PC's own, which a refusal must leave in place
        _data.Notes.AddNoteAsync(new Note { Title = "Made on this PC" }).GetAwaiter().GetResult();
    }

    public void Dispose() => _test.Dispose();

    // ---- The file

    [Fact]
    public void The_copy_is_byte_for_byte_Androids() =>
        Assert.Equal(AndroidSha256, Convert.ToHexStringLower(SHA256.HashData(_shared)));

    [Fact]
    public async Task The_header_has_the_written_settings_and_the_size_fits_the_chunks()
    {
        var header = EncryptedBackupFormat.ReadHeader(new MemoryStream(_shared));
        Assert.Equal(BackupKdfParameters.Written, header.KdfParameters);
        Assert.Equal(EncryptedBackupFormat.WrittenChunkSize, header.ChunkSize);

        var plain = _test.PathFor("decrypted.backup");
        using (var output = File.Create(plain))
        {
            EncryptedBackupFormat.Decrypt(new MemoryStream(_shared), EncryptedBackupFormatTests.Passphrase(AndroidPassphrase), output);
        }
        var plainSize = new FileInfo(plain).Length;
        var chunks = (plainSize + header.ChunkSize - 1) / header.ChunkSize;
        Assert.Equal(92 + plainSize + 16 * chunks, _shared.Length);

        var inspection = await BackupRepository.InspectAsync(plain);
        Assert.True(inspection.IsValid, inspection.Problem);
        Assert.Equal(3L, TestDatabase.ScalarRaw(plain, "PRAGMA user_version;"));
    }

    // ---- Restore

    [Theory]
    [InlineData(AndroidPassphrase)]
    [InlineData("gnoseis   shared  test backup     made on android")] // extra spaces between words
    [InlineData("gnoseis shared test\nbackup made\r\non android")] // line breaks between words
    [InlineData(" \t gnoseis shared test backup made on android \n")] // spaces at both ends
    public async Task Restores_with_the_passphrase_in_any_form_that_normalises_the_same(string passphrase)
    {
        var path = Write(_shared, "shared_android.encrypted.backup");
        var inspection = await BackupRepository.InspectAsync(path, Passphrase(passphrase));
        Assert.True(inspection.IsValid, inspection.Problem);

        await _data.Backup.RestoreAsync(path, Passphrase(passphrase));

        var notes = await _data.Notes.GetNotesAsync();
        Assert.Equal(AndroidTitles.Order(), notes.Where(n => n.OwnerDbId != "yyy").Select(n => n.Title!).Order());
        Assert.Equal(4L, Count("Note")); // the three and Android's seed note
        foreach (var table in new[] { "Contact", "Organization", "Category", "Item", "LinkedRecord" })
        {
            Assert.Equal(0L, Count(table));
        }
    }

    // ---- Refusals

    [Fact]
    public async Task A_wrong_passphrase_is_refused_by_the_apps_check_and_by_decrypting()
    {
        const string wrong = "gnoseis shared test backup made on iphone";
        var path = Write(_shared, "shared_android.encrypted.backup");
        var before = LiveBytes();

        // The app's restore prompt: TryPassphraseAsync gives no key, so the prompt asks again.
        var header = EncryptedBackupFormat.ReadHeader(new MemoryStream(_shared));
        Assert.Null(await BackupEncryption.TryPassphraseAsync(path, header, wrong));

        var problem = await RefusedProblem(path, Passphrase(wrong));
        Assert.Equal(EncryptedBackupProblem.AuthenticationFailed, problem);
        Assert.Equal(before, LiveBytes());
    }

    [Theory]
    [InlineData("KDF salt", 28 + 5, EncryptedBackupProblem.AuthenticationFailed)]
    [InlineData("chunk 0", 92 + 1000, EncryptedBackupProblem.AuthenticationFailed)]
    [InlineData("last chunk's tag", -1, EncryptedBackupProblem.AuthenticationFailed)]
    public async Task A_flipped_byte_is_refused(string where, int offset, EncryptedBackupProblem expected)
    {
        var bytes = (byte[])_shared.Clone();
        var at = offset < 0 ? bytes.Length + offset : offset;
        bytes[at] ^= 0x01;
        _output.WriteLine($"flipped byte in the {where} at {at}");

        await AssertRefused(bytes, expected);
    }

    [Fact]
    public async Task Version_2_is_a_newer_version() =>
        await AssertRefused(With(_shared, b => b[8] = 2), EncryptedBackupProblem.UnknownVersion);

    [Fact]
    public async Task Memory_of_2_GiB_is_refused_before_any_key_is_derived()
    {
        // 2 GiB = 2,097,152 KiB = 0x00200000, big-endian at offset 12
        var bytes = With(_shared, b => new byte[] { 0x00, 0x20, 0x00, 0x00 }.CopyTo(b, 12));
        var watch = Stopwatch.StartNew();

        await AssertRefused(bytes, EncryptedBackupProblem.ParametersOutOfRange);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Cut_at_the_chunk_boundary_is_refused() =>
        await AssertRefused(_shared[..(92 + 65_536 + 16)], EncryptedBackupProblem.Truncated);

    [Fact]
    public async Task Cut_inside_the_last_chunk_is_refused() =>
        await AssertRefused(_shared[..^10], EncryptedBackupProblem.AuthenticationFailed);

    /// <summary>
    /// Android's last chunk is a partial one, so one more byte moves where its tag is read and the chunk fails
    /// its check; TrailingData is only told apart when whole chunks follow the last one.
    /// </summary>
    [Fact]
    public async Task One_extra_byte_at_the_end_is_refused() =>
        await AssertRefused([.. _shared, 0x00], EncryptedBackupProblem.AuthenticationFailed);

    [Fact]
    public async Task An_encrypted_file_named_dot_backup_asks_for_the_passphrase()
    {
        var path = Write(_shared, "shared_android.backup");
        var before = LiveBytes();
        var asked = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _data.Backup.RestoreAsync(path, _ =>
        {
            asked = true;
            return Task.FromResult<BackupMasterKey?>(null); // the user cancels the prompt
        }));

        Assert.True(asked);
        Assert.Equal(before, LiveBytes());
    }

    [Fact]
    public async Task A_file_that_is_neither_kind_is_not_a_backup()
    {
        var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("This is a text file, not a Gnoseis backup. ", 3)))[..100];
        var path = Write(text, "text.encrypted.backup");
        var before = LiveBytes();

        var inspection = await BackupRepository.InspectAsync(path, Passphrase(AndroidPassphrase));
        Assert.False(inspection.IsValid);
        var e = await Assert.ThrowsAsync<DatabaseIncompatibleException>(() => _data.Backup.RestoreAsync(path, Passphrase(AndroidPassphrase)));

        _output.WriteLine($"neither kind: {e.GetType().Name}: {e.Message}");
        Assert.Equal(before, LiveBytes());
    }

    // ---- Helpers

    private static BackupUnlocker Passphrase(string passphrase) => header =>
        Task.FromResult<BackupMasterKey?>(BackupMasterKey.Derive(
            BackupPassphrase.ToKeyBytes(passphrase)!, header.KdfSalt, header.KdfParameters));

    private async Task AssertRefused(byte[] file, EncryptedBackupProblem expected)
    {
        var path = Write(file, "shared_android.encrypted.backup");
        var before = LiveBytes();

        Assert.Equal(expected, await RefusedProblem(path, Passphrase(AndroidPassphrase)));

        Assert.Equal(before, LiveBytes());
    }

    /// <summary>Restores and expects a refusal; returns its kind and logs the message the user sees.</summary>
    private async Task<EncryptedBackupProblem> RefusedProblem(string path, BackupUnlocker unlock)
    {
        var e = await Assert.ThrowsAsync<EncryptedBackupException>(() => _data.Backup.RestoreAsync(path, unlock));
        var inspection = await BackupRepository.InspectAsync(path, unlock);
        Assert.False(inspection.IsValid);
        Assert.Equal(e.Message, inspection.Problem);
        _output.WriteLine($"{e.Problem}: {e.Message}");
        if (e.Problem is EncryptedBackupProblem.AuthenticationFailed or EncryptedBackupProblem.Truncated
            or EncryptedBackupProblem.TrailingData or EncryptedBackupProblem.ParametersOutOfRange)
        {
            Assert.Equal(WrongDamaged, e.Message);
        }
        return e.Problem;
    }

    private static byte[] With(byte[] source, Action<byte[]> change)
    {
        var bytes = (byte[])source.Clone();
        change(bytes);
        return bytes;
    }

    private string Write(byte[] bytes, string name)
    {
        var path = _test.PathFor(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private long Count(string table) => Convert.ToInt64(TestDatabase.ScalarRaw(_test.FilePath, $"SELECT COUNT(*) FROM \"{table}\";"));

    private byte[] LiveBytes()
    {
        // Fold the -wal file in so the comparison covers all data.
        TestDatabase.ExecuteRaw(_test.FilePath, "PRAGMA wal_checkpoint(TRUNCATE);");
        using var stream = new FileStream(_test.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
