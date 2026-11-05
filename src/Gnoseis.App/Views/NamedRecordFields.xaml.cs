/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Views;

/// <summary>The fields of an organization, category or item: a name and a description.</summary>
public sealed partial class NamedRecordFields : UserControl, IEditFields
{
    public NamedRecordFields(NamedRecordEditViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public NamedRecordEditViewModel ViewModel { get; }

    public void FocusFirst() => NameBox.FocusText();

    public void FocusPrefilled() => NameBox.FocusText(caretAtEnd: true);
}
