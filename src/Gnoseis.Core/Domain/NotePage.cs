/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>
/// What the note page shows that doesn't depend on the UI: the title, the day and time line, the
/// "Added" and "Last modified" caption, the text Copy note text writes, the side column's sections, and
/// the note an edit in place saves.
/// </summary>
public static class NotePage
{
    /// <summary>Shown in place of a missing title.</summary>
    public const string UntitledText = "Untitled note";

    /// <summary>Shown while editing when the note has neither a title nor text.</summary>
    public const string InvalidText = "A note needs a title or some text.";

    /// <summary>The side column's types, in their fixed order: contacts first.</summary>
    public static IReadOnlyList<RecordType> SideTypes { get; } =
        [RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item, RecordType.Note];

    /// <summary>The trimmed title, or "Untitled note" (shown as a placeholder) when it's null or blank.</summary>
    public static (string Text, bool IsPlaceholder) Title(Note note) =>
        string.IsNullOrWhiteSpace(note.Title) ? (UntitledText, true) : (note.Title.Trim(), false);

    /// <summary>The note's time of day in <paramref name="zone"/> (Windows' when null), as the note lists show it.</summary>
    public static TimeSpan TimeOfDay(Note note, TimeZoneInfo? zone = null) =>
        NoteDates.ShownTimeOfDay(note.CreateDateTime, zone ?? TimeZoneInfo.Local);

    /// <summary>
    /// The line under the title, in <paramref name="zone"/> (Windows' when null): the long date and the
    /// short time, "Thursday, October 15, 2026, 9:30 AM".
    /// </summary>
    public static string DayAndTimeLine(Note note, CultureInfo? culture = null, TimeZoneInfo? zone = null) =>
        DateText.LongDateAndTime(NoteDates.Shown(note.CreateDateTime, zone ?? TimeZoneInfo.Local), culture);

    /// <summary>
    /// The caption under the text: "Added 10/15/2026, 1:40 PM · Last modified 10/20/2026, 8:55 AM", the
    /// short date and short time in <paramref name="zone"/> (Windows' when null). "Added" is shown whenever
    /// <paramref name="createdAt"/> is set; "Last modified" is left out when it equals "Added" to the minute.
    /// Empty when neither is set.
    /// </summary>
    public static string TimesCaption(long? createdAt, long? updatedAt, TimeZoneInfo? zone = null, CultureInfo? culture = null)
    {
        zone ??= TimeZoneInfo.Local;
        string Format(long millis) =>
            DateText.ShortDateAndTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(millis), zone).DateTime, culture);
        static long Minute(long millis) => Math.DivRem(millis, 60_000L, out var rest) - (rest < 0 ? 1 : 0);

        var added = createdAt is { } c ? Format(c) : null;
        var modified = updatedAt is { } u && (createdAt is not { } c2 || Minute(u) != Minute(c2)) ? Format(u) : null;
        return (added, modified) switch
        {
            ({ } a, { } m) => $"Added {a} · Last modified {m}",
            ({ } a, null) => $"Added {a}",
            (null, { } m) => $"Last modified {m}",
            _ => "",
        };
    }

    /// <summary>
    /// Copy note text: the title (left out when there's none), the day and time line, an empty
    /// line, then the text as stored.
    /// </summary>
    public static string CopyText(Note note, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        var lines = new List<string>();
        var (title, isPlaceholder) = Title(note);
        if (!isPlaceholder)
        {
            lines.Add(title);
        }
        lines.Add(DayAndTimeLine(note, culture, zone));
        lines.Add("");
        lines.Add(note.TextContents ?? "");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The side column's sections, in order, each with its rows latest linked first; types with no records left out.</summary>
    public static IReadOnlyList<(RecordType Type, IReadOnlyList<LinkedRecordSummary> Records)> SideSections(LinkedRecordSet linked) =>
        SideTypes
            .Select(t => (Type: t, Records: RecordPageLayout.SideOrder(linked.Find(t)?.Records ?? [])))
            .Where(s => s.Records.Count > 0)
            .ToList();

    /// <summary>A linked note's name in the side column: its title, its first line, or its day.</summary>
    public static string LinkedNoteName(LinkedRecordSummary note) =>
        note.Name.Length > 0 ? note.Name
        : note.FirstLine.Length > 0 ? note.FirstLine
        : LinkedNoteDay(note);

    /// <summary>
    /// A linked note's local day (in <paramref name="zone"/>, Windows' when null): the long date without its
    /// weekday, "October 15, 2026" (the side column is too narrow for one).
    /// </summary>
    public static string LinkedNoteDay(LinkedRecordSummary note, CultureInfo? culture = null, TimeZoneInfo? zone = null) =>
        DateText.LongDateWithoutWeekday(NoteDates.ShownDay(note.NoteDateTime, zone ?? TimeZoneInfo.Local), culture);

    /// <summary>A note can be saved with a title or some text (as on the edit page).</summary>
    public static bool IsValid(string title, string text) => title.Length > 0 || text.Length > 0;

    /// <summary>
    /// The note with an edited title, text, day and time, or null when nothing changed. A value equal to
    /// the stored one is kept as stored (an empty box doesn't turn a missing value into ""). A day and
    /// time equal to the ones shown in <paramref name="zone"/> keep both date columns exactly; otherwise
    /// they follow <see cref="NoteDates.ForEditedNote"/>. Text that differs only in its line breaks is
    /// equal; changed text is saved with LF line breaks.
    /// </summary>
    public static Note? Edited(Note note, string title, string text, DateOnly? day, TimeOnly? time = null, TimeZoneInfo? zone = null)
    {
        var (createDate, createDateTime) = NoteDates.ForEditedNote(note, day, time, zone ?? TimeZoneInfo.Local);
        var newTitle = title == (note.Title ?? "") ? note.Title : title;
        var newText = RecordText.Edited(note.TextContents, text);
        if (newTitle == note.Title && newText == note.TextContents
            && createDate == note.CreateDate && createDateTime == note.CreateDateTime)
        {
            return null;
        }
        return note with
        {
            Title = newTitle,
            TextContents = newText,
            CreateDate = createDate,
            CreateDateTime = createDateTime,
        };
    }
}
