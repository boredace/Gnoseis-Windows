/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;
using Microsoft.Data.Sqlite;

namespace Gnoseis.Core.Data;

/// <summary>
/// Builds <see cref="LinkedRecordSummary"/> rows (the fields a record row shows) from full table
/// rows. Shared by the record page's linked records and Home.
/// </summary>
internal static class RecordSummaries
{
    /// <summary>The table of a user record type.</summary>
    public static string TableOf(RecordType type) => type switch
    {
        RecordType.Note => "Note",
        RecordType.Contact => "Contact",
        RecordType.Organization => "Organization",
        RecordType.Category => "Category",
        RecordType.Item => "Item",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// Maps a row of <paramref name="type"/>'s table. A category's or item's parent is added to
    /// <paramref name="withParents"/>, for <see cref="AddParentPaths"/>.
    /// </summary>
    public static LinkedRecordSummary Map(RecordType type, SqliteDataReader r, List<(RecordType Type, string Id, string ParentId)> withParents)
    {
        switch (type)
        {
            case RecordType.Note:
                var note = RepositoryBase.MapNote(r);
                return new LinkedRecordSummary
                {
                    Id = note.Id,
                    Type = RecordType.Note,
                    Name = note.Title?.Trim() ?? "",
                    FirstLine = RecordText.FirstLine(note.TextContents),
                    Text = RecordText.OneLine(note.TextContents),
                    NoteDate = note.CreateDate,
                    NoteDateTime = note.CreateDateTime,
                };
            case RecordType.Contact:
                var contact = RepositoryBase.MapContact(r);
                return new LinkedRecordSummary
                {
                    Id = contact.Id,
                    Type = RecordType.Contact,
                    Name = RecordText.ContactFullName(contact),
                    JobAndCompany = RecordText.JobAndCompany(contact),
                    MainContact = RecordText.MainContact(contact),
                    SortName = $"{contact.NameLast.Trim()}\u0001{contact.NameFirst?.Trim()}",
                };
            case RecordType.Organization:
                var organization = RepositoryBase.MapOrganization(r);
                return Named(organization.Id, RecordType.Organization, organization.OrganizationName, organization.Comments);
            case RecordType.Category:
                var category = RepositoryBase.MapCategory(r);
                if (!string.IsNullOrEmpty(category.ParentId))
                {
                    withParents.Add((RecordType.Category, category.Id, category.ParentId));
                }
                return Named(category.Id, RecordType.Category, category.CategoryName, category.Comments);
            case RecordType.Item:
                var item = RepositoryBase.MapItem(r);
                if (!string.IsNullOrEmpty(item.ParentId))
                {
                    withParents.Add((RecordType.Item, item.Id, item.ParentId));
                }
                return Named(item.Id, RecordType.Item, item.ItemName, item.Comments);
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    /// <summary>Sets the parent path of each record in <paramref name="withParents"/>: its parents' names, top first, joined by " › ".</summary>
    public static void AddParentPaths(SqliteConnection connection, List<(RecordType Type, string Id, string ParentId)> withParents,
        List<LinkedRecordSummary> records)
    {
        foreach (var byType in withParents.GroupBy(p => p.Type))
        {
            var tree = RepositoryBase.LoadParentTree(connection, byType.Key, byType.Key == RecordType.Category ? "categoryName" : "itemName");
            foreach (var (type, id, parentId) in byType)
            {
                for (var i = 0; i < records.Count; i++)
                {
                    if (records[i].Type == type && records[i].Id == id)
                    {
                        records[i] = records[i] with { ParentPath = RepositoryBase.ParentPath(tree, id, parentId) };
                    }
                }
            }
        }
    }

    private static LinkedRecordSummary Named(string id, RecordType type, string name, string? comments) => new()
    {
        Id = id,
        Type = type,
        Name = name.Trim(),
        FirstLine = RecordText.FirstLine(comments),
        SortName = name.Trim(),
    };
}
