/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;

namespace Gnoseis.App.Helpers;

/// <summary>The PC's clock for time pickers: 24-hour when Windows' short time format uses 24-hour hours.</summary>
public static class SystemClock
{
    /// <summary>"24HourClock" or "12HourClock", for <c>TimePicker.ClockIdentifier</c>.</summary>
    public static string Identifier =>
        CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H') ? "24HourClock" : "12HourClock";
}
