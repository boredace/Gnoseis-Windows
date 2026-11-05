/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;

namespace Gnoseis.App.ViewModels;

/// <summary>
/// One linked record's row: the type tile (initials for contacts), up to three lines of stored
/// data, and the record's own link count. Empty lines are left out.
/// </summary>
public sealed class LinkedRow
{
    public LinkedRow(LinkedRecordSummary record)
    {
        Record = record;
        switch (record.Type)
        {
            case RecordType.Note when record.Name.Length > 0:
                Title = record.Name;
                Body = record.FirstLine;
                break;
            case RecordType.Note:
                // A note without a title shows its text on two lines.
                Body = record.Text;
                BodyMaxLines = 2;
                BodyIsPrimary = true;
                break;
            case RecordType.Contact:
                Title = record.Name;
                Body = record.JobAndCompany;
                Accent = record.MainContact;
                break;
            case RecordType.Category or RecordType.Item:
                Title = record.Name;
                Path = record.ParentPath;
                Body = record.FirstLine;
                break;
            default:
                Title = record.Name;
                Body = record.FirstLine;
                break;
        }
    }

    public LinkedRecordSummary Record { get; }
    public string Id => Record.Id;
    public RecordType Type => Record.Type;

    /// <summary>The first line, semibold.</summary>
    public string Title { get; } = "";

    /// <summary>A category's or item's parent path, in caption style.</summary>
    public string Path { get; } = "";

    /// <summary>A note's first line of text, a contact's job and company, or the first comment line.</summary>
    public string Body { get; } = "";

    public int BodyMaxLines { get; } = 1;

    /// <summary>The body in the primary text colour (a note without a title), else secondary.</summary>
    public bool BodyIsPrimary { get; }

    /// <summary>A contact's main phone or e-mail, in the accent colour (not a link inside the row).</summary>
    public string Accent { get; } = "";

    public bool HasTitle => Title.Length > 0;
    public bool HasPath => Path.Length > 0;
    public bool HasBody => Body.Length > 0;
    public bool HasAccent => Accent.Length > 0;
    public bool IsPerson => Type == RecordType.Contact;
    public bool ShowTile => Type != RecordType.Contact;

    public string LinkCountText => Record.LinkCount.ToString(CultureInfo.CurrentCulture);

    public string LinkCountAutomationName => Record.LinkCount == 1 ? "1 linked record" : $"{Record.LinkCount} linked records";

    /// <summary>The name used in messages ("Unlinked …"): as "Will be linked to" names a record.</summary>
    public string DisplayName => Record.Name.Length > 0
        ? Record.Name
        : Type == RecordType.Note && Record.NoteDate != 0 ? NoteDay.Format(Record.NoteDate) : Type.DisplayName();

    /// <summary>Text read by screen readers.</summary>
    public string AutomationName =>
        string.Join(". ", new[] { $"{Type.DisplayName()}: {(HasTitle ? Title : DisplayName)}", Path, Body, Accent, LinkCountAutomationName }
            .Where(s => s.Length > 0));

    public RecordRef ToRef() => new(Type, Id);
}

/// <summary>A note's day as the linked lists show it.</summary>
public static class NoteDay
{
    public static DateOnly Of(long createDate) => EpochTime.ToUtcDate(createDate);

    public static string Format(long createDate) => Of(createDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Weekday(long createDate) => Of(createDate).ToString("ddd", CultureInfo.CurrentCulture);
}

/// <summary>
/// A card of rows: one day of notes (with the date on the left and the short weekday on the
/// right), or some records of another type (no header).
/// </summary>
public sealed class LinkedCard(IReadOnlyList<LinkedRow> rows, string dayHeader = "", string weekday = "")
{
    public IReadOnlyList<LinkedRow> Rows { get; } = rows;
    public string DayHeader { get; } = dayHeader;
    public string Weekday { get; } = weekday;
    public bool HasDayHeader => DayHeader.Length > 0;

    /// <summary>Notes of one day, in the order given (newest first), one card per day.</summary>
    public static List<LinkedCard> ByDay(IEnumerable<LinkedRecordSummary> notes) =>
        notes
            .GroupBy(n => NoteDay.Of(n.NoteDate))
            .Select(g => new LinkedCard(
                g.Select(n => new LinkedRow(n)).ToList(),
                NoteDay.Format(g.First().NoteDate),
                NoteDay.Weekday(g.First().NoteDate)))
            .ToList();

    /// <summary>An estimate of the card's height, used to balance columns.</summary>
    public double EstimatedHeight =>
        (HasDayHeader ? 36 : 0) + 16 + Rows.Sum(r => 24 + 20 * ((r.HasTitle ? 1 : 0) + (r.HasPath ? 1 : 0) + (r.HasBody ? r.BodyMaxLines : 0) + (r.HasAccent ? 1 : 0)));
}

/// <summary>One linked type in All: a header with its count, its latest records, and "Show all n".</summary>
public sealed class LinkedAllGroup(LinkedTypeGroup group)
{
    public RecordType Type { get; } = group.Type;
    public string Header { get; } = group.Type.PluralDisplayName();
    public string CountText { get; } = $"({group.Count.ToString(CultureInfo.CurrentCulture)})";
    public string HeaderAutomationName => $"{Header} ({group.Count})";

    public IReadOnlyList<LinkedCard> Cards { get; } =
        group.Type == RecordType.Note ? LinkedCard.ByDay(group.Latest) : [new LinkedCard(group.Latest.Select(r => new LinkedRow(r)).ToList())];

    public bool HasShowAll { get; } = group.HasMore;

    /// <summary>"Show all 8 notes".</summary>
    public string ShowAllText { get; } = $"Show all {group.Count.ToString(CultureInfo.CurrentCulture)} {group.Type.PluralDisplayName().ToLower(CultureInfo.CurrentCulture)}";

    public double EstimatedHeight => 36 + Cards.Sum(c => c.EstimatedHeight + 8) + (HasShowAll ? 36 : 0);
}
