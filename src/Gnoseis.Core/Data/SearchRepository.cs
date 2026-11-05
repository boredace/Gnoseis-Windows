/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;
using Microsoft.Data.Sqlite;

namespace Gnoseis.Core.Data;

/// <summary>
/// Search across all record types by title. Like the Android app, the title of a contact is
/// "Last, First" and the query matches anywhere in the title, ignoring case and runs of spaces;
/// a contact is also found as "First Last". Each result carries what its row shows: link count,
/// note day, job title, company and when it was last changed.
/// </summary>
public sealed class SearchRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    // Differences from Android SearchDao:
    //  - A contact without a first name gets the title "Last" instead of NULL (fixes Android known bug 6).
    //  - A note without a title is listed by the start of its text, so it can be found and linked.

    /// <summary>
    /// Each type's records as search results, without link counts. Columns: id, title, type id,
    /// note day, job title, company, updatedAt.
    /// </summary>
    private static readonly IReadOnlyDictionary<RecordType, string> RecordsSql = new Dictionary<RecordType, string>
    {
        [RecordType.Note] =
            "SELECT id, COALESCE(NULLIF(title, ''), substr(textContents, 1, 200)), 1, createDate, NULL, NULL, updatedAt FROM Note " +
            "WHERE (NULLIF(title, '') IS NOT NULL OR NULLIF(textContents, '') IS NOT NULL)",
        [RecordType.Contact] =
            "SELECT id, nameLast || COALESCE(', ' || NULLIF(nameFirst, ''), ''), 2, NULL, jobTitle, company, updatedAt FROM Contact WHERE 1",
        [RecordType.Organization] =
            "SELECT id, organizationName, 3, NULL, NULL, NULL, updatedAt FROM Organization WHERE 1",
        [RecordType.Category] =
            "SELECT id, categoryName, 4, NULL, NULL, NULL, updatedAt FROM Category WHERE 1",
        [RecordType.Item] =
            "SELECT id, itemName, 5, NULL, NULL, NULL, updatedAt FROM Item WHERE 1",
    };

    /// <summary>
    /// The number of distinct records linked to each record, in either direction; a record linked
    /// to itself counts itself once, and two records linked twice count once. LinkedRecord has no
    /// index on record1Id / record2Id, so links are counted first, reading each link once, rather
    /// than with a count per record.
    /// </summary>
    private const string LinkCountsSql =
        "SELECT id, COUNT(DISTINCT other) FROM (" +
        "SELECT record1Id AS id, record2Id AS other FROM LinkedRecord " +
        "UNION ALL SELECT record2Id, record1Id FROM LinkedRecord) GROUP BY id;";

    /// <summary>Every record that search can find, with its row data, by type then title (unranked).</summary>
    public Task<List<SearchResult>> GetAllAsync() =>
        Task.Run(() =>
        {
            using var connection = Database.OpenConnection();
            var counts = ReadLinkCounts(connection);
            var results = new List<SearchResult>();
            foreach (var type in RecordTypeExtensions.UserTypes)
            {
                using var command = CreateCommand(connection, RecordsSql[type] + ";", []);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var result = Map(reader);
                    results.Add(result with { LinkCount = counts.GetValueOrDefault(result.RecordId) });
                }
            }
            return results;
        });

    /// <summary>
    /// Records whose title contains <paramref name="query"/>, ranked best first (see <see cref="SearchIndex.Find"/>);
    /// all records, unranked, when the query is empty.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string? query)
    {
        var all = await GetAllAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(query) ? all : new SearchIndex(all).Find(query);
    }

    /// <summary>
    /// Up to <paramref name="limit"/> records of the given types with a change time, most recently
    /// changed first, leaving out <paramref name="excludeIds"/>. Records never stamped (NULL updatedAt,
    /// from before schema v3) are left out.
    /// </summary>
    public Task<List<SearchResult>> GetRecentlyChangedAsync(IReadOnlyCollection<RecordType> types,
        IReadOnlyCollection<string> excludeIds, int limit) =>
        Task.Run(() =>
        {
            if (limit <= 0)
            {
                return [];
            }
            var excluded = excludeIds as IReadOnlySet<string> ?? excludeIds.ToHashSet(StringComparer.Ordinal);
            using var connection = Database.OpenConnection();
            var recent = new List<SearchResult>();
            foreach (var type in RecordTypeExtensions.UserTypes.Where(types.Contains))
            {
                // Newest first; the reader stops once this type has enough records that aren't excluded.
                using var command = CreateCommand(connection,
                    RecordsSql[type] + " AND updatedAt IS NOT NULL ORDER BY updatedAt DESC, id;", []);
                using var reader = command.ExecuteReader();
                var taken = 0;
                while (taken < limit && reader.Read())
                {
                    var result = Map(reader);
                    if (!excluded.Contains(result.RecordId))
                    {
                        recent.Add(result);
                        taken++;
                    }
                }
            }
            var newest = recent
                .OrderByDescending(r => r.UpdatedAt)
                .ThenBy(r => r.RecordId, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            var counts = ReadLinkCounts(connection, newest.Select(r => r.RecordId).ToList());
            return newest.Select(r => r with { LinkCount = counts.GetValueOrDefault(r.RecordId) }).ToList();
        });

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
        var all = (await GetAllAsync().ConfigureAwait(false)).ToDictionary(r => r.RecordId);
        return recordIds.Where(all.ContainsKey).Select(id => all[id]).ToList();
    }

    private static SearchResult Map(SqliteDataReader r) => new(
        r.GetString(0),
        r.GetInt32(2),
        FirstLine(r.IsDBNull(1) ? "" : r.GetString(1)),
        NoteDate: r.IsDBNull(3) ? null : r.GetInt64(3),
        JobTitle: r.IsDBNull(4) ? null : r.GetString(4),
        Company: r.IsDBNull(5) ? null : r.GetString(5),
        UpdatedAt: r.IsDBNull(6) ? null : r.GetInt64(6));

    /// <summary>Link counts of every record with a link.</summary>
    private static Dictionary<string, int> ReadLinkCounts(SqliteConnection connection)
    {
        using var command = CreateCommand(connection, LinkCountsSql, []);
        return ReadCounts(command);
    }

    /// <summary>Link counts of the given records (as <see cref="LinkCountsSql"/> counts them).</summary>
    private static Dictionary<string, int> ReadLinkCounts(SqliteConnection connection, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }
        var parameters = ids.Select((id, i) => ($"$p{i}", (object?)id)).ToArray();
        var list = string.Join(", ", parameters.Select(p => p.Item1));
        using var command = CreateCommand(connection,
            "SELECT id, COUNT(DISTINCT other) FROM (" +
            $"SELECT record1Id AS id, record2Id AS other FROM LinkedRecord WHERE record1Id IN ({list}) " +
            $"UNION ALL SELECT record2Id, record1Id FROM LinkedRecord WHERE record2Id IN ({list})) GROUP BY id;",
            parameters);
        return ReadCounts(command);
    }

    private static Dictionary<string, int> ReadCounts(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (reader.Read())
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }
        return counts;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length > TitleLength.Note ? line[..TitleLength.Note] + "…" : line;
    }
}
