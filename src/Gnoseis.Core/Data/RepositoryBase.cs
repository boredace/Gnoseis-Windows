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

/// <summary>Small helpers shared by the repositories. All database work runs off the UI thread.</summary>
public abstract class RepositoryBase(GnoseisDatabase database)
{
    protected GnoseisDatabase Database { get; } = database;

    protected Task<List<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters) =>
        Task.Run(() =>
        {
            using var connection = Database.OpenConnection();
            using var command = CreateCommand(connection, sql, parameters);
            using var reader = command.ExecuteReader();
            var results = new List<T>();
            while (reader.Read())
            {
                results.Add(map(reader));
            }
            return results;
        });

    protected async Task<T?> QuerySingleAsync<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
        where T : class =>
        (await QueryAsync(sql, map, parameters).ConfigureAwait(false)).FirstOrDefault();

    protected async Task<int> ExecuteAsync(RecordType changedType, string sql, params (string Name, object? Value)[] parameters)
    {
        var rows = await Task.Run(() =>
        {
            using var connection = Database.OpenConnection();
            using var command = CreateCommand(connection, sql, parameters);
            return command.ExecuteNonQuery();
        }).ConfigureAwait(false);
        Database.NotifyChanged(changedType);
        return rows;
    }

    internal static SqliteCommand CreateCommand(SqliteConnection connection, string sql, (string Name, object? Value)[] parameters, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    protected static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    protected static string GetString(SqliteDataReader reader, string column) =>
        GetNullableString(reader, column) ?? "";

    protected static long GetInt64(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt64(ordinal);
    }

    // ---------- Parent paths of categories and items

    /// <summary>Every row's parent and name, for building parent paths.</summary>
    internal static Dictionary<string, (string? ParentId, string Name)> LoadParentTree(SqliteConnection connection, RecordType type, string nameColumn)
    {
        var tree = new Dictionary<string, (string? ParentId, string Name)>(StringComparer.Ordinal);
        using var command = CreateCommand(connection, $"SELECT id, parentId, {nameColumn} FROM `{type}`;", []);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tree[reader.GetString(0)] = (reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2).Trim());
        }
        return tree;
    }

    /// <summary>
    /// The names of a record's parents, top first, joined by " › ". Stops at a missing parent or a loop;
    /// empty for a top-level record.
    /// </summary>
    internal static string ParentPath(Dictionary<string, (string? ParentId, string Name)> tree, string id, string? parentId)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { id };
        var current = string.IsNullOrEmpty(parentId) ? null : parentId;
        while (current != null && seen.Add(current) && tree.TryGetValue(current, out var parent))
        {
            if (parent.Name.Length > 0)
            {
                names.Add(parent.Name);
            }
            current = string.IsNullOrEmpty(parent.ParentId) ? null : parent.ParentId;
        }
        names.Reverse();
        return string.Join(" › ", names);
    }

    /// <summary>The parent path of a category or item (see <see cref="ParentPath"/>).</summary>
    protected Task<string> GetParentPathAsync(RecordType type, string nameColumn, string id, string? parentId) =>
        string.IsNullOrEmpty(parentId)
            ? Task.FromResult("")
            : Task.Run(() =>
            {
                using var connection = Database.OpenConnection();
                return ParentPath(LoadParentTree(connection, type, nameColumn), id, parentId);
            });

    // ---------- Mappers (column names are case-insensitive in SQLite and in GetOrdinal) ----------

    internal static Note MapNote(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        OwnerDbId = GetString(r, "ownerDbId"),
        Title = GetNullableString(r, "title"),
        TextContents = GetNullableString(r, "textContents"),
        CreateDateTime = GetInt64(r, "createDateTime"),
        CreateDate = GetInt64(r, "createDate"),
    };

    internal static Contact MapContact(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        OwnerDbId = GetString(r, "ownerDbId"),
        NameLast = GetString(r, "nameLast"),
        NameFirst = GetNullableString(r, "nameFirst"),
        JobTitle = GetNullableString(r, "jobTitle"),
        Company = GetNullableString(r, "company"),
        PhoneMain = GetNullableString(r, "phoneMain"),
        PhoneMobile = GetNullableString(r, "phoneMobile"),
        PhoneHome = GetNullableString(r, "phoneHome"),
        PhoneWork = GetNullableString(r, "phoneWork"),
        EmailMain = GetNullableString(r, "emailMain"),
        EmailMobile = GetNullableString(r, "emailMobile"),
        EmailHome = GetNullableString(r, "emailHome"),
        EmailWork = GetNullableString(r, "emailWork"),
        Comments = GetNullableString(r, "comments"),
    };

    internal static Organization MapOrganization(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        OwnerDbId = GetString(r, "ownerDbId"),
        OrganizationName = GetString(r, "organizationName"),
        Comments = GetNullableString(r, "comments"),
    };

    internal static Category MapCategory(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        OwnerDbId = GetString(r, "ownerDbId"),
        ParentId = GetNullableString(r, "parentId"),
        CategoryName = GetString(r, "categoryName"),
        Comments = GetNullableString(r, "comments"),
    };

    internal static Item MapItem(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        OwnerDbId = GetString(r, "ownerDbId"),
        ParentId = GetNullableString(r, "parentId"),
        ItemName = GetString(r, "itemName"),
        Comments = GetNullableString(r, "comments"),
    };
}
