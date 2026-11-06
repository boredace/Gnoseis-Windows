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
/// What the note page shows that doesn't depend on the UI: the title, the day and time line, the
/// created and changed caption, the text Copy note text writes, the side column's sections, and
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

    /// <summary>The note's time of day, as the note lists show it.</summary>
    public static TimeSpan TimeOfDay(Note note) => EpochTime.ToUtc(note.CreateDateTime).UtcDateTime.TimeOfDay;

    /// <summary>The line under the title: "Thu 15 Oct 2026, 09:30".</summary>
    public static string DayAndTimeLine(Note note, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var day = NoteDates.DisplayedDay(note).ToString("ddd d MMM yyyy", culture);
        var time = new DateTime(1, 1, 1).Add(TimeOfDay(note)).ToString("HH:mm", culture);
        return $"{day}, {time}";
    }

    /// <summary>
    /// The caption under the text: "Created 15 Oct 2026, 13:40 · changed 20 Oct 2026, 08:55", in
    /// <paramref name="zone"/>. A part whose time isn't set is left out, and "changed" when it equals
    /// "created" to the minute; empty when neither is set.
    /// </summary>
    public static string TimesCaption(long? createdAt, long? updatedAt, TimeZoneInfo? zone = null, CultureInfo? culture = null)
    {
        zone ??= TimeZoneInfo.Local;
        culture ??= CultureInfo.CurrentCulture;
        string Format(long millis) =>
            TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(millis), zone).ToString("d MMM yyyy, HH:mm", culture);

        var created = createdAt is { } c ? Format(c) : null;
        var changed = updatedAt is { } u ? Format(u) : null;
        if (created != null && changed == created)
        {
            changed = null;
        }
        return (created, changed) switch
        {
            ({ } a, { } b) => $"Created {a} · changed {b}",
            ({ } a, null) => $"Created {a}",
            (null, { } b) => $"Changed {b}",
            _ => "",
        };
    }

    /// <summary>
    /// Copy note text: the title (left out when there's none), the day and time line, an empty
    /// line, then the text as stored.
    /// </summary>
    public static string CopyText(Note note, CultureInfo? culture = null)
    {
        var lines = new List<string>();
        var (title, isPlaceholder) = Title(note);
        if (!isPlaceholder)
        {
            lines.Add(title);
        }
        lines.Add(DayAndTimeLine(note, culture));
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

    /// <summary>A linked note's day: "15 Oct 2026".</summary>
    public static string LinkedNoteDay(LinkedRecordSummary note, CultureInfo? culture = null) =>
        EpochTime.ToUtcDate(note.NoteDate).ToString("d MMM yyyy", culture ?? CultureInfo.CurrentCulture);

    /// <summary>A note can be saved with a title or some text (as on the edit page).</summary>
    public static bool IsValid(string title, string text) => title.Length > 0 || text.Length > 0;

    /// <summary>
    /// The note with an edited title, text and day, or null when nothing changed. A value equal to
    /// the stored one is kept as stored (an empty box doesn't turn a missing value into ""), and
    /// a new day keeps the note's time of day.
    /// </summary>
    public static Note? Edited(Note note, string title, string text, DateOnly day)
    {
        var (createDate, createDateTime) = NoteDates.ForEditedNote(note, day);
        var newTitle = title == (note.Title ?? "") ? note.Title : title;
        var newText = text == (note.TextContents ?? "") ? note.TextContents : text;
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
