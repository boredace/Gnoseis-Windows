/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>The Settings table keeps lastBackupAt and lastDeletionAt the same way as Android.</summary>
public class SettingsTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public SettingsTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    private static object? LastBackupAtIn(string path) =>
        TestDatabase.ScalarRaw(path, "SELECT valueInt FROM Settings WHERE settingId = 2 AND settingName = 'lastBackupAt';");

    [Fact]
    public async Task A_new_database_has_no_settings_yet()
    {
        Assert.Null(await _data.Settings.GetLastBackupAtAsync());
        Assert.Null(await _data.Settings.GetLastDeletionAtAsync());
    }

    [Fact]
    public async Task Export_writes_lastBackupAt_into_the_backup_and_then_the_live_file()
    {
        var backup = _test.PathFor("backup.backup");
        var before = EpochTime.UtcNowMillis();

        await _data.Backup.ExportAsync(backup);

        var inBackup = Assert.IsType<long>(LastBackupAtIn(backup));
        Assert.InRange(inBackup, before, EpochTime.UtcNowMillis());
        Assert.Equal(inBackup, await _data.Settings.GetLastBackupAtAsync());
    }

    [Fact]
    public async Task A_later_export_replaces_lastBackupAt_and_keeps_the_other_rows()
    {
        TestDatabase.ExecuteRaw(_data.Database.FilePath,
            "INSERT INTO Settings (settingId, settingName, valueInt) VALUES (1, 'appId', 77), (2, 'lastBackupAt', 5);");

        await _data.Backup.ExportAsync(_test.PathFor("backup.backup"));

        Assert.NotEqual(5L, await _data.Settings.GetLastBackupAtAsync());
        Assert.Equal(2L, TestDatabase.ScalarRaw(_data.Database.FilePath, "SELECT count(*) FROM Settings;"));
        Assert.Equal(77L, TestDatabase.ScalarRaw(_data.Database.FilePath, "SELECT valueInt FROM Settings WHERE settingId = 1;"));
    }

    [Fact]
    public async Task A_failed_export_leaves_the_live_file_unchanged()
    {
        var unreachable = Path.Combine(_test.Folder, "missing-folder", "backup.backup");

        await Assert.ThrowsAnyAsync<Exception>(() => _data.Backup.ExportAsync(unreachable));

        Assert.Null(await _data.Settings.GetLastBackupAtAsync());
    }

    [Fact]
    public async Task The_safety_copy_made_by_a_restore_is_not_a_backup()
    {
        var backup = _test.PathFor("backup.backup");
        DatabaseCompatibilityTests.CreateAndroidStyleDatabase(backup);

        var safetyCopy = await _data.Backup.RestoreAsync(backup);

        Assert.Null(LastBackupAtIn(safetyCopy));
        Assert.Null(await _data.Settings.GetLastBackupAtAsync());
    }

    [Fact]
    public async Task Deleting_a_record_writes_lastDeletionAt()
    {
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        var before = EpochTime.UtcNowMillis();

        await _data.DeleteRecord.InvokeAsync(contact.Id, RecordType.Contact);

        Assert.InRange((await _data.Settings.GetLastDeletionAtAsync())!.Value, before, EpochTime.UtcNowMillis());
        Assert.Null(await _data.Contacts.GetContactAsync(contact.Id));
    }

    [Fact]
    public async Task Unlinking_writes_lastDeletionAt_only_when_a_link_was_removed()
    {
        var a = await _data.Contacts.AddContactAsync(new Contact { NameLast = "A" });
        var b = await _data.Contacts.AddContactAsync(new Contact { NameLast = "B" });

        Assert.Equal(0, await _data.LinkedRecords.DeleteLinkAsync(a.Id, b.Id));
        Assert.Null(await _data.Settings.GetLastDeletionAtAsync());

        await _data.LinkedRecords.AddLinkedRecordsAsync(a.Id, RecordType.Contact, [new SearchResult(b.Id, (int)RecordType.Contact, "B")]);
        var before = EpochTime.UtcNowMillis();
        Assert.Equal(1, await _data.LinkedRecords.DeleteLinkAsync(b.Id, a.Id));

        Assert.InRange((await _data.Settings.GetLastDeletionAtAsync())!.Value, before, EpochTime.UtcNowMillis());
    }

    [Fact]
    public async Task Removing_all_links_to_a_record_writes_lastDeletionAt()
    {
        var a = await _data.Contacts.AddContactAsync(new Contact { NameLast = "A" });
        var b = await _data.Contacts.AddContactAsync(new Contact { NameLast = "B" });
        await _data.LinkedRecords.AddLinkedRecordsAsync(a.Id, RecordType.Contact, [new SearchResult(b.Id, (int)RecordType.Contact, "B")]);

        Assert.Equal(1, await _data.LinkedRecords.DeleteLinksToRecordIdAsync(b.Id));

        Assert.NotNull(await _data.Settings.GetLastDeletionAtAsync());
    }
}
