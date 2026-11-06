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

/// <summary>Lays its children out left to right, starting a new line when the next one doesn't fit.</summary>
public sealed partial class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;

    public double VerticalSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var lineWidth = 0.0;
        var lineHeight = 0.0;
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (size.Width == 0 && size.Height == 0)
            {
                continue;
            }
            if (lineWidth > 0 && lineWidth + HorizontalSpacing + size.Width > availableSize.Width)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight + VerticalSpacing;
                lineWidth = 0;
                lineHeight = 0;
            }
            lineWidth += (lineWidth > 0 ? HorizontalSpacing : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        width = Math.Max(width, lineWidth);
        height += lineHeight;
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var y = 0.0;
        var lineHeight = 0.0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (size.Width == 0 && size.Height == 0)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            if (x > 0 && x + HorizontalSpacing + size.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }
            if (x > 0)
            {
                x += HorizontalSpacing;
            }
            child.Arrange(new Rect(x, y, Math.Min(size.Width, finalSize.Width), size.Height));
            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return finalSize;
    }
}
