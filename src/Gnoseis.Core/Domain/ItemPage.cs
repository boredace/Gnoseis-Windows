/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>
/// What the item page shows that doesn't depend on the UI: the header line, copy text, and the
/// side column's type order.
/// </summary>
public static class ItemPage
{
    /// <summary>The side column's types, in their fixed order.</summary>
    public static IReadOnlyList<RecordType> SideTypes { get; } =
        [RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item];

    /// <summary>
    /// The line under the name: "Item · 26 notes · 16 linked".
    /// Singular forms for count 1. Each part after the type is left out when its count is 0.
    /// When both are 0: "Item · no notes or links yet".
    /// </summary>
    public static (string Text, bool IsPlaceholder) HeaderLine(int noteCount, int linkedCount)
    {
        var parts = new List<string>();
        parts.Add(RecordType.Item.DisplayName());

        if (noteCount > 0)
        {
            parts.Add($"{noteCount} {(noteCount == 1 ? "note" : "notes")}");
        }

        if (linkedCount > 0)
        {
            parts.Add($"{linkedCount} linked");
        }

        if (noteCount == 0 && linkedCount == 0)
        {
            return ("Item · no notes or links yet", true);
        }

        return (string.Join(" · ", parts), false);
    }

    /// <summary>
    /// Copy item details: the name, then an empty line and the comments when there are any.
    /// </summary>
    public static string CopyText(Item item)
    {
        var lines = new List<string>();
        var name = item.ItemName?.Trim() ?? "";
        if (name.Length > 0)
        {
            lines.Add(name);
        }
        var comments = item.Comments?.Trim();
        if (!string.IsNullOrWhiteSpace(comments))
        {
            lines.Add("");
            lines.Add(comments);
        }
        return string.Join(Environment.NewLine, lines);
    }
}
