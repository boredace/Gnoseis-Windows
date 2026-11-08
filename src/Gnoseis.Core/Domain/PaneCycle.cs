/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Domain;

/// <summary>The parts of the window that F6 and Shift+F6 move the focus between, in order.</summary>
public enum Pane
{
    /// <summary>The title bar's search box.</summary>
    Search,

    /// <summary>The navigation pane.</summary>
    Navigation,

    /// <summary>The section's list, or the trail column in its place.</summary>
    List,

    /// <summary>The record shown (or the page shown when there is no list).</summary>
    Record,
}

/// <summary>Which pane F6 (forwards) or Shift+F6 (backwards) moves the focus to.</summary>
public static class PaneCycle
{
    private static readonly Pane[] Order = [Pane.Search, Pane.Navigation, Pane.List, Pane.Record];

    /// <param name="current">The pane that has the focus, or null when the focus is in none of them.</param>
    /// <param name="backwards">Shift+F6.</param>
    /// <param name="showing">The panes showing; the others are skipped.</param>
    /// <returns>The next showing pane, wrapping around; null when none is showing.</returns>
    public static Pane? Next(Pane? current, bool backwards, IReadOnlyCollection<Pane> showing)
    {
        var step = backwards ? -1 : 1;
        // From outside every pane, F6 starts at the first and Shift+F6 at the last.
        var start = current is { } pane ? Array.IndexOf(Order, pane) : backwards ? Order.Length : -1;
        for (var i = 1; i <= Order.Length; i++)
        {
            var candidate = Order[((start + step * i) % Order.Length + Order.Length) % Order.Length];
            if (showing.Contains(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
