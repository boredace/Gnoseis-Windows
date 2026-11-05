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

/// <summary>Unlink and Undo, the name of a link source, and the linked records of the record page.</summary>
public class RecordPageTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public RecordPageTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    private long LinkCount() => (long)TestDatabase.ScalarRaw(_test.FilePath, "SELECT count(*) FROM LinkedRecord;")!;

    /// <summary>Inserts a link row as is (duplicates and missing createdAt included), as older files have them.</summary>
    private void InsertLinkRaw(string id1, RecordType type1, string id2, RecordType type2, long? createdAt)
    {
        TestDatabase.ExecuteRaw(_test.FilePath,
            "INSERT INTO LinkedRecord (id, ownerDbId, record1Id, record2Id, record1TypeId, record2TypeId, createdAt) VALUES " +
            $"('{Guid.NewGuid()}', 'db1', '{id1}', '{id2}', {(int)type1}, {(int)type2}, {(createdAt?.ToString() ?? "NULL")});");
    }

    private Task Link(string id1, RecordType type1, string id2, RecordType type2) =>
        _data.LinkedRecords.AddLinkedRecordAsync(new LinkedRecord
        {
            OwnerDbId = "db1", Record1Id = id1, Record1TypeId = (int)type1, Record2Id = id2, Record2TypeId = (int)type2,
        });

    // ---------------------------------------------------------------- Unlink and Undo

    [Fact]
    public async Task Unlink_removes_both_directions_and_old_duplicates_and_writes_lastDeletionAt()
    {
        InsertLinkRaw("a", RecordType.Note, "b", RecordType.Contact, 100);
        InsertLinkRaw("b", RecordType.Contact, "a", RecordType.Note, null);
        InsertLinkRaw("a", RecordType.Note, "b", RecordType.Contact, 50);
        InsertLinkRaw("a", RecordType.Note, "c", RecordType.Item, 10);
        var before = EpochTime.UtcNowMillis();

        var removed = await _data.UnlinkRecords.InvokeAsync("b", "a");

        Assert.NotNull(removed);
        Assert.Equal(("a", "b", 100L), (removed.Record1Id, removed.Record2Id, removed.CreatedAt!.Value));
        Assert.Equal(1L, LinkCount());
        Assert.InRange((await _data.Settings.GetLastDeletionAtAsync())!.Value, before, EpochTime.UtcNowMillis());
    }

    [Fact]
    public async Task Unlink_of_records_that_are_not_linked_writes_nothing()
    {
        InsertLinkRaw("a", RecordType.Note, "c", RecordType.Item, 10);

        Assert.Null(await _data.UnlinkRecords.InvokeAsync("a", "b"));

        Assert.Equal(1L, LinkCount());
        Assert.Null(await _data.Settings.GetLastDeletionAtAsync());
    }

    [Fact]
    public async Task Undo_links_again_once_in_the_original_direction_with_a_new_id_and_createdAt()
    {
        InsertLinkRaw("b", RecordType.Contact, "a", RecordType.Note, 100);
        var removed = (await _data.UnlinkRecords.InvokeAsync("a", "b"))!;
        var deletedAt = await _data.Settings.GetLastDeletionAtAsync();
        var before = EpochTime.UtcNowMillis();

        await _data.UnlinkRecords.UndoAsync(removed);
        await _data.UnlinkRecords.UndoAsync(removed);

        Assert.Equal(1L, LinkCount());
        Assert.Equal("b", TestDatabase.ScalarRaw(_test.FilePath, "SELECT record1Id FROM LinkedRecord;"));
        Assert.Equal((long)RecordType.Contact, TestDatabase.ScalarRaw(_test.FilePath, "SELECT record1TypeId FROM LinkedRecord;"));
        Assert.NotEqual(removed.Id, TestDatabase.ScalarRaw(_test.FilePath, "SELECT id FROM LinkedRecord;"));
        Assert.InRange((long)TestDatabase.ScalarRaw(_test.FilePath, "SELECT createdAt FROM LinkedRecord;")!, before, EpochTime.UtcNowMillis());
        Assert.Equal(deletedAt, await _data.Settings.GetLastDeletionAtAsync());
    }

    // ---------------------------------------------------------------- Link source name

    [Fact]
    public async Task Link_source_names_follow_the_android_rules()
    {
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameFirst = " Maria ", NameLast = "Lindqvist " });
        var lastOnly = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Holm" });
        var titled = await _data.Notes.AddNoteAsync(new Note { Title = "Quote", CreateDate = EpochTime.UtcMidnight(new DateOnly(2026, 11, 3)) });
        var untitled = await _data.Notes.AddNoteAsync(new Note { Title = "  ", TextContents = "Text", CreateDate = EpochTime.UtcMidnight(new DateOnly(2026, 10, 28)) });
        var blank = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = " " });
        var item = await _data.Items.AddItemAsync(new Item { ItemName = "Oak flooring" });

        Assert.Equal(new LinkSource(RecordType.Contact, "Maria Lindqvist"), await _data.GetLinkSource.InvokeAsync(RecordType.Contact, contact.Id));
        Assert.Equal("Holm", (await _data.GetLinkSource.InvokeAsync(RecordType.Contact, lastOnly.Id))!.Name);
        Assert.Equal("Quote", (await _data.GetLinkSource.InvokeAsync(RecordType.Note, titled.Id))!.Name);
        Assert.Equal("2026-10-28", (await _data.GetLinkSource.InvokeAsync(RecordType.Note, untitled.Id))!.Name);
        Assert.Equal("Organization", (await _data.GetLinkSource.InvokeAsync(RecordType.Organization, blank.Id))!.Name);
        Assert.Equal("Oak flooring", (await _data.GetLinkSource.InvokeAsync(RecordType.Item, item.Id))!.Name);
        Assert.Null(await _data.GetLinkSource.InvokeAsync(RecordType.Category, "missing"));
    }

    // ---------------------------------------------------------------- Linked records of one record

    [Fact]
    public async Task Linked_rows_carry_their_fields_and_link_counts()
    {
        var source = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Nordvik" });
        var note = await _data.Notes.AddNoteAsync(new Note { Title = "Quote", TextContents = "\n  First line  \nSecond line" });
        var phone = await _data.Contacts.AddContactAsync(new Contact
        {
            NameFirst = "Erik", NameLast = "Holm", JobTitle = "Site manager", Company = "Uppsala Builders",
            PhoneMain = "+46 70 555", EmailMain = "erik@example.com",
        });
        var mail = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Berg", Company = "Nordvik", EmailMain = "sara@example.com" });
        var root = await _data.Categories.AddCategoryAsync(new Category { CategoryName = "Flooring" });
        var middle = await _data.Categories.AddCategoryAsync(new Category { CategoryName = "Oak", ParentId = root.Id });
        var leaf = await _data.Categories.AddCategoryAsync(new Category { CategoryName = "Brushed", ParentId = middle.Id, Comments = "Matt\nlacquer" });
        var orphan = await _data.Items.AddItemAsync(new Item { ItemName = "Pine", ParentId = "missing" });

        foreach (var (id, type) in new[] { (note.Id, RecordType.Note), (phone.Id, RecordType.Contact), (mail.Id, RecordType.Contact), (leaf.Id, RecordType.Category), (root.Id, RecordType.Category), (orphan.Id, RecordType.Item) })
        {
            await Link(source.Id, RecordType.Organization, id, type);
        }
        await Link(phone.Id, RecordType.Contact, note.Id, RecordType.Note);
        InsertLinkRaw(note.Id, RecordType.Note, phone.Id, RecordType.Contact, null); // an old duplicate counts once

        var set = await _data.LinkedRecords.GetLinkedRecordSetAsync(source.Id);
        LinkedRecordSummary Row(string id) => set.Groups.SelectMany(g => g.Records).Single(r => r.Id == id);

        Assert.Equal(6, set.TotalCount);
        Assert.Equal(("Quote", "First line", "First line Second line", 2), (Row(note.Id).Name, Row(note.Id).FirstLine, Row(note.Id).Text, Row(note.Id).LinkCount));
        Assert.Equal(("Erik Holm", "Site manager · Uppsala Builders", "+46 70 555", 2), (Row(phone.Id).Name, Row(phone.Id).JobAndCompany, Row(phone.Id).MainContact, Row(phone.Id).LinkCount));
        Assert.Equal(("Berg", "Nordvik", "sara@example.com", 1), (Row(mail.Id).Name, Row(mail.Id).JobAndCompany, Row(mail.Id).MainContact, Row(mail.Id).LinkCount));
        Assert.Equal(("Flooring › Oak", "Matt"), (Row(leaf.Id).ParentPath, Row(leaf.Id).FirstLine));
        Assert.Equal("", Row(root.Id).ParentPath);
        Assert.Equal("", Row(orphan.Id).ParentPath);
        Assert.NotNull(Row(note.Id).LinkedAt);
        Assert.Equal("Flooring › Oak", await _data.Categories.GetParentPathAsync(leaf));
        Assert.Equal("", await _data.Categories.GetParentPathAsync(root));
    }

    [Fact]
    public async Task Types_are_sorted_by_count_with_ties_in_the_fixed_order()
    {
        var source = await _data.Notes.AddNoteAsync(new Note { Title = "Source" });
        foreach (var name in new[] { "i1", "i2" })
        {
            await Link(source.Id, RecordType.Note, (await _data.Items.AddItemAsync(new Item { ItemName = name })).Id, RecordType.Item);
        }
        foreach (var name in new[] { "o1", "o2" })
        {
            await Link((await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = name })).Id, RecordType.Organization, source.Id, RecordType.Note);
        }
        await Link(source.Id, RecordType.Note, (await _data.Categories.AddCategoryAsync(new Category { CategoryName = "c" })).Id, RecordType.Category);
        foreach (var name in new[] { "n1", "n2", "n3" })
        {
            await Link(source.Id, RecordType.Note, (await _data.Notes.AddNoteAsync(new Note { Title = name })).Id, RecordType.Note);
        }

        var set = await _data.LinkedRecords.GetLinkedRecordSetAsync(source.Id);

        Assert.Equal(new[] { RecordType.Note, RecordType.Organization, RecordType.Item, RecordType.Category }, set.Groups.Select(g => g.Type));
        Assert.Equal(new[] { 3, 2, 2, 1 }, set.Groups.Select(g => g.Count));
    }

    [Fact]
    public async Task All_shows_the_three_latest_notes_by_note_day()
    {
        var source = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Source" });
        Note NoteOn(string title, int day, int hour) => new()
        {
            Title = title,
            CreateDate = EpochTime.UtcMidnight(new DateOnly(2026, 10, day)),
            CreateDateTime = EpochTime.UtcMidnight(new DateOnly(2026, 10, day)) + hour * 3_600_000L,
        };
        foreach (var note in new[] { NoteOn("old", 1, 9), NoteOn("late", 20, 15), NoteOn("early", 20, 8), NoteOn("newest", 28, 10), NoteOn("middle", 10, 12) })
        {
            await _data.Notes.AddNoteAsync(note);
            await Link(source.Id, RecordType.Contact, note.Id, RecordType.Note);
        }

        var notes = (await _data.LinkedRecords.GetLinkedRecordSetAsync(source.Id)).Find(RecordType.Note)!;

        Assert.Equal(new[] { "newest", "late", "early" }, notes.Latest.Select(n => n.Name));
        Assert.Equal(new[] { "newest", "late", "early", "middle", "old" }, notes.Records.Select(n => n.Name));
        Assert.True(notes.HasMore);
    }

    [Fact]
    public async Task All_shows_other_types_by_link_time_with_undated_links_last_by_name()
    {
        var source = await _data.Notes.AddNoteAsync(new Note { Title = "Source" });
        var names = new Dictionary<string, long?> { ["Delta"] = null, ["Alpha"] = null, ["Bravo"] = 100, ["Charlie"] = 300, ["Echo"] = 200 };
        foreach (var (name, createdAt) in names)
        {
            var item = await _data.Items.AddItemAsync(new Item { ItemName = name });
            InsertLinkRaw(source.Id, RecordType.Note, item.Id, RecordType.Item, createdAt);
        }

        var items = (await _data.LinkedRecords.GetLinkedRecordSetAsync(source.Id)).Find(RecordType.Item)!;

        Assert.Equal(new[] { "Charlie", "Echo", "Bravo" }, items.Latest.Select(i => i.Name));
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" }, items.Records.Select(i => i.Name));

        // Fewer dated links than shown: the undated ones follow, by name.
        var few = new LinkedRecordSet(items.Records.Select(r => r.Name is "Bravo" ? r : r with { LinkedAt = null }));
        Assert.Equal(new[] { "Bravo", "Alpha", "Charlie" }, few.Find(RecordType.Item)!.Latest.Select(i => i.Name));
    }

    [Fact]
    public async Task Contacts_are_listed_by_last_then_first_name()
    {
        var source = await _data.Notes.AddNoteAsync(new Note { Title = "Source" });
        foreach (var (first, last) in new[] { ("Sara", "Berg"), ("Anna", "Holm"), ("Erik", "Berg") })
        {
            var contact = await _data.Contacts.AddContactAsync(new Contact { NameFirst = first, NameLast = last });
            await Link(source.Id, RecordType.Note, contact.Id, RecordType.Contact);
        }

        var contacts = (await _data.LinkedRecords.GetLinkedRecordSetAsync(source.Id)).Find(RecordType.Contact)!;

        Assert.Equal(new[] { "Erik Berg", "Sara Berg", "Anna Holm" }, contacts.Records.Select(c => c.Name));
        Assert.False(contacts.HasMore);
    }
}
