/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Views;

/// <summary>The fields of a contact: name, work, phone, e-mail and comments.</summary>
public sealed partial class ContactFields : UserControl, IEditFields
{
    public ContactFields(ContactEditViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public ContactEditViewModel ViewModel { get; }

    public void FocusFirst() => FirstNameBox.FocusText();

    // The last name is the field a search always fills (the first name only with more than one word).
    public void FocusPrefilled() => LastNameBox.FocusText(caretAtEnd: true);

    /// <summary>Moves keyboard focus to a field by its name (JobTitle, Company, PhoneMain, EmailMain); the first field otherwise.</summary>
    public void FocusField(string field)
    {
        var box = field switch
        {
            nameof(Contact.JobTitle) => JobTitleBox,
            nameof(Contact.Company) => CompanyBox,
            nameof(Contact.PhoneMain) => PhoneMainBox,
            nameof(Contact.EmailMain) => EmailMainBox,
            _ => FirstNameBox,
        };
        box.FocusText(caretAtEnd: true);
    }

    // ---------------------------------------------------------------- Pairs side by side, or one column when narrow

    private const double OneColumnWidth = 480;

    private readonly Dictionary<FrameworkElement, (int Row, int Column)> _pairPositions = [];
    private bool? _isOneColumn;

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var oneColumn = e.NewSize.Width < OneColumnWidth;
        if (oneColumn == _isOneColumn)
        {
            return;
        }
        _isOneColumn = oneColumn;
        foreach (var grid in Root.Children.OfType<Grid>())
        {
            Reflow(grid, oneColumn);
        }
    }

    /// <summary>Puts the fields of a two-column grid in one column (row by row, left before right), or back.</summary>
    private void Reflow(Grid grid, bool oneColumn)
    {
        var rows = Math.Max(1, grid.RowDefinitions.Count);
        if (!_pairPositions.ContainsKey(grid))
        {
            _pairPositions[grid] = (rows, 0);
            foreach (var child in grid.Children.OfType<FrameworkElement>())
            {
                _pairPositions[child] = (Grid.GetRow(child), Grid.GetColumn(child));
            }
        }
        var originalRows = _pairPositions[grid].Row;

        grid.RowDefinitions.Clear();
        for (var i = 0; i < (oneColumn ? originalRows * 2 : originalRows); i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        grid.ColumnDefinitions[1].Width = oneColumn ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        grid.ColumnSpacing = oneColumn ? 0 : 16;
        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            var (row, column) = _pairPositions[child];
            Grid.SetRow(child, oneColumn ? row * 2 + column : row);
            Grid.SetColumn(child, oneColumn ? 0 : column);
        }
    }
}
