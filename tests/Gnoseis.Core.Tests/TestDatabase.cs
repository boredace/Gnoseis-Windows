/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;
using Microsoft.Data.Sqlite;

namespace Gnoseis.Core.Tests;

/// <summary>A database file in its own temporary folder, deleted after the test.</summary>
public sealed class TestDatabase : IDisposable
{
    public TestDatabase()
    {
        Folder = Path.Combine(Path.GetTempPath(), "GnoseisTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        FilePath = Path.Combine(Folder, DatabaseSchema.FileName);
    }

    public string Folder { get; }
    public string FilePath { get; }

    public AppContainer Open() => new(GnoseisDatabase.Open(FilePath));

    public string PathFor(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Runs SQL against a file outside of GnoseisDatabase (e.g. to build an "Android" file).</summary>
    public static void ExecuteRaw(string path, params string[] statements)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        foreach (var sql in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    public static object? ScalarRaw(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
