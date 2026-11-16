/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.Windows.Storage.Pickers;

namespace Gnoseis.App.Views;

/// <summary>A database in the start-up picker or the Manage databases dialog.</summary>
public sealed class DatabaseRow(KnownDatabase entry, bool isDefault, bool isInUse)
{
    public KnownDatabase Entry { get; } = entry;

    public string Name => Entry.Name;

    public string Folder => Entry.Folder;

    public bool IsDefault { get; } = isDefault;

    public bool IsInUse { get; } = isInUse;

    /// <summary>The folder isn't there (for example an unplugged drive), so it can't be opened.</summary>
    public bool IsMissing { get; } = !Directory.Exists(entry.Folder);

    public string AutomationName =>
        string.Join(", ", new[]
        {
            Name, Folder,
            IsDefault ? "Default" : null,
            IsInUse ? "In use" : null,
            IsMissing ? "Folder not found" : null,
        }.Where(s => s != null));

    public static List<DatabaseRow> From(KnownDatabases list, string? folderInUse) =>
        list.Entries
            .Select(e => new DatabaseRow(e, e == list.Default, folderInUse != null && KnownDatabases.SameFolder(e.Folder, folderInUse)))
            .ToList();

    /// <summary>
    /// Asks for a folder (the picker can also make a new one); null if the user cancels. Uses the Windows App
    /// SDK picker, which also works when Gnoseis runs as administrator (Windows.Storage.Pickers fails then).
    /// </summary>
    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(App.MainWindow!.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }
}
