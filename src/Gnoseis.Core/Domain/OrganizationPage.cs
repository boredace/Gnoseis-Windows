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
/// One linked contact of an organization, as its card shows it: who it is, how to reach them, and
/// the notes linked to both the organization and the contact.
/// </summary>
/// <param name="SortKey">Last name, then first name, for ordering by name.</param>
/// <param name="Phone">The phone chip: the main phone, else the mobile, work or home number; null when none.</param>
/// <param name="Email">The e-mail chip: the main address, else the work, home or mobile one; null when none.</param>
public sealed record PersonSummary(
    string Id,
    string Name,
    string SortKey,
    string JobTitle,
    ContactChip? Phone,
    ContactChip? Email,
    int SharedNoteCount,
    DateOnly? LatestSharedNoteDay)
{
    /// <summary>Neither a phone nor an e-mail address: the card offers "Phone or e-mail".</summary>
    public bool HasNoWayToReach => Phone == null && Email == null;
}

/// <summary>
/// A contact whose company field matches the organization's name but who is not yet linked.
/// </summary>
public sealed record NotLinkedContact(string Id, string Name, string SortKey, string JobTitle);

/// <summary>
/// What the organization page shows that doesn't depend on the UI: the header line, copy text,
/// the people (linked contacts) with their shared notes, the person filter, and the contacts
/// whose company is the organization's name but who aren't linked yet.
/// </summary>
public static class OrganizationPage
{
    /// <summary>Contacts named in the not-linked box before "n more".</summary>
    public const int NotLinkedShownCount = 3;

    /// <summary>The side column's types, in their fixed order. Contacts are in the People column.</summary>
    public static IReadOnlyList<RecordType> SideTypes { get; } =
        [RecordType.Category, RecordType.Item, RecordType.Organization];

    /// <summary>
    /// The line under the name: "Organization · 7 people · 64 notes · 21 other linked".
    /// Each part after the type is left out when its count is 0. Singular forms for count 1.
    /// With nothing at all: "Organization · no notes or links yet".
    /// </summary>
    public static (string Text, bool IsPlaceholder) HeaderLine(int peopleCount, int noteCount, int otherLinkedCount)
    {
        if (peopleCount == 0 && noteCount == 0 && otherLinkedCount == 0)
        {
            return ("Organization · no notes or links yet", true);
        }

        var parts = new List<string> { RecordType.Organization.DisplayName() };
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
        return (string.Join(" · ", parts), false);
    }

    /// <summary>Copy organization details: the name, then an empty line and the comments when there are any.</summary>
    public static string CopyText(Organization org) => RecordHistoryPage.CopyText(org.OrganizationName, org.Comments);

    /// <summary>Cards shown in the People column before "Show all": 6 in S and L, 8 in XL, 10 in XXL; 5 compact rows in M.</summary>
    public static int ShownPeopleCount(RecordPageBand band) => band switch
    {
        RecordPageBand.M => 5,
        RecordPageBand.XL => 8,
        RecordPageBand.XXL => 10,
        _ => 6,
    };

    /// <summary>
    /// The people of the organization: each linked contact with the notes linked to both, in the
    /// People column's order (latest shared note first; contacts with no shared notes after, by name).
    /// </summary>
    public static IReadOnlyList<PersonSummary> People(IEnumerable<Contact> linkedContacts, IEnumerable<HistoryNote> history)
    {
        var notes = history.ToList();
        return PeopleOrder(linkedContacts.Select(contact =>
        {
            var shared = notes.Where(n => n.Links.Any(l => l.Id == contact.Id)).ToList();
            var chips = ContactPage.Chips(contact);
            return new PersonSummary(
                contact.Id,
                RecordText.ContactFullName(contact),
                SortKey(contact),
                contact.JobTitle?.Trim() ?? "",
                chips.FirstOrDefault(c => c.IsPhone),
                chips.FirstOrDefault(c => !c.IsPhone),
                shared.Count,
                shared.Count == 0 ? null : shared.Max(ContactHistory.Day));
        }));
    }

    /// <summary>
    /// The People column's order: contacts with a shared note first, latest shared note first; then
    /// contacts with no shared notes. Ties by last and first name.
    /// </summary>
    public static IReadOnlyList<PersonSummary> PeopleOrder(IEnumerable<PersonSummary> people) =>
        people
            .OrderByDescending(p => p.LatestSharedNoteDay.HasValue)
            .ThenByDescending(p => p.LatestSharedNoteDay ?? DateOnly.MinValue)
            .ThenBy(p => p.SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>"15 notes · last 15 Oct 2026", "1 note · last 3 Jan 2025", or "no notes yet".</summary>
    public static string NotesLine(PersonSummary person, CultureInfo? culture = null)
    {
        if (person.SharedNoteCount == 0 || person.LatestSharedNoteDay is not { } latest)
        {
            return "no notes yet";
        }
        var count = person.SharedNoteCount == 1 ? "1 note" : $"{person.SharedNoteCount} notes";
        return $"{count} · last {latest.ToString("d MMM yyyy", culture ?? CultureInfo.CurrentCulture)}";
    }

    /// <summary>The notes linked to both the organization (every note of its history) and the person.</summary>
    public static IReadOnlyList<HistoryNote> PeopleFilter(IEnumerable<HistoryNote> notes, string personId) =>
        notes.Where(n => n.Links.Any(l => l.Id == personId)).ToList();

    /// <summary>"15 of 64 notes" while a person is picked; otherwise "64 notes", "1 note" or "nothing yet".</summary>
    public static string HistoryCount(int shown, int total, bool personPicked) =>
        personPicked
            ? $"{shown} of {total} {(total == 1 ? "note" : "notes")}"
            : total switch { 0 => "nothing yet", 1 => "1 note", _ => $"{total} notes" };

    /// <summary>
    /// Contacts whose company is the organization's name (trimmed, ignoring case, runs of spaces as
    /// one) and who aren't linked to it, by name: the first 3, and how many there are in all.
    /// </summary>
    public static (IReadOnlyList<NotLinkedContact> Shown, int TotalCount) NotLinkedContacts(
        Organization org,
        IEnumerable<string> linkedContactIds,
        IEnumerable<Contact> contacts)
    {
        var orgName = CreateFromSearch.Normalize(org.OrganizationName);
        if (orgName.Length == 0)
        {
            return ([], 0);
        }
        var linkedIds = new HashSet<string>(linkedContactIds, StringComparer.Ordinal);
        var notLinked = contacts
            .Where(c => !linkedIds.Contains(c.Id) && CreateFromSearch.Normalize(c.Company) == orgName)
            .Select(c => new NotLinkedContact(c.Id, RecordText.ContactFullName(c), SortKey(c), c.JobTitle?.Trim() ?? ""))
            .OrderBy(c => c.SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        return (notLinked.Take(NotLinkedShownCount).ToList(), notLinked.Count);
    }

    private static string SortKey(Contact contact) => $"{contact.NameLast.Trim()}\u0001{contact.NameFirst?.Trim() ?? ""}";
}
