/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Domain;

namespace Gnoseis.App.ViewModels;

/// <summary>A count tile on the Home page, e.g. "42 Contacts", that opens the type's list.</summary>
public sealed class StatCard(RecordType type, int count) : BindableBase
{
    private bool _showChevron;

    public RecordType Type { get; } = type;
    public string CountText { get; } = count.ToString("N0", CultureInfo.CurrentCulture);
    public string Label { get; } = count == 1 ? type.DisplayName() : type.PluralDisplayName();
    public string Glyph => Glyphs.For(Type);

    /// <summary>"42 Contacts, open the Contacts list".</summary>
    public string AutomationName => $"{CountText} {Label}, open the {Type.PluralDisplayName()} list";

    /// <summary>The chevron shows where the tile is wide enough.</summary>
    public bool ShowChevron { get => _showChevron; set => SetProperty(ref _showChevron, value); }
}

/// <summary>The Links count tile (not clickable).</summary>
public sealed class LinksCard(int count)
{
    public string CountText { get; } = count.ToString("N0", CultureInfo.CurrentCulture);
    public string Label { get; } = count == 1 ? "Link" : "Links";
    public string AutomationName => $"{CountText} {Label}";
}

/// <summary>A type in the Most linked legend: its glyph and plural name.</summary>
public sealed class LegendEntry(RecordType type)
{
    public RecordType Type { get; } = type;
    public string Glyph => Glyphs.For(Type);
    public string Name => Type.PluralDisplayName();
}

/// <summary>What the Most linked card shows.</summary>
public enum MostLinkedState
{
    /// <summary>Nothing is linked yet: a message, no type picker.</summary>
    NoLinks,

    /// <summary>The picked type has no linked records: a message and "Show all types".</summary>
    TypeEmpty,

    /// <summary>The bubbles.</summary>
    Bubbles,
}

/// <summary>
/// Home: counts, Most linked, notes per day, unlinked notes, recent notes, recently opened records
/// and the backup reminder. Reloads whenever data changes.
/// </summary>
public sealed class HomeViewModel : PageViewModel
{
    private const int RecentNoteCount = 8;
    private const int UnlinkedNoteCount = 3;

    private IReadOnlyList<object> _countTiles = [];
    private IReadOnlyList<DayCount> _notesPerDay = [];
    private IReadOnlyList<LinkedCard> _recentNotes = [];
    private IReadOnlyList<LinkedCard> _unlinkedNotes = [];
    private int _unlinkedCount;
    private IReadOnlyList<LinkedRow> _recentlyOpened = [];
    private IReadOnlyList<MostLinkedRecord> _mostLinked = [];
    private IReadOnlyList<LinkEnds> _mostLinkedLinks = [];
    private IReadOnlyList<TypeLinkCount> _linksPerType = [];
    private RecordType? _selectedType;
    private BackupReminder _backup = new(BackupReminderKind.Hidden);
    private bool _isBackingUp;
    private int _loadVersion;
    private int _mostLinkedVersion;

    public string Greeting => DateTime.Now.Hour switch
    {
        < 5 => "Good evening",
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening",
    };

    public string DateText => DateTime.Now.ToString("D", CultureInfo.CurrentCulture);

    // ---------- Counts

    /// <summary>A <see cref="StatCard"/> for each type, then the <see cref="LinksCard"/>.</summary>
    public IReadOnlyList<object> CountTiles { get => _countTiles; private set => SetProperty(ref _countTiles, value); }

    public IEnumerable<StatCard> TypeCounts => CountTiles.OfType<StatCard>();

    // ---------- Most linked

    /// <summary>The bubbles: the records with the most links, most first.</summary>
    public IReadOnlyList<MostLinkedRecord> MostLinked => _mostLinked;

    /// <summary>The links between the records shown (the dashed lines).</summary>
    public IReadOnlyList<LinkEnds> MostLinkedLinks => _mostLinkedLinks;

    /// <summary>The types with links, in type order, for the type picker.</summary>
    public IReadOnlyList<TypeLinkCount> LinksPerType => _linksPerType;

    /// <summary>The type picked in the type picker, or null for all types.</summary>
    public RecordType? SelectedType => _selectedType;

    public MostLinkedState MostLinkedState =>
        _mostLinked.Count > 0 ? MostLinkedState.Bubbles
        : _selectedType != null ? MostLinkedState.TypeEmpty
        : MostLinkedState.NoLinks;

    /// <summary>"Bigger bubble, more records link to it", or "Items with the most links".</summary>
    public string MostLinkedSubtitle => _selectedType is { } type
        ? $"{type.PluralDisplayName()} with the most links"
        : "Bigger bubble, more records link to it";

    /// <summary>"No linked items yet".</summary>
    public string TypeEmptyText => _selectedType is { } type
        ? $"No linked {type.PluralDisplayName().ToLower(CultureInfo.CurrentCulture)} yet"
        : "";

    /// <summary>The types among the bubbles, in type order (the All view only).</summary>
    public IReadOnlyList<LegendEntry> Legend => _selectedType == null
        ? _mostLinked.Select(r => r.Type).Distinct().Order().Select(t => new LegendEntry(t)).ToList()
        : [];

    /// <summary>Shows the records of one type, or of all types (null).</summary>
    public async Task SelectTypeAsync(RecordType? type)
    {
        if (_selectedType == type)
        {
            return;
        }
        _selectedType = type;
        await LoadMostLinkedAsync();
    }

    private async Task LoadMostLinkedAsync()
    {
        var version = ++_mostLinkedVersion;
        var perTypeTask = Data.Home.GetLinksPerTypeAsync();
        var top = await Data.Home.GetMostLinkedAsync(_selectedType);
        var links = await Data.Home.GetLinksAmongAsync(top.Select(r => r.Id).ToList());
        var perType = await perTypeTask;
        if (version != _mostLinkedVersion)
        {
            return;
        }
        _mostLinked = top;
        _mostLinkedLinks = links;
        _linksPerType = perType;
        OnPropertyChanged(nameof(MostLinked));
        OnPropertyChanged(nameof(MostLinkedLinks));
        OnPropertyChanged(nameof(LinksPerType));
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(MostLinkedState));
        OnPropertyChanged(nameof(MostLinkedSubtitle));
        OnPropertyChanged(nameof(TypeEmptyText));
        OnPropertyChanged(nameof(Legend));
    }

    // ---------- Backup reminder

    public BackupReminder Backup
    {
        get => _backup;
        private set
        {
            if (SetProperty(ref _backup, value))
            {
                OnPropertyChanged(nameof(IsBackupReminderShown));
                OnPropertyChanged(nameof(BackupMessage));
                OnPropertyChanged(nameof(BackupSeverity));
            }
        }
    }

    /// <summary>Hidden when nothing changed since the last backup.</summary>
    public bool IsBackupReminderShown => Backup.Kind != BackupReminderKind.Hidden;

    public string BackupMessage => Backup.Message(TimeZoneInfo.Local, CultureInfo.CurrentCulture);

    /// <summary>A warning when never backed up, otherwise information.</summary>
    public Microsoft.UI.Xaml.Controls.InfoBarSeverity BackupSeverity => Backup.Kind == BackupReminderKind.NeverBackedUp
        ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning
        : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    public bool IsBackingUp
    {
        get => _isBackingUp;
        private set
        {
            if (SetProperty(ref _isBackingUp, value))
            {
                OnPropertyChanged(nameof(CanBackUp));
            }
        }
    }

    public bool CanBackUp => !IsBackingUp;

    /// <summary>Backs up to <paramref name="path"/> (as Settings › Back up database) and updates the reminder.</summary>
    public async Task BackUpAsync(string path)
    {
        IsBackingUp = true;
        try
        {
            await Data.Backup.ExportAsync(path);
            Backup = await Data.Home.GetBackupReminderAsync();
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    // ---------- Notes, last 14 days

    public IReadOnlyList<DayCount> NotesPerDay
    {
        get => _notesPerDay;
        private set
        {
            if (SetProperty(ref _notesPerDay, value))
            {
                OnPropertyChanged(nameof(NotesPerDayText));
            }
        }
    }

    /// <summary>"26 notes" in the chart's days.</summary>
    public string NotesPerDayText => NotesText(NotesPerDay.Sum(d => d.Count));

    // ---------- Unlinked notes

    public IReadOnlyList<LinkedCard> UnlinkedNotes
    {
        get => _unlinkedNotes;
        private set
        {
            if (SetProperty(ref _unlinkedNotes, value))
            {
                OnPropertyChanged(nameof(HasNoUnlinkedNotes));
            }
        }
    }

    public bool HasNoUnlinkedNotes => UnlinkedNotes.Count == 0;

    public int UnlinkedCount
    {
        get => _unlinkedCount;
        private set
        {
            if (SetProperty(ref _unlinkedCount, value))
            {
                OnPropertyChanged(nameof(UnlinkedCountText));
            }
        }
    }

    /// <summary>"17 unlinked notes".</summary>
    public string UnlinkedCountText => UnlinkedCount == 1 ? "1 unlinked note" : $"{UnlinkedCount.ToString("N0", CultureInfo.CurrentCulture)} unlinked notes";

    // ---------- Recent notes and recently opened

    public IReadOnlyList<LinkedCard> RecentNotes
    {
        get => _recentNotes;
        private set
        {
            if (SetProperty(ref _recentNotes, value))
            {
                OnPropertyChanged(nameof(HasNoRecentNotes));
            }
        }
    }

    public bool HasNoRecentNotes => RecentNotes.Count == 0;

    public IReadOnlyList<LinkedRow> RecentlyOpened
    {
        get => _recentlyOpened;
        private set
        {
            if (SetProperty(ref _recentlyOpened, value))
            {
                OnPropertyChanged(nameof(HasNoRecentlyOpened));
            }
        }
    }

    public bool HasNoRecentlyOpened => RecentlyOpened.Count == 0;

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var home = Data.Home;
            var today = DateOnly.FromDateTime(DateTime.Now);
            var countsTask = home.GetCountsAsync();
            var perDayTask = home.GetNotesPerDayAsync(today);
            var unlinkedTask = home.GetUnlinkedNotesAsync(UnlinkedNoteCount);
            var recentTask = home.GetRecentNotesAsync(RecentNoteCount);
            var openedTask = home.GetRecordSummariesAsync(App.Settings.RecentRecords.Select(r => (r.Type, r.Id)).ToList());
            var mostLinkedTask = LoadMostLinkedAsync();
            var backupTask = home.GetBackupReminderAsync();
            await Task.WhenAll(countsTask, perDayTask, unlinkedTask, recentTask, openedTask, mostLinkedTask, backupTask);
            if (version != _loadVersion)
            {
                return;
            }

            var counts = countsTask.Result;
            CountTiles = [.. RecordTypeExtensions.UserTypes.Select(t => new StatCard(t, counts.Of(t))), new LinksCard(counts.Links)];
            NotesPerDay = perDayTask.Result;
            UnlinkedNotes = LinkedCard.ByDay(unlinkedTask.Result.Newest, showsLinkCount: false);
            UnlinkedCount = unlinkedTask.Result.TotalCount;
            RecentNotes = LinkedCard.ByDay(recentTask.Result);
            RecentlyOpened = openedTask.Result.Select(r => new LinkedRow(r)).ToList();
            Backup = backupTask.Result;
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private static string NotesText(int count) => count == 1 ? "1 note" : $"{count.ToString("N0", CultureInfo.CurrentCulture)} notes";

    protected override void OnDataChanged(DataChangedEventArgs e) => _ = LoadAsync();
}
