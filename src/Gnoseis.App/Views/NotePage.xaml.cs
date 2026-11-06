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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Gnoseis.App.Views;

/// <summary>
/// A note: what it is and when in the header, its text as the main column in a readable width,
/// and the records linked to it in a side column. The layout follows the width of the record pane
/// (see <see cref="RecordPageBand"/>).
/// </summary>
public sealed partial class NotePage : Page, IRecordPage
{
    private const double Gap = 32;
    private const double TopPadding = 20;
    private const double TextColumnWidth = 760;

    private const string NoteKey = "note";
    private const string LinkedKey = "linked";

    private readonly SideColumn _side;
    private RecordPageBand? _band;
    private double _textWidth;
    private double _sideWidth;
    private double _padding;
    private string _selectedTab = NoteKey;
    private bool _updatingTabs;

    public NotePage()
    {
        InitializeComponent();
        _side = new SideColumn(this, LinkAsync, UnlinkAsync, RebuildSections);
        HeaderBlock.SizeChanged += (_, _) => PositionSideColumn();
    }

    public NotePageViewModel ViewModel { get; private set; } = null!;

    public RecordRef Record => ViewModel.Record;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = new NotePageViewModel((RecordRef)e.Parameter);
        ViewModel.Loaded += ViewModel_Loaded;
        ViewModel.Activate();
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Deactivate();
        ViewModel.Loaded -= ViewModel_Loaded;
    }

    private void ViewModel_Loaded(object? sender, EventArgs e)
    {
        var available = ViewModel.IsAvailable;
        MainScroller.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        MissingState.Visibility = ViewModel.IsMissing ? Visibility.Visible : Visibility.Collapsed;
        Commands.IsEnabled = available;
        if (!available)
        {
            SideScroller.Visibility = Visibility.Collapsed;
            return;
        }

        ShowHeader();
        ShowText();
        _band = null;
        UpdatePageLayout();
    }

    // ---------------------------------------------------------------- Layout

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePageLayout();

    /// <summary>The columns' widths and places for the pane width; the parts that depend on the band are rebuilt when it changes.</summary>
    private void UpdatePageLayout()
    {
        if (ViewModel == null || !ViewModel.IsAvailable || ActualWidth <= 0)
        {
            return;
        }

        var pane = ActualWidth;
        var band = RecordPageLayout.BandFor(pane);
        _padding = pane >= 1400 ? 40 : 24;
        var content = Math.Max(0, pane - 2 * _padding);
        if (band == RecordPageBand.XXL)
        {
            content = Math.Min(content, 2700);
        }

        // The note is the reading: no reading pane, the space left over stays empty on the right.
        (_textWidth, _sideWidth) = band switch
        {
            RecordPageBand.S => (content, 0.0),
            RecordPageBand.M => (content - 240 - Gap, 240.0),
            RecordPageBand.L or RecordPageBand.XL => (Math.Min(TextColumnWidth, content - 320 - Gap), 320.0),
            _ => (Math.Min(TextColumnWidth, content - 680 - Gap), 680.0),
        };

        MainGrid.Width = content;
        MainGrid.Margin = new Thickness(_padding, TopPadding, _padding, 32);
        BodyPanel.Width = Math.Max(0, _textWidth);
        SideScroller.Width = _sideWidth;

        if (band != _band)
        {
            _band = band;
            var wide = band >= RecordPageBand.L;
            Commands.DefaultLabelPosition = wide ? CommandBarDefaultLabelPosition.Right : CommandBarDefaultLabelPosition.Collapsed;
            ShowTabs();
            ShowSideColumn();
        }
        PositionSideColumn();
    }

    /// <summary>The side column moves up with the header, then stays below the top of the pane while the text scrolls on.</summary>
    private void PositionSideColumn()
    {
        if (_band is not { } band || band == RecordPageBand.S || ViewModel == null || !ViewModel.IsAvailable)
        {
            SideScroller.Visibility = Visibility.Collapsed;
            return;
        }

        var top = TopPadding + HeaderBlock.ActualHeight + MainGrid.RowSpacing - MainScroller.VerticalOffset;
        top = Math.Max(top, MinimumColumnTop);
        SideScroller.Visibility = Visibility.Visible;
        SideScroller.Margin = new Thickness(_padding + _textWidth + Gap, top, 0, 0);
        SideScroller.Height = Math.Max(0, Root.ActualHeight - top);
    }

    /// <summary>The highest the side column goes when the header has scrolled away.</summary>
    private double MinimumColumnTop => 12;

    private void MainScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => PositionSideColumn();

    // ---------------------------------------------------------------- Header

    private void ShowHeader()
    {
        var (title, isPlaceholder) = ViewModel.Title;
        TitleText.Text = title;
        TitleText.FontWeight = isPlaceholder ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
        TitleText.Foreground = (Brush)Application.Current.Resources[isPlaceholder ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush"];
        DayText.Text = ViewModel.DayAndTime;
    }

    // ---------------------------------------------------------------- Text

    /// <summary>The text as stored, with clickable links; under it the created and changed caption.</summary>
    private void ShowText()
    {
        var hasText = ViewModel.HasText;
        RecordPageParts.ShowLinkedText(NoteText, hasText ? ViewModel.Text : "");
        NoteText.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

        var caption = ViewModel.TimesCaption;
        TimesText.Text = caption;
        CaptionPanel.Visibility = caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CaptionRule.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- Tabs (under 860 px)

    private void ShowTabs()
    {
        var tabs = _band == RecordPageBand.S;
        TabBar.Visibility = tabs ? Visibility.Visible : Visibility.Collapsed;
        if (!tabs)
        {
            _selectedTab = NoteKey;
            ShowSelectedTab();
            return;
        }

        _updatingTabs = true;
        try
        {
            TabBar.Items.Clear();
            TabBar.Items.Add(new SelectorBarItem { Text = "Note", Tag = NoteKey });
            TabBar.Items.Add(new SelectorBarItem { Text = $"Linked records ({ViewModel.Linked.TotalCount})", Tag = LinkedKey });
            TabBar.SelectedItem = TabBar.Items.First(i => (string)i.Tag == _selectedTab);
        }
        finally
        {
            _updatingTabs = false;
        }
        ShowSelectedTab();
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingTabs || sender.SelectedItem is not { Tag: string key })
        {
            return;
        }
        _selectedTab = key;
        ShowSelectedTab();
    }

    private void ShowSelectedTab()
    {
        NotePanel.Visibility = _selectedTab == NoteKey ? Visibility.Visible : Visibility.Collapsed;
        TabLinkedPanel.Visibility = _selectedTab == LinkedKey ? Visibility.Visible : Visibility.Collapsed;
        if (_selectedTab == LinkedKey)
        {
            TabLinkedPanel.Children.Clear();
            foreach (var section in CreateSections())
            {
                TabLinkedPanel.Children.Add(section);
            }
        }
    }

    // ---------------------------------------------------------------- Side column

    private void ShowSideColumn()
    {
        if (_band is not { } band || band == RecordPageBand.S)
        {
            SidePanel.Children.Clear();
            SidePanel.ColumnDefinitions.Clear();
            return;
        }
        SideColumn.Flow(SidePanel, CreateSections(), band);
    }

    /// <summary>Contacts, Organizations, Categories, Items, Notes; with no links, the link buttons.</summary>
    private List<FrameworkElement> CreateSections()
    {
        var sections = ViewModel.SideSections;
        if (sections.Count == 0)
        {
            return
            [
                SideColumn.NoLinksSection(
                [
                    ("Link a contact", () => LinkAsync([RecordType.Contact], null)),
                    ("Link an organization", () => LinkAsync([RecordType.Organization], null)),
                    ("Add to a category", () => LinkAsync([RecordType.Category], null)),
                    ("Link anything… (Ctrl+L)", () => LinkAsync(null, null)),
                ]),
            ];
        }
        return sections.Select(s => _side.Section(s.Type, s.Records, _band ?? RecordPageBand.L)).ToList();
    }

    private void RebuildSections()
    {
        ShowSideColumn();
        if (_band == RecordPageBand.S)
        {
            ShowTabs();
        }
    }

    // ---------------------------------------------------------------- Commands

    private async void Edit_Click(object sender, RoutedEventArgs e) =>
        await Navigator.ShowEditorAsync(new EditRequest(RecordType.Note, Record.Id));

    private async void Link_Click(object sender, RoutedEventArgs e) => await LinkAsync(null, null);

    /// <summary>The link picker: all types, or only <paramref name="types"/>' chips, with an optional query.</summary>
    private async Task LinkAsync(IReadOnlyList<RecordType>? types, string? query)
    {
        try
        {
            await LinkRecordsDialog.ShowAsync(Record, types, query);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't link records", ex);
        }
    }

    private void MoreMenu_Opening(object? sender, object e)
    {
        MoreMenu.Items.Clear();
        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "" }, KeyboardAcceleratorTextOverride = "Del" };
        delete.Click += async (_, _) => await DeleteWithConfirmationAsync();
        MoreMenu.Items.Add(delete);
    }

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Del deletes the note (with a confirmation), unless a text box has the focus.
        if (e.Key == VirtualKey.Delete && !RecordPageParts.IsTextInput(e.OriginalSource))
        {
            e.Handled = true;
            await DeleteWithConfirmationAsync();
        }
    }

    /// <summary>Asks, then deletes the note.</summary>
    public async Task DeleteWithConfirmationAsync()
    {
        if (ViewModel == null || !ViewModel.IsAvailable)
        {
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            "Delete this note?",
            $"\"{ViewModel.DisplayName}\"\n\n{ViewModel.DeleteConfirmationText}",
            "Delete");
        if (!confirmed)
        {
            return;
        }

        try
        {
            var id = Record.Id;
            await ViewModel.DeleteAsync();
            Navigator.RecordDeleted(id);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't delete", ex);
        }
    }

    // ---------------------------------------------------------------- Unlink and Undo

    private LinkedRecord? _removedLink;

    /// <summary>Unlinks without asking; the InfoBar offers Undo.</summary>
    private async Task UnlinkAsync(string recordId, string name)
    {
        try
        {
            var removed = await ViewModel.UnlinkAsync(recordId);
            if (removed == null)
            {
                return;
            }
            _removedLink = removed;
            UnlinkInfoBar.Message = $"Unlinked {name}";
            UnlinkInfoBar.IsOpen = false;
            UnlinkInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't unlink", ex);
        }
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_removedLink is not { } removed)
        {
            return;
        }
        _removedLink = null;
        UnlinkInfoBar.IsOpen = false;
        try
        {
            await ViewModel.UndoUnlinkAsync(removed);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't undo", ex);
        }
    }

    private void UnlinkInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason == InfoBarCloseReason.CloseButton)
        {
            _removedLink = null;
        }
    }
}
