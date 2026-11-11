/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Backup;
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
/// Gives the key for an encrypted backup with this header: the key stored on this device if it fits, or one
/// derived from the passphrase the user types. Null means the user cancelled. The caller of the repository
/// method disposes the returned key, so return a key of its own (a copy of a stored key).
/// </summary>
public delegate Task<BackupMasterKey?> BackupUnlocker(EncryptedBackupHeader header);

/// <summary>
/// A backup file copied (or decrypted) into a private staging folder and checked, ready to restore.
/// Disposing it deletes the staged copy.
/// </summary>
public sealed class StagedBackup : IDisposable
{
    internal StagedBackup(string path, DatabaseInspection inspection, EncryptedBackupHeader? header)
    {
        Path = path;
        Inspection = inspection;
        EncryptionHeader = header;
    }

    internal string Path { get; }

    public DatabaseInspection Inspection { get; }

    /// <summary>The header of the encrypted file this came from, or null for a plain backup.</summary>
    public EncryptedBackupHeader? EncryptionHeader { get; }

    public bool WasEncrypted => EncryptionHeader != null;

    public void Dispose() => BackupRepository.DeleteStaged(Path);
}

/// <summary>
/// Backup (export) and restore (import) of the database file. Unlike the Android version, the
/// database stays open during a backup, and a restore checks the file first and reloads the data
/// without restarting the app (fixes Android known bugs 1-4).
/// </summary>
public sealed class BackupRepository(GnoseisDatabase database)
{
    public const string BackupFileExtension = ".backup";

    /// <summary>Folder under the data folder for the plain copy made during an encrypted export.</summary>
    public const string PrivateTemporaryFolderName = "backup-temp";

    public static string DefaultBackupFileName(DateTime now) =>
        $"gnoseis_data_{now:yyyy-MM-dd_HH-mm-ss}";

    /// <summary>Where restores stage the file they read (private to the user).</summary>
    private static string StagingRoot => Path.Combine(Path.GetTempPath(), "Gnoseis");

    private string PrivateTemporaryRoot =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database.FilePath))!, PrivateTemporaryFolderName);

    /// <summary>
    /// Writes a consistent, self-contained copy of the open database to <paramref name="destinationPath"/>,
    /// encrypted with <paramref name="encryptWith"/> when given (the caller keeps and disposes the key).
    /// The copy holds lastBackupAt = the time of this export; once the copy is complete, the live
    /// database gets the same value. A failed export leaves the live database unchanged.
    /// </summary>
    public Task ExportAsync(string destinationPath, BackupMasterKey? encryptWith = null) => Task.Run(() =>
    {
        var now = Models.EpochTime.UtcNowMillis();
        if (encryptWith == null)
        {
            CopyTo(destinationPath, lastBackupAt: now);
        }
        else
        {
            ExportEncrypted(destinationPath, encryptWith, lastBackupAt: now);
        }
        using var live = database.OpenConnection();
        SettingsRepository.UpsertLastBackupAt(live, now);
    });

    /// <summary>
    /// The plain copy is made in a private folder under the data folder, never next to the destination
    /// (which may be a synced folder); only encrypted bytes are written beside the destination.
    /// </summary>
    private void ExportEncrypted(string destinationPath, BackupMasterKey key, long lastBackupAt)
    {
        var folder = Path.Combine(PrivateTemporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var temporaryPath = destinationPath + ".tmp";
        try
        {
            var plainPath = Path.Combine(folder, DatabaseSchema.FileName);
            CopyTo(plainPath, lastBackupAt);

            DeleteIfExists(temporaryPath);
            using (var plain = new FileStream(plainPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                EncryptedBackupFormat.Encrypt(plain, output, key);
            }
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
            DeleteFolder(folder);
        }
    }

    /// <summary>
    /// Writes a copy of the open database to <paramref name="destinationPath"/>, through a temporary
    /// file so the destination is only ever a complete copy. With <paramref name="lastBackupAt"/>,
    /// that value is saved in the copy's Settings table; the live database is not changed.
    /// </summary>
    private void CopyTo(string destinationPath, long? lastBackupAt)
    {
        var temporaryPath = destinationPath + ".tmp";
        DeleteIfExists(temporaryPath);
        try
        {
            using (var source = database.OpenConnection())
            using (var destination = OpenUnpooled(temporaryPath))
            {
                source.BackupDatabase(destination);
                if (lastBackupAt is { } value)
                {
                    SettingsRepository.UpsertLastBackupAt(destination, value);
                }
                // One file with no -wal file next to it, so it can be copied anywhere (e.g. to Android).
                GnoseisDatabase.Execute(destination, "PRAGMA journal_mode=DELETE;");
            }
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    /// <summary>
    /// Copies a backup file into a private staging folder and checks it. A plain backup is copied; an
    /// encrypted one (told apart by its first 8 bytes, never by its name) is decrypted with the key
    /// <paramref name="unlock"/> gives. The file itself is not changed.
    /// </summary>
    /// <remarks>
    /// <paramref name="unlock"/> is called on the caller's thread (the UI thread in the app, so it can show a
    /// dialog); the file work runs on the thread pool.
    /// </remarks>
    /// <exception cref="EncryptedBackupException">The encrypted file can't be opened (wrong passphrase, damage, newer version).</exception>
    /// <exception cref="OperationCanceledException">The file is encrypted and no key was given.</exception>
    public static async Task<StagedBackup> StageAsync(string sourcePath, BackupUnlocker? unlock)
    {
        // No ConfigureAwait(false) before unlock is called: it must run where the caller runs.
        var header = await Task.Run(() => ReadEncryptionHeader(sourcePath));
        if (header == null)
        {
            return await Task.Run(() =>
            {
                var stagingPath = StagePlain(sourcePath);
                return new StagedBackup(stagingPath, InspectStagedOrDelete(stagingPath), null);
            }).ConfigureAwait(false);
        }

        using var key = unlock == null ? null : await unlock(header);
        if (key == null)
        {
            throw new OperationCanceledException("No key was given for the encrypted backup.");
        }

        return await Task.Run(() =>
        {
            var path = NewStagingPath();
            try
            {
                using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    EncryptedBackupFormat.Decrypt(input, key, output);
                }
            }
            catch
            {
                DeleteStaged(path);
                throw;
            }
            return new StagedBackup(path, InspectStagedOrDelete(path), header);
        }).ConfigureAwait(false);
    }

    /// <summary>The header of an encrypted backup, or null for any other file (decided by its first 8 bytes).</summary>
    private static EncryptedBackupHeader? ReadEncryptionHeader(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The file does not exist.", path);
        }
        if (!EncryptedBackupFormat.IsEncryptedFile(path))
        {
            return null;
        }
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return EncryptedBackupFormat.ReadHeader(input);
    }

    /// <summary>
    /// Checks that a file is an intact Gnoseis database at a supported schema version (an older
    /// one is migrated when restored). The file itself is not changed. An encrypted file is decrypted
    /// with the key <paramref name="unlock"/> gives; cancelling throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<DatabaseInspection> InspectAsync(string path, BackupUnlocker? unlock = null)
    {
        try
        {
            using var staged = await StageAsync(path, unlock).ConfigureAwait(false);
            return staged.Inspection;
        }
        catch (EncryptedBackupException e)
        {
            return DatabaseInspection.Invalid(e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DatabaseInspection.Invalid($"The file cannot be read ({e.Message}).");
        }
    }

    /// <summary>
    /// Replaces the current database with the given file (decrypted with the key <paramref name="unlock"/>
    /// gives, if it is encrypted). The file is checked first, and the current database is saved next to
    /// it as "gnoseis_data.before-restore-&lt;date&gt;.backup". Returns the path of that safety copy.
    /// </summary>
    public async Task<string> RestoreAsync(string sourcePath, BackupUnlocker? unlock = null)
    {
        // Work on a private copy, so the source is read once and never locked or modified.
        using var staged = await StageAsync(sourcePath, unlock).ConfigureAwait(false);
        return await RestoreAsync(staged).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the current database with a staged backup that passed its check. The current database is
    /// saved next to it first, as a plain copy. Returns the path of that safety copy.
    /// </summary>
    public async Task<string> RestoreAsync(StagedBackup staged)
    {
        if (!staged.Inspection.IsValid)
        {
            throw new DatabaseIncompatibleException(staged.Inspection.Problem ?? "The file cannot be restored.");
        }
        var stagingPath = staged.Path;

        var safetyCopyPath = $"{database.FilePath}.before-restore-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{BackupFileExtension}";
        // A safety copy, not a backup the user made: lastBackupAt is not written anywhere.
        await Task.Run(() => CopyTo(safetyCopyPath, lastBackupAt: null)).ConfigureAwait(false);

        await Task.Run(() =>
        {
            // Fold any -wal content into the staged file so it is a single self-contained file,
            // and bring an older supported schema version (1 or 2) up to date on the staged copy,
            // so the live file is only ever replaced by a current one. The source file is not
            // changed. Nothing is stamped: the restored file keeps the times and Settings rows
            // the backup holds.
            using (var connection = OpenUnpooled(stagingPath))
            {
                GnoseisDatabase.Execute(connection, "PRAGMA journal_mode=DELETE;");
                if (Convert.ToInt64(GnoseisDatabase.Scalar(connection, "PRAGMA user_version;")) < DatabaseSchema.Version)
                {
                    GnoseisDatabase.Migrate(connection);
                }
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

    /// <summary>
    /// Deletes plain copies left behind by an export or restore that didn't finish (the app was closed or
    /// stopped). Run at start-up. Files another running copy of Gnoseis has open are left alone.
    /// </summary>
    public static void DeleteTemporaryFiles(string dataFolder)
    {
        DeleteFolderContents(Path.Combine(dataFolder, PrivateTemporaryFolderName));
        DeleteFolderContents(StagingRoot);
    }

    private static string NewStagingPath()
    {
        var folder = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, DatabaseSchema.FileName);
    }

    /// <summary>Copies a database file (and its -wal file, if any) to a new staging folder.</summary>
    private static string StagePlain(string path)
    {
        var stagingPath = NewStagingPath();
        try
        {
            File.Copy(path, stagingPath);
            if (File.Exists(path + "-wal"))
            {
                File.Copy(path + "-wal", stagingPath + "-wal");
            }
        }
        catch
        {
            DeleteStaged(stagingPath);
            throw;
        }
        return stagingPath;
    }

    private static DatabaseInspection InspectStagedOrDelete(string stagingPath)
    {
        try
        {
            return InspectStaged(stagingPath);
        }
        catch
        {
            DeleteStaged(stagingPath);
            throw;
        }
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

    internal static void DeleteStaged(string? stagingPath)
    {
        if (stagingPath != null)
        {
            DeleteFolder(Path.GetDirectoryName(stagingPath)!);
        }
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the clean-up at the next start.
        }
    }

    private static void DeleteFolderContents(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            DeleteFolder(folder);
        }
        foreach (var file in Directory.EnumerateFiles(root))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
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
