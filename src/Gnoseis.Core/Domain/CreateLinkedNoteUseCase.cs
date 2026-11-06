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
/// Writes a note and its link to a record in one transaction: either both are saved or neither.
/// Used by the contact page's note box.
/// </summary>
public sealed class CreateLinkedNoteUseCase(GnoseisDatabase database)
{
    /// <summary>
    /// Saves a note with <paramref name="title"/> (null when blank, never taken from the text) and
    /// <paramref name="text"/> as typed, dated <paramref name="day"/> at <paramref name="timeOfDay"/>,
    /// linked to <paramref name="recordId"/>. Both rows are stamped with the current time.
    /// </summary>
    public async Task<Note> InvokeAsync(string recordId, RecordType recordType, string? title, string? text, DateOnly day, TimeSpan timeOfDay)
    {
        var (createDate, createDateTime) = NoteDates.ForNewNote(day, timeOfDay);
        var note = new Note
        {
            OwnerDbId = "db1",
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            TextContents = text,
            CreateDate = createDate,
            CreateDateTime = createDateTime,
        };

        await Task.Run(() =>
        {
            var now = EpochTime.UtcNowMillis();
            using var connection = database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var insert = RepositoryBase.CreateCommand(connection,
                       "INSERT INTO Note (id, ownerDbId, title, textContents, createDateTime, createDate, createdAt, updatedAt) " +
                       "VALUES ($id, $ownerDbId, $title, $textContents, $createDateTime, $createDate, $now, $now);",
                       [
                           ("$id", note.Id), ("$ownerDbId", note.OwnerDbId), ("$title", note.Title),
                           ("$textContents", note.TextContents), ("$createDateTime", note.CreateDateTime),
                           ("$createDate", note.CreateDate), ("$now", now),
                       ],
                       transaction))
            {
                insert.ExecuteNonQuery();
            }
            using (var link = RepositoryBase.CreateCommand(connection, LinkedRecordRepository.AddLinkSql,
                       [
                           ("$id", Note.NewId()), ("$ownerDbId", "db1"), ("$record1Id", recordId), ("$record2Id", note.Id),
                           ("$record1TypeId", (int)recordType), ("$record2TypeId", (int)RecordType.Note), ("$now", now),
                       ],
                       transaction))
            {
                link.ExecuteNonQuery();
            }
            transaction.Commit();
        }).ConfigureAwait(false);

        database.NotifyChanged(RecordType.Note, RecordType.LinkedRecord);
        return note;
    }
}
