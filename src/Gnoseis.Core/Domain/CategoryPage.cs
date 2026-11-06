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
/// One row of a category's lists, as it shows: a note's day, title and "with" line; a contact's name
/// and job title · company; another record's name and first comment line.
/// </summary>
/// <param name="Day">A note's day ("28 Oct", with the year when it isn't this year); empty for other types.</param>
/// <param name="Name">The title or name.</param>
/// <param name="Second">The "with" line, job title · company, or first comment line; empty when there is none.</param>
/// <param name="Searched">Further text a search matches that the row doesn't show (a note's first line when it has a title).</param>
public sealed record CategoryRow(string Id, RecordType Type, string Day, string Name, string Second, string Searched = "")
{
    /// <summary>Whether the row's text contains <paramref name="query"/>, ignoring case. A blank query matches every row.</summary>
    public bool Matches(string? query)
    {
        var text = (query ?? "").Trim();
        return text.Length == 0
            || Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || Second.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || Searched.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }
}

/// <summary>
/// What the category page shows that doesn't depend on the UI: the header line, copy text, the five
/// lists' rows, the search over all of them, and how many rows each list shows in which column.
/// </summary>
public static class CategoryPage
{
    /// <summary>Rows every list starts with.</summary>
    public const int StartRows = 3;

    /// <summary>Rows a list counts as at most when placing it in a column.</summary>
    public const int PlacementRowCap = 20;

    /// <summary>The lists, always all five, in this order.</summary>
    public static IReadOnlyList<RecordType> ListTypes { get; } =
        [RecordType.Note, RecordType.Contact, RecordType.Organization, RecordType.Item, RecordType.Category];

    /// <summary>
    /// The line under the name: "Category · 73 linked records" (notes and every other linked record
    /// together), or "Category · no notes or links yet".
    /// </summary>
    public static (string Text, bool IsPlaceholder) HeaderLine(int totalLinkedCount) =>
        totalLinkedCount == 0
            ? ("Category · no notes or links yet", true)
            : ($"Category · {totalLinkedCount} {(totalLinkedCount == 1 ? "linked record" : "linked records")}", false);

    /// <summary>Copy category details: the name, then an empty line and the comments when there are any.</summary>
    public static string CopyText(Category category) => RecordHistoryPage.CopyText(category.CategoryName, category.Comments);

    // ---------------------------------------------------------------- Rows

    /// <summary>
    /// The rows of each list: notes newest first by note day, with the note's other linked records
    /// (the category left out) as the second line; the other types by name.
    /// </summary>
    public static IReadOnlyDictionary<RecordType, IReadOnlyList<CategoryRow>> Rows(
        string categoryId, IEnumerable<HistoryNote> notes, LinkedRecordSet linked, DateOnly today, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var rows = new Dictionary<RecordType, IReadOnlyList<CategoryRow>>
        {
            [RecordType.Note] = ContactHistory.Order(notes).Select(n => NoteRow(n, categoryId, today, culture)).ToList(),
        };
        foreach (var type in ListTypes.Where(t => t != RecordType.Note))
        {
            rows[type] = (linked.Find(type)?.Records ?? [])
                .Where(r => r.Id != categoryId)
                .OrderBy(r => r.SortName.Length > 0 ? r.SortName : r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .Select(RecordRow)
                .ToList();
        }
        return rows;
    }

    private static CategoryRow NoteRow(HistoryNote note, string categoryId, DateOnly today, CultureInfo culture)
    {
        var day = ContactHistory.Day(note);
        var with = note.Links.Where(l => l.Id != categoryId).Select(l => l.Name.Length > 0 ? l.Name : l.Type.DisplayName()).ToList();
        var firstLine = note.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return new CategoryRow(
            note.Id,
            RecordType.Note,
            day.ToString(day.Year == today.Year ? "d MMM" : "d MMM yyyy", culture),
            ContactHistory.FullTitle(note),
            ContactHistory.RowLinks(note, categoryId),
            // A search also finds the first line of a titled note, and every name of the "with" line (not only the first 3).
            string.Join("\n", new[] { note.Title.Length > 0 ? firstLine : "" }.Concat(with)));
    }

    private static CategoryRow RecordRow(LinkedRecordSummary record) => record.Type switch
    {
        RecordType.Contact => new CategoryRow(record.Id, record.Type, "", record.Name.Length > 0 ? record.Name : record.Type.DisplayName(), record.JobAndCompany),
        _ => new CategoryRow(record.Id, record.Type, "", record.Name.Length > 0 ? record.Name : record.Type.DisplayName(), record.FirstLine),
    };

    // ---------------------------------------------------------------- Search

    /// <summary>"Search the 73 records in this category".</summary>
    public static string SearchPlaceholder(int totalCount) =>
        $"Search the {totalCount} {(totalCount == 1 ? "record" : "records")} in this category";

    /// <summary>
    /// The text beside the search box: "Searches all five lists at once"; while searching,
    /// "6 matches in 4 of 5 lists · Esc clears", or "No matches · Esc clears".
    /// </summary>
    public static string SearchStatus(IReadOnlyCollection<int> matchesPerList, bool searching)
    {
        if (!searching)
        {
            return "Searches all five lists at once";
        }
        var total = matchesPerList.Sum();
        if (total == 0)
        {
            return "No matches · Esc clears";
        }
        var lists = matchesPerList.Count(m => m > 0);
        return $"{total} {(total == 1 ? "match" : "matches")} in {lists} of {matchesPerList.Count} lists · Esc clears";
    }

    /// <summary>A list's count: "41", "none" when empty; while searching "3 of 41".</summary>
    public static string CountText(int count, int matches, bool searching) =>
        searching ? $"{matches} of {count}" : count == 0 ? "none" : count.ToString(CultureInfo.CurrentCulture);

    /// <summary>A list's sort: "Newest first" for notes, "A–Z" for the others; empty under 2 rows.</summary>
    public static string SortText(RecordType type, int rowCount) =>
        rowCount < 2 ? "" : type == RecordType.Note ? "Newest first" : "A–Z";

    /// <summary>"No contacts in this category yet".</summary>
    public static string EmptyText(RecordType type) =>
        $"No {type.PluralDisplayName().ToLower(CultureInfo.CurrentCulture)} in this category yet";

    /// <summary>"Add a contact", "Add an organization", "Add a note (Ctrl+Shift+N)".</summary>
    public static string AddText(RecordType type)
    {
        var name = type.DisplayName().ToLower(CultureInfo.CurrentCulture);
        var text = $"Add {("aeiou".Contains(name[0]) ? "an" : "a")} {name}";
        return type == RecordType.Note ? $"{text} (Ctrl+Shift+N)" : text;
    }

    /// <summary>Where <paramref name="query"/> occurs in <paramref name="text"/>, ignoring case, to mark the matches.</summary>
    public static IReadOnlyList<TextRange> MatchRanges(string text, string? query)
    {
        var needle = (query ?? "").Trim();
        var ranges = new List<TextRange>();
        if (needle.Length == 0)
        {
            return ranges;
        }
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        var start = 0;
        while (start < text.Length)
        {
            var found = compare.IndexOf(text.AsSpan(start), needle.AsSpan(), CompareOptions.IgnoreCase, out var length);
            if (found < 0 || length == 0)
            {
                break;
            }
            ranges.Add(new TextRange(start + found, length));
            start += found + length;
        }
        return ranges;
    }

    // ---------------------------------------------------------------- Columns and rows shown

    /// <summary>Columns of lists: 1 in S, 2 in M, 3 in L, all five from XL.</summary>
    public static int ColumnCount(RecordPageBand band) => band switch
    {
        RecordPageBand.S => 1,
        RecordPageBand.M => 2,
        RecordPageBand.L => 3,
        _ => 5,
    };

    /// <summary>
    /// The column of each list: in type order, each goes under the column that is shortest so far,
    /// counting each list's rows up to 20 (an empty list as 1). Ties go to the leftmost column.
    /// </summary>
    public static IReadOnlyList<int> PlaceColumns(IReadOnlyList<int> listLengths, int columnCount)
    {
        var heights = new int[Math.Max(1, columnCount)];
        var placed = new int[listLengths.Count];
        for (var i = 0; i < listLengths.Count; i++)
        {
            var column = 0;
            for (var c = 1; c < heights.Length; c++)
            {
                if (heights[c] < heights[column])
                {
                    column = c;
                }
            }
            placed[i] = column;
            heights[column] += Math.Clamp(listLengths[i], 1, PlacementRowCap);
        }
        return placed;
    }

    /// <summary>
    /// The rows each list of one column shows: every list starts with 3 (fewer when it is shorter),
    /// then the rows the column has left go one at a time, in list order, to the lists that have more,
    /// until <paramref name="columnRows"/> rows are used or no list has more.
    /// </summary>
    public static IReadOnlyList<int> AllotRows(IReadOnlyList<int> listLengths, int columnRows)
    {
        var rows = listLengths.Select(length => Math.Min(StartRows, length)).ToArray();
        var left = columnRows - rows.Sum();
        var given = true;
        while (left > 0 && given)
        {
            given = false;
            for (var i = 0; i < rows.Length && left > 0; i++)
            {
                if (rows[i] < listLengths[i])
                {
                    rows[i]++;
                    left--;
                    given = true;
                }
            }
        }
        return rows;
    }
}
