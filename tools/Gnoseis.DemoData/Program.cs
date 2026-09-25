/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Diagnostics;
using Gnoseis.Core.Data;
using Gnoseis.DemoData;
using Microsoft.Data.Sqlite;

// Creates a demo "gnoseis_data" file: the knowledge base of an independent IT consultant.
//
//   dotnet run --project tools/Gnoseis.DemoData -- [folder] [--count 10000] [--seed 42] [--force]
//
// The default folder is %LOCALAPPDATA%\Gnoseis-Demo. The real data folder (%LOCALAPPDATA%\Gnoseis)
// is refused, so a demo run can never overwrite real data.

var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gnoseis-Demo");
var count = 10_000;
var seed = 42;
var force = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--count" when i + 1 < args.Length && int.TryParse(args[i + 1], out var c) && c > 0:
            count = c;
            i++;
            break;
        case "--seed" when i + 1 < args.Length && int.TryParse(args[i + 1], out var s):
            seed = s;
            i++;
            break;
        case "--force":
            force = true;
            break;
        case "-h" or "--help" or "/?":
            Console.WriteLine("Usage: Gnoseis.DemoData [folder] [--count N] [--seed N] [--force]");
            Console.WriteLine($"  folder   where gnoseis_data is written (default {folder})");
            Console.WriteLine("  --count  records of each type: notes, contacts, organizations, categories, items (default 10000)");
            Console.WriteLine("  --seed   random seed; the same seed gives the same data (default 42)");
            Console.WriteLine("  --force  replace an existing demo database in the folder");
            return 0;
        case var arg when !arg.StartsWith('-'):
            folder = Path.GetFullPath(arg);
            break;
        default:
            Console.Error.WriteLine($"Unknown or invalid argument: {args[i]}. Use --help.");
            return 2;
    }
}

var realFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gnoseis");
if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), realFolder, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Refusing to write into {realFolder}: that is where the app keeps your real data.");
    return 1;
}

var file = Path.Combine(folder, DatabaseSchema.FileName);
if (File.Exists(file))
{
    if (!force)
    {
        Console.Error.WriteLine($"{file} already exists. Use --force to replace it.");
        return 1;
    }
    foreach (var path in new[] { file, file + "-journal", file + "-wal", file + "-shm" })
    {
        File.Delete(path);
    }
}

var stopwatch = Stopwatch.StartNew();
Console.WriteLine($"Generating {count:N0} records of each type (seed {seed})...");
var data = new DemoDataGenerator(count, seed, DateOnly.FromDateTime(DateTime.Now)).Generate();

var database = GnoseisDatabase.Open(file);
DemoDataWriter.Write(database, data);
GnoseisDatabase.ReleaseAllConnections();

Console.WriteLine();
Console.WriteLine($"  Notes          {data.Notes.Count,8:N0}");
Console.WriteLine($"  Contacts       {data.Contacts.Count,8:N0}");
Console.WriteLine($"  Organizations  {data.Organizations.Count,8:N0}");
Console.WriteLine($"  Categories     {data.Categories.Count,8:N0}");
Console.WriteLine($"  Items          {data.Items.Count,8:N0}");
Console.WriteLine($"  Links          {data.Links.Count,8:N0}");
Console.WriteLine();
Console.WriteLine($"Wrote {file} ({new FileInfo(file).Length / 1024.0 / 1024.0:0.0} MB) in {stopwatch.Elapsed.TotalSeconds:0.0} s.");
Console.WriteLine();
Console.WriteLine("To open it in Gnoseis for Windows, start the app with:");
Console.WriteLine($"  $env:GNOSEIS_DATA_FOLDER = \"{folder}\"");
return 0;

internal static class DemoDataWriter
{
    /// <summary>Inserts all records in one transaction, replacing the welcome note of the new file.</summary>
    public static void Write(GnoseisDatabase database, GeneratedData data)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        Execute(connection, transaction, "DELETE FROM Note;");

        Insert(connection, transaction,
            "INSERT INTO Note (id, ownerDbId, title, textContents, createDateTime, createDate) VALUES ($1, $2, $3, $4, $5, $6);",
            data.Notes, n => [n.Id, n.OwnerDbId, n.Title, n.TextContents, n.CreateDateTime, n.CreateDate]);
        Insert(connection, transaction,
            "INSERT INTO Contact (id, ownerDbId, nameLast, nameFirst, jobTitle, company, phoneMain, phoneMobile, phoneHome, phoneWork, " +
            "emailMain, emailMobile, emailHome, emailWork, comments) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15);",
            data.Contacts, c => [c.Id, c.OwnerDbId, c.NameLast, c.NameFirst, c.JobTitle, c.Company, c.PhoneMain, c.PhoneMobile,
                c.PhoneHome, c.PhoneWork, c.EmailMain, c.EmailMobile, c.EmailHome, c.EmailWork, c.Comments]);
        Insert(connection, transaction,
            "INSERT INTO Organization (id, ownerDbId, organizationName, comments) VALUES ($1, $2, $3, $4);",
            data.Organizations, o => [o.Id, o.OwnerDbId, o.OrganizationName, o.Comments]);
        Insert(connection, transaction,
            "INSERT INTO Category (id, ownerDbId, parentId, categoryName, comments) VALUES ($1, $2, $3, $4, $5);",
            data.Categories, c => [c.Id, c.OwnerDbId, c.ParentId, c.CategoryName, c.Comments]);
        Insert(connection, transaction,
            "INSERT INTO Item (id, ownerDbId, parentId, itemName, comments) VALUES ($1, $2, $3, $4, $5);",
            data.Items, i => [i.Id, i.OwnerDbId, i.ParentId, i.ItemName, i.Comments]);
        Insert(connection, transaction,
            "INSERT INTO LinkedRecord (id, ownerDbId, record1Id, record2Id, record1TypeId, record2TypeId) VALUES ($1, $2, $3, $4, $5, $6);",
            data.Links, l => [l.Id, l.OwnerDbId, l.Record1Id, l.Record2Id, l.Record1TypeId, l.Record2TypeId]);

        transaction.Commit();

        var check = Scalar(connection, "PRAGMA integrity_check;");
        if (!string.Equals(check as string, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Integrity check failed: {check}");
        }
    }

    private static void Insert<T>(SqliteConnection connection, SqliteTransaction transaction, string sql,
        IEnumerable<T> rows, Func<T, object?[]> values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var parameters = Enumerable.Range(1, sql.Count(ch => ch == '$'))
            .Select(n => command.Parameters.Add(new SqliteParameter($"${n}", null)))
            .ToArray();
        command.Prepare();

        foreach (var row in rows)
        {
            var rowValues = values(row);
            for (var i = 0; i < parameters.Length; i++)
            {
                parameters[i].Value = rowValues[i] ?? DBNull.Value;
            }
            command.ExecuteNonQuery();
        }
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
