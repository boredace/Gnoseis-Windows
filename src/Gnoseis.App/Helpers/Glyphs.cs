/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Gnoseis.App.Helpers;

/// <summary>Segoe Fluent Icons glyphs used by the app.</summary>
public static class Glyphs
{
    public const string Home = "";
    public const string Note = "";          // QuickNote
    public const string Contact = "";       // Contact
    public const string Organization = "";  // Work
    public const string Category = "";      // Tag
    public const string Item = "";          // Package
    public const string Details = "";       // Document
    public const string Phone = "";
    public const string Mail = "";
    public const string Link = "";
    public const string Question = "";      // Unknown
    public const string Warning = "";

    public static string For(RecordType type) => type switch
    {
        RecordType.Note => Note,
        RecordType.Contact => Contact,
        RecordType.Organization => Organization,
        RecordType.Category => Category,
        RecordType.Item => Item,
        _ => Question,
    };
}

/// <summary>
/// One color per record type, used for icon tiles so record types can be told apart at a glance.
/// The colors are dark enough for a white glyph in both light and dark themes.
/// </summary>
public static class TypeColors
{
    private static readonly Dictionary<RecordType, SolidColorBrush> Brushes = [];

    public static Color ColorFor(RecordType type) => type switch
    {
        RecordType.Note => Color.FromArgb(0xFF, 0xB2, 0x6B, 0x00),         // amber
        RecordType.Contact => Color.FromArgb(0xFF, 0x0F, 0x6C, 0xBD),      // blue
        RecordType.Organization => Color.FromArgb(0xFF, 0x7A, 0x4F, 0xB0), // purple
        RecordType.Category => Color.FromArgb(0xFF, 0x10, 0x7C, 0x41),     // green
        RecordType.Item => Color.FromArgb(0xFF, 0x03, 0x7B, 0x80),         // teal
        _ => Color.FromArgb(0xFF, 0x60, 0x60, 0x60),
    };

    /// <summary>Brush for the type (call on the UI thread).</summary>
    public static SolidColorBrush BrushFor(RecordType type)
    {
        if (!Brushes.TryGetValue(type, out var brush))
        {
            brush = new SolidColorBrush(ColorFor(type));
            Brushes[type] = brush;
        }
        return brush;
    }
}
