/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gnoseis.App.Views;

/// <summary>Search results across all record types.</summary>
public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            QueryBox.Focus(FocusState.Programmatic);
            QueryBox.SelectionStart = QueryBox.Text.Length;
        };
    }

    public SearchViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = new SearchViewModel();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Activate();
        ViewModel.Query = e.Parameter as string ?? "";
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Deactivate();
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.Groups))
        {
            GroupsSource.Source = ViewModel.Groups;
            ResultList.ItemsSource = GroupsSource.View;
        }
    }

    private async void ResultList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecordRow row)
        {
            await Navigator.OpenRecordAsync(row.ToRef());
        }
    }
}
