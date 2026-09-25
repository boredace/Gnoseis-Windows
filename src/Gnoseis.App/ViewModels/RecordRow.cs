/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Microsoft.UI.Xaml.Media;

namespace Gnoseis.App.ViewModels;

/// <summary>One line in any record list (lists, linked records, search and link results).</summary>
public sealed class RecordRow
{
    public required string Id { get; init; }
    public required RecordType Type { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";

    /// <summary>Short text on the right, e.g. the time of a note.</summary>
    public string Trailing { get; init; } = "";

    /// <summary>Show the record's picture: initials for contacts, a colored type tile for the rest.</summary>
    public bool ShowIcon { get; init; }

    /// <summary>Used by the link picker: the record is already linked to the source.</summary>
    public bool IsLinked { get; init; }

    public string Glyph => Glyphs.For(Type);
    public SolidColorBrush TileBrush => TypeColors.BrushFor(Type);
    public bool IsPerson => ShowIcon && Type == RecordType.Contact;
    public bool ShowTile => ShowIcon && Type != RecordType.Contact;
    public bool HasTitle => Title.Length > 0;
    public bool HasSubtitle => Subtitle.Length > 0;
    public bool HasTrailing => Trailing.Length > 0;
    public string TypeName => Type.DisplayName();

    /// <summary>Text read by screen readers.</summary>
    public string AutomationName => $"{TypeName}: {Title}. {Subtitle}";

    public RecordRef ToRef() => new(Type, Id);

    /// <summary>Shown in the search box when a suggestion is highlighted with the keyboard.</summary>
    public override string ToString() => Title;
}

/// <summary>A group of rows under a header (sticky in the list), e.g. a day or a first letter.</summary>
public sealed class RecordGroup(string header, IEnumerable<RecordRow> rows) : List<RecordRow>(rows)
{
    public string Header { get; } = header;
}

/// <summary>Builds list rows and groups.</summary>
public static class RecordRows
{
    public static RecordRow FromNote(Note note, bool mixedList = false)
    {
        var (title, rest) = NoteTitleAndPreview(note);
        return new RecordRow
        {
            Id = note.Id,
            Type = RecordType.Note,
            Title = title,
            // In mixed lists the date tells notes apart; in the note list the text does (the day is the group header).
            Subtitle = mixedList ? FormatNoteDate(note) : rest,
            Trailing = mixedList ? "" : FormatNoteTime(note),
            ShowIcon = mixedList,
        };
    }

    public static RecordRow FromContact(Contact contact, bool mixedList = false) => new()
    {
        Id = contact.Id,
        Type = RecordType.Contact,
        Title = contact.DisplayName,
        Subtitle = string.Join(" · ", new[] { contact.JobTitle, contact.Company }.Where(s => !string.IsNullOrWhiteSpace(s))),
        ShowIcon = true,
    };

    public static RecordRow FromOrganization(Organization organization, bool mixedList = false) => new()
    {
        Id = organization.Id,
        Type = RecordType.Organization,
        Title = organization.OrganizationName,
        Subtitle = Preview(organization.Comments, 120),
        ShowIcon = true,
    };

    public static RecordRow FromCategory(Category category, bool mixedList = false) => new()
    {
        Id = category.Id,
        Type = RecordType.Category,
        Title = category.CategoryName,
        Subtitle = Preview(category.Comments, 120),
        ShowIcon = true,
    };

    public static RecordRow FromItem(Item item, bool mixedList = false) => new()
    {
        Id = item.Id,
        Type = RecordType.Item,
        Title = item.ItemName,
        Subtitle = Preview(item.Comments, 120),
        ShowIcon = true,
    };

    public static RecordRow FromSearchResult(SearchResult result, bool isLinked = false) => new()
    {
        Id = result.RecordId,
        Type = result.RecordType,
        Title = result.RecordTitle,
        Subtitle = isLinked ? "Already linked" : result.RecordType.DisplayName(),
        ShowIcon = true,
        IsLinked = isLinked,
    };

    /// <summary>Notes grouped by day, newest first: "Today", "Yesterday", weekday, then the date.</summary>
    public static List<RecordGroup> GroupNotes(IEnumerable<Note> notes) =>
        notes
            .GroupBy(n => EpochTime.ToUtcDate(n.CreateDate))
            .OrderByDescending(g => g.Key)
            .Select(g => new RecordGroup(DayHeader(g.Key), g.OrderByDescending(n => n.CreateDateTime).Select(n => FromNote(n))))
            .ToList();

    /// <summary>Rows grouped by the first letter of their title, keeping the list order.</summary>
    public static List<RecordGroup> GroupByFirstLetter(IEnumerable<RecordRow> rows) =>
        rows.GroupBy(r => FirstLetter(r.Title)).Select(g => new RecordGroup(g.Key, g)).ToList();

    /// <summary>Rows grouped by record type (search and link results).</summary>
    public static List<RecordGroup> GroupByType(IEnumerable<RecordRow> rows) =>
        rows.GroupBy(r => r.Type).OrderBy(g => (int)g.Key).Select(g => new RecordGroup(g.Key.PluralDisplayName(), g)).ToList();

    public static string DayHeader(DateOnly day)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var days = today.DayNumber - day.DayNumber;
        return days switch
        {
            0 => "Today",
            1 => "Yesterday",
            > 1 and < 7 => day.ToString("dddd", CultureInfo.CurrentCulture),
            _ => day.ToDateTime(TimeOnly.MinValue).ToString(day.Year == today.Year ? "M" : "D", CultureInfo.CurrentCulture),
        };
    }

    /// <summary>Title of a note, or its first line when it has no title, plus a preview of the text.</summary>
    public static (string Title, string Preview) NoteTitleAndPreview(Note note)
    {
        var text = note.TextContents ?? "";
        if (!string.IsNullOrWhiteSpace(note.Title))
        {
            return (note.Title!, Preview(text, 200));
        }
        var lines = text.Split(['\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? ("Untitled note", "") : (Truncate(lines[0], 80), lines.Length > 1 ? Preview(lines[1], 200) : "");
    }

    public static string FormatNoteDate(Note note)
    {
        // Android shows note timestamps in UTC; do the same so both apps show the same date and time.
        var utc = EpochTime.ToUtc(note.CreateDateTime).UtcDateTime;
        return $"{utc.ToString("D", CultureInfo.CurrentCulture)}, {utc.ToString("t", CultureInfo.CurrentCulture)}";
    }

    public static string FormatNoteTime(Note note) =>
        EpochTime.ToUtc(note.CreateDateTime).UtcDateTime.ToString("t", CultureInfo.CurrentCulture);

    private static string FirstLetter(string title)
    {
        var trimmed = title.TrimStart();
        if (trimmed.Length == 0 || !char.IsLetter(trimmed[0]))
        {
            return "#";
        }
        return trimmed[..1].ToUpper(CultureInfo.CurrentCulture);
    }

    public static string Preview(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }
        var singleLine = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return Truncate(singleLine, maxLength);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength].TrimEnd() + "…" : text;
}
