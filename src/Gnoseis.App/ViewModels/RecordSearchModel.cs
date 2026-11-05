/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.App.ViewModels;

/// <summary>
/// One search, as the search page and the link picker show it (Android's RecordSearchModel): the
/// query, the filter chips, the ranked results laid out by type with a best match, the recently
/// changed records before typing, and the offer to create a record. Every record is loaded once per
/// visit (and again when data changes); each keystroke matches, ranks and lays out in memory.
/// In the link picker it also keeps the ticked records, whatever the search shows.
/// </summary>
public sealed class RecordSearchModel : PageViewModel
{
    private readonly IReadOnlyList<RecordType> _chipTypes;
    private readonly HashSet<RecordType> _chipTypeSet;
    private SearchIndex _index = SearchIndex.Empty;
    private IReadOnlySet<string> _linked = new HashSet<string>();
    private IReadOnlySet<RecordType> _selectedChips;
    private List<SearchResult> _recent = [];
    private string _query = "";
    private int _loadVersion;
    private int _recentVersion;
    private bool _isLinking;

    private SearchRow? _bestMatch;
    private List<SearchRowGroup> _groups = [];
    private string _noMatchText = "";
    private IReadOnlyList<RecordType> _moreTypes = [];
    private int _moreCount;
    private CreateOffer _createOffer = CreateOffer.None;
    private (string Id, IReadOnlyList<TypeLinkCountView> Counts)? _bestMatchLinks;

    /// <param name="chipTypes">The types searched and their chips, in order.</param>
    /// <param name="scope">The section searched from: its chip starts selected and its type is offered first.</param>
    /// <param name="createTypes">The types create from search offers.</param>
    /// <param name="linkSource">In the link picker, the record to link to: it and the records linked to it are handled apart.</param>
    public RecordSearchModel(IReadOnlyList<RecordType> chipTypes, RecordType? scope, IReadOnlyList<RecordType> createTypes, RecordRef? linkSource = null)
    {
        _chipTypes = chipTypes;
        _chipTypeSet = chipTypes.ToHashSet();
        Scope = scope;
        LinkSource = linkSource;
        CreateTypes = SearchLogic.CreateTypesWithFirst(createTypes, scope);
        _selectedChips = SearchLogic.StartingChips(scope, chipTypes);
        Chips = [new SearchChip(null), .. chipTypes.Select(t => new SearchChip(t))];
        UpdateChips(null);
    }

    public RecordType? Scope { get; }

    public RecordRef? LinkSource { get; }

    public bool IsPicker => LinkSource != null;

    /// <summary>The types create from search offers, the section's first.</summary>
    public IReadOnlyList<RecordType> CreateTypes { get; }

    /// <summary>All, then one chip per type.</summary>
    public IReadOnlyList<SearchChip> Chips { get; }

    /// <summary>The link picker's ticked records.</summary>
    public LinkSelection Selection { get; } = new();

    /// <summary>Raised when a record is ticked or unticked, or the ticks were linked.</summary>
    public event EventHandler? SelectionChanged;

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? ""))
            {
                Update();
            }
        }
    }

    public bool IsSearching => SearchLogic.IsSearchQuery(Query);

    /// <summary>Shown under the search box until the query is long enough.</summary>
    public string Hint => IsSearching ? "" :
        $"Type at least {CreateFromSearch.MinimumQueryLength} characters to search {AndList(_chipTypes.Select(t => t.PluralDisplayName().ToLowerInvariant()))}.";

    public bool HasHint => !IsSearching;

    public SearchRow? BestMatch
    {
        get => _bestMatch;
        private set
        {
            if (SetProperty(ref _bestMatch, value))
            {
                OnPropertyChanged(nameof(HasBestMatch));
            }
        }
    }

    public bool HasBestMatch => BestMatch != null;

    /// <summary>One group per type in chip order (best match not repeated); before typing, Recently changed.</summary>
    public List<SearchRowGroup> Groups
    {
        get => _groups;
        private set => SetProperty(ref _groups, value);
    }

    /// <summary>"No contacts match "harbour"." when the chips hide every match.</summary>
    public string NoMatchText
    {
        get => _noMatchText;
        private set
        {
            if (SetProperty(ref _noMatchText, value))
            {
                OnPropertyChanged(nameof(HasNoMatchText));
            }
        }
    }

    public bool HasNoMatchText => NoMatchText.Length > 0;

    /// <summary>From a section: the types with matches the chips hide, in type order.</summary>
    public IReadOnlyList<RecordType> MoreTypes
    {
        get => _moreTypes;
        private set => SetProperty(ref _moreTypes, value);
    }

    public int MoreCount
    {
        get => _moreCount;
        private set
        {
            if (SetProperty(ref _moreCount, value))
            {
                OnPropertyChanged(nameof(ShowsMore));
                OnPropertyChanged(nameof(MoreText));
            }
        }
    }

    public bool ShowsMore => MoreCount > 0;

    public string MoreText => $"{MoreCount} more in other types";

    /// <summary>What to offer to create for the query; whether a result matches exactly is decided on all matches.</summary>
    public CreateOffer CreateOffer
    {
        get => _createOffer;
        private set => SetProperty(ref _createOffer, value);
    }

    // ---------------------------------------------------------------- Link picker

    public int SelectedCount => Selection.Count;

    public bool HasSelection => !Selection.IsEmpty;

    public string SelectedText => $"{SelectedCount} selected";

    public bool CanLink => HasSelection && !_isLinking;

    public string LinkButtonText => HasSelection ? $"Link {SelectedCount}" : "Link";

    /// <summary>The ticked records, in the order picked, as rows (the selected records flyout).</summary>
    public List<SearchRow> SelectedRows() =>
        Selection.Picked.Select(r => SearchRow.For(r, "", showsCheck: true, isChecked: true)).ToList();

    // ---------------------------------------------------------------- Loading

    /// <summary>Loads every record (and, in the picker, the records linked to the source), then lays out the search.</summary>
    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var allTask = Data.Search.GetAllAsync();
            var linkedTask = LinkSource is { } source
                ? Data.LinkedRecords.GetRecordIdsLinkedToRecordIdAsync(source.Id)
                : Task.FromResult(new HashSet<string>());
            await Task.WhenAll(allTask, linkedTask);
            if (version != _loadVersion)
            {
                return;
            }

            var all = allTask.Result.Where(r => _chipTypeSet.Contains(r.RecordType) && r.RecordId != LinkSource?.Id).ToList();
            _linked = linkedTask.Result;
            _index = new SearchIndex(all);
            _bestMatchLinks = null;
            var unticked = Selection.SetLinked(_linked) | Selection.Refresh(all);
            await LoadRecentAsync();
            Update();
            if (unticked)
            {
                RaiseSelectionChanged();
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    protected override void OnDataChanged(DataChangedEventArgs e) => _ = LoadAsync();

    /// <summary>A click on a chip: a type's chip adds or removes its type, All (null) clears them.</summary>
    public async void ToggleChip(RecordType? type)
    {
        _selectedChips = SearchLogic.ToggleChip(_selectedChips, type);
        Update();
        await LoadRecentAsync();
        if (!IsSearching)
        {
            Update();
        }
    }

    /// <summary>Ticks or unticks a record in the picker; records already linked can't be ticked.</summary>
    public void SetChecked(SearchResult record, bool ticked)
    {
        if (Selection.Set(record, ticked))
        {
            RaiseSelectionChanged();
        }
        else
        {
            SyncChecks();
        }
    }

    /// <summary>Links every ticked record, shown or not, to the source.</summary>
    public async Task LinkSelectedAsync()
    {
        if (LinkSource is not { } source || !CanLink)
        {
            return;
        }
        _isLinking = true;
        OnPropertyChanged(nameof(CanLink));
        try
        {
            await Data.LinkedRecords.AddLinkedRecordsAsync(source.Id, source.Type, Selection.ToLink());
            Selection.Clear();
            RaiseSelectionChanged();
        }
        finally
        {
            _isLinking = false;
            OnPropertyChanged(nameof(CanLink));
        }
    }

    private async Task LoadRecentAsync()
    {
        var version = ++_recentVersion;
        var types = _chipTypes.Where(t => SearchLogic.ChipsShow(_selectedChips, t)).ToList();
        IReadOnlyCollection<string> excluded = LinkSource is { } source ? [.. _linked, source.Id] : [];
        var recent = await Data.Search.GetRecentlyChangedAsync(types, excluded, SearchLogic.RecentLimit);
        if (version == _recentVersion)
        {
            _recent = recent;
        }
    }

    // ---------------------------------------------------------------- Layout

    private void Update()
    {
        var query = Query.Trim();
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasHint));

        if (!IsSearching)
        {
            UpdateChips(null);
            BestMatch = null;
            NoMatchText = "";
            MoreTypes = [];
            MoreCount = 0;
            CreateOffer = CreateOffer.None;
            var now = EpochTime.UtcNowMillis();
            var recent = _recent
                .Where(r => SearchLogic.ChipsShow(_selectedChips, r.RecordType))
                .Select(r => new SearchRow(r, "", SearchLogic.ChangedText(r.UpdatedAt ?? now, now, TimeZoneInfo.Local))
                {
                    ShowsCheck = IsPicker,
                    IsChecked = Selection.Contains(r.RecordId),
                })
                .ToList();
            Groups = recent.Count > 0 ? [new SearchRowGroup("Recently changed", recent)] : [];
            return;
        }

        var matches = _index.Find(query);
        var view = SearchLogic.Layout(matches, _selectedChips, _chipTypes, r => !_linked.Contains(r.RecordId));
        UpdateChips(view);

        BestMatch = view.BestMatch is { } best ? Row(best, query) : null;
        Groups = view.Groups
            .Select(g => new SearchRowGroup(g.Type.PluralDisplayName(), g.Results.Select(r => Row(r, query)), g.Type))
            .ToList();
        NoMatchText = view.IsEmpty && view.HiddenTotal > 0
            ? $"No {SearchLogic.SelectedNames(_selectedChips)} match \"{query}\"."
            : "";
        var showsMore = Scope != null && view.HiddenTotal > 0;
        MoreTypes = showsMore ? _chipTypes.Where(view.Hidden.ContainsKey).ToList() : [];
        MoreCount = showsMore ? view.HiddenTotal : 0;
        CreateOffer = new CreateOffer(query, CreateFromSearch.OfferFor(query, matches));
        _ = LoadBestMatchLinksAsync(BestMatch);
    }

    private SearchRow Row(SearchResult result, string query) =>
        SearchRow.For(result, query, showsCheck: IsPicker, isLinked: _linked.Contains(result.RecordId), isChecked: Selection.Contains(result.RecordId));

    private void UpdateChips(SearchResultsView? view)
    {
        foreach (var chip in Chips)
        {
            chip.IsSelected = chip.Type is { } type ? _selectedChips.Contains(type) : _selectedChips.Count == 0;
            chip.Count = view == null ? null : chip.Type is { } t ? view.Counts.GetValueOrDefault(t) : view.Total;
        }
    }

    /// <summary>The best match's links per type, in type order; kept while the best match stays the same record.</summary>
    private async Task LoadBestMatchLinksAsync(SearchRow? row)
    {
        if (row == null)
        {
            return;
        }
        if (_bestMatchLinks is { } cached && cached.Id == row.Id)
        {
            row.LinkCounts = cached.Counts;
            return;
        }
        var counts = await Data.LinkedRecords.GetLinkedRecordTypeCountsAsync(row.Id);
        var views = SearchLogic.TypeOrder
            .Select(t => new TypeLinkCountView(t, counts.Where(c => c.RecordType == t).Sum(c => c.Count)))
            .Where(c => c.Count > 0)
            .ToList();
        _bestMatchLinks = (row.Id, views);
        if (BestMatch is { } current && current.Id == row.Id)
        {
            current.LinkCounts = views;
        }
    }

    private void RaiseSelectionChanged()
    {
        SyncChecks();
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(CanLink));
        OnPropertyChanged(nameof(LinkButtonText));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the ticks on the rows on screen.</summary>
    private void SyncChecks()
    {
        foreach (var row in Groups.SelectMany(g => g).Append(BestMatch).OfType<SearchRow>())
        {
            row.IsChecked = Selection.Contains(row.Id);
        }
    }

    private static string AndList(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list[..^1]) + " and " + list[^1];
    }
}
