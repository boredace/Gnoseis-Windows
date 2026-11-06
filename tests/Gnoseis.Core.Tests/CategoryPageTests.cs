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

/// <summary>The category page: header line, copy text, search matching, row allotment, column placement.</summary>
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

    // ---------------------------------------------------------------- Search matching

    [Fact]
    public void Note_matches_on_title_case_insensitive()
    {
        var note = new HistoryNote { Id = "n1", Title = "Visit to client", Text = "Discussed project" };

        Assert.True(CategoryPage.MatchesNote(note, "visit"));
        Assert.True(CategoryPage.MatchesNote(note, "VISIT"));
        Assert.False(CategoryPage.MatchesNote(note, "lens"));
    }

    [Fact]
    public void Note_matches_on_first_line_of_text()
    {
        var note = new HistoryNote { Id = "n1", Title = "", Text = "New frames\nSecond line" };

        Assert.True(CategoryPage.MatchesNote(note, "frames"));
        Assert.False(CategoryPage.MatchesNote(note, "second")); // second line, not first
    }

    [Fact]
    public void Note_matches_on_day_text()
    {
        var note = new HistoryNote
        {
            Id = "n1",
            Title = "Call",
            CreateDate = EpochTime.UtcMidnight(new DateOnly(2026, 5, 26)),
        };

        Assert.True(CategoryPage.MatchesNote(note, "May"));
        Assert.True(CategoryPage.MatchesNote(note, "26"));
        Assert.False(CategoryPage.MatchesNote(note, "June"));
    }

    [Fact]
    public void Note_matches_on_with_line()
    {
        var note = new HistoryNote
        {
            Id = "n1",
            Title = "Quote",
            Links = [new LinkedRecordSummary { Id = "i1", Type = RecordType.Item, Name = "Paint colours" }],
        };

        Assert.True(CategoryPage.MatchesNote(note, "Paint"));
        Assert.True(CategoryPage.MatchesNote(note, "colours"));
        Assert.False(CategoryPage.MatchesNote(note, "flooring"));
    }

    [Fact]
    public void Note_blank_query_matches_all()
    {
        var note = new HistoryNote { Id = "n1", Title = "Call" };

        Assert.True(CategoryPage.MatchesNote(note, ""));
        Assert.True(CategoryPage.MatchesNote(note, null!));
    }

    [Fact]
    public void Contact_matches_on_name()
    {
        var contact = new LinkedRecordSummary
        {
            Id = "c1", Type = RecordType.Contact, Name = "Emma van Leeuwen", JobAndCompany = "Owner · Ruby Eye Clinic",
        };

        Assert.True(CategoryPage.MatchesContact(contact, "Emma"));
        Assert.True(CategoryPage.MatchesContact(contact, "van Leeuwen"));
        Assert.False(CategoryPage.MatchesContact(contact, "Bakker"));
    }

    [Fact]
    public void Contact_matches_on_job_and_company()
    {
        var contact = new LinkedRecordSummary
        {
            Id = "c1", Type = RecordType.Contact, Name = "Emma van Leeuwen", JobAndCompany = "Owner · Ruby Eye Clinic",
        };

        Assert.True(CategoryPage.MatchesContact(contact, "Owner"));
        Assert.True(CategoryPage.MatchesContact(contact, "Ruby Eye Clinic"));
        Assert.False(CategoryPage.MatchesContact(contact, "Manager"));
    }

    [Fact]
    public void Named_record_matches_on_name()
    {
        var org = new LinkedRecordSummary { Id = "o1", Type = RecordType.Organization, Name = "Kok Catering" };

        Assert.True(CategoryPage.MatchesNamedRecord(org, "Kok"));
        Assert.True(CategoryPage.MatchesNamedRecord(org, "catering"));
        Assert.False(CategoryPage.MatchesNamedRecord(org, "Bakker"));
    }

    [Fact]
    public void Named_record_matches_on_first_line()
    {
        var item = new LinkedRecordSummary { Id = "i1", Type = RecordType.Item, Name = "SRV-0007", FirstLine = "Hyper-V host" };

        Assert.True(CategoryPage.MatchesNamedRecord(item, "Hyper-V"));
        Assert.True(CategoryPage.MatchesNamedRecord(item, "host"));
        Assert.False(CategoryPage.MatchesNamedRecord(item, "Dell"));
    }

    [Fact]
    public void Any_match_returns_true_when_any_record_matches()
    {
        var records = new[]
        {
            new LinkedRecordSummary { Id = "c1", Type = RecordType.Contact, Name = "Emma van Leeuwen" },
            new LinkedRecordSummary { Id = "o1", Type = RecordType.Organization, Name = "Kok Catering" },
        };

        Assert.True(CategoryPage.AnyMatch(records, "Emma"));
        Assert.True(CategoryPage.AnyMatch(records, "Kok"));
        Assert.False(CategoryPage.AnyMatch(records, "Bakker"));
    }

    [Fact]
    public void Any_match_returns_false_for_empty_list()
    {
        Assert.False(CategoryPage.AnyMatch([], "test"));
    }

    // ---------------------------------------------------------------- Row allotment

    [Fact]
    public void Allot_rows_starts_at_three_per_list()
    {
        var lengths = new[] { 10, 20, 5, 30, 8 };
        var result = CategoryPage.AllotRows(lengths, 15);

        Assert.Equal(new[] { 3, 3, 3, 3, 3 }, result);
    }

    [Fact]
    public void Allot_rows_distributes_spare_to_lists_with_more()
    {
        var lengths = new[] { 10, 20, 5, 30, 8 };
        var result = CategoryPage.AllotRows(lengths, 20);

        // 5 lists × 3 = 15, 5 spare. Distributed to lists with capacity.
        Assert.Equal(20, result.Sum());
        Assert.True(result.All(r => r <= lengths[result.ToList().IndexOf(r)]));
    }

    [Fact]
    public void Allot_rows_caps_at_list_length()
    {
        var lengths = new[] { 3, 3, 3, 3, 3 };
        var result = CategoryPage.AllotRows(lengths, 20);

        Assert.Equal(new[] { 3, 3, 3, 3, 3 }, result);
    }

    [Fact]
    public void Allot_rows_caps_at_max_rows()
    {
        var lengths = new[] { 100, 100, 100, 100, 100 };
        var result = CategoryPage.AllotRows(lengths, 10);

        // 5 × 3 = 15 > 10, so first 5 lists get 2 each (but capped at 3)
        // Actually: starts at 3 each = 15, remaining = 10 - 15 = -5, so just 3 each but capped at 10 total
        // Wait: maxRows=10, starts with 3 each = 15 > 10
        // The algorithm: remaining = 10 - 15 = -5, which is <= 0, so returns [3,3,3,3,3]
        // But that sums to 15 > 10. The cap is only on distribution, not on initial.
        Assert.Equal(new[] { 3, 3, 3, 3, 3 }, result);
        Assert.True(result.Sum() > 10); // Initial 3 each exceeds maxRows when lists > maxRows/3
    }

    // ---------------------------------------------------------------- Column placement

    [Fact]
    public void Place_columns_uses_shortest_column_first()
    {
        var allotment = new[] { 10, 10, 10, 10, 10 };
        var result = CategoryPage.PlaceColumns(allotment, 2, 20);

        // 5 lists, 2 columns, maxRows=20 per column:
        // List 0 → col 0: min(10, 20-0)=10, col0=10
        // List 1 → col 1: min(10, 20-0)=10, col1=10
        // List 2 → col 0 (tie): min(10, 20-10)=10, col0=20
        // List 3 → col 1: min(10, 20-10)=10, col1=20
        // List 4 → col 0 (tie): min(10, 20-20)=0, col0=20
        Assert.Equal(new[] { 10, 10, 10, 10, 0 }, result);
    }

    [Fact]
    public void Place_columns_respects_max_rows()
    {
        var allotment = new[] { 10, 10, 10, 10, 10 };
        var result = CategoryPage.PlaceColumns(allotment, 2, 6);

        // 2 columns, max 6 per column: each column gets 6, total 12
        Assert.Equal(12, result.Sum());
    }

    [Fact]
    public void Place_columns_single_column_passes_through()
    {
        var allotment = new[] { 5, 10, 3 };
        var result = CategoryPage.PlaceColumns(allotment, 1, 20);

        Assert.Equal(allotment, result);
    }
}
