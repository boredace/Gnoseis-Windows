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

            Read(connection, "Note", recordId, records, r =>
            {
                var note = MapNote(r);
                return new LinkedRecordSummary
                {
                    Id = note.Id,
                    Type = RecordType.Note,
                    Name = note.Title?.Trim() ?? "",
                    FirstLine = RecordText.FirstLine(note.TextContents),
                    Text = RecordText.OneLine(note.TextContents),
                    NoteDate = note.CreateDate,
                    NoteDateTime = note.CreateDateTime,
                };
            });
            Read(connection, "Contact", recordId, records, r =>
            {
                var contact = MapContact(r);
                return new LinkedRecordSummary
                {
                    Id = contact.Id,
                    Type = RecordType.Contact,
                    Name = RecordText.ContactFullName(contact),
                    JobAndCompany = RecordText.JobAndCompany(contact),
                    MainContact = RecordText.MainContact(contact),
                    SortName = $"{contact.NameLast.Trim()}\u0001{contact.NameFirst?.Trim()}",
                };
            });
            Read(connection, "Organization", recordId, records, r =>
            {
                var organization = MapOrganization(r);
                return Named(organization.Id, RecordType.Organization, organization.OrganizationName, organization.Comments);
            });

            var categoryParents = new List<(string Id, string ParentId)>();
            Read(connection, "Category", recordId, records, r =>
            {
                var category = MapCategory(r);
                if (!string.IsNullOrEmpty(category.ParentId))
                {
                    categoryParents.Add((category.Id, category.ParentId));
                }
                return Named(category.Id, RecordType.Category, category.CategoryName, category.Comments);
            });
            var itemParents = new List<(string Id, string ParentId)>();
            Read(connection, "Item", recordId, records, r =>
            {
                var item = MapItem(r);
                if (!string.IsNullOrEmpty(item.ParentId))
                {
                    itemParents.Add((item.Id, item.ParentId));
                }
                return Named(item.Id, RecordType.Item, item.ItemName, item.Comments);
            });

            AddParentPaths(connection, RecordType.Category, "categoryName", categoryParents, records);
            AddParentPaths(connection, RecordType.Item, "itemName", itemParents, records);
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

    private static void Read(SqliteConnection connection, string table, string recordId, List<LinkedRecordSummary> records,
        Func<SqliteDataReader, LinkedRecordSummary> map)
    {
        using var command = CreateCommand(connection,
            $"SELECT t.*, lr.linkedAt AS linkedAt, {LinkCountSql} AS linkCount " +
            $"FROM ({LinkedIdsWithTimeSql}) AS lr JOIN `{table}` AS t ON t.id = lr.linkedRecordId;",
            [("$recordId", recordId)]);
        using var reader = command.ExecuteReader();
        var linkedAt = reader.GetOrdinal("linkedAt");
        var linkCount = reader.GetOrdinal("linkCount");
        while (reader.Read())
        {
            records.Add(map(reader) with
            {
                LinkedAt = reader.IsDBNull(linkedAt) ? null : reader.GetInt64(linkedAt),
                LinkCount = reader.GetInt32(linkCount),
            });
        }
    }

    private static LinkedRecordSummary Named(string id, RecordType type, string name, string? comments) => new()
    {
        Id = id,
        Type = type,
        Name = name.Trim(),
        FirstLine = RecordText.FirstLine(comments),
        SortName = name.Trim(),
    };

    /// <summary>Sets the parent path of each record: its parents' names, top first, joined by " › ".</summary>
    private static void AddParentPaths(SqliteConnection connection, RecordType type, string nameColumn,
        List<(string Id, string ParentId)> withParents, List<LinkedRecordSummary> records)
    {
        if (withParents.Count == 0)
        {
            return;
        }

        var tree = new Dictionary<string, (string? ParentId, string Name)>(StringComparer.Ordinal);
        using (var command = CreateCommand(connection, $"SELECT id, parentId, {nameColumn} FROM `{type}`;", []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                tree[reader.GetString(0)] = (reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2).Trim());
            }
        }

        foreach (var (id, parentId) in withParents)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            var current = parentId;
            // Stops at a missing parent or a loop.
            while (current != null && seen.Add(current) && tree.TryGetValue(current, out var parent))
            {
                if (parent.Name.Length > 0)
                {
                    names.Add(parent.Name);
                }
                current = string.IsNullOrEmpty(parent.ParentId) ? null : parent.ParentId;
            }
            names.Reverse();
            var index = records.FindIndex(r => r.Type == type && r.Id == id);
            records[index] = records[index] with { ParentPath = string.Join(" › ", names) };
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
