/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Gnoseis.App.Views;

/// <summary>
/// The list of databases to choose from, in the start-up picker and the title bar's database flyout:
/// the default selected (or the one asked for), a database whose folder isn't there can't be opened,
/// double-click or Enter opens. Add folder adds a folder to the list and selects it.
/// </summary>
public sealed partial class DatabaseChooser : UserControl
{
    private KnownDatabases? _databases;
    private string? _folderInUse;

    public DatabaseChooser()
    {
        InitializeComponent();
        // The row (ListViewItem) marks Enter handled before the list sees it, so listen for handled keys too.
        DatabaseList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(DatabaseList_KeyDown), handledEventsToo: true);
    }

    /// <summary>The selected database can be opened (or not any more).</summary>
    public event EventHandler? CanOpenChanged;

    /// <summary>The user double-clicked or pressed Enter on a database that can be opened.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>The selected database, when it can be opened: its folder is there, and it isn't the one in use.</summary>
    public KnownDatabase? Openable => DatabaseList.SelectedItem is DatabaseRow { IsMissing: false, IsInUse: false } row ? row.Entry : null;

    /// <summary>Shows the list; the folder in use (if any) is tagged and can't be opened again.</summary>
    public void Show(KnownDatabases databases, string? folderInUse, string? select = null)
    {
        _databases = databases;
        _folderInUse = folderInUse;
        ErrorBar.IsOpen = false;
        var rows = DatabaseRow.From(databases, folderInUse);
        DatabaseList.ItemsSource = rows;
        DatabaseList.SelectedItem =
            (select != null ? rows.FirstOrDefault(r => KnownDatabases.SameFolder(r.Folder, select)) : null)
            ?? rows.FirstOrDefault(r => r.IsDefault)
            ?? rows.FirstOrDefault();
        if (DatabaseList.SelectedItem != null)
        {
            DatabaseList.ScrollIntoView(DatabaseList.SelectedItem);
        }
        CanOpenChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FocusList() => DatabaseList.Focus(FocusState.Programmatic);

    /// <summary>Asks for a folder, adds it to the list (saved at once) and selects it.</summary>
    public async Task AddFolderAsync()
    {
        if (_databases == null)
        {
            return;
        }
        var folder = await DatabaseRow.PickFolderAsync();
        if (folder == null)
        {
            return;
        }
        string? error = null;
        if (_databases.AddIfMissing(folder))
        {
            try
            {
                _databases.Save(AppPaths.DatabasesFile);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still listed for now; it's saved with the next change.
                error = e.Message;
            }
        }
        Show(_databases, _folderInUse, select: folder);
        if (error != null)
        {
            ErrorBar.Title = "Couldn't save the list of databases";
            ErrorBar.Message = error;
            ErrorBar.IsOpen = true;
        }
    }

    private void DatabaseList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        CanOpenChanged?.Invoke(this, EventArgs.Empty);

    private void DatabaseList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Openable != null)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DatabaseList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (Openable != null)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
