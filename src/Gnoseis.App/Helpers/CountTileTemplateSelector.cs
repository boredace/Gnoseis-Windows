/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Helpers;

/// <summary>Home's count strip: a type's tile (a button) or the Links tile (not clickable).</summary>
public sealed partial class CountTileTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TypeTemplate { get; set; }
    public DataTemplate? LinksTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is LinksCard ? LinksTemplate : TypeTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
