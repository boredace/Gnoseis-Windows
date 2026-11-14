/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Backup;
using Gnoseis.Core.Data;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>Encrypted export, and restore and inspection of encrypted backups, through BackupRepository.</summary>
public class EncryptedBackupTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;
    private readonly BackupMasterKey _key =
        BackupMasterKey.FromKey(Enumerable.Repeat((byte)0x11, 32).ToArray(), Enumerable.Repeat((byte)0x22, 32).ToArray(), BackupKdfParameters.Written);

    public EncryptedBackupTests()
    {
        _data = _test.Open();
        Destination = Path.Combine(_test.Folder, "chosen");
        Directory.CreateDirectory(Destination);
    }

    /// <summary>Stands for the folder the user picks in the save dialog (possibly a synced folder).</summary>
    private string Destination { get; }

    private string PrivateTemporaryFolder => _test.PathFor(BackupRepository.PrivateTemporaryFolderName);

    public void Dispose()
    {
        _key.Dispose();
        _test.Dispose();
    }

    /// <summary>A copy of the test key, as the app gives a copy of its stored key.</summary>
    private Task<BackupMasterKey?> StoredKey(EncryptedBackupHeader header) =>
        Task.FromResult<BackupMasterKey?>(BackupMasterKey.FromKey(_key.Key, _key.KdfSalt, _key.Parameters));

    private static BackupUnlocker Passphrase(string passphrase) => header =>
        Task.FromResult<BackupMasterKey?>(BackupMasterKey.Derive(
            BackupPassphrase.ToKeyBytes(passphrase)!, header.KdfSalt, header.KdfParameters));

    private static Task<BackupMasterKey?> Cancelled(EncryptedBackupHeader header) => Task.FromResult<BackupMasterKey?>(null);

    // ---- Export

    [Fact]
    public async Task Export_with_a_key_writes_an_encrypted_backup_that_restores()
    {
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        var path = Path.Combine(Destination, "gnoseis.encrypted.backup");

        await _data.Backup.ExportAsync(path, _key);

        Assert.True(EncryptedBackupFormat.IsEncryptedFile(path));
        var inspection = await BackupRepository.InspectAsync(path, StoredKey);
        Assert.True(inspection.IsValid, inspection.Problem);
        Assert.Equal(1, inspection.ContactCount);
        Assert.Equal([path], Directory.GetFiles(Destination));
        Assert.Empty(Directory.EnumerateFileSystemEntries(PrivateTemporaryFolder));
        Assert.NotNull(TestDatabase.ScalarRaw(_test.FilePath, "SELECT valueInt FROM Settings WHERE settingName = 'lastBackupAt';"));
    }

    [Fact]
    public async Task Export_without_a_key_still_writes_a_plain_backup()
    {
        var path = Path.Combine(Destination, "gnoseis.backup");

        await _data.Backup.ExportAsync(path);

        Assert.False(EncryptedBackupFormat.IsEncryptedFile(path));
        Assert.Equal(DatabaseSchema.RoomIdentityHash, TestDatabase.ScalarRaw(path, "SELECT identity_hash FROM room_master_table;"));
    }

    [Fact]
    public async Task An_encrypted_export_writes_nothing_but_encrypted_bytes_in_the_chosen_folder()
    {
        var path = Path.Combine(Destination, "gnoseis.encrypted.backup");
        var seen = await WatchDestinationDuring(() => _data.Backup.ExportAsync(path, _key));

        Assert.Subset(new HashSet<string> { "gnoseis.encrypted.backup", "gnoseis.encrypted.backup.tmp" }, seen);
        Assert.Contains("gnoseis.encrypted.backup", seen);
    }

    [Fact]
    public async Task A_failed_encrypted_export_leaves_no_file_behind_and_the_live_database_unchanged()
    {
        await _data.Notes.AddNoteAsync(new Note { Title = "Kept" });
        var before = LiveBytes();
        var path = Path.Combine(Destination, "gnoseis.encrypted.backup");
        var disposedKey = BackupMasterKey.FromKey(_key.Key, _key.KdfSalt, _key.Parameters);
        disposedKey.Dispose();

        // Fails after the plain copy is made and the encrypted output file is created.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _data.Backup.ExportAsync(path, disposedKey));

        Assert.Empty(Directory.EnumerateFileSystemEntries(Destination));
        Assert.Empty(Directory.EnumerateFileSystemEntries(PrivateTemporaryFolder));
        Assert.Equal(before, LiveBytes());
    }

    // ---- Restore

    [Fact]
    public async Task Restore_of_an_encrypted_backup_with_the_stored_key_replaces_the_data()
    {
        await _data.Notes.AddNoteAsync(new Note { Title = "In the backup" });
        var path = Path.Combine(Destination, "gnoseis.encrypted.backup");
        await _data.Backup.ExportAsync(path, _key);
        await _data.Notes.AddNoteAsync(new Note { Title = "After the backup" });

        var safetyCopy = await _data.Backup.RestoreAsync(path, StoredKey);

        var titles = (await _data.Notes.GetNotesAsync()).Select(n => n.Title).ToList();
        Assert.Contains("In the backup", titles);
        Assert.DoesNotContain("After the backup", titles);
        Assert.False(EncryptedBackupFormat.IsEncryptedFile(safetyCopy)); // the safety copy stays plain
        Assert.Empty(StagingFolders());
    }

    [Fact]
    public async Task Restore_of_an_encrypted_backup_with_its_passphrase_replaces_the_data()
    {
        using var key = BackupMasterKey.Create(BackupPassphrase.ToKeyBytes("a passphrase for this restore")!);
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Lovelace" });
        var path = Path.Combine(Destination, "gnoseis.encrypted.backup");
        await _data.Backup.ExportAsync(path, key);
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Babbage" });

        await _data.Backup.RestoreAsync(path, Passphrase("  a passphrase for\nthis restore "));

        Assert.Single(await _data.Contacts.GetContactsAsync());
    }

    [Fact]
    public async Task The_shared_Windows_file_restores_through_the_repository()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SharedBackups", "encrypted-v1-windows.backup");

        await _data.Backup.RestoreAsync(path, Passphrase(SharedBackupFileTests.WindowsFilePassphrase));

        Assert.Single(await _data.Contacts.GetContactsAsync());
        Assert.Equal(2, (await _data.Notes.GetNotesAsync()).Count);
    }

    [Fact]
    public async Task Staging_unlocks_once_and_reports_what_the_file_holds()
    {
        await _data.Items.AddItemAsync(new Item { ItemName = "Laptop" });
        var path = Path.Combine(Destination, "gnoseis.encrypted.backup");
        await _data.Backup.ExportAsync(path, _key);
        var calls = 0;

        using (var staged = await BackupRepository.StageAsync(path, header => { calls++; return StoredKey(header); }))
        {
            Assert.True(staged.WasEncrypted);
            Assert.True(_key.Fits(staged.EncryptionHeader!));
            Assert.Equal(1, staged.Inspection.ItemCount);
            await _data.Backup.RestoreAsync(staged);
        }

        Assert.Equal(1, calls);
        Assert.Empty(StagingFolders());
    }

    [Fact]
    public async Task A_plain_backup_is_never_sent_to_the_unlocker()
    {
        var path = Path.Combine(Destination, "gnoseis.backup");
        await _data.Backup.ExportAsync(path);

        using var staged = await BackupRepository.StageAsync(path, _ => throw new InvalidOperationException("asked for a key"));

        Assert.False(staged.WasEncrypted);
        Assert.True(staged.Inspection.IsValid);
    }

    // ---- Refused, with the live database unchanged

    [Fact]
    public async Task Wrong_passphrase_is_refused_and_changes_nothing()
    {
        using var key = BackupMasterKey.Create(BackupPassphrase.ToKeyBytes("the right passphrase")!);
        var path = await EncryptedBackupWith(key);
        var before = LiveBytes();

        var e = await Assert.ThrowsAsync<EncryptedBackupException>(() =>
            _data.Backup.RestoreAsync(path, Passphrase("the wrong passphrase")));

        Assert.Equal("Wrong passphrase, or the file is damaged", e.Message);
        Assert.Equal(before, LiveBytes());
        Assert.Empty(StagingFolders());
        var inspection = await BackupRepository.InspectAsync(path, Passphrase("the wrong passphrase"));
        Assert.Equal("Wrong passphrase, or the file is damaged", inspection.Problem);
    }

    [Theory]
    [InlineData("flip-header")]
    [InlineData("flip-chunk")]
    [InlineData("flip-tag")]
    [InlineData("cut-at-chunk")]
    [InlineData("cut-inside-chunk")]
    [InlineData("extra-data")]
    [InlineData("swap-chunks")]
    [InlineData("memory-out-of-range")]
    public async Task A_damaged_file_is_refused_and_changes_nothing(string damage)
    {
        await AddEnoughDataForThreeChunks();
        var path = await EncryptedBackupWith(_key);
        var bytes = await File.ReadAllBytesAsync(path);
        const int header = EncryptedBackupFormat.HeaderSize, stored = EncryptedBackupFormat.WrittenChunkSize + EncryptedBackupFormat.TagSize;
        Assert.True(bytes.Length > header + 2 * stored);
        switch (damage)
        {
            case "flip-header": bytes[40] ^= 1; break;
            case "flip-chunk": bytes[header + stored + 7] ^= 1; break;
            case "flip-tag": bytes[header + stored - 1] ^= 1; break;
            case "cut-at-chunk": bytes = bytes[..(header + 2 * stored)]; break;
            case "cut-inside-chunk": bytes = bytes[..(header + stored + 100)]; break;
            case "extra-data": bytes = [.. bytes, .. bytes[header..(header + stored)]]; break;
            case "swap-chunks":
                var first = bytes[header..(header + stored)];
                bytes[(header + stored)..(header + 2 * stored)].CopyTo(bytes, header);
                first.CopyTo(bytes, header + stored);
                break;
            case "memory-out-of-range": bytes[12] = 0xFF; break;
        }
        await File.WriteAllBytesAsync(path, bytes);
        var before = LiveBytes();

        var e = await Assert.ThrowsAsync<EncryptedBackupException>(() => _data.Backup.RestoreAsync(path, StoredKey));

        Assert.Equal("Wrong passphrase, or the file is damaged", e.Message);
        Assert.Equal(before, LiveBytes());
        Assert.Empty(StagingFolders());
    }

    [Fact]
    public async Task A_newer_version_is_refused_with_its_own_message_and_changes_nothing()
    {
        var path = await EncryptedBackupWith(_key);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[8] = 2;
        await File.WriteAllBytesAsync(path, bytes);
        var before = LiveBytes();

        var e = await Assert.ThrowsAsync<EncryptedBackupException>(() => _data.Backup.RestoreAsync(path, StoredKey));

        Assert.Equal("Made by a newer version of Gnoseis", e.Message);
        Assert.Equal(before, LiveBytes());
        Assert.Equal("Made by a newer version of Gnoseis", (await BackupRepository.InspectAsync(path, StoredKey)).Problem);
    }

    [Fact]
    public async Task Cancelling_the_passphrase_changes_nothing()
    {
        var path = await EncryptedBackupWith(_key);
        var before = LiveBytes();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _data.Backup.RestoreAsync(path, Cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _data.Backup.RestoreAsync(path));

        Assert.Equal(before, LiveBytes());
        Assert.Empty(StagingFolders());
    }

    // ---- Clean-up

    [Fact]
    public void Start_up_clean_up_deletes_left_over_plain_copies()
    {
        var leftOverExport = Path.Combine(PrivateTemporaryFolder, "0123", DatabaseSchema.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(leftOverExport)!);
        File.WriteAllText(leftOverExport, "plain");
        var leftOverStaging = Path.Combine(Path.GetTempPath(), "Gnoseis", "left-over-test", DatabaseSchema.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(leftOverStaging)!);
        File.WriteAllText(leftOverStaging, "plain");

        BackupRepository.DeleteTemporaryFiles(_test.Folder);

        Assert.False(File.Exists(leftOverExport));
        Assert.False(Directory.Exists(Path.GetDirectoryName(leftOverStaging)));
        Assert.True(File.Exists(_test.FilePath));
    }

    // ---- Helpers

    private async Task<string> EncryptedBackupWith(BackupMasterKey key)
    {
        var path = Path.Combine(Destination, $"{Guid.NewGuid():N}.encrypted.backup");
        await _data.Backup.ExportAsync(path, key);
        return path;
    }

    /// <summary>About 3 chunks of database, so damage can be put in the middle of the file.</summary>
    private async Task AddEnoughDataForThreeChunks()
    {
        var text = new string('x', 2000);
        for (var i = 0; i < 80; i++)
        {
            await _data.Notes.AddNoteAsync(new Note { Title = $"Note {i}", TextContents = text });
        }
    }

    private byte[] LiveBytes()
    {
        // Fold the -wal file in so the comparison covers all data.
        TestDatabase.ExecuteRaw(_test.FilePath, "PRAGMA wal_checkpoint(TRUNCATE);");
        using var stream = new FileStream(_test.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Staging folders still on disk (the test runs alone, so any left over is from this test).</summary>
    private static string[] StagingFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "Gnoseis");
        return Directory.Exists(root) ? Directory.GetDirectories(root) : [];
    }

    /// <summary>The names of every file created in or renamed into the chosen folder while the action runs.</summary>
    private async Task<HashSet<string>> WatchDestinationDuring(Func<Task> action)
    {
        var seen = new HashSet<string>();
        using var watcher = new FileSystemWatcher(Destination) { IncludeSubdirectories = true };
        watcher.Created += (_, e) => { lock (seen) seen.Add(e.Name!); };
        watcher.Renamed += (_, e) => { lock (seen) { seen.Add(e.OldName!); seen.Add(e.Name!); } };
        watcher.Changed += (_, e) => { lock (seen) seen.Add(e.Name!); };
        watcher.EnableRaisingEvents = true;
        try
        {
            await action();
        }
        finally
        {
            await Task.Delay(300); // let the watcher deliver its events
        }
        lock (seen)
        {
            return [.. seen];
        }
    }
}
