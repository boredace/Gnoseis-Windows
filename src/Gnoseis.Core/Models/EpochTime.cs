/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Models;

/// <summary>
/// Conversions for the epoch-millisecond timestamps stored by the Android app.
/// Android saves a note's picked date as UTC midnight of that calendar day and displays
/// note dates in UTC, so the same rules are used here to show identical dates on both platforms.
/// </summary>
public static class EpochTime
{
    /// <summary>UTC midnight of the given calendar day, in epoch milliseconds.</summary>
    public static long UtcMidnight(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>UTC midnight of today's local calendar day (default date of a new note).</summary>
    public static long UtcMidnightToday() => UtcMidnight(DateOnly.FromDateTime(DateTime.Now));

    /// <summary>Local midnight of today, in epoch milliseconds (Android Note() default createDate).</summary>
    public static long LocalMidnightNow() =>
        new DateTimeOffset(DateTime.Today).ToUnixTimeMilliseconds();

    /// <summary>The instant as a UTC date/time, the way Android displays note timestamps.</summary>
    public static DateTimeOffset ToUtc(long epochMillis) => DateTimeOffset.FromUnixTimeMilliseconds(epochMillis);

    /// <summary>The UTC calendar day of the instant.</summary>
    public static DateOnly ToUtcDate(long epochMillis) => DateOnly.FromDateTime(ToUtc(epochMillis).UtcDateTime);
}
