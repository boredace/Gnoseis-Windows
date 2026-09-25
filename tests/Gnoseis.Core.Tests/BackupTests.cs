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

public class BackupTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public BackupTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Saving_still_works_after_a_backup()
    {
        // Android known bug 1: export closed the database the app kept using.
        await _data.Notes.AddNoteAsync(new Note { Title = "A" });

        await _data.Backup.ExportAsync(_test.PathFor("backup.backup"));
        await _data.Notes.AddNoteAsync(new Note { Title = "B" });

        Assert.Equal(3, (await _data.Notes.GetNotesAsync()).Count);
    }

    [Fact]
    public async Task A_backup_is_a_complete_database_that_Android_can_open()
    {
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        var path = _test.PathFor("backup.backup");

        await _data.Backup.ExportAsync(path);

        Assert.False(File.Exists(path + "-wal"));
        Assert.Equal("delete", TestDatabase.ScalarRaw(path, "PRAGMA journal_mode;"));
        Assert.Equal(1L, TestDatabase.ScalarRaw(path, "SELECT count(*) FROM Contact;"));
        Assert.Equal(DatabaseSchema.RoomIdentityHash, TestDatabase.ScalarRaw(path, "SELECT identity_hash FROM room_master_table;"));
    }

    [Fact]
    public async Task Restore_replaces_the_data_and_the_app_keeps_working()
    {
        // Android known bug 2: after import the next save failed and screens showed old data.
        await _data.Notes.AddNoteAsync(new Note { Title = "A" });
        var backup = _test.PathFor("backup.backup");
        await _data.Backup.ExportAsync(backup);
        await _data.Notes.AddNoteAsync(new Note { Title = "B" });
        var replaced = false;
        _data.Database.DataChanged += (_, e) => replaced |= e.DatabaseReplaced;

        var safetyCopy = await _data.Backup.RestoreAsync(backup);
        await _data.Notes.AddNoteAsync(new Note { Title = "C" });

        var titles = (await _data.Notes.GetNotesAsync()).Select(n => n.Title).ToList();
        Assert.Contains("A", titles);
        Assert.Contains("C", titles);
        Assert.DoesNotContain("B", titles);
        Assert.True(replaced);
        Assert.True(File.Exists(safetyCopy));
        Assert.Equal(1L, TestDatabase.ScalarRaw(safetyCopy, "SELECT count(*) FROM Note WHERE title = 'B';"));
    }

    [Fact]
    public async Task Restore_accepts_a_database_file_from_Android()
    {
        var androidFile = _test.PathFor("gnoseis_data_from_phone");
        DatabaseCompatibilityTests.CreateAndroidStyleDatabase(androidFile);

        await _data.Backup.RestoreAsync(androidFile);

        Assert.Equal("Smith", Assert.Single(await _data.Contacts.GetContactsAsync()).NameLast);
    }

    [Fact]
    public async Task Restore_rejects_a_file_that_is_not_a_Gnoseis_database_and_keeps_the_data()
    {
        // Android known bug 3: any file was accepted and overwrote the user's data.
        await _data.Notes.AddNoteAsync(new Note { Title = "Keep me" });
        var textFile = _test.PathFor("notes.txt");
        await File.WriteAllTextAsync(textFile, "This is not a database");
        var otherDatabase = _test.PathFor("other.db");
        TestDatabase.ExecuteRaw(otherDatabase, "CREATE TABLE Something (x TEXT);");

        var textInspection = await BackupRepository.InspectAsync(textFile);
        await Assert.ThrowsAsync<DatabaseIncompatibleException>(() => _data.Backup.RestoreAsync(textFile));
        await Assert.ThrowsAsync<DatabaseIncompatibleException>(() => _data.Backup.RestoreAsync(otherDatabase));

        Assert.False(textInspection.IsValid);
        Assert.Contains((await _data.Notes.GetNotesAsync()), n => n.Title == "Keep me");
    }

    [Fact]
    public async Task Restore_of_a_missing_file_fails_cleanly_and_keeps_the_data()
    {
        // Android known bug 4: an unreadable file crashed the app.
        await _data.Notes.AddNoteAsync(new Note { Title = "Keep me" });

        var inspection = await BackupRepository.InspectAsync(_test.PathFor("missing"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => _data.Backup.RestoreAsync(_test.PathFor("missing")));

        Assert.False(inspection.IsValid);
        Assert.Contains((await _data.Notes.GetNotesAsync()), n => n.Title == "Keep me");
    }

    [Fact]
    public async Task Inspect_counts_the_records_in_a_file()
    {
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        await _data.Items.AddItemAsync(new Item { ItemName = "Laptop" });
        var backup = _test.PathFor("backup.backup");
        await _data.Backup.ExportAsync(backup);

        var inspection = await BackupRepository.InspectAsync(backup);

        Assert.True(inspection.IsValid);
        Assert.Equal((1, 1, 0, 0, 1),
            (inspection.NoteCount, inspection.ContactCount, inspection.OrganizationCount, inspection.CategoryCount, inspection.ItemCount));
    }
}
