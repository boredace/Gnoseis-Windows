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
