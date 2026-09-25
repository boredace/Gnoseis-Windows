/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gnoseis.App.Views;

/// <summary>Home: quick note, counts, recent notes and recently opened records.</summary>
public sealed partial class HomePage : Page
{
    private RecordRef? _lastAdded;

    public HomePage()
    {
        InitializeComponent();
        var open = new Button { Content = "Open" };
        open.Click += async (_, _) =>
        {
            if (_lastAdded != null)
            {
                await Navigator.OpenRecordAsync(_lastAdded);
            }
        };
        QuickNoteInfo.ActionButton = open;
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

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Two lists side by side when there is room.
        var twoColumns = e.NewSize.Width >= 900;
        SecondListColumn.Width = twoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(RecentlyOpenedCard, twoColumns ? 1 : 0);
        Grid.SetRow(RecentlyOpenedCard, twoColumns ? 0 : 1);
        ListsGrid.ColumnSpacing = twoColumns ? 16 : 0;
    }

    private async void AddQuickNote_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _lastAdded = await ViewModel.AddQuickNoteAsync();
            if (_lastAdded != null)
            {
                QuickNoteInfo.IsOpen = true;
                QuickTitleBox.Focus(FocusState.Programmatic);
            }
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't add the note", ex);
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
        if ((sender as FrameworkElement)?.DataContext is RecordRow row)
        {
            await Navigator.OpenRecordAsync(row.ToRef());
        }
    }

    private async void AllNotes_Click(object sender, RoutedEventArgs e) =>
        await Navigator.ShowSectionAsync(RecordType.Note);
}
