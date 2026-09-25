/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Data;

/// <summary>
/// Search across all record types by title. Like the Android app, the title of a contact is
/// "Last, First" and the query matches anywhere in the title, ignoring case.
/// </summary>
public sealed class SearchRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    // Differences from Android SearchDao:
    //  - A contact without a first name gets the title "Last" instead of NULL (fixes Android known bug 6).
    //  - A note without a title is listed by the start of its text, so it can be found and linked.
    private const string AllTitlesSql =
        "SELECT id AS recordId, COALESCE(NULLIF(title, ''), substr(textContents, 1, 200)) AS recordTitle, 1 AS recordTypeId FROM Note " +
        "WHERE NULLIF(title, '') IS NOT NULL OR NULLIF(textContents, '') IS NOT NULL " +
        "UNION ALL SELECT id, nameLast || COALESCE(', ' || NULLIF(nameFirst, ''), ''), 2 FROM Contact " +
        "UNION ALL SELECT id, organizationName, 3 FROM Organization " +
        "UNION ALL SELECT id, categoryName, 4 FROM Category " +
        "UNION ALL SELECT id, itemName, 5 FROM Item;";

    /// <summary>Records whose title contains <paramref name="query"/> (all records when the query is empty).</summary>
    public async Task<List<SearchResult>> SearchAsync(string? query)
    {
        var all = await QueryAsync(AllTitlesSql, r => new SearchResult(
            r.GetString(0),
            r.GetInt32(2),
            FirstLine(r.IsDBNull(1) ? "" : r.GetString(1)))).ConfigureAwait(false);

        var trimmed = query?.Trim() ?? "";
        return all
            .Where(r => trimmed.Length == 0 || r.RecordTitle.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(r => r.RecordTypeId)
            .ThenBy(r => r.RecordTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Number of records of each type (Home page).</summary>
    public async Task<Dictionary<RecordType, int>> GetRecordCountsAsync()
    {
        var rows = await QueryAsync(
            "SELECT 1, count(*) FROM Note UNION ALL SELECT 2, count(*) FROM Contact UNION ALL " +
            "SELECT 3, count(*) FROM Organization UNION ALL SELECT 4, count(*) FROM Category UNION ALL " +
            "SELECT 5, count(*) FROM Item;",
            r => ((RecordType)r.GetInt32(0), r.GetInt32(1))).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Item1, r => r.Item2);
    }

    /// <summary>Titles of the given records that still exist, in the given order (recently opened list).</summary>
    public async Task<List<SearchResult>> GetRecordsAsync(IReadOnlyList<string> recordIds)
    {
        var all = (await SearchAsync("").ConfigureAwait(false)).ToDictionary(r => r.RecordId);
        return recordIds.Where(all.ContainsKey).Select(id => all[id]).ToList();
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length > TitleLength.Note ? line[..TitleLength.Note] + "…" : line;
    }
}
