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

/// <summary>
/// Settings that belong to the database file (the Settings table), the same rows as Android.
/// They travel with backups. lastBackupAt is written by the export, lastDeletionAt by every
/// delete and unlink; the app does not show them yet, but keeps them right so the file gives
/// the Android app the right backup reminder.
/// </summary>
public sealed class SettingsRepository(GnoseisDatabase database) : RepositoryBase(database)
{
    /// <summary>UTC epoch ms of the last successful export, or null if there never was one.</summary>
    public Task<long?> GetLastBackupAtAsync() => GetValueIntAsync(DatabaseSchema.SettingIds.LastBackupAt);

    /// <summary>UTC epoch ms of the last record or link deletion, or null if there never was one.</summary>
    public Task<long?> GetLastDeletionAtAsync() => GetValueIntAsync(DatabaseSchema.SettingIds.LastDeletionAt);

    private async Task<long?> GetValueIntAsync(int settingId) =>
        (await QueryAsync("SELECT valueInt FROM Settings WHERE settingId = $id;",
            r => r.IsDBNull(0) ? (long?)null : r.GetInt64(0), ("$id", settingId)).ConfigureAwait(false))
        .FirstOrDefault();

    internal static void UpsertLastBackupAt(SqliteConnection connection, long value, SqliteTransaction? transaction = null) =>
        Upsert(connection, DatabaseSchema.SettingIds.LastBackupAt, "lastBackupAt", value, transaction);

    /// <summary>Saves lastDeletionAt = now. Call inside the transaction that deletes.</summary>
    internal static void RecordDeletion(SqliteConnection connection, SqliteTransaction transaction) =>
        Upsert(connection, DatabaseSchema.SettingIds.LastDeletionAt, "lastDeletionAt", EpochTime.UtcNowMillis(), transaction);

    /// <summary>Inserts or replaces the whole row, like Room's @Upsert of a Settings entity.</summary>
    private static void Upsert(SqliteConnection connection, int settingId, string settingName, long valueInt, SqliteTransaction? transaction)
    {
        using var command = CreateCommand(connection,
            "INSERT INTO Settings (settingId, settingName, valueInt, valueText) VALUES ($id, $name, $valueInt, NULL) " +
            "ON CONFLICT(settingId) DO UPDATE SET settingName = excluded.settingName, valueInt = excluded.valueInt, valueText = excluded.valueText;",
            [("$id", settingId), ("$name", settingName), ("$valueInt", valueInt)],
            transaction);
        command.ExecuteNonQuery();
    }
}
