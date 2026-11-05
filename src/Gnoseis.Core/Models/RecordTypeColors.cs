/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Models;

/// <summary>Light or dark app theme.</summary>
public enum ColorTheme
{
    Light,
    Dark,
}

/// <summary>
/// How a record type colour is used. Outline: glyphs, borders and the type's text accents.
/// Fill: tile, button and picture backgrounds. A glyph or initials on a fill use the outline colour.
/// </summary>
public enum ColorRole
{
    Outline,
    Fill,
}

/// <summary>An opaque sRGB colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>"#RRGGBB".</summary>
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// The record type colours, the same as Gnoseis for Android (ui/theme/Color.kt): an outline and a fill
/// per type, different in light and dark theme. These are the only colours that stand for a record type.
/// The app's theme resources (Resources/RecordTypeColors.xaml) hold the same values; a test checks they match.
/// </summary>
public static class RecordTypeColors
{
    private static readonly Dictionary<(RecordType, ColorTheme), (Rgb Outline, Rgb Fill)> Table = new()
    {
        [(RecordType.Note, ColorTheme.Light)] = (Rgb.FromHex(0x38641C), Rgb.FromHex(0xDBEFCB)),
        [(RecordType.Contact, ColorTheme.Light)] = (Rgb.FromHex(0x7C560D), Rgb.FromHex(0xFFE7CA)),
        [(RecordType.Organization, ColorTheme.Light)] = (Rgb.FromHex(0x96333F), Rgb.FromHex(0xFFCECF)),
        [(RecordType.Category, ColorTheme.Light)] = (Rgb.FromHex(0x235198), Rgb.FromHex(0xD0D9FF)),
        [(RecordType.Item, ColorTheme.Light)] = (Rgb.FromHex(0x00646B), Rgb.FromHex(0xAFECF3)),

        [(RecordType.Note, ColorTheme.Dark)] = (Rgb.FromHex(0xAACD90), Rgb.FromHex(0x324D21)),
        [(RecordType.Contact, ColorTheme.Dark)] = (Rgb.FromHex(0xE3BA82), Rgb.FromHex(0x553C12)),
        [(RecordType.Organization, ColorTheme.Dark)] = (Rgb.FromHex(0xFFAAAD), Rgb.FromHex(0x6B2D32)),
        [(RecordType.Category, ColorTheme.Dark)] = (Rgb.FromHex(0xACBFFF), Rgb.FromHex(0x293E6C)),
        [(RecordType.Item, ColorTheme.Dark)] = (Rgb.FromHex(0x62DEEA), Rgb.FromHex(0x004349)),
    };

    /// <summary>The colour of a user record type (see <see cref="RecordTypeExtensions.UserTypes"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The type has no colour.</exception>
    public static Rgb Get(RecordType type, ColorTheme theme, ColorRole role) =>
        Table.TryGetValue((type, theme), out var pair)
            ? role == ColorRole.Outline ? pair.Outline : pair.Fill
            : throw new ArgumentOutOfRangeException(nameof(type), type, "This record type has no colour.");
}
