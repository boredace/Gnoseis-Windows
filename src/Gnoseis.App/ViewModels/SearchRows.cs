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
/// One search result as the search page, the link picker and the title bar show it: the type tile
/// (or contact picture), the title with the query's matches highlighted, and a context line.
/// </summary>
public sealed class SearchRow(SearchResult result, string query, string context) : BindableBase
{
    private bool _isChecked;
    private IReadOnlyList<TypeLinkCountView> _linkCounts = [];

    public SearchResult Result { get; } = result;
    public string Id => Result.RecordId;
    public RecordType Type => Result.RecordType;
    public string Title { get; } = result.RecordTitle;
    public IReadOnlyList<TextRange> Highlights { get; } = SearchLogic.HighlightRanges(result.RecordTitle, query);
    public string Context { get; } = context;

    /// <summary>The type name and context line (the best match card).</summary>
    public string TypeAndContext => $"{TypeName} · {Context}";

    public string TypeName => Type.DisplayName();
    public bool IsPerson => Type == RecordType.Contact;
    public bool ShowTile => !IsPerson;

    /// <summary>In the link picker: rows have a check box.</summary>
    public bool ShowsCheck { get; init; }

    /// <summary>In the link picker: the record is linked to the source already (dimmed, can't be ticked).</summary>
    public bool IsLinked { get; init; }

    public bool CanCheck => ShowsCheck && !IsLinked;
    public double RowOpacity => IsLinked ? 0.6 : 1;

    /// <summary>In the link picker: the record is ticked.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    /// <summary>The best match's links per type, in type order (loaded after the card is shown).</summary>
    public IReadOnlyList<TypeLinkCountView> LinkCounts
    {
        get => _linkCounts;
        set
        {
            if (SetProperty(ref _linkCounts, value))
            {
                OnPropertyChanged(nameof(HasLinkCounts));
            }
        }
    }

    public bool HasLinkCounts => LinkCounts.Count > 0;

    /// <summary>Text read by screen readers: the plain title, type and context.</summary>
    public string AutomationName => IsLinked ? $"{TypeName}: {Title}. {Context}. Already linked" : $"{TypeName}: {Title}. {Context}";

    /// <summary>The best match card as screen readers read it.</summary>
    public string BestMatchAutomationName => $"Best match, {TypeName}: {Title}. {Context}";

    public RecordRef ToRef() => new(Type, Id);

    /// <summary>Shown in the title bar search box when a suggestion is highlighted with the keyboard.</summary>
    public override string ToString() => Title;

    /// <summary>A result row with its context line (see <see cref="SearchLogic.ContextLine"/>).</summary>
    public static SearchRow For(SearchResult result, string query, bool showsCheck = false, bool isLinked = false, bool isChecked = false) =>
        new(result, query, SearchLogic.ContextLine(result)) { ShowsCheck = showsCheck, IsLinked = isLinked, IsChecked = isChecked };
}

/// <summary>One type's link count on the best match card: the type glyph in its colour and the count.</summary>
public sealed record TypeLinkCountView(RecordType Type, int Count)
{
    public string Glyph => Glyphs.For(Type);
    public string CountText => Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string AutomationName => $"{Count} {(Count == 1 ? Type.DisplayName() : Type.PluralDisplayName()).ToLowerInvariant()}";
}

/// <summary>A group of search rows under a sticky header: a record type with its row count, or "Recently changed".</summary>
public sealed class SearchRowGroup(string header, IEnumerable<SearchRow> rows, RecordType? type = null) : List<SearchRow>(rows)
{
    public string Header { get; } = header;
    public RecordType? Type { get; } = type;
    public bool HasType => Type != null;
    public string Glyph => Type is { } t ? Glyphs.For(t) : "";
    public RecordType GlyphType => Type ?? RecordType.LinkedRecord;

    /// <summary>"· 3" for a type's group; empty for Recently changed.</summary>
    public string CountText => Type != null ? $"· {Count}" : "";

    public string AutomationName => Type != null ? $"{Header}, {Count}" : Header;
}

/// <summary>A filter chip: All (no type) or one type, with its number of matches once a search runs.</summary>
public sealed class SearchChip(RecordType? type) : BindableBase
{
    private int? _count;
    private bool _isSelected;

    public RecordType? Type { get; } = type;
    public string Name => Type?.PluralDisplayName() ?? "All";

    /// <summary>Matches of this type (all matches for All); null before a search.</summary>
    public int? Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string AutomationName => Count is { } c ? $"{Name}, {c} {(c == 1 ? "match" : "matches")}" : Name;
}
