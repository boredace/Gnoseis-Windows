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

    /// <summary>
    /// Removes the link between two records (whichever direction it was stored in). If a link was
    /// removed, lastDeletionAt is saved in the same transaction.
    /// </summary>
    public Task<int> DeleteLinkAsync(string recordId1, string recordId2) =>
        DeleteLinksRecordingDeletionAsync(
            "DELETE FROM LinkedRecord WHERE (record1Id = $a AND record2Id = $b) OR (record1Id = $b AND record2Id = $a);",
            ("$a", recordId1), ("$b", recordId2));

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
