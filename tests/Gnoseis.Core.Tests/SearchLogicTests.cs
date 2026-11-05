/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

public class SearchLogicTests
{
    private static SearchResult R(string id, RecordType type, string title, int links = 0) => new(id, (int)type, title, links);

    private static readonly IReadOnlySet<RecordType> None = new HashSet<RecordType>();

    private static IReadOnlySet<RecordType> Chips(params RecordType[] types) => types.ToHashSet();

    // ---------------------------------------------------------------- Ranking

    [Fact]
    public void Tiers_are_exact_starts_with_word_start_then_contains()
    {
        Assert.Equal(MatchTier.Exact, SearchLogic.TierOf(R("1", RecordType.Organization, "Harbour"), "harbour"));
        Assert.Equal(MatchTier.StartsWith, SearchLogic.TierOf(R("2", RecordType.Organization, "Harbour Labs"), "harbour"));
        Assert.Equal(MatchTier.WordStart, SearchLogic.TierOf(R("3", RecordType.Organization, "Old Harbour Coffee"), "harbour"));
        Assert.Equal(MatchTier.WordStart, SearchLogic.TierOf(R("4", RecordType.Organization, "Old-harbour"), "harbour"));
        Assert.Equal(MatchTier.Contains, SearchLogic.TierOf(R("5", RecordType.Note, "Ideas for Northharbour"), "harbour"));
        Assert.Null(SearchLogic.TierOf(R("6", RecordType.Note, "Harbor"), "harbour"));
        Assert.Null(SearchLogic.TierOf(R("7", RecordType.Note, "Harbour"), "  "));
    }

    [Fact]
    public void A_contact_matches_in_either_name_order()
    {
        var contact = R("c", RecordType.Contact, "Harris, Owen");

        Assert.Equal(MatchTier.Exact, SearchLogic.TierOf(contact, "owen harris"));
        Assert.Equal(MatchTier.Exact, SearchLogic.TierOf(contact, "Harris, Owen"));
        Assert.Equal(MatchTier.StartsWith, SearchLogic.TierOf(contact, "harr"));
        Assert.Equal(MatchTier.WordStart, SearchLogic.TierOf(contact, "owen har"));
        Assert.Equal(MatchTier.WordStart, SearchLogic.TierOf(contact, "owe"));
        Assert.Equal(MatchTier.Contains, SearchLogic.TierOf(contact, "wen ha"));
    }

    [Fact]
    public void Case_and_spaces_are_ignored()
    {
        Assert.Equal(MatchTier.Exact, SearchLogic.TierOf(R("1", RecordType.Item, "Standing  desk"), "  STANDING desk "));
        Assert.Equal(MatchTier.StartsWith, SearchLogic.TierOf(R("2", RecordType.Item, "Standing desk, oak"), "standing   DESK"));
    }

    [Fact]
    public void Ranking_orders_by_tier_then_links_then_title_then_id()
    {
        var results = new[]
        {
            R("contains", RecordType.Note, "Northharbour", 50),
            R("word", RecordType.Organization, "Old Harbour", 9),
            R("starts-b", RecordType.Note, "harbour b", 2),
            R("starts-a2", RecordType.Note, "Harbour a", 2),
            R("starts-a1", RecordType.Item, "Harbour A", 2),
            R("starts-many", RecordType.Item, "Harbour z", 7),
            R("exact", RecordType.Category, "Harbour", 0),
        };

        var ranked = SearchLogic.Rank(results, "harbour");

        Assert.Equal(new[] { "exact", "starts-many", "starts-a1", "starts-a2", "starts-b", "word", "contains" }, ranked.Select(r => r.RecordId));
    }

    [Fact]
    public void The_index_finds_and_ranks_like_rank()
    {
        var results = new[]
        {
            R("1", RecordType.Note, "Call with Owen Harris"),
            R("2", RecordType.Contact, "Harris, Owen", 3),
            R("3", RecordType.Organization, "Harbor"),
            R("4", RecordType.Item, "harris"),
        };
        var index = new SearchIndex(results);

        Assert.Equal(new[] { "4", "2", "1" }, index.Find("Harris").Select(r => r.RecordId));
        Assert.Equal(new[] { "2", "1" }, index.Find(" owen  harris ").Select(r => r.RecordId));
        Assert.Empty(index.Find(" "));
        Assert.Equal(4, index.All.Count);
    }

    [Fact]
    public void The_index_finds_what_title_contains_finds()
    {
        var results = new[]
        {
            R("1", RecordType.Contact, "Smithers, Waylon"),
            R("2", RecordType.Organization, "Acme  Ltd"),
            R("3", RecordType.Note, "Walk in the park"),
        };
        var index = new SearchIndex(results);

        foreach (var query in new[] { "waylon smith", "acme ltd", "ALK", "thers, Way", "k in" })
        {
            Assert.Equal(results.Where(r => CreateFromSearch.ResultContains(r, query)).Select(r => r.RecordId).Order(),
                index.Find(query).Select(r => r.RecordId).Order());
        }
    }

    // ---------------------------------------------------------------- Chips

    [Fact]
    public void Chips_are_multi_select_and_all_clears_them()
    {
        var one = SearchLogic.ToggleChip(None, RecordType.Contact);
        var two = SearchLogic.ToggleChip(one, RecordType.Item);
        var back = SearchLogic.ToggleChip(two, RecordType.Contact);

        Assert.Equal(new[] { RecordType.Contact }, one);
        Assert.Equal(new[] { RecordType.Contact, RecordType.Item }, two.Order());
        Assert.Equal(new[] { RecordType.Item }, back);
        Assert.Empty(SearchLogic.ToggleChip(two, null));
        Assert.True(SearchLogic.ChipsShow(None, RecordType.Note));
        Assert.False(SearchLogic.ChipsShow(two, RecordType.Note));
        Assert.True(SearchLogic.ChipsShow(two, RecordType.Item));
    }

    [Fact]
    public void Starting_chips_and_chip_types()
    {
        Assert.Equal(new[] { RecordType.Note, RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item }, SearchLogic.ChipTypes());
        Assert.Equal(new[] { RecordType.Note, RecordType.Organization, RecordType.Category, RecordType.Item }, SearchLogic.ChipTypes(RecordType.Contact));
        Assert.Equal(new[] { RecordType.Contact }, SearchLogic.StartingChips(RecordType.Contact, SearchLogic.ChipTypes()));
        Assert.Empty(SearchLogic.StartingChips(null, SearchLogic.ChipTypes()));
        Assert.Empty(SearchLogic.StartingChips(RecordType.Contact, SearchLogic.ChipTypes(RecordType.Contact)));
    }

    [Fact]
    public void Selected_names_read_as_a_list()
    {
        Assert.Equal("records", SearchLogic.SelectedNames([]));
        Assert.Equal("contacts", SearchLogic.SelectedNames([RecordType.Contact]));
        Assert.Equal("notes or contacts", SearchLogic.SelectedNames([RecordType.Contact, RecordType.Note]));
        Assert.Equal("notes, contacts or items", SearchLogic.SelectedNames([RecordType.Item, RecordType.Note, RecordType.Contact]));
    }

    // ---------------------------------------------------------------- Layout

    private static readonly SearchResult[] Harbour =
    [
        R("o1", RecordType.Organization, "Harbour Labs", 24),
        R("n1", RecordType.Note, "Harbour Labs kickoff notes", 6),
        R("c1", RecordType.Contact, "Harbour, Ellen", 9),
        R("c2", RecordType.Contact, "Harbourne, Lewis", 2),
        R("n2", RecordType.Note, "Call with Ellen about the harbour lease", 3),
        R("o2", RecordType.Organization, "Old Harbour Coffee", 1),
        R("i1", RecordType.Item, "Harbour office lease", 4),
    ];

    private static List<SearchResult> Ranked(string query = "harbour") => SearchLogic.Rank(Harbour, query);

    [Fact]
    public void Best_match_comes_first_and_is_not_repeated_in_its_group()
    {
        var view = SearchLogic.Layout(Ranked(), None, SearchLogic.ChipTypes());

        Assert.Equal("o1", view.BestMatch?.RecordId);
        Assert.DoesNotContain(view.Groups.SelectMany(g => g.Results), r => r.RecordId == "o1");
        Assert.Equal(new[] { RecordType.Note, RecordType.Contact, RecordType.Organization, RecordType.Item }, view.Groups.Select(g => g.Type));
        Assert.Equal(new[] { "o2" }, view.Groups.Single(g => g.Type == RecordType.Organization).Results.Select(r => r.RecordId));
        Assert.Equal(new[] { "c1", "c2" }, view.Groups.Single(g => g.Type == RecordType.Contact).Results.Select(r => r.RecordId));
    }

    [Fact]
    public void Chip_counts_include_the_best_match_and_hidden_types_are_counted()
    {
        var view = SearchLogic.Layout(Ranked(), Chips(RecordType.Contact), SearchLogic.ChipTypes());

        Assert.Equal(2, view.Counts[RecordType.Note]);
        Assert.Equal(2, view.Counts[RecordType.Contact]);
        Assert.Equal(2, view.Counts[RecordType.Organization]);
        Assert.Equal(0, view.Counts[RecordType.Category]);
        Assert.Equal(1, view.Counts[RecordType.Item]);
        Assert.Equal(7, view.Total);
        Assert.Equal("c1", view.BestMatch?.RecordId);
        var contacts = Assert.Single(view.Groups);
        Assert.Equal(new[] { "c2" }, contacts.Results.Select(r => r.RecordId));
        Assert.Equal(new Dictionary<RecordType, int> { [RecordType.Note] = 2, [RecordType.Organization] = 2, [RecordType.Item] = 1 }, view.Hidden);
        Assert.Equal(5, view.HiddenTotal);
    }

    [Fact]
    public void A_chip_without_matches_leaves_the_view_empty_with_hidden_matches()
    {
        var view = SearchLogic.Layout(Ranked(), Chips(RecordType.Category), SearchLogic.ChipTypes());

        Assert.True(view.IsEmpty);
        Assert.Null(view.BestMatch);
        Assert.Equal(7, view.HiddenTotal);
    }

    [Fact]
    public void In_the_picker_the_best_match_skips_linked_records()
    {
        var linked = new HashSet<string> { "o1", "c1" };

        var view = SearchLogic.Layout(Ranked(), None, SearchLogic.ChipTypes(), r => !linked.Contains(r.RecordId));

        Assert.Equal("n1", view.BestMatch?.RecordId);
        Assert.Contains(view.Groups.Single(g => g.Type == RecordType.Organization).Results, r => r.RecordId == "o1");
    }

    [Fact]
    public void No_best_match_when_every_visible_result_is_linked()
    {
        var view = SearchLogic.Layout(Ranked(), Chips(RecordType.Item), SearchLogic.ChipTypes(), r => r.RecordId != "i1");

        Assert.Null(view.BestMatch);
        Assert.Equal(new[] { "i1" }, Assert.Single(view.Groups).Results.Select(r => r.RecordId));
    }

    [Fact]
    public void The_picker_counts_only_its_chip_types()
    {
        var chipTypes = SearchLogic.ChipTypes(RecordType.Note);

        var view = SearchLogic.Layout(Ranked().Where(r => r.RecordType != RecordType.Note).ToList(), None, chipTypes);

        Assert.False(view.Counts.ContainsKey(RecordType.Note));
        Assert.Equal(5, view.Total);
    }

    // ---------------------------------------------------------------- Highlight

    [Fact]
    public void Every_occurrence_is_highlighted_ignoring_case()
    {
        Assert.Equal(new[] { new TextRange(0, 7), new TextRange(17, 7) }, SearchLogic.HighlightRanges("Harbour lease at harbour", "HARBOUR"));
        Assert.Equal(new[] { new TextRange(15, 7) }, SearchLogic.HighlightRanges("Ideas for Northharbour pitch", " harbour "));
        Assert.Empty(SearchLogic.HighlightRanges("Harbor", "harbour"));
        Assert.Empty(SearchLogic.HighlightRanges("Harbour", " "));
    }

    [Fact]
    public void A_query_with_spaces_matches_runs_of_spaces()
    {
        Assert.Equal(new[] { new TextRange(0, 9) }, SearchLogic.HighlightRanges("Acme  Ltd", "acme ltd"));
    }

    [Fact]
    public void A_contact_found_as_first_last_highlights_each_word()
    {
        Assert.Equal(new[] { new TextRange(0, 6), new TextRange(8, 4) }, SearchLogic.HighlightRanges("Harris, Owen", "owen harris"));
        // Overlapping words are merged into one part.
        Assert.Equal(new[] { new TextRange(0, 6) }, SearchLogic.HighlightRanges("Harris, Owen", "harr rris"));
    }

    // ---------------------------------------------------------------- Context line and times

    [Fact]
    public void Links_are_worded()
    {
        Assert.Equal("No links", SearchLogic.LinksText(0));
        Assert.Equal("1 link", SearchLogic.LinksText(1));
        Assert.Equal("24 links", SearchLogic.LinksText(24));
    }

    [Fact]
    public void Context_lines_per_type()
    {
        Assert.Equal("2026-10-28 · 6 links", SearchLogic.ContextLine(new SearchResult("n", 1, "N", 6, NoteDate: 1_793_145_600_000)));
        Assert.Equal("No links", SearchLogic.ContextLine(new SearchResult("n", 1, "N")));
        Assert.Equal("Head of operations, Harbour Labs · 9 links",
            SearchLogic.ContextLine(new SearchResult("c", 2, "C", 9, JobTitle: "Head of operations", Company: " Harbour Labs ")));
        Assert.Equal("Site lead · 1 link", SearchLogic.ContextLine(new SearchResult("c", 2, "C", 1, JobTitle: "Site lead", Company: " ")));
        Assert.Equal("Northwind · No links", SearchLogic.ContextLine(new SearchResult("c", 2, "C", Company: "Northwind")));
        Assert.Equal("No links", SearchLogic.ContextLine(new SearchResult("c", 2, "C")));
        Assert.Equal("31 links", SearchLogic.ContextLine(new SearchResult("o", 3, "O", 31)));
        Assert.Equal("1 link", SearchLogic.ContextLine(new SearchResult("k", 4, "K", 1)));
        Assert.Equal("No links", SearchLogic.ContextLine(new SearchResult("i", 5, "I")));
    }

    [Fact]
    public void Relative_times_step_from_just_now_to_the_date()
    {
        var zone = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        string Ago(TimeSpan span) => SearchLogic.RelativeTime(now - (long)span.TotalMilliseconds, now, zone);

        Assert.Equal("just now", Ago(TimeSpan.FromSeconds(30)));
        Assert.Equal("1 minute ago", Ago(TimeSpan.FromMinutes(1)));
        Assert.Equal("12 minutes ago", Ago(TimeSpan.FromMinutes(12)));
        Assert.Equal("1 hour ago", Ago(TimeSpan.FromMinutes(61)));
        Assert.Equal("3 hours ago", Ago(TimeSpan.FromHours(3)));
        Assert.Equal("yesterday", Ago(TimeSpan.FromHours(16)));
        Assert.Equal("yesterday", Ago(TimeSpan.FromHours(38)));
        Assert.Equal("2 days ago", Ago(TimeSpan.FromHours(40)));
        Assert.Equal("6 days ago", Ago(TimeSpan.FromDays(6)));
        Assert.Equal("last week", Ago(TimeSpan.FromDays(7)));
        Assert.Equal("last week", Ago(TimeSpan.FromDays(13)));
        Assert.Equal("2026-10-22", Ago(TimeSpan.FromDays(14)));
        Assert.Equal("Changed just now", SearchLogic.ChangedText(now, now, zone));
    }

    [Fact]
    public void Relative_days_follow_the_time_zone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Plus10", TimeSpan.FromHours(10), "Plus10", "Plus10");
        // 23:30 UTC on 4 Nov is 09:30 on 5 Nov in the zone; 12:30 UTC on 4 Nov is 22:30 on 4 Nov there.
        var now = new DateTimeOffset(2026, 11, 4, 23, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var earlier = new DateTimeOffset(2026, 11, 4, 12, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        Assert.Equal("yesterday", SearchLogic.RelativeTime(earlier, now, zone));
        Assert.Equal("11 hours ago", SearchLogic.RelativeTime(earlier, now, TimeZoneInfo.Utc));
    }

    // ---------------------------------------------------------------- Create from search

    [Fact]
    public void The_exact_match_check_uses_all_results_not_the_visible_ones()
    {
        var results = new[] { R("o", RecordType.Organization, "Norvik"), R("n", RecordType.Note, "Norvik renewal") };
        var view = SearchLogic.Layout(SearchLogic.Rank(results, "norvik"), Chips(RecordType.Note), SearchLogic.ChipTypes());

        Assert.DoesNotContain(view.Groups.SelectMany(g => g.Results), r => r.RecordId == "o");
        Assert.Equal(CreateFromSearchOffer.None, CreateFromSearch.OfferFor("norvik", results));
    }

    [Fact]
    public void From_a_section_its_type_is_offered_first()
    {
        Assert.Equal(new[] { RecordType.Contact, RecordType.Note, RecordType.Organization, RecordType.Category, RecordType.Item },
            SearchLogic.CreateTypesWithFirst(CreateFromSearch.TypesFor(null), RecordType.Contact));
        Assert.Equal(CreateFromSearch.TypesFor(null), SearchLogic.CreateTypesWithFirst(CreateFromSearch.TypesFor(null), null));
        Assert.Equal(CreateFromSearch.TypesFor(RecordType.Note),
            SearchLogic.CreateTypesWithFirst(CreateFromSearch.TypesFor(RecordType.Note), RecordType.Note));
    }

    [Fact]
    public void Search_starts_from_three_characters()
    {
        Assert.False(SearchLogic.IsSearchQuery(" ab "));
        Assert.True(SearchLogic.IsSearchQuery(" abc "));
        Assert.False(SearchLogic.IsSearchQuery(null));
    }
}

/// <summary>The link picker's ticks: kept through any search, across types, until unticked or linked.</summary>
public class LinkSelectionTests
{
    private static SearchResult R(string id, RecordType type) => new(id, (int)type, id);

    [Fact]
    public void Ticks_keep_the_order_picked_across_types()
    {
        var selection = new LinkSelection();

        Assert.True(selection.Set(R("c", RecordType.Contact), true));
        Assert.True(selection.Set(R("i", RecordType.Item), true));
        Assert.True(selection.Set(R("o", RecordType.Organization), true));
        Assert.False(selection.Set(R("c", RecordType.Contact), true));

        Assert.Equal(new[] { "c", "i", "o" }, selection.Picked.Select(r => r.RecordId));
        Assert.Equal(3, selection.Count);
    }

    [Fact]
    public void Ticks_do_not_depend_on_what_a_search_shows()
    {
        // The selection holds records, not rows: nothing a search shows or hides changes it.
        var selection = new LinkSelection();
        selection.Set(R("c", RecordType.Contact), true);
        selection.Set(R("i", RecordType.Item), true);
        var index = new SearchIndex([R("c", RecordType.Contact), R("i", RecordType.Item)]);

        Assert.Empty(index.Find("zzz"));
        Assert.Empty(index.Find(""));

        Assert.True(selection.Contains("c"));
        Assert.True(selection.Contains("i"));
        Assert.Equal(new[] { "c", "i" }, selection.ToLink().Select(r => r.RecordId));
    }

    [Fact]
    public void Unticking_removes_a_record()
    {
        var selection = new LinkSelection();
        selection.Set(R("c", RecordType.Contact), true);
        selection.Set(R("i", RecordType.Item), true);

        Assert.True(selection.Set(R("c", RecordType.Contact), false));
        Assert.False(selection.Set(R("c", RecordType.Contact), false));

        Assert.Equal(new[] { "i" }, selection.Picked.Select(r => r.RecordId));
        Assert.False(selection.Contains("c"));
    }

    [Fact]
    public void Linked_records_cannot_be_ticked_and_become_unticked_when_linked()
    {
        var selection = new LinkSelection();
        selection.Set(R("c", RecordType.Contact), true);
        selection.Set(R("i", RecordType.Item), true);

        Assert.True(selection.SetLinked(["c", "x"]));

        Assert.False(selection.Set(R("x", RecordType.Item), true));
        Assert.Equal(new[] { "i" }, selection.Picked.Select(r => r.RecordId));
        Assert.True(selection.IsLinked("x"));
    }

    [Fact]
    public void Refresh_drops_deleted_records_and_takes_new_titles()
    {
        var selection = new LinkSelection();
        selection.Set(R("c", RecordType.Contact), true);
        selection.Set(R("i", RecordType.Item), true);

        Assert.True(selection.Refresh([new SearchResult("i", (int)RecordType.Item, "Renamed", 3)]));

        var picked = Assert.Single(selection.Picked);
        Assert.Equal("Renamed", picked.RecordTitle);
        Assert.False(selection.Contains("c"));
    }

    [Fact]
    public void Clear_unticks_everything()
    {
        var selection = new LinkSelection();
        selection.Set(R("c", RecordType.Contact), true);

        selection.Clear();

        Assert.True(selection.IsEmpty);
        Assert.False(selection.Contains("c"));
    }
}
