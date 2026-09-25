/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>
/// The createDate / createDateTime values written when a note is saved.
/// Android stores the picked day as UTC midnight of that day and shows note times in UTC. The time
/// of day is added as an offset from that midnight, so both apps show the time the note was written.
/// </summary>
public static class NoteDates
{
    public static (long CreateDate, long CreateDateTime) ForNewNote(DateOnly day, TimeSpan timeOfDay)
    {
        var createDate = EpochTime.UtcMidnight(day);
        return (createDate, createDate + (long)timeOfDay.TotalMilliseconds);
    }

    /// <summary>
    /// Keeps the note's dates unless a different day was picked (fixes Android known bug 9, where
    /// every edit overwrote the creation day).
    /// </summary>
    public static (long CreateDate, long CreateDateTime) ForEditedNote(Note note, DateOnly pickedDay)
    {
        var currentDay = DisplayedDay(note);
        if (pickedDay == currentDay)
        {
            return (note.CreateDate, note.CreateDateTime);
        }

        var timeOfDay = note.CreateDateTime - EpochTime.UtcMidnight(currentDay);
        var createDate = EpochTime.UtcMidnight(pickedDay);
        return (createDate, createDate + timeOfDay);
    }

    /// <summary>The day shown for a note (its createDateTime in UTC, as on Android).</summary>
    public static DateOnly DisplayedDay(Note note) => EpochTime.ToUtcDate(note.CreateDateTime);
}
