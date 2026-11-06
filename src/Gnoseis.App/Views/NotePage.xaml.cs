/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
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
public sealed partial class NotePage : Page, IRecordPage, IConfirmLeave
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

        // A double-click on the text edits it in place (the text block handles the event itself).
        NoteText.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler(NoteText_DoubleTapped), handledEventsToo: true);
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
        ShowCompactHeader();
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
            ShowChips();
            ShowTabs();
            ShowSideColumn();
        }
        UpdateCompactHeader();
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
    private double MinimumColumnTop => CompactHeader.Visibility == Visibility.Visible ? CompactHeaderHeight + 12 : 12;

    private void MainScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateCompactHeader();
        PositionSideColumn();
    }

    // ---------------------------------------------------------------- Compact header and scrolling to the top

    private double CompactHeaderHeight => CompactHeader.ActualHeight > 0 ? CompactHeader.ActualHeight : 56;

    /// <summary>Shows the compact header once the full header has scrolled out of view.</summary>
    private void UpdateCompactHeader()
    {
        var show = ViewModel != null && ViewModel.IsAvailable && MainScroller.VerticalOffset > TopPadding + HeaderBlock.ActualHeight;
        CompactHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CompactHeader.Padding = new Thickness(_padding - 4, 8, _padding - 8, 8);
    }

    private void ShowCompactHeader()
    {
        var (title, isPlaceholder) = ViewModel.Title;
        CompactTitleText.Text = title;
        CompactTitleText.FontWeight = isPlaceholder ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
        CompactTitleText.Foreground = (Brush)Application.Current.Resources[isPlaceholder ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush"];
        CompactDayText.Text = ViewModel.DayAndTime;
        AutomationProperties.SetName(CompactTitleButton, $"{title}, back to the top");
    }

    private void CompactTitle_Click(object sender, RoutedEventArgs e) => ScrollToTop();

    private void CompactMore_Click(object sender, RoutedEventArgs e) => MoreMenu.ShowAt(CompactMoreButton);

    private void ScrollToTop() => MainScroller.ChangeView(null, 0, null, disableAnimation: false);

    /// <summary>
    /// Home and Ctrl+Home, outside a text box, scroll smoothly to the top. While editing, Ctrl+Enter
    /// saves and Esc cancels.
    /// </summary>
    private async void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_editing && e.Key == VirtualKey.Enter && IsControlDown())
        {
            e.Handled = true;
            await SaveEditAsync();
        }
        else if (_editing && e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            StopEditing();
        }
        else if (e.Key == VirtualKey.Home && !RecordPageParts.IsTextInput(e.OriginalSource))
        {
            e.Handled = true;
            ScrollToTop();
        }
    }

    private static bool IsControlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // ---------------------------------------------------------------- Header

    private void ShowHeader()
    {
        var (title, isPlaceholder) = ViewModel.Title;
        TitleText.Text = title;
        TitleText.FontWeight = isPlaceholder ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
        TitleText.Foreground = (Brush)Application.Current.Resources[isPlaceholder ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush"];
        TitleText.Visibility = _editing ? Visibility.Collapsed : Visibility.Visible;
        DayText.Text = ViewModel.DayAndTime;
        ShowChips();
    }

    /// <summary>A note without a title gets a dashed "Title" chip that starts editing in the title box. Every note has a day and time.</summary>
    private void ShowChips()
    {
        ChipsPanel.Children.Clear();
        if (!_editing && !ViewModel.HasTitle)
        {
            ChipsPanel.Children.Add(RecordPageParts.DashedButton("\uE710", "Title", "Add title", () =>
            {
                StartEditing(focusTitle: true);
                return Task.CompletedTask;
            }));
        }
        ChipsPanel.Visibility = ChipsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ChipsPanel.Margin = new Thickness(_band == RecordPageBand.S ? 0 : 72, 0, 0, 0);
    }

    // ---------------------------------------------------------------- Text

    /// <summary>The text as stored, with clickable links; under it the created and changed caption.</summary>
    private void ShowText()
    {
        var hasText = ViewModel.HasText;
        RecordPageParts.ShowLinkedText(NoteText, hasText ? ViewModel.Text : "");
        NoteText.Visibility = hasText && !_editing ? Visibility.Visible : Visibility.Collapsed;

        var caption = ViewModel.TimesCaption;
        TimesText.Text = caption;
        CaptionPanel.Visibility = caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CaptionRule.Visibility = hasText || _editing ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- Editing in place

    private bool _editing;
    private bool _saving;
    private DateOnly _editDay;
    private bool _updatingDatePicker;

    // The values the editor started with, to tell whether anything changed.
    private string _originalTitle = "";
    private string _originalText = "";
    private DateOnly _originalDay;

    private bool IsDirty =>
        _editing && (TitleBox.Text != _originalTitle || EditTextBox.Text != _originalText || _editDay != _originalDay);

    private bool IsEditValid => Gnoseis.Core.Domain.NotePage.IsValid(TitleBox.Text, EditTextBox.Text);

    /// <summary>
    /// Turns the page into the note's editor where it stands: the title becomes a text box in the
    /// header, the text a multi-line box with the day, Cancel and Save under it. Focus goes to the
    /// text (caret at the end), or to the title box.
    /// </summary>
    private void StartEditing(bool focusTitle = false)
    {
        if (ViewModel?.Note is not { } note)
        {
            return;
        }
        if (!_editing)
        {
            if (_band == RecordPageBand.S && _selectedTab != NoteKey)
            {
                _selectedTab = NoteKey;
                ShowTabs();
            }
            _editing = true;
            _originalTitle = note.Title ?? "";
            _originalText = note.TextContents ?? "";
            _originalDay = NoteDates.DisplayedDay(note);
            TitleBox.Text = _originalTitle;
            EditTextBox.Text = _originalText;
            _editDay = _originalDay;
            ShowEditDay();
            TitleText.Visibility = Visibility.Collapsed;
            TitleBox.Visibility = Visibility.Visible;
            ShowChips();
            NoteText.Visibility = Visibility.Collapsed;
            EditPanel.Visibility = Visibility.Visible;
            UpdateEditState();
            ShowText();
        }
        SyncEditButtons();

        // After the boxes are in the tree.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (focusTitle)
            {
                TitleBox.Focus(FocusState.Keyboard);
                TitleBox.SelectionStart = TitleBox.Text.Length;
            }
            else
            {
                EditTextBox.Focus(FocusState.Keyboard);
                EditTextBox.SelectionStart = EditTextBox.Text.Length;
            }
        });
    }

    /// <summary>Back to reading; the scroll position stays.</summary>
    private void StopEditing()
    {
        if (!_editing)
        {
            return;
        }
        _editing = false;

        // The focus leaves the boxes as they go: it goes to Edit, not to whatever comes next on the page.
        var focused = XamlRoot != null ? FocusManager.GetFocusedElement(XamlRoot) : null;
        var hadFocus = ReferenceEquals(focused, TitleBox) || ReferenceEquals(focused, EditTextBox);
        TitleBox.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Collapsed;
        ShowHeader();
        ShowText();
        SyncEditButtons();
        if (hadFocus)
        {
            (CompactHeader.Visibility == Visibility.Visible ? (Control)CompactEditButton : EditButton).Focus(FocusState.Programmatic);
        }
    }

    /// <summary>Edit shows as pressed while editing.</summary>
    private void SyncEditButtons()
    {
        EditButton.IsChecked = _editing;
        CompactEditButton.IsChecked = _editing;
    }

    private void UpdateEditState()
    {
        var valid = IsEditValid;
        EditSaveButton.IsEnabled = valid && !_saving;
        InvalidText.Text = Gnoseis.Core.Domain.NotePage.InvalidText;
        InvalidText.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
    }

    private void EditBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_editing)
        {
            UpdateEditState();
        }
    }

    private void ShowEditDay()
    {
        var time = ViewModel.Note is { } note ? Gnoseis.Core.Domain.NotePage.TimeOfDay(note) : TimeSpan.Zero;
        EditDayText.Text = Gnoseis.Core.Domain.ContactPage.DayAndTimeText(_editDay, new TimeSpan(time.Hours, time.Minutes, 0), DateOnly.FromDateTime(DateTime.Now));
        AutomationProperties.SetName(EditDayButton, $"Day: {EditDayText.Text}");
    }

    private void EditDayFlyout_Opening(object? sender, object e)
    {
        _updatingDatePicker = true;
        EditDatePicker.Date = new DateTimeOffset(_editDay.ToDateTime(TimeOnly.MinValue));
        _updatingDatePicker = false;
    }

    /// <summary>Only the day can be changed; the note keeps its time of day.</summary>
    private void EditDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_updatingDatePicker || args.NewDate is not { } date)
        {
            return;
        }
        _editDay = DateOnly.FromDateTime(date.Date);
        ShowEditDay();
        EditDayFlyout.Hide();
    }

    private void NoteText_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!_editing)
        {
            e.Handled = true;
            StartEditing();
        }
    }

    private void EditCancel_Click(object sender, RoutedEventArgs e) => StopEditing();

    private async void EditSave_Click(object sender, RoutedEventArgs e) => await SaveEditAsync();

    /// <summary>
    /// Saves the title, text and day (only what changed, with updatedAt) and returns to reading
    /// with the new text. A save with nothing changed just closes the editor. Returns false if the
    /// note couldn't be saved.
    /// </summary>
    private async Task<bool> SaveEditAsync()
    {
        if (!_editing || _saving)
        {
            return !_editing;
        }
        if (!IsEditValid)
        {
            UpdateEditState();
            return false;
        }
        if (!IsDirty)
        {
            StopEditing();
            return true;
        }

        _saving = true;
        UpdateEditState();
        try
        {
            await ViewModel.SaveAsync(TitleBox.Text, EditTextBox.Text, _editDay);
            await ViewModel.LoadAsync();
            StopEditing();
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't save the note", ex);
            return false;
        }
        finally
        {
            _saving = false;
            if (_editing)
            {
                UpdateEditState();
            }
        }
    }

    /// <summary>Leaving the page with changes asks to save them, as the edit page does.</summary>
    public async Task<bool> CanLeaveAsync()
    {
        if (_saving)
        {
            return false;
        }
        if (!IsDirty)
        {
            return true;
        }
        return await EditPageFlow.AskToLeaveAsync(RecordType.Note, IsEditValid, SaveEditAsync);
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

    /// <summary>Edit, and Edit while editing: the editor with the focus in the text.</summary>
    private void Edit_Click(object sender, RoutedEventArgs e) => StartEditing();

    private void EditAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.IsAvailable == true)
        {
            args.Handled = true;
            StartEditing();
        }
    }

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

    // Built each time it opens: the glyph brushes are picked for the page's theme (or high contrast) at that moment.
    private void MoreMenu_Opening(object? sender, object e)
    {
        MoreMenu.Items.Clear();

        // New linked: a new record of that type, linked to the note (never a note).
        var newLinked = new MenuFlyoutSubItem { Text = "New linked", Icon = new FontIcon { Glyph = "\uE710" } };
        foreach (var type in new[] { RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item })
        {
            var item = new MenuFlyoutItem
            {
                Text = type.DisplayName(),
                Icon = new FontIcon { Glyph = Glyphs.For(type), Foreground = RecordTypeStyles.Brush(this, type, ColorRole.Outline) },
            };
            AutomationProperties.SetName(item, $"New linked {type.DisplayName().ToLower(CultureInfo.CurrentCulture)}");
            item.Click += async (_, _) => await Navigator.ShowEditorAsync(new EditRequest(type, LinkFrom: Record));
            newLinked.Items.Add(item);
        }
        MoreMenu.Items.Add(newLinked);

        var copy = new MenuFlyoutItem
        {
            Text = "Copy note text",
            Icon = new FontIcon { Glyph = "\uE8C8" },
            KeyboardAcceleratorTextOverride = "Ctrl+Shift+C",
        };
        copy.Click += (_, _) => CopyNoteText();
        MoreMenu.Items.Add(copy);
        MoreMenu.Items.Add(new MenuFlyoutSeparator());

        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" }, KeyboardAcceleratorTextOverride = "Del" };
        delete.Click += async (_, _) => await DeleteWithConfirmationAsync();
        MoreMenu.Items.Add(delete);
    }

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.IsAvailable == true)
        {
            args.Handled = true;
            CopyNoteText();
        }
    }

    /// <summary>The title (when there is one), the day and time, an empty line and the text, as plain text.</summary>
    private void CopyNoteText()
    {
        if (ViewModel.Note is { } note)
        {
            FrameworkElement target = _editing ? TitleBox : TitleText;
            RecordPageParts.Copy(Gnoseis.Core.Domain.NotePage.CopyText(note), CopiedTip, target, "Note text copied");
        }
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
