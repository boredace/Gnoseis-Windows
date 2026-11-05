/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.ComponentModel;
using Gnoseis.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Views;

/// <summary>Picks existing records to link to a record (the Android Link Records page).</summary>
public sealed partial class LinkRecordsDialog : ContentDialog
{
    // Set while the list is refilled, so programmatic selection changes are not treated as user input.
    private bool _updatingList;

    // The new record to create after the dialog closes, when the user picked a type in the create offer.
    private EditRequest? _createAfterClose;

    private LinkRecordsDialog(LinkRecordsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Opened += (_, _) => QueryBox.Focus(FocusState.Programmatic);
    }

    public LinkRecordsViewModel ViewModel { get; }

    /// <summary>
    /// Shows the picker for a record and links what the user picks. When the user creates a record
    /// from the search instead, the picked records are linked, the dialog closes and the editor opens
    /// for a new record linked to <paramref name="source"/>.
    /// </summary>
    public static async Task ShowAsync(RecordRef source)
    {
        var viewModel = new LinkRecordsViewModel(source);
        var dialog = new LinkRecordsDialog(viewModel);
        await viewModel.LoadAsync();
        await Dialogs.ShowCustomAsync(dialog);
        if (dialog._createAfterClose is { } request)
        {
            await Navigator.ShowEditorAsync(request);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LinkRecordsViewModel.CreateOffer))
        {
            var noResults = CreateFromSearchPanel.ShowOffer(NoResultsCreatePanel, CreatePanel,
                ViewModel.CreateOffer.Query, ViewModel.CreateOffer.Offer, ViewModel.CreateTypes);
            ResultList.Visibility = noResults ? Visibility.Collapsed : Visibility.Visible;
            return;
        }
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

    /// <summary>Links the ticked records, as Link would, then closes so the new record's editor can open.</summary>
    private async void CreatePanel_CreateRequested(object? sender, RecordType type)
    {
        var query = ((CreateFromSearchPanel)sender!).Query;
        try
        {
            await ViewModel.LinkSelectedAsync();
        }
        catch (Exception ex)
        {
            StatusFallback(ex.Message);
            return;
        }
        _createAfterClose = new EditRequest(type, LinkFrom: ViewModel.Source, PrefillName: query);
        Hide();
    }

    private void StatusFallback(string message) => Title = $"Couldn't link: {message}";
}
