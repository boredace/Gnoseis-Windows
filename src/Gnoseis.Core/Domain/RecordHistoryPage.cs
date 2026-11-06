/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Domain;

/// <summary>
/// The columns of an item or organization page, in effective pixels: the People column (an
/// organization from band L), the history, the reading pane (0 when there is none) and the side column.
/// </summary>
public sealed record RecordHistoryColumns(double People, double History, double Reading, double Side, int SideColumnCount);

/// <summary>What the item and organization pages share that doesn't depend on the UI.</summary>
public static class RecordHistoryPage
{
    /// <summary>Space between columns.</summary>
    public const double Gap = 32;

    /// <summary>The widest the content gets, from band XXL.</summary>
    public const double MaxContentWidth = 2700;

    /// <summary>The name, then an empty line and the comments when there are any.</summary>
    public static string CopyText(string? name, string? comments)
    {
        var lines = new List<string>();
        var trimmedName = name?.Trim() ?? "";
        if (trimmedName.Length > 0)
        {
            lines.Add(trimmedName);
        }
        var trimmedComments = comments?.Trim() ?? "";
        if (trimmedComments.Length > 0)
        {
            lines.Add("");
            lines.Add(trimmedComments);
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The columns for <paramref name="content"/> pixels of content (the pane less its padding, at
    /// most 2700) in <paramref name="band"/>. An item has no People column; an organization has one
    /// from band L. The reading pane is there from XL when there are notes.
    /// </summary>
    public static RecordHistoryColumns Columns(RecordPageBand band, double content, bool hasNotes, bool isOrganization)
    {
        if (band == RecordPageBand.XXL)
        {
            content = Math.Min(content, MaxContentWidth);
        }
        static double Rest(double value) => Math.Max(0, value);

        if (!isOrganization)
        {
            return band switch
            {
                RecordPageBand.S => new(0, content, 0, 0, 1),
                RecordPageBand.M => new(0, Rest(content - 240 - Gap), 0, 240, 1),
                RecordPageBand.L => new(0, Rest(Math.Min(760, content - 320 - Gap)), 0, 320, 1),
                RecordPageBand.XL when hasNotes => new(0, 560, Rest(Math.Min(960, content - 560 - 320 - 2 * Gap)), 320, 1),
                RecordPageBand.XL => new(0, Rest(Math.Min(760, content - 320 - Gap)), 0, 320, 1),
                _ when hasNotes => new(0, 640, Rest(Math.Min(1100, content - 640 - 680 - 2 * Gap)), 680, 2),
                _ => new(0, Rest(Math.Min(760, content - 680 - Gap)), 0, 680, 2),
            };
        }

        return band switch
        {
            RecordPageBand.S => new(0, content, 0, 0, 1),
            RecordPageBand.M => new(0, Rest(content - 240 - Gap), 0, 240, 1),
            RecordPageBand.L => new(300, Rest(content - 300 - 260 - 2 * Gap), 0, 260, 1),
            RecordPageBand.XL when hasNotes => new(340, 560, Rest(Math.Min(720, content - 340 - 560 - 280 - 3 * Gap)), 280, 1),
            RecordPageBand.XL => new(340, Rest(Math.Min(760, content - 340 - 300 - 2 * Gap)), 0, 300, 1),
            _ when hasNotes => new(400, 640, Rest(Math.Min(1000, content - 400 - 640 - 320 - 3 * Gap)), 320, 1),
            _ => new(400, Rest(Math.Min(760, content - 400 - 320 - 2 * Gap)), 0, 320, 1),
        };
    }
}
