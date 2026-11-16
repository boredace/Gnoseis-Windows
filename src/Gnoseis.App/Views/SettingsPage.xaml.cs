/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Diagnostics;
using System.Reflection;
using Gnoseis.Core.Backup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Views;

/// <summary>Appearance, databases, backup / restore (Android Import / Export page) and about.</summary>
public sealed partial class SettingsPage : Page
{
    private bool _initializing = true;

    public SettingsPage()
    {
        InitializeComponent();

        ThemeComboBox.SelectedItem = ThemeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == App.Settings.ElementTheme.ToString()) ?? ThemeComboBox.Items[0];
        DatabasePathText.Text = AppPaths.DatabaseFile;
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        VersionText.Text = $"Gnoseis for Windows {version}";
        DataFolderText.Text = $"Data folder: {AppPaths.DataFolder}";
        PortableModeText.Text = $"Portable mode: {(AppPaths.IsPortable ? "on" : "off")}";
        ShowDatabases();
        ShowEncryptionState();
        _initializing = false;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ThemeComboBox.SelectedItem is not ComboBoxItem { Tag: string theme })
        {
            return;
        }
        App.Settings.Theme = theme;
        App.Settings.Save();
        App.MainWindow?.ApplyTheme(App.Settings.ElementTheme);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => MainWindow.OpenDataFolder();

    // ---------- Databases

    // The list as read when the page opened; null in a portable copy, which never reads or writes it.
    private KnownDatabases? _databases;

    // An unreadable list is only rewritten once the user changes something here.
    private bool _databasesWritten;

    private void ShowDatabases()
    {
        if (AppPaths.IsPortable)
        {
            DatabasesPanel.Visibility = Visibility.Collapsed;
            PortableDatabasesText.Visibility = Visibility.Visible;
            return;
        }
        _databases ??= AppPaths.LoadDatabases();

        var wasInitializing = _initializing;
        _initializing = true;
        DatabasesUnreadableInfoBar.IsOpen = _databases.CouldNotBeRead && !_databasesWritten;
        AskAtStartupToggle.IsOn = _databases.AskAtStartup;
        DefaultDatabaseComboBox.ItemsSource = _databases.Entries.ToList();
        DefaultDatabaseComboBox.SelectedItem = _databases.Default;
        DefaultDatabaseComboBox.IsEnabled = !_databases.AskAtStartup;
        _initializing = wasInitializing;
    }

    /// <summary>Writes databases.json; returns the error, or null.</summary>
    private string? SaveDatabases()
    {
        try
        {
            _databases!.Save(AppPaths.DatabasesFile);
            _databasesWritten = true;
            DatabasesUnreadableInfoBar.IsOpen = false;
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    private void SaveDatabasesAndShow()
    {
        if (SaveDatabases() is { } error)
        {
            ShowResult(InfoBarSeverity.Error, "Couldn't save the list of databases", error, null);
        }
        ShowDatabases();
    }

    private void AskAtStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing || _databases == null)
        {
            return;
        }
        _databases.AskAtStartup = AskAtStartupToggle.IsOn;
        SaveDatabasesAndShow();
    }

    private void DefaultDatabaseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _databases == null || DefaultDatabaseComboBox.SelectedItem is not KnownDatabase entry
            || entry == _databases.Default)
        {
            return;
        }
        _databases.SetDefault(entry.Folder);
        SaveDatabasesAndShow();
    }

    private async void ManageDatabases_Click(object sender, RoutedEventArgs e)
    {
        if (_databases == null)
        {
            return;
        }
        await Dialogs.ShowCustomAsync(new ManageDatabasesDialog(_databases, SaveDatabases));
        ShowDatabases();
    }

    // ---------- Encryption

    /// <summary>The switch is on only while backups are encrypted here; a key set up elsewhere shows as off, with a note.</summary>
    private void ShowEncryptionState()
    {
        var state = App.BackupEncryption.State;
        var wasInitializing = _initializing;
        _initializing = true;
        EncryptToggle.IsOn = state == BackupEncryptionState.On;
        _initializing = wasInitializing;
        KeyUnavailableInfoBar.IsOpen = state == BackupEncryptionState.KeyUnavailable;
        ChangePassphraseButton.Visibility = state == BackupEncryptionState.On ? Visibility.Visible : Visibility.Collapsed;
        PlainBackupCard.Visibility = state == BackupEncryptionState.Off ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void EncryptToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }
        try
        {
            if (EncryptToggle.IsOn)
            {
                var dialog = new BackupPassphraseSetupDialog(isChange: false);
                await Dialogs.ShowCustomAsync(dialog);
                if (dialog.Succeeded)
                {
                    ShowResult(InfoBarSeverity.Success, "Backups are now encrypted", "Back up and Back up now on Home encrypt every backup with this passphrase.", null);
                }
            }
            else if (await Dialogs.ConfirmAsync(
                "Turn off encryption?",
                "New backups won't be encrypted. Backups already made stay encrypted and still need their passphrase.",
                "Turn off"))
            {
                App.BackupEncryption.TurnOff();
                ShowResult(InfoBarSeverity.Informational, "Encryption turned off", "New backups won't be encrypted.", null);
            }
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, "Couldn't change backup encryption", ex.Message, null);
        }
        ShowEncryptionState();
    }

    private async void ChangePassphrase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BackupPassphraseSetupDialog(isChange: true);
        await Dialogs.ShowCustomAsync(dialog);
        if (dialog.Succeeded)
        {
            ShowResult(InfoBarSeverity.Success, "Passphrase changed", "New backups use the new passphrase. Backups made before keep their old one.", null);
        }
        ShowEncryptionState();
    }

    // ---------- Back up and restore

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (App.BackupEncryption.State == BackupEncryptionState.KeyUnavailable)
        {
            await Dialogs.ShowMessageAsync("Set up encryption again on this PC",
                "Encryption was set up for another Windows user or PC, so no backup was made. Turn on Encrypt backups to set it up here, or use Back up without encryption.");
            return;
        }

        var file = await BackupFiles.PickBackupFileAsync(App.BackupEncryption.State == BackupEncryptionState.On);
        if (file == null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var encrypted = await App.BackupEncryption.BackUpAsync(file);
            ShowResult(InfoBarSeverity.Success, encrypted ? "Encrypted backup saved" : "Backup saved", file, file);
        }, "Couldn't back up the database");
    }

    private async void PlainBackup_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await Dialogs.ConfirmAsync(
            "Back up without encryption?",
            "This backup won't be encrypted. Anyone who gets the file can read all your data.",
            "Back up without encryption");
        if (!confirmed)
        {
            return;
        }
        var file = await BackupFiles.PickBackupFileAsync(encrypted: false);
        if (file == null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await App.BackupEncryption.BackUpWithoutEncryptionAsync(file);
            ShowResult(InfoBarSeverity.Warning, "Unencrypted backup saved", file, file);
        }, "Couldn't back up the database");
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var file = await BackupFiles.PickRestoreFileAsync();
        if (file == null)
        {
            return;
        }

        // A copy of the key from a typed passphrase, for the offer to keep encrypting with it.
        BackupMasterKey? typedKey = null;
        async Task<BackupMasterKey?> Unlock(EncryptedBackupHeader header)
        {
            if (App.BackupEncryption.StoredKeyFor(header) is { } stored)
            {
                return stored;
            }
            var key = await BackupPassphraseDialog.AskAsync(Path.GetFileName(file), file, header);
            if (key != null)
            {
                typedKey = BackupMasterKey.FromKey(key.Key, key.KdfSalt, key.Parameters);
            }
            return key;
        }

        try
        {
            await RunBusyAsync(async () =>
            {
                StagedBackup staged;
                try
                {
                    staged = await BackupRepository.StageAsync(file, Unlock);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (EncryptedBackupException ex)
                {
                    await Dialogs.ShowMessageAsync("This file can't be restored", DatabaseInspection.Refusal(ex.Message));
                    return;
                }
                catch (BackupFileUnreadableException)
                {
                    await Dialogs.ShowMessageAsync("This file can't be restored", DatabaseInspection.Refusal(BackupFileUnreadableException.Text));
                    return;
                }

                using (staged)
                {
                    var inspection = staged.Inspection;
                    if (!inspection.IsValid)
                    {
                        await Dialogs.ShowMessageAsync("This file can't be restored", DatabaseInspection.Refusal(inspection.Problem ?? DatabaseInspection.NotABackup));
                        return;
                    }

                    var confirmed = await Dialogs.ConfirmAsync(
                        "Replace all data?",
                        $"\"{Path.GetFileName(file)}\" contains {inspection.NoteCount} notes, {inspection.ContactCount} contacts, " +
                        $"{inspection.OrganizationCount} organizations, {inspection.CategoryCount} categories and {inspection.ItemCount} items.\n\n" +
                        "All data currently in Gnoseis will be replaced with the data from this file. " +
                        "A copy of your current data is saved in the database folder first.",
                        "Replace data");
                    if (!confirmed)
                    {
                        return;
                    }

                    var safetyCopy = await App.Container.Backup.RestoreAsync(staged);
                    await Dialogs.ShowMessageAsync("Database restored",
                        $"Your data was replaced with the data from \"{Path.GetFileName(file)}\".\n\nThe previous data was saved as:\n{safetyCopy}");
                }

                if (typedKey != null && App.BackupEncryption.State != BackupEncryptionState.On
                    && await Dialogs.ConfirmAsync(
                        "Keep encrypting backups?",
                        "Keep encrypting backups on this device with this passphrase? Back up will then encrypt every backup with it, without asking for it.",
                        "Keep encrypting", "Not now"))
                {
                    App.BackupEncryption.KeepKey(typedKey);
                }
                Navigator.ResetToHome();
            }, "Couldn't restore the database");
        }
        finally
        {
            typedKey?.Dispose();
        }
    }

    private async Task RunBusyAsync(Func<Task> action, string errorTitle)
    {
        BackupButton.IsEnabled = false;
        RestoreButton.IsEnabled = false;
        PlainBackupButton.IsEnabled = false;
        BusyBar.Visibility = Visibility.Visible;
        ResultInfoBar.IsOpen = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, errorTitle, ex.Message, null);
        }
        finally
        {
            BackupButton.IsEnabled = true;
            RestoreButton.IsEnabled = true;
            PlainBackupButton.IsEnabled = true;
            BusyBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message, string? fileToShow)
    {
        ResultInfoBar.Severity = severity;
        ResultInfoBar.Title = title;
        ResultInfoBar.Message = message;
        ResultInfoBar.ActionButton = null;
        if (fileToShow != null)
        {
            var show = new Button { Content = "Show in folder" };
            show.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fileToShow}\"") { UseShellExecute = true });
            ResultInfoBar.ActionButton = show;
        }
        ResultInfoBar.IsOpen = true;
    }
}
