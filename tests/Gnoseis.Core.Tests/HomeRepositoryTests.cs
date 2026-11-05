/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Data;
using Gnoseis.Core.Models;
using Microsoft.Data.Sqlite;

namespace Gnoseis.Core.Tests;

public class HomeRepositoryTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;
    private int _linkNumber;

    public HomeRepositoryTests()
    {
        _data = _test.Open();
        // A new database starts with a welcome note; these tests start empty.
        Raw("DELETE FROM Note;", "DELETE FROM LinkedRecord;");
    }

    public void Dispose() => _test.Dispose();

    // ---------- Helpers: rows written directly, so duplicate and self links can be stored

    private void Raw(params string[] sql) => TestDatabase.ExecuteRaw(_test.FilePath, sql);

    private void Contact(string id, string last, string? first = null) =>
        Raw($"INSERT INTO Contact (id, ownerDbId, nameLast, nameFirst) VALUES ('{id}', 'db1', '{last}', {(first == null ? "NULL" : $"'{first}'")});");

    private void Organization(string id, string name) =>
        Raw($"INSERT INTO Organization (id, ownerDbId, organizationName) VALUES ('{id}', 'db1', '{name}');");

    private void Category(string id, string name, string? parentId = null) =>
        Raw($"INSERT INTO Category (id, ownerDbId, parentId, categoryName) VALUES ('{id}', 'db1', {(parentId == null ? "NULL" : $"'{parentId}'")}, '{name}');");

    private void Item(string id, string name) =>
        Raw($"INSERT INTO Item (id, ownerDbId, itemName) VALUES ('{id}', 'db1', '{name}');");

    private void Note(string id, DateOnly day, int minute = 0, string? title = null, string? text = null) =>
        Raw($"INSERT INTO Note (id, ownerDbId, title, textContents, createDate, createDateTime) VALUES ('{id}', 'db1', " +
            $"{(title == null ? "NULL" : $"'{title}'")}, {(text == null ? "NULL" : $"'{text}'")}, " +
            $"{EpochTime.UtcMidnight(day)}, {EpochTime.UtcMidnight(day) + minute * 60_000L});");

    private void Link(string id1, RecordType type1, string id2, RecordType type2, long? createdAt = null) =>
        Raw($"INSERT INTO LinkedRecord (id, ownerDbId, record1Id, record2Id, record1TypeId, record2TypeId, createdAt) VALUES " +
            $"('link{++_linkNumber}', 'db1', '{id1}', '{id2}', {(int)type1}, {(int)type2}, {(createdAt?.ToString(CultureInfo.InvariantCulture) ?? "NULL")});");

    private static readonly DateOnly Today = new(2026, 11, 4);

    // ---------- Counts

    [Fact]
    public async Task Counts_give_each_type_and_the_links()
    {
        Contact("c1", "Smith");
        Contact("c2", "Jones");
        Organization("o1", "Acme");
        Note("n1", Today);
        Link("n1", RecordType.Note, "c1", RecordType.Contact);
        Link("c1", RecordType.Contact, "o1", RecordType.Organization);
        Link("c2", RecordType.Contact, "o1", RecordType.Organization);

        var counts = await _data.Home.GetCountsAsync();

        Assert.Equal(1, counts.Of(RecordType.Note));
        Assert.Equal(2, counts.Of(RecordType.Contact));
        Assert.Equal(1, counts.Of(RecordType.Organization));
        Assert.Equal(0, counts.Of(RecordType.Category));
        Assert.Equal(0, counts.Of(RecordType.Item));
        Assert.Equal(3, counts.Links);
    }

    // ---------- Most linked

    [Fact]
    public async Task Most_linked_counts_both_ends_and_links_from_notes_and_never_lists_notes()
    {
        Contact("c1", "Smith", "Ann");
        Organization("o1", "Acme");
        Note("n1", Today);
        Note("n2", Today);
        Link("n1", RecordType.Note, "c1", RecordType.Contact);
        Link("c1", RecordType.Contact, "n2", RecordType.Note);
        Link("o1", RecordType.Organization, "c1", RecordType.Contact);

        var top = await _data.Home.GetMostLinkedAsync();

        Assert.Equal(
            [new MostLinkedRecord("c1", RecordType.Contact, "Ann Smith", 3), new MostLinkedRecord("o1", RecordType.Organization, "Acme", 1)],
            top);
    }

    [Fact]
    public async Task Most_linked_ties_go_by_name_ignoring_case()
    {
        Item("i1", "zebra");
        Item("i2", "Apple");
        Category("k1", "banana");
        Note("n1", Today);
        Link("n1", RecordType.Note, "i1", RecordType.Item);
        Link("n1", RecordType.Note, "i2", RecordType.Item);
        Link("n1", RecordType.Note, "k1", RecordType.Category);

        var top = await _data.Home.GetMostLinkedAsync();

        Assert.Equal(["i2", "k1", "i1"], top.Select(r => r.Id));
    }

    [Fact]
    public async Task Most_linked_keeps_the_top_ten_and_filters_by_type()
    {
        Note("n1", Today);
        for (var i = 0; i < 12; i++)
        {
            Item($"i{i:00}", $"Item {i:00}");
            for (var j = 0; j <= i; j++)
            {
                Link("n1", RecordType.Note, $"i{i:00}", RecordType.Item);
            }
        }
        Category("k1", "Big");
        for (var j = 0; j < 20; j++)
        {
            Link("k1", RecordType.Category, $"i00", RecordType.Item);
        }

        var all = await _data.Home.GetMostLinkedAsync();
        var items = await _data.Home.GetMostLinkedAsync(RecordType.Item);
        var contacts = await _data.Home.GetMostLinkedAsync(RecordType.Contact);

        Assert.Equal(10, all.Count);
        Assert.Equal(new MostLinkedRecord("i00", RecordType.Item, "Item 00", 21), all[0]);
        Assert.Equal(new MostLinkedRecord("k1", RecordType.Category, "Big", 20), all[1]);
        Assert.Equal(10, items.Count);
        Assert.All(items, r => Assert.Equal(RecordType.Item, r.Type));
        Assert.Equal(["i00", "i11", "i10", "i09"], items.Take(4).Select(r => r.Id));
        Assert.Empty(contacts);
    }

    [Fact]
    public async Task Most_linked_matches_the_android_query()
    {
        // Links from both sides, a record linked twice to the same record (pre-v2 duplicates) and a
        // record linked to itself.
        Contact("c1", "Smith", "Ann");
        Contact("c2", "Jones");
        Organization("o1", "Acme");
        Category("k1", "Home");
        Item("i1", "Drill");
        Note("n1", Today);
        Link("c1", RecordType.Contact, "o1", RecordType.Organization);
        Link("o1", RecordType.Organization, "c1", RecordType.Contact);
        Link("c1", RecordType.Contact, "o1", RecordType.Organization);
        Link("k1", RecordType.Category, "k1", RecordType.Category);
        Link("n1", RecordType.Note, "i1", RecordType.Item);
        Link("i1", RecordType.Item, "k1", RecordType.Category);
        Link("c2", RecordType.Contact, "c1", RecordType.Contact);
        Link("n1", RecordType.Note, "c2", RecordType.Contact);

        foreach (var type in new RecordType?[] { null, RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item })
        {
            var ours = await _data.Home.GetMostLinkedAsync(type);
            var android = AndroidMostLinked(type, 10);
            Assert.Equal(android, ours);
        }
    }

    /// <summary>Android's HomeDao.getMostLinked, as written there.</summary>
    private List<MostLinkedRecord> AndroidMostLinked(RecordType? type, int limit)
    {
        using var connection = new SqliteConnection($"Data Source={_test.FilePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT r.id, r.name, r.recordTypeId, COUNT(*) AS linkCount FROM (" +
            "SELECT id, TRIM(COALESCE(nameFirst, '') || ' ' || COALESCE(nameLast, '')) AS name, 2 AS recordTypeId FROM Contact " +
            "UNION ALL SELECT id, COALESCE(organizationName, ''), 3 FROM Organization " +
            "UNION ALL SELECT id, COALESCE(categoryName, ''), 4 FROM Category " +
            "UNION ALL SELECT id, COALESCE(itemName, ''), 5 FROM Item" +
            ") AS r JOIN LinkedRecord AS l ON l.record1Id = r.id OR l.record2Id = r.id " +
            "WHERE $type IS NULL OR r.recordTypeId = $type " +
            "GROUP BY r.id ORDER BY linkCount DESC, r.name COLLATE NOCASE, r.id LIMIT $limit";
        command.Parameters.AddWithValue("$type", type is { } t ? (int)t : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<MostLinkedRecord>();
        while (reader.Read())
        {
            rows.Add(new MostLinkedRecord(reader.GetString(0), (RecordType)reader.GetInt32(2), reader.GetString(1), reader.GetInt32(3)));
        }
        return rows;
    }

    // ---------- Links among the shown records, links per type

    [Fact]
    public async Task Links_among_gives_the_links_with_both_ends_shown()
    {
        Link("a", RecordType.Contact, "b", RecordType.Organization);
        Link("c", RecordType.Item, "a", RecordType.Contact);
        Link("a", RecordType.Contact, "x", RecordType.Item);
        Link("n", RecordType.Note, "b", RecordType.Organization);

        var links = await _data.Home.GetLinksAmongAsync(["a", "b", "c"]);

        Assert.Equal([new LinkEnds("a", "b"), new LinkEnds("c", "a")], links.OrderBy(l => l.Record1Id));
        Assert.Empty(await _data.Home.GetLinksAmongAsync(["a"]));
    }

    [Fact]
    public async Task Links_per_type_count_a_same_type_link_once_and_leave_out_notes_and_types_without_links()
    {
        Link("c1", RecordType.Contact, "c2", RecordType.Contact);
        Link("n1", RecordType.Note, "c1", RecordType.Contact);
        Link("c1", RecordType.Contact, "i1", RecordType.Item);
        Link("n1", RecordType.Note, "n2", RecordType.Note);

        var perType = await _data.Home.GetLinksPerTypeAsync();

        Assert.Equal([new TypeLinkCount(RecordType.Contact, 3), new TypeLinkCount(RecordType.Item, 1)], perType);
        Assert.Equal(AndroidLinksPerType(), perType);
    }

    /// <summary>Android's HomeDao.getLinksPerType, as written there.</summary>
    private List<TypeLinkCount> AndroidLinksPerType()
    {
        using var connection = new SqliteConnection($"Data Source={_test.FilePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT recordTypeId, COUNT(*) AS linkCount FROM (" +
            "SELECT id, record1TypeId AS recordTypeId FROM LinkedRecord " +
            "UNION SELECT id, record2TypeId FROM LinkedRecord" +
            ") WHERE recordTypeId IN (2, 3, 4, 5) GROUP BY recordTypeId ORDER BY recordTypeId";
        using var reader = command.ExecuteReader();
        var rows = new List<TypeLinkCount>();
        while (reader.Read())
        {
            rows.Add(new TypeLinkCount((RecordType)reader.GetInt32(0), reader.GetInt32(1)));
        }
        return rows;
    }

    // ---------- Notes

    [Fact]
    public async Task Recent_notes_are_the_newest_by_time_with_their_link_counts()
    {
        Note("old", Today.AddDays(-3), title: "Old");
        Note("new", Today, minute: 10, title: "New", text: "First line\nSecond");
        Note("mid", Today, minute: 5, text: "\nOnly text");
        Contact("c1", "Smith");
        Contact("c2", "Jones");
        Link("new", RecordType.Note, "c1", RecordType.Contact);
        Link("c2", RecordType.Contact, "new", RecordType.Note);
        Link("c1", RecordType.Contact, "new", RecordType.Note);

        var recent = await _data.Home.GetRecentNotesAsync(2);

        Assert.Equal(["new", "mid"], recent.Select(n => n.Id));
        Assert.Equal("New", recent[0].Name);
        Assert.Equal("First line", recent[0].FirstLine);
        Assert.Equal(2, recent[0].LinkCount);
        Assert.Equal("", recent[1].Name);
        Assert.Equal("Only text", recent[1].Text);
        Assert.Equal(0, recent[1].LinkCount);
    }

    [Fact]
    public async Task Notes_per_day_cover_fourteen_days_with_zero_days()
    {
        Note("a", Today, minute: 1);
        Note("b", Today, minute: 2);
        Note("c", Today.AddDays(-13));
        Note("too-old", Today.AddDays(-14));
        Note("future", Today.AddDays(1));
        Note("mid", Today.AddDays(-5), minute: 23 * 60);

        var days = await _data.Home.GetNotesPerDayAsync(Today);

        Assert.Equal(14, days.Count);
        Assert.Equal(Today.AddDays(-13), days[0].Day);
        Assert.Equal(Today, days[^1].Day);
        Assert.Equal(1, days[0].Count);
        Assert.Equal(2, days[^1].Count);
        Assert.Equal(1, days.Single(d => d.Day == Today.AddDays(-5)).Count);
        Assert.Equal(4, days.Sum(d => d.Count));
    }

    [Fact]
    public async Task Unlinked_notes_are_the_newest_without_links_on_either_side_plus_the_total()
    {
        Note("n1", Today.AddDays(-4));
        Note("n2", Today.AddDays(-3));
        Note("n3", Today.AddDays(-2));
        Note("n4", Today.AddDays(-1));
        Note("n5", Today);
        Note("linked1", Today, minute: 30);
        Note("linked2", Today, minute: 40);
        Link("linked1", RecordType.Note, "c1", RecordType.Contact);
        Link("c1", RecordType.Contact, "linked2", RecordType.Note);

        var unlinked = await _data.Home.GetUnlinkedNotesAsync(3);

        Assert.Equal(["n5", "n4", "n3"], unlinked.Newest.Select(n => n.Id));
        Assert.Equal(5, unlinked.TotalCount);
    }

    // ---------- Recently opened

    [Fact]
    public async Task Record_summaries_keep_the_given_order_skip_missing_records_and_add_parent_paths()
    {
        Category("top", "Tools");
        Category("k1", "Power tools", parentId: "top");
        Contact("c1", "Smith", "Ann");
        Link("c1", RecordType.Contact, "k1", RecordType.Category);

        var rows = await _data.Home.GetRecordSummariesAsync(
            [(RecordType.Category, "k1"), (RecordType.Contact, "gone"), (RecordType.Contact, "c1")]);

        Assert.Equal(["k1", "c1"], rows.Select(r => r.Id));
        Assert.Equal("Tools", rows[0].ParentPath);
        Assert.Equal(1, rows[0].LinkCount);
        Assert.Equal("Ann Smith", rows[1].Name);
    }

    // ---------- Changes since backup

    [Fact]
    public async Task Never_backed_up_without_last_backup_at()
    {
        var reminder = await _data.Home.GetBackupReminderAsync();

        Assert.Equal(BackupReminderKind.NeverBackedUp, reminder.Kind);
    }

    [Fact]
    public async Task Changes_since_backup_count_created_and_updated_records_and_new_links_and_ignore_null_times()
    {
        const long backup = 1_000_000;
        Raw($"INSERT INTO Settings (settingId, settingName, valueInt) VALUES (2, 'lastBackupAt', {backup});",
            "INSERT INTO Note (id, ownerDbId, createDate, createDateTime, createdAt, updatedAt) VALUES ('n-new', 'db1', 0, 0, 1000001, 1000001);",
            "INSERT INTO Note (id, ownerDbId, createDate, createDateTime, createdAt, updatedAt) VALUES ('n-old', 'db1', 0, 0, 1, 1);",
            "INSERT INTO Note (id, ownerDbId, createDate, createDateTime) VALUES ('n-null', 'db1', 0, 0);",
            "INSERT INTO Contact (id, ownerDbId, nameLast, createdAt, updatedAt) VALUES ('c-edited', 'db1', 'A', 1, 1000002);",
            "INSERT INTO Item (id, ownerDbId, itemName, createdAt, updatedAt) VALUES ('i-same', 'db1', 'B', 1000000, 1000000);");
        Link("n-new", RecordType.Note, "c-edited", RecordType.Contact, createdAt: 1_000_005);
        Link("n-old", RecordType.Note, "c-edited", RecordType.Contact, createdAt: 10);
        Link("n-null", RecordType.Note, "c-edited", RecordType.Contact);

        var reminder = await _data.Home.GetBackupReminderAsync();

        Assert.Equal(new BackupReminder(BackupReminderKind.ChangedSince, backup, 3, false), reminder);
    }

    [Fact]
    public async Task Nothing_changed_since_backup_hides_the_reminder_and_a_later_deletion_shows_it()
    {
        Raw("INSERT INTO Settings (settingId, settingName, valueInt) VALUES (2, 'lastBackupAt', 5000);",
            "INSERT INTO Settings (settingId, settingName, valueInt) VALUES (3, 'lastDeletionAt', 4000);");
        Assert.Equal(BackupReminderKind.Hidden, (await _data.Home.GetBackupReminderAsync()).Kind);

        Raw("UPDATE Settings SET valueInt = 6000 WHERE settingId = 3;");
        var reminder = await _data.Home.GetBackupReminderAsync();

        Assert.Equal(new BackupReminder(BackupReminderKind.ChangedSince, 5000, 0, true), reminder);
    }

    [Fact]
    public async Task A_backup_hides_the_reminder()
    {
        Note("n1", Today);
        await _data.Notes.AddNoteAsync(new Note { OwnerDbId = "db1", Title = "Timestamped" });
        Assert.Equal(BackupReminderKind.NeverBackedUp, (await _data.Home.GetBackupReminderAsync()).Kind);

        await _data.Backup.ExportAsync(_test.PathFor("copy.backup"));

        Assert.Equal(BackupReminderKind.Hidden, (await _data.Home.GetBackupReminderAsync()).Kind);
    }

    [Fact]
    public void Reminder_text_names_the_backup_day_the_changes_and_deletions()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var at = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        Assert.Equal(BackupReminder.NeverBackedUpMessage, new BackupReminder(BackupReminderKind.NeverBackedUp).Message(TimeZoneInfo.Utc, culture));
        Assert.Equal("", BackupReminder.From(at, null, 0).Message(TimeZoneInfo.Utc, culture));
        Assert.Equal("Last backup 21 Oct 2026. 18 records changed since and some records were deleted.",
            BackupReminder.From(at, at + 1, 18).Message(TimeZoneInfo.Utc, culture));
        Assert.Equal("Last backup 21 Oct 2026. 1 record changed since.",
            BackupReminder.From(at, at - 1, 1).Message(TimeZoneInfo.Utc, culture));
        Assert.Equal("Last backup 21 Oct 2026. Some records were deleted since.",
            BackupReminder.From(at, at + 1, 0).Message(TimeZoneInfo.Utc, culture));
    }
}
