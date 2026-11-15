/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Note times for test data, by the note time rule in Windows' time zone (the one the pages show notes
/// in by default), so a test sees the day and time it set on any PC.
/// </summary>
internal static class LocalNoteTimes
{
    /// <summary>The instant 00:00 of the local day begins (a note's createDate).</summary>
    public static long Start(DateOnly day) => NoteDates.StartOfDay(day, TimeZoneInfo.Local);

    /// <summary>The instant of the local day and time <paramref name="minutes"/> after midnight.</summary>
    public static long At(DateOnly day, long minutes) =>
        NoteDates.ToInstant(day.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes), TimeZoneInfo.Local);
}
