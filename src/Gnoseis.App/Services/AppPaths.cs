/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;

namespace Gnoseis.App.Services;

/// <summary>
/// Where the app keeps its files. The database is "gnoseis_data" (same name as on Android) in the first of:
///   the folder after --data-folder on the command line
///   the folder in GNOSEIS_DATA_FOLDER
///   a data folder next to Gnoseis.exe        (portable mode: a file named "portable" next to Gnoseis.exe)
///   the database chosen in the start-up picker, when "Always ask" is on (databases.json)
///   the default database in databases.json
///   %LOCALAPPDATA%\Gnoseis\                  (unpackaged build, the default)
///   the package LocalState folder            (instead of the default, if the app is ever packaged as MSIX)
/// The data folder is known once start-up has resolved it (after the picker, when there is one); nothing
/// reads it before.
/// </summary>
public static class AppPaths
{
    private static DataLocation? _location;

    /// <summary>The command line, read once; an error means Gnoseis says so and quits without touching any data.</summary>
    public static DataFolderArgument Argument { get; } = DataLocation.ParseArguments(
        Environment.GetCommandLineArgs().Skip(1).ToList(), Environment.CurrentDirectory);

    /// <summary>%LOCALAPPDATA%\Gnoseis, whatever folder is in use: databases.json, and the crash log before the folder is known.</summary>
    public static string LocalFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gnoseis");

    /// <summary>The list of known databases, for every copy except a portable one.</summary>
    public static string DatabasesFile => Path.Combine(LocalFolder, KnownDatabases.FileName);

    public static string DefaultDataFolder { get; } = FindDefaultDataFolder();

    public static bool IsPortableMarkerPresent =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, DataLocation.PortableMarkerName));

    /// <summary>
    /// The folder by the order above, or null when the start-up picker has to ask. Reads databases.json
    /// only when it's needed (never in a portable copy).
    /// </summary>
    public static DataLocation? ResolveAtStartup() => DataLocation.Resolve(
        Argument.Folder,
        // For testing, or to keep the data somewhere else (e.g. a synced folder).
        Environment.GetEnvironmentVariable("GNOSEIS_DATA_FOLDER"),
        AppContext.BaseDirectory,
        IsPortableMarkerPresent,
        LoadDatabases);

    public static KnownDatabases LoadDatabases() => KnownDatabases.Load(DatabasesFile, DefaultDataFolder);

    public static bool IsResolved => _location != null;

    /// <summary>Sets the folder in use, once, at start-up.</summary>
    public static void SetLocation(DataLocation location)
    {
        if (_location != null)
        {
            throw new InvalidOperationException("The data folder is already set.");
        }
        _location = location;
    }

    private static DataLocation Location =>
        _location ?? throw new InvalidOperationException("The data folder is not known yet.");

    public static string DataFolder => Location.Folder;

    public static bool IsPortable => Location.IsPortable;

    public static DataFolderSource Source => Location.Source;

    public static string DatabaseFile => Path.Combine(DataFolder, DatabaseSchema.FileName);

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    /// <summary>The backup master key, protected for the current Windows user (not in settings.json).</summary>
    public static string BackupKeyFile => Path.Combine(DataFolder, Core.Backup.StoredBackupKey.FileName);

    private static string FindDefaultDataFolder()
    {
        try
        {
            // Only available when running with package identity.
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            return LocalFolder;
        }
    }
}
