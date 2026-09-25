/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace Gnoseis.App.Services;

/// <summary>A record the user opened recently (Home page).</summary>
public sealed record RecentRecord(RecordType Type, string Id);

/// <summary>
/// User preferences and window state, stored as JSON next to the database. The app is meant to stay
/// open all day, so it comes back exactly as the user left it.
/// </summary>
public sealed class AppSettings
{
    public const int MaxRecentRecords = 12;

    private string _path = AppPaths.SettingsFile;

    /// <summary>"Light", "Dark" or "Default" (follow Windows).</summary>
    public string Theme { get; set; } = nameof(ElementTheme.Default);

    /// <summary>"Home" or a record type name.</summary>
    public string LastSection { get; set; } = "Home";

    public bool IsPaneOpen { get; set; } = true;

    public WindowPlacement? Window { get; set; }

    public List<RecentRecord> RecentRecords { get; set; } = [];

    [JsonIgnore]
    public ElementTheme ElementTheme =>
        Enum.TryParse<ElementTheme>(Theme, out var theme) ? theme : ElementTheme.Default;

    /// <summary>Moves a record to the top of the recently opened list.</summary>
    public void AddRecent(RecordType type, string id)
    {
        RecentRecords.RemoveAll(r => r.Id == id);
        RecentRecords.Insert(0, new RecentRecord(type, id));
        if (RecentRecords.Count > MaxRecentRecords)
        {
            RecentRecords.RemoveRange(MaxRecentRecords, RecentRecords.Count - MaxRecentRecords);
        }
        Save();
    }

    public static AppSettings Load(string path)
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(path))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings are not worth failing over; start with the defaults.
        }

        settings ??= new AppSettings();
        settings.RecentRecords ??= [];
        settings._path = path;
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Preferences are best effort.
        }
    }
}

/// <summary>Window position and size in screen pixels.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);
