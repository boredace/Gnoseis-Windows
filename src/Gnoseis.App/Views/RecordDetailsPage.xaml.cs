/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Gnoseis.App.Views;

/// <summary>
/// A note, organization, category or item (contacts have <see cref="ContactPage"/>): a header with
/// its commands, the Details section (per type) and the linked records.
/// From an 860 px pane the details and the linked records sit side by side; below it, one
/// selector bar (Details, All, then the linked types) shows one of them at a time.
/// </summary>
public sealed partial class RecordDetailsPage : Page, IRecordPage
{
    private const double SideBySideWidth = 860;
    private const double LabelsWidth = 520;
    private const double TwoColumnsWidth = 1050;
    private const double ThreeColumnsWidth = 1500;

    private const string DetailsKey = "details";
    private const string AllKey = "all";

    // The selected selector item of each record, kept while the app is open.
    private static readonly Dictionary<string, string> SelectedItems = [];

    // Full and compact labels of the selector items.
    private readonly Dictionary<SelectorBarItem, (string Full, string Compact)> _labels = [];

    private string _selected = "";
    private bool _isSideBySide;
    private bool _hasLayout;
    private bool _updatingSelector;
    private string _contentSignature = "";

    public RecordDetailsPage()
    {
        InitializeComponent();
    }

    public RecordDetailsViewModel ViewModel { get; private set; } = null!;

    public RecordRef Record => ViewModel.Record;

    /// <summary>After New linked saves, the record page of <paramref name="recordId"/> opens on the new record's type.</summary>
    public static void SelectTypeOnReturn(string recordId, RecordType type) => SelectedItems[recordId] = type.ToString();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = new RecordDetailsViewModel((RecordRef)e.Parameter);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Loaded += ViewModel_Loaded;
        _selected = SelectedItems.GetValueOrDefault(ViewModel.Record.Id, "");
        ViewModel.Activate();
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Deactivate();
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Loaded -= ViewModel_Loaded;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordDetailsViewModel.Body) && ViewModel.IsNote)
        {
            ShowNoteText(ViewModel.Body);
        }
    }

    private void ViewModel_Loaded(object? sender, EventArgs e)
    {
        _contentSignature = "";
        UpdatePageLayout(rebuildSelector: true);
    }

    // ---------------------------------------------------------------- Layout

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePageLayout(rebuildSelector: false);

    /// <summary>Side by side or tabs, the details column, the selector and the linked records, for the pane width.</summary>
    private void UpdatePageLayout(bool rebuildSelector)
    {
        if (ViewModel == null || !ViewModel.IsLoaded || ActualWidth <= 0)
        {
            return;
        }

        var pane = ActualWidth;
        var padding = pane >= 1400 ? 40 : 24;
        var content = Math.Max(0, pane - 2 * padding);
        var sideBySide = pane >= SideBySideWidth;
        var modeChanged = sideBySide != _isSideBySide || !_hasLayout;
        _isSideBySide = sideBySide;
        _hasLayout = true;

        Header.Margin = new Thickness(padding, 20, padding, 0);
        BodyGrid.Margin = new Thickness(padding, 16, padding, 32);
        TabBarScroller.Margin = new Thickness(padding - 8, 4, padding - 8, 0);
        Commands.DefaultLabelPosition = pane < LabelsWidth ? CommandBarDefaultLabelPosition.Collapsed : CommandBarDefaultLabelPosition.Right;

        double linkedWidth;
        if (sideBySide)
        {
            var details = Math.Clamp(content * 0.36, 340, 520);
            DetailsColumn.Width = new GridLength(details);
            LinkedColumn.Width = new GridLength(1, GridUnitType.Star);
            BodyGrid.ColumnSpacing = 32;
            Grid.SetColumn(LinkedPanel, 1);
            linkedWidth = content - details - 32;
        }
        else
        {
            DetailsColumn.Width = new GridLength(1, GridUnitType.Star);
            LinkedColumn.Width = new GridLength(0);
            BodyGrid.ColumnSpacing = 0;
            Grid.SetColumn(LinkedPanel, 0);
            linkedWidth = content;
        }

        if (modeChanged || rebuildSelector)
        {
            SelectValidItem();
            RebuildSelector();
        }
        UpdateCompactItems(linkedWidth, pane);
        ShowSelected(linkedWidth);
    }

    /// <summary>
    /// Keeps the selection when it still exists. Opens on All side by side and on Details in tabs;
    /// Details becomes All when the pane widens; a type that has no links any more becomes All
    /// (Details when nothing is linked).
    /// </summary>
    private void SelectValidItem()
    {
        var selected = _selected;
        if (selected.Length == 0)
        {
            selected = _isSideBySide ? AllKey : DetailsKey;
        }
        else if (selected == DetailsKey && _isSideBySide)
        {
            selected = AllKey;
        }
        else if (selected != DetailsKey && selected != AllKey
                 && !(Enum.TryParse<RecordType>(selected, out var type) && ViewModel.Linked.Find(type) != null))
        {
            selected = AllKey;
        }

        if (selected == AllKey && !ViewModel.HasLinks && !_isSideBySide)
        {
            selected = DetailsKey;
        }
        SetSelected(selected);
    }

    private void SetSelected(string key)
    {
        _selected = key;
        SelectedItems[ViewModel.Record.Id] = key;
    }

    // ---------------------------------------------------------------- Selector

    private SelectorBar ActiveBar => _isSideBySide ? LinkedBar : TabBar;

    /// <summary>Fills the selector: (Details,) All, then the linked types sorted by count, most first.</summary>
    private void RebuildSelector()
    {
        _updatingSelector = true;
        try
        {
            TabBar.Items.Clear();
            LinkedBar.Items.Clear();
            _labels.Clear();
            TabBarScroller.Visibility = _isSideBySide ? Visibility.Collapsed : Visibility.Visible;

            var bar = ActiveBar;
            if (!_isSideBySide)
            {
                bar.Items.Add(CreateItem(DetailsKey, "", null, "Details"));
            }
            if (ViewModel.HasLinks)
            {
                bar.Items.Add(CreateItem(AllKey, "", null, "All"));
                foreach (var group in ViewModel.Linked.Groups)
                {
                    bar.Items.Add(CreateItem(group.Type.ToString(), Helpers.Glyphs.For(group.Type), group.Type,
                        $"{group.Type.PluralDisplayName()} ({group.Count})", $"({group.Count})"));
                }
            }
            bar.SelectedItem = bar.Items.FirstOrDefault(i => (string)i.Tag == _selected);
        }
        finally
        {
            _updatingSelector = false;
        }
    }

    private SelectorBarItem CreateItem(string key, string glyph, RecordType? type, string label, string? compactLabel = null)
    {
        var icon = new FontIcon { Glyph = glyph };
        if (type is { } t)
        {
            icon.Style = RecordTypeStyles.Glyph(t);
        }
        var item = new SelectorBarItem { Tag = key, Text = label, Icon = icon };
        AutomationProperties.SetName(item, label);
        _labels[item] = (label, compactLabel ?? label);
        return item;
    }

    /// <summary>
    /// Where the bar is short of space, the items that aren't selected show glyph and count only,
    /// with the full label as a tooltip and automation name. The selected item always shows its full label.
    /// </summary>
    private void UpdateCompactItems(double linkedWidth, double pane)
    {
        var compact = linkedWidth < 640 || pane < 520 || (pane < 700 && ViewModel.Linked.Groups.Count > 3);
        foreach (var item in ActiveBar.Items)
        {
            var (full, compactLabel) = _labels[item];
            var useCompact = compact && !ReferenceEquals(item, ActiveBar.SelectedItem) && compactLabel != full;
            item.Text = useCompact ? compactLabel : full;
            ToolTipService.SetToolTip(item, useCompact ? full : null);
        }
    }

    private void Selector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingSelector || sender.SelectedItem is not { Tag: string key })
        {
            return;
        }
        SetSelected(key);
        UpdatePageLayout(rebuildSelector: false);
    }

    /// <summary>Selects an item from code (Show all).</summary>
    private void Select(string key)
    {
        SetSelected(key);
        _updatingSelector = true;
        try
        {
            ActiveBar.SelectedItem = ActiveBar.Items.FirstOrDefault(i => (string)i.Tag == key);
        }
        finally
        {
            _updatingSelector = false;
        }
        UpdatePageLayout(rebuildSelector: false);
        ActiveBar.SelectedItem?.Focus(FocusState.Programmatic);
    }

    // ---------------------------------------------------------------- Selected item's content

    private void ShowSelected(double linkedWidth)
    {
        var showDetails = _isSideBySide || _selected == DetailsKey;
        var showLinked = _isSideBySide || _selected != DetailsKey;
        DetailsPanel.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        LinkedPanel.Visibility = showLinked ? Visibility.Visible : Visibility.Collapsed;
        LinkedHeader.Visibility = _isSideBySide ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = _isSideBySide && !ViewModel.HasLinks ? Visibility.Visible : Visibility.Collapsed;

        if (!showLinked || !ViewModel.HasLinks)
        {
            SetLinkedContent([], 1, "none");
            return;
        }

        var columns = linkedWidth >= ThreeColumnsWidth ? 3 : linkedWidth >= TwoColumnsWidth ? 2 : 1;
        if (_selected == AllKey)
        {
            var groups = ViewModel.Linked.Groups.Select(g => new LinkedAllGroup(g)).ToList();
            SetLinkedContent(groups.Select(g => (g.EstimatedHeight, (object)g, "LinkedAllGroupTemplate")).ToList(), columns, $"all/{columns}");
        }
        else if (Enum.TryParse<RecordType>(_selected, out var type) && ViewModel.Linked.Find(type) is { } group)
        {
            List<LinkedCard> cards;
            if (type == RecordType.Note)
            {
                cards = LinkedCard.ByDay(group.Records);
            }
            else
            {
                // One list, in 2 columns on a wide section.
                columns = Math.Min(columns, 2);
                var perColumn = Math.Max(1, (int)Math.Ceiling(group.Count / (double)columns));
                cards = group.Records.Chunk(perColumn).Select(c => new LinkedCard(c.Select(r => new LinkedRow(r)).ToList())).ToList();
            }
            SetLinkedContent(cards.Select(c => (c.EstimatedHeight, (object)c, "LinkedCardTemplate")).ToList(), columns, $"{type}/{columns}");
        }
    }

    /// <summary>
    /// Lays the blocks out in columns, in order (down the first column, then the next), balancing
    /// the columns' heights. A block (a type in All, a day of notes) never splits across columns.
    /// </summary>
    private void SetLinkedContent(List<(double Height, object Content, string Template)> blocks, int columns, string signature)
    {
        if (signature == _contentSignature)
        {
            return;
        }
        _contentSignature = signature;

        LinkedContent.Children.Clear();
        LinkedContent.ColumnDefinitions.Clear();
        if (blocks.Count == 0)
        {
            return;
        }

        columns = Math.Clamp(columns, 1, blocks.Count);
        var spacing = blocks[0].Template == "LinkedAllGroupTemplate" ? 20 : 8;
        var target = blocks.Sum(b => b.Height) / columns;
        var panels = new List<StackPanel>();
        var height = 0.0;
        for (var i = 0; i < blocks.Count; i++)
        {
            var columnsLeft = columns - panels.Count;
            var blocksLeft = blocks.Count - i;
            var startNew = panels.Count == 0
                || (columnsLeft > 0 && (blocksLeft <= columnsLeft || (height > 0 && height + blocks[i].Height / 2 > target)));
            if (startNew)
            {
                var panel = new StackPanel { Spacing = spacing };
                Grid.SetColumn(panel, panels.Count);
                LinkedContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                LinkedContent.Children.Add(panel);
                panels.Add(panel);
                height = 0;
            }
            panels[^1].Children.Add(new ContentControl
            {
                Content = blocks[i].Content,
                ContentTemplate = (DataTemplate)Resources[blocks[i].Template],
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsTabStop = false,
            });
            height += blocks[i].Height;
        }
        while (LinkedContent.ColumnDefinitions.Count < columns)
        {
            LinkedContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecordType type })
        {
            Select(type.ToString());
        }
    }

    // ---------------------------------------------------------------- Note text with clickable links

    [GeneratedRegex(@"(https?://[^\s<>""]+|www\.[^\s<>""]+|[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,})")]
    private static partial Regex LinkPattern();

    private void ShowNoteText(string text)
    {
        NoteText.Blocks.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, line.Length == 0 ? 0 : 4) };
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
            NoteText.Blocks.Add(paragraph);
        }
    }

    // ---------------------------------------------------------------- Commands

    // Built each time it opens: the glyph brushes are picked for the page's theme (or high contrast) at that moment.
    private void NewLinkedMenu_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu)
        {
            return;
        }
        menu.Items.Clear();
        foreach (var type in ViewModel.NewLinkedTypes)
        {
            var icon = new FontIcon { Glyph = Helpers.Glyphs.For(type) };
            if (RecordTypeStyles.Brush(this, type, ColorRole.Outline) is { } brush)
            {
                icon.Foreground = brush;
            }
            var item = new MenuFlyoutItem
            {
                Text = $"New {type.DisplayName().ToLowerInvariant()}",
                Icon = icon,
                Tag = type,
            };
            item.Click += NewLinked_Click;
            menu.Items.Add(item);
        }
    }

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Del deletes the record (with a confirmation), unless a text box has the focus.
        if (e.Key == VirtualKey.Delete && e.OriginalSource is not TextBox and not RichEditBox and not PasswordBox and not AutoSuggestBox)
        {
            e.Handled = true;
            await DeleteWithConfirmationAsync();
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e) =>
        await Navigator.ShowEditorAsync(new EditRequest(ViewModel.Type, ViewModel.Record.Id));

    private async void LinkExisting_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await LinkRecordsDialog.ShowAsync(ViewModel.Record);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't link records", ex);
        }
    }

    private async void NewLinked_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: RecordType type })
        {
            await Navigator.ShowEditorAsync(new EditRequest(type, LinkFrom: ViewModel.Record));
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) => await DeleteWithConfirmationAsync();

    /// <summary>Asks, then deletes the record (also used by the Delete key in the list).</summary>
    public async Task DeleteWithConfirmationAsync()
    {
        if (!ViewModel.IsAvailable)
        {
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            $"Delete this {ViewModel.TypeName.ToLowerInvariant()}?",
            $"\"{ViewModel.Title}\"\n\n{ViewModel.DeleteConfirmationText}",
            "Delete");
        if (!confirmed)
        {
            return;
        }

        try
        {
            var id = ViewModel.Record.Id;
            await ViewModel.DeleteAsync();
            Navigator.RecordDeleted(id);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't delete", ex);
        }
    }

    // ---------------------------------------------------------------- Linked rows

    private async void LinkedRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as ContentControl)?.Content is LinkedRow row)
        {
            await Navigator.OpenRecordAsync(row.ToRef());
        }
    }

    /// <summary>Right-click, Shift+F10 or the context-menu key: Open and Unlink.</summary>
    private void LinkedRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if ((sender as ContentControl)?.Content is not LinkedRow row)
        {
            return;
        }

        var open = new MenuFlyoutItem { Text = "Open", Icon = new FontIcon { Glyph = "" } };
        open.Click += async (_, _) => await Navigator.OpenRecordAsync(row.ToRef());
        var unlink = new MenuFlyoutItem { Text = "Unlink", Icon = new FontIcon { Glyph = "" } };
        unlink.Click += async (_, _) => await UnlinkAsync(row);

        var menu = new MenuFlyout();
        menu.Items.Add(open);
        menu.Items.Add(unlink);
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

    // ---------------------------------------------------------------- Unlink and Undo

    private LinkedRecord? _removedLink;
    private string _selectedBeforeUnlink = "";

    /// <summary>Unlinks without asking; the InfoBar offers Undo.</summary>
    private async Task UnlinkAsync(LinkedRow row)
    {
        try
        {
            var selected = _selected;
            var removed = await ViewModel.UnlinkAsync(row);
            if (removed == null)
            {
                return;
            }
            _removedLink = removed;
            _selectedBeforeUnlink = selected;
            UnlinkInfoBar.Message = $"Unlinked {row.DisplayName}";
            UnlinkInfoBar.IsOpen = false;
            UnlinkInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't unlink", ex);
        }
    }

    /// <summary>Links the records again, the same way round, and shows the row where it was.</summary>
    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_removedLink is not { } removed)
        {
            return;
        }
        _removedLink = null;
        UnlinkInfoBar.IsOpen = false;
        try
        {
            if (_selectedBeforeUnlink.Length > 0)
            {
                SetSelected(_selectedBeforeUnlink);
            }
            await ViewModel.UndoUnlinkAsync(removed);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't undo", ex);
        }
    }

    private void UnlinkInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason == InfoBarCloseReason.CloseButton)
        {
            _removedLink = null;
        }
    }
}
