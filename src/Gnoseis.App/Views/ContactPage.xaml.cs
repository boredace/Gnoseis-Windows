/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.App.Controls;
using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using ContactLogic = Gnoseis.Core.Domain.ContactPage;

namespace Gnoseis.App.Views;

/// <summary>
/// A contact: who it is and how to reach them in the header, its history (the linked notes) as the
/// main column, and the other linked records in a side column. The layout follows the width of
/// the record pane (see <see cref="ContactPageBand"/>).
/// </summary>
public sealed partial class ContactPage : Page, IRecordPage, IConfirmLeave
{
    private const double Gap = 32;
    private const double TopPadding = 20;

    private const string HistoryKey = "history";
    private const string LinkedKey = "linked";
    private const string AboutKey = "about";

    private ContactPageBand? _band;
    private double _historyWidth;
    private double _readingWidth;
    private double _sideWidth;
    private double _padding;
    private string _selectedTab = HistoryKey;
    private bool _updatingTabs;
    private int _shownNotes = ContactHistory.PageSize;
    private bool _aboutExpanded;

    // Side column sections shown in full ("Show all"), kept while the page is open.
    private readonly HashSet<RecordType> _expandedSections = [];

    public ContactPage()
    {
        InitializeComponent();
        HeaderBlock.SizeChanged += (_, _) => PositionSideColumn();

        // A click in the box grows it again after Esc or Save.
        NoteTextBox.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) =>
        {
            _keepCollapsed = false;
            ExpandNoteBox();
        }), handledEventsToo: true);
        NoteBox.LostFocus += NoteBox_LostFocus;
    }

    /// <summary>An empty box goes back to one line when the focus leaves it.</summary>
    private void NoteBox_LostFocus(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (HasDraft || NoteDayFlyout.IsOpen || XamlRoot == null)
            {
                return;
            }
            var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            for (var element = focused; element != null; element = VisualTreeHelper.GetParent(element))
            {
                if (ReferenceEquals(element, NoteBox))
                {
                    return;
                }
            }
            CollapseNoteBox(clear: true);
        });

    public ContactPageViewModel ViewModel { get; private set; } = null!;

    public RecordRef Record => ViewModel.Record;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = new ContactPageViewModel((RecordRef)e.Parameter);
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
        ShowNoteBoxTexts();
        ShowHistory();
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
        var band = ContactLogic.BandFor(pane);
        _padding = pane >= 1400 ? 40 : 24;
        var content = Math.Max(0, pane - 2 * _padding);
        if (band == ContactPageBand.XXL)
        {
            content = Math.Min(content, 2700);
        }

        var hasNotes = ViewModel.History.Count > 0;
        (_historyWidth, _readingWidth, _sideWidth) = band switch
        {
            ContactPageBand.S => (content, 0.0, 0.0),
            ContactPageBand.M => (content - 240 - Gap, 0.0, 240.0),
            ContactPageBand.L => (Math.Min(760, content - 320 - Gap), 0.0, 320.0),
            ContactPageBand.XL when hasNotes => (560, Math.Min(960, content - 560 - 320 - 2 * Gap), 320.0),
            ContactPageBand.XL => (Math.Min(760, content - 320 - Gap), 0.0, 320.0),
            _ when hasNotes => (640, Math.Min(1100, content - 640 - 680 - 2 * Gap), 680.0),
            _ => (Math.Min(760, content - 680 - Gap), 0.0, 680.0),
        };

        MainGrid.Width = content;
        MainGrid.Margin = new Thickness(_padding, TopPadding, _padding, 32);
        BodyPanel.Width = Math.Max(0, _historyWidth);
        SideScroller.Width = _sideWidth;

        if (band != _band)
        {
            _band = band;
            var wide = band >= ContactPageBand.L;
            Commands.DefaultLabelPosition = wide ? CommandBarDefaultLabelPosition.Right : CommandBarDefaultLabelPosition.Collapsed;
            ShowChips();
            ShowTabs();
            ShowSideColumn();
        }
        UpdateCompactHeader();
        PositionSideColumn();
    }

    /// <summary>
    /// The side column moves up with the header, then stays below the top of the pane while the
    /// history scrolls on.
    /// </summary>
    private void PositionSideColumn()
    {
        if (_band is not { } band || band == ContactPageBand.S || !ViewModel.IsAvailable)
        {
            SideScroller.Visibility = Visibility.Collapsed;
            return;
        }

        var top = TopPadding + HeaderBlock.ActualHeight + MainGrid.RowSpacing - MainScroller.VerticalOffset;
        top = Math.Max(top, MinimumColumnTop);
        var left = _padding + _historyWidth + Gap + (_readingWidth > 0 ? _readingWidth + Gap : 0);
        SideScroller.Visibility = Visibility.Visible;
        SideScroller.Margin = new Thickness(left, top, 0, 0);
        SideScroller.Height = Math.Max(0, Root.ActualHeight - top);
    }

    /// <summary>The highest the side column goes when the header has scrolled away.</summary>
    private double MinimumColumnTop => CompactHeader.Visibility == Visibility.Visible ? CompactHeaderHeight + 12 : 12;

    private void MainScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateCompactHeader();
        PositionSideColumn();
        if (!e.IsIntermediate || _jumpedYear == null)
        {
            UpdateLitYear();
        }
    }

    // ---------------------------------------------------------------- Header

    private void ShowHeader()
    {
        NameText.Text = ViewModel.Name;
        Picture.DisplayName = ViewModel.Name;
        AutomationProperties.SetName(Picture, ViewModel.Name);
        ShowHeaderLine();
    }

    /// <summary>"Owner at Ruby Eye Clinic", the company a link when a linked organization has its name.</summary>
    private void ShowHeaderLine()
    {
        HeaderLinePanel.Children.Clear();
        var (text, isPlaceholder) = ViewModel.HeaderLine;
        var company = ViewModel.Company;
        if (isPlaceholder)
        {
            HeaderLinePanel.Children.Add(LineText(text, "TextFillColorTertiaryBrush"));
            return;
        }
        if (!company.IsLinked)
        {
            HeaderLinePanel.Children.Add(LineText(text, "TextFillColorSecondaryBrush"));
            return;
        }

        var job = ViewModel.Contact?.JobTitle?.Trim() ?? "";
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        line.Children.Add(LineText(job.Length > 0 ? $"{job} at" : "At", "TextFillColorSecondaryBrush"));
        var link = new HyperlinkButton
        {
            Content = company.Company,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(link, $"Open {company.Organization!.Name}");
        AutomationProperties.SetName(link, $"Open organization {company.Organization.Name}");
        var organization = new RecordRef(RecordType.Organization, company.Organization.Id);
        link.Click += async (_, _) => await Navigator.OpenRecordAsync(organization);
        line.Children.Add(link);
        HeaderLinePanel.Children.Add(line);
    }

    private static TextBlock LineText(string text, string brush) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources[brush],
        TextTrimming = TextTrimming.CharacterEllipsis,
        IsTextSelectionEnabled = true,
    };

    // ---------------------------------------------------------------- Contact chips

    /// <summary>One chip per filled field (folded for the band), then the dashed chips for missing facts.</summary>
    private void ShowChips()
    {
        ChipsPanel.Children.Clear();
        var folding = ContactLogic.Fold(ViewModel.Chips, _band ?? ContactPageBand.S);
        foreach (var chip in folding.Shown)
        {
            ChipsPanel.Children.Add(CreateChip(chip));
        }
        if (folding.HasMore)
        {
            var more = new Button { Content = folding.MoreText, Style = ChipStyle };
            AutomationProperties.SetName(more, $"{folding.Folded.Count} more ways to reach {ViewModel.ShortName}");
            var list = new StackPanel { Spacing = 8 };
            foreach (var chip in folding.Folded)
            {
                list.Children.Add(CreateChip(chip));
            }
            more.Flyout = new Flyout { Content = list, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
            ChipsPanel.Children.Add(more);
        }
        foreach (var fact in ViewModel.MissingFacts)
        {
            var field = ContactLogic.FieldOf(fact);
            ChipsPanel.Children.Add(CreateDashedButton("", ContactLogic.Label(fact), ContactLogic.AutomationName(fact),
                async () => await Navigator.ShowEditorAsync(new EditRequest(RecordType.Contact, Record.Id, FocusField: field))));
        }
        ChipsPanel.Visibility = ChipsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ChipsPanel.Margin = new Thickness(_band == ContactPageBand.S ? 0 : 72, 0, 0, 0);
    }

    private Style ChipStyle => (Style)Resources["ContactChipButtonStyle"];

    private Button CreateChip(ContactChip chip)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = chip.Kind switch { ContactChipKind.Mobile => "", ContactChipKind.Email => Glyphs.Mail, _ => Glyphs.Phone }, FontSize = 14 });
        content.Children.Add(new TextBlock { Text = chip.Value, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock
        {
            Text = chip.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });
        var button = new Button { Content = content, Style = ChipStyle, Tag = chip };
        AutomationProperties.SetName(button, chip.AutomationName);
        ToolTipService.SetToolTip(button, chip.IsPhone ? $"Call {chip.Value}" : $"Write to {chip.Value}");
        button.Click += async (_, _) => await LaunchAsync(chip, button);
        return button;
    }

    /// <summary>Calls or writes; when nothing handles tel: or mailto:, copies the value instead.</summary>
    private async Task LaunchAsync(ContactChip chip, FrameworkElement source)
    {
        var launched = false;
        if (chip.Target is { } uri)
        {
            try
            {
                var support = await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri);
                launched = support == LaunchQuerySupportStatus.Available && await Launcher.LaunchUriAsync(uri);
            }
            catch (Exception)
            {
                launched = false;
            }
        }
        if (!launched)
        {
            Copy(chip.Value, source);
        }
    }

    /// <summary>Puts text on the clipboard and says "Copied" next to <paramref name="source"/>.</summary>
    private void Copy(string text, FrameworkElement source, string message = "Copied")
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        CopiedTip.Target = source;
        CopiedTip.Title = message;
        CopiedTip.IsOpen = false;
        CopiedTip.IsOpen = true;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => CopiedTip.IsOpen = false;
        timer.Start();
    }

    /// <summary>A dashed button: a missing fact, "Link as organization".</summary>
    private static Button CreateDashedButton(string glyph, string text, string automationName, Func<Task> click, bool accentText = false)
    {
        var foreground = (Brush)Application.Current.Resources[accentText ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = foreground });
        content.Children.Add(new TextBlock { Text = text, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        var grid = new Grid();
        grid.Children.Add(new Rectangle
        {
            RadiusX = 15,
            RadiusY = 15,
            Stroke = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"],
            StrokeDashArray = [3, 2],
            StrokeThickness = 1,
        });
        content.Margin = new Thickness(12, 5, 12, 6);
        grid.Children.Add(content);
        var button = new Button
        {
            Content = grid,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(15),
        };
        AutomationProperties.SetName(button, automationName);
        button.Click += async (_, _) => await click();
        return button;
    }

    // ---------------------------------------------------------------- Tabs (under 860 px)

    private void ShowTabs()
    {
        var tabs = _band == ContactPageBand.S;
        TabBar.Visibility = tabs ? Visibility.Visible : Visibility.Collapsed;
        if (!tabs)
        {
            _selectedTab = HistoryKey;
            ShowSelectedTab();
            return;
        }

        if (_selectedTab == AboutKey && !ViewModel.HasComments)
        {
            _selectedTab = HistoryKey;
        }
        _updatingTabs = true;
        try
        {
            TabBar.Items.Clear();
            TabBar.Items.Add(new SelectorBarItem { Text = "History", Tag = HistoryKey });
            TabBar.Items.Add(new SelectorBarItem { Text = $"Linked records ({ViewModel.SideCount})", Tag = LinkedKey });
            if (ViewModel.HasComments)
            {
                TabBar.Items.Add(new SelectorBarItem { Text = "About", Tag = AboutKey });
            }
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
        HistoryPanel.Visibility = _selectedTab == HistoryKey ? Visibility.Visible : Visibility.Collapsed;
        NoteBox.Visibility = HistoryPanel.Visibility;
        TabLinkedPanel.Visibility = _selectedTab == LinkedKey ? Visibility.Visible : Visibility.Collapsed;
        TabAboutText.Visibility = _selectedTab == AboutKey ? Visibility.Visible : Visibility.Collapsed;
        TabAboutText.Text = ViewModel.Comments;
        if (_selectedTab == LinkedKey)
        {
            TabLinkedPanel.Children.Clear();
            foreach (var section in CreateSections(includeAbout: false))
            {
                TabLinkedPanel.Children.Add(section);
            }
        }
    }

    // ---------------------------------------------------------------- History

    // The rows shown, in order (filtered, up to the number shown).
    private List<HistoryRow> _rows = [];

    private void ShowHistory()
    {
        var notes = ViewModel.History;
        HistoryCountText.Text = notes.Count switch { 0 => "nothing yet", 1 => "1 note", var n => $"{n} notes" };
        EmptyHistory.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (notes.Count == 0)
        {
            var name = ViewModel.ShortName;
            EmptyHistoryTitle.Text = $"No notes about {name} yet";
            EmptyHistoryText.Text = $"Write the first one above. Calls, meetings and e-mails noted here build {name}'s history, newest first.";
            CreatedText.Text = ViewModel.CreatedAt is { } created
                ? $"{DateTimeOffset.FromUnixTimeMilliseconds(created).ToLocalTime():d MMM yyyy} · Contact created"
                : "";
            CreatedText.Visibility = ViewModel.CreatedAt != null ? Visibility.Visible : Visibility.Collapsed;
        }

        var grouped = ContactHistory.IsGrouped(notes.Count);
        FilterRow.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
        if (!grouped && FilterBox.Text.Length > 0)
        {
            FilterBox.Text = "";
        }
        ShowYearChips();
        ShowHistoryRows();
    }

    /// <summary>The notes that match the filter, up to the number shown, under month headers from 13 notes.</summary>
    private void ShowHistoryRows()
    {
        var notes = ViewModel.History;
        var matching = notes.Where(n => ContactHistory.Matches(n, FilterBox.Text)).ToList();
        _rows = matching.Take(_shownNotes).Select(n => new HistoryRow(n, Record.Id)).ToList();
        HistorySource.Source = ContactHistory.IsGrouped(notes.Count)
            ? _rows.GroupBy(r => ContactHistory.Month(r.Note)).Select(g => new HistoryMonth(ContactHistory.MonthText(g.Key), g)).ToList()
            : new List<HistoryMonth> { new("", _rows) };
        HistoryList.ItemsSource = HistorySource.View;
        HistoryList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NoMatchText.Visibility = notes.Count > 0 && matching.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var older = matching.Count - _rows.Count;
        ShowOlderButton.Visibility = older > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowOlderButton.Content = $"Show older ({older.ToString(CultureInfo.CurrentCulture)})";
    }

    private void ShowOlder_Click(object sender, RoutedEventArgs e)
    {
        _shownNotes += ContactHistory.PageSize;
        ShowHistoryRows();
    }

    private void FilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput || sender.Text.Length == 0)
        {
            ShowHistoryRows();
        }
    }

    // ---------------------------------------------------------------- Year chips

    private int? _litYear;

    // The year last jumped to: it stays lit while its first note is in view, also when the list can't scroll that far.
    private int? _jumpedYear;

    /// <summary>Over more than one year: a chip per year with its count, in the filter row and the compact header.</summary>
    private void ShowYearChips()
    {
        YearChips.Children.Clear();
        CompactYearChips.Children.Clear();
        var years = ContactHistory.IsGrouped(ViewModel.History.Count) ? ContactHistory.YearCounts(ViewModel.History) : [];
        foreach (var (year, count) in years)
        {
            YearChips.Children.Add(CreateYearChip(year, count));
            CompactYearChips.Children.Add(CreateYearChip(year, count));
        }
        _litYear = null;
        LightYear(years.Count > 0 ? years[0].Year : null);
    }

    private Button CreateYearChip(int year, int count)
    {
        var chip = new Button
        {
            Content = $"{year} · {count}",
            Tag = year,
            Style = ChipStyle,
            Padding = new Thickness(10, 3, 10, 4),
            MinHeight = 0,
        };
        AutomationProperties.SetName(chip, $"Go to {year}, {count} notes");
        chip.Click += (_, _) => JumpToYear(year);
        return chip;
    }

    /// <summary>Lights the chips of the year of the topmost note shown.</summary>
    private void LightYear(int? year)
    {
        if (year == _litYear)
        {
            return;
        }
        _litYear = year;
        foreach (var chip in YearChips.Children.Concat(CompactYearChips.Children).OfType<Button>())
        {
            var lit = chip.Tag is int y && y == year;
            chip.Background = lit ? (Brush)Application.Current.Resources["AccentFillColorTertiaryBrush"] : null;
            chip.Foreground = lit ? (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"] : null;
            if (!lit)
            {
                chip.ClearValue(BackgroundProperty);
                chip.ClearValue(ForegroundProperty);
            }
        }
    }

    /// <summary>Scrolls to the year's first note (newest first), showing older notes when it isn't shown yet. A chip doesn't filter.</summary>
    private void JumpToYear(int year)
    {
        var index = _rows.FindIndex(r => r.Year == year);
        if (index < 0)
        {
            var all = ViewModel.History.Where(n => ContactHistory.Matches(n, FilterBox.Text)).ToList();
            var position = all.FindIndex(n => ContactHistory.Day(n).Year == year);
            if (position < 0)
            {
                return;
            }
            _shownNotes = Math.Max(_shownNotes, (position / ContactHistory.PageSize + 1) * ContactHistory.PageSize);
            ShowHistoryRows();
            index = _rows.FindIndex(r => r.Year == year);
        }
        var row = _rows[index];
        HistoryList.UpdateLayout();
        if (HistoryList.ContainerFromItem(row) is UIElement container)
        {
            var transform = container.TransformToVisual(MainScroller.Content as UIElement);
            var y = transform.TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            var monthHeader = index == 0 || _rows[index - 1].Note is var previous && ContactHistory.Month(previous) != ContactHistory.Month(row.Note) ? 48 : 0;
            MainScroller.ChangeView(null, Math.Max(0, y - CompactHeaderHeight - monthHeader - 8), null);
        }
        _jumpedYear = year;
        LightYear(year);
    }

    /// <summary>The year of the topmost note in view, for the chips.</summary>
    private void UpdateLitYear()
    {
        if (YearChips.Children.Count == 0 || _rows.Count == 0)
        {
            return;
        }
        var content = MainScroller.Content as UIElement;
        var top = MainScroller.VerticalOffset + (CompactHeader.Visibility == Visibility.Visible ? CompactHeaderHeight : 0);
        var bottom = MainScroller.VerticalOffset + MainScroller.ViewportHeight;
        if (_jumpedYear is { } jumped)
        {
            var first = _rows.Find(r => r.Year == jumped);
            if (first != null && HistoryList.ContainerFromItem(first) is FrameworkElement jumpedContainer)
            {
                var jy = jumpedContainer.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                if (jy + jumpedContainer.ActualHeight > top && jy < bottom)
                {
                    LightYear(jumped);
                    return;
                }
            }
            _jumpedYear = null;
        }
        foreach (var row in _rows)
        {
            if (HistoryList.ContainerFromItem(row) is FrameworkElement container)
            {
                var y = container.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                if (y + container.ActualHeight > top)
                {
                    LightYear(row.Year);
                    return;
                }
            }
        }
    }

    // ---------------------------------------------------------------- Compact header and scrolling to the top

    private double CompactHeaderHeight => CompactHeader.ActualHeight > 0 ? CompactHeader.ActualHeight : 56;

    /// <summary>Shows the compact header once the full header has scrolled out of view.</summary>
    private void UpdateCompactHeader()
    {
        var show = ViewModel.IsAvailable && MainScroller.VerticalOffset > TopPadding + HeaderBlock.ActualHeight;
        CompactHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CompactHeader.Padding = new Thickness(_padding - 4, 8, _padding - 8, 8);
        CompactYearChips.Visibility = _band >= ContactPageBand.M ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowCompactHeader()
    {
        CompactNameText.Text = ViewModel.Name;
        CompactPicture.DisplayName = ViewModel.Name;
        var (line, _) = ViewModel.HeaderLine;
        CompactLineText.Text = line;
        AutomationProperties.SetName(CompactNameButton, $"{ViewModel.Name}, back to the top");
    }

    private void CompactName_Click(object sender, RoutedEventArgs e) => ScrollToTop();

    private void CompactNote_Click(object sender, RoutedEventArgs e) => FocusNoteBox();

    private void CompactMore_Click(object sender, RoutedEventArgs e) => MoreMenu.ShowAt(CompactMoreButton);

    private void ScrollToTop() => MainScroller.ChangeView(null, 0, null, disableAnimation: false);

    /// <summary>Home and Ctrl+Home, outside a text box, scroll smoothly to the top (the history list doesn't take them).</summary>
    private void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Home && !IsTextInput(e.OriginalSource))
        {
            e.Handled = true;
            ScrollToTop();
        }
    }

    private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HistoryRow row)
        {
            await Navigator.OpenRecordAsync(row.ToRef());
        }
    }

    /// <summary>Right-click, Shift+F10 or the context-menu key on a note: Open and Unlink.</summary>
    private void HistoryRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is HistoryRow row)
        {
            ShowRowMenu(sender, args, row.ToRef(), row.Title.Length > 0 ? row.Title : ContactHistory.FullTitle(row.Note));
        }
    }

    private void ShowRowMenu(UIElement sender, ContextRequestedEventArgs args, RecordRef record, string name)
    {
        var open = new MenuFlyoutItem { Text = "Open", Icon = new FontIcon { Glyph = "" } };
        open.Click += async (_, _) => await Navigator.OpenRecordAsync(record);
        var unlink = new MenuFlyoutItem { Text = "Unlink", Icon = new FontIcon { Glyph = "" } };
        unlink.Click += async (_, _) => await UnlinkAsync(record.Id, name);
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

    // ---------------------------------------------------------------- Side column

    private void ShowSideColumn()
    {
        SidePanel.Children.Clear();
        SidePanel.ColumnDefinitions.Clear();
        if (_band is not { } band || band == ContactPageBand.S)
        {
            return;
        }

        var sections = CreateSections(includeAbout: true);
        var columns = band == ContactPageBand.XXL ? 2 : 1;
        for (var i = 0; i < columns; i++)
        {
            SidePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        // Sections flow down the first column, then the second, balancing their heights.
        var heights = sections.Select(EstimatedHeight).ToList();
        var target = heights.Sum() / columns;
        var column = new StackPanel { Spacing = 24 };
        SidePanel.Children.Add(column);
        var height = 0.0;
        for (var i = 0; i < sections.Count; i++)
        {
            if (columns > 1 && Grid.GetColumn(column) == 0 && height > 0 && height + heights[i] / 2 > target)
            {
                column = new StackPanel { Spacing = 24 };
                Grid.SetColumn(column, 1);
                SidePanel.Children.Add(column);
                height = 0;
            }
            column.Children.Add(sections[i]);
            height += heights[i];
        }
    }

    private static double EstimatedHeight(FrameworkElement section) => section.Tag is int rows ? 40 + rows * 50 : 120;

    /// <summary>About (in the side column), Organizations, Categories, Items; with no links, the link buttons.</summary>
    private List<FrameworkElement> CreateSections(bool includeAbout)
    {
        var sections = new List<FrameworkElement>();
        if (includeAbout && ViewModel.HasComments)
        {
            sections.Add(CreateAboutSection());
        }
        if (ViewModel.SideCount == 0)
        {
            sections.Add(CreateNoLinksSection());
            return sections;
        }
        foreach (var type in ContactLogic.SideTypes)
        {
            var records = ViewModel.SideRecords(type);
            if (records.Count > 0)
            {
                sections.Add(CreateSection(type, records));
            }
        }
        return sections;
    }

    private FrameworkElement CreateAboutSection()
    {
        var panel = new StackPanel { Spacing = 6, Tag = 3 };
        panel.Children.Add(SectionHeading("About", null));
        var text = new TextBlock
        {
            Text = ViewModel.Comments,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = _aboutExpanded ? 0 : 4,
            IsTextSelectionEnabled = true,
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        panel.Children.Add(text);
        var more = new HyperlinkButton
        {
            Content = _aboutExpanded ? "Less" : "More",
            Padding = new Thickness(0),
            Visibility = _aboutExpanded ? Visibility.Visible : Visibility.Collapsed,
        };
        text.IsTextTrimmedChanged += (_, _) =>
        {
            if (!_aboutExpanded)
            {
                more.Visibility = text.IsTextTrimmed ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        more.Click += (_, _) =>
        {
            _aboutExpanded = !_aboutExpanded;
            text.MaxLines = _aboutExpanded ? 0 : 4;
            more.Content = _aboutExpanded ? "Less" : "More";
        };
        panel.Children.Add(more);
        return panel;
    }

    private FrameworkElement CreateSection(RecordType type, IReadOnlyList<LinkedRecordSummary> records)
    {
        var limit = ContactLogic.SideRowCount(_band ?? ContactPageBand.L);
        var expanded = _expandedSections.Contains(type);
        var shown = expanded ? records : records.Take(limit).ToList();
        var panel = new StackPanel { Spacing = 2, Tag = shown.Count };

        var heading = SectionHeading(type.PluralDisplayName(), records.Count);
        if (records.Count > limit)
        {
            var showAll = new HyperlinkButton { Content = expanded ? "Show fewer" : "Show all", Padding = new Thickness(4, 2, 4, 2) };
            showAll.Click += (_, _) =>
            {
                if (!_expandedSections.Remove(type))
                {
                    _expandedSections.Add(type);
                }
                RebuildSections();
            };
            AddToHeading(heading, showAll);
        }
        panel.Children.Add(heading);

        foreach (var record in shown)
        {
            panel.Children.Add(CreateSideRow(record));
        }
        return panel;
    }

    private static Grid SectionHeading(string title, int? count)
    {
        var grid = new Grid { MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = title });
        if (count is { } n)
        {
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = $"  {n.ToString(CultureInfo.CurrentCulture)}",
                FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            });
        }
        AutomationProperties.SetHeadingLevel(text, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        grid.Children.Add(text);
        grid.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(grid.Children[1] as FrameworkElement, 1);
        return grid;
    }

    private static void AddToHeading(Grid heading, UIElement element) => ((StackPanel)heading.Children[1]).Children.Add(element);

    /// <summary>A linked record: its type tile, title and second line (an organization's first comment line, a category's or item's parent path).</summary>
    private Button CreateSideRow(LinkedRecordSummary record)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TypeTile
        {
            Type = record.Type,
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(5),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        });
        var lines = new StackPanel();
        Grid.SetColumn(lines, 1);
        var name = record.Name.Length > 0 ? record.Name : record.Type.DisplayName();
        lines.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["BodyTextBlockStyle"], TextWrapping = TextWrapping.NoWrap });
        var second = record.Type == RecordType.Organization ? record.FirstLine : record.ParentPath;
        if (second.Length > 0)
        {
            lines.Children.Add(new TextBlock
            {
                Text = second,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }
        grid.Children.Add(lines);

        var button = new Button
        {
            Content = grid,
            Style = (Style)Application.Current.Resources["RowButtonStyle"],
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(-8, 0, 0, 0),
        };
        AutomationProperties.SetName(button, string.Join(". ", new[] { $"{record.Type.DisplayName()}: {name}", second }.Where(s => s.Length > 0)));
        var target = new RecordRef(record.Type, record.Id);
        button.Click += async (_, _) => await Navigator.OpenRecordAsync(target);
        button.ContextRequested += (sender, args) => ShowRowMenu(sender, args, target, name);
        return button;
    }

    /// <summary>No organizations, categories or items linked: buttons to link some.</summary>
    private FrameworkElement CreateNoLinksSection()
    {
        var panel = new StackPanel { Spacing = 4, Tag = 3 };
        panel.Children.Add(SectionHeading("Linked records · none", null));
        var company = ViewModel.Company;
        panel.Children.Add(LinkTextButton(company.CanLinkAsOrganization ? $"Link {company.Company}" : "Link an organization",
            () => LinkAsync([RecordType.Organization], company.CanLinkAsOrganization ? company.Company : null)));
        panel.Children.Add(LinkTextButton("Add to a category", () => LinkAsync([RecordType.Category], null)));
        panel.Children.Add(LinkTextButton("Link anything… (Ctrl+L)", () => LinkAsync(null, null)));
        return panel;
    }

    private static HyperlinkButton LinkTextButton(string text, Func<Task> click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = Glyphs.Link, FontSize = 14 });
        content.Children.Add(new TextBlock { Text = text });
        var button = new HyperlinkButton { Content = content, Padding = new Thickness(4, 4, 4, 4), Margin = new Thickness(-4, 0, 0, 0) };
        AutomationProperties.SetName(button, text);
        button.Click += async (_, _) => await click();
        return button;
    }

    private void RebuildSections()
    {
        ShowSideColumn();
        if (_band == ContactPageBand.S)
        {
            ShowSelectedTab();
        }
    }

    // ---------------------------------------------------------------- Note box

    private DateOnly _noteDay;
    private TimeSpan _noteTime;
    private bool _noteDatePicked;
    private bool _noteBoxCollapsed = true;
    private bool _keepCollapsed;
    private bool _updatingNotePickers;
    private bool _savingNote;

    private bool HasDraft => !string.IsNullOrWhiteSpace(NoteTextBox.Text) || !string.IsNullOrWhiteSpace(NoteTitleBox.Text);

    private void ShowNoteBoxTexts()
    {
        NoteTextBox.PlaceholderText = $"Write a note about {ViewModel.ShortName}…";
        AutomationProperties.SetName(NoteTextBox, $"Write a note about {ViewModel.ShortName}");
        WillLinkText.Text = $"Will be linked to {ViewModel.Name}";
    }

    /// <summary>Ctrl+Shift+N: scrolls the note box into view and puts the cursor in it.</summary>
    private void NoteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FocusNoteBox();
    }

    private void FocusNoteBox()
    {
        if (!ViewModel.IsAvailable)
        {
            return;
        }
        if (_band == ContactPageBand.S && _selectedTab != HistoryKey)
        {
            _selectedTab = HistoryKey;
            ShowTabs();
        }
        MainScroller.ChangeView(null, 0, null);
        _keepCollapsed = false;
        NoteTextBox.Focus(FocusState.Keyboard);
        ExpandNoteBox();
    }

    private void NoteTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_keepCollapsed)
        {
            ExpandNoteBox();
        }
    }

    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NoteSaveButton.IsEnabled = HasDraft && !_savingNote;
        if (HasDraft && _noteBoxCollapsed && NoteTextBox.FocusState != FocusState.Unfocused)
        {
            _keepCollapsed = false;
            ExpandNoteBox();
        }
    }

    /// <summary>Grows the box in place: several lines, then day and time, title, Open full editor, Cancel and Save.</summary>
    private void ExpandNoteBox()
    {
        if (!_noteBoxCollapsed)
        {
            return;
        }
        _noteBoxCollapsed = false;
        if (!_noteDatePicked)
        {
            var now = DateTime.Now;
            _noteDay = DateOnly.FromDateTime(now);
            _noteTime = new TimeSpan(now.Hour, now.Minute, 0);
        }
        ShowNoteDay();
        NoteTextBox.MinHeight = 88;
        NoteTextBox.MaxHeight = 320;
        ShortcutHint.Visibility = Visibility.Collapsed;
        NoteBoxCommands.Visibility = Visibility.Visible;
        NoteTitleBox.Visibility = NoteTitleBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoteBox.Padding = new Thickness(12);
        NoteBox.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        NoteBox.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
    }

    /// <summary>Back to one line. The draft stays unless <paramref name="clear"/>.</summary>
    private void CollapseNoteBox(bool clear)
    {
        if (clear)
        {
            NoteTextBox.Text = "";
            NoteTitleBox.Text = "";
            _noteDatePicked = false;
        }
        _noteBoxCollapsed = true;
        NoteTextBox.MinHeight = 0;
        NoteTextBox.MaxHeight = clear || !HasDraft ? double.PositiveInfinity : 34;
        ShortcutHint.Visibility = HasDraft ? Visibility.Collapsed : Visibility.Visible;
        NoteBoxCommands.Visibility = Visibility.Collapsed;
        NoteTitleBox.Visibility = Visibility.Collapsed;
        NoteBox.Padding = new Thickness(0);
        NoteBox.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        NoteBox.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void ShowNoteDay()
    {
        NoteDayText.Text = ContactLogic.DayAndTimeText(_noteDay, _noteTime, DateOnly.FromDateTime(DateTime.Now));
        AutomationProperties.SetName(NoteDayButton, $"Day and time: {NoteDayText.Text}");
    }

    private void NoteDayFlyout_Opening(object? sender, object e)
    {
        _updatingNotePickers = true;
        NoteDatePicker.Date = new DateTimeOffset(_noteDay.ToDateTime(TimeOnly.MinValue));
        NoteTimePicker.Time = _noteTime;
        _updatingNotePickers = false;
    }

    private void NoteDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_updatingNotePickers || args.NewDate is not { } date)
        {
            return;
        }
        _noteDay = DateOnly.FromDateTime(date.Date);
        _noteDatePicked = true;
        ShowNoteDay();
    }

    private void NoteTimePicker_TimeChanged(object? sender, TimePickerValueChangedEventArgs e)
    {
        if (_updatingNotePickers)
        {
            return;
        }
        _noteTime = e.NewTime;
        _noteDatePicked = true;
        ShowNoteDay();
    }

    private void NoteTitleButton_Click(object sender, RoutedEventArgs e)
    {
        NoteTitleBox.Visibility = Visibility.Visible;
        NoteTitleBox.Focus(FocusState.Keyboard);
    }

    /// <summary>Ctrl+Enter saves; Esc collapses the box and keeps the draft.</summary>
    private async void NoteBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Enter && ctrl)
        {
            e.Handled = true;
            await SaveNoteAsync();
        }
        else if (e.Key == VirtualKey.Escape && !_noteBoxCollapsed)
        {
            e.Handled = true;
            _keepCollapsed = true;
            CollapseNoteBox(clear: false);
        }
    }

    private void NoteCancel_Click(object sender, RoutedEventArgs e)
    {
        _keepCollapsed = true;
        CollapseNoteBox(clear: true);
        NoteTextBox.Focus(FocusState.Programmatic);
    }

    private async void NoteSave_Click(object sender, RoutedEventArgs e) => await SaveNoteAsync();

    /// <summary>
    /// Saves the note and its link to the contact in one transaction; the box then empties and
    /// collapses, and keeps the focus.
    /// </summary>
    private async Task<bool> SaveNoteAsync()
    {
        if (!HasDraft || _savingNote)
        {
            return !HasDraft;
        }
        _savingNote = true;
        NoteSaveButton.IsEnabled = false;
        try
        {
            if (!_noteDatePicked && _noteBoxCollapsed)
            {
                var now = DateTime.Now;
                _noteDay = DateOnly.FromDateTime(now);
                _noteTime = new TimeSpan(now.Hour, now.Minute, 0);
            }
            var note = await App.Container.CreateLinkedNote.InvokeAsync(
                Record.Id, RecordType.Contact, NoteTitleBox.Text, NoteTextBox.Text, _noteDay, _noteTime);
            _selectAfterLoad = note.Id;
            _keepCollapsed = true;
            CollapseNoteBox(clear: true);
            NoteTextBox.Focus(FocusState.Programmatic);
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't save the note", ex);
            return false;
        }
        finally
        {
            _savingNote = false;
            NoteSaveButton.IsEnabled = HasDraft;
        }
    }

    // The note saved from the box, selected in the reading pane once the history has reloaded.
    private string? _selectAfterLoad;

    /// <summary>Opens the note editor with the draft, for a new note linked to the contact.</summary>
    private async void OpenFullEditor_Click(object sender, RoutedEventArgs e)
    {
        var draft = new NoteDraft(NoteTitleBox.Text, NoteTextBox.Text, _noteDay, _noteTime);
        CollapseNoteBox(clear: true);
        await Navigator.ShowEditorAsync(new EditRequest(RecordType.Note, LinkFrom: Record, NoteDraft: draft));
    }

    /// <summary>Leaving with a draft asks, as the editor does: save it, drop it, or stay.</summary>
    public async Task<bool> CanLeaveAsync()
    {
        if (_savingNote)
        {
            return false;
        }
        if (!HasDraft)
        {
            return true;
        }
        var choice = await Dialogs.AskAsync("Save changes?", "Do you want to save your changes to this note?", "Save", "Don't save", "Cancel");
        switch (choice)
        {
            case ContentDialogResult.Primary:
                return await SaveNoteAsync();
            case ContentDialogResult.Secondary:
                CollapseNoteBox(clear: true);
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- Commands

    private async void Edit_Click(object sender, RoutedEventArgs e) =>
        await Navigator.ShowEditorAsync(new EditRequest(RecordType.Contact, Record.Id));

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
        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "" } };
        delete.KeyboardAcceleratorTextOverride = "Del";
        delete.Click += async (_, _) => await DeleteWithConfirmationAsync();
        MoreMenu.Items.Add(delete);
    }

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Del deletes the contact (with a confirmation), unless a text box has the focus.
        if (e.Key == VirtualKey.Delete && !IsTextInput(e.OriginalSource))
        {
            e.Handled = true;
            await DeleteWithConfirmationAsync();
        }
    }

    private static bool IsTextInput(object source) => source is TextBox or RichEditBox or PasswordBox or AutoSuggestBox;

    /// <summary>Asks, then deletes the contact.</summary>
    public async Task DeleteWithConfirmationAsync()
    {
        if (!ViewModel.IsAvailable)
        {
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            "Delete this contact?",
            $"\"{ViewModel.Name}\"\n\n{ViewModel.DeleteConfirmationText}",
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
