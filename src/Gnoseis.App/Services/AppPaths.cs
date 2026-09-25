/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;

namespace Gnoseis.App.Services;

/// <summary>
/// Where the app keeps its files. The database is "gnoseis_data" (same name as on Android) in:
///   %LOCALAPPDATA%\Gnoseis\                  (unpackaged build, the default)
///   the package LocalState folder            (if the app is ever packaged as MSIX)
/// </summary>
public static class AppPaths
{
    public static string DataFolder { get; } = ResolveDataFolder();

    public static string DatabaseFile => Path.Combine(DataFolder, DatabaseSchema.FileName);

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    private static string ResolveDataFolder()
    {
        // For testing, or to keep the data somewhere else (e.g. a synced folder).
        var overridden = Environment.GetEnvironmentVariable("GNOSEIS_DATA_FOLDER");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        try
        {
            // Only available when running with package identity.
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gnoseis");
        }
    }
}
