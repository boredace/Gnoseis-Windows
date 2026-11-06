/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using System.Text.RegularExpressions;
using Gnoseis.App.Controls;
using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using NoteLogic = Gnoseis.Core.Domain.NotePage;

namespace Gnoseis.App.Views;

/// <summary>
/// Parts the contact and note pages share: the side column's sections and rows, dashed buttons,
/// row menus, clipboard copies with a "Copied" tip, and text with clickable links.
/// </summary>
internal static partial class RecordPageParts
{
    /// <summary>True for controls that take typed text (where Del, Home and the like stay with the control).</summary>
    public static bool IsTextInput(object source) => source is TextBox or RichEditBox or PasswordBox or AutoSuggestBox;

    /// <summary>A dashed button: a missing fact, "Link as organization", "Title".</summary>
    public static Button DashedButton(string glyph, string text, string automationName, Func<Task> click, bool accentText = false)
    {
        var foreground = (Brush)Application.Current.Resources[accentText ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = foreground });
        content.Children.Add(new TextBlock { Text = text, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        var grid = new Grid();
        grid.Children.Add(new Rectangle
        {
            RadiusX = 15,
            RadiusY = 15,
            Stroke = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"],
            StrokeDashArray = [3, 2],
            StrokeThickness = 1,
        });
        content.Margin = new Thickness(12, 5, 12, 6);
        grid.Children.Add(content);
        var button = new Button
        {
            Content = grid,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(15),
        };
        AutomationProperties.SetName(button, automationName);
        button.Click += async (_, _) => await click();
        return button;
    }

    /// <summary>Right-click, Shift+F10 or the context-menu key on a linked record: Open and Unlink.</summary>
    public static void ShowRowMenu(UIElement sender, ContextRequestedEventArgs args, RecordRef record, string name, Func<string, string, Task> unlink)
    {
        var open = new MenuFlyoutItem { Text = "Open", Icon = new FontIcon { Glyph = "" } };
        open.Click += async (_, _) => await Navigator.OpenRecordAsync(record);
        var unlinkItem = new MenuFlyoutItem { Text = "Unlink", Icon = new FontIcon { Glyph = "" } };
        unlinkItem.Click += async (_, _) => await unlink(record.Id, name);
        var menu = new MenuFlyout();
        menu.Items.Add(open);
        menu.Items.Add(unlinkItem);
        if (args.TryGetPosition(sender, out var point))
        {
            menu.ShowAt(sender, point);
        }
        else
        {
            menu.ShowAt((FrameworkElement)sender);
        }
        args.Handled = true;
    }

    /// <summary>Puts text on the clipboard and says <paramref name="message"/> in <paramref name="tip"/>, next to <paramref name="source"/>.</summary>
    public static async void Copy(string text, TeachingTip tip, FrameworkElement source, string message = "Copied")
    {
        var package = new DataPackage();
        package.SetText(text);

        // Another app may hold the clipboard for a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetContent(package);
                break;
            }
            catch (Exception) when (attempt < 3)
            {
                await Task.Delay(100);
            }
            catch (Exception)
            {
                message = "Couldn't copy";
                break;
            }
        }
        tip.Target = source;
        tip.Title = message;
        tip.IsOpen = false;
        tip.IsOpen = true;
        var timer = tip.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => tip.IsOpen = false;
        timer.Start();
    }

    // ---------------------------------------------------------------- Text with clickable links

    [GeneratedRegex(@"(https?://[^\s<>""]+|www\.[^\s<>""]+|[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,})")]
    private static partial Regex LinkPattern();

    /// <summary>Fills <paramref name="block"/> with <paramref name="text"/>, line breaks kept, web and e-mail addresses as links.</summary>
    public static void ShowLinkedText(RichTextBlock block, string text, double paragraphSpacing = 4)
    {
        block.Blocks.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, line.Length == 0 ? 0 : paragraphSpacing) };
            var position = 0;
            foreach (Match match in LinkPattern().Matches(line))
            {
                if (match.Index > position)
                {
                    paragraph.Inlines.Add(new Run { Text = line[position..match.Index] });
                }
                var target = match.Value.TrimEnd('.', ',', ';', ':', ')', '!', '?');
                var address = target.Contains('@') && !target.Contains("://") ? "mailto:" + target
                    : target.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + target
                    : target;
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                {
                    var link = new Hyperlink { NavigateUri = uri };
                    link.Inlines.Add(new Run { Text = target });
                    paragraph.Inlines.Add(link);
                }
                else
                {
                    paragraph.Inlines.Add(new Run { Text = target });
                }
                position = match.Index + target.Length;
            }
            if (position < line.Length)
            {
                paragraph.Inlines.Add(new Run { Text = line[position..] });
            }
            block.Blocks.Add(paragraph);
        }
    }
}

/// <summary>
/// The side column of the contact and note pages: one section per linked type, with its count,
/// "Show all" in place and "+", and rows that open the record or, from their menu, unlink it.
/// </summary>
internal sealed class SideColumn(FrameworkElement owner, Func<IReadOnlyList<RecordType>?, string?, Task> link, Func<string, string, Task> unlink, Action rebuild)
{
    // Sections shown in full ("Show all"), kept while the page is open.
    private readonly HashSet<RecordType> _expanded = [];

    /// <summary>
    /// Lays the sections out in one column, or in two (balancing their heights) in XXL, or in
    /// <paramref name="columnCount"/> columns when it is given.
    /// </summary>
    public static void Flow(Grid panel, IReadOnlyList<FrameworkElement> sections, RecordPageBand band, int? columnCount = null)
    {
        panel.Children.Clear();
        panel.ColumnDefinitions.Clear();
        var columns = columnCount ?? (band == RecordPageBand.XXL ? 2 : 1);
        for (var i = 0; i < columns; i++)
        {
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        // Sections flow down the first column, then the second.
        var heights = sections.Select(EstimatedHeight).ToList();
        var target = heights.Sum() / columns;
        var column = new StackPanel { Spacing = 24 };
        panel.Children.Add(column);
        var height = 0.0;
        for (var i = 0; i < sections.Count; i++)
        {
            if (columns > 1 && Grid.GetColumn(column) == 0 && height > 0 && height + heights[i] / 2 > target)
            {
                column = new StackPanel { Spacing = 24 };
                Grid.SetColumn(column, 1);
                panel.Children.Add(column);
                height = 0;
            }
            column.Children.Add(sections[i]);
            height += heights[i];
        }
    }

    private static double EstimatedHeight(FrameworkElement section) => section.Tag is int rows ? 40 + rows * 50 : 120;

    /// <summary>A section: the type and count, "Show all" when there are more rows than fit, "+", then the rows.</summary>
    public FrameworkElement Section(RecordType type, IReadOnlyList<LinkedRecordSummary> records, RecordPageBand band)
    {
        var limit = RecordPageLayout.SideRowCount(band);
        var expanded = _expanded.Contains(type);
        var shown = expanded ? records : records.Take(limit).ToList();
        var panel = new StackPanel { Spacing = 2, Tag = shown.Count };

        var heading = Heading(type.PluralDisplayName(), records.Count);
        var plus = Plus(type, panel);
        if (records.Count > limit)
        {
            var showAll = new HyperlinkButton { Content = expanded ? "Show fewer" : "Show all", Padding = new Thickness(4, 2, 4, 2) };
            showAll.Click += (_, _) =>
            {
                if (!_expanded.Remove(type))
                {
                    _expanded.Add(type);
                }
                rebuild();
            };
            AddToHeading(heading, showAll);
        }
        AddToHeading(heading, plus);
        panel.Children.Add(heading);

        foreach (var record in shown)
        {
            panel.Children.Add(Row(record));
        }
        return panel;
    }

    /// <summary>"Link a contact", "Link an organization".</summary>
    public static string LinkName(RecordType type)
    {
        var name = type.DisplayName().ToLower(CultureInfo.CurrentCulture);
        return $"Link {(name.Length > 0 && "aeiou".Contains(name[0]) ? "an" : "a")} {name}";
    }

    /// <summary>
    /// "+" on a section's heading: the link picker with only that type. Shown on hover and keyboard
    /// focus of the section; always there for Narrator and the Tab key.
    /// </summary>
    private Button Plus(RecordType type, Panel section)
    {
        var name = type.DisplayName().ToLower(CultureInfo.CurrentCulture);
        var plus = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Opacity = 0,
        };
        AutomationProperties.SetName(plus, LinkName(type));
        ToolTipService.SetToolTip(plus, $"Find or create {(name.Length > 0 && "aeiou".Contains(name[0]) ? "an" : "a")} {name} to link");
        plus.Click += async (_, _) => await link([type], null);

        var hovered = false;
        void Update()
        {
            if (owner.XamlRoot == null)
            {
                return;
            }
            var focused = FocusManager.GetFocusedElement(owner.XamlRoot) as DependencyObject;
            var inside = false;
            for (var element = focused; element != null; element = VisualTreeHelper.GetParent(element))
            {
                if (ReferenceEquals(element, section))
                {
                    inside = true;
                    break;
                }
            }
            plus.Opacity = hovered || inside ? 1 : 0;
        }
        section.Background ??= new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        section.PointerEntered += (_, _) => { hovered = true; Update(); };
        section.PointerExited += (_, _) => { hovered = false; Update(); };
        section.GotFocus += (_, _) => Update();
        section.LostFocus += (_, _) => owner.DispatcherQueue.TryEnqueue(Update);
        return plus;
    }

    /// <summary>A section heading: the title, then the count in tertiary text; commands go on the right.</summary>
    public static Grid Heading(string title, int? count)
    {
        var grid = new Grid { MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        text.Inlines.Add(new Run { Text = title });
        if (count is { } n)
        {
            text.Inlines.Add(new Run
            {
                Text = $"  {n.ToString(CultureInfo.CurrentCulture)}",
                FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            });
        }
        AutomationProperties.SetHeadingLevel(text, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        grid.Children.Add(text);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(commands, 1);
        grid.Children.Add(commands);
        return grid;
    }

    /// <summary>Adds a command (Show all, +) to the right of a heading made by <see cref="Heading"/>.</summary>
    public static void AddToHeading(Grid heading, UIElement element) => ((StackPanel)heading.Children[1]).Children.Add(element);

    /// <summary>
    /// A linked record: a contact's picture, "First Last" and job · company; a note's tile, title (or
    /// first line) and day; another record's tile, name and its first comment line or parent path.
    /// </summary>
    private Button Row(LinkedRecordSummary record)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = record.Type switch
        {
            RecordType.Note => NoteLogic.LinkedNoteName(record),
            _ when record.Name.Length > 0 => record.Name,
            _ => record.Type.DisplayName(),
        };
        var second = record.Type switch
        {
            RecordType.Contact => record.JobAndCompany,
            RecordType.Note => NoteLogic.LinkedNoteDay(record),
            RecordType.Organization => record.FirstLine,
            _ => record.ParentPath,
        };

        if (record.Type == RecordType.Contact)
        {
            grid.Children.Add(new PersonPicture
            {
                DisplayName = name,
                Width = 24,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
                Style = (Style)Application.Current.Resources["ContactPictureStyle"],
            });
            AutomationProperties.SetAccessibilityView((UIElement)grid.Children[0], Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        }
        else
        {
            grid.Children.Add(new TypeTile
            {
                Type = record.Type,
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(5),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var lines = new StackPanel();
        Grid.SetColumn(lines, 1);
        lines.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["BodyTextBlockStyle"], TextWrapping = TextWrapping.NoWrap });
        if (second.Length > 0)
        {
            lines.Children.Add(new TextBlock
            {
                Text = second,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }
        grid.Children.Add(lines);

        var button = new Button
        {
            Content = grid,
            Style = (Style)Application.Current.Resources["RowButtonStyle"],
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(-8, 0, 0, 0),
        };
        AutomationProperties.SetName(button, string.Join(". ", new[] { $"{record.Type.DisplayName()}: {name}", second }.Where(s => s.Length > 0)));
        var target = new RecordRef(record.Type, record.Id);
        button.Click += async (_, _) => await Navigator.OpenRecordAsync(target);
        button.ContextRequested += (sender, args) => RecordPageParts.ShowRowMenu(sender, args, target, name, unlink);
        return button;
    }

    /// <summary>"Linked records · none" and link buttons, each opening the picker with one type (or all for null).</summary>
    public static FrameworkElement NoLinksSection(IEnumerable<(string Text, Func<Task> Click)> buttons)
    {
        var panel = new StackPanel { Spacing = 4, Tag = 3 };
        panel.Children.Add(Heading("Linked records · none", null));
        foreach (var (text, click) in buttons)
        {
            panel.Children.Add(LinkTextButton(text, click));
        }
        return panel;
    }

    private static HyperlinkButton LinkTextButton(string text, Func<Task> click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = Helpers.Glyphs.Link, FontSize = 14 });
        content.Children.Add(new TextBlock { Text = text });
        var button = new HyperlinkButton { Content = content, Padding = new Thickness(4, 4, 4, 4), Margin = new Thickness(-4, 0, 0, 0) };
        AutomationProperties.SetName(button, text);
        button.Click += async (_, _) => await click();
        return button;
    }
}
