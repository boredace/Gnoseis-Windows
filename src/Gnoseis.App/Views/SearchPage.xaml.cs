/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.App.Controls;
using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gnoseis.App.Views;

/// <summary>
/// Search across all record types. The page is kept when the user leaves it, so Back returns to the
/// same query, chips and scroll position; a new search (title bar, a section's filter box) starts over.
/// </summary>
public sealed partial class SearchPage : Page
{
    private bool _changedWhileAway;
    private bool _focusOnLoaded;

    public SearchPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_focusOnLoaded)
            {
                _focusOnLoaded = false;
                Search.FocusSearchBox();
            }
        };
    }

    public RecordSearchModel? ViewModel { get; private set; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Container.Database.DataChanged -= Database_DataChanged;

        if (e.NavigationMode == NavigationMode.Back && ViewModel is { } kept)
        {
            kept.Activate();
            if (_changedWhileAway)
            {
                await kept.LoadAsync();
            }
            return;
        }

        var args = e.Parameter as SearchArgs ?? new SearchArgs(e.Parameter as string ?? "");
        var model = new RecordSearchModel(SearchLogic.ChipTypes(), args.Scope, CreateFromSearch.TypesFor(null));
        ViewModel = model;
        model.Query = args.Query;
        Search.Model = model;
        model.Activate();
        _focusOnLoaded = true;
        await model.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel?.Deactivate();
        // Reload on Back only when something changed meanwhile, so the list keeps its scroll position.
        _changedWhileAway = false;
        App.Container.Database.DataChanged += Database_DataChanged;
    }

    private void Database_DataChanged(object? sender, DataChangedEventArgs e) => _changedWhileAway = true;

    /// <summary>Opens the editor for a new record of the type, named after the query, in the type's section.</summary>
    private async void Search_CreateRequested(object? sender, CreateFromSearchRequest request) =>
        await Navigator.ShowEditorAsync(new EditRequest(request.Type, PrefillName: request.Query));

    private async void Search_RecordInvoked(object? sender, SearchRow row) =>
        await Navigator.OpenRecordAsync(row.ToRef());
}
