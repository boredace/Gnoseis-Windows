/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gnoseis.App.Views;

/// <summary>Placeholder for a section's detail pane when no record is selected.</summary>
public sealed partial class EmptyDetailPage : Page
{
    public EmptyDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is RecordListViewModel list)
        {
            Tile.Background = TypeColors.BrushFor(list.Type);
            TileIcon.Glyph = list.Glyph;
            MessageText.Text = list.NoSelectionText;
            HintText.Text = $"or press Ctrl+N to create a new {list.TypeNameLower}";
        }
    }
}
