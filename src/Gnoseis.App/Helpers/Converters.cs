/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;

namespace Gnoseis.App.Helpers;

/// <summary>Functions for x:Bind function bindings.</summary>
public static class Converters
{
    public static bool Not(bool value) => !value;

    public static Visibility VisibleWhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The style of a linked row's body line (see RecordTemplates.xaml).</summary>
    public static Style RowBodyStyle(bool primary) =>
        (Style)Application.Current.Resources[primary ? "RowBodyPrimaryTextStyle" : "RowBodyTextStyle"];

    /// <summary>"In trail" on a history row whose note is already in the section's trail, other than the record shown.</summary>
    public static Visibility InTrailNote(string noteId) =>
        Views.RecordPageParts.IsInTrail(new RecordRef(RecordType.Note, noteId)) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A dashed line's dash pattern (in stroke widths), or a solid line's.</summary>
    public static Microsoft.UI.Xaml.Media.DoubleCollection Dashes(bool dashed) => dashed ? [2, 2] : [];

    /// <summary>The title of a row in a section's trail (disabled-text colour for a dimmed row in high contrast).</summary>
    public static Style TrailTitleStyle(bool disabled) =>
        (Style)Application.Current.Resources[disabled ? "TrailTitleDisabledTextStyle" : "TrailTitleTextStyle"];

    /// <summary>The second line of a row in a section's trail.</summary>
    public static Style TrailSubtitleStyle(bool disabled) =>
        (Style)Application.Current.Resources[disabled ? "TrailSubtitleDisabledTextStyle" : "TrailSubtitleTextStyle"];

    /// <summary>A search group header: a type's in its outline colour, Recently changed in the body strong style.</summary>
    public static Style SearchGroupHeaderStyle(RecordType? type) =>
        type is { } t ? RecordTypeStyles.GroupHeader(t) : (Style)Application.Current.Resources["BodyStrongTextBlockStyle"];

    /// <summary>Tooltip text with its keyboard shortcut, e.g. "New note (Ctrl+N)".</summary>
    public static string Shortcut(string text, string keys) => $"{text} ({keys})";
}
