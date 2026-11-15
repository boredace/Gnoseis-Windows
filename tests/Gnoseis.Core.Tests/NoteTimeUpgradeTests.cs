/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Backup;
using Gnoseis.Core.Data;
using Gnoseis.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Schema version 4: opening an older live database or restoring an older backup converts each note's
/// createDateTime (local clock time stored as if UTC) to its UTC instant in this PC's time zone, and
/// recomputes createDate. Nothing else changes. Zones are fixed, never Windows' own.
/// </summary>
public class NoteTimeUpgradeTests : IDisposable
{
    private static readonly TimeZoneInfo Toronto = NoteDatesTests.Zone("America/Toronto");
    private static readonly TimeZoneInfo Tokyo = NoteDatesTests.Zone("Asia/Tokyo");

    /// <summary>The Toronto rows of the conversion examples: v3 createDateTime, v3 createDate, v4 createDateTime, v4 createDate.</summary>
    private static readonly (long V3DateTime, long V3Date, long V4DateTime, long V4Date)[] TorontoRows =
    [
        (1784125800000, 1784073600000, 1784140200000, 1784088000000),
        (1768487400000, 1768435200000, 1768505400000, 1768453200000),
        (1794614400000, 1794614400000, 1794632400000, 1794632400000),
        (1772937000000, 1772928000000, 1772955000000, 1772946000000),
        (1793496600000, 1793491200000, 1793511000000, 1793505600000),
    ];

    private readonly TestDatabase _test = new();

    public void Dispose() => _test.Dispose();

    /// <summary>A version 3 file (as Android 0.3.0 writes it) with the Toronto example notes, and other columns set.</summary>
    private static void CreateVersion3FileWithExampleNotes(string path)
    {
        DatabaseCompatibilityTests.CreateAndroidStyleDatabase(path, version: 3);
        TestDatabase.ExecuteRaw(path, "DELETE FROM Note;");
        TestDatabase.ExecuteRaw(path, TorontoRows.Select((r, i) =>
            "INSERT INTO Note (id, ownerDbId, title, textContents, createDateTime, createDate, createdAt, updatedAt) " +
            $"VALUES ('n{i}', 'db{i}', 'Title {i}', 'Text {i}', {r.V3DateTime}, {r.V3Date}, {1_700_000_000_000 + i}, {1_700_000_100_000 + i});").ToArray());
    }

    private static List<object?[]> Rows(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            reader.GetValues(row!);
            rows.Add(row);
        }
        return rows;
    }

    private static Dictionary<string, (long CreateDate, long CreateDateTime)> NoteTimes(string path) =>
        Rows(path, "SELECT id, createDate, createDateTime FROM Note;")
            .ToDictionary(r => (string)r[0]!, r => ((long)r[1]!, (long)r[2]!));

    private const string OtherNoteColumns = "SELECT id, ownerDbId, title, textContents, createdAt, updatedAt FROM Note ORDER BY id;";

    [Fact]
    public void Opening_a_version_3_file_converts_note_times_in_the_zone_and_nothing_else()
    {
        CreateVersion3FileWithExampleNotes(_test.FilePath);
        var otherColumns = Rows(_test.FilePath, OtherNoteColumns);
        var contactBefore = Rows(_test.FilePath, "SELECT * FROM Contact;");

        _test.Open(Toronto);

        Assert.Equal(4L, TestDatabase.ScalarRaw(_test.FilePath, "PRAGMA user_version;"));
        Assert.Equal(DatabaseSchema.RoomIdentityHash, TestDatabase.ScalarRaw(_test.FilePath, "SELECT identity_hash FROM room_master_table WHERE id = 42;"));
        var times = NoteTimes(_test.FilePath);
        for (var i = 0; i < TorontoRows.Length; i++)
        {
            Assert.Equal((TorontoRows[i].V4Date, TorontoRows[i].V4DateTime), times[$"n{i}"]);
        }
        Assert.Equal(otherColumns, Rows(_test.FilePath, OtherNoteColumns));
        Assert.Equal(contactBefore, Rows(_test.FilePath, "SELECT * FROM Contact;"));
        DatabaseCompatibilityTests.AssertColumnsMatchRoomSchema(_test.FilePath);
    }

    [Fact]
    public void The_before_v4_copy_holds_the_file_as_it_was()
    {
        CreateVersion3FileWithExampleNotes(_test.FilePath);
        TestDatabase.ExecuteRaw(_test.FilePath, "PRAGMA journal_mode=DELETE;");
        var before = Rows(_test.FilePath, "SELECT * FROM Note ORDER BY id;");
        var original = File.ReadAllBytes(_test.FilePath);

        _test.Open(Toronto);

        var copy = Assert.Single(Directory.GetFiles(_test.Folder, "gnoseis_data.before-v4-*.backup"));
        // SQLite's online backup copies every page but rewrites header counters, so the copy is compared
        // table by table, every row and column.
        var originalPath = _test.PathFor("original.db");
        File.WriteAllBytes(originalPath, original);
        var tables = Rows(originalPath, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;").Select(r => (string)r[0]!).ToList();
        Assert.Equal(tables, Rows(copy, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;").Select(r => (string)r[0]!));
        foreach (var table in tables)
        {
            var sql = $"SELECT * FROM \"{table}\" ORDER BY rowid;";
            Assert.Equal(Rows(originalPath, sql), Rows(copy, sql));
        }
        Assert.Equal(3L, TestDatabase.ScalarRaw(copy, "PRAGMA user_version;"));
        Assert.Equal(before, Rows(copy, "SELECT * FROM Note ORDER BY id;"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_version_1_or_2_file_reaches_version_4_with_converted_note_times(int version)
    {
        DatabaseCompatibilityTests.CreateAndroidStyleDatabase(_test.FilePath, version);
        var old = (long)TestDatabase.ScalarRaw(_test.FilePath, "SELECT createDateTime FROM Note;")!;

        _test.Open(Tokyo);

        Assert.Equal(4L, TestDatabase.ScalarRaw(_test.FilePath, "PRAGMA user_version;"));
        Assert.Equal(NoteDates.UpgradeFromV3(old, Tokyo), NoteTimes(_test.FilePath)["n1"]);
    }

    [Fact]
    public void Opening_a_version_4_file_changes_no_note()
    {
        DatabaseCompatibilityTests.CreateAndroidStyleDatabase(_test.FilePath);
        var before = NoteTimes(_test.FilePath);

        _test.Open(Tokyo);

        Assert.Equal(before, NoteTimes(_test.FilePath));
    }

    [Fact]
    public async Task Restoring_a_version_3_backup_converts_its_note_times_in_the_zone()
    {
        var data = _test.Open(Toronto);
        var backup = _test.PathFor("v3.backup");
        CreateVersion3FileWithExampleNotes(backup);
        var otherColumns = Rows(backup, OtherNoteColumns);

        await data.Backup.RestoreAsync(backup);

        var times = NoteTimes(_test.FilePath);
        for (var i = 0; i < TorontoRows.Length; i++)
        {
            Assert.Equal((TorontoRows[i].V4Date, TorontoRows[i].V4DateTime), times[$"n{i}"]);
        }
        Assert.Equal(otherColumns, Rows(_test.FilePath, OtherNoteColumns));
        Assert.Equal(4L, TestDatabase.ScalarRaw(_test.FilePath, "PRAGMA user_version;"));
        // The chosen file isn't changed.
        Assert.Equal(3L, TestDatabase.ScalarRaw(backup, "PRAGMA user_version;"));
        Assert.Equal(TorontoRows[0].V3DateTime, TestDatabase.ScalarRaw(backup, "SELECT createDateTime FROM Note WHERE id = 'n0';"));
    }

    [Fact]
    public async Task Restoring_a_version_4_backup_keeps_its_note_times()
    {
        var data = _test.Open(Tokyo);
        var backup = _test.PathFor("v4.backup");
        DatabaseCompatibilityTests.CreateAndroidStyleDatabase(backup);
        var before = NoteTimes(backup);

        await data.Backup.RestoreAsync(backup);

        Assert.Equal(before, NoteTimes(_test.FilePath));
    }

    [Theory]
    [InlineData("plain-v3-android.backup", null)]
    [InlineData("plain-v3-windows.backup", null)]
    [InlineData("encrypted-v1-android.backup", "gnoseis shared test backup made on android")]
    [InlineData("encrypted-v1-windows.backup", SharedBackupFileTests.WindowsFilePassphrase)]
    public async Task The_shared_backups_restore_and_convert_in_the_zone(string name, string? passphrase)
    {
        var data = _test.Open(Tokyo);
        var path = _test.PathFor(name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SharedBackups", name), path);
        var plain = path;
        if (passphrase != null)
        {
            plain = _test.PathFor("decrypted.backup");
            using var input = File.OpenRead(path);
            using var output = File.Create(plain);
            EncryptedBackupFormat.Decrypt(input, EncryptedBackupFormatTests.Passphrase(passphrase), output);
        }
        Assert.Equal(3L, TestDatabase.ScalarRaw(plain, "PRAGMA user_version;"));
        var old = NoteTimes(plain);
        var otherColumns = Rows(plain, OtherNoteColumns);

        await data.Backup.RestoreAsync(path, passphrase == null ? null : Unlock(passphrase));

        var restored = NoteTimes(_test.FilePath);
        Assert.Equal(old.Keys.Order(), restored.Keys.Order());
        foreach (var (id, (_, oldDateTime)) in old)
        {
            Assert.Equal(NoteDates.UpgradeFromV3(oldDateTime, Tokyo), restored[id]);
        }
        Assert.Equal(otherColumns, Rows(_test.FilePath, OtherNoteColumns));
    }

    private static BackupUnlocker Unlock(string passphrase) => header =>
        Task.FromResult<BackupMasterKey?>(BackupMasterKey.Derive(
            BackupPassphrase.ToKeyBytes(passphrase)!, header.KdfSalt, header.KdfParameters));
}
