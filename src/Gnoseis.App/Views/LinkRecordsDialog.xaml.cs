/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.App.Controls;
using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Views;

/// <summary>
/// Picks existing records to link to a record (the Android Link Records page), with the same search as
/// the search page. Records ticked in one search stay ticked through later searches and chips, of any
/// types, until unticked or linked; Link links them all, shown or not.
/// </summary>
public sealed partial class LinkRecordsDialog : ContentDialog
{
    /// <summary>The selected strip shows this many ticked records' tiles.</summary>
    private const int StripTiles = 5;

    /// <summary>The dialog's title, padding and buttons around the content, and the cap on its height.</summary>
    private const double DialogChrome = 200;
    private const double MaxDialogHeight = 860;
    private const double ContentWidth = 720;

    // The new record to create after the dialog closes, when the user picked a type in the create offer.
    private EditRequest? _createAfterClose;

    private LinkRecordsDialog(RecordSearchModel model)
    {
        Model = model;
        InitializeComponent();
        Search.Model = model;
        model.SelectionChanged += (_, _) => ShowSelectedTiles();
        Opened += (_, _) =>
        {
            FitToWindow();
            XamlRoot.Changed += XamlRoot_Changed;
            Search.FocusSearchBox();
        };
        Closed += (_, _) =>
        {
            XamlRoot.Changed -= XamlRoot_Changed;
            model.Deactivate();
        };
    }

    public RecordSearchModel Model { get; }

    /// <summary>
    /// Shows the picker for a record and links what the user picks. When the user creates a record
    /// from the search instead, the ticked records are linked, the dialog closes and the editor opens
    /// for a new record linked to <paramref name="source"/>. With <paramref name="types"/>, only those
    /// types are searched and offered to create; <paramref name="query"/> starts the search.
    /// </summary>
    public static async Task ShowAsync(RecordRef source, IReadOnlyList<RecordType>? types = null, string? query = null)
    {
        var chipTypes = types ?? SearchLogic.ChipTypes(source.Type);
        var createTypes = types ?? CreateFromSearch.TypesFor(source.Type);
        var model = new RecordSearchModel(chipTypes, types?.Count == 1 ? types[0] : null, createTypes, source);
        var dialog = new LinkRecordsDialog(model);
        model.Activate();
        await model.LoadAsync();
        if (!string.IsNullOrWhiteSpace(query))
        {
            model.Query = query;
        }
        await Dialogs.ShowCustomAsync(dialog);
        if (dialog._createAfterClose is { } request)
        {
            await Navigator.ShowEditorAsync(request);
        }
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => FitToWindow();

    /// <summary>About 720 wide; the height follows the window, up to the dialog's cap.</summary>
    private void FitToWindow()
    {
        var size = XamlRoot.Size;
        Root.Width = Math.Max(320, Math.Min(ContentWidth, size.Width - 96));
        Root.Height = Math.Max(240, Math.Min(MaxDialogHeight, size.Height - 48) - DialogChrome);
    }

    /// <summary>The first ticked records' tiles, overlapping, in the order picked.</summary>
    private void ShowSelectedTiles()
    {
        SelectedTiles.Children.Clear();
        foreach (var (record, i) in Model.Selection.Picked.Take(StripTiles).Select((r, i) => (r, i)))
        {
            FrameworkElement tile = record.RecordType == RecordType.Contact
                ? new PersonPicture
                {
                    DisplayName = record.RecordTitle,
                    Style = (Style)Application.Current.Resources["ContactPictureStyle"],
                }
                : new TypeTile { Type = record.RecordType, CornerRadius = new CornerRadius(6), FontSize = 13 };
            tile.Width = 28;
            tile.Height = 28;
            tile.Margin = new Thickness(i == 0 ? 0 : -8, 0, 0, 0);
            Canvas.SetZIndex(tile, StripTiles - i);
            SelectedTiles.Children.Add(tile);
        }
        if (Model.Selection.IsEmpty)
        {
            SelectedFlyout.Hide();
        }
    }

    private void ShowSelectedButton_Click(object sender, RoutedEventArgs e) =>
        Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(ShowSelectedButton);

    private void SelectedFlyout_Opening(object? sender, object e) => SelectedList.ItemsSource = Model.SelectedRows();

    /// <summary>Unticking a record in the flyout removes it from the selection and the list.</summary>
    private void SelectedCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: SearchRow row } box && box.IsChecked != true)
        {
            Model.SetChecked(row.Result, false);
            SelectedList.ItemsSource = Model.SelectedRows();
        }
    }

    private async void LinkRecordsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await Model.LinkSelectedAsync();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            StatusFallback(ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>Links the ticked records, as Link would, then closes so the new record's editor can open.</summary>
    private async void Search_CreateRequested(object? sender, CreateFromSearchRequest request)
    {
        try
        {
            await Model.LinkSelectedAsync();
        }
        catch (Exception ex)
        {
            StatusFallback(ex.Message);
            return;
        }
        _createAfterClose = new EditRequest(request.Type, LinkFrom: Model.LinkSource, PrefillName: request.Query);
        Hide();
    }

    private void StatusFallback(string message) => Title = $"Couldn't link: {message}";
}
