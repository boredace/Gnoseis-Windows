/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using Gnoseis.App.Views;
using Gnoseis.Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

    // Every record the title bar suggestions search, loaded on first use and again after data changes.
    private Task<SearchIndex>? _suggestionIndex;

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

        DatabaseChooser.CanOpenChanged += DatabaseChooser_CanOpenChanged;
        DatabaseChooser.OpenRequested += DatabaseChooser_OpenRequested;
        Closed += MainWindow_Closed;
        Navigator.BackStateChanged += (_, _) => AppTitleBar.IsBackButtonEnabled = Navigator.CanGoBackAnywhere;
    }

    // Reloads the pages when Windows' time zone changes, so note times move to it.
    private TimeZoneWatcher? _timeZoneWatcher;

    /// <summary>
    /// Finds the data folder (a bad argument quits, the picker asks, a folder with no database asks before
    /// one is made there), reads its settings, opens the database and shows the page the user was on last time.
    /// </summary>
    public async Task StartAsync()
    {
        await _loaded.Task;
        NavView.IsEnabled = false;
        SearchBox.IsEnabled = false;

        var location = await ChooseDataFolderAsync();
        if (location == null)
        {
            Application.Current.Exit();
            return;
        }
        AppPaths.SetLocation(location);

        // A folder opened from the command line shows in the picker and Settings next time.
        if (location.Source == DataFolderSource.CommandLine)
        {
            AddToKnownDatabases(location.Folder);
        }

        // settings.json belongs to this database: until now the window had the default placement and theme.
        App.LoadSettings();
        RestoreWindowPlacement();
        ApplyTheme(App.Settings.ElementTheme);

        // Plain copies left by a backup or restore that didn't finish.
        await Task.Run(() => BackupRepository.DeleteTemporaryFiles(AppPaths.DataFolder));

        while (true)
        {
            try
            {
                var database = await Task.Run(() => GnoseisDatabase.Open(AppPaths.DatabaseFile));
                App.Container = new AppContainer(database);
                database.DataChanged += (_, _) => _suggestionIndex = null;
                _timeZoneWatcher ??= new TimeZoneWatcher(this, () => App.Container.Database.NotifyTimeZoneChanged());
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
        ShowDatabaseName();
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

    /// <summary>
    /// The data folder to use, or null to quit: the argument error first, then the order (with the picker
    /// when asked for), then the "no database here" question and the write checks. Nothing is written in
    /// any data folder, or in databases.json, before the user has agreed to the folder.
    /// </summary>
    private static async Task<DataLocation?> ChooseDataFolderAsync()
    {
        if (AppPaths.Argument.IsError)
        {
            await Dialogs.ShowQuitMessageAsync("Gnoseis can't start",
                $"{AppPaths.Argument.Error}\n\nTo start Gnoseis on a data folder, use:\n\nGnoseis.exe {DataLocation.DataFolderOption} \"<folder>\"");
            return null;
        }

        var location = AppPaths.ResolveAtStartup();
        if (location == null)
        {
            location = await PickDatabaseAsync();
            if (location == null)
            {
                return null;
            }
        }
        else if (location.AsksBeforeCreating(AppPaths.DefaultDataFolder) && !DataLocation.HasDatabase(location.Folder)
                 && !await AskToCreateAsync(location.Folder, fromPicker: false))
        {
            return null;
        }

        if (location.IsPortable)
        {
            return await CanUsePortableFolderAsync(location.Folder) ? location : null;
        }
        if (location.Source is DataFolderSource.CommandLine or DataFolderSource.Picker or DataFolderSource.DefaultEntry)
        {
            return await CanUseChosenFolderAsync(location) ? location : null;
        }
        return location;
    }

    /// <summary>The start-up picker, until a database is opened (null: Quit).</summary>
    private static async Task<DataLocation?> PickDatabaseAsync()
    {
        var databases = AppPaths.LoadDatabases();
        while (true)
        {
            var dialog = new DatabasePickerDialog(databases);
            await Dialogs.ShowCustomAsync(dialog);
            if (dialog.Chosen == null)
            {
                return null;
            }
            var location = new DataLocation(dialog.Chosen.Folder, IsPortable: false, DataFolderSource.Picker);
            if (!location.AsksBeforeCreating(AppPaths.DefaultDataFolder) || DataLocation.HasDatabase(location.Folder)
                || await AskToCreateAsync(location.Folder, fromPicker: true))
            {
                return location;
            }
        }
    }

    /// <summary>A typo or a disconnected drive never makes a new database silently. From the picker, Back returns to it.</summary>
    private static Task<bool> AskToCreateAsync(string folder, bool fromPicker) =>
        Dialogs.ConfirmAsync("No database here",
            $"There's no Gnoseis database in this folder: {folder}. Start with a new, empty database there?",
            "Create", fromPicker ? "Back" : "Quit");

    private static void AddToKnownDatabases(string folder)
    {
        var databases = AppPaths.LoadDatabases();
        // An unreadable list is only rewritten when the user changes it in Settings.
        if (databases.CouldNotBeRead || !databases.AddIfMissing(folder))
        {
            return;
        }
        try
        {
            databases.Save(AppPaths.DatabasesFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The list is a convenience; the folder is open either way.
        }
    }

    /// <summary>In portable mode the data stays beside the app or nowhere: never silently somewhere else.</summary>
    private static async Task<bool> CanUsePortableFolderAsync(string folder)
    {
        while (!DataLocation.CanWrite(folder, out var error))
        {
            var message =
                $"This copy of Gnoseis is portable, so it keeps its data in this folder, which can't be written to:\n\n{folder}\n\n{error}\n\n" +
                $"Move Gnoseis to a folder you can write to, or delete the file named \"{DataLocation.PortableMarkerName}\" next to Gnoseis.exe to keep the data in your user folder.";
            if (!await Dialogs.ConfirmAsync("Can't write to the data folder", message, "Try again", "Quit"))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>A folder from the command line or the list that can't be written: try again or quit, never somewhere else.</summary>
    private static async Task<bool> CanUseChosenFolderAsync(DataLocation location)
    {
        while (!DataLocation.CanWrite(location.Folder, out var error))
        {
            var from = location.Source == DataFolderSource.CommandLine
                ? $"Gnoseis was started with {DataLocation.DataFolderOption} on this folder, which can't be written to:"
                : "This database's folder, from your list of databases, can't be written to:";
            var message =
                $"{from}\n\n{location.Folder}\n\n{error}\n\n" +
                "Check that the drive is connected and that you can write to the folder, then try again.";
            if (!await Dialogs.ConfirmAsync("Can't write to the data folder", message, "Try again", "Quit"))
            {
                return false;
            }
        }
        return true;
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

    // ---------------------------------------------------------------- Database in the title bar

    /// <summary>Shows "Database: name" in the title bar (not in a portable copy, which has no list).</summary>
    public void ShowDatabaseName()
    {
        if (!AppPaths.IsResolved || AppPaths.IsPortable)
        {
            DatabaseLink.Visibility = Visibility.Collapsed;
            return;
        }
        var name = AppPaths.LoadDatabases().Find(AppPaths.DataFolder)?.Name ?? KnownDatabases.NameFor(AppPaths.DataFolder);
        DatabaseLinkText.Text = $"Database: {name}";
        AutomationProperties.SetName(DatabaseLink, $"Database: {name}. Choose another database");
        DatabaseLink.Visibility = Visibility.Visible;
    }

    private void DatabaseLink_Click(object sender, RoutedEventArgs e)
    {
        DatabaseChooser.Show(AppPaths.LoadDatabases(), AppPaths.DataFolder);
        FlyoutBase.ShowAttachedFlyout(DatabaseLink);
        DatabaseChooser.FocusList();
    }

    private void DatabaseChooser_CanOpenChanged(object? sender, EventArgs e) =>
        DatabaseOpenButton.IsEnabled = DatabaseChooser.Openable != null;

    private async void DatabaseAddFolder_Click(object sender, RoutedEventArgs e)
    {
        await DatabaseChooser.AddFolderAsync();
        // The folder picker takes the focus, which closes the flyout.
        if (!DatabaseFlyout.IsOpen)
        {
            FlyoutBase.ShowAttachedFlyout(DatabaseLink);
        }
    }

    private async void DatabaseOpen_Click(object sender, RoutedEventArgs e) => await RestartWithChosenDatabaseAsync();

    private async void DatabaseChooser_OpenRequested(object? sender, EventArgs e) => await RestartWithChosenDatabaseAsync();

    /// <summary>
    /// Gnoseis opens one database per run: after unsaved edits are dealt with and the user agrees, it starts
    /// a new Gnoseis on the chosen folder (--data-folder) and closes this one.
    /// </summary>
    private async Task RestartWithChosenDatabaseAsync()
    {
        if (DatabaseChooser.Openable is not { } database)
        {
            return;
        }
        DatabaseFlyout.Hide();
        if (!await Navigator.CanLeaveAsync())
        {
            return;
        }
        if (!await Dialogs.ConfirmAsync("Restart Gnoseis?",
                $"Gnoseis will close and start again with \"{database.Name}\":\n\n{database.Folder}",
                "Restart", defaultToPrimary: true))
        {
            return;
        }

        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        // ArgumentList quotes the folder so that one ending in a backslash (a drive) arrives whole.
        start.ArgumentList.Add(DataLocation.DataFolderOption);
        start.ArgumentList.Add(database.Folder);
        try
        {
            Process.Start(start);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await Dialogs.ShowMessageAsync("Couldn't restart Gnoseis", e.Message);
            return;
        }
        // Saves this database's settings and releases its file.
        Close();
    }

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

    private async void ForwardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await Navigator.GoForwardAsync();
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
        // Mouse "back" and "forward" buttons.
        var properties = e.GetCurrentPoint(RootGrid).Properties;
        if (properties.IsXButton1Pressed)
        {
            e.Handled = true;
            await Navigator.GoBackAsync();
        }
        else if (properties.IsXButton2Pressed)
        {
            e.Handled = true;
            await Navigator.GoForwardAsync();
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

    /// <summary>Ctrl+E and F3: search everything.</summary>
    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FocusSearchEverything();
    }

    /// <summary>Ctrl+F: find on this screen, else search everything.</summary>
    private void FindAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ContentFrame.Content is SectionPage section && section.FocusFindBox())
        {
            return;
        }
        FocusSearchEverything();
    }

    // ---------------------------------------------------------------- Panes

    /// <summary>
    /// F6 and Shift+F6: the search box, the navigation pane, the list (or trail column) and the
    /// record, in that order, skipping those not showing and wrapping around.
    /// </summary>
    private void PaneAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!NavView.IsEnabled)
        {
            return;
        }

        var section = ContentFrame.Content as SectionPage;
        var record = section != null ? section.FirstInRecord() : FocusManager.FindFirstFocusableElement(ContentFrame) as Control;
        var showing = new List<Pane>();
        if (SearchBox.IsEnabled)
        {
            showing.Add(Pane.Search);
        }
        if (NavigationTarget() != null)
        {
            showing.Add(Pane.Navigation);
        }
        if (section?.IsListShowing == true)
        {
            showing.Add(Pane.List);
        }
        if (record != null)
        {
            showing.Add(Pane.Record);
        }

        var focused = KeyboardFocus.Focused(RootGrid.XamlRoot);
        Pane? current =
            KeyboardFocus.IsInside(focused, SearchBox) ? Pane.Search
            : KeyboardFocus.IsInside(focused, ContentFrame) ? section?.PaneOf(focused) ?? Pane.Record
            : KeyboardFocus.IsInside(focused, NavView) ? Pane.Navigation
            : null;

        switch (PaneCycle.Next(current, sender.Modifiers == VirtualKeyModifiers.Shift, showing))
        {
            case Pane.Search:
                SearchBox.Focus(FocusState.Keyboard);
                break;
            case Pane.Navigation:
                NavigationTarget()?.Focus(FocusState.Keyboard);
                break;
            case Pane.List:
                section!.FocusList();
                break;
            case Pane.Record:
                record!.Focus(FocusState.Keyboard);
                break;
        }
    }

    /// <summary>The navigation pane's selected item, else its first; null while the pane is hidden.</summary>
    private Control? NavigationTarget()
    {
        if (NavView.DisplayMode == NavigationViewDisplayMode.Minimal && !NavView.IsPaneOpen)
        {
            return null;
        }
        if (NavView.SelectedItem is NavigationViewItem { Visibility: Visibility.Visible } selected)
        {
            return selected;
        }
        return NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
    }

    /// <summary>The title bar search box, or the search page's own box while it's shown.</summary>
    private void FocusSearchEverything()
    {
        if (ContentFrame.Content is SearchPage search)
        {
            search.FocusSearchBox();
        }
        else if (SearchBox.IsEnabled)
        {
            KeyboardFocus.FocusAndSelectAll(SearchBox);
        }
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

        SearchIndex index;
        try
        {
            index = await (_suggestionIndex ??= LoadSuggestionIndexAsync());
        }
        catch (Exception)
        {
            // Try again on the next keystroke.
            _suggestionIndex = null;
            if (version == _searchVersion)
            {
                sender.ItemsSource = null;
            }
            return;
        }
        if (version == _searchVersion)
        {
            sender.ItemsSource = index.Find(query).Take(SearchLogic.SuggestionLimit).Select(r => SearchRow.For(r, query)).ToList();
        }
    }

    private static async Task<SearchIndex> LoadSuggestionIndexAsync()
    {
        var all = await App.Container.Search.GetAllAsync();
        return await Task.Run(() => new SearchIndex(all));
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SearchRow row)
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
