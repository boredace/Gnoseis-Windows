/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>
/// Every record search can find, loaded once, with each title already normalized (and a contact's
/// "First Last" form), so each keystroke only compares strings and ranks the matches.
/// </summary>
public sealed class SearchIndex
{
    private readonly Entry[] _entries;

    private readonly record struct Entry(SearchResult Result, string Title, string? FirstLast);

    public SearchIndex(IEnumerable<SearchResult> results)
    {
        _entries = results
            .Select(r => new Entry(r, CreateFromSearch.Normalize(r.RecordTitle),
                CreateFromSearch.FirstLastTitle(r) is { } firstLast ? CreateFromSearch.Normalize(firstLast) : null))
            .ToArray();
        All = _entries.Select(e => e.Result).ToList();
    }

    public static SearchIndex Empty { get; } = new([]);

    /// <summary>Every record, in load order.</summary>
    public IReadOnlyList<SearchResult> All { get; }

    /// <summary>
    /// The records <paramref name="query"/> finds (title contains it, ignoring case and runs of spaces;
    /// a contact also as "First Last"), ranked best first (see <see cref="SearchLogic.Rank(IEnumerable{SearchResult}, string?)"/>).
    /// Nothing for an empty query.
    /// </summary>
    public List<SearchResult> Find(string? query)
    {
        var normalized = CreateFromSearch.Normalize(query);
        if (normalized.Length == 0)
        {
            return [];
        }
        var matches = new List<(SearchResult, MatchTier?)>();
        foreach (var entry in _entries)
        {
            if (SearchLogic.TierOf(entry.Title, entry.FirstLast, normalized) is { } tier)
            {
                matches.Add((entry.Result, tier));
            }
        }
        return SearchLogic.Rank(matches);
    }
}
