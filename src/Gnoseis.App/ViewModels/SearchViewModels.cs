/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.App.ViewModels;

/// <summary>What a finished search offers to create: the query it searched for and where the offer goes.</summary>
public sealed record CreateOffer(string Query, CreateFromSearchOffer Offer)
{
    public static CreateOffer None { get; } = new("", CreateFromSearchOffer.None);
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
    private CreateOffer _createOffer = CreateOffer.None;

    public RecordRef Source { get; } = source;

    /// <summary>The types the picker offers to create: all but the source's.</summary>
    public IReadOnlyList<RecordType> CreateTypes { get; } = CreateFromSearch.TypesFor(source.Type);

    /// <summary>What the picker offers to create for its query, after its own filters.</summary>
    public CreateOffer CreateOffer
    {
        get => _createOffer;
        private set => SetProperty(ref _createOffer, value);
    }

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

    /// <summary>Links the picked records; does nothing when none is picked.</summary>
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
        var results = _allRecords
            .Where(r => query.Length == 0 || CreateFromSearch.ResultContains(r, query))
            .ToList();
        var rows = results.Select(r => RecordRows.FromSearchResult(r, _alreadyLinked.Contains(r.RecordId))).ToList();
        _visibleCount = rows.Count;
        Groups = RecordRows.GroupByType(rows);
        CreateOffer = new CreateOffer(query, CreateFromSearch.OfferFor(query, results));
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
