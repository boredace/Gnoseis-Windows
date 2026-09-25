/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Navigation;

namespace Gnoseis.App.Views;

/// <summary>A record: its content, and all records linked to it.</summary>
public sealed partial class RecordDetailsPage : Page
{
    private const double TwoColumnWidth = 860;

    public RecordDetailsPage()
    {
        InitializeComponent();
    }

    public RecordDetailsViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = new RecordDetailsViewModel((RecordRef)e.Parameter);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        BuildNewLinkedMenu();
        ViewModel.Activate();
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Deactivate();
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordDetailsViewModel.Body) && ViewModel.IsNote)
        {
            ShowNoteText(ViewModel.Body);
        }
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Linked records to the right of the content when there is room, below it otherwise.
        var twoColumns = e.NewSize.Width >= TwoColumnWidth;
        SideColumn.Width = twoColumns ? new GridLength(320) : new GridLength(0);
        Grid.SetColumn(LinkedPanel, twoColumns ? 1 : 0);
        Grid.SetRow(LinkedPanel, twoColumns ? 0 : 1);
        ContentGrid.ColumnSpacing = twoColumns ? 24 : 0;
    }

    // ---------------------------------------------------------------- Note text with clickable links

    [GeneratedRegex(@"(https?://[^\s<>""]+|www\.[^\s<>""]+|[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,})")]
    private static partial Regex LinkPattern();

    private void ShowNoteText(string text)
    {
        NoteText.Blocks.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, line.Length == 0 ? 0 : 4) };
            var position = 0;
            foreach (Match match in LinkPattern().Matches(line))
            {
                if (match.Index > position)
                {
                    paragraph.Inlines.Add(new Run { Text = line[position..match.Index] });
                }
                var target = match.Value.TrimEnd('.', ',', ';', ':', ')', '!', '?');
                var address = target.Contains('@') && !target.Contains("://") ? "mailto:" + target
                    : target.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + target
                    : target;
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                {
                    var link = new Hyperlink { NavigateUri = uri };
                    link.Inlines.Add(new Run { Text = target });
                    paragraph.Inlines.Add(link);
                }
                else
                {
                    paragraph.Inlines.Add(new Run { Text = target });
                }
                position = match.Index + target.Length;
            }
            if (position < line.Length)
            {
                paragraph.Inlines.Add(new Run { Text = line[position..] });
            }
            NoteText.Blocks.Add(paragraph);
        }
    }

    // ---------------------------------------------------------------- Commands

    private void BuildNewLinkedMenu()
    {
        NewLinkedMenu.Items.Clear();
        foreach (var type in ViewModel.NewLinkedTypes)
        {
            var item = new MenuFlyoutItem
            {
                Text = $"New {type.DisplayName().ToLowerInvariant()}",
                Icon = new FontIcon { Glyph = Helpers.Glyphs.For(type) },
                Tag = type,
            };
            item.Click += NewLinked_Click;
            NewLinkedMenu.Items.Add(item);
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e) =>
        await Navigator.ShowEditorAsync(new EditRequest(ViewModel.Type, ViewModel.Record.Id));

    private async void LinkExisting_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await LinkRecordsDialog.ShowAsync(ViewModel.Record);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't link records", ex);
        }
    }

    private async void NewLinked_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: RecordType type })
        {
            await Navigator.ShowEditorAsync(new EditRequest(type, LinkFrom: ViewModel.Record));
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) => await DeleteWithConfirmationAsync();

    /// <summary>Asks, then deletes the record (also used by the Delete key in the list).</summary>
    public async Task DeleteWithConfirmationAsync()
    {
        if (!ViewModel.IsAvailable)
        {
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            $"Delete this {ViewModel.TypeName.ToLowerInvariant()}?",
            $"\"{ViewModel.Title}\"\n\n{ViewModel.DeleteConfirmationText}",
            "Delete");
        if (!confirmed)
        {
            return;
        }

        try
        {
            var id = ViewModel.Record.Id;
            await ViewModel.DeleteAsync();
            Navigator.RecordDeleted(id);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't delete", ex);
        }
    }

    // ---------------------------------------------------------------- Linked records

    private async void LinkedRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecordRow row)
        {
            await Navigator.OpenRecordAsync(row.ToRef());
        }
    }

    private void LinkedRow_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecordRow row)
        {
            return;
        }

        var open = new MenuFlyoutItem { Text = "Open", Icon = new FontIcon { Glyph = "" } };
        open.Click += async (_, _) => await Navigator.OpenRecordAsync(row.ToRef());
        var unlink = new MenuFlyoutItem { Text = "Remove link", Icon = new FontIcon { Glyph = "" } };
        unlink.Click += async (_, _) =>
        {
            var confirmed = await Dialogs.ConfirmAsync("Remove link?",
                $"\"{row.Title}\" will no longer be linked to this {ViewModel.TypeName.ToLowerInvariant()}. Neither record is deleted.",
                "Remove link");
            if (confirmed)
            {
                await ViewModel.UnlinkAsync(row);
            }
        };

        var menu = new MenuFlyout();
        menu.Items.Add(open);
        menu.Items.Add(unlink);
        if (args.TryGetPosition(sender, out var point))
        {
            menu.ShowAt(sender, point);
        }
        else
        {
            menu.ShowAt((FrameworkElement)sender);
        }
        args.Handled = true;
    }
}
