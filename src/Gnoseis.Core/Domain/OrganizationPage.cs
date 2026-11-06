/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>
/// One linked contact of an organization: the contact's details plus shared note metadata.
/// </summary>
public sealed record PersonSummary(
    string Id,
    string Name,
    string SortKey,
    string JobTitle,
    string MainPhone,
    string Email,
    int SharedNoteCount,
    DateOnly? LatestSharedNoteDay);

/// <summary>
/// A contact whose company field matches the organization's name but who is not yet linked.
/// </summary>
public sealed record NotLinkedContact(string Id, string Name, string SortKey);

/// <summary>
/// The side column's types for an organization page: categories, items, other organizations.
/// Contacts are shown in the People column, not repeated here.
/// </summary>
public static class OrganizationPage
{
    /// <summary>The side column's types, in their fixed order.</summary>
    public static IReadOnlyList<RecordType> SideTypes { get; } =
        [RecordType.Category, RecordType.Item, RecordType.Organization];

    /// <summary>
    /// The line under the name: "Organization · 7 people · 64 notes · 21 other linked".
    /// Each part after the type is left out when its count is 0. Singular forms for count 1.
    /// When both notes and other linked are 0: "Organization · no notes or links yet".
    /// </summary>
    public static (string Text, bool IsPlaceholder) HeaderLine(int peopleCount, int noteCount, int otherLinkedCount)
    {
        var parts = new List<string>();
        parts.Add(RecordType.Organization.DisplayName());

        if (peopleCount > 0)
        {
            parts.Add($"{peopleCount} {(peopleCount == 1 ? "person" : "people")}");
        }

        if (noteCount > 0)
        {
            parts.Add($"{noteCount} {(noteCount == 1 ? "note" : "notes")}");
        }

        if (otherLinkedCount > 0)
        {
            parts.Add($"{otherLinkedCount} other linked");
        }

        if (peopleCount == 0 && noteCount == 0 && otherLinkedCount == 0)
        {
            return ("Organization · no notes or links yet", true);
        }

        return (string.Join(" · ", parts), false);
    }

    /// <summary>
    /// Copy organization details: the name, then an empty line and the comments when there are any.
    /// </summary>
    public static string CopyText(Organization org)
    {
        var lines = new List<string>();
        var name = org.OrganizationName?.Trim() ?? "";
        if (name.Length > 0)
        {
            lines.Add(name);
        }
        var comments = org.Comments?.Trim();
        if (!string.IsNullOrWhiteSpace(comments))
        {
            lines.Add("");
            lines.Add(comments);
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Order people for the People column: contacts with a shared note first (latest shared note
    /// descending), then contacts with no shared notes, each group sorted by name.
    /// </summary>
    public static IReadOnlyList<PersonSummary> PeopleOrder(IEnumerable<PersonSummary> people) =>
        people
            .OrderByDescending(p => p.LatestSharedNoteDay.HasValue)
            .ThenByDescending(p => p.LatestSharedNoteDay ?? new DateOnly(1, 1, 1))
            .ThenBy(p => p.SortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Filter notes to those linked to both the organization and the given person.
    /// A note is shared when it links to both record IDs.
    /// </summary>
    public static IReadOnlyList<HistoryNote> PeopleFilter(IEnumerable<HistoryNote> notes, PersonSummary person)
    {
        var personId = person.Id;
        return notes
            .Where(n => n.Links.Any(l => l.Id == personId))
            .ToList();
    }

    /// <summary>
    /// Contacts whose company field matches the organization's name but are not yet linked:
    /// trimmed, case-insensitive, runs of spaces as one. At most 3 shown, with totalCount for "n more".
    /// </summary>
    public static (IReadOnlyList<NotLinkedContact> Shown, int TotalCount) NotLinkedContacts(
        Organization org,
        IReadOnlyList<PersonSummary> linkedPeople,
        IReadOnlyList<Contact> contacts)
    {
        var orgName = Normalize(org.OrganizationName);
        var linkedIds = new HashSet<string>(linkedPeople.Select(p => p.Id), StringComparer.Ordinal);

        var notLinked = contacts
            .Where(c => !linkedIds.Contains(c.Id))
            .Select(c => new { Contact = c, CompanyNorm = Normalize(c.Company) })
            .Where(x => x.CompanyNorm.Length > 0 && x.CompanyNorm == orgName)
            .Select(x => new NotLinkedContact(
                x.Contact.Id,
                RecordText.ContactFullName(x.Contact),
                $"{x.Contact.NameLast.Trim()}\u0001{(x.Contact.NameFirst?.Trim() ?? "")}"))
            .ToList();

        notLinked.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.SortKey, b.SortKey));

        var totalCount = notLinked.Count;
        var shown = totalCount > 3 ? notLinked.Take(3).ToList() : notLinked;
        return (shown, totalCount);
    }

    private static string Normalize(string? text) => CreateFromSearch.Normalize(text);
}
