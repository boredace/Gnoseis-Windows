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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using ContactLogic = Gnoseis.Core.Domain.ContactPage;
using ContactHistory = Gnoseis.Core.Domain.ContactHistory;
using HistoryLayout = Gnoseis.Core.Domain.RecordHistoryPage;
using OrganizationLogic = Gnoseis.Core.Domain.OrganizationPage;

namespace Gnoseis.App.Views;

/// <summary>
/// An item or an organization: its name in the header, its history (the linked notes) as the main
/// column, and the other linked records in a side column. An organization also gets a People
/// column of contact cards beside the history; picking a person narrows the history to the notes
/// linked to both. The layout follows the width of the record pane (see <see cref="RecordPageBand"/>).
/// </summary>
public sealed partial class RecordHistoryPage : Page, IRecordPage, IConfirmLeave
{
    private const double Gap = HistoryLayout.Gap;
    private const double TopPadding = 20;

    private const string HistoryKey = "history";
    private const string PeopleKey = "people";
    private const string LinkedKey = "linked";
    private const string AboutKey = "about";

    private RecordPageBand? _band;
    private RecordHistoryColumns _columns = new(0, 0, 0, 0, 1);
    private double _padding;
    private string _selectedTab = HistoryKey;
    private bool _updatingTabs;
    private int _shownNotes = ContactHistory.PageSize;
    private bool _aboutExpanded;

    private readonly SideColumn _side;

    public RecordHistoryPage()
    {
        InitializeComponent();
        _side = new SideColumn(this, LinkAsync, UnlinkAsync, RebuildSections);
        HeaderBlock.SizeChanged += (_, _) => PositionSideColumn();

        // A click in the box grows it again after Esc or Save.
        NoteTextBox.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) =>
        {
            _keepCollapsed = false;
            ExpandNoteBox();
        }), handledEventsToo: true);
        NoteBox.LostFocus += NoteBox_LostFocus;
    }

    /// <summary>An empty box goes back to one line when the focus leaves it.</summary>
    private void NoteBox_LostFocus(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (HasDraft || NoteDayFlyout.IsOpen || XamlRoot == null)
            {
                return;
            }
            var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            for (var element = focused; element != null; element = VisualTreeHelper.GetParent(element))
            {
                if (ReferenceEquals(element, NoteBox))
                {
                    return;
                }
            }
            CollapseNoteBox(clear: true);
        });

    public RecordHistoryPageViewModel ViewModel { get; private set; } = null!;

    public RecordRef Record => ViewModel.Record;

    public bool IsFindBoxShowing => KeyboardFocus.IsShowing(FilterBox);

    public void FocusFindBox() => KeyboardFocus.FocusAndSelectAll(FilterBox);

    /// <summary>"item" or "organization", for texts.</summary>
    private string TypeWord => ViewModel.Type.DisplayName().ToLower(CultureInfo.CurrentCulture);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = new RecordHistoryPageViewModel((RecordRef)e.Parameter);
        RecordPageParts.InsertOpenedFrom(HeaderBlock, ViewModel.Record);
        TypeTile.Type = ViewModel.Type;
        CompactTypeTile.Type = ViewModel.Type;
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
            SideScroller.Visibility = Visibility.Collapsed;
            return;
        }

        // A person unlinked meanwhile is no longer picked.
        if (_pickedPersonId != null && PickedPerson == null)
        {
            _pickedPersonId = null;
        }

        var noteBoxHadFocus = IsInNoteBox();
        ShowHeader();
        ShowChips();
        ShowCompactHeader();
        ShowNoteBoxTexts();
        ShowHistory();
        _band = null;
        UpdatePageLayout();

        // The focused card or button may have been rebuilt; the focus then falls back into the note
        // box. Put it on the person just linked, else on Edit; the empty box then collapses again.
        DispatcherQueue.TryEnqueue(() =>
        {
            var focusId = _focusPersonAfterLoad;
            _focusPersonAfterLoad = null;
            if (noteBoxHadFocus || !IsInNoteBox() || HasDraft)
            {
                return;
            }
            if (focusId != null && _cards.TryGetValue(focusId, out var parts))
            {
                parts.Surface.Focus(FocusState.Programmatic);
            }
            else
            {
                EditButton.Focus(FocusState.Programmatic);
            }
        });
    }

    // The contact just linked from the not-linked box, focused once the page has reloaded.
    private string? _focusPersonAfterLoad;

    private bool IsInNoteBox()
    {
        if (XamlRoot == null)
        {
            return false;
        }
        for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; element != null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, NoteBox))
            {
                return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- Layout

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePageLayout();

    /// <summary>The columns' widths and places for the pane width; the parts that depend on the band are rebuilt when it changes.</summary>
    private void UpdatePageLayout()
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

        var previous = _columns;
        _columns = HistoryLayout.Columns(band, content, ViewModel.History.Count > 0, ViewModel.IsOrganization);

        MainGrid.Width = content;
        MainGrid.Margin = new Thickness(_padding, TopPadding, _padding, 32);
        PeopleColumn.Width = new GridLength(_columns.People);
        BodyRow.ColumnSpacing = _columns.People > 0 ? Gap : 0;
        BodyPanel.Width = _columns.History;
        SideScroller.Width = _columns.Side;
        ReadingScroller.Width = _columns.Reading;

        if (band != _band)
        {
            _band = band;
            var wide = band >= RecordPageBand.L;
            Commands.DefaultLabelPosition = wide ? CommandBarDefaultLabelPosition.Right : CommandBarDefaultLabelPosition.Collapsed;
            ShowTabs();
            ShowSideColumn();
            ShowPeople();
        }
        else if (previous.SideColumnCount != _columns.SideColumnCount)
        {
            ShowSideColumn();
        }
        UpdateHistoryMode();
        UpdateCompactHeader();
        PositionSideColumn();
    }

    /// <summary>Where the history starts: after the People column when there is one.</summary>
    private double HistoryLeft => _padding + (_columns.People > 0 ? _columns.People + Gap : 0);

    /// <summary>
    /// The side column and the reading pane move up with the header, then stay below the top of
    /// the pane while the history scrolls on.
    /// </summary>
    private void PositionSideColumn()
    {
        if (_band is not { } band || band == RecordPageBand.S || !ViewModel.IsAvailable)
        {
            SideScroller.Visibility = Visibility.Collapsed;
            ReadingScroller.Visibility = Visibility.Collapsed;
            return;
        }

        var top = TopPadding + HeaderBlock.ActualHeight + MainGrid.RowSpacing - MainScroller.VerticalOffset;
        top = Math.Max(top, MinimumColumnTop);
        var height = Math.Max(0, Root.ActualHeight - top);
        var left = HistoryLeft + _columns.History + Gap + (_columns.Reading > 0 ? _columns.Reading + Gap : 0);
        SideScroller.Visibility = Visibility.Visible;
        SideScroller.Margin = new Thickness(left, top, 0, 0);
        SideScroller.Height = height;

        ReadingScroller.Visibility = HasReadingPane ? Visibility.Visible : Visibility.Collapsed;
        ReadingScroller.Margin = new Thickness(HistoryLeft + _columns.History + Gap, top, 0, 0);
        ReadingScroller.Height = height;
    }

    /// <summary>The highest the side column goes when the header has scrolled away.</summary>
    private double MinimumColumnTop => CompactHeader.Visibility == Visibility.Visible ? CompactHeaderHeight + 12 : 12;

    private void MainScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateCompactHeader();
        PositionSideColumn();
        if (!e.IsIntermediate || _jumpedYear == null)
        {
            UpdateLitYear();
        }
    }

    // ---------------------------------------------------------------- Header

    private void ShowHeader()
    {
        NameText.Text = ViewModel.Name;
        AutomationProperties.SetName(TypeTile, ViewModel.Type.DisplayName());
        ShowHeaderLine();
    }

    /// <summary>"Item · 26 notes · 16 linked", "Organization · 7 people · 64 notes", or a placeholder.</summary>
    private void ShowHeaderLine()
    {
        HeaderLinePanel.Children.Clear();
        var (text, isPlaceholder) = ViewModel.HeaderLine;
        if (isPlaceholder)
        {
            // The type in secondary text, the rest in tertiary.
            var type = ViewModel.Type.DisplayName();
            var parts = new StackPanel { Orientation = Orientation.Horizontal };
            parts.Children.Add(LineText(type, "SecondaryLineTextStyle"));
            parts.Children.Add(LineText(text[type.Length..], "TertiaryLineTextStyle"));
            AutomationProperties.SetName(parts, text);
            HeaderLinePanel.Children.Add(parts);
            return;
        }
        HeaderLinePanel.Children.Add(LineText(text, "SecondaryLineTextStyle"));
    }

    private static TextBlock LineText(string text, string style) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Style = (Style)Application.Current.Resources[style],
        TextTrimming = TextTrimming.CharacterEllipsis,
        IsTextSelectionEnabled = true,
    };

    /// <summary>No comments: a dashed About chip that opens the edit page in Comments.</summary>
    private void ShowChips()
    {
        ChipsPanel.Children.Clear();
        if (!ViewModel.HasComments)
        {
            var chip = RecordPageParts.DashedButton("", "About", "Add about",
                async () => await Navigator.ShowEditorAsync(new EditRequest(ViewModel.Type, Record.Id, FocusField: NamedRecordFields.CommentsField)));
            ToolTipService.SetToolTip(chip, $"Add a description of this {TypeWord}");
            ChipsPanel.Children.Add(chip);
        }
        ChipsPanel.Visibility = ChipsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- Tabs (under 860 px)

    private void ShowTabs()
    {
        var tabs = _band == RecordPageBand.S;
        TabBar.Visibility = tabs ? Visibility.Visible : Visibility.Collapsed;
        if (!tabs)
        {
            _selectedTab = HistoryKey;
            ShowSelectedTab();
            return;
        }

        if ((_selectedTab == AboutKey && !ViewModel.HasComments) || (_selectedTab == PeopleKey && !ViewModel.IsOrganization))
        {
            _selectedTab = HistoryKey;
        }
        _updatingTabs = true;
        try
        {
            TabBar.Items.Clear();
            TabBar.Items.Add(new SelectorBarItem { Text = "History", Tag = HistoryKey });
            if (ViewModel.IsOrganization)
            {
                TabBar.Items.Add(new SelectorBarItem { Text = $"People ({ViewModel.People.Count})", Tag = PeopleKey });
            }
            TabBar.Items.Add(new SelectorBarItem { Text = $"Linked records ({ViewModel.SideCount})", Tag = LinkedKey });
            if (ViewModel.HasComments)
            {
                TabBar.Items.Add(new SelectorBarItem { Text = "About", Tag = AboutKey });
            }
            TabBar.SelectedItem = TabBar.Items.First(i => (string)i.Tag == _selectedTab);
        }
        finally
        {
            _updatingTabs = false;
        }
        ShowSelectedTab();
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingTabs || sender.SelectedItem is not { Tag: string key })
        {
            return;
        }
        _selectedTab = key;
        ShowSelectedTab();
    }

    private void ShowSelectedTab()
    {
        HistoryPanel.Visibility = _selectedTab == HistoryKey ? Visibility.Visible : Visibility.Collapsed;
        NoteBox.Visibility = HistoryPanel.Visibility;
        TabPeoplePanel.Visibility = _selectedTab == PeopleKey ? Visibility.Visible : Visibility.Collapsed;
        TabLinkedPanel.Visibility = _selectedTab == LinkedKey ? Visibility.Visible : Visibility.Collapsed;
        TabAboutText.Visibility = _selectedTab == AboutKey ? Visibility.Visible : Visibility.Collapsed;
        TabAboutText.Text = ViewModel.Comments;
        TabPeoplePanel.Children.Clear();
        if (_selectedTab == PeopleKey)
        {
            foreach (var part in CreatePeopleParts(cards: true))
            {
                TabPeoplePanel.Children.Add(part);
            }
        }
        if (_selectedTab == LinkedKey)
        {
            TabLinkedPanel.Children.Clear();
            foreach (var section in CreateSections(includeAbout: false, includePeople: false))
            {
                TabLinkedPanel.Children.Add(section);
            }
        }
    }

    // ---------------------------------------------------------------- History

    // The rows shown, in order (filtered, up to the number shown).
    private List<HistoryRow> _rows = [];

    /// <summary>The history the rows come from: every note, or only the notes with the person picked.</summary>
    private IReadOnlyList<HistoryNote> BaseNotes =>
        PickedPerson is { } person ? OrganizationLogic.PeopleFilter(ViewModel.History, person.Id) : ViewModel.History;

    private void ShowHistory()
    {
        var all = ViewModel.History;
        var notes = BaseNotes;
        var person = PickedPerson;
        HistoryCountText.Text = OrganizationLogic.HistoryCount(notes.Count, all.Count, person != null);
        EmptyHistory.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyHistoryTitle.Text = $"No notes about {ViewModel.Name} yet";
            EmptyHistoryText.Text = ViewModel.IsOrganization
                ? "Write the first one above. Calls, visits and e-mails noted here build its history, newest first."
                : "Write the first one above. Repairs, checks and changes noted here build its history, newest first.";
            CreatedText.Text = ViewModel.CreatedAt is { } created
                ? $"{DateTimeOffset.FromUnixTimeMilliseconds(created).ToLocalTime():d MMM yyyy} · {ViewModel.Type.DisplayName()} created"
                : "";
            CreatedText.Visibility = ViewModel.CreatedAt != null ? Visibility.Visible : Visibility.Collapsed;
        }

        var filtered = ContactHistory.ShowsFilter(all.Count);
        FilterBox.Visibility = filtered ? Visibility.Visible : Visibility.Collapsed;
        FilterRow.Visibility = filtered || person != null ? Visibility.Visible : Visibility.Collapsed;
        if (!filtered && FilterBox.Text.Length > 0)
        {
            FilterBox.Text = "";
        }
        PersonChip.Visibility = person != null ? Visibility.Visible : Visibility.Collapsed;
        if (person != null)
        {
            PersonChipText.Text = $"With {person.Name}";
            AutomationProperties.SetName(PersonChip, $"With {person.Name}, clear (Esc)");
            ToolTipService.SetToolTip(PersonChip, "Show every note (Esc)");
        }
        ShowYearChips();
        ShowHistoryRows();
    }

    /// <summary>
    /// The notes that match the filter, up to the number shown, under month headers from 13 notes.
    /// Under 13 the list gets the rows themselves, so no group header is drawn above the first note.
    /// </summary>
    private void ShowHistoryRows()
    {
        var notes = BaseNotes;
        var matching = notes.Where(n => ContactHistory.Matches(n, FilterBox.Text)).ToList();
        _rows = matching.Take(_shownNotes).Select(n => new HistoryRow(n, Record.Id)).ToList();
        if (ContactHistory.IsGrouped(notes.Count))
        {
            HistorySource.Source = _rows.GroupBy(r => ContactHistory.Month(r.Note)).Select(g => new HistoryMonth(ContactHistory.MonthText(g.Key), g)).ToList();
            HistoryList.ItemsSource = HistorySource.View;
        }
        else
        {
            HistoryList.ItemsSource = _rows;
        }
        HistoryList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var person = PickedPerson;
        NoMatchText.Text = person != null && notes.Count == 0 ? $"No notes with {person.Name} yet" : "No notes match";
        NoMatchText.Visibility = ViewModel.History.Count > 0 && matching.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        SelectNote();

        var older = matching.Count - _rows.Count;
        ShowOlderButton.Visibility = older > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowOlderButton.Content = $"Show older ({older.ToString(CultureInfo.CurrentCulture)})";
    }

    private void ShowOlder_Click(object sender, RoutedEventArgs e)
    {
        _shownNotes += ContactHistory.PageSize;
        ShowHistoryRows();
    }

    private void FilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput || sender.Text.Length == 0)
        {
            ShowHistoryRows();
        }
    }

    // ---------------------------------------------------------------- Year chips

    private int? _litYear;

    // The year last jumped to: it stays lit while its first note is in view, also when the list can't scroll that far.
    private int? _jumpedYear;

    /// <summary>Over more than one year: a chip per year with its count, in the filter row and the compact header.</summary>
    private void ShowYearChips()
    {
        YearChips.Children.Clear();
        CompactYearChips.Children.Clear();
        var notes = BaseNotes;
        var years = ContactHistory.IsGrouped(notes.Count) ? ContactHistory.YearCounts(notes) : [];
        foreach (var (year, count) in years)
        {
            YearChips.Children.Add(CreateYearChip(year, count));
            CompactYearChips.Children.Add(CreateYearChip(year, count));
        }
        _litYear = null;
        LightYear(years.Count > 0 ? years[0].Year : null);
    }

    private Button CreateYearChip(int year, int count)
    {
        var chip = new Button
        {
            Content = $"{year} · {count}",
            Tag = year,
            Style = ChipStyle,
            Padding = new Thickness(10, 3, 10, 4),
            MinHeight = 0,
        };
        AutomationProperties.SetName(chip, $"Go to {year}, {count} notes");
        chip.Click += (_, _) => JumpToYear(year);
        return chip;
    }

    /// <summary>Lights the chips of the year of the topmost note shown.</summary>
    private void LightYear(int? year)
    {
        if (year == _litYear)
        {
            return;
        }
        _litYear = year;
        foreach (var chip in YearChips.Children.Concat(CompactYearChips.Children).OfType<Button>())
        {
            var lit = chip.Tag is int y && y == year;
            chip.Style = lit ? (Style)Resources["LitYearChipStyle"] : ChipStyle;
        }
    }

    /// <summary>Scrolls to the year's first note (newest first), showing older notes when it isn't shown yet. A chip doesn't filter.</summary>
    private void JumpToYear(int year)
    {
        var index = _rows.FindIndex(r => r.Year == year);
        if (index < 0)
        {
            var all = BaseNotes.Where(n => ContactHistory.Matches(n, FilterBox.Text)).ToList();
            var position = all.FindIndex(n => ContactHistory.Day(n).Year == year);
            if (position < 0)
            {
                return;
            }
            _shownNotes = Math.Max(_shownNotes, (position / ContactHistory.PageSize + 1) * ContactHistory.PageSize);
            ShowHistoryRows();
            index = _rows.FindIndex(r => r.Year == year);
        }
        var row = _rows[index];
        HistoryList.UpdateLayout();
        if (HistoryList.ContainerFromItem(row) is UIElement container)
        {
            var transform = container.TransformToVisual(MainScroller.Content as UIElement);
            var y = transform.TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            var monthHeader = index == 0 || _rows[index - 1].Note is var previous && ContactHistory.Month(previous) != ContactHistory.Month(row.Note) ? 48 : 0;
            MainScroller.ChangeView(null, Math.Max(0, y - CompactHeaderHeight - monthHeader - 8), null);
        }
        _jumpedYear = year;
        LightYear(year);
    }

    /// <summary>The year of the topmost note in view, for the chips.</summary>
    private void UpdateLitYear()
    {
        if (YearChips.Children.Count == 0 || _rows.Count == 0)
        {
            return;
        }
        var content = MainScroller.Content as UIElement;
        var top = MainScroller.VerticalOffset + (CompactHeader.Visibility == Visibility.Visible ? CompactHeaderHeight : 0);
        var bottom = MainScroller.VerticalOffset + MainScroller.ViewportHeight;
        if (_jumpedYear is { } jumped)
        {
            var first = _rows.Find(r => r.Year == jumped);
            if (first != null && HistoryList.ContainerFromItem(first) is FrameworkElement jumpedContainer)
            {
                var jy = jumpedContainer.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                if (jy + jumpedContainer.ActualHeight > top && jy < bottom)
                {
                    LightYear(jumped);
                    return;
                }
            }
            _jumpedYear = null;
        }
        foreach (var row in _rows)
        {
            if (HistoryList.ContainerFromItem(row) is FrameworkElement container)
            {
                var y = container.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                if (y + container.ActualHeight > top)
                {
                    LightYear(row.Year);
                    return;
                }
            }
        }
    }

    // ---------------------------------------------------------------- Compact header and scrolling to the top

    private double CompactHeaderHeight => CompactHeader.ActualHeight > 0 ? CompactHeader.ActualHeight : 56;

    /// <summary>Shows the compact header once the full header has scrolled out of view.</summary>
    private void UpdateCompactHeader()
    {
        var show = ViewModel.IsAvailable && MainScroller.VerticalOffset > TopPadding + HeaderBlock.ActualHeight;
        CompactHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CompactHeader.Padding = new Thickness(_padding - 4, 8, _padding - 8, 8);
        CompactYearChips.Visibility = _band >= RecordPageBand.M ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowCompactHeader()
    {
        CompactNameText.Text = ViewModel.Name;
        var (line, _) = ViewModel.HeaderLine;
        CompactLineText.Text = line;
        AutomationProperties.SetName(CompactNameButton, $"{ViewModel.Name}, back to the top");
    }

    private void CompactNote_Click(object sender, RoutedEventArgs e) => FocusNoteBox();

    private void CompactMore_Click(object sender, RoutedEventArgs e) => MoreMenu.ShowAt(CompactMoreButton);

    private void CompactName_Click(object sender, RoutedEventArgs e) => ScrollToTop();

    private void ScrollToTop() => MainScroller.ChangeView(null, 0, null, disableAnimation: false);

    /// <summary>Home and Ctrl+Home, outside a text box, scroll smoothly to the top (the history list doesn't take them).</summary>
    private void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Home && !RecordPageParts.IsTextInput(e.OriginalSource))
        {
            e.Handled = true;
            ScrollToTop();
        }
    }

    private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HistoryRow row)
        {
            await Navigator.OpenLinkedRecordAsync(row.ToRef());
        }
    }

    // ---------------------------------------------------------------- Reading pane

    // The note in the reading pane: the newest on opening, then the one selected.
    private string? _selectedNoteId;
    private bool _selectingNote;

    private bool HasReadingPane => _columns.Reading > 0;

    /// <summary>
    /// With the reading pane a click (or Up and Down) selects a note and double-click or Enter
    /// opens it; without it a click opens the note.
    /// </summary>
    private void UpdateHistoryMode()
    {
        var reading = HasReadingPane;
        var mode = reading ? ListViewSelectionMode.Single : ListViewSelectionMode.None;
        if (HistoryList.SelectionMode == mode)
        {
            return;
        }
        HistoryList.SelectionMode = mode;
        HistoryList.IsItemClickEnabled = !reading;
        if (reading)
        {
            SelectNote();
        }
    }

    /// <summary>Selects the note just saved, else the one selected before, else the newest shown, and shows it.</summary>
    private void SelectNote()
    {
        if (!HasReadingPane)
        {
            return;
        }
        var id = _selectAfterLoad is { } saved && _rows.Exists(r => r.Id == saved) ? saved : _selectedNoteId;
        _selectAfterLoad = null;
        var row = _rows.Find(r => r.Id == id) ?? (_rows.Count > 0 ? _rows[0] : null);
        _selectingNote = true;
        try
        {
            HistoryList.SelectedItem = row;
        }
        finally
        {
            _selectingNote = false;
        }

        // Nothing shown (the filter matches none): the pane keeps the note it had, if it still exists.
        var note = row?.Note
            ?? BaseNotes.FirstOrDefault(n => n.Id == _selectedNoteId)
            ?? BaseNotes.FirstOrDefault();
        ShowReading(note);
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectingNote && HasReadingPane && HistoryList.SelectedItem is HistoryRow row)
        {
            ShowReading(row.Note);
        }
    }

    private void ShowReading(HistoryNote? note)
    {
        _selectedNoteId = note?.Id;
        ReadingCard.Visibility = note != null ? Visibility.Visible : Visibility.Collapsed;
        if (note == null)
        {
            return;
        }

        ReadingCaptionText.Text = ContactHistory.ReadingCaption(note);
        var title = ContactHistory.FullTitle(note);
        ReadingTitle.Text = title;
        AutomationProperties.SetName(ReadingCard, $"Selected note: {title}");

        // Untitled: the first line is the title, the rest the text.
        var text = note.Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');
        if (note.Title.Length == 0)
        {
            var newline = text.IndexOf('\n');
            text = newline < 0 ? "" : text[(newline + 1)..].TrimStart('\n');
        }
        ReadingText.Text = text;
        ReadingText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        ReadingLinks.Children.Clear();
        foreach (var link in note.Links)
        {
            ReadingLinks.Children.Add(CreateReadingChip(link));
        }
    }

    /// <summary>A linked record of the note: a type-colour dot and the name; opens the record.</summary>
    private Button CreateReadingChip(LinkedRecordSummary link)
    {
        var name = link.Name.Length > 0 ? link.Name : link.FirstLine.Length > 0 ? link.FirstLine : link.Type.DisplayName();
        var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Style = RecordTypeStyles.Dot(link.Type) };
        var chip = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    dot,
                    new TextBlock { Text = name, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis },
                },
            },
            Style = ChipStyle,
            Padding = new Thickness(10, 4, 12, 5),
            MinHeight = 0,
        };
        AutomationProperties.SetName(chip, $"{link.Type.DisplayName()}: {name}");
        ToolTipService.SetToolTip(chip, $"Open {name}");
        var target = new RecordRef(link.Type, link.Id);
        chip.Click += async (_, _) => await Navigator.OpenLinkedRecordAsync(target);
        return chip;
    }

    private async void ReadingOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNoteId is { } id)
        {
            await Navigator.OpenLinkedRecordAsync(new RecordRef(RecordType.Note, id));
        }
    }

    private async void ReadingEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNoteId is { } id)
        {
            await Navigator.ShowEditorAsync(new EditRequest(RecordType.Note, id));
        }
    }

    /// <summary>With the reading pane: double-click opens the note.</summary>
    private async void HistoryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (HasReadingPane && (e.OriginalSource as FrameworkElement)?.DataContext is HistoryRow row)
        {
            e.Handled = true;
            await Navigator.OpenLinkedRecordAsync(row.ToRef());
        }
    }

    /// <summary>With the reading pane: Enter opens the selected note.</summary>
    private async void HistoryList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (HasReadingPane && e.Key == VirtualKey.Enter && HistoryList.SelectedItem is HistoryRow row)
        {
            e.Handled = true;
            await Navigator.OpenLinkedRecordAsync(row.ToRef());
        }
    }

    /// <summary>Right-click, Shift+F10 or the context-menu key on a note: Open and Unlink.</summary>
    private void HistoryRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is HistoryRow row)
        {
            ShowRowMenu(sender, args, row.ToRef(), row.Title.Length > 0 ? row.Title : ContactHistory.FullTitle(row.Note));
        }
    }

    private void ShowRowMenu(UIElement sender, ContextRequestedEventArgs args, RecordRef record, string name) =>
        RecordPageParts.ShowRowMenu(sender, args, record, name, UnlinkAsync);

    // ---------------------------------------------------------------- People (organizations)

    // The person whose notes the history shows; null for every note. Not kept when the page is left.
    private string? _pickedPersonId;
    private bool _peopleExpanded;

    // The People column's cards, by contact id, to show the pick without rebuilding them.
    private readonly Dictionary<string, (Border Card, TextBlock Badge, Button Surface, PersonSummary Person)> _cards = [];

    private PersonSummary? PickedPerson =>
        _pickedPersonId is { } id ? ViewModel.People.FirstOrDefault(p => p.Id == id) : null;

    /// <summary>The People column (from band L): heading, cards, the not-linked box.</summary>
    private void ShowPeople()
    {
        PeoplePanel.Children.Clear();
        var shown = _columns.People > 0;
        PeoplePanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            return;
        }
        foreach (var part in CreatePeopleParts(cards: true))
        {
            PeoplePanel.Children.Add(part);
        }
    }

    /// <summary>
    /// The people: "People" with the count, "Show all" and "+"; a card per person (or, in band M,
    /// a compact row); with nobody linked a dashed box; then the contacts not linked yet.
    /// </summary>
    private List<FrameworkElement> CreatePeopleParts(bool cards)
    {
        if (cards)
        {
            _cards.Clear();
        }
        var parts = new List<FrameworkElement>();
        var people = ViewModel.People;
        var limit = OrganizationLogic.ShownPeopleCount(_band ?? RecordPageBand.L);

        var heading = SideColumn.Heading("People", people.Count);
        if (people.Count > limit)
        {
            var showAll = new HyperlinkButton { Content = _peopleExpanded ? "Show fewer" : "Show all", Padding = new Thickness(4, 2, 4, 2) };
            showAll.Click += (_, _) =>
            {
                _peopleExpanded = !_peopleExpanded;
                RebuildPeople();
            };
            SideColumn.AddToHeading(heading, showAll);
        }
        var plus = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        AutomationProperties.SetName(plus, "Link a contact");
        ToolTipService.SetToolTip(plus, "Find or create a contact to link");
        plus.Click += async (_, _) => await LinkAsync([RecordType.Contact], null);
        SideColumn.AddToHeading(heading, plus);
        parts.Add(heading);

        if (people.Count == 0)
        {
            parts.Add(NoPeopleBox());
        }
        foreach (var person in _peopleExpanded ? people : people.Take(limit))
        {
            parts.Add(cards ? PersonCard(person) : PersonRow(person));
        }
        if (ViewModel.NotLinked.TotalCount > 0)
        {
            parts.Add(NotLinkedBox());
        }
        return parts;
    }

    private void RebuildPeople()
    {
        ShowPeople();
        if (_band == RecordPageBand.M)
        {
            ShowSideColumn();
        }
        if (_band == RecordPageBand.S)
        {
            ShowSelectedTab();
        }
    }

    /// <summary>
    /// A person's card: picture, name and job title; a phone and an E-mail chip; the notes linked to
    /// both. A click or Space picks the person (the history then shows only their notes); a
    /// double-click or Enter opens the contact.
    /// </summary>
    private FrameworkElement PersonCard(PersonSummary person)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var notesLine = OrganizationLogic.NotesLine(person);
        var surface = new Button
        {
            Style = (Style)Application.Current.Resources["RowButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
        };
        Grid.SetRowSpan(surface, 3);
        AutomationProperties.SetHelpText(surface, "Press Space to show only the notes with this person, Enter to open the contact");
        surface.Click += (_, _) => TogglePerson(person.Id);
        surface.DoubleTapped += async (_, e) =>
        {
            e.Handled = true;
            await Navigator.OpenLinkedRecordAsync(new RecordRef(RecordType.Contact, person.Id));
        };
        surface.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await Navigator.OpenLinkedRecordAsync(new RecordRef(RecordType.Contact, person.Id));
            }
        };
        var contact = new RecordRef(RecordType.Contact, person.Id);
        surface.ContextRequested += (sender, args) => ShowRowMenu(sender, args, contact, person.Name);
        grid.Children.Add(surface);

        // Who: picture, name and job title; "Filtering history" in the corner while picked.
        var who = new Grid { ColumnSpacing = 10, Margin = new Thickness(12, 12, 12, 0), IsHitTestVisible = false };
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var picture = new PersonPicture
        {
            DisplayName = person.Name,
            Width = 32,
            Height = 32,
            Style = (Style)Application.Current.Resources["ContactPictureStyle"],
        };
        AutomationProperties.SetAccessibilityView(picture, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        who.Children.Add(picture);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(names, 1);
        names.Children.Add(new TextBlock { Text = person.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        if (person.JobTitle.Length > 0)
        {
            names.Children.Add(new TextBlock
            {
                Text = person.JobTitle,
                Style = (Style)Application.Current.Resources["SecondaryCaptionTextStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            });
        }
        who.Children.Add(names);
        var badge = new TextBlock
        {
            Text = "Filtering history",
            VerticalAlignment = VerticalAlignment.Top,
            Style = (Style)Application.Current.Resources["AccentCaptionTextStyle"],
        };
        var corner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        corner.Children.Add(badge);
        if (RecordPageParts.IsInTrail(contact))
        {
            var inTrail = RecordPageParts.InTrailTag();
            inTrail.HorizontalAlignment = HorizontalAlignment.Right;
            corner.Children.Add(inTrail);
            AutomationProperties.SetName(surface, $"{person.Name}. In trail");
        }
        Grid.SetColumn(corner, 2);
        who.Children.Add(corner);
        grid.Children.Add(who);

        // How to reach them: the phone chip and the E-mail chip, or a dashed "Phone or e-mail".
        var chips = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6, Margin = new Thickness(54, 8, 12, 0) };
        Grid.SetRow(chips, 1);
        if (person.Phone is { } phone)
        {
            chips.Children.Add(CardChip(phone, phone.Value, Glyphs.Phone));
        }
        if (person.Email is { } email)
        {
            chips.Children.Add(CardChip(email, "E-mail", Glyphs.Mail));
        }
        if (person.HasNoWayToReach)
        {
            chips.Children.Add(RecordPageParts.DashedButton("", "Phone or e-mail", $"Add a phone number or e-mail address for {person.Name}",
                async () => await Navigator.ShowEditorAsync(new EditRequest(RecordType.Contact, person.Id,
                    FocusField: ContactLogic.FieldOf(MissingContactFact.Phone)))));
        }
        grid.Children.Add(chips);

        var notes = new TextBlock
        {
            Text = notesLine,
            Margin = new Thickness(54, 8, 12, 12),
            IsHitTestVisible = false,
            Style = (Style)Application.Current.Resources["TertiaryCaptionTextStyle"],
        };
        Grid.SetRow(notes, 2);
        grid.Children.Add(notes);

        var card = new Border
        {
            Child = grid,
            Style = (Style)Application.Current.Resources["CardSurfaceStyle"],
            CornerRadius = new CornerRadius(6),
        };
        _cards[person.Id] = (card, badge, surface, person);
        ShowPick(person.Id);
        return card;
    }

    /// <summary>The card's look for whether its person is picked: an accent outline and "Filtering history".</summary>
    private void ShowPick(string personId)
    {
        if (!_cards.TryGetValue(personId, out var parts))
        {
            return;
        }
        var picked = personId == _pickedPersonId;
        parts.Card.BorderBrush = (Brush)Application.Current.Resources[picked ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];
        parts.Card.BorderThickness = new Thickness(picked ? 2 : 1);
        parts.Badge.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;
        var person = parts.Person;
        var name = string.Join(", ", new[] { person.Name, person.JobTitle, OrganizationLogic.NotesLine(person) }.Where(s => s.Length > 0));
        AutomationProperties.SetName(parts.Surface, picked ? $"{name}, filtering history" : name);
    }

    /// <summary>A chip on a card that calls or writes; the address in its tooltip.</summary>
    private Button CardChip(ContactChip chip, string text, string glyph)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12 });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        var button = new Button { Content = content, Style = ChipStyle, Padding = new Thickness(10, 3, 10, 4), MinHeight = 0 };
        AutomationProperties.SetName(button, chip.AutomationName);
        ToolTipService.SetToolTip(button, chip.IsPhone ? $"Call {chip.Value}" : $"Write to {chip.Value}");
        button.Click += async (_, _) => await LaunchAsync(chip, button);
        return button;
    }

    /// <summary>Calls or writes; when nothing handles tel: or mailto:, copies the value instead.</summary>
    private async Task LaunchAsync(ContactChip chip, FrameworkElement source)
    {
        var launched = false;
        if (chip.Target is { } uri)
        {
            try
            {
                var support = await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri);
                launched = support == LaunchQuerySupportStatus.Available && await Launcher.LaunchUriAsync(uri);
            }
            catch (Exception)
            {
                launched = false;
            }
        }
        if (!launched)
        {
            Copy(chip.Value, source);
        }
    }

    /// <summary>Picks the person (or, picked already, shows every note again).</summary>
    private void TogglePerson(string personId) => PickPerson(_pickedPersonId == personId ? null : personId);

    private void PickPerson(string? personId)
    {
        var previous = _pickedPersonId;
        _pickedPersonId = personId;
        _shownNotes = ContactHistory.PageSize;
        if (previous != null)
        {
            ShowPick(previous);
        }
        if (personId != null)
        {
            ShowPick(personId);
        }
        ShowHistory();
        if (personId != null && _band == RecordPageBand.S && _selectedTab != HistoryKey)
        {
            _selectedTab = HistoryKey;
            ShowTabs();
        }
    }

    private void PersonChip_Click(object sender, RoutedEventArgs e)
    {
        var id = _pickedPersonId;
        PickPerson(null);
        if (id != null && _cards.TryGetValue(id, out var parts))
        {
            parts.Surface.Focus(FocusState.Keyboard);
        }
    }

    /// <summary>A compact row (band M's side column): picture, name, and job title · phone. Opens the contact.</summary>
    private Button PersonRow(PersonSummary person)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var picture = new PersonPicture
        {
            DisplayName = person.Name,
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Style = (Style)Application.Current.Resources["ContactPictureStyle"],
        };
        AutomationProperties.SetAccessibilityView(picture, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        grid.Children.Add(picture);
        var second = string.Join(" · ", new[] { person.JobTitle, person.Phone?.Value ?? "" }.Where(s => s.Length > 0));
        var lines = new StackPanel();
        Grid.SetColumn(lines, 1);
        lines.Children.Add(new TextBlock { Text = person.Name, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Style = (Style)Application.Current.Resources["BodyTextBlockStyle"] });
        if (second.Length > 0)
        {
            lines.Children.Add(new TextBlock
            {
                Text = second,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Style = (Style)Application.Current.Resources["SecondaryCaptionTextStyle"],
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
        AutomationProperties.SetName(button, string.Join(". ", new[] { $"Contact: {person.Name}", second }.Where(s => s.Length > 0)));
        var target = new RecordRef(RecordType.Contact, person.Id);
        button.Click += async (_, _) => await Navigator.OpenLinkedRecordAsync(target);
        button.ContextRequested += (sender, args) => ShowRowMenu(sender, args, target, person.Name);
        RecordPageParts.MarkInTrail(button, grid, target);
        return button;
    }

    /// <summary>A dashed box around <paramref name="content"/>.</summary>
    private static Grid DashedBox(UIElement content)
    {
        var grid = new Grid();
        grid.Children.Add(new Rectangle
        {
            RadiusX = 8,
            RadiusY = 8,
            Style = (Style)Application.Current.Resources["DashedOutlineStyle"],
            StrokeDashArray = [4, 3],
        });
        grid.Children.Add(content);
        return grid;
    }

    /// <summary>Nobody linked yet: what the column is for, and "Link a contact".</summary>
    private FrameworkElement NoPeopleBox()
    {
        var panel = new StackPanel { Padding = new Thickness(16, 12, 16, 12), Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = "No people linked yet. Link the people who work here; notes about them then show in this history too.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
        });
        var link = new HyperlinkButton { Content = "Link a contact", Padding = new Thickness(0) };
        link.Click += async (_, _) => await LinkAsync([RecordType.Contact], null);
        panel.Children.Add(link);
        return DashedBox(panel);
    }

    /// <summary>
    /// Contacts whose Company field is the organization's name but who aren't linked: up to 3, each
    /// with a Link button, then "n more" (the link picker on contacts, with the name as the query).
    /// </summary>
    private FrameworkElement NotLinkedBox()
    {
        var name = ViewModel.Organization?.OrganizationName.Trim() ?? ViewModel.Name;
        var (shown, total) = ViewModel.NotLinked;
        var panel = new StackPanel { Padding = new Thickness(12, 10, 12, 10), Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Company field says {name}, not linked:",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SecondaryCaptionTextStyle"],
        });
        foreach (var contact in shown)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var picture = new PersonPicture
            {
                DisplayName = contact.Name,
                Width = 24,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["ContactPictureStyle"],
            };
            AutomationProperties.SetAccessibilityView(picture, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            row.Children.Add(picture);
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lines, 1);
            lines.Children.Add(new TextBlock { Text = contact.Name, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            if (contact.JobTitle.Length > 0)
            {
                lines.Children.Add(new TextBlock
                {
                    Text = contact.JobTitle,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Style = (Style)Application.Current.Resources["SecondaryCaptionTextStyle"],
                });
            }
            row.Children.Add(lines);
            var link = new Button { Content = "Link", VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(link, $"Link {contact.Name} to {name}");
            Grid.SetColumn(link, 2);
            var contactId = contact.Id;
            var contactName = contact.Name;
            link.Click += async (_, _) => await LinkContactAsync(contactId, contactName);
            row.Children.Add(link);
            panel.Children.Add(row);
        }
        if (total > shown.Count)
        {
            var more = new HyperlinkButton { Content = $"{total - shown.Count} more", Padding = new Thickness(0) };
            AutomationProperties.SetName(more, $"{total - shown.Count} more contacts at {name}");
            more.Click += async (_, _) => await LinkAsync([RecordType.Contact], name);
            panel.Children.Add(more);
        }
        return DashedBox(panel);
    }

    /// <summary>Links a contact to the organization; the InfoBar offers Undo.</summary>
    private async Task LinkContactAsync(string contactId, string name)
    {
        try
        {
            _focusPersonAfterLoad = contactId;
            await ViewModel.LinkContactAsync(contactId);
            ShowUndo($"Linked {name}", async () => await ViewModel.UnlinkAsync(contactId));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't link records", ex);
        }
    }

    // ---------------------------------------------------------------- Side column

    private void ShowSideColumn()
    {
        if (_band is not { } band || band == RecordPageBand.S)
        {
            SidePanel.Children.Clear();
            SidePanel.ColumnDefinitions.Clear();
            return;
        }
        SideColumn.Flow(SidePanel, CreateSections(includeAbout: true, includePeople: band == RecordPageBand.M), band, _columns.SideColumnCount);
    }

    /// <summary>
    /// People (an organization in band M), About (in the side column), then the side column's types;
    /// with none of those linked, the link buttons.
    /// </summary>
    private List<FrameworkElement> CreateSections(bool includeAbout, bool includePeople)
    {
        var sections = new List<FrameworkElement>();
        if (includePeople && ViewModel.IsOrganization)
        {
            var people = new StackPanel { Spacing = 2, Tag = Math.Min(ViewModel.People.Count, 5) + 1 };
            foreach (var part in CreatePeopleParts(cards: false))
            {
                people.Children.Add(part);
            }
            sections.Add(people);
        }
        if (includeAbout && ViewModel.HasComments)
        {
            sections.Add(CreateAboutSection());
        }
        if (ViewModel.SideCount == 0)
        {
            sections.Add(CreateNoLinksSection());
            return sections;
        }
        foreach (var type in ViewModel.SideTypes)
        {
            var records = ViewModel.SideRecords(type);
            if (records.Count > 0)
            {
                sections.Add(_side.Section(type, records, _band ?? RecordPageBand.L));
            }
        }
        return sections;
    }

    private FrameworkElement CreateAboutSection()
    {
        var panel = new StackPanel { Spacing = 6, Tag = 3 };
        panel.Children.Add(SideColumn.Heading("About", null));
        var text = new TextBlock
        {
            Text = ViewModel.Comments,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = _aboutExpanded ? 0 : 4,
            IsTextSelectionEnabled = true,
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
        };
        panel.Children.Add(text);
        var more = new HyperlinkButton
        {
            Content = _aboutExpanded ? "Less" : "More",
            Padding = new Thickness(0),
            Visibility = _aboutExpanded ? Visibility.Visible : Visibility.Collapsed,
        };
        text.IsTextTrimmedChanged += (_, _) =>
        {
            if (!_aboutExpanded)
            {
                more.Visibility = text.IsTextTrimmed ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        more.Click += (_, _) =>
        {
            _aboutExpanded = !_aboutExpanded;
            text.MaxLines = _aboutExpanded ? 0 : 4;
            more.Content = _aboutExpanded ? "Less" : "More";
        };
        panel.Children.Add(more);
        return panel;
    }

    /// <summary>Nothing linked in the side column's types: buttons to link some.</summary>
    private FrameworkElement CreateNoLinksSection() =>
        SideColumn.NoLinksSection(ViewModel.IsOrganization
            ?
            [
                ("Add to a category", () => LinkAsync([RecordType.Category], null)),
                ("Link an item", () => LinkAsync([RecordType.Item], null)),
                ("Link anything… (Ctrl+L)", () => LinkAsync(null, null)),
            ]
            :
            [
                ("Link an organization", () => LinkAsync([RecordType.Organization], null)),
                ("Link a contact", () => LinkAsync([RecordType.Contact], null)),
                ("Add to a category", () => LinkAsync([RecordType.Category], null)),
                ("Link anything… (Ctrl+L)", () => LinkAsync(null, null)),
            ]);

    private void RebuildSections()
    {
        ShowSideColumn();
        if (_band == RecordPageBand.S)
        {
            ShowSelectedTab();
        }
    }

    // ---------------------------------------------------------------- Note box

    private DateOnly _noteDay;
    private TimeSpan _noteTime;
    private bool _noteDatePicked;
    private bool _noteBoxCollapsed = true;
    private bool _keepCollapsed;
    private bool _updatingNotePickers;
    private bool _savingNote;

    private bool HasDraft => !string.IsNullOrWhiteSpace(NoteTextBox.Text) || !string.IsNullOrWhiteSpace(NoteTitleBox.Text);

    private void ShowNoteBoxTexts()
    {
        NoteTextBox.PlaceholderText = $"Write a note about {ViewModel.Name}…";
        AutomationProperties.SetName(NoteTextBox, $"Write a note about {ViewModel.Name}");
        WillLinkText.Text = $"Will be linked to {ViewModel.Name}";
    }

    /// <summary>Ctrl+Shift+N: scrolls the note box into view and puts the cursor in it.</summary>
    private void NoteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FocusNoteBox();
    }

    private void FocusNoteBox()
    {
        if (!ViewModel.IsAvailable)
        {
            return;
        }
        if (_band == RecordPageBand.S && _selectedTab != HistoryKey)
        {
            _selectedTab = HistoryKey;
            ShowTabs();
        }
        MainScroller.ChangeView(null, 0, null);
        _keepCollapsed = false;
        NoteTextBox.Focus(FocusState.Keyboard);
        ExpandNoteBox();
    }

    private void NoteTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_keepCollapsed)
        {
            ExpandNoteBox();
        }
    }

    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NoteSaveButton.IsEnabled = HasDraft && !_savingNote;
        if (HasDraft && _noteBoxCollapsed && NoteTextBox.FocusState != FocusState.Unfocused)
        {
            _keepCollapsed = false;
            ExpandNoteBox();
        }
    }

    /// <summary>Grows the box in place: several lines, then day and time, title, Open full editor, Cancel and Save.</summary>
    private void ExpandNoteBox()
    {
        if (!_noteBoxCollapsed)
        {
            return;
        }
        _noteBoxCollapsed = false;
        if (!_noteDatePicked)
        {
            var now = DateTime.Now;
            _noteDay = DateOnly.FromDateTime(now);
            _noteTime = new TimeSpan(now.Hour, now.Minute, 0);
        }
        ShowNoteDay();
        NoteTextBox.MinHeight = 88;
        NoteTextBox.MaxHeight = 320;
        ShortcutHint.Visibility = Visibility.Collapsed;
        NoteBoxCommands.Visibility = Visibility.Visible;
        NoteTitleBox.Visibility = NoteTitleBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoteBox.Padding = new Thickness(12);
        NoteBox.Style = (Style)Resources["NoteBoxOpenStyle"];
    }

    /// <summary>Back to one line. The draft stays unless <paramref name="clear"/>.</summary>
    private void CollapseNoteBox(bool clear)
    {
        if (clear)
        {
            NoteTextBox.Text = "";
            NoteTitleBox.Text = "";
            _noteDatePicked = false;
        }
        _noteBoxCollapsed = true;
        NoteTextBox.MinHeight = 0;
        NoteTextBox.MaxHeight = clear || !HasDraft ? double.PositiveInfinity : 34;
        ShortcutHint.Visibility = HasDraft ? Visibility.Collapsed : Visibility.Visible;
        NoteBoxCommands.Visibility = Visibility.Collapsed;
        NoteTitleBox.Visibility = Visibility.Collapsed;
        NoteBox.Padding = new Thickness(0);
        NoteBox.Style = (Style)Resources["NoteBoxClosedStyle"];
    }

    private void ShowNoteDay()
    {
        NoteDayText.Text = ContactLogic.DayAndTimeText(_noteDay, _noteTime, DateOnly.FromDateTime(DateTime.Now));
        AutomationProperties.SetName(NoteDayButton, $"Day and time: {NoteDayText.Text}");
    }

    private void NoteDayFlyout_Opening(object? sender, object e)
    {
        _updatingNotePickers = true;
        NoteDatePicker.Date = new DateTimeOffset(_noteDay.ToDateTime(TimeOnly.MinValue));
        NoteTimePicker.Time = _noteTime;
        _updatingNotePickers = false;
    }

    private void NoteDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_updatingNotePickers || args.NewDate is not { } date)
        {
            return;
        }
        _noteDay = DateOnly.FromDateTime(date.Date);
        _noteDatePicked = true;
        ShowNoteDay();
    }

    private void NoteTimePicker_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
    {
        if (_updatingNotePickers)
        {
            return;
        }
        _noteTime = e.NewTime;
        _noteDatePicked = true;
        ShowNoteDay();
    }

    private void NoteTitleButton_Click(object sender, RoutedEventArgs e)
    {
        NoteTitleBox.Visibility = Visibility.Visible;
        NoteTitleBox.Focus(FocusState.Keyboard);
    }

    /// <summary>Ctrl+Enter saves; Esc collapses the box and keeps the draft.</summary>
    private async void NoteBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Enter && ctrl)
        {
            e.Handled = true;
            await SaveNoteAsync();
        }
        else if (e.Key == VirtualKey.Escape && !_noteBoxCollapsed)
        {
            e.Handled = true;
            _keepCollapsed = true;
            CollapseNoteBox(clear: false);
        }
    }

    private void NoteCancel_Click(object sender, RoutedEventArgs e)
    {
        _keepCollapsed = true;
        CollapseNoteBox(clear: true);
        NoteTextBox.Focus(FocusState.Programmatic);
    }

    private async void NoteSave_Click(object sender, RoutedEventArgs e) => await SaveNoteAsync();

    /// <summary>
    /// Saves the note and its link to the record in one transaction; the box then empties and
    /// collapses, and keeps the focus.
    /// </summary>
    private async Task<bool> SaveNoteAsync()
    {
        if (!HasDraft || _savingNote)
        {
            return !HasDraft;
        }
        _savingNote = true;
        NoteSaveButton.IsEnabled = false;
        try
        {
            if (!_noteDatePicked && _noteBoxCollapsed)
            {
                var now = DateTime.Now;
                _noteDay = DateOnly.FromDateTime(now);
                _noteTime = new TimeSpan(now.Hour, now.Minute, 0);
            }
            var note = await App.Container.CreateLinkedNote.InvokeAsync(
                Record.Id, ViewModel.Type, NoteTitleBox.Text, NoteTextBox.Text, _noteDay, _noteTime);
            _selectAfterLoad = note.Id;
            _keepCollapsed = true;
            CollapseNoteBox(clear: true);
            NoteTextBox.Focus(FocusState.Programmatic);
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't save the note", ex);
            return false;
        }
        finally
        {
            _savingNote = false;
            NoteSaveButton.IsEnabled = HasDraft;
        }
    }

    // The note saved from the box, selected in the reading pane once the history has reloaded.
    private string? _selectAfterLoad;

    /// <summary>Opens the note editor with the draft, for a new note linked to the record.</summary>
    private async void OpenFullEditor_Click(object sender, RoutedEventArgs e)
    {
        var draft = new NoteDraft(NoteTitleBox.Text, NoteTextBox.Text, _noteDay, _noteTime);
        CollapseNoteBox(clear: true);
        await Navigator.ShowEditorAsync(new EditRequest(RecordType.Note, LinkFrom: Record, NoteDraft: draft));
    }

    /// <summary>Leaving with a draft asks, as the editor does: save it, drop it, or stay.</summary>
    public async Task<bool> CanLeaveAsync()
    {
        if (_savingNote)
        {
            return false;
        }
        if (!HasDraft)
        {
            return true;
        }
        var choice = await Dialogs.AskAsync("Save changes?", "Do you want to save your changes to this note?", "Save", "Don't save", "Cancel");
        switch (choice)
        {
            case ContentDialogResult.Primary:
                return await SaveNoteAsync();
            case ContentDialogResult.Secondary:
                CollapseNoteBox(clear: true);
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- Commands

    private async void Edit_Click(object sender, RoutedEventArgs e) =>
        await Navigator.ShowEditorAsync(new EditRequest(ViewModel.Type, Record.Id));

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

        // New linked: a new record of every other type, Note first, linked to this one.
        var newLinked = new MenuFlyoutSubItem { Text = "New linked", Icon = new FontIcon { Glyph = "" } };
        foreach (var type in RecordTypeExtensions.UserTypes.Where(t => t != ViewModel.Type))
        {
            var item = new MenuFlyoutItem
            {
                Text = type.DisplayName(),
                Icon = new FontIcon { Glyph = Glyphs.For(type), Foreground = RecordTypeStyles.Brush(this, type, ColorRole.Outline) },
            };
            AutomationProperties.SetName(item, $"New linked {type.DisplayName().ToLower(CultureInfo.CurrentCulture)}");
            item.Click += async (_, _) => await Navigator.ShowEditorAsync(new EditRequest(type, LinkFrom: Record));
            newLinked.Items.Add(item);
        }
        MoreMenu.Items.Add(newLinked);

        var copy = new MenuFlyoutItem
        {
            Text = $"Copy {TypeWord} details",
            Icon = new FontIcon { Glyph = "" },
            KeyboardAcceleratorTextOverride = "Ctrl+Shift+C",
        };
        copy.Click += (_, _) => CopyDetails();
        MoreMenu.Items.Add(copy);
        RecordPageParts.AddShowInList(MoreMenu, Record);
        MoreMenu.Items.Add(new MenuFlyoutSeparator());

        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "" } };
        delete.KeyboardAcceleratorTextOverride = "Del";
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
            Copy(ViewModel.CopyText, NameText, $"{ViewModel.Type.DisplayName()} details copied");
        }
    }

    private Style ChipStyle => (Style)Resources["ContactChipButtonStyle"];

    private void Copy(string text, FrameworkElement source, string message = "Copied") =>
        RecordPageParts.Copy(text, CopiedTip, source, message);

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Del deletes the record (with a confirmation), unless a text box has the focus.
        if (e.Key == VirtualKey.Delete && !RecordPageParts.IsTextInput(e.OriginalSource))
        {
            e.Handled = true;
            _ = DeleteWithConfirmationAsync();
        }
        // Esc shows every note again after a person was picked.
        else if (e.Key == VirtualKey.Escape && _pickedPersonId != null)
        {
            e.Handled = true;
            PickPerson(null);
        }
    }

    /// <summary>Asks, then deletes the record.</summary>
    public async Task DeleteWithConfirmationAsync()
    {
        if (!ViewModel.IsAvailable)
        {
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            $"Delete this {TypeWord}?",
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

    // What Undo does for the last unlink or link; null once done or closed.
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
            ShowUndo($"Unlinked {name}", () => ViewModel.UndoUnlinkAsync(removed));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't unlink", ex);
        }
    }

    private void ShowUndo(string message, Func<Task> undo)
    {
        _undo = undo;
        UnlinkInfoBar.Message = message;
        UnlinkInfoBar.IsOpen = false;
        UnlinkInfoBar.IsOpen = true;
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
