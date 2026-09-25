/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;

namespace Gnoseis.App.Resources;

/// <summary>Shared data templates (a class, so the templates can use compiled x:Bind).</summary>
public sealed partial class RecordTemplates : ResourceDictionary
{
    public RecordTemplates()
    {
        InitializeComponent();
    }
}
