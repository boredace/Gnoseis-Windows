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
/// What the category page shows that doesn't depend on the UI: the header line, copy text,
/// search matching per type, row allotment, and column placement.
/// </summary>
public static class CategoryPage
{
    /// <summary>
    /// The line under the name: "Category · 73 linked records".
    /// Singular form for count 1. Placeholder when count is 0.
    /// </summary>
    public static (string Text, bool IsPlaceholder) HeaderLine(int totalLinkedCount)
    {
        if (totalLinkedCount == 0)
        {
            return ("Category · no notes or links yet", true);
        }

        return ($"Category · {totalLinkedCount} {(totalLinkedCount == 1 ? "linked record" : "linked records")}", false);
    }

    /// <summary>
    /// Copy category details: the name, then an empty line and the comments when there are any.
    /// </summary>
    public static string CopyText(Category category)
    {
        var lines = new List<string>();
        var name = category.CategoryName?.Trim() ?? "";
        if (name.Length > 0)
        {
            lines.Add(name);
        }
        var comments = category.Comments?.Trim();
        if (!string.IsNullOrWhiteSpace(comments))
        {
            lines.Add("");
            lines.Add(comments);
        }
        return string.Join(Environment.NewLine, lines);
    }

    // ---------------------------------------------------------------- Search matching

    /// <summary>
    /// Whether a note matches the query: day, title, first line of text, and the "with" line
    /// (other linked records), case-insensitive. A blank query matches all.
    /// </summary>
    public static bool MatchesNote(HistoryNote note, string query, CultureInfo? culture = null)
    {
        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        var cmp = StringComparison.CurrentCultureIgnoreCase;

        // Day text: "26 May" or "26 May 2025"
        var dayText = ContactHistory.DayText(note, culture);
        if (dayText.Contains(query, cmp))
        {
            return true;
        }

        // Title
        if (note.Title.Contains(query, cmp))
        {
            return true;
        }

        // First line of text body
        var firstLine = NoteFirstLine(note);
        if (firstLine.Contains(query, cmp))
        {
            return true;
        }

        // "With" line (other linked records)
        var withLine = ContactHistory.RowLinks(note, "");
        if (withLine.Contains(query, cmp))
        {
            return true;
        }

        return false;
    }

    /// <summary>The first line of a note's text body.</summary>
    private static string NoteFirstLine(HistoryNote note)
    {
        var lines = note.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[0] : "";
    }

    /// <summary>
    /// Whether a contact matches the query: name, job title, and company, case-insensitive.
    /// </summary>
    public static bool MatchesContact(LinkedRecordSummary contact, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        var cmp = StringComparison.CurrentCultureIgnoreCase;
        return contact.Name.Contains(query, cmp)
            || contact.JobAndCompany.Contains(query, cmp);
    }

    /// <summary>
    /// Whether an organization, category, or item matches the query: name and first comment line,
    /// case-insensitive.
    /// </summary>
    public static bool MatchesNamedRecord(LinkedRecordSummary record, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        var cmp = StringComparison.CurrentCultureIgnoreCase;
        return record.Name.Contains(query, cmp)
            || record.FirstLine.Contains(query, cmp);
    }

    /// <summary>
    /// Whether any record type matches the query. Delegates to the appropriate method.
    /// </summary>
    public static bool MatchesAnyType(LinkedRecordSummary record, string query) =>
        record.Type switch
        {
            RecordType.Note => MatchesNote(new HistoryNote
            {
                Id = record.Id,
                Title = record.Name,
                Text = record.Text,
                CreateDate = record.NoteDate,
                CreateDateTime = record.NoteDateTime,
                Links = [record],
            }, query),
            RecordType.Contact => MatchesContact(record, query),
            _ => MatchesNamedRecord(record, query),
        };

    /// <summary>
    /// Whether any of the records match the query, regardless of type.
    /// </summary>
    public static bool AnyMatch(IEnumerable<LinkedRecordSummary> records, string query) =>
        records.Any(r => MatchesAnyType(r, query));

    // ---------------------------------------------------------------- Row allotment

    /// <summary>
    /// Distribute rows across lists: start at 3 per list, then give spare rows to lists that
    /// have more available, up to each list's length. Total rows capped at <paramref name="maxRows"/>.
    /// </summary>
    public static IReadOnlyList<int> AllotRows(IReadOnlyList<int> listLengths, int maxRows)
    {
        var rows = listLengths.Select(_ => 3).ToList();
        var remaining = maxRows - rows.Sum();
        if (remaining <= 0)
        {
            return rows;
        }

        // Keep distributing until no more rows can be given or we've used all remaining.
        while (remaining > 0)
        {
            var distributed = false;
            for (var i = 0; i < listLengths.Count; i++)
            {
                var canAdd = listLengths[i] - rows[i];
                if (canAdd > 0 && remaining > 0)
                {
                    rows[i]++;
                    remaining--;
                    distributed = true;
                }
            }
            if (!distributed)
            {
                break;
            }
        }

        return rows;
    }

    // ---------------------------------------------------------------- Column placement

    /// <summary>
    /// Number of columns for the category lists, by band: 1 under 860, 2 at 860–1099,
    /// 3 at 1100–1699, 5 from 1700 (capped at 5 lists).
    /// </summary>
    public static int ColumnCount(RecordPageBand band) => band switch
    {
        RecordPageBand.S => 1,
        RecordPageBand.M => 2,
        RecordPageBand.L => 3,
        _ => 5,
    };

    /// <summary>
    /// Place lists into columns using shortest-column-first: each list goes under the column
    /// that currently has the fewest rows. Returns the row count per list in column order.
    /// </summary>
    public static IReadOnlyList<int> PlaceColumns(IReadOnlyList<int> allotment, int columnCount, int maxRows)
    {
        var columns = new int[columnCount]; // total rows per column
        var result = new int[allotment.Count]; // rows per list

        for (var i = 0; i < allotment.Count; i++)
        {
            // Find the shortest column
            var minCol = 0;
            for (var c = 1; c < columnCount; c++)
            {
                if (columns[c] < columns[minCol])
                {
                    minCol = c;
                }
            }

            result[i] = Math.Min(allotment[i], maxRows - columns[minCol]);
            columns[minCol] += result[i];
        }

        return result;
    }
}
