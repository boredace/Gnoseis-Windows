/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Gnoseis.App.Views;

/// <summary>
/// The start-up picker ("Always ask for a database at start-up"): shown before any database is opened.
/// Open (or double-click, or Enter) opens the selected database without changing the default; Add folder
/// adds a folder to the list and selects it; Quit leaves <see cref="Chosen"/> null.
/// </summary>
public sealed partial class DatabasePickerDialog : ContentDialog
{
    private readonly KnownDatabases _databases;

    public DatabasePickerDialog(KnownDatabases databases)
    {
        _databases = databases;
        InitializeComponent();
        ShowRows(select: null);
        Opened += (_, _) => DatabaseList.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    /// <summary>The database to open; null when the user quit.</summary>
    public KnownDatabase? Chosen { get; private set; }

    private DatabaseRow? Selected => DatabaseList.SelectedItem as DatabaseRow;

    private void ShowRows(string? select)
    {
        var rows = DatabaseRow.From(_databases, folderInUse: null);
        DatabaseList.ItemsSource = rows;
        DatabaseList.SelectedItem =
            (select != null ? rows.FirstOrDefault(r => KnownDatabases.SameFolder(r.Folder, select)) : null)
            ?? rows.FirstOrDefault(r => r.IsDefault)
            ?? rows.FirstOrDefault();
        if (DatabaseList.SelectedItem != null)
        {
            DatabaseList.ScrollIntoView(DatabaseList.SelectedItem);
        }
        UpdateOpenButton();
    }

    private void UpdateOpenButton() => IsPrimaryButtonEnabled = Selected is { IsMissing: false };

    private void DatabaseList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOpenButton();

    private void DatabasePickerDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (Selected is not { IsMissing: false } row)
        {
            args.Cancel = true;
            return;
        }
        Chosen = row.Entry;
    }

    private void OpenSelected()
    {
        if (Selected is { IsMissing: false } row)
        {
            Chosen = row.Entry;
            Hide();
        }
    }

    private void DatabaseList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => OpenSelected();

    private void DatabaseList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            OpenSelected();
        }
    }

    private async void DatabasePickerDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Add folder keeps the picker open.
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try
        {
            var folder = await DatabaseRow.PickFolderAsync();
            if (folder == null)
            {
                return;
            }
            ErrorBar.IsOpen = false;
            if (_databases.AddIfMissing(folder))
            {
                try
                {
                    _databases.Save(AppPaths.DatabasesFile);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Still listed for this start; it will be saved with the next change.
                    ErrorBar.Title = "Couldn't save the list of databases";
                    ErrorBar.Message = e.Message;
                    ErrorBar.IsOpen = true;
                }
            }
            ShowRows(select: folder);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
