/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;

namespace Gnoseis.Core.Models;

/// <summary>The number of records of each type and of links (Home count strip).</summary>
public sealed record HomeCounts(IReadOnlyDictionary<RecordType, int> Records, int Links)
{
    public int Of(RecordType type) => Records.GetValueOrDefault(type);
}

/// <summary>A contact, organization, category or item with its number of links (Most linked).</summary>
/// <param name="Name">A contact's "First Last", or the organization, category or item name.</param>
/// <param name="LinkCount">The number of LinkedRecord rows with the record at either end.</param>
public sealed record MostLinkedRecord(string Id, RecordType Type, string Name, int LinkCount);

/// <summary>The two ends of a link.</summary>
public sealed record LinkEnds(string Record1Id, string Record2Id);

/// <summary>The number of links with at least one end of a type (Most linked type picker).</summary>
public sealed record TypeLinkCount(RecordType Type, int LinkCount);

/// <summary>The number of notes on one note day (UTC calendar day, as the note list groups them).</summary>
public sealed record DayCount(DateOnly Day, int Count);

/// <summary>The newest notes that aren't linked to anything, and how many there are in all.</summary>
public sealed record UnlinkedNotes(IReadOnlyList<LinkedRecordSummary> Newest, int TotalCount);

/// <summary>What the backup reminder shows.</summary>
public enum BackupReminderKind
{
    /// <summary>Backed up, and nothing changed since: no reminder.</summary>
    Hidden,

    /// <summary>The database was never backed up (no lastBackupAt).</summary>
    NeverBackedUp,

    /// <summary>Records changed, or were deleted, after the last backup.</summary>
    ChangedSince,
}

/// <summary>
/// The backup reminder: never backed up, or records changed or deleted since the last backup
/// (lastBackupAt and lastDeletionAt from the Settings table, and the record timestamps).
/// </summary>
public sealed record BackupReminder(BackupReminderKind Kind, long? LastBackupAt = null, int ChangeCount = 0, bool DeletedSince = false)
{
    public const string NeverBackedUpMessage =
        "Your database isn't backed up. It's stored only on this PC, and you'll lose it if the PC fails or is lost. Back it up to keep it safe.";

    public static BackupReminder From(long? lastBackupAt, long? lastDeletionAt, int changeCount)
    {
        if (lastBackupAt is not { } backupAt)
        {
            return new(BackupReminderKind.NeverBackedUp);
        }
        var deletedSince = lastDeletionAt is { } deletionAt && deletionAt > backupAt;
        return changeCount == 0 && !deletedSince
            ? new(BackupReminderKind.Hidden, backupAt)
            : new(BackupReminderKind.ChangedSince, backupAt, changeCount, deletedSince);
    }

    /// <summary>
    /// The reminder's text, or empty when hidden. The backup date is shown in the local time zone
    /// (lastBackupAt is a UTC time, not a note day).
    /// </summary>
    public string Message(TimeZoneInfo zone, CultureInfo culture)
    {
        switch (Kind)
        {
            case BackupReminderKind.NeverBackedUp:
                return NeverBackedUpMessage;
            case BackupReminderKind.ChangedSince:
                var date = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(LastBackupAt ?? 0), zone);
                var changes = ChangeCount switch
                {
                    0 => null,
                    1 => "1 record changed since",
                    _ => $"{ChangeCount.ToString("N0", culture)} records changed since",
                };
                var detail = changes switch
                {
                    null => "Some records were deleted since.",
                    _ when DeletedSince => $"{changes} and some records were deleted.",
                    _ => $"{changes}.",
                };
                return $"Last backup {date.ToString("d MMM yyyy", culture)}. {detail}";
            default:
                return "";
        }
    }
}
