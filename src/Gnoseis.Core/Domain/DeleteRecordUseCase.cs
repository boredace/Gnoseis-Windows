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
/// Deletes a record and every link to it. Linked records themselves are kept.
/// Both steps run in one transaction, so a failure never leaves links to a deleted record.
/// </summary>
public sealed class DeleteRecordUseCase(GnoseisDatabase database)
{
    public async Task InvokeAsync(string recordId, RecordType recordType)
    {
        var table = recordType switch
        {
            RecordType.Note => "Note",
            RecordType.Contact => "Contact",
            RecordType.Organization => "Organization",
            RecordType.Category => "Category",
            RecordType.Item => "Item",
            _ => throw new ArgumentOutOfRangeException(nameof(recordType), recordType, "Only user records can be deleted."),
        };

        await Task.Run(() =>
        {
            using var connection = database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var deleteLinks = RepositoryBase.CreateCommand(connection,
                       "DELETE FROM LinkedRecord WHERE record1Id = $id OR record2Id = $id;", [("$id", recordId)], transaction))
            {
                deleteLinks.ExecuteNonQuery();
            }
            using (var deleteRecord = RepositoryBase.CreateCommand(connection,
                       $"DELETE FROM `{table}` WHERE id = $id;", [("$id", recordId)], transaction))
            {
                deleteRecord.ExecuteNonQuery();
            }
            transaction.Commit();
        }).ConfigureAwait(false);

        database.NotifyChanged(recordType, RecordType.LinkedRecord);
    }
}
