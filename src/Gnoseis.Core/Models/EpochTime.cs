/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Models;

/// <summary>
/// Every time Gnoseis stores is a UTC instant in epoch milliseconds, and is shown in the device's time
/// zone. Note days and times have their own rule (<see cref="Domain.NoteDates"/>).
/// </summary>
public static class EpochTime
{
    /// <summary>
    /// The current instant in UTC epoch milliseconds. Used for the system times a record keeps
    /// (createdAt / updatedAt) and for the Settings times.
    /// </summary>
    public static long UtcNowMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
