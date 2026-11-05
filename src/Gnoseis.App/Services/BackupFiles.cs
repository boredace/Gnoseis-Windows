/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Windows.Storage;
using Windows.Storage.Pickers;

namespace Gnoseis.App.Services;

/// <summary>The file pickers of Back up and Restore (Settings, and Back up now on Home).</summary>
public static class BackupFiles
{
    /// <summary>Asks where to save a backup; null if the user cancels.</summary>
    public static async Task<StorageFile?> PickBackupFileAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = BackupRepository.DefaultBackupFileName(DateTime.Now),
        };
        picker.FileTypeChoices.Add("Gnoseis backup", [BackupRepository.BackupFileExtension]);
        InitializeWithWindow(picker);
        return await picker.PickSaveFileAsync();
    }

    /// <summary>Asks for a backup or database file to restore; null if the user cancels.</summary>
    public static async Task<StorageFile?> PickRestoreFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        // Android database and backup files have no fixed extension.
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow(picker);
        return await picker.PickSingleFileAsync();
    }

    private static void InitializeWithWindow(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }
}
