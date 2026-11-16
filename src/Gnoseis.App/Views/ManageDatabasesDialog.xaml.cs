/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Gnoseis.App.Views;

/// <summary>
/// Settings › Databases › Manage databases: the list, and for the selected database Set as default,
/// Rename and Remove from list (never the default folder; the files stay). Add existing folder and
/// New database add to it. Each change is saved at once through <c>save</c>, which returns an error or null.
/// </summary>
public sealed partial class ManageDatabasesDialog : ContentDialog
{
    private readonly KnownDatabases _databases;
    private readonly Func<string?> _save;
    private string? _newFolder;

    public ManageDatabasesDialog(KnownDatabases databases, Func<string?> save)
    {
        _databases = databases;
        _save = save;
        InitializeComponent();
        ShowRows(select: null);
    }

    private DatabaseRow? Selected => DatabaseList.SelectedItem as DatabaseRow;

    private void ShowRows(string? select)
    {
        var current = select ?? Selected?.Folder;
        var rows = DatabaseRow.From(_databases, AppPaths.DataFolder);
        DatabaseList.ItemsSource = rows;
        DatabaseList.SelectedItem =
            (current != null ? rows.FirstOrDefault(r => KnownDatabases.SameFolder(r.Folder, current)) : null)
            ?? rows.FirstOrDefault(r => r.IsDefault)
            ?? rows.FirstOrDefault();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var row = Selected;
        SetDefaultButton.IsEnabled = row is { IsDefault: false };
        RenameButton.IsEnabled = row != null;
        RemoveButton.IsEnabled = row != null && !_databases.IsDefaultFolder(row.Folder);
    }

    private void DatabaseList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void ShowMessage(InfoBarSeverity severity, string title, string message)
    {
        MessageBar.Severity = severity;
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.IsOpen = true;
    }

    /// <summary>Saves after a change and shows the list again; an error is shown and the change stays for this session.</summary>
    private void Saved(string? select)
    {
        MessageBar.IsOpen = false;
        if (_save() is { } error)
        {
            ShowMessage(InfoBarSeverity.Error, "Couldn't save the list of databases", error);
        }
        ShowRows(select);
    }

    private void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row)
        {
            _databases.SetDefault(row.Folder);
            Saved(row.Folder);
        }
    }

    // ---------- Rename

    private void RenameFlyout_Opening(object sender, object e)
    {
        RenameBox.Text = Selected?.Name ?? "";
        RenameBox.SelectAll();
    }

    private void RenameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Rename();
        }
    }

    private void RenameConfirm_Click(object sender, RoutedEventArgs e) => Rename();

    private void Rename()
    {
        if (Selected is not { } row)
        {
            return;
        }
        if (_databases.Rename(row.Folder, RenameBox.Text) is { } refused)
        {
            ShowMessage(InfoBarSeverity.Warning, "Not renamed", refused);
            return;
        }
        RenameFlyout.Hide();
        Saved(row.Folder);
    }

    // ---------- Remove

    private void RemoveFlyout_Opening(object sender, object e) =>
        RemoveText.Text = $"Remove \"{Selected?.Name}\" from the list? The folder and its files are kept.";

    private void RemoveConfirm_Click(object sender, RoutedEventArgs e)
    {
        RemoveFlyout.Hide();
        if (Selected is { } row && _databases.Remove(row.Folder))
        {
            Saved(select: null);
        }
    }

    // ---------- Add existing folder

    private async void AddExisting_Click(object sender, RoutedEventArgs e)
    {
        var folder = await DatabaseRow.PickFolderAsync();
        if (folder == null)
        {
            return;
        }
        if (_databases.Find(folder) is { } existing)
        {
            ShowRows(existing.Folder);
            ShowMessage(InfoBarSeverity.Informational, "Already in the list", $"This folder is already in the list as \"{existing.Name}\".");
            return;
        }
        _databases.AddIfMissing(folder);
        Saved(folder);
        if (!MessageBar.IsOpen && !DataLocation.HasDatabase(folder))
        {
            ShowMessage(InfoBarSeverity.Informational, "No database in this folder yet",
                "Gnoseis asks before starting a new, empty database there when it's opened.");
        }
    }

    // ---------- New database

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        MessageBar.IsOpen = false;
        var folder = await DatabaseRow.PickFolderAsync();
        if (folder == null)
        {
            return;
        }
        if (_databases.Find(folder) is { } existing)
        {
            ShowRows(existing.Folder);
            ShowMessage(InfoBarSeverity.Informational, "Already in the list", $"This folder is already in the list as \"{existing.Name}\".");
            return;
        }
        if (Directory.EnumerateFileSystemEntries(folder).Any())
        {
            ShowMessage(InfoBarSeverity.Warning, "Choose an empty folder",
                "A new database goes in an empty folder (the folder picker can make one). To list a folder that already has a database in it, use Add existing folder.");
            return;
        }
        _newFolder = folder;
        NewNameBox.Text = KnownDatabases.NameFor(folder);
        NewFolderText.Text = $"{folder}\nThe database is created the first time it's opened.";
        FlyoutBase.ShowAttachedFlyout(NewButton);
        NewNameBox.SelectAll();
    }

    private void NewNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            AddNew();
        }
    }

    private void NewConfirm_Click(object sender, RoutedEventArgs e) => AddNew();

    private void AddNew()
    {
        if (_newFolder == null)
        {
            return;
        }
        if (_databases.Add(NewNameBox.Text, _newFolder) is { } refused)
        {
            ShowMessage(InfoBarSeverity.Warning, "Not added", refused);
            return;
        }
        NewFlyout.Hide();
        var folder = _newFolder;
        _newFolder = null;
        Saved(folder);
    }
}
