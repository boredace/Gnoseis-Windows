/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Domain;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI.ViewManagement;

namespace Gnoseis.App.Controls;

/// <summary>
/// Home's Most linked bubbles: one per record, in its type's colours, sized and packed by
/// <see cref="BubbleLayout"/>, with dashed lines between records linked to each other. Bubbles
/// are buttons in size order (Tab moves from the largest), Enter or a click opens the record.
/// </summary>
public sealed partial class MostLinkedChart : UserControl
{
    private readonly Canvas _lines = new();
    private readonly Canvas _bubbles = new();
    private readonly UISettings _uiSettings = new();
    private IReadOnlyList<MostLinkedRecord> _records = [];
    private IReadOnlyList<LinkEnds> _links = [];

    public MostLinkedChart()
    {
        var root = new Grid();
        root.Children.Add(_lines);
        root.Children.Add(_bubbles);
        Content = root;
        AutomationProperties.SetName(_bubbles, "Most linked records");
        SizeChanged += (_, _) => Build();
        ActualThemeChanged += (_, _) => Build();
        // High contrast on or off changes the type brushes, not the element's theme.
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(Build);
    }

    /// <summary>Raised when a bubble is clicked or invoked with the keyboard.</summary>
    public event EventHandler<MostLinkedRecord>? RecordInvoked;

    /// <summary>Shows <paramref name="records"/> (most linked first) and the links among them.</summary>
    public void Show(IReadOnlyList<MostLinkedRecord> records, IReadOnlyList<LinkEnds> links)
    {
        _records = records;
        _links = links;
        Build();
    }

    private void Build()
    {
        _lines.Children.Clear();
        _bubbles.Children.Clear();
        var width = ActualWidth;
        var height = ActualHeight;
        if (_records.Count == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        var placed = BubbleLayout.Layout(_records.Select(r => r.LinkCount).ToList(), width, height);
        var byId = new Dictionary<string, PlacedBubble>(StringComparer.Ordinal);
        for (var i = 0; i < _records.Count; i++)
        {
            byId[_records[i].Id] = placed[i];
        }

        foreach (var link in _links.DistinctBy(l => string.CompareOrdinal(l.Record1Id, l.Record2Id) < 0 ? (l.Record1Id, l.Record2Id) : (l.Record2Id, l.Record1Id)))
        {
            if (link.Record1Id != link.Record2Id && byId.TryGetValue(link.Record1Id, out var a) && byId.TryGetValue(link.Record2Id, out var b))
            {
                _lines.Children.Add(new Line
                {
                    X1 = a.X,
                    Y1 = a.Y,
                    X2 = b.X,
                    Y2 = b.Y,
                    Style = (Style)Application.Current.Resources["BubbleLinkLineStyle"],
                });
            }
        }

        // Added in size order, so Tab moves from the most linked record to the least.
        for (var i = 0; i < _records.Count; i++)
        {
            var bubble = CreateBubble(_records[i], placed[i], isLargest: i == 0);
            Canvas.SetLeft(bubble, placed[i].X - placed[i].Diameter / 2);
            Canvas.SetTop(bubble, placed[i].Y - placed[i].Diameter / 2);
            _bubbles.Children.Add(bubble);
        }
    }

    private Button CreateBubble(MostLinkedRecord record, PlacedBubble place, bool isLargest)
    {
        var d = place.Diameter;
        var outline = RecordTypeStyles.Brush(this, record.Type, ColorRole.Outline);
        var countText = record.LinkCount.ToString("N0", CultureInfo.CurrentCulture);
        var content = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        if (d >= 64)
        {
            content.Children.Add(new FontIcon
            {
                FontSize = isLargest ? 16 : 13,
                Glyph = Glyphs.For(record.Type),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            });
        }
        content.Children.Add(new TextBlock
        {
            Text = record.Name,
            MaxWidth = Math.Max(24, d * 0.78),
            FontSize = isLargest ? 14 : 12,
            FontWeight = isLargest ? FontWeights.SemiBold : FontWeights.Normal,
            Style = (Style)Application.Current.Resources["BubbleNameTextStyle"],
        });
        content.Children.Add(new TextBlock
        {
            Text = countText,
            FontSize = isLargest ? 14 : 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var button = new Button
        {
            Width = d,
            Height = d,
            CornerRadius = new CornerRadius(d / 2),
            Content = content,
            Style = (Style)Application.Current.Resources["BubbleButtonStyle"],
            Background = RecordTypeStyles.Brush(this, record.Type, ColorRole.Fill),
            BorderBrush = outline,
            Foreground = outline,
            Tag = record,
        };
        var links = record.LinkCount == 1 ? "1 link" : $"{countText} links";
        AutomationProperties.SetName(button, $"{record.Name}, {record.Type.DisplayName()}, {links}");
        ToolTipService.SetToolTip(button, $"{record.Name}, {links}");
        button.Click += (_, _) => RecordInvoked?.Invoke(this, record);
        return button;
    }
}
