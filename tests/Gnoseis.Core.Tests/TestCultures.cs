/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Two regional formats made from explicit patterns (never the PC's), which differ in clock and in the
/// order of day and month: shown dates and times must follow whichever one is current.
/// </summary>
internal static class TestCultures
{
    /// <summary>12-hour clock, month before day: "h:mm tt", "MMMM d", "dddd, MMMM d, yyyy", "M/d/yyyy", "MMMM yyyy".</summary>
    public static CultureInfo TwelveHour { get; } = Make("h:mm tt", "MMMM d", "dddd, MMMM d, yyyy", "M/d/yyyy", "MMMM yyyy");

    /// <summary>24-hour clock, day before month: "HH:mm", "d MMMM", "dddd d MMMM yyyy", "dd/MM/yyyy", "yyyy MMMM".</summary>
    public static CultureInfo TwentyFourHour { get; } = Make("HH:mm", "d MMMM", "dddd d MMMM yyyy", "dd/MM/yyyy", "yyyy MMMM");

    private static CultureInfo Make(string shortTime, string monthDay, string longDate, string shortDate, string yearMonth)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        var f = culture.DateTimeFormat;
        f.ShortTimePattern = shortTime;
        f.MonthDayPattern = monthDay;
        f.LongDatePattern = longDate;
        f.ShortDatePattern = shortDate;
        f.YearMonthPattern = yearMonth;
        f.AMDesignator = "AM";
        f.PMDesignator = "PM";
        return CultureInfo.ReadOnly(culture);
    }
}
