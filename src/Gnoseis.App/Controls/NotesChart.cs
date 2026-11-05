/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Controls;

/// <summary>
/// Home's "Notes, last 14 days": one bar per day in the Note outline colour, oldest first, with the
/// day of the month under every other bar (today's in bold). Each day's count is in its tooltip and
/// automation name.
/// </summary>
public sealed partial class NotesChart : UserControl
{
    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
        nameof(Days), typeof(IReadOnlyList<DayCount>), typeof(NotesChart), new PropertyMetadata(null, (d, _) => ((NotesChart)d).Build()));

    private const double BarAreaHeight = 120;

    private readonly Grid _grid = new() { ColumnSpacing = 4 };

    public NotesChart()
    {
        Content = _grid;
    }

    public IReadOnlyList<DayCount>? Days
    {
        get => (IReadOnlyList<DayCount>?)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    private void Build()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        var days = Days ?? [];
        var max = Math.Max(1, days.Count == 0 ? 0 : days.Max(d => d.Count));
        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var isLast = i == days.Count - 1;
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bar = new Border
            {
                Height = day.Count == 0 ? 2 : Math.Max(4, BarAreaHeight * day.Count / max),
                VerticalAlignment = VerticalAlignment.Bottom,
                Style = (Style)Application.Current.Resources[day.Count == 0 ? "NotesChartEmptyBarStyle" : "NotesChartBarStyle"],
            };
            var label = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)Application.Current.Resources[isLast ? "NotesChartTodayTextStyle" : "NotesChartDayTextStyle"],
                Text = (days.Count - 1 - i) % 2 == 0 ? day.Day.Day.ToString(CultureInfo.CurrentCulture) : "",
            };
            // Transparent background so the whole column shows the tooltip, not only the bar.
            var column = new Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                RowSpacing = 6,
            };
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarAreaHeight) });
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            column.Children.Add(bar);
            Grid.SetRow(label, 1);
            column.Children.Add(label);

            var description = Describe(day);
            ToolTipService.SetToolTip(column, description);
            AutomationProperties.SetName(column, description);
            AutomationProperties.SetAccessibilityView(column, AccessibilityView.Content);
            AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
            Grid.SetColumn(column, i);
            _grid.Children.Add(column);
        }
    }

    /// <summary>"2026-11-03, Tue: 2 notes".</summary>
    public static string Describe(DayCount day) =>
        $"{day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, {day.Day.ToString("ddd", CultureInfo.CurrentCulture)}: " +
        (day.Count == 1 ? "1 note" : $"{day.Count.ToString(CultureInfo.CurrentCulture)} notes");
}
