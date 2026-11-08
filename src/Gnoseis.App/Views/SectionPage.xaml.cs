/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Gnoseis.App.Views;

/// <summary>
/// A section: the list of one record type with the selected record (or an editor) next to it.
/// In a narrow window only one of the two panes is shown at a time. Records opened from links on a
/// record page are shown in the detail pane too, in a trail that starts at the record selected in the list.
/// </summary>
public sealed partial class SectionPage : Page, ISectionHost, IConfirmLeave
{
    private const double NarrowWidth = 720;

    // Selected record of each section, restored when the user comes back to the section.
    private static readonly Dictionary<RecordType, string> LastSelected = [];

    private bool _updatingSelection;
    private string? _selectedId;
    private int? _selectIndexAfterReload;
    private bool _isNarrow;
    private readonly RecordTrail<RecordRef> _trail = new();
    private readonly Dictionary<RecordRef, double> _scrollOffsets = [];

    private bool _updatingTrailSelection;

    public SectionPage()
    {
        InitializeComponent();
        TrailChanged += (_, _) => RefreshTrailColumn();
        SetSlideIn(ListPane);
        SetSlideIn(TrailPane);
    }

    public RecordListViewModel ViewModel { get; private set; } = null!;

    /// <summary>The trail column's rows, header and hint.</summary>
    public TrailViewModel TrailView { get; private set; } = null!;

    public RecordType Type => ViewModel.Type;

    public bool CanHandleBack => _trail.IsOpen || (_isNarrow && IsDetailShown);

    public RecordTrail<RecordRef> Trail => _trail;

    /// <summary>Raised when records are added to the trail, it's stepped along, or it closes.</summary>
    public event EventHandler? TrailChanged;

    private bool IsDetailShown => DetailFrame.Content is not EmptyDetailPage and not null;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var args = e.Parameter as SectionArgs ?? new SectionArgs(RecordType.Note);
        ViewModel = new RecordListViewModel(args.Type);
        ViewModel.GroupsChanged += ViewModel_GroupsChanged;
        ViewModel.Activate();
        TrailView = new TrailViewModel(args.Type, _trail);
        TrailView.PropertyChanged += TrailView_PropertyChanged;
        TrailView.RecordsMissing += TrailView_RecordsMissing;
        TrailView.Activate();
        Navigator.ActiveSection = this;
        App.Settings.LastSection = args.Type.ToString();

        ShowEmptyDetail();
        await ViewModel.LoadAsync();

        if (args.Edit != null)
        {
            await ShowEditorAsync(args.Edit);
        }
        else if ((args.SelectedId ?? LastSelected.GetValueOrDefault(args.Type)) is { } id && (args.SelectedId != null || ViewModel.FindRow(id) != null))
        {
            await ShowRecordAsync(id);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Deactivate();
        ViewModel.GroupsChanged -= ViewModel_GroupsChanged;
        TrailView.Deactivate();
        TrailView.PropertyChanged -= TrailView_PropertyChanged;
        TrailView.RecordsMissing -= TrailView_RecordsMissing;
        if (ReferenceEquals(Navigator.ActiveSection, this))
        {
            Navigator.ActiveSection = null;
        }
    }

    public Task<bool> CanLeaveAsync() =>
        DetailFrame.Content is IConfirmLeave editor ? editor.CanLeaveAsync() : Task.FromResult(true);

    // ---------------------------------------------------------------- List

    private void ViewModel_GroupsChanged(object? sender, EventArgs e)
    {
        _updatingSelection = true;
        try
        {
            GroupsSource.Source = ViewModel.Groups;
            RecordList.ItemsSource = GroupsSource.View;

            if (_selectIndexAfterReload is { } index)
            {
                // After a delete, move on to the next record, like Mail and Outlook do.
                _selectIndexAfterReload = null;
                var rows = ViewModel.VisibleRows;
                if (rows.Count > 0 && !_isNarrow)
                {
                    var next = rows[Math.Min(index, rows.Count - 1)];
                    RecordList.SelectedItem = next;
                    ShowDetails(next.Id);
                }
                return;
            }

            if (_selectedId != null && ViewModel.FindRow(_selectedId) is { } row)
            {
                RecordList.SelectedItem = row;
            }
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private async void RecordList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection || RecordList.SelectedItem is not RecordRow row || (row.Id == _selectedId && IsDetailShown && DetailFrame.Content is IRecordPage))
        {
            return;
        }

        if (!await CanLeaveAsync())
        {
            // Stay in the editor: put the selection back.
            SetListSelection(_selectedId);
            return;
        }
        ShowDetails(row.Id);
    }

    /// <summary>A click on the row already selected while only the list is shown (narrow window) opens it.</summary>
    private async void RecordList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecordRow row && row.Id == _selectedId && !IsDetailShown && await CanLeaveAsync())
        {
            ShowDetails(row.Id);
        }
    }

    /// <summary>
    /// Enter (or the box's icon) with at least 3 characters opens the search page for this section's
    /// type with the filter text; the filter stays as it is in the section.
    /// </summary>
    private async void FilterBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = (args.QueryText ?? "").Trim();
        if (query.Length >= Gnoseis.Core.Domain.CreateFromSearch.MinimumQueryLength)
        {
            await Navigator.ShowSearchAsync(query, ViewModel.Type);
        }
    }

    private async void RecordList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (RecordPageParts.IsDeleteKey(e.Key) && DetailFrame.Content is IRecordPage details)
        {
            e.Handled = true;
            await details.DeleteWithConfirmationAsync();
        }
        else if (e.Key == VirtualKey.F2 && DetailFrame.Content is IRecordPage shown)
        {
            e.Handled = true;
            shown.Edit();
        }
        else if (e.Key == VirtualKey.Enter && DetailFrame.Content is IRecordPage)
        {
            e.Handled = true;
            DetailFrame.Focus(FocusState.Keyboard);
        }
    }

    /// <summary>
    /// Find on this screen: focuses the record page's own search or filter box, or the list's filter
    /// box, with its text selected. False when neither is showing, so the caller searches everything.
    /// </summary>
    public bool FocusFindBox()
    {
        var record = DetailFrame.Content as IRecordPage;
        var recordBox = record != null && DetailFrame.Visibility == Visibility.Visible && record.IsFindBoxShowing;
        var focusInRecord = KeyboardFocus.IsInside(KeyboardFocus.Focused(XamlRoot), DetailFrame);
        var listFilter = ListPane.Visibility == Visibility.Visible && KeyboardFocus.IsShowing(FilterBox);

        switch (FindOnScreen.Choose(focusInRecord, recordBox, listFilter))
        {
            case FindTarget.RecordPageBox:
                record!.FocusFindBox();
                return true;
            case FindTarget.ListFilterBox:
                KeyboardFocus.FocusAndSelectAll(FilterBox);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The pane the element is in: the list or trail column, or the record; null for neither.</summary>
    public Pane? PaneOf(DependencyObject? element) =>
        KeyboardFocus.IsInside(element, ListPane) || KeyboardFocus.IsInside(element, TrailPane) ? Pane.List
        : KeyboardFocus.IsInside(element, DetailFrame) || KeyboardFocus.IsInside(element, TrailCrumbs) ? Pane.Record
        : null;

    /// <summary>The list, or the trail column in its place, is showing.</summary>
    public bool IsListShowing => ListPane.Visibility == Visibility.Visible || TrailPane.Visibility == Visibility.Visible;

    /// <summary>Focuses the selected row of the list or trail column, else its first row.</summary>
    public bool FocusList()
    {
        var list = TrailPane.Visibility == Visibility.Visible ? TrailList : RecordList;
        if (list.Visibility != Visibility.Visible)
        {
            return false;
        }
        if (list.SelectedItem is { } selected && list.ContainerFromItem(selected) is Control row)
        {
            return row.Focus(FocusState.Keyboard);
        }
        if (list.Items.Count > 0 && list.ContainerFromIndex(0) is Control first)
        {
            return first.Focus(FocusState.Keyboard);
        }
        return list.Focus(FocusState.Keyboard);
    }

    /// <summary>The first control of the record pane (the narrow window's trail bar first), when one is showing.</summary>
    public Control? FirstInRecord()
    {
        foreach (var part in new FrameworkElement[] { TrailCrumbs, DetailFrame })
        {
            if (part.Visibility == Visibility.Visible && FocusManager.FindFirstFocusableElement(part) is Control control)
            {
                return control;
            }
        }
        return null;
    }

    private async void NewButton_Click(object sender, RoutedEventArgs e) =>
        await ShowEditorAsync(new EditRequest(Type));

    private void SetListSelection(string? id)
    {
        _updatingSelection = true;
        try
        {
            var row = id == null ? null : ViewModel.FindRow(id);
            RecordList.SelectedItem = row;
            if (row != null)
            {
                RecordList.ScrollIntoView(row);
            }
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    // ---------------------------------------------------------------- Detail pane

    // Contacts, notes, items, organizations and categories each have their own page.
    private static Type PageFor(RecordType type) => type switch
    {
        RecordType.Contact => typeof(ContactPage),
        RecordType.Note => typeof(NotePage),
        RecordType.Item or RecordType.Organization => typeof(RecordHistoryPage),
        _ => typeof(CategoryPage),
    };

    /// <summary>Shows the record selected in the list; any trail starts again from it.</summary>
    private void ShowDetails(string id)
    {
        _selectedId = id;
        LastSelected[Type] = id;
        var record = new RecordRef(Type, id);
        _trail.Start(record);
        _scrollOffsets.Clear();
        NavigateDetail(record, new EntranceNavigationTransitionInfo());
    }

    private void NavigateDetail(RecordRef record, NavigationTransitionInfo transition)
    {
        // Each record in the trail is shown again where it was scrolled to.
        if (DetailFrame.Content is IRecordPage shown && ScrollerOf(DetailFrame.Content) is { } oldScroller)
        {
            _scrollOffsets[shown.Record] = oldScroller.VerticalOffset;
        }
        var offset = _scrollOffsets.GetValueOrDefault(record);
        if (!_trail.IsOpen)
        {
            _scrollOffsets.Clear();
        }

        DetailFrame.Navigate(PageFor(record.Type), record, transition);
        DetailFrame.BackStack.Clear();
        UpdatePanes();
        TrailChanged?.Invoke(this, EventArgs.Empty);

        if (offset > 0 && ScrollerOf(DetailFrame.Content) is { } scroller)
        {
            RestoreScroll(scroller, offset);
        }
    }

    private static ScrollViewer? ScrollerOf(object? page) => (page as FrameworkElement)?.FindName("MainScroller") as ScrollViewer;

    /// <summary>Scrolls a record page back to an offset once its content is tall enough (it loads after it's shown).</summary>
    private static void RestoreScroll(ScrollViewer scroller, double offset)
    {
        var until = DateTime.UtcNow.AddSeconds(2);
        void OnLayoutUpdated(object? sender, object e)
        {
            if (scroller.ScrollableHeight >= offset - 1 || DateTime.UtcNow > until)
            {
                scroller.LayoutUpdated -= OnLayoutUpdated;
                scroller.ChangeView(null, Math.Min(offset, scroller.ScrollableHeight), null, disableAnimation: true);
            }
        }
        scroller.LayoutUpdated += OnLayoutUpdated;
    }

    private bool IsShowing(RecordRef record) => DetailFrame.Content is IRecordPage page && page.Record == record;

    private void ShowEmptyDetail()
    {
        _selectedId = null;
        _trail.Clear();
        TrailChanged?.Invoke(this, EventArgs.Empty);
        DetailFrame.Navigate(typeof(EmptyDetailPage), ViewModel, new SuppressNavigationTransitionInfo());
        DetailFrame.BackStack.Clear();
        SetListSelection(null);
        UpdatePanes();
    }

    public async Task ShowRecordAsync(string id)
    {
        if (id == _selectedId && !_trail.IsOpen && DetailFrame.Content is IRecordPage)
        {
            return;
        }
        if (!await CanLeaveAsync())
        {
            return;
        }
        if (ViewModel.FindRow(id) == null && ViewModel.Filter.Length > 0)
        {
            ViewModel.Filter = "";
        }
        SetListSelection(id);
        ShowDetails(id);
    }

    public async Task ShowEditorAsync(EditRequest request)
    {
        if (!await CanLeaveAsync())
        {
            return;
        }

        var page = typeof(RecordEditPage);

        if (request.IsNew && request.LinkFrom == null)
        {
            // A new record replaces whatever was shown.
            SetListSelection(null);
            _selectedId = null;
            _trail.Clear();
            TrailChanged?.Invoke(this, EventArgs.Empty);
            DetailFrame.Navigate(page, request, new DrillInNavigationTransitionInfo());
            DetailFrame.BackStack.Clear();
        }
        else
        {
            // Editing, or creating a record linked to the one shown: return to it afterwards.
            DetailFrame.Navigate(page, request, new DrillInNavigationTransitionInfo());
        }
        UpdatePanes();
    }

    public async Task CloseEditorAsync(EditRequest request, RecordRef? saved)
    {
        if (saved != null && request.IsNew && request.LinkFrom == null && saved.Type == Type)
        {
            // Show the new record in the list and open it.
            await ViewModel.LoadAsync();
            SetListSelection(saved.Id);
            ShowDetails(saved.Id);
        }
        else if (DetailFrame.CanGoBack)
        {
            DetailFrame.GoBack();
            UpdatePanes();
        }
        else if (_selectedId != null)
        {
            ShowDetails(_selectedId);
        }
        else
        {
            ShowEmptyDetail();
        }
    }

    public void OnRecordDeleted(string id)
    {
        if (_trail.IsOpen && _trail.First!.Id != id)
        {
            // A record of the trail other than the list's: only it is removed, one step back. (Not in
            // the trail any more: it was already removed when the data change was noticed.)
            if (_trail.Items.FirstOrDefault(r => r.Id == id) is { } deleted)
            {
                RemoveFromTrail(deleted);
            }
            return;
        }

        var rows = ViewModel.VisibleRows;
        var index = rows.FindIndex(r => r.Id == id);
        LastSelected.Remove(Type);
        ShowEmptyDetail();
        _selectIndexAfterReload = index >= 0 ? index : null;
    }

    // ---------------------------------------------------------------- Trail

    public async Task OpenLinkedRecordAsync(RecordRef record)
    {
        if (_trail.Current == null)
        {
            // Nothing selected in the list (should not happen from a record page): open it as from search.
            if (record.Type == Type)
            {
                await ShowRecordAsync(record.Id);
            }
            else
            {
                await Navigator.OpenRecordAsync(record);
            }
            return;
        }
        if (_trail.Current == record && IsShowing(record))
        {
            return;
        }
        if (!await CanLeaveAsync())
        {
            return;
        }
        var index = _trail.IndexOf(record);
        var forward = index < 0 || index > _trail.CurrentIndex;
        _trail.Open(record);
        NavigateDetail(record, Slide(forward));
    }

    public async Task<bool> TryGoForwardAsync()
    {
        if (!_trail.CanGoForward || !await CanLeaveAsync())
        {
            return false;
        }
        _trail.Forward();
        NavigateDetail(_trail.Current!, Slide(forward: true));
        return true;
    }

    /// <summary>Makes a row of the trail current (a click on it); the rows after it stay.</summary>
    public async Task GoToTrailRowAsync(int index)
    {
        if (index == _trail.CurrentIndex && IsShowing(_trail.Current!))
        {
            return;
        }
        if (!await CanLeaveAsync())
        {
            return;
        }
        var forward = index > _trail.CurrentIndex;
        if (_trail.GoTo(index))
        {
            NavigateDetail(_trail.Current!, Slide(forward));
        }
    }

    public async Task CloseTrailAsync()
    {
        if (!_trail.IsOpen || !await CanLeaveAsync())
        {
            return;
        }
        _trail.Close();
        ShowTrailFirst();
    }

    /// <summary>
    /// After the trail closed: its first record selected in the list, with its page next to it, or in a
    /// narrow window the list alone.
    /// </summary>
    private void ShowTrailFirst()
    {
        var first = _trail.First!;
        SetListSelection(first.Id);
        if (_isNarrow)
        {
            DetailFrame.Navigate(typeof(EmptyDetailPage), ViewModel, new SuppressNavigationTransitionInfo());
            DetailFrame.BackStack.Clear();
            UpdatePanes();
            TrailChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        NavigateDetail(first, Slide(forward: false));
    }

    private static NavigationTransitionInfo Slide(bool forward) => new SlideNavigationTransitionInfo
    {
        Effect = forward ? SlideNavigationTransitionEffect.FromRight : SlideNavigationTransitionEffect.FromLeft,
    };

    // ---------------------------------------------------------------- Trail column

    public string? TrailTitleOf(RecordRef record) => TrailView?.TitleOf(record);

    /// <summary>
    /// Removes a deleted record from the trail; the others keep their order. When it was shown, the
    /// record before it is shown instead; when one record is left, the trail closes.
    /// </summary>
    private void RemoveFromTrail(RecordRef record)
    {
        var wasShown = _trail.Current == record;
        _trail.Remove(record);
        _scrollOffsets.Remove(record);
        if (!_trail.IsOpen)
        {
            _scrollOffsets.Clear();
            if (_trail.First is { } first)
            {
                SetListSelection(first.Id);
            }
        }
        if (wasShown && _trail.Current is { } current)
        {
            NavigateDetail(current, Slide(forward: false));
        }
        else
        {
            UpdatePanes();
            TrailChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Records of the trail deleted elsewhere (another window, a restore, a sync).</summary>
    private void TrailView_RecordsMissing(object? sender, IReadOnlyList<RecordRef> missing)
    {
        foreach (var record in missing)
        {
            if (!_trail.Contains(record))
            {
                continue;
            }
            if (record == _trail.First)
            {
                // The list's own record: the list moves on, as after any delete.
                OnRecordDeleted(record.Id);
                return;
            }
            RemoveFromTrail(record);
        }
    }

    private void RefreshTrailColumn()
    {
        if (TrailView == null)
        {
            return;
        }
        TrailView.Refresh();
        UpdateTrailRows();
    }

    private void UpdateTrailRows()
    {
        _updatingTrailSelection = true;
        try
        {
            var rows = TrailView.Rows;
            TrailList.ItemsSource = rows;
            if (_trail.CurrentIndex >= 0 && _trail.CurrentIndex < rows.Count)
            {
                var current = rows[_trail.CurrentIndex];
                TrailList.SelectedItem = current;
                TrailList.ScrollIntoView(current);
            }
        }
        finally
        {
            _updatingTrailSelection = false;
        }
        UpdateBreadcrumb();
    }

    private void UpdateBreadcrumb()
    {
        TrailBreadcrumb.ItemsSource = TrailView.Crumbs();
        ForwardCrumbs.Children.Clear();
        foreach (var row in TrailView.Rows.Where(r => r.IsAfterCurrent))
        {
            ForwardCrumbs.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 10,
                Margin = new Thickness(6, 0, 6, 0),
                Opacity = row.RowOpacity,
                Style = (Style)Application.Current.Resources[row.UsesDisabledText ? "DisabledGlyphStyle" : "SecondaryGlyphStyle"],
            });
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = row.Title,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 160,
                    Style = (Style)Application.Current.Resources[row.UsesDisabledText ? "TrailSubtitleDisabledTextStyle" : "BodyTextBlockStyle"],
                },
                Opacity = row.RowOpacity,
                Padding = new Thickness(4, 2, 4, 2),
                Style = (Style)Application.Current.Resources["RowButtonStyle"],
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, row.AutomationName);
            var index = row.Index;
            button.Click += async (_, _) => await GoToTrailRowAsync(index);
            ForwardCrumbs.Children.Add(button);
        }
    }

    private async void TrailBreadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is not TrailCrumb crumb || crumb.IsCurrent)
        {
            return;
        }
        if (crumb.Index < 0)
        {
            await CloseTrailAsync();
        }
        else
        {
            await GoToTrailRowAsync(crumb.Index);
        }
    }

    /// <summary>True while the section is narrower than 720 px (the list or the record, one at a time).</summary>
    public bool IsNarrow => _isNarrow;

    /// <summary>Raised when the section crosses the narrow width.</summary>
    public event EventHandler? NarrowChanged;

    private void TrailView_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Titles loaded after the rows were first shown.
        if (e.PropertyName == nameof(TrailViewModel.Rows) && !ReferenceEquals(TrailList.ItemsSource, TrailView.Rows))
        {
            UpdateTrailRows();
        }
    }

    private async void TrailList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrailRow row)
        {
            await GoToTrailRowAsync(row.Index);
        }
    }

    private async void TrailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingTrailSelection)
        {
            return;
        }
        if (TrailList.SelectedItem is TrailRow row)
        {
            await GoToTrailRowAsync(row.Index);
        }
        if (TrailList.SelectedIndex != _trail.CurrentIndex)
        {
            // Stayed (an editor with changes): put the selection back.
            UpdateTrailRows();
        }
    }

    private async void BackToListButton_Click(object sender, RoutedEventArgs e) => await CloseTrailAsync();

    /// <summary>Draws a trail row's connecting line over the height of its part of the row.</summary>
    private void TrailLine_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Canvas canvas && canvas.Children.Count == 1 && canvas.Children[0] is Line line)
        {
            line.Y2 = e.NewSize.Height;
        }
    }

    /// <summary>The list column's content slides in when the list or the trail is shown in it.</summary>
    private static void SetSlideIn(UIElement element)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Target = "Translation";
        offset.InsertKeyFrame(0, new System.Numerics.Vector3(0, 24, 0));
        offset.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
        offset.Duration = TimeSpan.FromMilliseconds(200);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(200);
        var group = compositor.CreateAnimationGroup();
        group.Add(offset);
        group.Add(fade);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        ElementCompositionPreview.SetImplicitShowAnimation(element, group);
    }

    public async Task<bool> TryHandleBackAsync()
    {
        if (_trail.IsOpen)
        {
            if (!await CanLeaveAsync())
            {
                return true;
            }
            if (DetailFrame.Content is not IRecordPage && DetailFrame.CanGoBack)
            {
                // An editor opened from a record in the trail: back to that record.
                DetailFrame.GoBack();
                UpdatePanes();
                return true;
            }
            if (_trail.Back() == TrailBackResult.Close)
            {
                ShowTrailFirst();
            }
            else
            {
                NavigateDetail(_trail.Current!, Slide(forward: false));
            }
            return true;
        }

        if (!_isNarrow || !IsDetailShown)
        {
            return false;
        }
        if (!await CanLeaveAsync())
        {
            return true;
        }

        // Narrow window: back from an editor returns to the record, back from a record returns to the list.
        if (DetailFrame.Content is not IRecordPage && DetailFrame.CanGoBack)
        {
            DetailFrame.GoBack();
            UpdatePanes();
        }
        else
        {
            ShowEmptyDetail();
        }
        return true;
    }

    // ---------------------------------------------------------------- Layout

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wasNarrow = _isNarrow;
        _isNarrow = e.NewSize.Width < NarrowWidth;
        UpdatePanes();
        if (wasNarrow != _isNarrow)
        {
            if (!_isNarrow && !IsDetailShown && _selectedId != null)
            {
                // The list was shown alone with a record still selected (a trail closed): show it again.
                ShowDetails(_selectedId);
            }
            NarrowChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdatePanes()
    {
        UpdatePaneLayout();
        Navigator.NotifyBackStateChanged();
    }

    private void UpdatePaneLayout()
    {
        if (!_isNarrow)
        {
            // While a trail is open, the trail column takes the list's place, at the same width.
            ListColumn.Width = new GridLength(ActualWidth >= 1100 ? 380 : 320);
            ListPane.Visibility = _trail.IsOpen ? Visibility.Collapsed : Visibility.Visible;
            TrailPane.Visibility = _trail.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            TrailCrumbs.Visibility = Visibility.Collapsed;
            Divider.Visibility = Visibility.Visible;
            DetailFrame.Visibility = Visibility.Visible;
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            return;
        }

        // One pane at a time.
        TrailPane.Visibility = Visibility.Collapsed;
        var showDetail = IsDetailShown;
        TrailCrumbs.Visibility = showDetail && _trail.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        var star = new GridLength(1, GridUnitType.Star);
        ListPane.Visibility = showDetail ? Visibility.Collapsed : Visibility.Visible;
        DetailFrame.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = Visibility.Collapsed;
        ListColumn.Width = showDetail ? new GridLength(0) : star;
        DetailColumn.Width = showDetail ? star : new GridLength(0);
    }
}
