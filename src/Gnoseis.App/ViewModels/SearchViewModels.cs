/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.App.ViewModels;

/// <summary>Search across all records (Android SearchPage).</summary>
public sealed class SearchViewModel : PageViewModel
{
    /// <summary>Results are shown from the second character on.</summary>
    public const int MinimumQueryLength = 2;

    private string _query = "";
    private List<RecordGroup> _groups = [];
    private int _resultCount;
    private int _searchVersion;

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? ""))
            {
                OnPropertyChanged(nameof(Hint));
                _ = SearchAsync();
            }
        }
    }

    public List<RecordGroup> Groups
    {
        get => _groups;
        private set => SetProperty(ref _groups, value);
    }

    public int ResultCount
    {
        get => _resultCount;
        private set
        {
            if (SetProperty(ref _resultCount, value))
            {
                OnPropertyChanged(nameof(Hint));
            }
        }
    }

    public string Hint => Query.Trim().Length < MinimumQueryLength
        ? $"Type at least {MinimumQueryLength} characters to search notes, contacts, organizations, categories and items."
        : ResultCount switch
        {
            0 => $"No results for \"{Query.Trim()}\"",
            1 => "1 result",
            _ => $"{ResultCount} results",
        };

    public async Task SearchAsync()
    {
        var version = ++_searchVersion;
        var query = Query.Trim();
        if (query.Length < MinimumQueryLength)
        {
            Groups = [];
            ResultCount = 0;
            return;
        }

        IsLoading = true;
        try
        {
            var results = await Data.Search.SearchAsync(query);
            if (version != _searchVersion)
            {
                return;
            }
            Groups = RecordRows.GroupByType(results.Select(r => RecordRows.FromSearchResult(r)));
            ResultCount = results.Count;
        }
        finally
        {
            if (version == _searchVersion)
            {
                IsLoading = false;
            }
        }
    }

    protected override void OnDataChanged(DataChangedEventArgs e) => _ = SearchAsync();
}

/// <summary>
/// Pick existing records to link to a source record (Android LinkRecordsPage). Records of the
/// source's own type and the source itself are not offered; records already linked are shown
/// but cannot be picked.
/// </summary>
public sealed class LinkRecordsViewModel(RecordRef source) : PageViewModel
{
    private List<SearchResult> _allRecords = [];
    private HashSet<string> _alreadyLinked = [];
    private readonly HashSet<string> _selectedIds = [];
    private List<RecordGroup> _groups = [];
    private string _query = "";
    private string _sourceTitle = "";
    private int _visibleCount;
    private bool _isLinking;

    public RecordRef Source { get; } = source;

    public string SourceTitle
    {
        get => _sourceTitle;
        private set
        {
            if (SetProperty(ref _sourceTitle, value))
            {
                OnPropertyChanged(nameof(PageTitle));
            }
        }
    }

    public string PageTitle => SourceTitle.Length > 0 ? $"Link records to \"{SourceTitle}\"" : "Link records";

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? ""))
            {
                ApplyFilter();
            }
        }
    }

    public List<RecordGroup> Groups
    {
        get => _groups;
        private set => SetProperty(ref _groups, value);
    }

    public int SelectedCount => _selectedIds.Count;

    public bool CanLink => SelectedCount > 0 && !_isLinking;

    public string LinkButtonText => SelectedCount > 0 ? $"Link ({SelectedCount})" : "Link";

    public string StatusText => $"{_visibleCount} records · {SelectedCount} selected. Records already linked are dimmed.";

    public bool IsSelected(string recordId) => _selectedIds.Contains(recordId);

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var linkedTask = Data.LinkedRecords.GetRecordIdsLinkedToRecordIdAsync(Source.Id);
            var recordsTask = Data.Search.SearchAsync("");
            await Task.WhenAll(linkedTask, recordsTask);

            _alreadyLinked = linkedTask.Result;
            _allRecords = recordsTask.Result
                .Where(r => r.RecordId != Source.Id && r.RecordType != Source.Type)
                .ToList();
            SourceTitle = recordsTask.Result.FirstOrDefault(r => r.RecordId == Source.Id)?.RecordTitle ?? "";
            _selectedIds.RemoveWhere(id => _alreadyLinked.Contains(id) || _allRecords.All(r => r.RecordId != id));
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Called by the page when the user (de)selects rows.</summary>
    public void SetSelected(RecordRow row, bool selected)
    {
        if (row.IsLinked)
        {
            return;
        }
        var changed = selected ? _selectedIds.Add(row.Id) : _selectedIds.Remove(row.Id);
        if (changed)
        {
            RaiseSelectionChanged();
        }
    }

    public async Task LinkSelectedAsync()
    {
        if (!CanLink)
        {
            return;
        }
        _isLinking = true;
        OnPropertyChanged(nameof(CanLink));
        try
        {
            var targets = _allRecords.Where(r => _selectedIds.Contains(r.RecordId) && !_alreadyLinked.Contains(r.RecordId));
            await Data.LinkedRecords.AddLinkedRecordsAsync(Source.Id, Source.Type, targets);
            _selectedIds.Clear();
            RaiseSelectionChanged();
        }
        finally
        {
            _isLinking = false;
            OnPropertyChanged(nameof(CanLink));
        }
    }

    private void ApplyFilter()
    {
        var query = Query.Trim();
        var rows = _allRecords
            .Where(r => query.Length == 0 || r.RecordTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(r => RecordRows.FromSearchResult(r, _alreadyLinked.Contains(r.RecordId)))
            .ToList();
        _visibleCount = rows.Count;
        Groups = RecordRows.GroupByType(rows);
        OnPropertyChanged(nameof(StatusText));
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanLink));
        OnPropertyChanged(nameof(LinkButtonText));
        OnPropertyChanged(nameof(StatusText));
    }
}
