/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Gnoseis.App.Services;

/// <summary>
/// Notices when Windows' time zone (or its daylight saving setting) changes while the app runs, so note
/// times move to the new zone without a restart. .NET caches <see cref="TimeZoneInfo.Local"/>; the cache is
/// cleared and the zone compared each time the window is activated (changing the zone means going to
/// Windows' settings and back) and once a minute (when the window stays in the background).
/// </summary>
public sealed class TimeZoneWatcher
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Action _changed;
    private readonly DispatcherQueueTimer _timer;
    private string _zone;

    /// <param name="window">The main window, whose activation triggers a check.</param>
    /// <param name="changed">Called on the UI thread after the zone changed.</param>
    public TimeZoneWatcher(Window window, Action changed)
    {
        _changed = changed;
        _zone = TimeZoneInfo.Local.ToSerializedString();
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

    private void Check()
    {
        TimeZoneInfo.ClearCachedData();
        var zone = TimeZoneInfo.Local.ToSerializedString();
        if (zone == _zone)
        {
            return;
        }
        _zone = zone;
        _changed();
    }
}
