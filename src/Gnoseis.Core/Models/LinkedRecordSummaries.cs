/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Models;

/// <summary>
/// One record linked to the record shown, with the stored fields its row shows. Every text is
/// trimmed, and empty when the record has nothing to show there.
/// </summary>
public sealed record LinkedRecordSummary
{
    public required string Id { get; init; }
    public required RecordType Type { get; init; }

    /// <summary>
    /// The record's name: a note's title (empty if it has none), a contact's "First Last", or the
    /// organization, category or item name.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// A note's first line of text, or the first line of the comments of an organization,
    /// category or item. Empty for contacts.
    /// </summary>
    public string FirstLine { get; init; } = "";

    /// <summary>A note's whole text on one line (shown when the note has no title). Empty for other types.</summary>
    public string Text { get; init; } = "";

    /// <summary>A contact's "job title · company", or either one alone.</summary>
    public string JobAndCompany { get; init; } = "";

    /// <summary>A contact's main phone, or the main e-mail if there is no phone.</summary>
    public string MainContact { get; init; } = "";

    /// <summary>The names of a category's or item's parents, top first, joined by " › ". Empty for a top-level record.</summary>
    public string ParentPath { get; init; } = "";

    /// <summary>Number of records linked to this record (each linked record counted once).</summary>
    public int LinkCount { get; init; }

    /// <summary>UTC epoch ms of the link to the record shown; null for links made before schema v3.</summary>
    public long? LinkedAt { get; init; }

    /// <summary>A note's day (createDate) and time (createDateTime); 0 for other types.</summary>
    public long NoteDate { get; init; }

    public long NoteDateTime { get; init; }

    /// <summary>What the type's list sorts by: a contact's last and first name, or the name.</summary>
    public string SortName { get; init; } = "";
}

/// <summary>The records of one type linked to the record shown.</summary>
public sealed record LinkedTypeGroup(RecordType Type, IReadOnlyList<LinkedRecordSummary> Records, IReadOnlyList<LinkedRecordSummary> Latest)
{
    /// <summary>Records shown per type in All.</summary>
    public const int LatestCount = 3;

    public int Count => Records.Count;

    /// <summary>True when All shows only some of the type's records ("Show all n").</summary>
    public bool HasMore => Count > LatestCount;
}

/// <summary>
/// The records linked to one record, by type: the types sorted by link count, most first (ties in
/// the fixed order Note, Contact, Organization, Category, Item); in each, the records sorted as
/// the type's list and the 3 latest for All.
/// </summary>
public sealed class LinkedRecordSet
{
    // As the lists' SQL (COLLATE NOCASE): ordinal, ignoring case. Contacts sort by "lastfirst".
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    public LinkedRecordSet(IEnumerable<LinkedRecordSummary> records)
    {
        Groups = records
            .Where(r => RecordTypeExtensions.UserTypes.Contains(r.Type))
            .GroupBy(r => r.Type)
            .Select(g => new LinkedTypeGroup(g.Key, SortForList(g.Key, g), Latest(g.Key, g)))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => (int)g.Type)
            .ToList();
    }

    public static LinkedRecordSet Empty { get; } = new([]);

    public IReadOnlyList<LinkedTypeGroup> Groups { get; }

    public int TotalCount => Groups.Sum(g => g.Count);

    public LinkedTypeGroup? Find(RecordType type) => Groups.FirstOrDefault(g => g.Type == type);

    /// <summary>
    /// The type's list: notes newest first (by day, then time), contacts by last and first name,
    /// the other types by name.
    /// </summary>
    private static List<LinkedRecordSummary> SortForList(RecordType type, IEnumerable<LinkedRecordSummary> records) =>
        type == RecordType.Note
            ? NotesNewestFirst(records).ToList()
            : records.OrderBy(r => r.SortName, NameComparer).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// The 3 latest: notes by note day, then time, newest first; other types by when they were
    /// linked, newest first, then the undated links (made before v3) by name.
    /// </summary>
    private static List<LinkedRecordSummary> Latest(RecordType type, IEnumerable<LinkedRecordSummary> records) =>
        (type == RecordType.Note
            ? NotesNewestFirst(records)
            : records
                .OrderBy(r => r.LinkedAt == null)
                .ThenByDescending(r => r.LinkedAt ?? 0)
                .ThenBy(r => r.SortName, NameComparer)
                .ThenBy(r => r.Id, StringComparer.Ordinal))
        .Take(LinkedTypeGroup.LatestCount)
        .ToList();

    private static IOrderedEnumerable<LinkedRecordSummary> NotesNewestFirst(IEnumerable<LinkedRecordSummary> notes) =>
        notes
            .OrderByDescending(n => n.NoteDate)
            .ThenByDescending(n => n.NoteDateTime)
            .ThenBy(n => n.Id, StringComparer.Ordinal);
}

/// <summary>The record a new record will be linked to: its type and the name shown for it.</summary>
public sealed record LinkSource(RecordType Type, string Name);
