/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>The category page: header line, copy text, rows, search, row allotment, column placement.</summary>
public class CategoryPageTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // ---------------------------------------------------------------- Header line

    [Fact]
    public void Header_line_shows_total_linked_records()
    {
        Assert.Equal(("Category · 73 linked records", false), CategoryPage.HeaderLine(73));
    }

    [Fact]
    public void Header_line_uses_singular_for_one()
    {
        Assert.Equal(("Category · 1 linked record", false), CategoryPage.HeaderLine(1));
    }

    [Fact]
    public void Header_line_placeholder_when_no_linked_records()
    {
        Assert.Equal(("Category · no notes or links yet", true), CategoryPage.HeaderLine(0));
    }

    // ---------------------------------------------------------------- Copy text

    [Fact]
    public void Copy_text_has_name_and_comments()
    {
        var cat = new Category { CategoryName = "Project tags", Comments = "For client projects only" };

        Assert.Equal(string.Join(Environment.NewLine, "Project tags", "", "For client projects only"), CategoryPage.CopyText(cat));
    }

    [Fact]
    public void Copy_text_leaves_out_blank_comments()
    {
        var cat = new Category { CategoryName = "Project tags" };

        Assert.Equal("Project tags", CategoryPage.CopyText(cat));
    }

    // ---------------------------------------------------------------- Rows

    private static readonly DateOnly Today = new(2026, 11, 6);

    private static LinkedRecordSummary Linked(string id, RecordType type, string name, string firstLine = "", string job = "", string sortName = "") =>
        new() { Id = id, Type = type, Name = name, FirstLine = firstLine, JobAndCompany = job, SortName = sortName.Length > 0 ? sortName : name };

    private static HistoryNote Note(string id, DateOnly day, string title, string text, params LinkedRecordSummary[] links) => new()
    {
        Id = id,
        Title = title,
        Text = text,
        CreateDate = EpochTime.UtcMidnight(day),
        CreateDateTime = EpochTime.UtcMidnight(day),
        Links = links,
    };

    private static IReadOnlyDictionary<RecordType, IReadOnlyList<CategoryRow>> SampleRows()
    {
        var category = Linked("cat", RecordType.Category, "Kitchen");
        var notes = new[]
        {
            Note("n1", new DateOnly(2026, 10, 28), "Oven repair", "Called Kok about the oven", category, Linked("o1", RecordType.Organization, "Kok Catering")),
            Note("n2", new DateOnly(2025, 3, 2), "", "Fridge check\nAll fine", category),
        };
        var linked = new LinkedRecordSet(
        [
            Linked("c1", RecordType.Contact, "Emma van Leeuwen", job: "Owner · Kok Catering", sortName: "van Leeuwenemma"),
            Linked("c2", RecordType.Contact, "Daan Bakker", job: "Chef", sortName: "Bakkerdaan"),
            Linked("o1", RecordType.Organization, "Kok Catering", "Supplier"),
            Linked("i1", RecordType.Item, "Oven", "Bought 2020"),
            Linked("n1", RecordType.Note, "Oven repair"),
        ]);
        return CategoryPage.Rows("cat", notes, linked, Today, Invariant);
    }

    [Fact]
    public void Rows_show_notes_newest_first_with_day_and_with_line()
    {
        var notes = SampleRows()[RecordType.Note];

        Assert.Equal(["n1", "n2"], notes.Select(r => r.Id));
        Assert.Equal(("28 Oct", "Oven repair", "Kok Catering"), (notes[0].Day, notes[0].Name, notes[0].Second));
        Assert.Equal(("2 Mar 2025", "Fridge check", ""), (notes[1].Day, notes[1].Name, notes[1].Second));
    }

    [Fact]
    public void Rows_show_the_other_types_by_name_with_their_second_line()
    {
        var rows = SampleRows();

        Assert.Equal(["c2", "c1"], rows[RecordType.Contact].Select(r => r.Id));
        Assert.Equal("Owner · Kok Catering", rows[RecordType.Contact][1].Second);
        Assert.Equal(("Kok Catering", "Supplier"), (rows[RecordType.Organization][0].Name, rows[RecordType.Organization][0].Second));
        Assert.Empty(rows[RecordType.Category]);
        Assert.Equal(CategoryPage.ListTypes.ToHashSet(), rows.Keys.ToHashSet());
    }

    [Fact]
    public void List_types_are_notes_contacts_organizations_items_categories()
    {
        Assert.Equal([RecordType.Note, RecordType.Contact, RecordType.Organization, RecordType.Item, RecordType.Category], CategoryPage.ListTypes);
    }

    // ---------------------------------------------------------------- Search

    [Fact]
    public void Search_matches_what_each_row_shows_ignoring_case()
    {
        var rows = SampleRows();

        Assert.True(rows[RecordType.Note][0].Matches("KOK"));           // the with line
        Assert.True(rows[RecordType.Note][0].Matches("called"));        // a titled note's first line
        Assert.True(rows[RecordType.Note][1].Matches("fridge"));        // an untitled note's first line is its title
        Assert.False(rows[RecordType.Note][1].Matches("all fine"));     // not the whole note text
        Assert.True(rows[RecordType.Contact][1].Matches("kok"));        // job title · company
        Assert.True(rows[RecordType.Organization][0].Matches("suppl")); // first comment line
        Assert.False(rows[RecordType.Item][0].Matches("kok"));
        Assert.True(rows[RecordType.Item][0].Matches("  "));
    }

    [Fact]
    public void Search_counts_per_list_and_in_total()
    {
        var rows = SampleRows();
        var matches = CategoryPage.ListTypes.Select(t => rows[t].Count(r => r.Matches("kok"))).ToList();

        Assert.Equal([1, 1, 1, 0, 0], matches);
        Assert.Equal("3 matches in 3 of 5 lists · Esc clears", CategoryPage.SearchStatus(matches, searching: true));
        Assert.Equal("1 match in 1 of 5 lists · Esc clears", CategoryPage.SearchStatus([1, 0, 0, 0, 0], searching: true));
        Assert.Equal("No matches · Esc clears", CategoryPage.SearchStatus([0, 0, 0, 0, 0], searching: true));
        Assert.Equal("Searches all five lists at once", CategoryPage.SearchStatus(matches, searching: false));
    }

    [Fact]
    public void List_texts()
    {
        Assert.Equal("Search the 73 records in this category", CategoryPage.SearchPlaceholder(73));
        Assert.Equal("Search the 1 record in this category", CategoryPage.SearchPlaceholder(1));
        Assert.Equal("none", CategoryPage.CountText(0, 0, searching: false));
        Assert.Equal("41", CategoryPage.CountText(41, 41, searching: false));
        Assert.Equal("3 of 41", CategoryPage.CountText(41, 3, searching: true));
        Assert.Equal("Newest first", CategoryPage.SortText(RecordType.Note, 2));
        Assert.Equal("A–Z", CategoryPage.SortText(RecordType.Contact, 5));
        Assert.Equal("", CategoryPage.SortText(RecordType.Contact, 1));
        Assert.Equal("No contacts in this category yet", CategoryPage.EmptyText(RecordType.Contact));
        Assert.Equal("Add an organization", CategoryPage.AddText(RecordType.Organization));
        Assert.Equal("Add a note (Ctrl+Shift+N)", CategoryPage.AddText(RecordType.Note));
    }

    [Fact]
    public void Match_ranges_mark_every_occurrence_ignoring_case()
    {
        Assert.Equal([new TextRange(0, 3), new TextRange(8, 3)], CategoryPage.MatchRanges("Kok and kok", " KOK "));
        Assert.Empty(CategoryPage.MatchRanges("Kok", ""));
        Assert.Empty(CategoryPage.MatchRanges("Kok", "x"));
    }

    // ---------------------------------------------------------------- Rows shown and columns

    [Fact]
    public void Allot_rows_starts_at_three_and_never_over_a_list_length()
    {
        Assert.Equal([3, 1, 0], CategoryPage.AllotRows([10, 1, 0], 4));
    }

    [Fact]
    public void Allot_rows_gives_spare_rows_one_at_a_time_to_lists_with_more()
    {
        Assert.Equal([6, 5], CategoryPage.AllotRows([20, 5], 11));
        Assert.Equal([5, 4], CategoryPage.AllotRows([20, 20], 9));
        Assert.Equal([20, 5], CategoryPage.AllotRows([20, 5], 100));
    }

    [Fact]
    public void Column_count_by_band()
    {
        Assert.Equal([1, 2, 3, 5, 5], new[] { RecordPageBand.S, RecordPageBand.M, RecordPageBand.L, RecordPageBand.XL, RecordPageBand.XXL }.Select(CategoryPage.ColumnCount));
    }

    [Fact]
    public void Place_columns_puts_each_list_under_the_shortest_column_counting_up_to_20_rows()
    {
        // Notes 41 (counts 20), contacts 7, organizations 5, items 2, categories 0 (counts 1), in three columns.
        Assert.Equal([0, 1, 2, 2, 1], CategoryPage.PlaceColumns([41, 7, 5, 2, 0], 3));
        Assert.Equal([0, 1, 0, 1, 0], CategoryPage.PlaceColumns([3, 3, 3, 3, 3], 2));
        Assert.Equal([0, 1, 2, 3, 4], CategoryPage.PlaceColumns([41, 7, 5, 2, 0], 5));
        Assert.Equal([0, 0, 0, 0, 0], CategoryPage.PlaceColumns([41, 7, 5, 2, 0], 1));
    }
}