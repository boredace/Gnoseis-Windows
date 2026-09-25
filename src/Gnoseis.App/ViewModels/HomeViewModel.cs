/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml.Media;

namespace Gnoseis.App.ViewModels;

/// <summary>A count on the Home page, e.g. "42 Contacts".</summary>
public sealed class StatCard(RecordType type, int count)
{
    public RecordType Type { get; } = type;
    public string CountText { get; } = count.ToString("N0", CultureInfo.CurrentCulture);
    public string Label { get; } = count == 1 ? type.DisplayName() : type.PluralDisplayName();
    public string Glyph => Glyphs.For(Type);
    public SolidColorBrush TileBrush => TypeColors.BrushFor(Type);
    public string AutomationName => $"{CountText} {Label}. Open {Type.PluralDisplayName()}";
}

/// <summary>Home: quick note, counts, recent notes and recently opened records.</summary>
public sealed class HomeViewModel : PageViewModel
{
    private const int RecentNoteCount = 8;

    private IReadOnlyList<StatCard> _stats = [];
    private IReadOnlyList<RecordRow> _recentNotes = [];
    private IReadOnlyList<RecordRow> _recentlyOpened = [];
    private string _quickTitle = "";
    private string _quickText = "";
    private bool _isAdding;
    private int _loadVersion;

    public string Greeting => DateTime.Now.Hour switch
    {
        < 5 => "Good evening",
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening",
    };

    public string DateText => DateTime.Now.ToString("D", CultureInfo.CurrentCulture);

    public IReadOnlyList<StatCard> Stats { get => _stats; private set => SetProperty(ref _stats, value); }

    public IReadOnlyList<RecordRow> RecentNotes
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

    public IReadOnlyList<RecordRow> RecentlyOpened
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

    public string QuickTitle
    {
        get => _quickTitle;
        set
        {
            if (SetProperty(ref _quickTitle, value ?? ""))
            {
                OnPropertyChanged(nameof(CanAddQuickNote));
            }
        }
    }

    public string QuickText
    {
        get => _quickText;
        set
        {
            if (SetProperty(ref _quickText, value ?? ""))
            {
                OnPropertyChanged(nameof(CanAddQuickNote));
            }
        }
    }

    public bool CanAddQuickNote => !_isAdding && (QuickTitle.Trim().Length > 0 || QuickText.Trim().Length > 0);

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var countsTask = Data.Search.GetRecordCountsAsync();
            var notesTask = Data.Notes.GetNotesAsync();
            var recentIds = App.Settings.RecentRecords.Select(r => r.Id).ToList();
            var openedTask = Data.Search.GetRecordsAsync(recentIds);
            await Task.WhenAll(countsTask, notesTask, openedTask);
            if (version != _loadVersion)
            {
                return;
            }

            var counts = countsTask.Result;
            Stats = RecordTypeExtensions.UserTypes.Select(t => new StatCard(t, counts.GetValueOrDefault(t))).ToList();
            RecentNotes = notesTask.Result.Take(RecentNoteCount).Select(n => RecordRows.FromNote(n, mixedList: true)).ToList();
            RecentlyOpened = openedTask.Result.Select(r => RecordRows.FromSearchResult(r)).ToList();
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Adds the quick note for today and clears the boxes. Returns the new note.</summary>
    public async Task<RecordRef?> AddQuickNoteAsync()
    {
        if (!CanAddQuickNote)
        {
            return null;
        }

        _isAdding = true;
        OnPropertyChanged(nameof(CanAddQuickNote));
        try
        {
            var (createDate, createDateTime) = NoteDates.ForNewNote(DateOnly.FromDateTime(DateTime.Now), DateTime.Now.TimeOfDay);
            var note = await Data.Notes.AddNoteAsync(new Note
            {
                OwnerDbId = "db1",
                Title = QuickTitle.Trim(),
                TextContents = QuickText.TrimEnd(),
                CreateDate = createDate,
                CreateDateTime = createDateTime,
            });
            QuickTitle = "";
            QuickText = "";
            return new RecordRef(RecordType.Note, note.Id);
        }
        finally
        {
            _isAdding = false;
            OnPropertyChanged(nameof(CanAddQuickNote));
        }
    }

    protected override void OnDataChanged(DataChangedEventArgs e) => _ = LoadAsync();
}
