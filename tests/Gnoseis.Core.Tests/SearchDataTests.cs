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

/// <summary>What search reads for each record: title, link count, note day, job title, company and updatedAt.</summary>
public class SearchDataTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public SearchDataTests()
    {
        _data = _test.Open();
        // Start without the welcome note.
        TestDatabase.ExecuteRaw(_test.FilePath, "DELETE FROM Note;");
    }

    public void Dispose() => _test.Dispose();

    private Task Link(string id1, RecordType type1, string id2, RecordType type2) =>
        _data.LinkedRecords.AddLinkedRecordAsync(new LinkedRecord
        {
            OwnerDbId = "db1",
            Record1Id = id1,
            Record1TypeId = (int)type1,
            Record2Id = id2,
            Record2TypeId = (int)type2,
        });

    private void SetUpdatedAt(string table, string id, long? updatedAt) =>
        TestDatabase.ExecuteRaw(_test.FilePath,
            $"UPDATE {table} SET updatedAt = {(updatedAt is { } u ? u.ToString() : "NULL")} WHERE id = '{id}';");

    private async Task<SearchResult> Find(string id) => (await _data.Search.GetAllAsync()).Single(r => r.RecordId == id);

    [Fact]
    public async Task Link_counts_are_distinct_records_in_both_directions()
    {
        var note = await _data.Notes.AddNoteAsync(new Note { Title = "Kickoff" });
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Harbour" });
        var organization = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Norvik" });
        var item = await _data.Items.AddItemAsync(new Item { ItemName = "Desk" });
        await Link(note.Id, RecordType.Note, contact.Id, RecordType.Contact);
        await Link(organization.Id, RecordType.Organization, note.Id, RecordType.Note);
        await Link(note.Id, RecordType.Note, item.Id, RecordType.Item);

        Assert.Equal(3, (await Find(note.Id)).LinkCount);
        Assert.Equal(1, (await Find(contact.Id)).LinkCount);
        Assert.Equal(1, (await Find(organization.Id)).LinkCount);
        Assert.Equal(0, (await Find((await _data.Categories.AddCategoryAsync(new Category { CategoryName = "Alone" })).Id)).LinkCount);
    }

    [Fact]
    public async Task A_self_link_counts_once_and_a_pair_linked_twice_counts_once()
    {
        var note = await _data.Notes.AddNoteAsync(new Note { Title = "Self" });
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Twice" });
        // Links from before schema v2 can repeat a pair, in either direction, and link a record to itself.
        TestDatabase.ExecuteRaw(_test.FilePath,
            $"INSERT INTO LinkedRecord (id, ownerDbId, record1Id, record2Id, record1TypeId, record2TypeId) VALUES ('l1', 'db1', '{note.Id}', '{note.Id}', 1, 1);",
            $"INSERT INTO LinkedRecord (id, ownerDbId, record1Id, record2Id, record1TypeId, record2TypeId) VALUES ('l2', 'db1', '{note.Id}', '{contact.Id}', 1, 2);",
            $"INSERT INTO LinkedRecord (id, ownerDbId, record1Id, record2Id, record1TypeId, record2TypeId) VALUES ('l3', 'db1', '{contact.Id}', '{note.Id}', 2, 1);");

        Assert.Equal(2, (await Find(note.Id)).LinkCount);
        Assert.Equal(1, (await Find(contact.Id)).LinkCount);
    }

    [Fact]
    public async Task Results_carry_note_day_job_title_company_and_updated_at()
    {
        var note = await _data.Notes.AddNoteAsync(new Note { Title = "Kickoff", CreateDate = 1_761_609_600_000 });
        var contact = await _data.Contacts.AddContactAsync(new Contact
        {
            NameLast = "Harbour", NameFirst = "Ellen", JobTitle = "Head of operations", Company = "Harbour Labs",
        });
        var organization = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Norvik" });
        SetUpdatedAt("Organization", organization.Id, 1234);
        SetUpdatedAt("Note", note.Id, null);

        var noteResult = await Find(note.Id);
        Assert.Equal(1_761_609_600_000, noteResult.NoteDate);
        Assert.Null(noteResult.UpdatedAt);
        Assert.Null(noteResult.JobTitle);

        var contactResult = await Find(contact.Id);
        Assert.Equal("Harbour, Ellen", contactResult.RecordTitle);
        Assert.Equal("Head of operations", contactResult.JobTitle);
        Assert.Equal("Harbour Labs", contactResult.Company);
        Assert.Null(contactResult.NoteDate);
        Assert.NotNull(contactResult.UpdatedAt);

        var organizationResult = await Find(organization.Id);
        Assert.Equal(RecordType.Organization, organizationResult.RecordType);
        Assert.Equal(1234, organizationResult.UpdatedAt);
    }

    [Fact]
    public async Task Untitled_notes_are_listed_by_their_text_and_empty_notes_are_left_out()
    {
        var untitled = await _data.Notes.AddNoteAsync(new Note { Title = null, TextContents = "Call the plumber\nabout the sink" });
        var empty = await _data.Notes.AddNoteAsync(new Note { Title = "", TextContents = "" });

        var all = await _data.Search.GetAllAsync();

        Assert.Equal("Call the plumber", all.Single(r => r.RecordId == untitled.Id).RecordTitle);
        Assert.DoesNotContain(all, r => r.RecordId == empty.Id);
    }

    [Fact]
    public async Task Contact_titles_are_last_first_or_last_alone()
    {
        var both = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smithers", NameFirst = "Waylon" });
        var lastOnly = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        var emptyFirst = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Jones", NameFirst = "" });

        Assert.Equal("Smithers, Waylon", (await Find(both.Id)).RecordTitle);
        Assert.Equal("Smith", (await Find(lastOnly.Id)).RecordTitle);
        Assert.Equal("Jones", (await Find(emptyFirst.Id)).RecordTitle);
    }

    [Fact]
    public async Task Recently_changed_is_newest_first_in_scope_without_nulls_or_excluded_ids()
    {
        var n1 = await _data.Notes.AddNoteAsync(new Note { Title = "N1" });
        var n2 = await _data.Notes.AddNoteAsync(new Note { Title = "N2" });
        var c1 = await _data.Contacts.AddContactAsync(new Contact { NameLast = "C1" });
        var o1 = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "O1" });
        var i1 = await _data.Items.AddItemAsync(new Item { ItemName = "I1" });
        SetUpdatedAt("Note", n1.Id, 100);
        SetUpdatedAt("Note", n2.Id, 500);
        SetUpdatedAt("Contact", c1.Id, 300);
        SetUpdatedAt("Organization", o1.Id, null);
        SetUpdatedAt("Item", i1.Id, 400);
        await Link(c1.Id, RecordType.Contact, i1.Id, RecordType.Item);

        var all = await _data.Search.GetRecentlyChangedAsync(RecordTypeExtensions.UserTypes, [], 5);
        Assert.Equal(new[] { n2.Id, i1.Id, c1.Id, n1.Id }, all.Select(r => r.RecordId));
        Assert.Equal(1, all.Single(r => r.RecordId == c1.Id).LinkCount);

        var notesAndItems = await _data.Search.GetRecentlyChangedAsync([RecordType.Note, RecordType.Item], [n2.Id], 5);
        Assert.Equal(new[] { i1.Id, n1.Id }, notesAndItems.Select(r => r.RecordId));

        var organizations = await _data.Search.GetRecentlyChangedAsync([RecordType.Organization], [], 5);
        Assert.Empty(organizations);
    }

    [Fact]
    public async Task Recently_changed_takes_at_most_the_limit()
    {
        for (var i = 0; i < 8; i++)
        {
            var note = await _data.Notes.AddNoteAsync(new Note { Title = $"N{i}" });
            SetUpdatedAt("Note", note.Id, 1000 + i);
        }

        var recent = await _data.Search.GetRecentlyChangedAsync([RecordType.Note], [], 5);

        Assert.Equal(new[] { "N7", "N6", "N5", "N4", "N3" }, recent.Select(r => r.RecordTitle));
    }

    [Fact]
    public async Task Recently_changed_skips_excluded_ids_and_still_fills_the_limit()
    {
        var ids = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var item = await _data.Items.AddItemAsync(new Item { ItemName = $"I{i}" });
            SetUpdatedAt("Item", item.Id, 1000 + i);
            ids.Add(item.Id);
        }

        var recent = await _data.Search.GetRecentlyChangedAsync([RecordType.Item], [ids[3], ids[2]], 2);

        Assert.Equal(new[] { "I1", "I0" }, recent.Select(r => r.RecordTitle));
    }
}
