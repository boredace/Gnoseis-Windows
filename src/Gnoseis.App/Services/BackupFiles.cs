/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.Windows.Storage.Pickers;

namespace Gnoseis.App.Services;

/// <summary>
/// The file pickers of Back up and Restore (Settings, and Back up now on Home). They use the Windows App
/// SDK pickers, which also work when Gnoseis runs as administrator (Windows.Storage.Pickers fails then).
/// </summary>
public static class BackupFiles
{
    /// <summary>
    /// Asks where to save a backup; the full path, or null if the user cancels. An encrypted backup is
    /// suggested as "gnoseis_data_&lt;date&gt;.encrypted.backup", still under the .backup type.
    /// </summary>
    public static async Task<string?> PickBackupFileAsync(bool encrypted)
    {
        var picker = new FileSavePicker(App.MainWindow!.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Core.Backup.BackupEncryption.SuggestedFileName(DateTime.Now, encrypted),
            DefaultFileExtension = BackupRepository.BackupFileExtension,
        };
        picker.FileTypeChoices.Add("Gnoseis backup", [BackupRepository.BackupFileExtension]);
        var result = await picker.PickSaveFileAsync();
        return result?.Path;
    }

    /// <summary>Asks for a backup or database file to restore; the full path, or null if the user cancels.</summary>
    public static async Task<string?> PickRestoreFileAsync()
    {
        var picker = new FileOpenPicker(App.MainWindow!.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        // Android database and backup files have no fixed extension.
        picker.FileTypeFilter.Add("*");
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }
}
