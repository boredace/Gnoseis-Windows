/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Text.Json;
using Gnoseis.Core.Data;

namespace Gnoseis.Core.Tests;

/// <summary>The database file must stay interchangeable with the Android (Room) database.</summary>
public class DatabaseCompatibilityTests
{
    private static JsonElement RoomSchema()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RoomSchema", "1.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("database");
    }

    [Fact]
    public void Version_and_identity_hash_match_the_Room_schema_export()
    {
        var schema = RoomSchema();
        Assert.Equal(DatabaseSchema.Version, schema.GetProperty("version").GetInt32());
        Assert.Equal(DatabaseSchema.RoomIdentityHash, schema.GetProperty("identityHash").GetString());
    }

    [Fact]
    public void New_database_has_exactly_the_Room_tables()
    {
        using var test = new TestDatabase();
        test.Open();

        foreach (var entity in RoomSchema().GetProperty("entities").EnumerateArray())
        {
            var table = entity.GetProperty("tableName").GetString()!;
            // SQLite stores CREATE TABLE statements without "IF NOT EXISTS".
            var expected = entity.GetProperty("createSql").GetString()!
                .Replace("${TABLE_NAME}", table)
                .Replace("CREATE TABLE IF NOT EXISTS", "CREATE TABLE");
            var actual = TestDatabase.ScalarRaw(test.FilePath, $"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{table}';");
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void New_database_has_Room_version_and_identity_hash()
    {
        using var test = new TestDatabase();
        test.Open();

        Assert.Equal(1L, TestDatabase.ScalarRaw(test.FilePath, "PRAGMA user_version;"));
        Assert.Equal(DatabaseSchema.RoomIdentityHash, TestDatabase.ScalarRaw(test.FilePath, "SELECT identity_hash FROM room_master_table WHERE id = 42;"));
    }

    [Fact]
    public async Task New_database_contains_the_welcome_note_immediately()
    {
        // Android known bug 7: the seed note was inserted by a racing background job.
        using var test = new TestDatabase();
        var data = test.Open();

        var notes = await data.Notes.GetNotesAsync();

        var note = Assert.Single(notes);
        Assert.Equal("This is a default note", note.Title);
    }

    [Fact]
    public async Task Opens_a_file_created_by_Android()
    {
        using var test = new TestDatabase();
        CreateAndroidStyleDatabase(test.FilePath);

        var data = test.Open();

        var contact = Assert.Single(await data.Contacts.GetContactsAsync());
        Assert.Equal("Smith", contact.NameLast);
        Assert.Null(contact.NameFirst);
        Assert.Single(await data.Notes.GetNotesAsync());
    }

    [Fact]
    public void Leaves_the_file_as_a_single_self_contained_file()
    {
        using var test = new TestDatabase();
        CreateAndroidStyleDatabase(test.FilePath);

        test.Open();

        Assert.Equal("delete", TestDatabase.ScalarRaw(test.FilePath, "PRAGMA journal_mode;"));
    }

    [Fact]
    public void Refuses_a_newer_schema_version_and_keeps_the_data()
    {
        // Android known bug 5: a version mismatch silently deleted all data.
        using var test = new TestDatabase();
        CreateAndroidStyleDatabase(test.FilePath);
        TestDatabase.ExecuteRaw(test.FilePath, "PRAGMA user_version = 2;");

        var error = Assert.Throws<DatabaseIncompatibleException>(() => GnoseisDatabase.Open(test.FilePath));

        Assert.Contains("newer version", error.Message);
        Assert.Equal(1L, TestDatabase.ScalarRaw(test.FilePath, "SELECT count(*) FROM Contact;"));
    }

    [Fact]
    public void Refuses_a_different_identity_hash()
    {
        using var test = new TestDatabase();
        CreateAndroidStyleDatabase(test.FilePath);
        TestDatabase.ExecuteRaw(test.FilePath, "UPDATE room_master_table SET identity_hash = 'something-else';");

        Assert.Throws<DatabaseIncompatibleException>(() => GnoseisDatabase.Open(test.FilePath));
    }

    [Fact]
    public void Refuses_an_unrelated_sqlite_database()
    {
        using var test = new TestDatabase();
        TestDatabase.ExecuteRaw(test.FilePath, "CREATE TABLE Other (x TEXT);", "PRAGMA user_version = 1;");

        Assert.Throws<DatabaseIncompatibleException>(() => GnoseisDatabase.Open(test.FilePath));
    }

    [Fact]
    public void SetAside_renames_the_file_without_deleting_it()
    {
        using var test = new TestDatabase();
        TestDatabase.ExecuteRaw(test.FilePath, "CREATE TABLE Other (x TEXT);");

        var newName = GnoseisDatabase.SetAside(test.FilePath);

        Assert.False(File.Exists(test.FilePath));
        Assert.True(File.Exists(newName));
    }

    /// <summary>A database the way Room creates it on Android: WAL mode, android_metadata table, Room tables.</summary>
    internal static void CreateAndroidStyleDatabase(string path)
    {
        var statements = new List<string> { "PRAGMA journal_mode=WAL;", "CREATE TABLE android_metadata (locale TEXT);", "INSERT INTO android_metadata VALUES ('en_US');" };
        statements.AddRange(DatabaseSchema.CreateStatements);
        statements.Add("PRAGMA user_version = 1;");
        statements.Add("INSERT INTO Contact (id, ownerDbId, nameLast) VALUES ('c1', 'db1', 'Smith');");
        statements.Add("INSERT INTO Note (id, ownerDbId, title, textContents, createDateTime, createDate) VALUES ('n1', 'db1', 'Android note', 'Text', 1700000000000, 1699920000000);");
        TestDatabase.ExecuteRaw(path, statements.ToArray());
    }
}
