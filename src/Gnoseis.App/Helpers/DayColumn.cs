/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Gnoseis.App.Helpers;

/// <summary>
/// The width of a list's day column: the widest day text of the current regional format, so a long
/// month name ("September 13") never breaks or trims, and every row's column is the same width. Measured
/// once per format and text size: a format change reloads the pages, which then read the new width.
/// </summary>
public static class DayColumn
{
    // Every day of a leap year: every month name and day number the column can show.
    private static readonly DateOnly[] Days =
        Enumerable.Range(0, 366).Select(i => new DateOnly(2024, 1, 1).AddDays(i)).ToArray();

    private static readonly UISettings Ui = new();
    private static readonly Dictionary<string, double> Widths = [];

    /// <summary>
    /// A history row's (contact, organization, item): the month and day over the weekday and year, at
    /// least the 64 px it had before.
    /// </summary>
    public static double History => Width("history", 64, culture => Math.Max(
        Widest(AppStyle("BodyStrongTextBlockStyle"), Days.Select(d => DateText.MonthDay(d, culture))),
        // The year varies its digits only; a week of days in a few years covers the weekday names.
        Widest(AppStyle("CaptionTextBlockStyle"), Days.Take(7).SelectMany(d =>
            Enumerable.Range(0, 10).Select(y => ContactHistory.WeekdayAndYearText(d.AddYears(y), culture))))));

    /// <summary>
    /// A category's note row, in <paramref name="style"/>: the month and day this year, or the long date
    /// without its weekday for an earlier year, at least the 76 px it had before.
    /// </summary>
    public static double CategoryNote(Style style) => Width("category", 76, culture =>
    {
        var lastYear = new DateOnly(2023, 1, 1);
        return Widest(style, Days.SelectMany(d => new[]
        {
            CategoryPage.NoteDayText(d, d, culture),
            CategoryPage.NoteDayText(d, lastYear, culture),
        }));
    });

    private static double Width(string list, double least, Func<CultureInfo, double> measure)
    {
        var culture = CultureInfo.CurrentCulture;
        var key = $"{list}\u001f{DateText.FormatKey(culture)}\u001f{Ui.TextScaleFactor}";
        if (!Widths.TryGetValue(key, out var width))
        {
            // Rounded up, with a pixel to spare for rounding at other display scales.
            width = Math.Max(least, Math.Ceiling(measure(culture)) + 1);
            Widths[key] = width;
        }
        return width;
    }

    private static double Widest(Style style, IEnumerable<string> texts)
    {
        var block = new TextBlock { Style = style, TextWrapping = TextWrapping.NoWrap };
        var widest = 0.0;
        foreach (var text in texts.Distinct())
        {
            block.Text = text;
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, block.DesiredSize.Width);
        }
        return widest;
    }

    private static Style AppStyle(string key) => (Style)Application.Current.Resources[key];
}
