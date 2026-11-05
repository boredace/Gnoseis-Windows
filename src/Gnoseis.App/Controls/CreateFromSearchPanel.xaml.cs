/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Controls;

/// <summary>
/// Offers to create a record named after a search query, one button per record type: in place of
/// the results when there are none, after them when none has exactly that name. Shown on the search
/// results page and in the link picker; hidden when there is nothing to offer.
/// </summary>
public sealed partial class CreateFromSearchPanel : UserControl
{
    private string _query = "";
    private CreateFromSearchOffer _offer;
    private IReadOnlyList<RecordType> _types = [];

    public CreateFromSearchPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Raised when the user picks a type, with that type.</summary>
    public event EventHandler<RecordType>? CreateRequested;

    /// <summary>The trimmed query the offer is for.</summary>
    public string Query => _query;

    /// <summary>
    /// Shows a finished search's offer in a page's two panels: <paramref name="inPlaceOfList"/> (in the
    /// list's place, with no results) and <paramref name="afterResults"/> (the list's footer). An empty
    /// list puts its footer at the bottom, so the no-results case needs its own panel. Returns true when
    /// the list should be hidden.
    /// </summary>
    public static bool ShowOffer(CreateFromSearchPanel inPlaceOfList, CreateFromSearchPanel afterResults,
        string query, CreateFromSearchOffer offer, IReadOnlyList<RecordType> types)
    {
        inPlaceOfList.Show(query, offer == CreateFromSearchOffer.NoResults ? offer : CreateFromSearchOffer.None, types);
        afterResults.Show(query, offer == CreateFromSearchOffer.AfterResults ? offer : CreateFromSearchOffer.None, types);
        return offer == CreateFromSearchOffer.NoResults;
    }

    /// <summary>Shows the offer for a finished search, or hides the panel for <see cref="CreateFromSearchOffer.None"/>.</summary>
    public void Show(string query, CreateFromSearchOffer offer, IReadOnlyList<RecordType> types)
    {
        query = query.Trim();
        if (offer == _offer && query == _query && types.SequenceEqual(_types))
        {
            return;
        }
        _query = query;
        _offer = offer;
        _types = types;

        Visibility = offer == CreateFromSearchOffer.None ? Visibility.Collapsed : Visibility.Visible;
        if (offer == CreateFromSearchOffer.None)
        {
            return;
        }

        HeadingText.Text = offer == CreateFromSearchOffer.NoResults ? $"No matches for \"{query}\"" : "Not what you're looking for?";
        PromptText.Text = $"Create \"{query}\" as:";
        TypeButtons.ItemsSource = types.Select(CreateButton).ToList();
    }

    private Button CreateButton(RecordType type)
    {
        var name = type.DisplayName();
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = Glyphs.For(type), FontSize = 16 });
        content.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });

        var button = new Button
        {
            Content = content,
            Style = RecordTypeStyles.Button(type),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = type,
        };
        AutomationProperties.SetName(button, $"Create {name.ToLowerInvariant()} {_query}");
        ToolTipService.SetToolTip(button, $"Create {name.ToLowerInvariant()} \"{_query}\"");
        button.Click += (_, _) => CreateRequested?.Invoke(this, type);
        return button;
    }
}
