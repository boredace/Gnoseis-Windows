/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Gnoseis.App.Controls;

/// <summary>
/// Lays out Home's five cards, given in the narrow order: Most linked, Recent notes, the notes
/// chart, Unlinked notes, Recently opened. Narrow: one column in that order. Wide: a main column
/// (two thirds) with Most linked, then the chart and unlinked notes side by side, and a second
/// column (a third, at least <see cref="SideMinWidth"/>) with recent notes and recently opened.
/// The two columns stack independently. A collapsed card takes no room; the chart takes the
/// main column's width when the unlinked notes are collapsed.
/// </summary>
public sealed partial class HomeColumnsPanel : Panel
{
    public static readonly DependencyProperty IsNarrowProperty = DependencyProperty.Register(
        nameof(IsNarrow), typeof(bool), typeof(HomeColumnsPanel), new PropertyMetadata(false, (d, _) => ((HomeColumnsPanel)d).InvalidateMeasure()));

    public bool IsNarrow
    {
        get => (bool)GetValue(IsNarrowProperty);
        set => SetValue(IsNarrowProperty, value);
    }

    public double Spacing { get; set; } = 16;

    public double SideMinWidth { get; set; } = 300;

    private UIElement? MostLinked => Child(0);
    private UIElement? RecentNotes => Child(1);
    private UIElement? Chart => Child(2);
    private UIElement? Unlinked => Child(3);
    private UIElement? RecentlyOpened => Child(4);

    private UIElement? Child(int index) => index < Children.Count ? Children[index] : null;

    private static bool Shown(UIElement? e) => e != null && e.Visibility == Visibility.Visible;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        var rects = Plan(width, measure: true);
        var height = rects.Count == 0 ? 0 : rects.Max(r => r.Bottom);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rects = Plan(finalSize.Width, measure: false);
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(i < rects.Count ? rects[i] : new Rect(0, 0, 0, 0));
        }
        return finalSize;
    }

    /// <summary>Each child's rectangle (in child order); measures the children first when <paramref name="measure"/>.</summary>
    private List<Rect> Plan(double width, bool measure)
    {
        var rects = Enumerable.Repeat(new Rect(0, 0, 0, 0), Children.Count).ToList();

        double Place(UIElement? element, double x, double y, double w)
        {
            if (!Shown(element))
            {
                return y;
            }
            if (measure)
            {
                element!.Measure(new Size(w, double.PositiveInfinity));
            }
            var h = element!.DesiredSize.Height;
            rects[Children.IndexOf(element)] = new Rect(x, y, w, h);
            return y + h + Spacing;
        }

        if (IsNarrow)
        {
            var y = 0.0;
            foreach (var child in Children)
            {
                y = Place(child, 0, y, width);
            }
            return rects;
        }

        var side = Math.Max(SideMinWidth, (width - Spacing) / 3);
        var main = Math.Max(0, width - Spacing - side);

        var left = Place(MostLinked, 0, 0, main);
        if (Shown(Unlinked))
        {
            var half = Math.Max(0, (main - Spacing) / 2);
            var chartBottom = Place(Chart, 0, left, half);
            var unlinkedBottom = Place(Unlinked, half + Spacing, left, half);
            left = Math.Max(chartBottom, unlinkedBottom);
        }
        else
        {
            left = Place(Chart, 0, left, main);
        }

        var right = Place(RecentNotes, main + Spacing, 0, side);
        Place(RecentlyOpened, main + Spacing, right, side);
        return rects;
    }
}
