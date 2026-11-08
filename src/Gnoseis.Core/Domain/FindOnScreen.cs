/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Domain;

/// <summary>The box that "find on this screen" (Ctrl+F) focuses.</summary>
public enum FindTarget
{
    /// <summary>The record page's own search or filter box.</summary>
    RecordPageBox,

    /// <summary>The section list's filter box.</summary>
    ListFilterBox,

    /// <summary>No box to find in on this screen: search everything instead.</summary>
    SearchEverything,
}

/// <summary>Picks the box Ctrl+F focuses from where the focus is and which boxes are showing.</summary>
public static class FindOnScreen
{
    /// <param name="focusInRecordPane">The keyboard focus is inside the record shown next to the list.</param>
    /// <param name="recordFindBoxShowing">The record page shows its own search or filter box.</param>
    /// <param name="listFilterShowing">The section list, with its filter box, is showing.</param>
    public static FindTarget Choose(bool focusInRecordPane, bool recordFindBoxShowing, bool listFilterShowing)
    {
        if (focusInRecordPane && recordFindBoxShowing)
        {
            return FindTarget.RecordPageBox;
        }
        if (listFilterShowing)
        {
            return FindTarget.ListFilterBox;
        }
        return recordFindBoxShowing ? FindTarget.RecordPageBox : FindTarget.SearchEverything;
    }
}
