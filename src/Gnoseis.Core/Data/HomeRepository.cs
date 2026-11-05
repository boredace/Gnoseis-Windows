/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;
using Microsoft.Data.Sqlite;

namespace Gnoseis.Core.Data;

/// <summary>
/// Read-only queries for the Home page, with the same rules as the Android HomeDao.
/// LinkedRecord has no index on record1Id / record2Id, so no query here joins it with
/// "record1Id = x OR record2Id = x": links are counted first, reading each link once, and the
/// result is joined to the record tables by primary key.
/// </summary>
public sealed class HomeRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    /// <summary>Bubbles shown in Most linked.</summary>
    public const int MostLinkedLimit = 10;

    /// <summary>Days in the "Notes, last 14 days" chart.</summary>
    public const int ChartDays = 14;

    private const long DayMillis = 86_400_000;

    /// <summary>
    /// The number of links of each record: every LinkedRecord row counts once at each end, and a
    /// link from a record to itself counts once (as Android's OR join counts it).
    /// </summary>
    private const string LinkCountsSql =
        "SELECT id, COUNT(*) AS linkCount FROM (" +
        "SELECT record1Id AS id FROM LinkedRecord " +
        "UNION ALL SELECT record2Id FROM LinkedRecord WHERE record2Id <> record1Id) GROUP BY id";

    /// <summary>Contacts, organizations, categories and items with the names Most linked shows.</summary>
    private const string NamedRecordsSql =
        "SELECT c.id AS id, TRIM(COALESCE(t.nameFirst, '') || ' ' || COALESCE(t.nameLast, '')) AS name, 2 AS recordTypeId, c.linkCount AS linkCount " +
        "FROM counts AS c JOIN Contact AS t ON t.id = c.id " +
        "UNION ALL SELECT c.id, COALESCE(t.organizationName, ''), 3, c.linkCount FROM counts AS c JOIN Organization AS t ON t.id = c.id " +
        "UNION ALL SELECT c.id, COALESCE(t.categoryName, ''), 4, c.linkCount FROM counts AS c JOIN Category AS t ON t.id = c.id " +
        "UNION ALL SELECT c.id, COALESCE(t.itemName, ''), 5, c.linkCount FROM counts AS c JOIN Item AS t ON t.id = c.id";

    /// <summary>Ids at either end of a link.</summary>
    private const string LinkedIdsSql =
        "SELECT record1Id AS id FROM LinkedRecord UNION SELECT record2Id FROM LinkedRecord";

    /// <summary>The number of records of each type and of links.</summary>
    public async Task<HomeCounts> GetCountsAsync()
    {
        var rows = await QueryAsync(
            "SELECT 1, count(*) FROM Note UNION ALL SELECT 2, count(*) FROM Contact UNION ALL " +
            "SELECT 3, count(*) FROM Organization UNION ALL SELECT 4, count(*) FROM Category UNION ALL " +
            "SELECT 5, count(*) FROM Item UNION ALL SELECT 0, count(*) FROM LinkedRecord;",
            r => (r.GetInt32(0), r.GetInt32(1))).ConfigureAwait(false);
        return new HomeCounts(
            rows.Where(r => r.Item1 != 0).ToDictionary(r => (RecordType)r.Item1, r => r.Item2),
            rows.Single(r => r.Item1 == 0).Item2);
    }

    /// <summary>
    /// The contacts, organizations, categories and items with the most links (links from notes
    /// count; notes are never included), most first, then by name ignoring case, then by id.
    /// With <paramref name="type"/>, only records of that type.
    /// </summary>
    public Task<List<MostLinkedRecord>> GetMostLinkedAsync(RecordType? type = null, int limit = MostLinkedLimit) =>
        QueryAsync(
            $"WITH counts AS MATERIALIZED ({LinkCountsSql}) " +
            $"SELECT id, name, recordTypeId, linkCount FROM ({NamedRecordsSql}) " +
            "WHERE $type IS NULL OR recordTypeId = $type " +
            "ORDER BY linkCount DESC, name COLLATE NOCASE, id LIMIT $limit;",
            r => new MostLinkedRecord(r.GetString(0), (RecordType)r.GetInt32(2), r.GetString(1), r.GetInt32(3)),
            ("$type", type is { } t ? (int)t : null), ("$limit", limit));

    /// <summary>The links with both ends in <paramref name="recordIds"/> (the lines between bubbles).</summary>
    public async Task<List<LinkEnds>> GetLinksAmongAsync(IReadOnlyCollection<string> recordIds)
    {
        if (recordIds.Count < 2)
        {
            return [];
        }
        var (list, parameters) = InList(recordIds);
        return await QueryAsync(
            $"SELECT record1Id, record2Id FROM LinkedRecord WHERE record1Id IN ({list}) AND record2Id IN ({list});",
            r => new LinkEnds(r.GetString(0), r.GetString(1)),
            parameters).ConfigureAwait(false);
    }

    /// <summary>
    /// For contacts, organizations, categories and items, the number of links with at least one
    /// end of that type; a link between two records of the same type counts once. Types without
    /// links are left out. In type order.
    /// </summary>
    public Task<List<TypeLinkCount>> GetLinksPerTypeAsync() =>
        // Each link counts once for its first end's type, and once for its second end's type when
        // that is a different type (the same result as Android's UNION over (link, type) pairs,
        // without sorting every pair).
        QueryAsync(
            "SELECT recordTypeId, COUNT(*) FROM (" +
            "SELECT record1TypeId AS recordTypeId FROM LinkedRecord " +
            "UNION ALL SELECT record2TypeId FROM LinkedRecord WHERE record2TypeId <> record1TypeId" +
            ") WHERE recordTypeId IN (2, 3, 4, 5) GROUP BY recordTypeId ORDER BY recordTypeId;",
            r => new TypeLinkCount((RecordType)r.GetInt32(0), r.GetInt32(1)));

    /// <summary>The newest notes by createDateTime, as rows with their link counts.</summary>
    public Task<List<LinkedRecordSummary>> GetRecentNotesAsync(int limit) =>
        Task.Run(() =>
        {
            using var connection = Database.OpenConnection();
            var notes = ReadSummaries(connection, RecordType.Note,
                "SELECT * FROM Note ORDER BY createDateTime DESC, id LIMIT $limit;", [("$limit", limit)]);
            return WithLinkCounts(connection, notes);
        });

    /// <summary>
    /// Notes per note day for the <see cref="ChartDays"/> days ending <paramref name="today"/>,
    /// oldest first, with zero for days without notes. Days are UTC calendar days of createDate,
    /// as the note list groups notes.
    /// </summary>
    public async Task<List<DayCount>> GetNotesPerDayAsync(DateOnly today)
    {
        var first = today.AddDays(-(ChartDays - 1));
        var counts = (await QueryAsync(
            "SELECT createDate / $day AS day, COUNT(*) FROM Note " +
            "WHERE createDate >= $from AND createDate < $to GROUP BY day;",
            r => (Day: r.GetInt64(0), Count: r.GetInt32(1)),
            ("$day", DayMillis), ("$from", EpochTime.UtcMidnight(first)), ("$to", EpochTime.UtcMidnight(today.AddDays(1))))
            .ConfigureAwait(false))
            .ToDictionary(c => c.Day, c => c.Count);
        return Enumerable.Range(0, ChartDays)
            .Select(i => first.AddDays(i))
            .Select(d => new DayCount(d, counts.GetValueOrDefault(EpochTime.UtcMidnight(d) / DayMillis)))
            .ToList();
    }

    /// <summary>Notes with no link on either side: the newest <paramref name="limit"/> by createDateTime, and the total.</summary>
    public Task<UnlinkedNotes> GetUnlinkedNotesAsync(int limit) =>
        Task.Run(() =>
        {
            using var connection = Database.OpenConnection();
            // One statement, so the linked ids are collected once: the window count is taken over
            // every unlinked note before LIMIT.
            using var command = CreateCommand(connection,
                $"WITH linked AS MATERIALIZED ({LinkedIdsSql}) " +
                "SELECT n.*, COUNT(*) OVER () AS unlinkedCount FROM Note AS n " +
                "WHERE NOT EXISTS (SELECT 1 FROM linked AS l WHERE l.id = n.id) " +
                "ORDER BY n.createDateTime DESC, n.id LIMIT $limit;",
                [("$limit", limit)]);
            using var reader = command.ExecuteReader();
            var notes = new List<LinkedRecordSummary>();
            var total = 0;
            while (reader.Read())
            {
                notes.Add(RecordSummaries.Map(RecordType.Note, reader, []));
                total = reader.GetInt32(reader.GetOrdinal("unlinkedCount"));
            }
            return new UnlinkedNotes(notes, total);
        });

    /// <summary>
    /// Rows for the given records that still exist, in the given order, with their link counts and
    /// parent paths (Recently opened).
    /// </summary>
    public Task<List<LinkedRecordSummary>> GetRecordSummariesAsync(IReadOnlyList<(RecordType Type, string Id)> records) =>
        Task.Run(() =>
        {
            using var connection = Database.OpenConnection();
            var found = new List<LinkedRecordSummary>();
            var withParents = new List<(RecordType Type, string Id, string ParentId)>();
            foreach (var byType in records.Where(r => RecordTypeExtensions.UserTypes.Contains(r.Type)).GroupBy(r => r.Type))
            {
                var (list, parameters) = InList(byType.Select(r => r.Id).Distinct().ToList());
                using var command = CreateCommand(connection,
                    $"SELECT * FROM `{RecordSummaries.TableOf(byType.Key)}` WHERE id IN ({list});", parameters);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    found.Add(RecordSummaries.Map(byType.Key, reader, withParents));
                }
            }
            RecordSummaries.AddParentPaths(connection, withParents, found);
            var withCounts = WithLinkCounts(connection, found);
            return records
                .Select(r => withCounts.FirstOrDefault(s => s.Type == r.Type && s.Id == r.Id))
                .OfType<LinkedRecordSummary>()
                .ToList();
        });

    /// <summary>
    /// Records created or edited, and links created, after <paramref name="since"/> (UTC epoch ms).
    /// Rows with NULL times (from before schema v3) never count.
    /// </summary>
    public async Task<int> GetChangeCountSinceAsync(long since) =>
        (await QueryAsync(
            "SELECT (SELECT COUNT(*) FROM Note WHERE createdAt > $since OR updatedAt > $since) + " +
            "(SELECT COUNT(*) FROM Contact WHERE createdAt > $since OR updatedAt > $since) + " +
            "(SELECT COUNT(*) FROM Organization WHERE createdAt > $since OR updatedAt > $since) + " +
            "(SELECT COUNT(*) FROM Category WHERE createdAt > $since OR updatedAt > $since) + " +
            "(SELECT COUNT(*) FROM Item WHERE createdAt > $since OR updatedAt > $since) + " +
            "(SELECT COUNT(*) FROM LinkedRecord WHERE createdAt > $since);",
            r => r.GetInt32(0), ("$since", since)).ConfigureAwait(false)).Single();

    /// <summary>The backup reminder, from lastBackupAt, lastDeletionAt and the changes since the last backup.</summary>
    public async Task<BackupReminder> GetBackupReminderAsync()
    {
        var settings = new SettingsRepository(Database);
        var lastBackupAt = await settings.GetLastBackupAtAsync().ConfigureAwait(false);
        var lastDeletionAt = await settings.GetLastDeletionAtAsync().ConfigureAwait(false);
        var changes = lastBackupAt is { } since ? await GetChangeCountSinceAsync(since).ConfigureAwait(false) : 0;
        return BackupReminder.From(lastBackupAt, lastDeletionAt, changes);
    }

    private static List<LinkedRecordSummary> ReadSummaries(SqliteConnection connection, RecordType type, string sql,
        (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<LinkedRecordSummary>();
        var withParents = new List<(RecordType Type, string Id, string ParentId)>();
        while (reader.Read())
        {
            rows.Add(RecordSummaries.Map(type, reader, withParents));
        }
        RecordSummaries.AddParentPaths(connection, withParents, rows);
        return rows;
    }

    /// <summary>
    /// Sets each row's link count as the record page shows it: the number of distinct records
    /// linked to it. Reads LinkedRecord twice in all, not twice per row.
    /// </summary>
    private static List<LinkedRecordSummary> WithLinkCounts(SqliteConnection connection, List<LinkedRecordSummary> rows)
    {
        if (rows.Count == 0)
        {
            return rows;
        }
        var (list, parameters) = InList(rows.Select(r => r.Id).Distinct().ToList());
        using var command = CreateCommand(connection,
            "SELECT id, COUNT(DISTINCT other) FROM (" +
            $"SELECT record1Id AS id, record2Id AS other FROM LinkedRecord WHERE record1Id IN ({list}) " +
            $"UNION ALL SELECT record2Id, record1Id FROM LinkedRecord WHERE record2Id IN ({list})) GROUP BY id;",
            parameters);
        using var reader = command.ExecuteReader();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (reader.Read())
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }
        return rows.Select(r => r with { LinkCount = counts.GetValueOrDefault(r.Id) }).ToList();
    }

    /// <summary>"$p0, $p1, …" and the parameters for an IN list.</summary>
    private static (string List, (string Name, object? Value)[] Parameters) InList(IReadOnlyCollection<string> values)
    {
        var parameters = values.Select((v, i) => ($"$p{i}", (object?)v)).ToArray();
        return (string.Join(", ", parameters.Select(p => p.Item1)), parameters);
    }
}
