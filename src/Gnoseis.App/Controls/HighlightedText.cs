/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using TextRange = Gnoseis.Core.Domain.TextRange;

namespace Gnoseis.App.Controls;

/// <summary>
/// One line of text with parts highlighted: semibold, underlined by a 2 px line in the record type's
/// outline colour. The line is drawn under the text, so it keeps that colour (an underlined run takes
/// the text colour). Screen readers read the plain text.
/// </summary>
public sealed partial class HighlightedText : Grid
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(HighlightedText), new PropertyMetadata("", (d, _) => ((HighlightedText)d).Rebuild()));

    public static readonly DependencyProperty RangesProperty = DependencyProperty.Register(
        nameof(Ranges), typeof(IReadOnlyList<TextRange>), typeof(HighlightedText), new PropertyMetadata(null, (d, _) => ((HighlightedText)d).Rebuild()));

    public static readonly DependencyProperty TypeProperty = DependencyProperty.Register(
        nameof(Type), typeof(RecordType), typeof(HighlightedText), new PropertyMetadata(RecordType.Note, (d, _) => ((HighlightedText)d).Redraw()));

    public static readonly DependencyProperty TextStyleProperty = DependencyProperty.Register(
        nameof(TextStyle), typeof(Style), typeof(HighlightedText), new PropertyMetadata(null, (d, e) => ((HighlightedText)d)._text.Style = (Style)e.NewValue));

    public static readonly DependencyProperty UsesAccentProperty = DependencyProperty.Register(
        nameof(UsesAccent), typeof(bool), typeof(HighlightedText), new PropertyMetadata(false, (d, _) => ((HighlightedText)d).Redraw()));

    private const double LineThickness = 2;

    private readonly TextBlock _text = new()
    {
        MaxLines = 1,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
    };

    private readonly Canvas _lines = new() { IsHitTestVisible = false };
    private readonly List<Run> _highlighted = [];
    private List<Rect> _drawn = [];

    public HighlightedText()
    {
        AutomationProperties.SetAccessibilityView(_lines, AccessibilityView.Raw);
        Children.Add(_lines);
        Children.Add(_text);
        ActualThemeChanged += (_, _) => Redraw();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The highlighted parts of <see cref="Text"/>.</summary>
    public IReadOnlyList<TextRange>? Ranges
    {
        get => (IReadOnlyList<TextRange>?)GetValue(RangesProperty);
        set => SetValue(RangesProperty, value);
    }

    /// <summary>The record type whose outline colour the underline takes.</summary>
    public RecordType Type
    {
        get => (RecordType)GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    /// <summary>Underlines in the accent colour instead of the record type's colour.</summary>
    public bool UsesAccent
    {
        get => (bool)GetValue(UsesAccentProperty);
        set => SetValue(UsesAccentProperty, value);
    }

    /// <summary>The text's colour; the theme's primary text colour when not set.</summary>
    public Brush? TextForeground
    {
        get => _text.Foreground;
        set => _text.Foreground = value;
    }

    public Style? TextStyle
    {
        get => (Style?)GetValue(TextStyleProperty);
        set => SetValue(TextStyleProperty, value);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        PlaceLines();
        return size;
    }

    private void Rebuild()
    {
        var text = Text ?? "";
        _text.Inlines.Clear();
        _highlighted.Clear();
        var position = 0;
        foreach (var range in (Ranges ?? []).Where(r => r.Start >= 0 && r.End <= text.Length).OrderBy(r => r.Start))
        {
            if (range.Start < position)
            {
                continue;
            }
            if (range.Start > position)
            {
                _text.Inlines.Add(new Run { Text = text[position..range.Start] });
            }
            var run = new Run { Text = text[range.Start..range.End], FontWeight = FontWeights.SemiBold };
            _text.Inlines.Add(run);
            _highlighted.Add(run);
            position = range.End;
        }
        if (position < text.Length)
        {
            _text.Inlines.Add(new Run { Text = text[position..] });
        }
        _drawn = [];
        InvalidateArrange();
    }

    private void Redraw()
    {
        _drawn = [];
        _lines.Children.Clear();
        InvalidateArrange();
    }

    /// <summary>Puts a line under each highlighted part that is shown (not cut off by the ellipsis).</summary>
    private void PlaceLines()
    {
        var width = _text.ActualWidth;
        var rects = new List<Rect>();
        foreach (var run in _highlighted)
        {
            var start = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var end = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            var left = Math.Min(start.X, end.X);
            var right = Math.Min(Math.Max(start.X, end.X + end.Width), width);
            if (right - left < 1 || double.IsNaN(left) || double.IsInfinity(right))
            {
                continue;
            }
            var bottom = Math.Max(start.Y + start.Height, end.Y + end.Height);
            rects.Add(new Rect(left, bottom - LineThickness + 1, right - left, LineThickness));
        }
        if (rects.SequenceEqual(_drawn) && _lines.Children.Count == rects.Count)
        {
            return;
        }
        _drawn = rects;
        _lines.Children.Clear();
        // The accent and text colours come from styles, so they follow this element's theme.
        var brush = UsesAccent ? null : RecordTypeStyles.Brush(this, Type, ColorRole.Outline);
        var style = (Style)Application.Current.Resources[UsesAccent ? "AccentLineStyle" : "TextLineStyle"];
        foreach (var rect in rects)
        {
            var line = new Rectangle { Width = rect.Width, Height = rect.Height };
            if (brush != null)
            {
                line.Fill = brush;
            }
            else
            {
                line.Style = style;
            }
            Canvas.SetLeft(line, rect.X);
            Canvas.SetTop(line, rect.Y);
            _lines.Children.Add(line);
        }
    }
}
