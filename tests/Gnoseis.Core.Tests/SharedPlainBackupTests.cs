/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Security.Cryptography;
using System.Text;
using Gnoseis.Core.Backup;
using Gnoseis.Core.Data;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Plain backups shared byte for byte with Gnoseis-Android: plain-v3-windows.backup, made by this client's plain
/// export, and plain-v3-android.backup, made by Gnoseis-Android's. Each holds every record type, two links, a
/// title with non-ASCII text and a note with a line break. Both restore here through BackupRepository.
/// </summary>
public class SharedPlainBackupTests : IDisposable
{
    private const string AndroidFile = "plain-v3-android.backup";
    private const string AndroidSha256 = "07c6058ba09088fe016e095e955ac9d21b220799218fbc9a34f0706ecadcf41d";
    private const string WindowsFile = "plain-v3-windows.backup";
    private const string WindowsSha256 = "39d38a759f2b23c2178136e4c0ed47dd6b6674245853bef970257cc49c5a0ff4";
    private const string SeedTitle = "This is a default note";
    private const string AccentedTitle = "Zürich café ✓ 東京";
    private const string TwoLines = "First line\nSecond line";

    /// <summary>The same in both files: the seed note and two more, one of each other type, and two links.</summary>
    private static readonly Dictionary<string, long> ExpectedCounts = new()
    {
        ["Note"] = 3, ["Contact"] = 1, ["Organization"] = 1, ["Category"] = 1, ["Item"] = 1, ["LinkedRecord"] = 2,
    };

    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public SharedPlainBackupTests()
    {
        _data = _test.Open();
        _data.Notes.AddNoteAsync(new Note { Title = "Made on this PC" }).GetAwaiter().GetResult(); // replaced by the restore
    }

    public void Dispose() => _test.Dispose();

    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SharedBackups", name));

    // ---- The files

    [Theory]
    [InlineData(AndroidFile, AndroidSha256)]
    [InlineData(WindowsFile, WindowsSha256)]
    public void Each_file_is_unchanged_and_plain(string name, string sha256)
    {
        var bytes = Read(name);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal("SQLite format 3\0"u8.ToArray(), bytes[..16]);
        Assert.False(EncryptedBackupFormat.IsEncrypted(bytes));
    }

    // ---- Restore

    [Theory]
    [InlineData(AndroidFile, "Hopper|Grace", "Android Test Organization", "Android")]
    [InlineData(WindowsFile, "Lovelace|Ada", "Windows Test Organization", "Windows")]
    public async Task Each_file_restores_with_every_record_link_and_text(string name, string contact, string organization, string client)
    {
        var path = Write(Read(name), "shared_plain.backup");
        var inspection = await BackupRepository.InspectAsync(path);
        Assert.True(inspection.IsValid, inspection.Problem);

        await _data.Backup.RestoreAsync(path, NeverAsked);

        Assert.Equal(ExpectedCounts, Counts());
        // The two texts, byte for byte
        var notes = await _data.Notes.GetNotesAsync();
        var accented = notes.Single(n => n.Title?.StartsWith('Z') == true);
        Assert.Equal(Utf8Hex(AccentedTitle), Utf8Hex(accented.Title!));
        Assert.Equal($"Made by Gnoseis for {client}", accented.TextContents);
        Assert.Equal(Utf8Hex(TwoLines), Utf8Hex(notes.Single(n => n.Title == "Two lines").TextContents!));
        Assert.Equal(new[] { SeedTitle, AccentedTitle, "Two lines" }.Order(), notes.Select(n => n.Title!).Order());
        // The other records and both links, by the records they join
        Assert.Equal(contact, Scalar("SELECT nameLast || '|' || nameFirst FROM Contact;"));
        Assert.Equal(organization, Scalar("SELECT organizationName FROM Organization;"));
        Assert.Equal($"{client} test category", Scalar("SELECT categoryName FROM Category;"));
        Assert.Equal($"{client} test item", Scalar("SELECT itemName FROM Item;"));
        Assert.Equal(
            $"1:{AccentedTitle} -> 2:{contact}\n2:{contact} -> 3:{organization}",
            Scalar("SELECT group_concat(link, char(10)) FROM (" +
                "SELECT l.record1TypeId || ':' || COALESCE(n1.title, c1.nameLast || '|' || c1.nameFirst) || ' -> ' || " +
                "l.record2TypeId || ':' || COALESCE(c2.nameLast || '|' || c2.nameFirst, o2.organizationName) AS link " +
                "FROM LinkedRecord l " +
                "LEFT JOIN Note n1 ON n1.id = l.record1Id LEFT JOIN Contact c1 ON c1.id = l.record1Id " +
                "LEFT JOIN Contact c2 ON c2.id = l.record2Id LEFT JOIN Organization o2 ON o2.id = l.record2Id " +
                "ORDER BY l.record1TypeId);"));
    }

    /// <summary>Restore decides by the first 8 bytes, never by the name: a plain file named .encrypted.backup is plain.</summary>
    [Fact]
    public async Task Androids_file_named_encrypted_restores_as_plain()
    {
        var path = Write(Read(AndroidFile), "shared_plain.encrypted.backup");

        await _data.Backup.RestoreAsync(path, NeverAsked);

        Assert.Equal(ExpectedCounts, Counts());
        Assert.Equal("Hopper|Grace", Scalar("SELECT nameLast || '|' || nameFirst FROM Contact;"));
    }

    // ---- Helpers

    private static Task<BackupMasterKey?> NeverAsked(EncryptedBackupHeader header) =>
        throw new InvalidOperationException("A plain backup must not ask for a passphrase.");

    private string Write(byte[] bytes, string name)
    {
        var path = _test.PathFor(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private Dictionary<string, long> Counts() =>
        ExpectedCounts.Keys.ToDictionary(table => table, table => Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM \"{table}\";")));

    private object? Scalar(string sql) => TestDatabase.ScalarRaw(_test.FilePath, sql);

    private static string Utf8Hex(string text) => Convert.ToHexStringLower(Encoding.UTF8.GetBytes(text));
}
