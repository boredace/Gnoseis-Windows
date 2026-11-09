/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Data;

/// <summary>The folder that holds the database, settings and crash log.</summary>
public sealed record DataLocation(string Folder, bool IsPortable)
{
    /// <summary>A file with this name (no extension, content ignored) next to Gnoseis.exe turns on portable mode.</summary>
    public const string PortableMarkerName = "portable";

    /// <summary>In portable mode the data is in this folder next to Gnoseis.exe.</summary>
    public const string PortableDataFolderName = "data";

    /// <summary>
    /// The environment variable wins, then the portable marker, then the default folder.
    /// </summary>
    public static DataLocation Resolve(string? overrideFolder, string exeFolder, bool portableMarkerExists, string defaultFolder)
    {
        if (!string.IsNullOrWhiteSpace(overrideFolder))
        {
            return new DataLocation(Path.GetFullPath(overrideFolder), IsPortable: false);
        }

        return portableMarkerExists
            ? new DataLocation(Path.Combine(exeFolder, PortableDataFolderName), IsPortable: true)
            : new DataLocation(defaultFolder, IsPortable: false);
    }

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
