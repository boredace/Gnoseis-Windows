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
        if (e.Key == VirtualKey.Delete && DetailFrame.Content is IRecordPage details)
        {
            e.Handled = true;
            await details.DeleteWithConfirmationAsync();
        }
        else if (e.Key == VirtualKey.Enter && DetailFrame.Content is IRecordPage)
        {
            e.Handled = true;
            DetailFrame.Focus(FocusState.Keyboard);
        }
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
        NavigateDetail(record, new EntranceNavigationTransitionInfo());
    }

    private void NavigateDetail(RecordRef record, NavigationTransitionInfo transition)
    {
        DetailFrame.Navigate(PageFor(record.Type), record, transition);
        DetailFrame.BackStack.Clear();
        UpdatePanes();
        TrailChanged?.Invoke(this, EventArgs.Empty);
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
    }

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
        _isNarrow = e.NewSize.Width < NarrowWidth;
        UpdatePanes();
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
            Divider.Visibility = Visibility.Visible;
            DetailFrame.Visibility = Visibility.Visible;
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            return;
        }

        // One pane at a time.
        TrailPane.Visibility = Visibility.Collapsed;
        var showDetail = IsDetailShown;
        var star = new GridLength(1, GridUnitType.Star);
        ListPane.Visibility = showDetail ? Visibility.Collapsed : Visibility.Visible;
        DetailFrame.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = Visibility.Collapsed;
        ListColumn.Width = showDetail ? new GridLength(0) : star;
        DetailColumn.Width = showDetail ? star : new GridLength(0);
    }
}
