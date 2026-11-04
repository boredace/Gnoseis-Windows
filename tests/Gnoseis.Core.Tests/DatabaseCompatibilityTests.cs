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
    private static JsonElement RoomSchema(int version = DatabaseSchema.Version)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RoomSchema", $"{version}.json");
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

        Assert.Equal(3L, TestDatabase.ScalarRaw(test.FilePath, "PRAGMA user_version;"));
        Assert.Equal(DatabaseSchema.RoomIdentityHash, TestDatabase.ScalarRaw(test.FilePath, "SELECT identity_hash FROM room_master_table WHERE id = 42;"));
    }

    [Fact]
    public void New_database_has_an_empty_Settings_table()
    {
        using var test = new TestDatabase();
        test.Open();

        Assert.Equal(0L, TestDatabase.ScalarRaw(test.FilePath, "SELECT count(*) FROM Settings;"));
    }

    [Fact]
    public void New_database_columns_match_the_Room_schema()
    {
        using var test = new TestDatabase();
        test.Open();

        AssertColumnsMatchRoomSchema(test.FilePath);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_version_1_or_2_file_opens_as_version_3_with_empty_times(int version)
    {
        using var test = new TestDatabase();
        CreateAndroidStyleDatabase(test.FilePath, version);
        TestDatabase.ExecuteRaw(test.FilePath,
            "INSERT INTO Organization (id, ownerDbId, organizationName) VALUES ('o1', 'db1', 'Acme');",
            "INSERT INTO Category (id, ownerDbId, categoryName) VALUES ('k1', 'db1', 'Work');",
            "INSERT INTO Item (id, ownerDbId, itemName) VALUES ('i1', 'db1', 'Laptop');",
            "INSERT INTO LinkedRecord (id, ownerDbId, record1Id, record2Id, record1TypeId, record2TypeId) VALUES ('l1', 'db1', 'c1', 'o1', 2, 3);");

        test.Open();

        Assert.Equal(3L, TestDatabase.ScalarRaw(test.FilePath, "PRAGMA user_version;"));
        Assert.Equal(DatabaseSchema.RoomIdentityHash, TestDatabase.ScalarRaw(test.FilePath, "SELECT identity_hash FROM room_master_table WHERE id = 42;"));
        AssertColumnsMatchRoomSchema(test.FilePath);
        foreach (var table in new[] { "Note", "Contact", "Organization", "Category", "Item" })
        {
            Assert.Equal(1L, TestDatabase.ScalarRaw(test.FilePath, $"SELECT count(*) FROM {table};"));
            Assert.Equal(1L, TestDatabase.ScalarRaw(test.FilePath, $"SELECT count(*) FROM {table} WHERE createdAt IS NULL AND updatedAt IS NULL;"));
        }
        Assert.Equal(1L, TestDatabase.ScalarRaw(test.FilePath, "SELECT count(*) FROM LinkedRecord WHERE createdAt IS NULL;"));
        Assert.Equal(0L, TestDatabase.ScalarRaw(test.FilePath, "SELECT count(*) FROM Settings;"));

        // The file as it was before the migration is kept next to it.
        var copy = Assert.Single(Directory.GetFiles(test.Folder, "gnoseis_data.before-v3-*.backup"));
        Assert.Equal((long)version, TestDatabase.ScalarRaw(copy, "PRAGMA user_version;"));
    }

    [Fact]
    public void Refuses_a_version_3_file_without_the_new_columns_and_leaves_it_unchanged()
    {
        using var test = new TestDatabase();
        CreateAndroidStyleDatabase(test.FilePath, version: 2);
        TestDatabase.ExecuteRaw(test.FilePath,
            $"UPDATE room_master_table SET identity_hash = '{DatabaseSchema.RoomIdentityHash}';",
            "PRAGMA user_version = 3;");

        var error = Assert.Throws<DatabaseIncompatibleException>(() => GnoseisDatabase.Open(test.FilePath));

        Assert.Contains("createdAt", error.Message);
        Assert.Equal(3L, TestDatabase.ScalarRaw(test.FilePath, "PRAGMA user_version;"));
        Assert.Empty(Directory.GetFiles(test.Folder, "*.backup"));
    }

    [Fact]
    public void Refuses_a_version_2_file_with_the_version_3_identity_hash()
    {
        using var test = new TestDatabase();
        CreateAndroidStyleDatabase(test.FilePath, version: 2);
        TestDatabase.ExecuteRaw(test.FilePath, $"UPDATE room_master_table SET identity_hash = '{DatabaseSchema.RoomIdentityHash}';");

        Assert.Throws<DatabaseIncompatibleException>(() => GnoseisDatabase.Open(test.FilePath));

        Assert.Equal(2L, TestDatabase.ScalarRaw(test.FilePath, "PRAGMA user_version;"));
    }

    /// <summary>
    /// Every table has the columns of the Room schema export, with the same type, NOT NULL and
    /// primary key (the way Room validates a table; the stored CREATE TABLE text of a migrated
    /// table differs, because ALTER TABLE appends to it).
    /// </summary>
    internal static void AssertColumnsMatchRoomSchema(string path)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        foreach (var entity in RoomSchema().GetProperty("entities").EnumerateArray())
        {
            var table = entity.GetProperty("tableName").GetString()!;
            var primaryKey = entity.GetProperty("primaryKey").GetProperty("columnNames").EnumerateArray().Select(c => c.GetString()!).ToList();
            var expected = entity.GetProperty("fields").EnumerateArray()
                .Select(f => (
                    Name: f.GetProperty("columnName").GetString()!,
                    Type: f.GetProperty("affinity").GetString()!,
                    NotNull: f.GetProperty("notNull").GetBoolean(),
                    PrimaryKeyPosition: primaryKey.IndexOf(f.GetProperty("columnName").GetString()!) + 1))
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ToList();

            var actual = new List<(string Name, string Type, bool NotNull, int PrimaryKeyPosition)>();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(`{table}`);";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    actual.Add((reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0, (int)reader.GetInt64(5)));
                }
            }

            Assert.Equal(expected, actual.OrderBy(c => c.Name, StringComparer.Ordinal).ToList());
        }
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
        TestDatabase.ExecuteRaw(test.FilePath, "PRAGMA user_version = 4;");

        var error = Assert.Throws<DatabaseIncompatibleException>(() => GnoseisDatabase.Open(test.FilePath));

        Assert.Contains("newer version", error.Message);
        Assert.Equal(1L, TestDatabase.ScalarRaw(test.FilePath, "SELECT count(*) FROM Contact;"));
    }

    [Fact]
    public async Task Migrates_a_version_1_file_to_version_3_and_keeps_a_copy()
    {
        using var test = new TestDatabase();
        CreateVersion1DatabaseWithDuplicateLinks(test.FilePath);

        var data = test.Open();

        Assert.Equal(3L, TestDatabase.ScalarRaw(test.FilePath, "PRAGMA user_version;"));
        Assert.Equal(DatabaseSchema.RoomIdentityHash, TestDatabase.ScalarRaw(test.FilePath, "SELECT identity_hash FROM room_master_table WHERE id = 42;"));
        // Duplicates in both directions are removed; the first link of each pair is kept.
        Assert.Equal("l1,l4", TestDatabase.ScalarRaw(test.FilePath, "SELECT group_concat(id, ',') FROM (SELECT id FROM LinkedRecord ORDER BY id);"));
        // The extra index is gone, SQLite's own index for the primary key is not.
        Assert.Equal(0L, TestDatabase.ScalarRaw(test.FilePath, "SELECT count(*) FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL;"));
        Assert.True(Convert.ToInt64(TestDatabase.ScalarRaw(test.FilePath, "SELECT count(*) FROM sqlite_master WHERE type = 'index';")) > 0);
        // The rest of the data is untouched.
        Assert.Equal(3, (await data.Contacts.GetContactsAsync()).Count);
        Assert.Single(await data.Notes.GetNotesAsync());

        // The file as it was before the migration is kept next to it.
        var copy = Assert.Single(Directory.GetFiles(test.Folder, "gnoseis_data.before-v3-*.backup"));
        Assert.Equal(1L, TestDatabase.ScalarRaw(copy, "PRAGMA user_version;"));
        Assert.Equal(4L, TestDatabase.ScalarRaw(copy, "SELECT count(*) FROM LinkedRecord;"));
        Assert.Equal(1L, TestDatabase.ScalarRaw(copy, "SELECT count(*) FROM sqlite_master WHERE name = 'index_extra';"));
    }

    [Fact]
    public void Opening_a_version_3_file_makes_no_copy()
    {
        using var test = new TestDatabase();
        CreateAndroidStyleDatabase(test.FilePath);

        test.Open();

        Assert.Empty(Directory.GetFiles(test.Folder, "*.backup"));
    }

    [Fact]
    public void Refuses_a_version_1_file_with_a_different_identity_hash_and_leaves_it_unchanged()
    {
        using var test = new TestDatabase();
        CreateVersion1DatabaseWithDuplicateLinks(test.FilePath);
        TestDatabase.ExecuteRaw(test.FilePath, "UPDATE room_master_table SET identity_hash = 'something-else';");

        Assert.Throws<DatabaseIncompatibleException>(() => GnoseisDatabase.Open(test.FilePath));

        Assert.Equal(1L, TestDatabase.ScalarRaw(test.FilePath, "PRAGMA user_version;"));
        Assert.Equal(4L, TestDatabase.ScalarRaw(test.FilePath, "SELECT count(*) FROM LinkedRecord;"));
        Assert.Empty(Directory.GetFiles(test.Folder, "*.backup"));
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
        TestDatabase.ExecuteRaw(test.FilePath, "CREATE TABLE Other (x TEXT);", "PRAGMA user_version = 2;");

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

    /// <summary>
    /// A database the way Room creates it on Android: WAL mode, android_metadata table, Room tables.
    /// Schema version 3 by default, as written by Android 0.3.0 and later. The tables come from the
    /// Room schema export of that version (versions 1 and 2 have the same tables).
    /// </summary>
    internal static void CreateAndroidStyleDatabase(string path, int version = DatabaseSchema.Version)
    {
        var statements = new List<string> { "PRAGMA journal_mode=WAL;", "CREATE TABLE android_metadata (locale TEXT);", "INSERT INTO android_metadata VALUES ('en_US');" };
        var schema = RoomSchema(Math.Clamp(version, 2, DatabaseSchema.Version));
        foreach (var entity in schema.GetProperty("entities").EnumerateArray())
        {
            statements.Add(entity.GetProperty("createSql").GetString()!.Replace("${TABLE_NAME}", entity.GetProperty("tableName").GetString()!));
        }
        statements.AddRange(schema.GetProperty("setupQueries").EnumerateArray().Select(q => q.GetString()!));
        statements.Add($"PRAGMA user_version = {version};");
        statements.Add("INSERT INTO Contact (id, ownerDbId, nameLast) VALUES ('c1', 'db1', 'Smith');");
        statements.Add("INSERT INTO Note (id, ownerDbId, title, textContents, createDateTime, createDate) VALUES ('n1', 'db1', 'Android note', 'Text', 1700000000000, 1699920000000);");
        TestDatabase.ExecuteRaw(path, statements.ToArray());
    }

    /// <summary>
    /// A version 1 file as Android 0.2.3 and earlier could leave it: an extra index, and the same link
    /// stored more than once (also in the other direction).
    /// </summary>
    internal static void CreateVersion1DatabaseWithDuplicateLinks(string path)
    {
        CreateAndroidStyleDatabase(path, version: 1);
        TestDatabase.ExecuteRaw(path,
            "INSERT INTO Contact (id, ownerDbId, nameLast) VALUES ('c2', 'db1', 'Jones'), ('c3', 'db1', 'Brown');",
            "INSERT INTO LinkedRecord (id, ownerDbId, record1Id, record2Id, record1TypeId, record2TypeId) VALUES " +
            "('l1', 'db1', 'c1', 'c2', 2, 2), ('l2', 'db1', 'c2', 'c1', 2, 2), ('l3', 'db1', 'c1', 'c2', 2, 2), ('l4', 'db1', 'c1', 'c3', 2, 2);",
            "CREATE INDEX index_extra ON LinkedRecord (record1Id);");
    }
}
