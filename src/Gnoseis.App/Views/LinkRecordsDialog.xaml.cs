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

namespace Gnoseis.App.Views;

/// <summary>Picks existing records to link to a record (the Android Link Records page).</summary>
public sealed partial class LinkRecordsDialog : ContentDialog
{
    // Set while the list is refilled, so programmatic selection changes are not treated as user input.
    private bool _updatingList;

    private LinkRecordsDialog(LinkRecordsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Opened += (_, _) => QueryBox.Focus(FocusState.Programmatic);
    }

    public LinkRecordsViewModel ViewModel { get; }

    /// <summary>Shows the picker for a record and links what the user picks.</summary>
    public static async Task ShowAsync(RecordRef source)
    {
        var viewModel = new LinkRecordsViewModel(source);
        var dialog = new LinkRecordsDialog(viewModel);
        await viewModel.LoadAsync();
        await Dialogs.ShowCustomAsync(dialog);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LinkRecordsViewModel.Groups))
        {
            return;
        }

        // Refill the list and restore the selection, which survives changes to the search text.
        _updatingList = true;
        try
        {
            GroupsSource.Source = ViewModel.Groups;
            ResultList.ItemsSource = GroupsSource.View;
            foreach (var row in ViewModel.Groups.SelectMany(g => g).Where(r => ViewModel.IsSelected(r.Id)))
            {
                ResultList.SelectedItems.Add(row);
            }
        }
        finally
        {
            _updatingList = false;
        }
    }

    private void ResultList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // Records that are already linked are shown but cannot be picked.
        if (args.Item is RecordRow row)
        {
            args.ItemContainer.IsEnabled = !row.IsLinked;
        }
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingList)
        {
            return;
        }
        foreach (var row in e.AddedItems.OfType<RecordRow>())
        {
            ViewModel.SetSelected(row, true);
        }
        foreach (var row in e.RemovedItems.OfType<RecordRow>())
        {
            ViewModel.SetSelected(row, false);
        }
    }

    private async void LinkRecordsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.LinkSelectedAsync();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            StatusFallback(ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void StatusFallback(string message) => Title = $"Couldn't link: {message}";
}
