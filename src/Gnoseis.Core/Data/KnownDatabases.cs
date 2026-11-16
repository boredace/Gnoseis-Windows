/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gnoseis.Core.Data;

/// <summary>A database the user knows about: a name and its data folder (full path).</summary>
public sealed record KnownDatabase(string Name, string Folder);

/// <summary>
/// The user's list of databases (data folders), kept in databases.json for every copy of Gnoseis except a
/// portable one. The default folder is always in the list and can't be removed; no two entries share a
/// folder or a name (both compared ignoring case). One entry is the default ("Always start with this
/// database"), and <see cref="AskAtStartup"/> shows the list at each start instead.
/// </summary>
public sealed class KnownDatabases
{
    public const string FileName = "databases.json";

    /// <summary>The default folder's name until the user renames it.</summary>
    public const string DefaultFolderName = "My database";

    private readonly List<KnownDatabase> _entries = [];
    private string _defaultEntryFolder;

    private KnownDatabases(string defaultFolder)
    {
        DefaultFolder = Normalize(defaultFolder);
        _entries.Add(new KnownDatabase(DefaultFolderName, DefaultFolder));
        _defaultEntryFolder = DefaultFolder;
    }

    /// <summary>The list when there is no file: just the default folder, which is the default.</summary>
    public static KnownDatabases Create(string defaultFolder) => new(defaultFolder);

    /// <summary>The default folder, %LOCALAPPDATA%\Gnoseis.</summary>
    public string DefaultFolder { get; }

    public IReadOnlyList<KnownDatabase> Entries => _entries;

    /// <summary>The database opened at start-up when <see cref="AskAtStartup"/> is off.</summary>
    public KnownDatabase Default => Find(_defaultEntryFolder) ?? _entries[0];

    /// <summary>Show the list each time Gnoseis starts ("Always ask for a database at start-up").</summary>
    public bool AskAtStartup { get; set; }

    /// <summary>True when the file was there but couldn't be read or was damaged; the list is then the one for no file.</summary>
    public bool CouldNotBeRead { get; private init; }

    public bool IsDefaultFolder(string folder) => SameFolder(folder, DefaultFolder);

    public KnownDatabase? Find(string folder)
    {
        var normalized = Normalize(folder);
        return _entries.FirstOrDefault(e => string.Equals(e.Folder, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds a database; null when added, otherwise why not (a name or folder already in the list).</summary>
    public string? Add(string name, string folder)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return "Give the database a name.";
        }
        string normalized;
        try
        {
            normalized = Normalize(folder);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Not a valid folder: {folder}";
        }
        if (Find(normalized) is { } existing)
        {
            return $"This folder is already in the list as \"{existing.Name}\".";
        }
        if (NameTaken(name, except: null))
        {
            return $"There's already a database named \"{name}\".";
        }
        _entries.Add(new KnownDatabase(name, normalized));
        return null;
    }

    /// <summary>Adds a folder named after itself (a number added if the name is taken) unless it's already listed. True when added.</summary>
    public bool AddIfMissing(string folder)
    {
        if (Find(folder) != null)
        {
            return false;
        }
        var normalized = Normalize(folder);
        var baseName = NameFor(normalized);
        var name = baseName;
        for (var n = 2; NameTaken(name, except: null); n++)
        {
            name = $"{baseName} ({n})";
        }
        _entries.Add(new KnownDatabase(name, normalized));
        return true;
    }

    /// <summary>The name a folder gets when it's added without one: its own name, or the drive for a root folder.</summary>
    public static string NameFor(string folder)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : name;
    }

    /// <summary>Renames a database; null when renamed, otherwise why not.</summary>
    public string? Rename(string folder, string newName)
    {
        var entry = Find(folder) ?? throw new ArgumentException("Not in the list.", nameof(folder));
        newName = newName.Trim();
        if (newName.Length == 0)
        {
            return "Give the database a name.";
        }
        if (NameTaken(newName, except: entry))
        {
            return $"There's already a database named \"{newName}\".";
        }
        _entries[_entries.IndexOf(entry)] = entry with { Name = newName };
        return null;
    }

    /// <summary>Takes a database off the list (its files stay). The default folder can't be removed; false then.</summary>
    public bool Remove(string folder)
    {
        var entry = Find(folder);
        if (entry == null || IsDefaultFolder(entry.Folder))
        {
            return false;
        }
        _entries.Remove(entry);
        if (SameFolder(_defaultEntryFolder, entry.Folder))
        {
            _defaultEntryFolder = DefaultFolder;
        }
        return true;
    }

    public void SetDefault(string folder)
    {
        var entry = Find(folder) ?? throw new ArgumentException("Not in the list.", nameof(folder));
        _defaultEntryFolder = entry.Folder;
    }

    private bool NameTaken(string name, KnownDatabase? except) =>
        _entries.Any(e => e != except && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A full path without a trailing separator (a root keeps its own), for comparing and storing.</summary>
    public static string Normalize(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    public static bool SameFolder(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- The file

    private sealed class FileModel
    {
        [JsonPropertyName("askAtStartup")]
        public bool AskAtStartup { get; set; }

        [JsonPropertyName("defaultFolder")]
        public string? DefaultFolder { get; set; }

        [JsonPropertyName("databases")]
        public List<FileEntry>? Databases { get; set; }
    }

    private sealed class FileEntry
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("folder")]
        public string? Folder { get; set; }
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(new FileModel
    {
        AskAtStartup = AskAtStartup,
        DefaultFolder = Default.Folder,
        Databases = _entries.Select(e => new FileEntry { Name = e.Name, Folder = e.Folder }).ToList(),
    }, WriteOptions);

    /// <summary>
    /// Reads the file's text (null: no file). A damaged file (not JSON, an entry without a name or a
    /// full folder, a repeated name or folder) gives the list for no file, marked <see cref="CouldNotBeRead"/>.
    /// </summary>
    public static KnownDatabases FromJson(string? json, string defaultFolder)
    {
        if (json == null)
        {
            return Create(defaultFolder);
        }
        try
        {
            var model = JsonSerializer.Deserialize<FileModel>(json) ?? throw new JsonException("Empty.");
            var list = new KnownDatabases(defaultFolder) { AskAtStartup = model.AskAtStartup };
            list._entries.Clear();
            foreach (var entry in model.Databases ?? [])
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Folder) || !Path.IsPathFullyQualified(entry.Folder))
                {
                    throw new JsonException("An entry without a name or a full folder.");
                }
                if (list.Find(entry.Folder) != null || list.NameTaken(entry.Name.Trim(), except: null))
                {
                    throw new JsonException("A repeated name or folder.");
                }
                list._entries.Add(new KnownDatabase(entry.Name.Trim(), Normalize(entry.Folder)));
            }

            // The default folder is always listed.
            if (list.Find(list.DefaultFolder) == null)
            {
                var name = DefaultFolderName;
                for (var n = 2; list.NameTaken(name, except: null); n++)
                {
                    name = $"{DefaultFolderName} ({n})";
                }
                list._entries.Insert(0, new KnownDatabase(name, list.DefaultFolder));
            }
            var defaultEntry = string.IsNullOrWhiteSpace(model.DefaultFolder) ? null : list.Find(model.DefaultFolder);
            list._defaultEntryFolder = defaultEntry?.Folder ?? list.DefaultFolder;
            return list;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new KnownDatabases(defaultFolder) { CouldNotBeRead = true };
        }
    }

    /// <summary>Reads databases.json; a missing file gives the list for no file, an unreadable one the same marked <see cref="CouldNotBeRead"/>.</summary>
    public static KnownDatabases Load(string path, string defaultFolder)
    {
        string? json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            json = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new KnownDatabases(defaultFolder) { CouldNotBeRead = true };
        }
        return FromJson(json, defaultFolder);
    }

    /// <summary>Writes the whole file through a temporary file and a replace, so another copy never reads half of it.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, ToJson());
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
