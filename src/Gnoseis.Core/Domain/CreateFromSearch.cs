/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Text.RegularExpressions;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>Where a search offers to create a record named after its query.</summary>
public enum CreateFromSearchOffer
{
    /// <summary>Nothing to offer: the query is too short, or a result matches it exactly.</summary>
    None,

    /// <summary>No results: the offer replaces the list.</summary>
    NoResults,

    /// <summary>Results, but none matches exactly: the offer is the list's last item.</summary>
    AfterResults,
}

/// <summary>A new contact's names, prefilled from a search.</summary>
public sealed record ContactNamePrefill(string? NameFirst, string NameLast);

/// <summary>
/// Creating a record from a search that found no record with that name (same rules as Gnoseis for
/// Android): when to offer it, which types to offer, and the name the new record starts with.
/// </summary>
public static partial class CreateFromSearch
{
    /// <summary>The search results page and the link picker offer to create from this many characters on.</summary>
    public const int MinimumQueryLength = 3;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary><paramref name="text"/> as search compares it: trimmed, runs of spaces as one, lower case.</summary>
    public static string Normalize(string? text) =>
        Spaces().Replace((text ?? "").Trim(), " ").ToLower(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>A contact's title shows as "Last, First"; this is it as "First Last". Null for other records.</summary>
    public static string? FirstLastTitle(SearchResult result)
    {
        var title = result.RecordTitle;
        var comma = title.IndexOf(", ", StringComparison.Ordinal);
        return result.RecordType == RecordType.Contact && comma >= 0
            ? $"{title[(comma + 2)..]} {title[..comma]}"
            : null;
    }

    /// <summary>
    /// Whether <paramref name="title"/> contains <paramref name="query"/>, ignoring case, the spaces
    /// around the query and runs of spaces, so a title that matches exactly is always found.
    /// </summary>
    public static bool TitleContains(string? title, string? query) =>
        Normalize(title).Contains(Normalize(query), StringComparison.Ordinal);

    /// <summary>Whether <paramref name="result"/> is found by <paramref name="query"/>; a contact also as "First Last".</summary>
    public static bool ResultContains(SearchResult result, string? query) =>
        TitleContains(result.RecordTitle, query) || (FirstLastTitle(result) is { } firstLast && TitleContains(firstLast, query));

    /// <summary>
    /// Whether <paramref name="title"/> is <paramref name="query"/>: the same once trimmed, with runs of
    /// spaces as one and ignoring case. A title that only contains the query doesn't match.
    /// </summary>
    public static bool MatchesExactly(string? title, string? query) =>
        title != null && Normalize(query).Length > 0 && Normalize(title) == Normalize(query);

    /// <summary>Whether <paramref name="result"/> is the record <paramref name="query"/> names; a contact also as "First Last".</summary>
    public static bool MatchesExactly(SearchResult result, string? query) =>
        MatchesExactly(result.RecordTitle, query) || MatchesExactly(FirstLastTitle(result), query);

    /// <summary>What to offer for a finished search of <paramref name="query"/> that found <paramref name="results"/>.</summary>
    public static CreateFromSearchOffer OfferFor(string? query, IReadOnlyCollection<SearchResult> results)
    {
        if ((query ?? "").Trim().Length < MinimumQueryLength)
        {
            return CreateFromSearchOffer.None;
        }
        if (results.Count == 0)
        {
            return CreateFromSearchOffer.NoResults;
        }
        return results.Any(r => MatchesExactly(r, query)) ? CreateFromSearchOffer.None : CreateFromSearchOffer.AfterResults;
    }

    /// <summary>
    /// The types a search can create, in the order their buttons show: all of them on the search
    /// results page (no <paramref name="source"/>), all but the source's type in the link picker.
    /// </summary>
    public static IReadOnlyList<RecordType> TypesFor(RecordType? source) =>
        RecordTypeExtensions.UserTypes.Where(t => t != source).ToList();

    /// <summary>The length limit of a type's title field.</summary>
    public static int TitleLengthOf(RecordType type) => type switch
    {
        RecordType.Note => TitleLength.Note,
        RecordType.Contact => TitleLength.Contact,
        RecordType.Organization => TitleLength.Organization,
        RecordType.Category => TitleLength.Category,
        _ => TitleLength.Item,
    };

    /// <summary>The name a new note, organization, category or item starts with: the trimmed query, cut to the title length. Null when empty.</summary>
    public static string? PrefillName(string? query, RecordType type)
    {
        var name = (query ?? "").Trim();
        name = name.Length > TitleLengthOf(type) ? name[..TitleLengthOf(type)].TrimEnd() : name;
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// A new contact's names: a query typed as "Last, First" is split at the comma; otherwise one word is
    /// the last name (the required field), and with more words the last one is the last name and the
    /// rest the first name. The last name is cut to its length limit. Null when the query is empty.
    /// </summary>
    public static ContactNamePrefill? PrefillContactName(string? query)
    {
        var text = Spaces().Replace((query ?? "").Trim(), " ");
        var comma = text.IndexOf(',');
        if (comma >= 0)
        {
            var last = text[..comma].Trim();
            var first = text[(comma + 1)..].Trim();
            if (last.Length > 0)
            {
                return new ContactNamePrefill(first.Length > 0 ? first : null, CutLastName(last));
            }
            text = first;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }
        var nameFirst = string.Join(' ', words[..^1]);
        return new ContactNamePrefill(nameFirst.Length > 0 ? nameFirst : null, CutLastName(words[^1]));
    }

    private static string CutLastName(string name) =>
        name.Length > TitleLength.Contact ? name[..TitleLength.Contact].TrimEnd() : name;
}
