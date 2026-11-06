/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>The contact and note pages' layouts, by the width of the record pane.</summary>
public enum RecordPageBand
{
    /// <summary>Under 860: one column, with tabs under the header.</summary>
    S,

    /// <summary>860–1099: the main column and a side column of 240.</summary>
    M,

    /// <summary>1100–1699: the main column up to 760 wide and a side column of 320.</summary>
    L,

    /// <summary>1700–2599: as L, with a reading pane on the contact page.</summary>
    XL,

    /// <summary>From 2600: content capped at 2700, the side column in two columns.</summary>
    XXL,
}

/// <summary>What the contact and note pages share: the band from the pane width and the side column's rows.</summary>
public static class RecordPageLayout
{
    public const double MediumWidth = 860;
    public const double LargeWidth = 1100;
    public const double ExtraLargeWidth = 1700;
    public const double WidestWidth = 2600;

    /// <summary>The layout for a record pane of <paramref name="paneWidth"/> effective pixels.</summary>
    public static RecordPageBand BandFor(double paneWidth) => paneWidth switch
    {
        < MediumWidth => RecordPageBand.S,
        < LargeWidth => RecordPageBand.M,
        < ExtraLargeWidth => RecordPageBand.L,
        < WidestWidth => RecordPageBand.XL,
        _ => RecordPageBand.XXL,
    };

    /// <summary>Rows a side column section shows before "Show all": 5 in S, M and L, 6 in XL, 8 in XXL.</summary>
    public static int SideRowCount(RecordPageBand band) => band switch
    {
        RecordPageBand.XL => 6,
        RecordPageBand.XXL => 8,
        _ => 5,
    };

    /// <summary>
    /// A side column section's rows: latest linked first, links without a time last, then by name
    /// (a contact by last and first name, an untitled note by its first line).
    /// </summary>
    public static IReadOnlyList<LinkedRecordSummary> SideOrder(IEnumerable<LinkedRecordSummary> records) =>
        records
            .OrderBy(r => r.LinkedAt == null)
            .ThenByDescending(r => r.LinkedAt ?? 0)
            .ThenBy(SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

    private static string SortKey(LinkedRecordSummary record) =>
        record.SortName.Length > 0 ? record.SortName
        : record.Name.Length > 0 ? record.Name
        : record.FirstLine;
}
