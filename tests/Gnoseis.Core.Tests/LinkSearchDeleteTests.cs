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

public class LinkSearchDeleteTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public LinkSearchDeleteTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    private static LinkedRecord Link(string id1, RecordType type1, string id2, RecordType type2) => new()
    {
        OwnerDbId = "db1",
        Record1Id = id1,
        Record1TypeId = (int)type1,
        Record2Id = id2,
        Record2TypeId = (int)type2,
    };

    [Fact]
    public async Task Linked_records_are_found_from_both_ends()
    {
        var note = await _data.Notes.AddNoteAsync(new Note { Title = "Meeting" });
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        var item = await _data.Items.AddItemAsync(new Item { ItemName = "Laptop" });
        await _data.LinkedRecords.AddLinkedRecordAsync(Link(note.Id, RecordType.Note, contact.Id, RecordType.Contact));
        await _data.LinkedRecords.AddLinkedRecordAsync(Link(item.Id, RecordType.Item, note.Id, RecordType.Note));

        var fromNote = await _data.LinkedRecords.GetLinkedRecordsAsync(note.Id);
        var fromContact = await _data.LinkedRecords.GetLinkedRecordsAsync(contact.Id);

        Assert.Equal(contact.Id, Assert.Single(fromNote.Contacts).Id);
        Assert.Equal(item.Id, Assert.Single(fromNote.Items).Id);
        Assert.Equal(note.Id, Assert.Single(fromContact.Notes).Id);
        Assert.Equal(2, fromNote.TotalCount);
    }

    [Fact]
    public async Task A_link_is_stored_once_whichever_direction_it_is_added_in()
    {
        // Android issue #13: prevent duplicate links between records.
        await _data.LinkedRecords.AddLinkedRecordAsync(Link("a", RecordType.Note, "b", RecordType.Contact));
        await _data.LinkedRecords.AddLinkedRecordAsync(Link("a", RecordType.Note, "b", RecordType.Contact));
        await _data.LinkedRecords.AddLinkedRecordAsync(Link("b", RecordType.Contact, "a", RecordType.Note));
        await _data.LinkedRecords.AddLinkedRecordsAsync("a", RecordType.Note, [new SearchResult("b", 2, "B")]);

        Assert.Equal(1L, TestDatabase.ScalarRaw(_test.FilePath, "SELECT count(*) FROM LinkedRecord;"));
    }

    [Fact]
    public async Task Link_type_counts_are_grouped_and_most_common_first()
    {
        await _data.LinkedRecords.AddLinkedRecordAsync(Link("a", RecordType.Note, "c1", RecordType.Contact));
        await _data.LinkedRecords.AddLinkedRecordAsync(Link("c2", RecordType.Contact, "a", RecordType.Note));
        await _data.LinkedRecords.AddLinkedRecordAsync(Link("a", RecordType.Note, "i1", RecordType.Item));

        var counts = await _data.LinkedRecords.GetLinkedRecordTypeCountsAsync("a");

        Assert.Equal(new[] { new LinkedRecordTypeCount(2, 2), new LinkedRecordTypeCount(5, 1) }, counts);
    }

    [Fact]
    public async Task A_link_can_be_removed_without_deleting_either_record()
    {
        var note = await _data.Notes.AddNoteAsync(new Note { Title = "Meeting" });
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        await _data.LinkedRecords.AddLinkedRecordAsync(Link(note.Id, RecordType.Note, contact.Id, RecordType.Contact));

        await _data.LinkedRecords.DeleteLinkAsync(contact.Id, note.Id);

        Assert.Equal(0, (await _data.LinkedRecords.GetLinkedRecordsAsync(note.Id)).TotalCount);
        Assert.NotNull(await _data.Contacts.GetContactAsync(contact.Id));
    }

    [Fact]
    public async Task Deleting_a_record_removes_its_links_but_keeps_linked_records()
    {
        var note = await _data.Notes.AddNoteAsync(new Note { Title = "Meeting" });
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        var other = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Doe" });
        await _data.LinkedRecords.AddLinkedRecordAsync(Link(note.Id, RecordType.Note, contact.Id, RecordType.Contact));
        await _data.LinkedRecords.AddLinkedRecordAsync(Link(other.Id, RecordType.Contact, note.Id, RecordType.Note));

        await _data.DeleteRecord.InvokeAsync(note.Id, RecordType.Note);

        Assert.Null(await _data.Notes.GetNoteAsync(note.Id));
        Assert.Equal(2, (await _data.Contacts.GetContactsAsync()).Count);
        Assert.Equal(0L, TestDatabase.ScalarRaw(_test.FilePath, "SELECT count(*) FROM LinkedRecord;"));
    }

    [Fact]
    public async Task Search_finds_a_contact_without_a_first_name()
    {
        // Android known bug 6: "Last, NULL" made the whole search crash.
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smithers", NameFirst = "Waylon" });

        var results = await _data.Search.SearchAsync("smith");

        Assert.Equal(new[] { "Smith", "Smithers, Waylon" }, results.Select(r => r.RecordTitle));
    }

    [Fact]
    public async Task Search_covers_every_record_type_and_ignores_case()
    {
        await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Walker Inc" });
        await _data.Categories.AddCategoryAsync(new Category { CategoryName = "Walking" });
        await _data.Items.AddItemAsync(new Item { ItemName = "WALKIE talkie" });
        await _data.Notes.AddNoteAsync(new Note { Title = "Walk in the park" });

        var results = await _data.Search.SearchAsync("walk");

        Assert.Equal(new[] { RecordType.Note, RecordType.Organization, RecordType.Category, RecordType.Item }, results.Select(r => r.RecordType));
    }

    [Fact]
    public async Task Search_lists_a_note_without_title_by_its_text()
    {
        await _data.Notes.AddNoteAsync(new Note { Title = "", TextContents = "Call the plumber\nabout the sink" });

        var result = Assert.Single(await _data.Search.SearchAsync("plumber"));

        Assert.Equal("Call the plumber", result.RecordTitle);
    }

    [Fact]
    public async Task Record_counts_cover_every_type()
    {
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        await _data.Items.AddItemAsync(new Item { ItemName = "Laptop" });
        await _data.Items.AddItemAsync(new Item { ItemName = "Phone" });

        var counts = await _data.Search.GetRecordCountsAsync();

        Assert.Equal(1, counts[RecordType.Note]); // the welcome note
        Assert.Equal(1, counts[RecordType.Contact]);
        Assert.Equal(0, counts[RecordType.Organization]);
        Assert.Equal(0, counts[RecordType.Category]);
        Assert.Equal(2, counts[RecordType.Item]);
    }

    [Fact]
    public async Task Recently_opened_records_keep_their_order_and_skip_deleted_ones()
    {
        var item = await _data.Items.AddItemAsync(new Item { ItemName = "Laptop" });
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });

        var records = await _data.Search.GetRecordsAsync([contact.Id, "deleted-id", item.Id]);

        Assert.Equal(new[] { "Smith", "Laptop" }, records.Select(r => r.RecordTitle));
    }

    [Fact]
    public async Task Empty_search_returns_all_records()
    {
        await _data.Items.AddItemAsync(new Item { ItemName = "Laptop" });

        var results = await _data.Search.SearchAsync("");

        // The welcome note and the item.
        Assert.Equal(2, results.Count);
    }
}
