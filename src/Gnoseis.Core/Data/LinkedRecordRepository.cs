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
/// Links between records. A link is stored once, in either direction, so every query looks at
/// both record1Id and record2Id (same queries as the Android LinkedRecordDao).
/// </summary>
public sealed class LinkedRecordRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    private const string LinkedIdsSql =
        "SELECT lrt1.record2Id AS linkedRecordId FROM LinkedRecord AS lrt1 WHERE record1Id = $recordId " +
        "UNION " +
        "SELECT lrt2.record1Id AS linkedRecordId FROM LinkedRecord AS lrt2 WHERE record2Id = $recordId";

    /// <summary>All records linked to the given record, split by type.</summary>
    public async Task<LinkedRecords> GetLinkedRecordsAsync(string recordId)
    {
        var notes = QueryAsync(LinkedSql("Note", "t.createDateTime DESC"), MapNote, ("$recordId", recordId));
        var contacts = QueryAsync(LinkedSql("Contact", "t.nameLast COLLATE NOCASE, t.nameFirst COLLATE NOCASE"), MapContact, ("$recordId", recordId));
        var organizations = QueryAsync(LinkedSql("Organization", "t.organizationName COLLATE NOCASE"), MapOrganization, ("$recordId", recordId));
        var categories = QueryAsync(LinkedSql("Category", "t.categoryName COLLATE NOCASE"), MapCategory, ("$recordId", recordId));
        var items = QueryAsync(LinkedSql("Item", "t.itemName COLLATE NOCASE"), MapItem, ("$recordId", recordId));
        await Task.WhenAll(notes, contacts, organizations, categories, items).ConfigureAwait(false);
        return new LinkedRecords(notes.Result, contacts.Result, organizations.Result, categories.Result, items.Result);
    }

    /// <summary>
    /// The records linked to the given record with the fields their rows show (first lines,
    /// contact details, parent path, link count, when they were linked), by type.
    /// </summary>
    public Task<LinkedRecordSet> GetLinkedRecordSetAsync(string recordId) =>
        Task.Run(() =>
        {
            using var connection = Database.OpenConnection();
            var records = new List<LinkedRecordSummary>();
            var withParents = new List<(RecordType Type, string Id, string ParentId)>();
            foreach (var type in RecordTypeExtensions.UserTypes)
            {
                Read(connection, type, recordId, records, withParents);
            }
            RecordSummaries.AddParentPaths(connection, withParents, records);
            return new LinkedRecordSet(records);
        });

    /// <summary>Linked ids with the newest createdAt of their links (pre-v2 duplicates count once).</summary>
    private const string LinkedIdsWithTimeSql =
        "SELECT linkedRecordId, MAX(createdAt) AS linkedAt FROM (" +
        "SELECT record2Id AS linkedRecordId, createdAt FROM LinkedRecord WHERE record1Id = $recordId " +
        "UNION ALL " +
        "SELECT record1Id AS linkedRecordId, createdAt FROM LinkedRecord WHERE record2Id = $recordId) " +
        "WHERE linkedRecordId <> $recordId GROUP BY linkedRecordId";

    /// <summary>Number of distinct records linked to t.id.</summary>
    private const string LinkCountSql =
        "(SELECT count(*) FROM (SELECT record2Id FROM LinkedRecord WHERE record1Id = t.id " +
        "UNION SELECT record1Id FROM LinkedRecord WHERE record2Id = t.id))";

    private static void Read(SqliteConnection connection, RecordType type, string recordId, List<LinkedRecordSummary> records,
        List<(RecordType Type, string Id, string ParentId)> withParents)
    {
        using var command = CreateCommand(connection,
            $"SELECT t.*, lr.linkedAt AS linkedAt, {LinkCountSql} AS linkCount " +
            $"FROM ({LinkedIdsWithTimeSql}) AS lr JOIN `{RecordSummaries.TableOf(type)}` AS t ON t.id = lr.linkedRecordId;",
            [("$recordId", recordId)]);
        using var reader = command.ExecuteReader();
        var linkedAt = reader.GetOrdinal("linkedAt");
        var linkCount = reader.GetOrdinal("linkCount");
        while (reader.Read())
        {
            records.Add(RecordSummaries.Map(type, reader, withParents) with
            {
                LinkedAt = reader.IsDBNull(linkedAt) ? null : reader.GetInt64(linkedAt),
                LinkCount = reader.GetInt32(linkCount),
            });
        }
    }

    private static string LinkedSql(string table, string orderBy) =>
        $"SELECT t.* FROM ({LinkedIdsSql}) AS lr JOIN `{table}` AS t ON t.id = lr.linkedRecordId ORDER BY {orderBy};";

    /// <summary>Number of links per linked record type, most common first.</summary>
    public Task<List<LinkedRecordTypeCount>> GetLinkedRecordTypeCountsAsync(string recordId) =>
        QueryAsync(
            "SELECT typeId AS recordTypeId, count(typeId) AS count FROM (" +
            "SELECT lrt1.record2TypeId AS typeId, lrt1.id AS id FROM LinkedRecord AS lrt1 WHERE record1Id = $recordId " +
            "UNION " +
            "SELECT lrt2.record1TypeId AS typeId, lrt2.id AS id FROM LinkedRecord AS lrt2 WHERE record2Id = $recordId) " +
            "GROUP BY typeId ORDER BY count(typeId) DESC;",
            r => new LinkedRecordTypeCount((int)GetInt64(r, "recordTypeId"), (int)GetInt64(r, "count")),
            ("$recordId", recordId));

    /// <summary>Ids of all records linked to the given record.</summary>
    public async Task<HashSet<string>> GetRecordIdsLinkedToRecordIdAsync(string recordId) =>
        (await QueryAsync($"SELECT linkedRecordId FROM ({LinkedIdsSql});", r => r.GetString(0), ("$recordId", recordId))
            .ConfigureAwait(false)).ToHashSet();

    /// <summary>Links two records. Does nothing if they are already linked, in either direction.</summary>
    public Task AddLinkedRecordAsync(LinkedRecord link) =>
        ExecuteAsync(RecordType.LinkedRecord,
            AddLinkSql,
            ("$id", link.Id), ("$ownerDbId", link.OwnerDbId), ("$record1Id", link.Record1Id),
            ("$record2Id", link.Record2Id), ("$record1TypeId", link.Record1TypeId), ("$record2TypeId", link.Record2TypeId),
            ("$now", EpochTime.UtcNowMillis()));

    /// <summary>Inserts a link stamped with its created time, unless the records are already linked.</summary>
    private const string AddLinkSql =
        "INSERT INTO LinkedRecord (id, ownerDbId, record1Id, record2Id, record1TypeId, record2TypeId, createdAt) " +
        "SELECT $id, $ownerDbId, $record1Id, $record2Id, $record1TypeId, $record2TypeId, $now " +
        "WHERE NOT EXISTS (SELECT 1 FROM LinkedRecord " +
        "WHERE (record1Id = $record1Id AND record2Id = $record2Id) OR (record1Id = $record2Id AND record2Id = $record1Id));";

    /// <summary>Links the source record to each target record, in one transaction.</summary>
    public async Task AddLinkedRecordsAsync(string sourceId, RecordType sourceType, IEnumerable<SearchResult> targets)
    {
        var targetList = targets.ToList();
        if (targetList.Count == 0)
        {
            return;
        }

        await Task.Run(() =>
        {
            var now = EpochTime.UtcNowMillis();
            using var connection = Database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            foreach (var target in targetList)
            {
                using var command = CreateCommand(connection, AddLinkSql,
                    [
                        ("$id", Note.NewId()), ("$ownerDbId", "db1"), ("$record1Id", sourceId),
                        ("$record2Id", target.RecordId), ("$record1TypeId", (int)sourceType),
                        ("$record2TypeId", target.RecordTypeId), ("$now", now),
                    ],
                    transaction);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }).ConfigureAwait(false);
        Database.NotifyChanged(RecordType.LinkedRecord);
    }

    /// <summary>Removes every link to the record. If any was removed, lastDeletionAt is saved in the same transaction.</summary>
    public Task<int> DeleteLinksToRecordIdAsync(string recordId) =>
        DeleteLinksRecordingDeletionAsync(
            "DELETE FROM LinkedRecord WHERE record1Id = $id OR record2Id = $id;", ("$id", recordId));

    private async Task<int> DeleteLinksRecordingDeletionAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        var rows = await Task.Run(() =>
        {
            using var connection = Database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            int deleted;
            using (var command = CreateCommand(connection, sql, parameters, transaction))
            {
                deleted = command.ExecuteNonQuery();
            }
            if (deleted > 0)
            {
                SettingsRepository.RecordDeletion(connection, transaction);
            }
            transaction.Commit();
            return deleted;
        }).ConfigureAwait(false);
        Database.NotifyChanged(RecordType.LinkedRecord);
        return rows;
    }
}
