/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Data;

/// <summary>Where the data folder in use came from.</summary>
public enum DataFolderSource
{
    /// <summary><c>--data-folder &lt;folder&gt;</c> on the command line.</summary>
    CommandLine,

    /// <summary>The GNOSEIS_DATA_FOLDER environment variable.</summary>
    EnvironmentVariable,

    /// <summary>The <c>portable</c> marker next to Gnoseis.exe.</summary>
    Portable,

    /// <summary>Chosen in the start-up picker.</summary>
    Picker,

    /// <summary>The default database in the list of known databases ("Always start with this database").</summary>
    DefaultEntry,

    /// <summary>The default folder, %LOCALAPPDATA%\Gnoseis.</summary>
    DefaultFolder,
}

/// <summary>What the command line asked for: a data folder, nothing, or an error (then <see cref="Folder"/> is null).</summary>
public sealed record DataFolderArgument(string? Folder, string? Error)
{
    public static readonly DataFolderArgument None = new(null, null);

    public bool IsError => Error != null;
}

/// <summary>The folder that holds the database, settings and crash log.</summary>
public sealed record DataLocation(string Folder, bool IsPortable, DataFolderSource Source)
{
    /// <summary>A file with this name (no extension, content ignored) next to Gnoseis.exe turns on portable mode.</summary>
    public const string PortableMarkerName = "portable";

    /// <summary>In portable mode the data is in this folder next to Gnoseis.exe.</summary>
    public const string PortableDataFolderName = "data";

    /// <summary>The command-line argument that names the data folder.</summary>
    public const string DataFolderOption = "--data-folder";

    /// <summary>
    /// Reads the command line (without the program name). <c>--data-folder &lt;folder&gt;</c> or
    /// <c>--data-folder=&lt;folder&gt;</c>, the name in any case; a relative folder is taken from
    /// <paramref name="workingFolder"/>. Anything else is an error, never a fallback to the usual data.
    /// </summary>
    public static DataFolderArgument ParseArguments(IReadOnlyList<string> args, string workingFolder)
    {
        string? value = null;
        var seen = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? argValue;
            if (arg.Equals(DataFolderOption, StringComparison.OrdinalIgnoreCase))
            {
                argValue = i + 1 < args.Count ? args[++i] : null;
            }
            else if (arg.StartsWith(DataFolderOption + "=", StringComparison.OrdinalIgnoreCase))
            {
                argValue = arg[(DataFolderOption.Length + 1)..];
            }
            else
            {
                return new DataFolderArgument(null, $"Unknown argument: {arg}");
            }

            if (seen)
            {
                return new DataFolderArgument(null, $"{DataFolderOption} was given more than once.");
            }
            seen = true;
            value = argValue;
            if (value == null)
            {
                return new DataFolderArgument(null, $"{DataFolderOption} needs a folder after it.");
            }
        }

        if (!seen)
        {
            return DataFolderArgument.None;
        }

        // "D:\Gnoseis data\" reaches the app as D:\Gnoseis data" under Windows' argument rules.
        if (value!.EndsWith('"'))
        {
            value = value[..^1];
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            return new DataFolderArgument(null, $"{DataFolderOption} needs a folder after it.");
        }

        try
        {
            return new DataFolderArgument(Path.GetFullPath(value, workingFolder), null);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new DataFolderArgument(null, $"Not a valid folder: {value}");
        }
    }

    /// <summary>
    /// The first that applies: the command-line folder, the environment variable, the portable marker,
    /// the start-up picker (null is returned: ask), the default database in the list, the default folder.
    /// The list is loaded only when it's needed, so a portable copy never reads it.
    /// </summary>
    public static DataLocation? Resolve(string? argumentFolder, string? variableFolder, string exeFolder,
        bool portableMarkerExists, Func<KnownDatabases> loadDatabases)
    {
        if (!string.IsNullOrWhiteSpace(argumentFolder))
        {
            return new DataLocation(Path.GetFullPath(argumentFolder), IsPortable: false, DataFolderSource.CommandLine);
        }
        if (!string.IsNullOrWhiteSpace(variableFolder))
        {
            return new DataLocation(Path.GetFullPath(variableFolder), IsPortable: false, DataFolderSource.EnvironmentVariable);
        }
        if (portableMarkerExists)
        {
            return new DataLocation(Path.Combine(exeFolder, PortableDataFolderName), IsPortable: true, DataFolderSource.Portable);
        }

        var databases = loadDatabases();
        if (databases.AskAtStartup)
        {
            return null;
        }
        return databases.IsDefaultFolder(databases.Default.Folder)
            ? new DataLocation(databases.DefaultFolder, IsPortable: false, DataFolderSource.DefaultFolder)
            : new DataLocation(databases.Default.Folder, IsPortable: false, DataFolderSource.DefaultEntry);
    }

    /// <summary>
    /// Whether to ask before using a folder with no database in it: for a folder from the command line,
    /// the picker or the default entry, but not the default folder (first run) or the environment variable.
    /// </summary>
    public bool AsksBeforeCreating(string defaultFolder) =>
        Source is DataFolderSource.CommandLine or DataFolderSource.Picker or DataFolderSource.DefaultEntry
        && !KnownDatabases.SameFolder(Folder, defaultFolder);

    /// <summary>Whether the folder has a Gnoseis database file in it.</summary>
    public static bool HasDatabase(string folder) => File.Exists(Path.Combine(folder, DatabaseSchema.FileName));

    /// <summary>Creates the folder if needed and checks that a file can be written in it.</summary>
    public static bool CanWrite(string folder, out string? error)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return false;
        }
    }
}
