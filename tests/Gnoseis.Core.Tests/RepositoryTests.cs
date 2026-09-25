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

public class RepositoryTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public RepositoryTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Notes_are_added_updated_and_listed_newest_first()
    {
        var older = await _data.Notes.AddNoteAsync(new Note { Title = "Older", CreateDateTime = 1_000, CreateDate = 0 });
        var newer = await _data.Notes.AddNoteAsync(new Note { Title = "Newer", CreateDateTime = 2_000, CreateDate = 0 });
        await _data.Notes.UpdateNoteAsync(older with { TextContents = "Changed" });

        var notes = await _data.Notes.GetNotesAsync();

        var ids = notes.Select(n => n.Id).ToList();
        Assert.True(ids.IndexOf(newer.Id) < ids.IndexOf(older.Id));
        Assert.Equal("Changed", (await _data.Notes.GetNoteAsync(older.Id))!.TextContents);
    }

    [Fact]
    public async Task Contacts_keep_null_optional_fields_and_sort_by_last_then_first_name()
    {
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "smith", NameFirst = "Zoe" });
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Smith", NameFirst = "Adam" });
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "Doe" });

        var contacts = await _data.Contacts.GetContactsAsync();

        Assert.Equal(new[] { "Doe", "Smith, Adam", "smith, Zoe" }, contacts.Select(c => c.DisplayName));
        Assert.Null(contacts[0].NameFirst);
        Assert.Null(contacts[0].EmailMain);
    }

    [Fact]
    public async Task Organizations_categories_and_items_round_trip()
    {
        var organization = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Acme", Comments = "Widgets" });
        var category = await _data.Categories.AddCategoryAsync(new Category { CategoryName = "Important" });
        var item = await _data.Items.AddItemAsync(new Item { ItemName = "Model Y" });

        await _data.Organizations.UpdateOrganizationAsync(organization with { OrganizationName = "Acme Ltd" });
        await _data.Categories.UpdateCategoryAsync(category with { Comments = "Top" });
        await _data.Items.UpdateItemAsync(item with { ItemName = "Model 3" });

        Assert.Equal("Acme Ltd", (await _data.Organizations.GetOrganizationAsync(organization.Id))!.OrganizationName);
        Assert.Equal("Top", (await _data.Categories.GetCategoryAsync(category.Id))!.Comments);
        Assert.Equal("Model 3", (await _data.Items.GetItemAsync(item.Id))!.ItemName);
        Assert.Null(await _data.Items.GetItemAsync("missing"));
    }

    [Fact]
    public async Task Writes_raise_DataChanged_for_the_changed_type()
    {
        var changes = new List<DataChangedEventArgs>();
        _data.Database.DataChanged += (_, e) => changes.Add(e);

        await _data.Items.AddItemAsync(new Item { ItemName = "Thing" });

        var change = Assert.Single(changes);
        Assert.True(change.Affects(RecordType.Item));
        Assert.False(change.Affects(RecordType.Note));
    }
}
