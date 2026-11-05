/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Controls;

/// <summary>
/// A record type's tile: the type's fill, its glyph in the outline colour and a 1 px outline border.
/// The colours come from "&lt;Type&gt;TileStyle" (theme resources), so the tile follows the app's theme.
/// Set Width, Height, CornerRadius and FontSize (the glyph size) where it is used.
/// </summary>
public sealed partial class TypeTile : Control
{
    public static readonly DependencyProperty TypeProperty = DependencyProperty.Register(
        nameof(Type), typeof(RecordType), typeof(TypeTile), new PropertyMetadata(RecordType.Note, (d, _) => ((TypeTile)d).Update()));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(TypeTile), new PropertyMetadata(Glyphs.Note));

    public TypeTile()
    {
        Update();
    }

    public RecordType Type
    {
        get => (RecordType)GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    /// <summary>The type's glyph (set from <see cref="Type"/>).</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        private set => SetValue(GlyphProperty, value);
    }

    private void Update()
    {
        Style = RecordTypeStyles.Tile(Type);
        Glyph = Glyphs.For(Type);
    }
}
