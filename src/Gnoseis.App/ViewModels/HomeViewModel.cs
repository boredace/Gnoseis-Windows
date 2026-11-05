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
    private int _loadVersion;

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
            await Task.WhenAll(countsTask, perDayTask, unlinkedTask, recentTask, openedTask);
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
