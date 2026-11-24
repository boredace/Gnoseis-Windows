/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using System.Text.RegularExpressions;

namespace Gnoseis.Core.Domain;

/// <summary>
/// Dates and times as shown on screen and read by a screen reader: always one of the culture's
/// standard formats (Windows' regional format for the current culture), never a fixed pattern.
/// File names and stored values don't use this.
/// </summary>
public static partial class DateText
{
    /// <summary>The short time ("t"): "9:30 AM" or "09:30", as the culture's clock has it.</summary>
    public static string Time(DateTime time, CultureInfo? culture = null) =>
        time.ToString("t", culture ?? CultureInfo.CurrentCulture);

    /// <summary>A time of day as the short time ("t").</summary>
    public static string Time(TimeSpan timeOfDay, CultureInfo? culture = null) =>
        Time(DateTime.MinValue.Add(timeOfDay), culture);

    /// <summary>The long date ("D"), with the weekday where the culture's long date has one.</summary>
    public static string LongDate(DateOnly day, CultureInfo? culture = null) =>
        day.ToString("D", culture ?? CultureInfo.CurrentCulture);

    /// <summary>The long date ("D") of <paramref name="time"/>.</summary>
    public static string LongDate(DateTime time, CultureInfo? culture = null) =>
        LongDate(DateOnly.FromDateTime(time), culture);

    /// <summary>
    /// The long date without its weekday, for lines too narrow for one: the culture's long date pattern
    /// with a leading or trailing weekday taken out. The full long date when the weekday is elsewhere.
    /// </summary>
    public static string LongDateWithoutWeekday(DateOnly day, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return day.ToString(LongDatePatternWithoutWeekday(culture), culture);
    }

    /// <summary>The culture's long date pattern with a leading or trailing weekday ("dddd") taken out.</summary>
    public static string LongDatePatternWithoutWeekday(CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.LongDatePattern;
        var stripped = WeekdayAtEnds().Replace(pattern, "").Trim();
        return stripped.Length == 0 || stripped.Contains("ddd", StringComparison.Ordinal) ? pattern : stripped;
    }

    /// <summary>The short date ("d").</summary>
    public static string ShortDate(DateOnly day, CultureInfo? culture = null) =>
        day.ToString("d", culture ?? CultureInfo.CurrentCulture);

    /// <summary>Month and day ("M"), in the culture's order.</summary>
    public static string MonthDay(DateOnly day, CultureInfo? culture = null) =>
        day.ToString("M", culture ?? CultureInfo.CurrentCulture);

    /// <summary>The short weekday name, then month and day ("M"): a day this year.</summary>
    public static string WeekdayMonthDay(DateOnly day, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return $"{day.ToString("ddd", culture)} {MonthDay(day, culture)}";
    }

    /// <summary>Year and month ("Y"), in the culture's order.</summary>
    public static string YearMonth(DateOnly day, CultureInfo? culture = null) =>
        day.ToString("Y", culture ?? CultureInfo.CurrentCulture);

    /// <summary>The long date ("D") and the short time ("t"), joined by ", ".</summary>
    public static string LongDateAndTime(DateTime time, CultureInfo? culture = null) =>
        $"{LongDate(time, culture)}, {Time(time, culture)}";

    /// <summary>The short date ("d") and the short time ("t"), joined by ", ".</summary>
    public static string ShortDateAndTime(DateTime time, CultureInfo? culture = null) =>
        $"{ShortDate(DateOnly.FromDateTime(time), culture)}, {Time(time, culture)}";

    /// <summary>
    /// The parts of the culture's formats that shown dates and times use: when this changes, the
    /// regional format changed and shown text needs redrawing.
    /// </summary>
    public static string FormatKey(CultureInfo culture)
    {
        var f = culture.DateTimeFormat;
        return string.Join("\u001f", culture.Name, f.ShortDatePattern, f.LongDatePattern, f.ShortTimePattern,
            f.MonthDayPattern, f.YearMonthPattern, f.AMDesignator, f.PMDesignator, f.FirstDayOfWeek.ToString());
    }

    // A weekday at the start with the separators after it ("dddd, "), or at the end with those before it.
    [GeneratedRegex(@"^\s*dddd[\s,.]*|[\s,]*dddd\s*$")]
    private static partial Regex WeekdayAtEnds();
}
