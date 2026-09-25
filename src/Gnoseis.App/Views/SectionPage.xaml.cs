/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Gnoseis.App.Views;

/// <summary>
/// A section: the list of one record type with the selected record (or an editor) next to it.
/// In a narrow window only one of the two panes is shown at a time.
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

    public SectionPage()
    {
        InitializeComponent();
    }

    public RecordListViewModel ViewModel { get; private set; } = null!;

    public RecordType Type => ViewModel.Type;

    public bool CanHandleBack => _isNarrow && IsDetailShown;

    private bool IsDetailShown => DetailFrame.Content is not EmptyDetailPage and not null;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var args = e.Parameter as SectionArgs ?? new SectionArgs(RecordType.Note);
        ViewModel = new RecordListViewModel(args.Type);
        ViewModel.GroupsChanged += ViewModel_GroupsChanged;
        ViewModel.Activate();
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
        if (_updatingSelection || RecordList.SelectedItem is not RecordRow row || (row.Id == _selectedId && IsDetailShown && DetailFrame.Content is RecordDetailsPage))
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

    private async void RecordList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && DetailFrame.Content is RecordDetailsPage details)
        {
            e.Handled = true;
            await details.DeleteWithConfirmationAsync();
        }
        else if (e.Key == VirtualKey.Enter && DetailFrame.Content is RecordDetailsPage)
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

    private void ShowDetails(string id)
    {
        _selectedId = id;
        LastSelected[Type] = id;
        DetailFrame.Navigate(typeof(RecordDetailsPage), new RecordRef(Type, id), new EntranceNavigationTransitionInfo());
        DetailFrame.BackStack.Clear();
        UpdatePanes();
    }

    private void ShowEmptyDetail()
    {
        _selectedId = null;
        DetailFrame.Navigate(typeof(EmptyDetailPage), ViewModel, new SuppressNavigationTransitionInfo());
        DetailFrame.BackStack.Clear();
        SetListSelection(null);
        UpdatePanes();
    }

    public async Task ShowRecordAsync(string id)
    {
        if (id == _selectedId && DetailFrame.Content is RecordDetailsPage)
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

        var page = request.Type switch
        {
            RecordType.Note => typeof(NoteEditPage),
            RecordType.Contact => typeof(ContactEditPage),
            _ => typeof(NamedRecordEditPage),
        };

        if (request.IsNew && request.LinkFrom == null)
        {
            // A new record replaces whatever was shown.
            SetListSelection(null);
            _selectedId = null;
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

    public async Task<bool> TryHandleBackAsync()
    {
        if (!_isNarrow || !IsDetailShown)
        {
            return false;
        }
        if (!await CanLeaveAsync())
        {
            return true;
        }

        // Narrow window: back from an editor returns to the record, back from a record returns to the list.
        if (DetailFrame.Content is not RecordDetailsPage && DetailFrame.CanGoBack)
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
            ListColumn.Width = new GridLength(ActualWidth >= 1100 ? 380 : 320);
            ListPane.Visibility = Visibility.Visible;
            Divider.Visibility = Visibility.Visible;
            DetailFrame.Visibility = Visibility.Visible;
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            return;
        }

        // One pane at a time.
        var showDetail = IsDetailShown;
        var star = new GridLength(1, GridUnitType.Star);
        ListPane.Visibility = showDetail ? Visibility.Collapsed : Visibility.Visible;
        DetailFrame.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = Visibility.Collapsed;
        ListColumn.Width = showDetail ? new GridLength(0) : star;
        DetailColumn.Width = showDetail ? star : new GridLength(0);
    }
}
