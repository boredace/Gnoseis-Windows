/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.Data.Sqlite;

namespace Gnoseis.Core.Data;

/// <summary>What was found when checking a database file before restoring it.</summary>
public sealed record DatabaseInspection(
    bool IsValid,
    string? Problem,
    int NoteCount = 0,
    int ContactCount = 0,
    int OrganizationCount = 0,
    int CategoryCount = 0,
    int ItemCount = 0)
{
    public static DatabaseInspection Invalid(string problem) => new(false, problem);
}

/// <summary>
/// Backup (export) and restore (import) of the database file. Unlike the Android version, the
/// database stays open during a backup, and a restore checks the file first and reloads the data
/// without restarting the app (fixes Android known bugs 1-4).
/// </summary>
public sealed class BackupRepository(GnoseisDatabase database)
{
    public const string BackupFileExtension = ".backup";

    public static string DefaultBackupFileName(DateTime now) =>
        $"gnoseis_data_{now:yyyy-MM-dd_HH-mm-ss}";

    /// <summary>Writes a consistent, self-contained copy of the open database to <paramref name="destinationPath"/>.</summary>
    public Task ExportAsync(string destinationPath) => Task.Run(() =>
    {
        var temporaryPath = destinationPath + ".tmp";
        DeleteIfExists(temporaryPath);
        try
        {
            using (var source = database.OpenConnection())
            using (var destination = OpenUnpooled(temporaryPath))
            {
                source.BackupDatabase(destination);
                // One file with no -wal file next to it, so it can be copied anywhere (e.g. to Android).
                GnoseisDatabase.Execute(destination, "PRAGMA journal_mode=DELETE;");
            }
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    });

    /// <summary>Checks that a file is an intact Gnoseis database. The file itself is not changed.</summary>
    public static Task<DatabaseInspection> InspectAsync(string path) => Task.Run(() =>
    {
        string? stagingPath = null;
        try
        {
            stagingPath = Stage(path);
            return InspectStaged(stagingPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DatabaseInspection.Invalid($"The file cannot be read ({e.Message}).");
        }
        finally
        {
            DeleteStaged(stagingPath);
        }
    });

    /// <summary>
    /// Replaces the current database with the given file. The file is checked first, and the
    /// current database is saved next to it as "gnoseis_data.before-restore-&lt;date&gt;.backup".
    /// Returns the path of that safety copy.
    /// </summary>
    public async Task<string> RestoreAsync(string sourcePath)
    {
        // Work on a private copy, so the source is read once and never locked or modified.
        var stagingPath = await Task.Run(() => Stage(sourcePath)).ConfigureAwait(false);
        try
        {
            var inspection = await Task.Run(() => InspectStaged(stagingPath)).ConfigureAwait(false);
            if (!inspection.IsValid)
            {
                throw new DatabaseIncompatibleException(inspection.Problem ?? "The file cannot be restored.");
            }

            var safetyCopyPath = $"{database.FilePath}.before-restore-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{BackupFileExtension}";
            await ExportAsync(safetyCopyPath).ConfigureAwait(false);

            await Task.Run(() =>
            {
                // Fold any -wal content into the staged file so it is a single self-contained file.
                using (var staged = OpenUnpooled(stagingPath))
                {
                    GnoseisDatabase.Execute(staged, "PRAGMA journal_mode=DELETE;");
                }

                // Release every pooled handle on the live file before replacing it.
                SqliteConnection.ClearAllPools();
                DeleteIfExists(database.FilePath + "-wal");
                DeleteIfExists(database.FilePath + "-shm");
                File.Copy(stagingPath, database.FilePath, overwrite: true);
                database.Initialize();
            }).ConfigureAwait(false);

            database.NotifyReplaced();
            return safetyCopyPath;
        }
        finally
        {
            DeleteStaged(stagingPath);
        }
    }

    /// <summary>Copies a database file (and its -wal file, if any) to a new temporary folder.</summary>
    private static string Stage(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The file does not exist.", path);
        }

        var folder = Path.Combine(Path.GetTempPath(), "Gnoseis", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var stagingPath = Path.Combine(folder, DatabaseSchema.FileName);
        File.Copy(path, stagingPath);
        if (File.Exists(path + "-wal"))
        {
            File.Copy(path + "-wal", stagingPath + "-wal");
        }
        return stagingPath;
    }

    private static DatabaseInspection InspectStaged(string stagingPath)
    {
        try
        {
            using var connection = OpenUnpooled(stagingPath);

            var integrity = GnoseisDatabase.Scalar(connection, "PRAGMA quick_check;") as string;
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseInspection.Invalid("The file is damaged (integrity check failed).");
            }

            var problem = GnoseisDatabase.FindSchemaProblem(connection);
            if (problem != null)
            {
                return DatabaseInspection.Invalid(problem);
            }

            int Count(string table) => Convert.ToInt32(GnoseisDatabase.Scalar(connection, $"SELECT count(*) FROM `{table}`;"));
            return new DatabaseInspection(true, null,
                Count("Note"), Count("Contact"), Count("Organization"), Count("Category"), Count("Item"));
        }
        catch (SqliteException e)
        {
            return DatabaseInspection.Invalid($"The file is not a readable SQLite database ({e.Message}).");
        }
    }

    private static void DeleteStaged(string? stagingPath)
    {
        if (stagingPath == null)
        {
            return;
        }

        try
        {
            Directory.Delete(Path.GetDirectoryName(stagingPath)!, recursive: true);
        }
        catch (IOException)
        {
            // Temporary files; Windows cleans the temp folder eventually.
        }
    }

    private static SqliteConnection OpenUnpooled(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
