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
            "INSERT INTO Note (id, ownerDbId, title, textContents, createDateTime, createDate, createdAt, updatedAt) " +
            "VALUES ($id, $ownerDbId, $title, $textContents, $createDateTime, $createDate, $now, $now);",
            ("$id", note.Id), ("$ownerDbId", note.OwnerDbId), ("$title", note.Title),
            ("$textContents", note.TextContents), ("$createDateTime", note.CreateDateTime),
            ("$createDate", note.CreateDate), ("$now", EpochTime.UtcNowMillis())).ConfigureAwait(false);
        return note;
    }

    /// <summary>
    /// Saves the user-editable columns (the note's dates count as editable). Only a real change
    /// moves updatedAt; the owner and createdAt are never written.
    /// </summary>
    public Task UpdateNoteAsync(Note note) =>
        ExecuteAsync(RecordType.Note,
            "UPDATE Note SET title = $title, textContents = $textContents, " +
            "createDateTime = $createDateTime, createDate = $createDate, updatedAt = $now " +
            "WHERE id = $id AND (title IS NOT $title OR textContents IS NOT $textContents " +
            "OR createDateTime IS NOT $createDateTime OR createDate IS NOT $createDate);",
            ("$id", note.Id), ("$title", note.Title),
            ("$textContents", note.TextContents), ("$createDateTime", note.CreateDateTime),
            ("$createDate", note.CreateDate), ("$now", EpochTime.UtcNowMillis()));
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
            "phoneHome, phoneWork, emailMain, emailMobile, emailHome, emailWork, comments, createdAt, updatedAt) VALUES ($id, $ownerDbId, " +
            "$nameLast, $nameFirst, $jobTitle, $company, $phoneMain, $phoneMobile, $phoneHome, $phoneWork, $emailMain, " +
            "$emailMobile, $emailHome, $emailWork, $comments, $now, $now);",
            [.. Parameters(contact), ("$now", EpochTime.UtcNowMillis())]).ConfigureAwait(false);
        return contact;
    }

    /// <summary>
    /// Saves the user-editable columns. Only a real change moves updatedAt; the owner and
    /// createdAt are never written.
    /// </summary>
    public Task UpdateContactAsync(Contact contact) =>
        ExecuteAsync(RecordType.Contact,
            "UPDATE Contact SET nameLast = $nameLast, nameFirst = $nameFirst, " +
            "jobTitle = $jobTitle, company = $company, phoneMain = $phoneMain, phoneMobile = $phoneMobile, " +
            "phoneHome = $phoneHome, phoneWork = $phoneWork, emailMain = $emailMain, emailMobile = $emailMobile, " +
            "emailHome = $emailHome, emailWork = $emailWork, comments = $comments, updatedAt = $now " +
            "WHERE id = $id AND (nameLast IS NOT $nameLast OR nameFirst IS NOT $nameFirst " +
            "OR jobTitle IS NOT $jobTitle OR company IS NOT $company OR phoneMain IS NOT $phoneMain " +
            "OR phoneMobile IS NOT $phoneMobile OR phoneHome IS NOT $phoneHome OR phoneWork IS NOT $phoneWork " +
            "OR emailMain IS NOT $emailMain OR emailMobile IS NOT $emailMobile OR emailHome IS NOT $emailHome " +
            "OR emailWork IS NOT $emailWork OR comments IS NOT $comments);",
            [.. Parameters(contact).Where(p => p.Item1 != "$ownerDbId"), ("$now", EpochTime.UtcNowMillis())]);

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
            "INSERT INTO Organization (id, ownerDbId, organizationName, comments, createdAt, updatedAt) VALUES ($id, $ownerDbId, $name, $comments, $now, $now);",
            ("$id", organization.Id), ("$ownerDbId", organization.OwnerDbId),
            ("$name", organization.OrganizationName), ("$comments", organization.Comments),
            ("$now", EpochTime.UtcNowMillis())).ConfigureAwait(false);
        return organization;
    }

    /// <summary>
    /// Saves the user-editable columns. Only a real change moves updatedAt; the owner and
    /// createdAt are never written.
    /// </summary>
    public Task UpdateOrganizationAsync(Organization organization) =>
        ExecuteAsync(RecordType.Organization,
            "UPDATE Organization SET organizationName = $name, comments = $comments, updatedAt = $now " +
            "WHERE id = $id AND (organizationName IS NOT $name OR comments IS NOT $comments);",
            ("$id", organization.Id),
            ("$name", organization.OrganizationName), ("$comments", organization.Comments),
            ("$now", EpochTime.UtcNowMillis()));
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
            "INSERT INTO Category (id, ownerDbId, parentId, categoryName, comments, createdAt, updatedAt) VALUES ($id, $ownerDbId, $parentId, $name, $comments, $now, $now);",
            ("$id", category.Id), ("$ownerDbId", category.OwnerDbId), ("$parentId", category.ParentId),
            ("$name", category.CategoryName), ("$comments", category.Comments),
            ("$now", EpochTime.UtcNowMillis())).ConfigureAwait(false);
        return category;
    }

    /// <summary>
    /// Saves the user-editable columns. Only a real change moves updatedAt; the owner and
    /// createdAt are never written.
    /// </summary>
    public Task UpdateCategoryAsync(Category category) =>
        ExecuteAsync(RecordType.Category,
            "UPDATE Category SET parentId = $parentId, categoryName = $name, comments = $comments, updatedAt = $now " +
            "WHERE id = $id AND (parentId IS NOT $parentId OR categoryName IS NOT $name OR comments IS NOT $comments);",
            ("$id", category.Id), ("$parentId", category.ParentId),
            ("$name", category.CategoryName), ("$comments", category.Comments),
            ("$now", EpochTime.UtcNowMillis()));
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
            "INSERT INTO Item (id, ownerDbId, parentId, itemName, comments, createdAt, updatedAt) VALUES ($id, $ownerDbId, $parentId, $name, $comments, $now, $now);",
            ("$id", item.Id), ("$ownerDbId", item.OwnerDbId), ("$parentId", item.ParentId),
            ("$name", item.ItemName), ("$comments", item.Comments),
            ("$now", EpochTime.UtcNowMillis())).ConfigureAwait(false);
        return item;
    }

    /// <summary>
    /// Saves the user-editable columns. Only a real change moves updatedAt; the owner and
    /// createdAt are never written.
    /// </summary>
    public Task UpdateItemAsync(Item item) =>
        ExecuteAsync(RecordType.Item,
            "UPDATE Item SET parentId = $parentId, itemName = $name, comments = $comments, updatedAt = $now " +
            "WHERE id = $id AND (parentId IS NOT $parentId OR itemName IS NOT $name OR comments IS NOT $comments);",
            ("$id", item.Id), ("$parentId", item.ParentId),
            ("$name", item.ItemName), ("$comments", item.Comments),
            ("$now", EpochTime.UtcNowMillis()));
}
