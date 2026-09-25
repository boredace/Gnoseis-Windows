/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.App.ViewModels;

/// <summary>The list of a section: all records of one type, grouped, with a quick filter.</summary>
public sealed class RecordListViewModel(RecordType type) : PageViewModel
{
    private List<RecordGroup> _allGroups = [];
    private List<RecordGroup> _groups = [];
    private string _filter = "";
    private int _count;
    private int _visibleCount;
    private bool _isLoaded;
    private int _loadVersion;

    public RecordType Type { get; } = type;
    public string Title => Type.PluralDisplayName();
    public string Glyph => Glyphs.For(Type);
    public string TypeNameLower => Type.DisplayName().ToLowerInvariant();
    public string NewButtonText => $"New {TypeNameLower}";
    public string FilterPlaceholder => $"Filter {Title.ToLowerInvariant()}";
    public string EmptyText => $"No {Title.ToLowerInvariant()} yet";
    public string EmptyHint => $"Select \"{NewButtonText}\" or press Ctrl+N to add one.";
    public string NoSelectionText => $"Select {(Type == RecordType.Organization || Type == RecordType.Item ? "an" : "a")} {TypeNameLower} to see it here";

    /// <summary>Raised after the groups changed (load or filter).</summary>
    public event EventHandler? GroupsChanged;

    public List<RecordGroup> Groups
    {
        get => _groups;
        private set => SetProperty(ref _groups, value);
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? ""))
            {
                ApplyFilter();
            }
        }
    }

    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public string CountText =>
        Filter.Trim().Length > 0 ? $"{_visibleCount} of {Count}" :
        Count == 1 ? $"1 {TypeNameLower}" : $"{Count} {Title.ToLowerInvariant()}";

    public bool IsLoaded
    {
        get => _isLoaded;
        private set
        {
            if (SetProperty(ref _isLoaded, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool IsEmpty => IsLoaded && Count == 0;

    public bool IsFilteredEmpty => IsLoaded && Count > 0 && _visibleCount == 0;

    public RecordRow? FindRow(string id) => Groups.SelectMany(g => g).FirstOrDefault(r => r.Id == id);

    /// <summary>All visible rows in list order.</summary>
    public List<RecordRow> VisibleRows => Groups.SelectMany(g => g).ToList();

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var (groups, count) = await LoadGroupsAsync();
            if (version != _loadVersion)
            {
                return;
            }
            _allGroups = groups;
            Count = count;
            IsLoaded = true;
            ApplyFilter();
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private void ApplyFilter()
    {
        var filter = Filter.Trim();
        Groups = filter.Length == 0
            ? _allGroups
            : _allGroups
                .Select(g => new RecordGroup(g.Header, g.Where(r =>
                    r.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                    r.Subtitle.Contains(filter, StringComparison.CurrentCultureIgnoreCase))))
                .Where(g => g.Count > 0)
                .ToList();
        _visibleCount = Groups.Sum(g => g.Count);
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<(List<RecordGroup>, int)> LoadGroupsAsync()
    {
        switch (Type)
        {
            case RecordType.Note:
                var notes = await Data.Notes.GetNotesAsync();
                return (RecordRows.GroupNotes(notes), notes.Count);
            case RecordType.Contact:
                var contacts = await Data.Contacts.GetContactsAsync();
                return (RecordRows.GroupByFirstLetter(contacts.Select(c => RecordRows.FromContact(c))), contacts.Count);
            case RecordType.Organization:
                var organizations = await Data.Organizations.GetOrganizationsAsync();
                return (RecordRows.GroupByFirstLetter(organizations.Select(o => RecordRows.FromOrganization(o))), organizations.Count);
            case RecordType.Category:
                var categories = await Data.Categories.GetCategoriesAsync();
                return (RecordRows.GroupByFirstLetter(categories.Select(c => RecordRows.FromCategory(c))), categories.Count);
            case RecordType.Item:
                var items = await Data.Items.GetItemsAsync();
                return (RecordRows.GroupByFirstLetter(items.Select(i => RecordRows.FromItem(i))), items.Count);
            default:
                return (new List<RecordGroup>(), 0);
        }
    }

    protected override void OnDataChanged(DataChangedEventArgs e)
    {
        if (e.Affects(Type))
        {
            _ = LoadAsync();
        }
    }
}
