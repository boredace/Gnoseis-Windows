/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>
/// Unlinks two records, and links them again for Undo. Neither record is deleted.
/// </summary>
public sealed class UnlinkRecordsUseCase(GnoseisDatabase database, LinkedRecordRepository linkedRecords)
{
    private const string BetweenSql = "(record1Id = $a AND record2Id = $b) OR (record1Id = $b AND record2Id = $a)";

    /// <summary>
    /// Removes every link between the two records, in either direction (old duplicate links too).
    /// If a link was removed, lastDeletionAt is saved in the same transaction. Returns the removed
    /// link, so Undo can link the records again the same way round, or null if they weren't linked
    /// (already unlinked, or one of them deleted meanwhile).
    /// </summary>
    public async Task<LinkedRecord?> InvokeAsync(string recordId, string linkedRecordId)
    {
        var removed = await Task.Run(() =>
        {
            using var connection = database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            (string, object?)[] parameters = [("$a", recordId), ("$b", linkedRecordId)];

            LinkedRecord? link = null;
            using (var select = RepositoryBase.CreateCommand(connection,
                       $"SELECT * FROM LinkedRecord WHERE {BetweenSql} ORDER BY createdAt IS NULL, createdAt DESC LIMIT 1;",
                       parameters, transaction))
            using (var reader = select.ExecuteReader())
            {
                if (reader.Read())
                {
                    var createdAt = reader.GetOrdinal("createdAt");
                    link = new LinkedRecord
                    {
                        Id = reader.GetString(reader.GetOrdinal("id")),
                        OwnerDbId = reader.GetString(reader.GetOrdinal("ownerDbId")),
                        Record1Id = reader.GetString(reader.GetOrdinal("record1Id")),
                        Record2Id = reader.GetString(reader.GetOrdinal("record2Id")),
                        Record1TypeId = reader.GetInt32(reader.GetOrdinal("record1TypeId")),
                        Record2TypeId = reader.GetInt32(reader.GetOrdinal("record2TypeId")),
                        CreatedAt = reader.IsDBNull(createdAt) ? null : reader.GetInt64(createdAt),
                    };
                }
            }

            int deleted;
            using (var delete = RepositoryBase.CreateCommand(connection, $"DELETE FROM LinkedRecord WHERE {BetweenSql};", parameters, transaction))
            {
                deleted = delete.ExecuteNonQuery();
            }
            if (deleted > 0)
            {
                SettingsRepository.RecordDeletion(connection, transaction);
            }
            transaction.Commit();
            return deleted > 0 ? link : null;
        }).ConfigureAwait(false);

        if (removed != null)
        {
            database.NotifyChanged(RecordType.LinkedRecord);
        }
        return removed;
    }

    /// <summary>
    /// Undo: links the two records of <paramref name="removed"/> again, the same way round, as a
    /// new link (new id, new createdAt). lastDeletionAt stays as it was written.
    /// </summary>
    public Task UndoAsync(LinkedRecord removed) =>
        linkedRecords.AddLinkedRecordAsync(removed with { Id = Note.NewId(), CreatedAt = null });
}
