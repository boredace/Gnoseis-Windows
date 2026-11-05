/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.App.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Gnoseis.App.Helpers;

/// <summary>
/// Finds the record type styles and colours in Resources/RecordTypeColors.xaml.
/// Prefer the styles: their colours are theme resources, so they follow the theme of the element they are on.
/// </summary>
public static class RecordTypeStyles
{
    private static readonly AccessibilitySettings Accessibility = new();

    /// <summary>The type's tile style ("NoteTileStyle", …), or the uncoloured one for a type without a colour.</summary>
    public static Style Tile(RecordType type) => Find($"{KeyOf(type) ?? "Unknown"}TileStyle");

    /// <summary>The style of a type glyph shown without a tile: the type's outline colour.</summary>
    public static Style Glyph(RecordType type) => Find($"{KeyOf(type) ?? "Unknown"}GlyphStyle");

    /// <summary>The style of a button that creates a record of the type, or the default button style.</summary>
    public static Style Button(RecordType type) => Find(KeyOf(type) is { } key ? $"{key}ButtonStyle" : "DefaultButtonStyle");

    /// <summary>
    /// The style of a list group header: a record type's group in the type's outline colour,
    /// any other group (a day, a first letter) in the accent colour.
    /// </summary>
    public static Style GroupHeader(RecordType? type) =>
        Find(type is { } t && KeyOf(t) is { } key ? $"{key}GroupHeaderTextStyle" : "GroupHeaderTextStyle");

    /// <summary>True if the user has a high-contrast theme on.</summary>
    public static bool IsHighContrast => Accessibility.HighContrast;

    /// <summary>
    /// The type's brush for the theme <paramref name="element"/> shows (its ActualTheme, or high contrast),
    /// for code that can't use a theme resource, such as menus built in code: popups don't follow the
    /// page's theme. Rebuild when the element's ActualThemeChanged fires or high contrast changes.
    /// Null for a type without a colour.
    /// </summary>
    public static Brush? Brush(FrameworkElement element, RecordType type, ColorRole role)
    {
        if (KeyOf(type) is not { } key)
        {
            return null;
        }
        var themeKey = IsHighContrast ? "HighContrast" : element.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        var colors = Application.Current.Resources.MergedDictionaries.OfType<RecordTypeColorResources>().Single();
        var theme = (ResourceDictionary)colors.ThemeDictionaries[themeKey];
        return (Brush)theme[$"{key}{role}Brush"];
    }

    private static string? KeyOf(RecordType type) =>
        RecordTypeExtensions.UserTypes.Contains(type) ? type.ToString() : null;

    private static Style Find(string key) => (Style)Application.Current.Resources[key];
}
