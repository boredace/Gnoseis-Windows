/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.App.Controls;
using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using CategoryLogic = Gnoseis.Core.Domain.CategoryPage;
using HistoryLayout = Gnoseis.Core.Domain.RecordHistoryPage;

namespace Gnoseis.App.Views;

/// <summary>
/// A category: its name and comments in the header, a search box, and five equal lists of the
/// records linked to it (notes, contacts, organizations, items, categories), placed in columns by
/// the width of the record pane (see <see cref="RecordPageBand"/>). The search narrows all five
/// lists at once.
/// </summary>
public sealed partial class CategoryPage : Page, IRecordPage, IConfirmLeave
{
    private const double TopPadding = 20;
    private const double ColumnGap = 24;
    private const double CardGap = 16;
    private const double RowHeight = 32;

    // A card's height without its rows: padding, heading and "Show all".
    private const double CardChrome = 100;

    private RecordPageBand? _band;
    private double _padding;
    private int _columnCount;
    private int _columnRows = -1;
    private string _query = "";
    private readonly HashSet<RecordType> _expanded = [];
    private QuickNoteBox? _noteBox;
    private bool _noteBoxOpen;

    public CategoryPage()
    {
        InitializeComponent();
        TypeTile.Type = RecordType.Category;
        CompactTypeTile.Type = RecordType.Category;
        HeaderBlock.SizeChanged += (_, _) => UpdatePageLayout();
        SearchRow.SizeChanged += (_, _) => UpdatePageLayout();
    }

    public CategoryPageViewModel ViewModel { get; private set; } = null!;

    public RecordRef Record => ViewModel.Record;

    private bool Searching => _query.Trim().Length > 0;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = new CategoryPageViewModel((RecordRef)e.Parameter);
        ViewModel.Loaded += ViewModel_Loaded;
        ViewModel.Activate();
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Deactivate();
        ViewModel.Loaded -= ViewModel_Loaded;
    }

    private void ViewModel_Loaded(object? sender, EventArgs e)
    {
        var available = ViewModel.IsAvailable;
        MainScroller.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        MissingState.Visibility = ViewModel.IsMissing ? Visibility.Visible : Visibility.Collapsed;
        Commands.IsEnabled = available;
        if (!available)
        {
            CompactHeader.Visibility = Visibility.Collapsed;
            return;
        }

        ShowHeader();
        ShowSearch();
        _band = null;
        UpdatePageLayout();
    }

    // ---------------------------------------------------------------- Layout

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePageLayout();

    /// <summary>The content width, the columns and the rows each list shows; the lists are rebuilt when any of those change.</summary>
    private void UpdatePageLayout(bool rebuild = false)
    {
        if (ViewModel == null || !ViewModel.IsAvailable || ActualWidth <= 0)
        {
            return;
        }

        var pane = ActualWidth;
        var band = RecordPageLayout.BandFor(pane);
        _padding = pane >= 1400 ? 40 : 24;
        var content = Math.Max(0, pane - 2 * _padding);
        if (band == RecordPageBand.XXL)
        {
            content = Math.Min(content, HistoryLayout.MaxContentWidth);
        }
        MainGrid.Width = content;
        MainGrid.Margin = new Thickness(_padding, TopPadding, _padding, 32);
        SearchBox.Width = Math.Min(560, content);

        var columnCount = CategoryLogic.ColumnCount(band);
        var columnRows = ColumnRows(band, columnCount);
        if (band != _band)
        {
            _band = band;
            Commands.DefaultLabelPosition = band >= RecordPageBand.L ? CommandBarDefaultLabelPosition.Right : CommandBarDefaultLabelPosition.Collapsed;
            rebuild = true;
        }
        if (rebuild || columnCount != _columnCount || columnRows != _columnRows)
        {
            _columnCount = columnCount;
            _columnRows = columnRows;
            ShowLists();
        }
        UpdateCompactHeader();
    }

    /// <summary>
    /// The height a column has for its lists in the pane, under the header and the search box, in
    /// whole rows; none in S, where every list shows 3.
    /// </summary>
    private int ColumnRows(RecordPageBand band, int columnCount)
    {
        if (band == RecordPageBand.S)
        {
            return 0;
        }
        var top = TopPadding + HeaderBlock.ActualHeight + MainGrid.RowSpacing
            + (SearchRow.Visibility == Visibility.Visible ? SearchRow.ActualHeight + MainGrid.RowSpacing : 0);
        return Math.Max(0, (int)((Root.ActualHeight - top - 32) / (RowHeight + 1)));
    }

    /// <summary>The rows a column with <paramref name="cards"/> lists has room for, after their headings and "Show all".</summary>
    private int RowsFor(int cards) =>
        Math.Max(0, _columnRows - (int)Math.Ceiling(cards * (CardChrome + CardGap) / (RowHeight + 1)));

    private void MainScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateCompactHeader();

    // ---------------------------------------------------------------- Header

    private void ShowHeader()
    {
        NameText.Text = ViewModel.Name;
        AutomationProperties.SetName(TypeTile, RecordType.Category.DisplayName());

        // A placeholder line: the type in secondary text, the rest in tertiary.
        var (line, isPlaceholder) = ViewModel.HeaderLine;
        var typeLength = RecordType.Category.DisplayName().Length;
        HeaderLineRun.Text = isPlaceholder ? line[..typeLength] : line;
        HeaderPlaceholderRun.Text = isPlaceholder ? line[typeLength..] : "";

        CommentsText.Text = ViewModel.Comments;
        CommentsText.Visibility = ViewModel.HasComments ? Visibility.Visible : Visibility.Collapsed;
        ChipsPanel.Children.Clear();
        if (!ViewModel.HasComments)
        {
            var chip = RecordPageParts.DashedButton("", "About", "Add about",
                async () => await Navigator.ShowEditorAsync(new EditRequest(RecordType.Category, Record.Id, FocusField: NamedRecordFields.CommentsField)));
            ToolTipService.SetToolTip(chip, "Add a description of this category");
            ChipsPanel.Children.Add(chip);
        }
        ChipsPanel.Visibility = ChipsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        CompactNameText.Text = ViewModel.Name;
        CompactLineText.Text = line;
        AutomationProperties.SetName(CompactNameButton, $"{ViewModel.Name}, back to the top");
    }

    // ---------------------------------------------------------------- Search

    private void ShowSearch()
    {
        var total = ViewModel.TotalCount;
        SearchRow.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.PlaceholderText = CategoryLogic.SearchPlaceholder(total);
        AutomationProperties.SetName(SearchBox, CategoryLogic.SearchPlaceholder(total));
        if (total == 0 && _query.Length > 0)
        {
            SearchBox.Text = "";
            _query = "";
        }
        ShowSearchStatus();
    }

    private void ShowSearchStatus()
    {
        var matches = CategoryLogic.ListTypes.Select(t => ViewModel.RowsOf(t).Count(r => r.Matches(_query))).ToList();
        SearchStatusText.Text = CategoryLogic.SearchStatus(matches, Searching);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_query == SearchBox.Text)
        {
            return;
        }
        var wasSearching = Searching;
        _query = SearchBox.Text;
        if (wasSearching != Searching)
        {
            // Show all from before the search doesn't carry into it, nor back out of it.
            _expanded.Clear();
        }
        ShowSearchStatus();
        ShowLists();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && SearchBox.Text.Length > 0)
        {
            e.Handled = true;
            SearchBox.Text = "";
        }
    }

    // ---------------------------------------------------------------- Lists

    /// <summary>
    /// The five lists in columns: each placed under the column that is shortest so far, and in each
    /// column the rows shared out so the column fills the pane's height.
    /// </summary>
    private void ShowLists()
    {
        if (_band is not { } band)
        {
            return;
        }
        var types = CategoryLogic.ListTypes;
        var all = types.Select(t => ViewModel.RowsOf(t)).ToList();
        var shown = all.Select(rows => (IReadOnlyList<CategoryRow>)(Searching ? rows.Where(r => r.Matches(_query)).ToList() : rows)).ToList();

        // Placement by the lists' full lengths, so the lists stay put while the search narrows them.
        var columnCount = Math.Max(1, _columnCount);
        var columnOf = CategoryLogic.PlaceColumns(all.Select(r => r.Count).ToList(), columnCount);

        var noteBoxHadFocus = _noteBoxOpen && IsInside(_noteBox);
        if (_noteBox?.Parent is Panel oldParent)
        {
            oldParent.Children.Remove(_noteBox);
        }

        ListsGrid.Children.Clear();
        ListsGrid.ColumnDefinitions.Clear();
        var columns = new List<StackPanel>();
        for (var c = 0; c < columnCount; c++)
        {
            ListsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var column = new StackPanel { Spacing = CardGap };
            Grid.SetColumn(column, c);
            ListsGrid.Children.Add(column);
            columns.Add(column);
        }

        for (var c = 0; c < columnCount; c++)
        {
            var inColumn = Enumerable.Range(0, types.Count).Where(i => columnOf[i] == c).ToList();
            var allotted = CategoryLogic.AllotRows(inColumn.Select(i => shown[i].Count).ToList(), band == RecordPageBand.S ? 0 : RowsFor(inColumn.Count));
            for (var k = 0; k < inColumn.Count; k++)
            {
                var i = inColumn[k];
                columns[c].Children.Add(ListCard(types[i], all[i].Count, shown[i], allotted[k]));
            }
        }

        if (noteBoxHadFocus)
        {
            DispatcherQueue.TryEnqueue(() => _noteBox?.FocusText());
        }
    }

    /// <summary>One list: the heading with the type, count, sort and "+", then its rows, "Show all", or the empty text.</summary>
    private Border ListCard(RecordType type, int count, IReadOnlyList<CategoryRow> rows, int allotted)
    {
        var panel = new StackPanel();
        var card = new Border { Child = panel, Style = PageStyle("ListCardStyle") };
        AutomationProperties.SetName(card, type.PluralDisplayName());
        AutomationProperties.SetLandmarkType(card, Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType.Custom);
        AutomationProperties.SetLocalizedLandmarkType(card, "List");

        panel.Children.Add(ListHeading(type, count, rows.Count));

        if (type == RecordType.Note && _noteBoxOpen)
        {
            var box = EnsureNoteBox();
            box.Margin = new Thickness(0, 4, 4, 8);
            panel.Children.Add(box);
        }

        if (count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = CategoryLogic.EmptyText(type),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
            });
            var add = new HyperlinkButton { Content = CategoryLogic.AddText(type), Padding = new Thickness(0, 4, 0, 4) };
            add.Click += async (_, _) => await AddAsync(type);
            panel.Children.Add(add);
            return card;
        }
        if (rows.Count == 0)
        {
            // Searching, and nothing here matches.
            panel.Children.Add(new TextBlock
            {
                Text = "No matches",
                Margin = new Thickness(0, 4, 0, 4),
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
            });
            card.Opacity = 0.6;
            return card;
        }

        var expanded = _expanded.Contains(type);
        var shownCount = expanded ? rows.Count : Math.Min(rows.Count, allotted);
        for (var i = 0; i < shownCount; i++)
        {
            if (i > 0)
            {
                panel.Children.Add(new Rectangle { Style = PageStyle("RowDividerStyle") });
            }
            panel.Children.Add(Row(rows[i]));
        }

        if (rows.Count > allotted)
        {
            var more = new HyperlinkButton
            {
                Content = expanded ? "Show fewer" : $"Show all {rows.Count.ToString(CultureInfo.CurrentCulture)}",
                Padding = new Thickness(0, 4, 0, 4),
                Margin = new Thickness(0, 4, 0, 0),
            };
            AutomationProperties.SetName(more, expanded
                ? $"Show fewer {type.PluralDisplayName().ToLower(CultureInfo.CurrentCulture)}"
                : $"Show all {rows.Count} {type.PluralDisplayName().ToLower(CultureInfo.CurrentCulture)}");
            more.Click += (_, _) =>
            {
                if (!_expanded.Remove(type))
                {
                    _expanded.Add(type);
                }
                ShowLists();
                FocusShowAll(type);
            };
            more.Tag = type;
            panel.Children.Add(more);
        }
        return card;
    }

    private Style PageStyle(string key) => (Style)Resources[key];

    /// <summary>After Show all or Show fewer, the focus stays on that button.</summary>
    private void FocusShowAll(RecordType type) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var column in ListsGrid.Children.OfType<StackPanel>())
            {
                foreach (var card in column.Children.OfType<Border>())
                {
                    if (card.Child is StackPanel panel && panel.Children.OfType<HyperlinkButton>().LastOrDefault(b => b.Tag is RecordType t && t == type) is { } button)
                    {
                        button.Focus(FocusState.Programmatic);
                        return;
                    }
                }
            }
        });

    private Grid ListHeading(RecordType type, int count, int matches)
    {
        var grid = new Grid { ColumnSpacing = 8, MinHeight = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TypeTile
        {
            Type = type,
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(5),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var countText = CategoryLogic.CountText(count, matches, Searching);
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var heading = new TextBlock { Text = type.PluralDisplayName(), Style = PageStyle("ListTitleStyle") };
        AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        AutomationProperties.SetName(heading, $"{type.PluralDisplayName()}, {countText}");
        title.Children.Add(heading);
        title.Children.Add(new TextBlock { Text = countText, Style = PageStyle("ListCountStyle") });
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var sort = new TextBlock { Text = CategoryLogic.SortText(type, count), Style = PageStyle("TertiaryCaptionStyle") };
        Grid.SetColumn(sort, 2);
        grid.Children.Add(sort);

        var name = type.DisplayName().ToLower(CultureInfo.CurrentCulture);
        var plus = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        var plusName = type == RecordType.Note ? "Write a note" : SideColumn.LinkName(type);
        AutomationProperties.SetName(plus, plusName);
        ToolTipService.SetToolTip(plus, type == RecordType.Note
            ? "Write a note in this category (Ctrl+Shift+N)"
            : $"Find or create {("aeiou".Contains(name[0]) ? "an" : "a")} {name} to link");
        plus.Click += async (_, _) => await AddAsync(type);
        Grid.SetColumn(plus, 3);
        grid.Children.Add(plus);
        return grid;
    }

    /// <summary>
    /// One row, one line: a note's day and title then its "with" line; a contact's picture, name, then
    /// job title · company; another record's name then its first comment line. Matches are marked.
    /// </summary>
    private Button Row(CategoryRow row)
    {
        var grid = new Grid { ColumnSpacing = 10, Height = RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        double lead = 0;
        if (row.Type == RecordType.Note)
        {
            grid.Children.Add(new TextBlock { Text = row.Day, Width = 76, Style = PageStyle("DayTextStyle") });
            lead = 76;
        }
        else if (row.Type == RecordType.Contact)
        {
            var picture = new PersonPicture
            {
                DisplayName = row.Name,
                Width = 22,
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["ContactPictureStyle"],
            };
            AutomationProperties.SetAccessibilityView(picture, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            grid.Children.Add(picture);
            lead = 22;
        }
        else
        {
            grid.ColumnSpacing = 0;
        }

        var name = new HighlightedText
        {
            Text = row.Name,
            Ranges = CategoryLogic.MatchRanges(row.Name, _query),
            UsesAccent = true,
            VerticalAlignment = VerticalAlignment.Center,
            TextStyle = PageStyle("RowTextStyle"),
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        if (row.Second.Length > 0)
        {
            var second = new HighlightedText
            {
                Text = row.Second,
                Ranges = CategoryLogic.MatchRanges(row.Second, _query),
                UsesAccent = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                TextStyle = PageStyle("TertiaryCaptionStyle"),
            };
            Grid.SetColumn(second, 2);
            grid.Children.Add(second);
        }

        // The name takes what it needs up to most of the row; the second line gets the rest, cut with an ellipsis.
        grid.SizeChanged += (_, e) => name.MaxWidth = Math.Max(0, (e.NewSize.Width - lead) * (row.Second.Length > 0 ? 0.7 : 1) - 10);

        var button = new Button
        {
            Content = grid,
            Style = (Style)Application.Current.Resources["RowButtonStyle"],
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(-6, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var parts = row.Type == RecordType.Note
            ? new[] { $"{row.Type.DisplayName()}: {row.Day}, {row.Name}", row.Second.Length > 0 ? $"with {row.Second}" : "" }
            : new[] { $"{row.Type.DisplayName()}: {row.Name}", row.Second };
        AutomationProperties.SetName(button, string.Join(". ", parts.Where(s => s.Length > 0)));
        var target = new RecordRef(row.Type, row.Id);
        button.Click += async (_, _) => await Navigator.OpenLinkedRecordAsync(target);
        button.ContextRequested += (sender, args) => RecordPageParts.ShowRowMenu(sender, args, target, row.Name, UnlinkAsync);
        return button;
    }

    /// <summary>"+" or the add link: the note box for notes, else the link picker with only that type.</summary>
    private async Task AddAsync(RecordType type)
    {
        if (type == RecordType.Note)
        {
            OpenNoteBox();
        }
        else
        {
            await LinkAsync([type], null);
        }
    }

    // ---------------------------------------------------------------- Note box (in the Notes list)

    private QuickNoteBox EnsureNoteBox()
    {
        if (_noteBox == null)
        {
            _noteBox = new QuickNoteBox();
            _noteBox.Closed += (_, _) =>
            {
                _noteBoxOpen = false;
                ShowLists();
                DispatcherQueue.TryEnqueue(() => EditButton.Focus(FocusState.Programmatic));
            };
        }
        _noteBox.LinkTo = Record;
        _noteBox.SetRecordName(ViewModel.Name);
        return _noteBox;
    }

    /// <summary>Opens the note box at the top of the Notes list and puts the cursor in it.</summary>
    private void OpenNoteBox()
    {
        if (!ViewModel.IsAvailable)
        {
            return;
        }
        EnsureNoteBox();
        if (!_noteBoxOpen)
        {
            _noteBoxOpen = true;
            ShowLists();
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            _noteBox?.FocusText();
            _noteBox?.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.3 });
        });
    }

    private void NoteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenNoteBox();
    }

    private bool IsInside(UIElement? container)
    {
        if (container == null || XamlRoot == null)
        {
            return false;
        }
        for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; element != null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, container))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Leaving with a note draft asks, as the editor does: save it, drop it, or stay.</summary>
    public async Task<bool> CanLeaveAsync()
    {
        if (_noteBox == null || !_noteBox.HasDraft)
        {
            return true;
        }
        var choice = await Dialogs.AskAsync("Save changes?", "Do you want to save your changes to this note?", "Save", "Don't save", "Cancel");
        switch (choice)
        {
            case ContentDialogResult.Primary:
                return await _noteBox.SaveAsync();
            case ContentDialogResult.Secondary:
                _noteBox.Clear();
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- Compact header and scrolling to the top

    /// <summary>Shows the compact header once the full header has scrolled out of view.</summary>
    private void UpdateCompactHeader()
    {
        var show = ViewModel.IsAvailable && MainScroller.VerticalOffset > TopPadding + HeaderBlock.ActualHeight;
        CompactHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CompactHeader.Padding = new Thickness(_padding - 4, 8, _padding - 8, 8);
    }

    private void CompactMore_Click(object sender, RoutedEventArgs e) => MoreMenu.ShowAt(CompactMoreButton);

    private void CompactName_Click(object sender, RoutedEventArgs e) => ScrollToTop();

    private void ScrollToTop() => MainScroller.ChangeView(null, 0, null, disableAnimation: false);

    /// <summary>Home and Ctrl+Home, outside a text box, scroll smoothly to the top.</summary>
    private void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Home && !RecordPageParts.IsTextInput(e.OriginalSource))
        {
            e.Handled = true;
            ScrollToTop();
        }
    }

    // ---------------------------------------------------------------- Commands

    private async void Edit_Click(object sender, RoutedEventArgs e) =>
        await Navigator.ShowEditorAsync(new EditRequest(RecordType.Category, Record.Id));

    private async void Link_Click(object sender, RoutedEventArgs e) => await LinkAsync(null, null);

    /// <summary>The link picker: all types, or only <paramref name="types"/>' chips, with an optional query.</summary>
    private async Task LinkAsync(IReadOnlyList<RecordType>? types, string? query)
    {
        try
        {
            await LinkRecordsDialog.ShowAsync(Record, types, query);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't link records", ex);
        }
    }

    private void MoreMenu_Opening(object? sender, object e)
    {
        MoreMenu.Items.Clear();

        // New linked: a new record of every other type, Note first, linked to this category.
        var newLinked = new MenuFlyoutSubItem { Text = "New linked", Icon = new FontIcon { Glyph = "" } };
        foreach (var type in RecordTypeExtensions.UserTypes.Where(t => t != RecordType.Category))
        {
            var item = new MenuFlyoutItem
            {
                Text = type.DisplayName(),
                Icon = new FontIcon { Glyph = Helpers.Glyphs.For(type), Foreground = RecordTypeStyles.Brush(this, type, ColorRole.Outline) },
            };
            AutomationProperties.SetName(item, $"New linked {type.DisplayName().ToLower(CultureInfo.CurrentCulture)}");
            item.Click += async (_, _) => await Navigator.ShowEditorAsync(new EditRequest(type, LinkFrom: Record));
            newLinked.Items.Add(item);
        }
        MoreMenu.Items.Add(newLinked);

        var copy = new MenuFlyoutItem
        {
            Text = "Copy category details",
            Icon = new FontIcon { Glyph = "" },
            KeyboardAcceleratorTextOverride = "Ctrl+Shift+C",
        };
        copy.Click += (_, _) => CopyDetails();
        MoreMenu.Items.Add(copy);
        MoreMenu.Items.Add(new MenuFlyoutSeparator());

        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "" }, KeyboardAcceleratorTextOverride = "Del" };
        delete.Click += async (_, _) => await DeleteWithConfirmationAsync();
        MoreMenu.Items.Add(delete);
    }

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.IsAvailable)
        {
            args.Handled = true;
            CopyDetails();
        }
    }

    /// <summary>The name and the comments, as plain text.</summary>
    private void CopyDetails()
    {
        if (ViewModel.IsAvailable)
        {
            RecordPageParts.Copy(ViewModel.CopyText, CopiedTip, NameText, "Category details copied");
        }
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Del deletes the category (with a confirmation), unless a text box has the focus.
        if (e.Key == VirtualKey.Delete && !RecordPageParts.IsTextInput(e.OriginalSource))
        {
            e.Handled = true;
            _ = DeleteWithConfirmationAsync();
        }
        // Esc anywhere on the page clears the search.
        else if (e.Key == VirtualKey.Escape && SearchBox.Text.Length > 0)
        {
            e.Handled = true;
            SearchBox.Text = "";
        }
    }

    /// <summary>Asks, then deletes the category.</summary>
    public async Task DeleteWithConfirmationAsync()
    {
        if (!ViewModel.IsAvailable)
        {
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            "Delete this category?",
            $"\"{ViewModel.Name}\"\n\n{ViewModel.DeleteConfirmationText}",
            "Delete");
        if (!confirmed)
        {
            return;
        }

        try
        {
            var id = Record.Id;
            await ViewModel.DeleteAsync();
            Navigator.RecordDeleted(id);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't delete", ex);
        }
    }

    // ---------------------------------------------------------------- Unlink and Undo

    // What Undo does for the last unlink; null once done or closed.
    private Func<Task>? _undo;

    /// <summary>Unlinks without asking; the InfoBar offers Undo.</summary>
    private async Task UnlinkAsync(string recordId, string name)
    {
        try
        {
            var removed = await ViewModel.UnlinkAsync(recordId);
            if (removed == null)
            {
                return;
            }
            _undo = () => ViewModel.UndoUnlinkAsync(removed);
            UnlinkInfoBar.Message = $"Unlinked {name}";
            UnlinkInfoBar.IsOpen = false;
            UnlinkInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't unlink", ex);
        }
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo is not { } undo)
        {
            return;
        }
        _undo = null;
        UnlinkInfoBar.IsOpen = false;
        try
        {
            await undo();
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
            _undo = null;
        }
    }
}
