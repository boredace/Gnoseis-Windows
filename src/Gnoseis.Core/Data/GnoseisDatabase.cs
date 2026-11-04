/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;
using Microsoft.Data.Sqlite;

namespace Gnoseis.Core.Data;

/// <summary>Raised after data changes, so open pages can refresh (the Room Flow equivalent).</summary>
public sealed class DataChangedEventArgs(IReadOnlyCollection<RecordType> types, bool databaseReplaced = false) : EventArgs
{
    public IReadOnlyCollection<RecordType> Types { get; } = types;

    /// <summary>True when the whole database file was replaced (restore); everything must reload.</summary>
    public bool DatabaseReplaced { get; } = databaseReplaced;

    public bool Affects(params RecordType[] types) => DatabaseReplaced || types.Any(t => Types.Contains(t));
}

/// <summary>Thrown when a database file cannot be used by this version of Gnoseis.</summary>
public sealed class DatabaseIncompatibleException(string message) : Exception(message);

/// <summary>
/// The Gnoseis SQLite database file. The file format is shared with the Android app (Room),
/// so a "gnoseis_data" file copied from Android can be used as is, and vice versa.
/// </summary>
public sealed class GnoseisDatabase
{
    private readonly string _connectionString;

    public GnoseisDatabase(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    public string FilePath { get; }

    public event EventHandler<DataChangedEventArgs>? DataChanged;

    /// <summary>
    /// Opens (creating if needed) and checks the database file. A file that does not match the
    /// expected schema raises <see cref="DatabaseIncompatibleException"/> and is left as it is.
    /// A file at an older supported schema version is first copied next to itself, then migrated.
    /// </summary>
    public static GnoseisDatabase Open(string filePath)
    {
        var database = new GnoseisDatabase(filePath);
        database.Initialize();
        return database;
    }

    /// <summary>
    /// Closes all pooled connections. Call when the app exits, so no file handles stay open and
    /// the database file can be copied right away.
    /// </summary>
    public static void ReleaseAllConnections() => SqliteConnection.ClearAllPools();

    /// <summary>
    /// Renames an unusable database file (and its -wal / -shm files) to
    /// "gnoseis_data.unusable-&lt;date&gt;" so a new database can be created. Nothing is deleted.
    /// Returns the new name of the main file.
    /// </summary>
    public static string SetAside(string filePath)
    {
        ReleaseAllConnections();
        var target = $"{filePath}.unusable-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        File.Move(filePath, target);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(filePath + suffix))
            {
                File.Move(filePath + suffix, target + suffix);
            }
        }
        return target;
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    internal void NotifyChanged(params RecordType[] types) =>
        DataChanged?.Invoke(this, new DataChangedEventArgs(types));

    internal void NotifyReplaced() =>
        DataChanged?.Invoke(this, new DataChangedEventArgs(RecordTypeExtensions.UserTypes, databaseReplaced: true));

    internal void Initialize()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();

        if (IsEmpty(connection))
        {
            CreateSchema(connection);
        }
        else
        {
            var problem = FindSchemaProblem(connection);
            if (problem != null)
            {
                throw new DatabaseIncompatibleException(problem);
            }

            if (UserVersion(connection) < DatabaseSchema.Version)
            {
                // Keep the file as it was before changing it (never lose data).
                using (var safetyCopy = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = $"{FilePath}.before-v{DatabaseSchema.Version}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.backup",
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false,
                }.ToString()))
                {
                    safetyCopy.Open();
                    connection.BackupDatabase(safetyCopy);
                    Execute(safetyCopy, "PRAGMA journal_mode=DELETE;");
                }
                Migrate(connection);
            }
        }

        // Rollback journal instead of Room's write-ahead log: every committed change is in the main
        // "gnoseis_data" file, so the file can be copied to Android as is. Any -wal file that came
        // from Android is folded into the main file here. Room switches back to WAL when it opens the file.
        Execute(connection, "PRAGMA journal_mode=DELETE;");
    }

    private static bool IsEmpty(SqliteConnection connection)
    {
        var userVersion = Convert.ToInt64(Scalar(connection, "PRAGMA user_version;"));
        var tableCount = Convert.ToInt64(Scalar(connection,
            "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name NOT IN ('android_metadata', 'sqlite_sequence');"));
        return userVersion == 0 && tableCount == 0;
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var statement in DatabaseSchema.CreateStatements)
        {
            Execute(connection, statement, transaction);
        }

        // The welcome note is written in the same transaction as the schema, so it is always
        // there before the first read (fixes Android known bug 7).
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO Note (id, ownerDbId, title, textContents, createDateTime, createDate) " +
                "VALUES ($id, $ownerDbId, $title, $textContents, $createDateTime, $createDate);";
            var (createDate, createDateTime) = Domain.NoteDates.ForNewNote(DateOnly.FromDateTime(DateTime.Now), DateTime.Now.TimeOfDay);
            var note = new Note
            {
                OwnerDbId = "yyy",
                CreateDate = createDate,
                CreateDateTime = createDateTime,
                Title = "This is a default note",
                TextContents = "This note is created when application was initialized. Feel free to delete or edit it as you please.",
            };
            insert.Parameters.AddWithValue("$id", note.Id);
            insert.Parameters.AddWithValue("$ownerDbId", note.OwnerDbId);
            insert.Parameters.AddWithValue("$title", note.Title);
            insert.Parameters.AddWithValue("$textContents", note.TextContents);
            insert.Parameters.AddWithValue("$createDateTime", note.CreateDateTime);
            insert.Parameters.AddWithValue("$createDate", note.CreateDate);
            insert.ExecuteNonQuery();
        }

        Execute(connection, $"PRAGMA user_version = {DatabaseSchema.Version};", transaction);
        transaction.Commit();
    }

    private static long UserVersion(SqliteConnection connection, SqliteTransaction? transaction = null) =>
        Convert.ToInt64(Scalar(connection, "PRAGMA user_version;", transaction));

    /// <summary>
    /// Brings a file that passed <see cref="FindSchemaProblem"/> up to <see cref="DatabaseSchema.Version"/>,
    /// in one transaction. The same steps as the Android Room migrations.
    /// </summary>
    internal static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        var version = UserVersion(connection, transaction);

        if (version == 1)
        {
            // 1 -> 2: no table changes. Drop indexes that SQLite did not create itself, and keep only
            // the first link of each unordered pair of records.
            var indexes = new List<string>();
            using (var query = connection.CreateCommand())
            {
                query.Transaction = transaction;
                query.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL;";
                using var reader = query.ExecuteReader();
                while (reader.Read())
                {
                    indexes.Add(reader.GetString(0));
                }
            }
            foreach (var index in indexes)
            {
                Execute(connection, $"DROP INDEX `{index.Replace("`", "``")}`;", transaction);
            }

            Execute(connection,
                "DELETE FROM LinkedRecord WHERE rowid NOT IN (" +
                "SELECT MIN(rowid) FROM LinkedRecord " +
                "GROUP BY MIN(record1Id, record2Id), MAX(record1Id, record2Id));", transaction);
            version = 2;
        }

        if (version == 2)
        {
            // 2 -> 3: created and updated times (UTC epoch ms) on every record, a created time on
            // every link, and the Settings table. Existing rows keep NULL: their times are unknown.
            // Settings is created empty: a missing row means "never".
            foreach (var table in DatabaseSchema.TimestampedRecordTables)
            {
                Execute(connection, $"ALTER TABLE `{table}` ADD COLUMN `createdAt` INTEGER;", transaction);
                Execute(connection, $"ALTER TABLE `{table}` ADD COLUMN `updatedAt` INTEGER;", transaction);
            }
            Execute(connection, "ALTER TABLE `LinkedRecord` ADD COLUMN `createdAt` INTEGER;", transaction);
            Execute(connection, DatabaseSchema.SettingsCreateStatement, transaction);
            version = 3;
        }

        // Room writes the new identity hash itself after its migrations; do the same.
        Execute(connection, "CREATE TABLE IF NOT EXISTS room_master_table (id INTEGER PRIMARY KEY,identity_hash TEXT)", transaction);
        Execute(connection, $"INSERT OR REPLACE INTO room_master_table (id,identity_hash) VALUES(42, '{DatabaseSchema.RoomIdentityHashFor(version)}')", transaction);
        Execute(connection, $"PRAGMA user_version = {version};", transaction);
        transaction.Commit();
    }

    /// <summary>Returns a description of why the database cannot be used, or null if it is fine.</summary>
    internal static string? FindSchemaProblem(SqliteConnection connection)
    {
        var userVersion = UserVersion(connection);
        if (userVersion > DatabaseSchema.Version)
        {
            return $"The database was created by a newer version of Gnoseis (schema version {userVersion}). Update this app to open it.";
        }
        if (userVersion < DatabaseSchema.OldestSupportedVersion)
        {
            return $"The database has an unsupported schema version ({userVersion}).";
        }

        var hasMasterTable = Convert.ToInt64(Scalar(connection,
            "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'room_master_table';")) > 0;
        if (hasMasterTable)
        {
            var hash = Scalar(connection, "SELECT identity_hash FROM room_master_table WHERE id = 42;") as string;
            if (hash != null && hash != DatabaseSchema.RoomIdentityHashFor(userVersion))
            {
                return "The database schema does not match this version of Gnoseis.";
            }
        }

        foreach (var (table, columns) in DatabaseSchema.RequiredColumnsFor(userVersion))
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(`{table}`);";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    existing.Add(reader.GetString(reader.GetOrdinal("name")));
                }
            }

            if (existing.Count == 0)
            {
                return $"The database has no {table} table, so it is not a Gnoseis database.";
            }

            var missing = columns.FirstOrDefault(c => !existing.Contains(c));
            if (missing != null)
            {
                return $"The {table} table has no {missing} column, so it is not a compatible Gnoseis database.";
            }
        }

        return null;
    }

    internal static object? Scalar(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    internal static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
