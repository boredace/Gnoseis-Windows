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

/// <summary>
/// Home: greeting and Add new, counts, Most linked, notes per day, unlinked notes, recent notes,
/// recently opened records and the backup reminder.
/// </summary>
public sealed partial class HomePage : Page
{
    /// <summary>Count tiles at least this wide show a chevron.</summary>
    private const double ChevronTileWidth = 170;

    /// <summary>Below this tile width six tiles don't fit a five-digit count: three per row instead.</summary>
    private const double MinTileWidth = 136;

    /// <summary>Below this page width: one column, and three count tiles per row.</summary>
    public const double NarrowWidth = 700;

    public HomePage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public HomeViewModel ViewModel { get; } = new();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Settings.LastSection = "Home";
        ViewModel.Activate();
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Deactivate();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HomeViewModel.CountTiles):
                UpdateChevrons();
                break;
            case nameof(HomeViewModel.HasNoUnlinkedNotes):
                UpdatePair();
                break;
        }
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateChevrons();

    private void CountTiles_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateChevrons();

    /// <summary>
    /// Six count tiles in a row, or three per row on a narrow page or when six would be too narrow;
    /// chevrons on the tiles where they are wide enough.
    /// </summary>
    private void UpdateChevrons()
    {
        var spacing = CountTilesLayout.MinColumnSpacing;
        var width = CountTiles.ActualWidth;
        var columns = ActualWidth < NarrowWidth || (width - 5 * spacing) / 6 < MinTileWidth ? 3 : 6;
        if (CountTilesLayout.MaximumRowsOrColumns != columns)
        {
            CountTilesLayout.MaximumRowsOrColumns = columns;
        }
        var tileWidth = (width - (columns - 1) * spacing) / columns;
        foreach (var card in ViewModel.TypeCounts)
        {
            card.ShowChevron = tileWidth >= ChevronTileWidth;
        }
    }

    /// <summary>The chart and unlinked notes side by side; the chart alone when every note is linked.</summary>
    private void UpdatePair()
    {
        UnlinkedCard.Visibility = ViewModel.HasNoUnlinkedNotes ? Visibility.Collapsed : Visibility.Visible;
        PairSecondColumn.Width = ViewModel.HasNoUnlinkedNotes ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        PairGrid.ColumnSpacing = ViewModel.HasNoUnlinkedNotes ? 0 : 16;
    }

    private async void AddNew_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag && Enum.TryParse<RecordType>(tag, out var type))
        {
            await Navigator.ShowEditorAsync(new EditRequest(type));
        }
    }

    private async void StatCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is StatCard card)
        {
            await Navigator.ShowSectionAsync(card.Type);
        }
    }

    private async void Row_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LinkedRow row)
        {
            await Navigator.OpenRecordAsync(row.ToRef());
        }
    }

    private async void LinkNote_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LinkedRow row)
        {
            try
            {
                await LinkRecordsDialog.ShowAsync(row.ToRef());
            }
            catch (Exception ex)
            {
                await Dialogs.ShowErrorAsync("Couldn't link the note", ex);
            }
        }
    }

    private async void AllNotes_Click(object sender, RoutedEventArgs e) =>
        await Navigator.ShowSectionAsync(RecordType.Note);
}
