/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using Gnoseis.App.Views;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using Windows.System;

namespace Gnoseis.App;

/// <summary>The app shell: title bar with search, navigation pane and the page frame.</summary>
public sealed partial class MainWindow : Window
{
    private readonly TaskCompletionSource _loaded = new();
    private RectInt32? _normalBounds;
    private int _searchVersion;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Gnoseis.ico"));
        }
        catch (Exception)
        {
            // The icon is cosmetic; the window still works without it.
        }

        RestoreWindowPlacement();
        AppWindow.Changed += AppWindow_Changed;

        RootGrid.RequestedTheme = App.Settings.ElementTheme;
        RootGrid.Loaded += (_, _) =>
        {
            Dialogs.XamlRoot = RootGrid.XamlRoot;
            _loaded.TrySetResult();
        };

        Closed += MainWindow_Closed;
        Navigator.BackStateChanged += (_, _) => AppTitleBar.IsBackButtonEnabled = Navigator.CanGoBackAnywhere;
    }

    /// <summary>Opens the database and shows the page the user was on last time.</summary>
    public async Task StartAsync()
    {
        await _loaded.Task;
        NavView.IsEnabled = false;
        SearchBox.IsEnabled = false;

        while (true)
        {
            try
            {
                var database = await Task.Run(() => GnoseisDatabase.Open(AppPaths.DatabaseFile));
                App.Container = new AppContainer(database);
                break;
            }
            catch (Exception e) when (e is DatabaseIncompatibleException or SqliteException or IOException or UnauthorizedAccessException)
            {
                if (!await AskHowToContinueAsync(e))
                {
                    Application.Current.Exit();
                    return;
                }
            }
        }

        NavView.IsEnabled = true;
        SearchBox.IsEnabled = true;
        if (NavView.DisplayMode == NavigationViewDisplayMode.Expanded)
        {
            NavView.IsPaneOpen = App.Settings.IsPaneOpen;
        }

        Navigator.Initialize(ContentFrame);
        if (Enum.TryParse<RecordType>(App.Settings.LastSection, out var section) && RecordTypeExtensions.UserTypes.Contains(section))
        {
            Navigator.ResetTo(typeof(SectionPage), new SectionArgs(section));
        }
        else
        {
            Navigator.ResetToHome();
        }
    }

    /// <summary>Lets the user choose what to do when the database file cannot be opened. Nothing is deleted.</summary>
    private async Task<bool> AskHowToContinueAsync(Exception error)
    {
        while (true)
        {
            var message =
                $"The Gnoseis database could not be opened.\n\n{error.Message}\n\n" +
                $"File: {AppPaths.DatabaseFile}\n\n" +
                "You can start with a new, empty database. The current file is kept and renamed so it can be recovered later.";
            var choice = await Dialogs.AskAsync("Can't open the database", message,
                "Start with a new database", "Open data folder", "Exit");

            switch (choice)
            {
                case ContentDialogResult.Primary:
                    try
                    {
                        GnoseisDatabase.SetAside(AppPaths.DatabaseFile);
                        return true;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        error = e;
                        continue;
                    }
                case ContentDialogResult.Secondary:
                    OpenDataFolder();
                    continue;
                default:
                    return false;
            }
        }
    }

    public static void OpenDataFolder()
    {
        Directory.CreateDirectory(AppPaths.DataFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataFolder}\"") { UseShellExecute = true });
    }

    /// <summary>Applies the theme chosen in Settings.</summary>
    public void ApplyTheme(ElementTheme theme) => RootGrid.RequestedTheme = theme;

    // ---------------------------------------------------------------- Window placement

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void RestoreWindowPlacement()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            presenter.PreferredMinimumWidth = (int)(520 * scale);
            presenter.PreferredMinimumHeight = (int)(420 * scale);

            var saved = App.Settings.Window;
            if (saved != null && IsOnScreen(saved))
            {
                AppWindow.MoveAndResize(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height));
                _normalBounds = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
                if (saved.IsMaximized)
                {
                    presenter.Maximize();
                }
            }
            else
            {
                // First start: a comfortable size, centered on the screen.
                var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                var width = Math.Min((int)(1360 * scale), area.Width);
                var height = Math.Min((int)(880 * scale), area.Height);
                AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
            }
        }
    }

    private static bool IsOnScreen(WindowPlacement placement)
    {
        var area = DisplayArea.GetFromRect(new RectInt32(placement.X, placement.Y, placement.Width, placement.Height), DisplayAreaFallback.None);
        return area != null && placement.Width >= 200 && placement.Height >= 200;
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Remember the size and position of the window when it is not maximized or minimized.
        if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _normalBounds = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_normalBounds is { } bounds)
        {
            var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            App.Settings.Window = new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized);
        }
        App.Settings.Save();

        // Close pooled connections so the database file is complete and unlocked after exit.
        GnoseisDatabase.ReleaseAllConnections();
    }

    // ---------------------------------------------------------------- Navigation

    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            await Navigator.ShowSettingsAsync();
        }
        else if (args.InvokedItemContainer?.Tag is string tag)
        {
            await ShowSectionByTagAsync(tag);
        }
    }

    private static Task ShowSectionByTagAsync(string tag) =>
        Enum.TryParse<RecordType>(tag, out var type) ? Navigator.ShowSectionAsync(type) : Navigator.ShowHomeAsync();

    private void NavView_PaneChanged(NavigationView sender, object args)
    {
        if (sender.DisplayMode == NavigationViewDisplayMode.Expanded)
        {
            App.Settings.IsPaneOpen = sender.IsPaneOpen;
        }
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private async void AppTitleBar_BackRequested(TitleBar sender, object args) => await Navigator.GoBackAsync();

    private async void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await Navigator.GoBackAsync();
    }

    private async void SectionAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!NavView.IsEnabled)
        {
            return;
        }
        var tags = NavView.MenuItems.OfType<NavigationViewItem>().Select(i => (string)i.Tag).ToList();
        var index = sender.Key - VirtualKey.Number1;
        if (index >= 0 && index < tags.Count)
        {
            await ShowSectionByTagAsync(tags[index]);
        }
    }

    private async void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Mouse "back" button.
        if (e.GetCurrentPoint(RootGrid).Properties.IsXButton1Pressed)
        {
            e.Handled = true;
            await Navigator.GoBackAsync();
        }
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        Navigator.CurrentParameter = e.Parameter;
        AppTitleBar.IsBackButtonEnabled = Navigator.CanGoBackAnywhere;

        // Highlight the pane item of the page being shown.
        string? tag = e.Parameter switch
        {
            SectionArgs args => args.Type.ToString(),
            _ when e.SourcePageType == typeof(HomePage) => "Home",
            _ => null,
        };

        if (e.SourcePageType == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
        }
        else
        {
            NavView.SelectedItem = tag == null
                ? null
                : NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag);
        }
    }

    // ---------------------------------------------------------------- Search

    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Keyboard);
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var version = ++_searchVersion;
        var query = sender.Text.Trim();
        if (query.Length < 2)
        {
            sender.ItemsSource = null;
            return;
        }

        var results = await App.Container.Search.SearchAsync(query);
        if (version == _searchVersion)
        {
            sender.ItemsSource = results.Take(10).Select(r => RecordRows.FromSearchResult(r)).ToList();
        }
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is RecordRow row)
        {
            sender.Text = "";
            sender.ItemsSource = null;
            await Navigator.OpenRecordAsync(row.ToRef());
        }
        else if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            sender.ItemsSource = null;
            await Navigator.ShowSearchAsync(args.QueryText.Trim());
        }
    }
}
