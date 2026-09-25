/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Gnoseis.App.Views;

/// <summary>Appearance, backup / restore (Android Import / Export page) and about.</summary>
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
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Gnoseis for Windows {version?.ToString(3)}";
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

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = BackupRepository.DefaultBackupFileName(DateTime.Now),
        };
        picker.FileTypeChoices.Add("Gnoseis backup", [BackupRepository.BackupFileExtension]);
        InitializeWithWindow(picker);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await App.Container.Backup.ExportAsync(file.Path);
            ShowResult(InfoBarSeverity.Success, "Backup saved", file.Path, file.Path);
        }, "Couldn't back up the database");
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        // Android database and backup files have no fixed extension.
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow(picker);

        var file = await picker.PickSingleFileAsync();
        if (file == null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var inspection = await BackupRepository.InspectAsync(file.Path);
            if (!inspection.IsValid)
            {
                await Dialogs.ShowMessageAsync("This file can't be restored", inspection.Problem ?? "The file is not a Gnoseis database.");
                return;
            }

            var confirmed = await Dialogs.ConfirmAsync(
                "Replace all data?",
                $"\"{file.Name}\" contains {inspection.NoteCount} notes, {inspection.ContactCount} contacts, " +
                $"{inspection.OrganizationCount} organizations, {inspection.CategoryCount} categories and {inspection.ItemCount} items.\n\n" +
                "All data currently in Gnoseis will be replaced with the data from this file. " +
                "A copy of your current data is saved in the database folder first.",
                "Replace data");
            if (!confirmed)
            {
                return;
            }

            var safetyCopy = await App.Container.Backup.RestoreAsync(file.Path);
            await Dialogs.ShowMessageAsync("Database restored",
                $"Your data was replaced with the data from \"{file.Name}\".\n\nThe previous data was saved as:\n{safetyCopy}");
            Navigator.ResetToHome();
        }, "Couldn't restore the database");
    }

    private async Task RunBusyAsync(Func<Task> action, string errorTitle)
    {
        BackupButton.IsEnabled = false;
        RestoreButton.IsEnabled = false;
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

    private static void InitializeWithWindow(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }
}
