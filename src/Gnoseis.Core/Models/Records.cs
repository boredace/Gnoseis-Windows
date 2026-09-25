/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Models;

// Column names and types match the Android Room entities exactly, so the same SQLite file
// can be used by both apps. Ids are lowercase GUID strings (java.util.UUID.toString format).

public sealed record Note
{
    public string Id { get; init; } = NewId();
    public string OwnerDbId { get; init; } = "db1";
    public string? Title { get; init; }
    public string? TextContents { get; init; }

    /// <summary>Epoch milliseconds (UTC).</summary>
    public long CreateDateTime { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Epoch milliseconds of the day the note belongs to. Used to group the note list.</summary>
    public long CreateDate { get; init; } = EpochTime.LocalMidnightNow();

    public static string NewId() => Guid.NewGuid().ToString();
}

public sealed record Contact
{
    public string Id { get; init; } = Note.NewId();
    public string OwnerDbId { get; init; } = "db1";
    public string NameLast { get; init; } = "";
    public string? NameFirst { get; init; }
    public string? JobTitle { get; init; }
    public string? Company { get; init; }
    public string? PhoneMain { get; init; }
    public string? PhoneMobile { get; init; }
    public string? PhoneHome { get; init; }
    public string? PhoneWork { get; init; }
    public string? EmailMain { get; init; }
    public string? EmailMobile { get; init; }
    public string? EmailHome { get; init; }
    public string? EmailWork { get; init; }
    public string? Comments { get; init; }

    /// <summary>"Last, First" as shown in lists and search results.</summary>
    public string DisplayName => string.IsNullOrEmpty(NameFirst) ? NameLast : $"{NameLast}, {NameFirst}";
}

public sealed record Organization
{
    public string Id { get; init; } = Note.NewId();
    public string OwnerDbId { get; init; } = "db1";
    public string OrganizationName { get; init; } = "";
    public string? Comments { get; init; }
}

public sealed record Category
{
    public string Id { get; init; } = Note.NewId();
    public string OwnerDbId { get; init; } = "db1";
    public string? ParentId { get; init; }
    public string CategoryName { get; init; } = "New Category";
    public string? Comments { get; init; }
}

public sealed record Item
{
    public string Id { get; init; } = Note.NewId();
    public string OwnerDbId { get; init; } = "db1";
    public string? ParentId { get; init; }
    public string ItemName { get; init; } = "";
    public string? Comments { get; init; }
}

public sealed record LinkedRecord
{
    public string Id { get; init; } = Note.NewId();
    public required string OwnerDbId { get; init; }
    public required string Record1Id { get; init; }
    public required string Record2Id { get; init; }
    public required int Record1TypeId { get; init; }
    public required int Record2TypeId { get; init; }
}

public sealed record LinkedRecordTypeCount(int RecordTypeId, int Count)
{
    public RecordType RecordType => (RecordType)RecordTypeId;
}

public sealed record SearchResult(string RecordId, int RecordTypeId, string RecordTitle)
{
    public RecordType RecordType => (RecordType)RecordTypeId;
}

/// <summary>All records linked to one record, split by type.</summary>
public sealed record LinkedRecords(
    IReadOnlyList<Note> Notes,
    IReadOnlyList<Contact> Contacts,
    IReadOnlyList<Organization> Organizations,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Item> Items)
{
    public static LinkedRecords Empty { get; } = new([], [], [], [], []);

    public int TotalCount => Notes.Count + Contacts.Count + Organizations.Count + Categories.Count + Items.Count;
}
