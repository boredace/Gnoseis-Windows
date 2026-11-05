/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using System.Text.RegularExpressions;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>How well a title matches a query, best first.</summary>
public enum MatchTier
{
    /// <summary>The title is the query.</summary>
    Exact,

    /// <summary>The title starts with the query.</summary>
    StartsWith,

    /// <summary>A word in the title starts with the query (a contact: in either name order).</summary>
    WordStart,

    /// <summary>The title contains the query somewhere (a contact: in either name order).</summary>
    Contains,
}

/// <summary>A part of a title, by start index and length.</summary>
public readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>One type's results, as a group under the best match.</summary>
public sealed record SearchGroup(RecordType Type, IReadOnlyList<SearchResult> Results);

/// <summary>Search results laid out for the page: the best match, then one group per type.</summary>
public sealed record SearchResultsView(
    IReadOnlyDictionary<RecordType, int> Counts,
    SearchResult? BestMatch,
    IReadOnlyList<SearchGroup> Groups,
    IReadOnlyDictionary<RecordType, int> Hidden)
{
    public static SearchResultsView Empty { get; } = new(new Dictionary<RecordType, int>(), null, [], new Dictionary<RecordType, int>());

    /// <summary>All matches, whatever the chips show.</summary>
    public int Total => Counts.Values.Sum();

    /// <summary>Nothing to show for the chips selected.</summary>
    public bool IsEmpty => BestMatch == null && Groups.Count == 0;

    /// <summary>Matches of the types the chips hide.</summary>
    public int HiddenTotal => Hidden.Values.Sum();
}

/// <summary>
/// Search as Gnoseis for Android does it: the ranking, the filter chips, the layout of the results,
/// the highlighted parts of a title and the context line under it.
/// </summary>
public static partial class SearchLogic
{
    /// <summary>Records before typing (recently changed).</summary>
    public const int RecentLimit = 5;

    /// <summary>Title bar suggestions.</summary>
    public const int SuggestionLimit = 10;

    /// <summary>Types in the order of the chips and the groups.</summary>
    public static IReadOnlyList<RecordType> TypeOrder => RecordTypeExtensions.UserTypes;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Whether <paramref name="query"/> is long enough to search for.</summary>
    public static bool IsSearchQuery(string? query) => (query ?? "").Trim().Length >= CreateFromSearch.MinimumQueryLength;

    // ---------------------------------------------------------------- Ranking

    /// <summary>How <paramref name="result"/> matches <paramref name="query"/>; null when it doesn't.</summary>
    public static MatchTier? TierOf(SearchResult result, string? query) =>
        TierOf(CreateFromSearch.Normalize(result.RecordTitle),
            CreateFromSearch.FirstLastTitle(result) is { } firstLast ? CreateFromSearch.Normalize(firstLast) : null,
            CreateFromSearch.Normalize(query));

    /// <summary>
    /// The tier for a normalized title, its "First Last" form for a contact, and a normalized query.
    /// </summary>
    internal static MatchTier? TierOf(string title, string? firstLast, string query)
    {
        if (query.Length == 0)
        {
            return null;
        }
        if (title == query || firstLast == query)
        {
            return MatchTier.Exact;
        }
        if (title.StartsWith(query, StringComparison.Ordinal))
        {
            return MatchTier.StartsWith;
        }
        if (StartsAtWord(title, query) || (firstLast != null && StartsAtWord(firstLast, query)))
        {
            return MatchTier.WordStart;
        }
        if (title.Contains(query, StringComparison.Ordinal) || (firstLast?.Contains(query, StringComparison.Ordinal) ?? false))
        {
            return MatchTier.Contains;
        }
        return null;
    }

    private static bool StartsAtWord(string text, string query)
    {
        var index = text.IndexOf(query, StringComparison.Ordinal);
        while (index >= 0)
        {
            if (index == 0 || !char.IsLetterOrDigit(text[index - 1]))
            {
                return true;
            }
            index = text.IndexOf(query, index + 1, StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// <paramref name="results"/> best first: by tier, then more links first, then title A–Z ignoring
    /// case, then id. Results that don't match go last.
    /// </summary>
    public static List<SearchResult> Rank(IEnumerable<SearchResult> results, string? query)
    {
        var normalized = CreateFromSearch.Normalize(query);
        return Rank(results.Select(r => (r, TierOf(
            CreateFromSearch.Normalize(r.RecordTitle),
            CreateFromSearch.FirstLastTitle(r) is { } firstLast ? CreateFromSearch.Normalize(firstLast) : null,
            normalized))));
    }

    /// <summary>Sorts results whose tiers are known.</summary>
    internal static List<SearchResult> Rank(IEnumerable<(SearchResult Result, MatchTier? Tier)> results) =>
        results
            .OrderBy(r => r.Tier ?? (MatchTier)int.MaxValue)
            .ThenByDescending(r => r.Result.LinkCount)
            .ThenBy(r => r.Result.RecordTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Result.RecordId, StringComparer.Ordinal)
            .Select(r => r.Result)
            .ToList();

    // ---------------------------------------------------------------- Filter chips

    /// <summary>
    /// The chips after a click on a type's chip (adds or removes it) or on All (null: clears them,
    /// which shows every type).
    /// </summary>
    public static IReadOnlySet<RecordType> ToggleChip(IReadOnlySet<RecordType> selected, RecordType? type)
    {
        if (type is not { } t)
        {
            return new HashSet<RecordType>();
        }
        var next = new HashSet<RecordType>(selected);
        if (!next.Remove(t))
        {
            next.Add(t);
        }
        return next;
    }

    /// <summary>Whether the chips show results of <paramref name="type"/>: none selected shows all.</summary>
    public static bool ChipsShow(IReadOnlySet<RecordType> selected, RecordType type) => selected.Count == 0 || selected.Contains(type);

    /// <summary>The chip types in order, without <paramref name="excluded"/> (the link source's type).</summary>
    public static IReadOnlyList<RecordType> ChipTypes(RecordType? excluded = null) => TypeOrder.Where(t => t != excluded).ToList();

    /// <summary>The chips selected at the start: the section's type when there is one and it has a chip.</summary>
    public static IReadOnlySet<RecordType> StartingChips(RecordType? scope, IReadOnlyList<RecordType> chipTypes) =>
        scope is { } s && chipTypes.Contains(s) ? new HashSet<RecordType> { s } : new HashSet<RecordType>();

    /// <summary>The types create from search offers, with the section's type first.</summary>
    public static IReadOnlyList<RecordType> CreateTypesWithFirst(IReadOnlyList<RecordType> types, RecordType? first) =>
        first is { } f && types.Contains(f) ? [f, .. types.Where(t => t != f)] : types;

    /// <summary>"contacts", "notes or contacts", "notes, contacts or items"; "records" for none.</summary>
    public static string SelectedNames(IEnumerable<RecordType> types)
    {
        var set = types.ToHashSet();
        var names = TypeOrder.Where(set.Contains).Select(t => t.PluralDisplayName().ToLowerInvariant()).ToList();
        return names.Count switch
        {
            0 => "records",
            1 => names[0],
            _ => string.Join(", ", names[..^1]) + " or " + names[^1],
        };
    }

    // ---------------------------------------------------------------- Layout

    /// <summary>
    /// Lays out <paramref name="ranked"/> results (best first) for the selected chips: match counts
    /// per chip type (all matches), the best match (the first visible result <paramref name="canBeBest"/>
    /// accepts; not repeated in its group), one group per visible type in chip order, and the number
    /// of matches the chips hide per type.
    /// </summary>
    public static SearchResultsView Layout(IReadOnlyList<SearchResult> ranked, IReadOnlySet<RecordType> selected,
        IReadOnlyList<RecordType> chipTypes, Func<SearchResult, bool>? canBeBest = null)
    {
        var byType = ranked.GroupBy(r => r.RecordType).ToDictionary(g => g.Key, g => g.ToList());
        var best = ranked.FirstOrDefault(r => chipTypes.Contains(r.RecordType) && ChipsShow(selected, r.RecordType) && (canBeBest?.Invoke(r) ?? true));
        var counts = chipTypes.ToDictionary(t => t, t => byType.GetValueOrDefault(t)?.Count ?? 0);
        var groups = chipTypes
            .Where(t => ChipsShow(selected, t))
            .Select(t => new SearchGroup(t, (byType.GetValueOrDefault(t) ?? []).Where(r => r.RecordId != best?.RecordId).ToList()))
            .Where(g => g.Results.Count > 0)
            .ToList();
        var hidden = chipTypes
            .Where(t => !ChipsShow(selected, t) && byType.ContainsKey(t))
            .ToDictionary(t => t, t => byType[t].Count);
        return new SearchResultsView(counts, best, groups, hidden);
    }

    // ---------------------------------------------------------------- Highlight

    /// <summary>
    /// Where <paramref name="query"/> occurs in <paramref name="title"/>, ignoring case and with any run
    /// of spaces between its words: every occurrence. When the whole query isn't there (a contact found
    /// as "First Last"), each word wherever it occurs, with touching or overlapping parts merged.
    /// </summary>
    public static List<TextRange> HighlightRanges(string title, string? query)
    {
        var words = Spaces().Split((query ?? "").Trim()).Where(w => w.Length > 0).ToList();
        if (words.Count == 0 || title.Length == 0)
        {
            return [];
        }
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var whole = new Regex(string.Join(@"\s+", words.Select(Regex.Escape)), options);
        var ranges = whole.Matches(title).Select(m => new TextRange(m.Index, m.Length)).ToList();
        if (ranges.Count > 0 || words.Count == 1)
        {
            return ranges;
        }

        var merged = new List<TextRange>();
        foreach (var range in words
                     .SelectMany(w => new Regex(Regex.Escape(w), options).Matches(title).Select(m => new TextRange(m.Index, m.Length)))
                     .OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = new TextRange(last.Start, Math.Max(last.End, range.End) - last.Start);
            }
            else
            {
                merged.Add(range);
            }
        }
        return merged;
    }

    // ---------------------------------------------------------------- Context line and times

    /// <summary>"No links", "1 link", "N links".</summary>
    public static string LinksText(int count) => count switch
    {
        0 => "No links",
        1 => "1 link",
        _ => $"{count} links",
    };

    /// <summary>A note's day (createDate, read in UTC) as yyyy-MM-dd.</summary>
    public static string NoteDayText(long noteDate) =>
        EpochTime.ToUtcDate(noteDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The line under a result's title: a note's day, a contact's job title and company (whichever are
    /// set, joined by ", "), then its links.
    /// </summary>
    public static string ContextLine(SearchResult result)
    {
        var details = result.RecordType switch
        {
            RecordType.Note => result.NoteDate is { } day ? NoteDayText(day) : null,
            RecordType.Contact => string.Join(", ", new[] { result.JobTitle, result.Company }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())),
            _ => null,
        };
        return string.IsNullOrEmpty(details) ? LinksText(result.LinkCount) : $"{details} · {LinksText(result.LinkCount)}";
    }

    /// <summary>
    /// How long ago <paramref name="time"/> was at <paramref name="now"/> (UTC epoch ms), with days in
    /// <paramref name="zone"/>: "just now", "N minutes ago", "N hours ago" the same day, "yesterday",
    /// "N days ago", "last week", then the date.
    /// </summary>
    public static string RelativeTime(long time, long now, TimeZoneInfo zone)
    {
        var minutes = (long)Math.Floor((now - time) / 60_000.0);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(EpochTime.ToUtc(time), zone).DateTime);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(EpochTime.ToUtc(now), zone).DateTime);
        var days = today.DayNumber - day.DayNumber;
        return minutes switch
        {
            < 1 => "just now",
            1 => "1 minute ago",
            < 60 => $"{minutes} minutes ago",
            _ when days <= 0 => minutes / 60 == 1 ? "1 hour ago" : $"{minutes / 60} hours ago",
            _ when days == 1 => "yesterday",
            _ when days < 7 => $"{days} days ago",
            _ when days < 14 => "last week",
            _ => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>"Changed …" for a recently changed record (see <see cref="RelativeTime"/>).</summary>
    public static string ChangedText(long updatedAt, long now, TimeZoneInfo zone) => $"Changed {RelativeTime(updatedAt, now, zone)}";
}
