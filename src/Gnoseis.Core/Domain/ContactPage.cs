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

/// <summary>The contact page's layouts, by the width of the record pane.</summary>
public enum ContactPageBand
{
    /// <summary>Under 860: one column with History, Linked records and About tabs.</summary>
    S,

    /// <summary>860–1099: the history and a side column of 240.</summary>
    M,

    /// <summary>1100–1699: the history up to 760 wide and a side column of 320.</summary>
    L,

    /// <summary>1700–2599: the history, a reading pane and a side column of 320.</summary>
    XL,

    /// <summary>From 2600: content capped at 2700, the side column in two columns.</summary>
    XXL,
}

/// <summary>The kind of a phone or e-mail field, which picks the chip's glyph and action.</summary>
public enum ContactChipKind
{
    Phone,
    Mobile,
    Email,
}

/// <summary>A fact the contact doesn't have yet, offered as a dashed chip that opens Edit at that field.</summary>
public enum MissingContactFact
{
    JobTitle,
    Company,
    Phone,
    Email,
}

/// <summary>One filled phone or e-mail field of a contact, as its chip shows it.</summary>
/// <param name="Field">The field's name: PhoneMain, PhoneMobile, …</param>
/// <param name="Value">The trimmed value.</param>
/// <param name="Label">"main", "mobile", "work" or "home".</param>
public sealed record ContactChip(string Field, ContactChipKind Kind, string Value, string Label)
{
    public bool IsPhone => Kind != ContactChipKind.Email;

    /// <summary>tel:&lt;number without spaces&gt; or mailto:&lt;address&gt;; null when the value doesn't make a valid URI.</summary>
    public Uri? Target
    {
        get
        {
            var text = IsPhone ? string.Concat(Value.Where(c => !char.IsWhiteSpace(c))) : Value;
            return text.Length > 0 && Uri.TryCreate((IsPhone ? "tel:" : "mailto:") + text, UriKind.Absolute, out var uri) ? uri : null;
        }
    }

    /// <summary>"Call +31 30 816 8888, work" or "Send e-mail to emma@example.org, work".</summary>
    public string AutomationName => IsPhone ? $"Call {Value}, {Label}" : $"Send e-mail to {Value}, {Label}";

    /// <summary>The chip's main action in its context menu: "Call" or "Send e-mail".</summary>
    public string ActionName => IsPhone ? "Call" : "Send e-mail";

    /// <summary>The line Copy contact details writes: "Phone (work): +31 30 816 8888".</summary>
    public string CopyLine => $"{(IsPhone ? "Phone" : "E-mail")} ({Label}): {Value}";
}

/// <summary>The chips shown before "+n more", and the ones behind it.</summary>
public sealed record ContactChipFolding(IReadOnlyList<ContactChip> Shown, IReadOnlyList<ContactChip> Folded)
{
    public bool HasMore => Folded.Count > 0;

    /// <summary>"+2 more".</summary>
    public string MoreText => $"+{Folded.Count} more";
}

/// <summary>How the contact's company relates to the organizations linked to it.</summary>
/// <param name="Company">The trimmed company; empty when none is set.</param>
/// <param name="Organization">The linked organization with the company's name, if there is one.</param>
public sealed record CompanyLink(string Company, LinkedRecordSummary? Organization)
{
    public bool HasCompany => Company.Length > 0;

    /// <summary>The company is a link to <see cref="Organization"/>.</summary>
    public bool IsLinked => Organization != null;

    /// <summary>The company is set and no linked organization has its name: offer "Link as organization".</summary>
    public bool CanLinkAsOrganization => HasCompany && Organization == null;
}

/// <summary>
/// What the contact page shows that doesn't depend on the UI: the header line, the company link,
/// the contact chips and their folding, the text Copy contact details writes, and the layout band.
/// </summary>
public static class ContactPage
{
    public const double MediumWidth = 860;
    public const double LargeWidth = 1100;
    public const double ExtraLargeWidth = 1700;
    public const double WidestWidth = 2600;

    /// <summary>The layout for a record pane of <paramref name="paneWidth"/> effective pixels.</summary>
    public static ContactPageBand BandFor(double paneWidth) => paneWidth switch
    {
        < MediumWidth => ContactPageBand.S,
        < LargeWidth => ContactPageBand.M,
        < ExtraLargeWidth => ContactPageBand.L,
        < WidestWidth => ContactPageBand.XL,
        _ => ContactPageBand.XXL,
    };

    /// <summary>
    /// The line under the name: "Owner at Ruby Eye Clinic", "Owner", "At Ruby Eye Clinic", or
    /// "Contact" (shown as a placeholder) when neither is set.
    /// </summary>
    public static (string Text, bool IsPlaceholder) HeaderLine(Contact contact)
    {
        var job = contact.JobTitle?.Trim() ?? "";
        var company = contact.Company?.Trim() ?? "";
        return (job.Length > 0, company.Length > 0) switch
        {
            (true, true) => ($"{job} at {company}", false),
            (true, false) => (job, false),
            (false, true) => ($"At {company}", false),
            _ => (RecordType.Contact.DisplayName(), true),
        };
    }

    /// <summary>
    /// The company and the linked organization with the same name: compared trimmed, ignoring case,
    /// with runs of spaces as one (as search compares names).
    /// </summary>
    public static CompanyLink Company(Contact contact, IEnumerable<LinkedRecordSummary> linkedRecords)
    {
        var company = contact.Company?.Trim() ?? "";
        var organization = company.Length == 0
            ? null
            : linkedRecords.FirstOrDefault(r => r.Type == RecordType.Organization && CreateFromSearch.MatchesExactly(r.Name, company));
        return new CompanyLink(company, organization);
    }

    /// <summary>The name the note box addresses: the first name, or the last name when there is none.</summary>
    public static string ShortName(Contact contact) =>
        !string.IsNullOrWhiteSpace(contact.NameFirst) ? contact.NameFirst.Trim() : contact.NameLast.Trim();

    /// <summary>One chip per filled phone and e-mail field: phones main, mobile, work, home, then e-mails main, work, home, mobile.</summary>
    public static IReadOnlyList<ContactChip> Chips(Contact contact)
    {
        var chips = new List<ContactChip>();
        Add(chips, nameof(Contact.PhoneMain), ContactChipKind.Phone, contact.PhoneMain, "main");
        Add(chips, nameof(Contact.PhoneMobile), ContactChipKind.Mobile, contact.PhoneMobile, "mobile");
        Add(chips, nameof(Contact.PhoneWork), ContactChipKind.Phone, contact.PhoneWork, "work");
        Add(chips, nameof(Contact.PhoneHome), ContactChipKind.Phone, contact.PhoneHome, "home");
        Add(chips, nameof(Contact.EmailMain), ContactChipKind.Email, contact.EmailMain, "main");
        Add(chips, nameof(Contact.EmailWork), ContactChipKind.Email, contact.EmailWork, "work");
        Add(chips, nameof(Contact.EmailHome), ContactChipKind.Email, contact.EmailHome, "home");
        Add(chips, nameof(Contact.EmailMobile), ContactChipKind.Email, contact.EmailMobile, "mobile");
        return chips;
    }

    private static void Add(List<ContactChip> chips, string field, ContactChipKind kind, string? value, string label)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            chips.Add(new ContactChip(field, kind, value.Trim(), label));
        }
    }

    /// <summary>Chips shown before "+n more": 2 in S, 3 in M, 4 in L, all in XL and XXL.</summary>
    public static int ShownChipCount(ContactPageBand band) => band switch
    {
        ContactPageBand.S => 2,
        ContactPageBand.M => 3,
        ContactPageBand.L => 4,
        _ => int.MaxValue,
    };

    public static ContactChipFolding Fold(IReadOnlyList<ContactChip> chips, ContactPageBand band)
    {
        var shown = ShownChipCount(band);
        return chips.Count <= shown
            ? new ContactChipFolding(chips, [])
            : new ContactChipFolding(chips.Take(shown).ToList(), chips.Skip(shown).ToList());
    }

    /// <summary>The dashed chips, in order: job title, company, phone (no phone field filled), e-mail (no e-mail field filled).</summary>
    public static IReadOnlyList<MissingContactFact> MissingFacts(Contact contact)
    {
        var chips = Chips(contact);
        var missing = new List<MissingContactFact>();
        if (string.IsNullOrWhiteSpace(contact.JobTitle))
        {
            missing.Add(MissingContactFact.JobTitle);
        }
        if (string.IsNullOrWhiteSpace(contact.Company))
        {
            missing.Add(MissingContactFact.Company);
        }
        if (!chips.Any(c => c.IsPhone))
        {
            missing.Add(MissingContactFact.Phone);
        }
        if (!chips.Any(c => !c.IsPhone))
        {
            missing.Add(MissingContactFact.Email);
        }
        return missing;
    }

    /// <summary>The dashed chip's text: "Job title", "Company", "Phone", "E-mail".</summary>
    public static string Label(MissingContactFact fact) => fact switch
    {
        MissingContactFact.JobTitle => "Job title",
        MissingContactFact.Company => "Company",
        MissingContactFact.Phone => "Phone",
        _ => "E-mail",
    };

    /// <summary>"Add job title", "Add e-mail".</summary>
    public static string AutomationName(MissingContactFact fact) => $"Add {Label(fact).ToLowerInvariant()}";

    /// <summary>The contact field Edit focuses for a missing fact (the first phone or e-mail field).</summary>
    public static string FieldOf(MissingContactFact fact) => fact switch
    {
        MissingContactFact.JobTitle => nameof(Contact.JobTitle),
        MissingContactFact.Company => nameof(Contact.Company),
        MissingContactFact.Phone => nameof(Contact.PhoneMain),
        _ => nameof(Contact.EmailMain),
    };

    /// <summary>
    /// Copy contact details: the name, the job title and company line (when set), then one line per
    /// filled phone and e-mail field in chip order. No comments, no links.
    /// </summary>
    public static string CopyText(Contact contact)
    {
        var lines = new List<string>();
        var name = RecordText.ContactFullName(contact);
        if (name.Length > 0)
        {
            lines.Add(name);
        }
        var (header, isPlaceholder) = HeaderLine(contact);
        if (!isPlaceholder)
        {
            lines.Add(header);
        }
        lines.AddRange(Chips(contact).Select(c => c.CopyLine));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Rows a side column section shows before "Show all": 5 in S, M and L, 6 in XL, 8 in XXL.</summary>
    public static int SideRowCount(ContactPageBand band) => band switch
    {
        ContactPageBand.XL => 6,
        ContactPageBand.XXL => 8,
        _ => 5,
    };

    /// <summary>The side column's types, in their fixed order.</summary>
    public static IReadOnlyList<RecordType> SideTypes { get; } = [RecordType.Organization, RecordType.Category, RecordType.Item];

    /// <summary>A side column section's rows: latest linked first, links without a time last, then by name.</summary>
    public static IReadOnlyList<LinkedRecordSummary> SideOrder(IEnumerable<LinkedRecordSummary> records) =>
        records
            .OrderBy(r => r.LinkedAt == null)
            .ThenByDescending(r => r.LinkedAt ?? 0)
            .ThenBy(r => r.SortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The note box's day and time button: "Today, 15:40", "Yesterday, 09:05", "Tue 26 May, 15:40",
    /// with the year for another year.
    /// </summary>
    public static string DayAndTimeText(DateOnly day, TimeSpan time, DateOnly today, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var dayText = day == today ? "Today"
            : day == today.AddDays(-1) ? "Yesterday"
            : day.ToString(day.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy", culture);
        return $"{dayText}, {new DateTime(1, 1, 1).Add(time).ToString("HH:mm", culture)}";
    }
}

/// <summary>
/// A note in the contact's history, with its text and the records linked to it.
/// </summary>
public sealed record HistoryNote
{
    public required string Id { get; init; }

    /// <summary>The stored title, trimmed; empty when the note has none.</summary>
    public string Title { get; init; } = "";

    public string Text { get; init; } = "";
    public long CreateDate { get; init; }
    public long CreateDateTime { get; init; }

    /// <summary>Every record linked to the note, the contact included, by type then name.</summary>
    public IReadOnlyList<LinkedRecordSummary> Links { get; init; } = [];
}

/// <summary>Filter and month header thresholds, order, rows, filter and year chips of the contact's history.</summary>
public static class ContactHistory
{
    /// <summary>The filter box from this many notes on (more than 5).</summary>
    public const int FilterThreshold = 6;

    /// <summary>Month headers and year chips from this many notes on.</summary>
    public const int GroupingThreshold = 13;

    /// <summary>Notes shown at first, and loaded by each "Show older".</summary>
    public const int PageSize = 50;

    /// <summary>Other linked records named on a row before "+n".</summary>
    public const int RowLinkCount = 3;

    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>Newest first: by createDate, then createDateTime, then title.</summary>
    public static IReadOnlyList<HistoryNote> Order(IEnumerable<HistoryNote> notes) =>
        notes
            .OrderByDescending(n => n.CreateDate)
            .ThenByDescending(n => n.CreateDateTime)
            .ThenBy(n => RowTitle(n), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>True from 6 notes (more than 5): the filter box.</summary>
    public static bool ShowsFilter(int noteCount) => noteCount >= FilterThreshold;

    /// <summary>True from 13 notes: month headers and (over more than one year) year chips.</summary>
    public static bool IsGrouped(int noteCount) => noteCount >= GroupingThreshold;

    /// <summary>The note's day (its createDate, read in UTC, as the note list shows it).</summary>
    public static DateOnly Day(HistoryNote note) => EpochTime.ToUtcDate(note.CreateDate);

    private static string[] Lines(string text) =>
        text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The row's title: the note's title, or its first line of text when it has none.</summary>
    public static string RowTitle(HistoryNote note) =>
        note.Title.Length > 0 ? note.Title : Lines(note.Text).FirstOrDefault() ?? "";

    /// <summary>The row's line of text: the first line when the note has a title, else the second.</summary>
    public static string RowText(HistoryNote note)
    {
        var lines = Lines(note.Text);
        var index = note.Title.Length > 0 ? 0 : 1;
        return lines.Length > index ? lines[index] : "";
    }

    /// <summary>The note's other linked records: up to 3 names joined by " · ", then "+n"; the contact left out.</summary>
    public static string RowLinks(HistoryNote note, string contactId)
    {
        var names = note.Links.Where(l => l.Id != contactId).Select(l => l.Name.Length > 0 ? l.Name : l.Type.DisplayName()).ToList();
        var text = string.Join(" · ", names.Take(RowLinkCount));
        return names.Count > RowLinkCount ? $"{text} +{names.Count - RowLinkCount}" : text;
    }

    /// <summary>"26 May".</summary>
    public static string DayText(HistoryNote note, CultureInfo? culture = null) =>
        Day(note).ToString("d MMM", culture ?? CultureInfo.CurrentCulture);

    /// <summary>"Tue · 2026".</summary>
    public static string WeekdayAndYearText(HistoryNote note, CultureInfo? culture = null) =>
        $"{Day(note).ToString("ddd", culture ?? CultureInfo.CurrentCulture)} · {Day(note).Year}";

    /// <summary>"Oct 2026".</summary>
    public static string MonthText(DateOnly day, CultureInfo? culture = null) =>
        day.ToString("MMM yyyy", culture ?? CultureInfo.CurrentCulture);

    /// <summary>The note's time of day, when it has one (createDateTime after its day's midnight).</summary>
    public static TimeSpan? TimeOfDay(HistoryNote note) =>
        note.CreateDateTime == note.CreateDate ? null : EpochTime.ToUtc(note.CreateDateTime).UtcDateTime.TimeOfDay;

    /// <summary>The reading pane's caption: "Note · Tue 26 May 2026, 15:00", the time only when the note has one.</summary>
    public static string ReadingCaption(HistoryNote note, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var text = $"Note · {Day(note).ToString("ddd d MMM yyyy", culture)}";
        return TimeOfDay(note) is { } time ? $"{text}, {new DateTime(1, 1, 1).Add(time).ToString("HH:mm", culture)}" : text;
    }

    /// <summary>Whether the note's title or text contains <paramref name="filter"/>, ignoring case. A blank filter matches all.</summary>
    public static bool Matches(HistoryNote note, string? filter)
    {
        var text = filter?.Trim() ?? "";
        return text.Length == 0
            || note.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || note.Text.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>The notes per year, newest year first; empty unless the notes span more than one year.</summary>
    public static IReadOnlyList<(int Year, int Count)> YearCounts(IEnumerable<HistoryNote> notes)
    {
        var years = notes.GroupBy(n => Day(n).Year).Select(g => (Year: g.Key, Count: g.Count())).OrderByDescending(y => y.Year).ToList();
        return years.Count > 1 ? years : [];
    }

    /// <summary>The note's month (first of the month), the key of its month header.</summary>
    public static DateOnly Month(HistoryNote note) => new(Day(note).Year, Day(note).Month, 1);

    /// <summary>The note's name where one is needed: its row title, or its day (yyyy-MM-dd) when it has no title or text.</summary>
    public static string FullTitle(HistoryNote note) => RowTitle(note) is { Length: > 0 } title ? title : NoteDayFallback(note);

    private static string NoteDayFallback(HistoryNote note) => Day(note).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
