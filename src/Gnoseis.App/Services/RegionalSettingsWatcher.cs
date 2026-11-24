/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Domain;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Gnoseis.App.Services;

/// <summary>
/// Notices when Windows' regional settings change while the app runs, or the day does, so shown dates
/// and times follow without a restart: the time zone (or its daylight saving setting), the regional
/// format (12- or 24-hour clock, short and long date, month-day and year-month order, AM/PM, first
/// day of the week), and today's date in the current zone (midnight, or a zone change into another
/// day). .NET caches the zone and the culture's formats; both caches are cleared and the settings
/// compared each time the window is activated (changing them means going to Windows' settings and
/// back) and once a minute (when the window stays in the background, and to move on after midnight).
/// </summary>
public sealed class RegionalSettingsWatcher
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Action _changed;
    private readonly DispatcherQueueTimer _timer;
    private string _zone;
    private string _formats;
    private DateOnly _today;

    /// <summary>
    /// Raised on the UI thread after a change, before the pages reload: for text and time pickers a
    /// page sets itself, outside its view model (a day and time button, a picker's clock).
    /// </summary>
    public static event EventHandler? Changed;

    /// <param name="window">The main window, whose activation triggers a check.</param>
    /// <param name="changed">Called on the UI thread after a change, to reload every page.</param>
    public RegionalSettingsWatcher(Window window, Action changed)
    {
        _changed = changed;
        (_zone, _formats, _today) = Current();
        window.Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                Check();
            }
        };
        _timer = window.DispatcherQueue.CreateTimer();
        _timer.Interval = Interval;
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private static (string Zone, string Formats, DateOnly Today) Current()
    {
        var zone = TimeZoneInfo.Local;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        return (zone.ToSerializedString(), DateText.FormatKey(CultureInfo.CurrentCulture), today);
    }

    private void Check()
    {
        // Clearing the culture's cache makes CurrentCulture a new object with Windows' current formats
        // (the app never sets the thread's culture), and clears the time zone's cache too.
        CultureInfo.CurrentCulture.ClearCachedData();
        TimeZoneInfo.ClearCachedData();
        var current = Current();
        if (current == (_zone, _formats, _today))
        {
            return;
        }
        (_zone, _formats, _today) = current;
        Changed?.Invoke(this, EventArgs.Empty);
        _changed();
    }
}
