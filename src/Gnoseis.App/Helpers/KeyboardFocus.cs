/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Gnoseis.App.Helpers;

/// <summary>Moving the keyboard focus for the app's shortcuts.</summary>
internal static class KeyboardFocus
{
    /// <summary>Focuses a text box or a search box by keyboard and selects its text.</summary>
    public static void FocusAndSelectAll(Control box)
    {
        box.Focus(FocusState.Keyboard);
        var text = box as TextBox ?? FindChild<TextBox>(box);
        text?.SelectAll();
    }

    /// <summary>The element that has the keyboard focus in the given tree, if any.</summary>
    public static DependencyObject? Focused(XamlRoot? root) =>
        root == null ? null : FocusManager.GetFocusedElement(root) as DependencyObject;

    /// <summary>Whether <paramref name="element"/> is <paramref name="ancestor"/> or inside it.</summary>
    public static bool IsInside(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether the element is showing: it and every parent are visible.</summary>
    public static bool IsShowing(UIElement element)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: Visibility.Collapsed })
            {
                return false;
            }
        }
        return element.XamlRoot != null;
    }

    public static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }
            if (FindChild<T>(child) is { } deeper)
            {
                return deeper;
            }
        }
        return null;
    }
}
