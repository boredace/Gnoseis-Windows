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

    /// <summary>Tooltip text with its keyboard shortcut, e.g. "New note (Ctrl+N)".</summary>
    public static string Shortcut(string text, string keys) => $"{text} ({keys})";
}
