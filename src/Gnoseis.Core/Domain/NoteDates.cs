/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>
/// How a note's day and time are stored and shown (schema version 4 on).
/// <list type="bullet">
/// <item><c>createDateTime</c> is the UTC instant of the note, in epoch milliseconds.</item>
/// <item><c>createDate</c> is the UTC instant of 00:00 of that instant's local day. Nothing reads it to
/// show, group or sort notes; it's written so the column keeps a plain meaning.</item>
/// <item>A note is shown, grouped and counted by its <c>createDateTime</c> in the device's time zone
/// (Windows' setting, <see cref="TimeZoneInfo.Local"/>), and sorted by <c>createDateTime</c>.</item>
/// <item>A local date and time becomes an instant with the UTC offset in effect just before a daylight
/// saving change when it falls in a gap or an overlap (as java.time's <c>LocalDateTime.atZone</c>,
/// which Android uses): a time in a gap moves later by the gap, a time in an overlap is the earlier
/// of its two moments, and a day starts at the end of a midnight gap.</item>
/// </list>
/// Every method takes the zone so tests can set it; the app passes <see cref="TimeZoneInfo.Local"/>.
/// </summary>
public static class NoteDates
{
    /// <summary>
    /// The instant of a local date and time in <paramref name="zone"/>, in epoch milliseconds, by the
    /// rule above. .NET's ConvertTimeToUtc throws for a time in a gap and picks the standard offset
    /// in an overlap, so neither is used here.
    /// </summary>
    public static long ToInstant(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (zone.IsInvalidTime(local))
        {
            // In a gap the offset grows. The offset just before the change is the one in effect at an
            // instant before the gap's earliest possible start: no zone is more than 14 hours from UTC,
            // so (local read as UTC) − 15 hours is before it.
            var before = DateTime.SpecifyKind(local, DateTimeKind.Utc).AddHours(-15);
            offset = zone.GetUtcOffset(before);
        }
        else if (zone.IsAmbiguousTime(local))
        {
            // In an overlap the offset shrinks: the one before the change is the larger, the earlier moment.
            offset = zone.GetAmbiguousTimeOffsets(local).Max();
        }
        else
        {
            offset = zone.GetUtcOffset(local);
        }
        return new DateTimeOffset(local.Ticks - offset.Ticks, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>The note's local date and time in <paramref name="zone"/>.</summary>
    public static DateTime Shown(long createDateTime, TimeZoneInfo zone) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds(createDateTime).UtcDateTime, zone),
            DateTimeKind.Unspecified);

    /// <summary>The note's local day in <paramref name="zone"/>.</summary>
    public static DateOnly ShownDay(long createDateTime, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(Shown(createDateTime, zone));

    /// <summary>The note's local time of day in <paramref name="zone"/>.</summary>
    public static TimeSpan ShownTimeOfDay(long createDateTime, TimeZoneInfo zone) =>
        Shown(createDateTime, zone).TimeOfDay;

    /// <summary>The instant 00:00 of <paramref name="day"/> begins in <paramref name="zone"/> (the end of a midnight gap).</summary>
    public static long StartOfDay(DateOnly day, TimeZoneInfo zone) =>
        ToInstant(day.ToDateTime(TimeOnly.MinValue), zone);

    /// <summary>Both columns for a note at the instant <paramref name="createDateTime"/>.</summary>
    public static (long CreateDate, long CreateDateTime) ForInstant(long createDateTime, TimeZoneInfo zone) =>
        (StartOfDay(ShownDay(createDateTime, zone), zone), createDateTime);

    /// <summary>Both columns for a note at a local day and time in <paramref name="zone"/>.</summary>
    public static (long CreateDate, long CreateDateTime) ToStored(DateOnly day, TimeOnly time, TimeZoneInfo zone) =>
        ForInstant(ToInstant(day.ToDateTime(time), zone), zone);

    /// <summary>Both columns for a new note whose day and time nobody changed: the moment of saving.</summary>
    public static (long CreateDate, long CreateDateTime) ForNow(DateTimeOffset now, TimeZoneInfo zone) =>
        ForInstant(now.ToUnixTimeMilliseconds(), zone);

    /// <summary>
    /// Both columns for a new note: the moment of saving when neither day nor time was picked,
    /// otherwise the picked day and time (seconds and milliseconds zero) by the rule.
    /// </summary>
    public static (long CreateDate, long CreateDateTime) ForNewNote(
        DateOnly? pickedDay, TimeOnly? pickedTime, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (pickedDay == null && pickedTime == null)
        {
            return ForNow(now, zone);
        }
        var shownNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var day = pickedDay ?? DateOnly.FromDateTime(shownNow);
        var time = pickedTime ?? TimeOnly.FromDateTime(shownNow);
        return ToStored(day, WholeMinutes(time), zone);
    }

    /// <summary>
    /// Upgrading a schema version 3 (or older) note: its stored createDateTime, read as UTC, is the local
    /// date and time it was written. That becomes an instant in <paramref name="zone"/>, and createDate is
    /// recomputed from it (the old createDate isn't used).
    /// </summary>
    public static (long CreateDate, long CreateDateTime) UpgradeFromV3(long oldCreateDateTime, TimeZoneInfo zone) =>
        ForInstant(ToInstant(DateTimeOffset.FromUnixTimeMilliseconds(oldCreateDateTime).UtcDateTime, zone), zone);

    /// <summary>
    /// An existing note's columns after an edit. A day or time that is null, or equal to the one shown
    /// in <paramref name="zone"/> (the time to the minute), counts as unchanged. When neither changed,
    /// both columns stay exactly as stored. Otherwise a new day keeps the shown time, a new time
    /// (seconds and milliseconds zero) keeps the shown day, and both columns are recomputed.
    /// </summary>
    public static (long CreateDate, long CreateDateTime) ForEditedNote(
        Note note, DateOnly? pickedDay, TimeOnly? pickedTime, TimeZoneInfo zone)
    {
        var shown = Shown(note.CreateDateTime, zone);
        var shownDay = DateOnly.FromDateTime(shown);
        var shownTime = TimeOnly.FromDateTime(shown);
        var dayChanged = pickedDay is { } d && d != shownDay;
        var timeChanged = pickedTime is { } t && WholeMinutes(t) != WholeMinutes(shownTime);
        if (!dayChanged && !timeChanged)
        {
            return (note.CreateDate, note.CreateDateTime);
        }
        var day = dayChanged ? pickedDay!.Value : shownDay;
        var time = timeChanged ? WholeMinutes(pickedTime!.Value) : shownTime;
        return ToStored(day, time, zone);
    }

    /// <summary>The time with seconds and milliseconds zero (what the time pickers set).</summary>
    public static TimeOnly WholeMinutes(TimeOnly time) => new(time.Hour, time.Minute);
}
