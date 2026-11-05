/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Views;

/// <summary>The fields of a note: title, text and day.</summary>
public sealed partial class NoteFields : UserControl, IEditFields
{
    public NoteFields(NoteEditViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public NoteEditViewModel ViewModel { get; }

    public void FocusFirst() => TitleBox.FocusText();
}
