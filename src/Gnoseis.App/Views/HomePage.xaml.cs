/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        TypeMenu.Opening += (_, _) => BuildTypeMenu();
        UpdateMostLinked();
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
            case nameof(HomeViewModel.MostLinkedState):
                UpdateMostLinked();
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

    // ---------- Most linked

    /// <summary>The bubbles or an empty state, and the type picker's label.</summary>
    private void UpdateMostLinked()
    {
        var state = ViewModel.MostLinkedState;
        Bubbles.Show(ViewModel.MostLinked, ViewModel.MostLinkedLinks);
        Bubbles.Visibility = state == MostLinkedState.Bubbles ? Visibility.Visible : Visibility.Collapsed;
        NoLinksText.Visibility = state == MostLinkedState.NoLinks ? Visibility.Visible : Visibility.Collapsed;
        TypeEmptyPanel.Visibility = state == MostLinkedState.TypeEmpty ? Visibility.Visible : Visibility.Collapsed;
        TypePicker.Visibility = state == MostLinkedState.NoLinks ? Visibility.Collapsed : Visibility.Visible;
        LegendList.Visibility = state == MostLinkedState.Bubbles && ViewModel.SelectedType == null ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.SelectedType is { } type)
        {
            TypeEmptyTile.Type = type;
        }

        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (ViewModel.SelectedType is { } picked)
        {
            label.Children.Add(new FontIcon { FontSize = 14, Glyph = Glyphs.For(picked), Style = RecordTypeStyles.Glyph(picked) });
            label.Children.Add(new TextBlock { Text = picked.PluralDisplayName() });
        }
        else
        {
            label.Children.Add(new TextBlock { Text = "All types" });
        }
        TypePicker.Content = label;
        AutomationProperties.SetName(TypePicker,
            $"Most linked type: {(ViewModel.SelectedType is { } t ? t.PluralDisplayName() : "All types")}");
    }

    /// <summary>All types, then the types with links, each with its number of links; a check on the current one.</summary>
    private void BuildTypeMenu()
    {
        TypeMenu.Items.Clear();
        TypeMenu.Items.Add(TypeMenuItem(null, "All types", ""));
        TypeMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (var perType in ViewModel.LinksPerType)
        {
            var count = perType.LinkCount == 1 ? "1 link" : $"{perType.LinkCount.ToString("N0", CultureInfo.CurrentCulture)} links";
            TypeMenu.Items.Add(TypeMenuItem(perType.Type, perType.Type.PluralDisplayName(), count));
        }
    }

    private ToggleMenuFlyoutItem TypeMenuItem(RecordType? type, string text, string count)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = text,
            IsChecked = ViewModel.SelectedType == type,
            // Shown right-aligned, where a shortcut would be.
            KeyboardAcceleratorTextOverride = count,
        };
        if (type is { } t)
        {
            // Menus are popups and don't follow the page's theme: brushes for the theme the page shows.
            item.Icon = new FontIcon { Glyph = Glyphs.For(t), Foreground = RecordTypeStyles.Brush(this, t, ColorRole.Outline) };
            AutomationProperties.SetName(item, $"{text}, {count}");
        }
        item.Click += async (_, _) => await ViewModel.SelectTypeAsync(type);
        return item;
    }

    private async void ShowAllTypes_Click(object sender, RoutedEventArgs e) => await ViewModel.SelectTypeAsync(null);

    private async void Bubbles_RecordInvoked(object? sender, MostLinkedRecord record) =>
        await Navigator.OpenRecordAsync(new RecordRef(record.Type, record.Id));

    // ---------- Backup reminder

    /// <summary>The same backup as Settings › Back up database; the reminder updates when it succeeds.</summary>
    private async void BackUpNow_Click(object sender, RoutedEventArgs e)
    {
        var file = await BackupFiles.PickBackupFileAsync();
        if (file == null)
        {
            return;
        }
        try
        {
            await ViewModel.BackUpAsync(file.Path);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't back up the database", ex);
        }
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
