/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;

namespace Gnoseis.App.Controls;

/// <summary>A type picked in the create offer, with the query the new record is named after.</summary>
public sealed record CreateFromSearchRequest(RecordType Type, string Query);

/// <summary>
/// The search component shared by the search page and the link picker: box, filter chips and the
/// results of a <see cref="RecordSearchModel"/>. On the search page a result opens; in the picker
/// (the model has a link source) a click ticks or unticks it.
/// </summary>
public sealed partial class RecordSearchView : UserControl
{
    private RecordSearchModel? _model;
    private readonly List<(SearchChip Chip, ToggleButton Button, TextBlock Count)> _chips = [];

    public RecordSearchView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user opens a result (not in the picker).</summary>
    public event EventHandler<SearchRow>? RecordInvoked;

    /// <summary>Raised when the user picks a type in the create offer.</summary>
    public event EventHandler<CreateFromSearchRequest>? CreateRequested;

    public RecordSearchModel? Model
    {
        get => _model;
        set
        {
            if (ReferenceEquals(_model, value))
            {
                return;
            }
            if (_model != null)
            {
                _model.PropertyChanged -= Model_PropertyChanged;
            }
            _model = value;
            if (_model != null)
            {
                _model.PropertyChanged += Model_PropertyChanged;
            }
            BuildChips();
            Bindings.Update();
            ShowGroups();
            ShowCreateOffer();
            ShowMoreDots();
        }
    }

    /// <summary>The search box's placeholder, also its name for screen readers.</summary>
    public string PlaceholderText
    {
        get => QueryBox.PlaceholderText;
        set
        {
            QueryBox.PlaceholderText = value;
            AutomationProperties.SetName(QueryBox, value);
        }
    }

    /// <summary>The name screen readers give the results list.</summary>
    public string ResultsName
    {
        get => AutomationProperties.GetName(ResultList);
        set => AutomationProperties.SetName(ResultList, value);
    }

    /// <summary>Puts the cursor at the end of the search box.</summary>
    public void FocusSearchBox()
    {
        QueryBox.Focus(FocusState.Programmatic);
        if (FindTextBox(QueryBox) is { } box)
        {
            box.SelectionStart = box.Text.Length;
        }
    }

    private static TextBox? FindTextBox(DependencyObject parent)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if ((child as TextBox ?? FindTextBox(child)) is { } box)
            {
                return box;
            }
        }
        return null;
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RecordSearchModel.Groups):
                ShowGroups();
                break;
            case nameof(RecordSearchModel.CreateOffer):
                ShowCreateOffer();
                break;
            case nameof(RecordSearchModel.MoreTypes):
                ShowMoreDots();
                break;
        }
    }

    private void ShowGroups()
    {
        GroupsSource.Source = _model?.Groups ?? [];
        ResultList.ItemsSource = GroupsSource.View;
    }

    private void ShowCreateOffer()
    {
        var offer = _model?.CreateOffer ?? CreateOffer.None;
        var noResults = CreateFromSearchPanel.ShowOffer(NoResultsCreatePanel, CreatePanel, offer.Query, offer.Offer, _model?.CreateTypes ?? []);
        ResultList.Visibility = noResults ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowMoreDots()
    {
        MoreDots.Children.Clear();
        foreach (var type in _model?.MoreTypes ?? [])
        {
            MoreDots.Children.Add(new Ellipse { Width = 8, Height = 8, Style = RecordTypeStyles.Dot(type) });
        }
    }

    // ---------------------------------------------------------------- Chips

    private void BuildChips()
    {
        foreach (var (chip, _, _) in _chips)
        {
            chip.PropertyChanged -= Chip_PropertyChanged;
        }
        _chips.Clear();
        ChipsPanel.Children.Clear();
        if (_model == null)
        {
            return;
        }

        foreach (var chip in _model.Chips)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (chip.Type is { } type)
            {
                content.Children.Add(new FontIcon { Glyph = Glyphs.For(type), FontSize = 14, Style = RecordTypeStyles.Glyph(type) });
            }
            content.Children.Add(new TextBlock { Text = chip.Name });
            var count = new TextBlock { Opacity = 0.75 };
            content.Children.Add(count);

            var button = new ToggleButton
            {
                Content = content,
                Style = chip.Type is { } t ? RecordTypeStyles.Chip(t) : (Style)Resources["AllChipStyle"],
            };
            button.Click += (_, _) =>
            {
                _model?.ToggleChip(chip.Type);
                SyncChips();
            };
            chip.PropertyChanged += Chip_PropertyChanged;
            _chips.Add((chip, button, count));
            ChipsPanel.Children.Add(button);
        }
        SyncChips();
    }

    private void Chip_PropertyChanged(object? sender, PropertyChangedEventArgs e) => SyncChips();

    /// <summary>Shows each chip's state and count; a type without matches is shown with less emphasis.</summary>
    private void SyncChips()
    {
        foreach (var (chip, button, count) in _chips)
        {
            button.IsChecked = chip.IsSelected;
            count.Text = chip.Count?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "";
            count.Visibility = chip.Count == null ? Visibility.Collapsed : Visibility.Visible;
            button.Opacity = chip.Type != null && chip.Count == 0 ? 0.55 : 1;
            AutomationProperties.SetName(button, chip.AutomationName);
        }
    }

    private void ShowAllTypes_Click(object sender, RoutedEventArgs e)
    {
        _model?.ToggleChip(null);
        SyncChips();
    }

    // ---------------------------------------------------------------- Results

    private void ResultList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchRow row)
        {
            Invoke(row);
        }
    }

    private void BestMatch_Click(object sender, RoutedEventArgs e)
    {
        if (_model?.BestMatch is { } row)
        {
            Invoke(row);
        }
    }

    /// <summary>Opens a result, or in the picker ticks or unticks it.</summary>
    private void Invoke(SearchRow row)
    {
        if (_model is not { } model)
        {
            return;
        }
        if (model.IsPicker)
        {
            if (row.CanCheck)
            {
                model.SetChecked(row.Result, !row.IsChecked);
            }
            return;
        }
        RecordInvoked?.Invoke(this, row);
    }

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: SearchRow row } box)
        {
            _model?.SetChecked(row.Result, box.IsChecked == true);
        }
    }

    private void CreatePanel_CreateRequested(object? sender, RecordType type) =>
        CreateRequested?.Invoke(this, new CreateFromSearchRequest(type, ((CreateFromSearchPanel)sender!).Query));
}
