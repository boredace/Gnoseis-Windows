/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Data;

public sealed class NoteRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    public Task<List<Note>> GetNotesAsync() =>
        QueryAsync("SELECT * FROM Note ORDER BY createDateTime DESC;", MapNote);

    public Task<Note?> GetNoteAsync(string id) =>
        QuerySingleAsync("SELECT * FROM Note WHERE id = $id;", MapNote, ("$id", id));

    public async Task<Note> AddNoteAsync(Note note)
    {
        await ExecuteAsync(RecordType.Note,
            "INSERT INTO Note (id, ownerDbId, title, textContents, createDateTime, createDate) " +
            "VALUES ($id, $ownerDbId, $title, $textContents, $createDateTime, $createDate);",
            ("$id", note.Id), ("$ownerDbId", note.OwnerDbId), ("$title", note.Title),
            ("$textContents", note.TextContents), ("$createDateTime", note.CreateDateTime),
            ("$createDate", note.CreateDate)).ConfigureAwait(false);
        return note;
    }

    public Task UpdateNoteAsync(Note note) =>
        ExecuteAsync(RecordType.Note,
            "UPDATE Note SET ownerDbId = $ownerDbId, title = $title, textContents = $textContents, " +
            "createDateTime = $createDateTime, createDate = $createDate WHERE id = $id;",
            ("$id", note.Id), ("$ownerDbId", note.OwnerDbId), ("$title", note.Title),
            ("$textContents", note.TextContents), ("$createDateTime", note.CreateDateTime),
            ("$createDate", note.CreateDate));
}

public sealed class ContactRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    public Task<List<Contact>> GetContactsAsync() =>
        QueryAsync("SELECT * FROM Contact ORDER BY nameLast COLLATE NOCASE, nameFirst COLLATE NOCASE;", MapContact);

    public Task<Contact?> GetContactAsync(string id) =>
        QuerySingleAsync("SELECT * FROM Contact WHERE id = $id;", MapContact, ("$id", id));

    public async Task<Contact> AddContactAsync(Contact contact)
    {
        await ExecuteAsync(RecordType.Contact,
            "INSERT INTO Contact (id, ownerDbId, nameLast, nameFirst, jobTitle, company, phoneMain, phoneMobile, " +
            "phoneHome, phoneWork, emailMain, emailMobile, emailHome, emailWork, comments) VALUES ($id, $ownerDbId, " +
            "$nameLast, $nameFirst, $jobTitle, $company, $phoneMain, $phoneMobile, $phoneHome, $phoneWork, $emailMain, " +
            "$emailMobile, $emailHome, $emailWork, $comments);",
            Parameters(contact)).ConfigureAwait(false);
        return contact;
    }

    public Task UpdateContactAsync(Contact contact) =>
        ExecuteAsync(RecordType.Contact,
            "UPDATE Contact SET ownerDbId = $ownerDbId, nameLast = $nameLast, nameFirst = $nameFirst, " +
            "jobTitle = $jobTitle, company = $company, phoneMain = $phoneMain, phoneMobile = $phoneMobile, " +
            "phoneHome = $phoneHome, phoneWork = $phoneWork, emailMain = $emailMain, emailMobile = $emailMobile, " +
            "emailHome = $emailHome, emailWork = $emailWork, comments = $comments WHERE id = $id;",
            Parameters(contact));

    private static (string, object?)[] Parameters(Contact c) =>
    [
        ("$id", c.Id), ("$ownerDbId", c.OwnerDbId), ("$nameLast", c.NameLast), ("$nameFirst", c.NameFirst),
        ("$jobTitle", c.JobTitle), ("$company", c.Company), ("$phoneMain", c.PhoneMain),
        ("$phoneMobile", c.PhoneMobile), ("$phoneHome", c.PhoneHome), ("$phoneWork", c.PhoneWork),
        ("$emailMain", c.EmailMain), ("$emailMobile", c.EmailMobile), ("$emailHome", c.EmailHome),
        ("$emailWork", c.EmailWork), ("$comments", c.Comments),
    ];
}

public sealed class OrganizationRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    public Task<List<Organization>> GetOrganizationsAsync() =>
        QueryAsync("SELECT * FROM Organization ORDER BY organizationName COLLATE NOCASE;", MapOrganization);

    public Task<Organization?> GetOrganizationAsync(string id) =>
        QuerySingleAsync("SELECT * FROM Organization WHERE id = $id;", MapOrganization, ("$id", id));

    public async Task<Organization> AddOrganizationAsync(Organization organization)
    {
        await ExecuteAsync(RecordType.Organization,
            "INSERT INTO Organization (id, ownerDbId, organizationName, comments) VALUES ($id, $ownerDbId, $name, $comments);",
            ("$id", organization.Id), ("$ownerDbId", organization.OwnerDbId),
            ("$name", organization.OrganizationName), ("$comments", organization.Comments)).ConfigureAwait(false);
        return organization;
    }

    public Task UpdateOrganizationAsync(Organization organization) =>
        ExecuteAsync(RecordType.Organization,
            "UPDATE Organization SET ownerDbId = $ownerDbId, organizationName = $name, comments = $comments WHERE id = $id;",
            ("$id", organization.Id), ("$ownerDbId", organization.OwnerDbId),
            ("$name", organization.OrganizationName), ("$comments", organization.Comments));
}

public sealed class CategoryRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    public Task<List<Category>> GetCategoriesAsync() =>
        QueryAsync("SELECT * FROM Category ORDER BY categoryName COLLATE NOCASE;", MapCategory);

    public Task<Category?> GetCategoryAsync(string id) =>
        QuerySingleAsync("SELECT * FROM Category WHERE id = $id;", MapCategory, ("$id", id));

    public async Task<Category> AddCategoryAsync(Category category)
    {
        await ExecuteAsync(RecordType.Category,
            "INSERT INTO Category (id, ownerDbId, parentId, categoryName, comments) VALUES ($id, $ownerDbId, $parentId, $name, $comments);",
            ("$id", category.Id), ("$ownerDbId", category.OwnerDbId), ("$parentId", category.ParentId),
            ("$name", category.CategoryName), ("$comments", category.Comments)).ConfigureAwait(false);
        return category;
    }

    public Task UpdateCategoryAsync(Category category) =>
        ExecuteAsync(RecordType.Category,
            "UPDATE Category SET ownerDbId = $ownerDbId, parentId = $parentId, categoryName = $name, comments = $comments WHERE id = $id;",
            ("$id", category.Id), ("$ownerDbId", category.OwnerDbId), ("$parentId", category.ParentId),
            ("$name", category.CategoryName), ("$comments", category.Comments));
}

public sealed class ItemRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    public Task<List<Item>> GetItemsAsync() =>
        QueryAsync("SELECT * FROM Item ORDER BY itemName COLLATE NOCASE;", MapItem);

    public Task<Item?> GetItemAsync(string id) =>
        QuerySingleAsync("SELECT * FROM Item WHERE id = $id;", MapItem, ("$id", id));

    public async Task<Item> AddItemAsync(Item item)
    {
        await ExecuteAsync(RecordType.Item,
            "INSERT INTO Item (id, ownerDbId, parentId, itemName, comments) VALUES ($id, $ownerDbId, $parentId, $name, $comments);",
            ("$id", item.Id), ("$ownerDbId", item.OwnerDbId), ("$parentId", item.ParentId),
            ("$name", item.ItemName), ("$comments", item.Comments)).ConfigureAwait(false);
        return item;
    }

    public Task UpdateItemAsync(Item item) =>
        ExecuteAsync(RecordType.Item,
            "UPDATE Item SET ownerDbId = $ownerDbId, parentId = $parentId, itemName = $name, comments = $comments WHERE id = $id;",
            ("$id", item.Id), ("$ownerDbId", item.OwnerDbId), ("$parentId", item.ParentId),
            ("$name", item.ItemName), ("$comments", item.Comments));
}
