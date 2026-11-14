/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Saving the comments of a contact, organization, category and item from the edit pages, which
/// pass the text box's value through <see cref="RecordText.EditedOrNull"/>: changed text is stored
/// with line feeds, and text that differs only in its line breaks writes nothing.
/// </summary>
public class LineBreakSaveTests : IDisposable
{
    private static readonly string[] StoredForms = ["One\rTwo", "One\r\nTwo", "One\nTwo"];

    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public LineBreakSaveTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    private object? Scalar(string sql) => TestDatabase.ScalarRaw(_test.FilePath, sql);

    private async Task<(Contact, Organization, Category, Item)> AddAsync(string comments)
    {
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith", Comments = comments });
        var organization = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Acme", Comments = comments });
        var category = await _data.Categories.AddCategoryAsync(new Category { CategoryName = "Work", Comments = comments });
        var item = await _data.Items.AddItemAsync(new Item { ItemName = "Laptop", Comments = comments });
        foreach (var table in new[] { "Contact", "Organization", "Category", "Item" })
        {
            TestDatabase.ExecuteRaw(_test.FilePath, $"UPDATE {table} SET createdAt = 100, updatedAt = 200;");
        }
        return (contact, organization, category, item);
    }

    private async Task SaveAsync(Contact contact, Organization organization, Category category, Item item, string boxText)
    {
        await _data.Contacts.UpdateContactAsync(contact with { Comments = RecordText.EditedOrNull(contact.Comments, boxText) });
        await _data.Organizations.UpdateOrganizationAsync(organization with { Comments = RecordText.EditedOrNull(organization.Comments, boxText) });
        await _data.Categories.UpdateCategoryAsync(category with { Comments = RecordText.EditedOrNull(category.Comments, boxText) });
        await _data.Items.UpdateItemAsync(item with { Comments = RecordText.EditedOrNull(item.Comments, boxText) });
    }

    [Fact]
    public async Task Saving_text_that_differs_only_in_its_line_breaks_writes_nothing()
    {
        foreach (var stored in StoredForms)
        {
            var (contact, organization, category, item) = await AddAsync(stored);

            foreach (var boxText in StoredForms)
            {
                await SaveAsync(contact, organization, category, item, boxText);
            }

            foreach (var (table, id) in new[] { ("Contact", contact.Id), ("Organization", organization.Id), ("Category", category.Id), ("Item", item.Id) })
            {
                Assert.Equal(stored, Scalar($"SELECT comments FROM {table} WHERE id = '{id}';"));
                Assert.Equal(200L, Scalar($"SELECT updatedAt FROM {table} WHERE id = '{id}';"));
            }
        }
    }

    [Fact]
    public async Task Changed_text_is_stored_with_line_feeds_only()
    {
        foreach (var stored in StoredForms)
        {
            var (contact, organization, category, item) = await AddAsync(stored);

            await SaveAsync(contact, organization, category, item, "One\rTwo\r\rThree");

            foreach (var (table, id) in new[] { ("Contact", contact.Id), ("Organization", organization.Id), ("Category", category.Id), ("Item", item.Id) })
            {
                Assert.Equal("One\nTwo\n\nThree", Scalar($"SELECT comments FROM {table} WHERE id = '{id}';"));
                Assert.NotEqual(200L, Scalar($"SELECT updatedAt FROM {table} WHERE id = '{id}';"));
            }
        }
    }

    [Fact]
    public async Task New_records_store_their_comments_with_line_feeds()
    {
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith", Comments = RecordText.EditedOrNull(null, "One\rTwo") });
        var item = await _data.Items.AddItemAsync(new Item { ItemName = "Laptop", Comments = RecordText.EditedOrNull(null, "One\r\nTwo") });

        Assert.Equal("One\nTwo", Scalar($"SELECT comments FROM Contact WHERE id = '{contact.Id}';"));
        Assert.Equal("One\nTwo", Scalar($"SELECT comments FROM Item WHERE id = '{item.Id}';"));
    }
}
