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

/// <summary>Records keep the time they were created and last changed (UTC epoch ms), the same way as Android.</summary>
public class RecordTimestampTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public RecordTimestampTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    private object? Scalar(string sql) => TestDatabase.ScalarRaw(_test.FilePath, sql);

    private (long? CreatedAt, long? UpdatedAt) Times(string table, string id)
    {
        var createdAt = Scalar($"SELECT createdAt FROM {table} WHERE id = '{id}';");
        var updatedAt = Scalar($"SELECT updatedAt FROM {table} WHERE id = '{id}';");
        return (createdAt is long c ? c : null, updatedAt is long u ? u : null);
    }

    private static void AssertBetween(long before, long? actual, long after)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual.Value, before, after);
    }

    [Fact]
    public async Task New_records_get_created_and_updated_times()
    {
        var before = EpochTime.UtcNowMillis();
        var note = await _data.Notes.AddNoteAsync(new Note { Title = "N" });
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        var organization = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Acme" });
        var category = await _data.Categories.AddCategoryAsync(new Category { CategoryName = "Work" });
        var item = await _data.Items.AddItemAsync(new Item { ItemName = "Laptop" });
        var after = EpochTime.UtcNowMillis();

        foreach (var (table, id) in new[] { ("Note", note.Id), ("Contact", contact.Id), ("Organization", organization.Id), ("Category", category.Id), ("Item", item.Id) })
        {
            var (createdAt, updatedAt) = Times(table, id);
            AssertBetween(before, createdAt, after);
            Assert.Equal(createdAt, updatedAt);
        }
    }

    [Fact]
    public async Task New_links_get_a_created_time()
    {
        var a = await _data.Contacts.AddContactAsync(new Contact { NameLast = "A" });
        var b = await _data.Contacts.AddContactAsync(new Contact { NameLast = "B" });
        var c = await _data.Contacts.AddContactAsync(new Contact { NameLast = "C" });
        var before = EpochTime.UtcNowMillis();

        await _data.LinkedRecords.AddLinkedRecordAsync(new LinkedRecord
        {
            OwnerDbId = "db1", Record1Id = a.Id, Record2Id = b.Id,
            Record1TypeId = (int)RecordType.Contact, Record2TypeId = (int)RecordType.Contact,
        });
        await _data.LinkedRecords.AddLinkedRecordsAsync(a.Id, RecordType.Contact, [new SearchResult(c.Id, (int)RecordType.Contact, "C")]);
        var after = EpochTime.UtcNowMillis();

        Assert.Equal(2L, Scalar("SELECT count(*) FROM LinkedRecord;"));
        AssertBetween(before, Scalar("SELECT min(createdAt) FROM LinkedRecord;") as long?, after);
        AssertBetween(before, Scalar("SELECT max(createdAt) FROM LinkedRecord;") as long?, after);
    }

    /// <summary>Adds one record of each type, with old times and a different owner written straight to the file.</summary>
    private async Task<(Note, Contact, Organization, Category, Item)> AddOldRecordsAsync()
    {
        var records = (
            await _data.Notes.AddNoteAsync(new Note { Title = "N", CreateDateTime = 1_000, CreateDate = 0 }),
            await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" }),
            await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Acme" }),
            await _data.Categories.AddCategoryAsync(new Category { CategoryName = "Work" }),
            await _data.Items.AddItemAsync(new Item { ItemName = "Laptop" }));
        foreach (var table in RecordTables)
        {
            TestDatabase.ExecuteRaw(_test.FilePath, $"UPDATE {table} SET createdAt = 100, updatedAt = 200, ownerDbId = 'other-owner';");
        }
        return records;
    }

    private static readonly string[] RecordTables = ["Note", "Contact", "Organization", "Category", "Item"];

    [Fact]
    public async Task Saving_without_a_change_leaves_the_record_untouched()
    {
        var (note, contact, organization, category, item) = await AddOldRecordsAsync();

        await _data.Notes.UpdateNoteAsync(note);
        await _data.Contacts.UpdateContactAsync(contact);
        await _data.Organizations.UpdateOrganizationAsync(organization);
        await _data.Categories.UpdateCategoryAsync(category);
        await _data.Items.UpdateItemAsync(item);

        foreach (var (table, id) in new[] { ("Note", note.Id), ("Contact", contact.Id), ("Organization", organization.Id), ("Category", category.Id), ("Item", item.Id) })
        {
            Assert.Equal((100L, 200L), Times(table, id));
            Assert.Equal("other-owner", Scalar($"SELECT ownerDbId FROM {table} WHERE id = '{id}';"));
        }
    }

    [Fact]
    public async Task An_edit_moves_only_the_updated_time_and_keeps_the_owner()
    {
        var (note, contact, organization, category, item) = await AddOldRecordsAsync();
        var before = EpochTime.UtcNowMillis();

        await _data.Notes.UpdateNoteAsync(note with { TextContents = "Changed" });
        await _data.Contacts.UpdateContactAsync(contact with { EmailWork = "a@b.c" });
        await _data.Organizations.UpdateOrganizationAsync(organization with { OrganizationName = "Acme Ltd" });
        await _data.Categories.UpdateCategoryAsync(category with { ParentId = "p1" });
        await _data.Items.UpdateItemAsync(item with { Comments = "Old" });
        var after = EpochTime.UtcNowMillis();

        foreach (var (table, id) in new[] { ("Note", note.Id), ("Contact", contact.Id), ("Organization", organization.Id), ("Category", category.Id), ("Item", item.Id) })
        {
            var (createdAt, updatedAt) = Times(table, id);
            Assert.Equal(100L, createdAt);
            AssertBetween(before, updatedAt, after);
            Assert.Equal("other-owner", Scalar($"SELECT ownerDbId FROM {table} WHERE id = '{id}';"));
        }
        Assert.Equal("Changed", (await _data.Notes.GetNoteAsync(note.Id))!.TextContents);
        Assert.Equal("a@b.c", (await _data.Contacts.GetContactAsync(contact.Id))!.EmailWork);
        Assert.Equal("Acme Ltd", (await _data.Organizations.GetOrganizationAsync(organization.Id))!.OrganizationName);
        Assert.Equal("p1", (await _data.Categories.GetCategoryAsync(category.Id))!.ParentId);
        Assert.Equal("Old", (await _data.Items.GetItemAsync(item.Id))!.Comments);
    }

    [Fact]
    public async Task Changing_a_note_date_counts_as_an_edit()
    {
        var (note, _, _, _, _) = await AddOldRecordsAsync();

        await _data.Notes.UpdateNoteAsync(note with { CreateDate = 86_400_000 });

        Assert.NotEqual(200L, Times("Note", note.Id).UpdatedAt);
        Assert.Equal(100L, Times("Note", note.Id).CreatedAt);
    }

    [Fact]
    public async Task Editing_a_record_from_before_version_3_sets_only_its_updated_time()
    {
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        TestDatabase.ExecuteRaw(_test.FilePath, "UPDATE Contact SET createdAt = NULL, updatedAt = NULL;");

        await _data.Contacts.UpdateContactAsync(contact with { NameFirst = "Ann" });

        var (createdAt, updatedAt) = Times("Contact", contact.Id);
        Assert.Null(createdAt);
        Assert.NotNull(updatedAt);
    }

    [Fact]
    public void The_welcome_note_of_a_new_database_gets_created_and_updated_times()
    {
        var createdAt = Scalar("SELECT createdAt FROM Note;");
        var updatedAt = Scalar("SELECT updatedAt FROM Note;");

        Assert.IsType<long>(createdAt);
        Assert.Equal(createdAt, updatedAt);
        Assert.InRange((long)createdAt!, EpochTime.UtcNowMillis() - 60_000, EpochTime.UtcNowMillis());
    }
}
