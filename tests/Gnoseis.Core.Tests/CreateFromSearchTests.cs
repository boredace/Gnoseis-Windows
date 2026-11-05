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

/// <summary>Creating a record from a search: exact matches, the offer, the types offered and the prefill.</summary>
public class CreateFromSearchTests
{
    private static SearchResult Result(RecordType type, string title) => new(Guid.NewGuid().ToString(), (int)type, title);

    [Theory]
    [InlineData("Acme", "Acme", true)]
    [InlineData("Acme", "acme", true)]
    [InlineData("ACME LTD", "acme ltd", true)]
    [InlineData("Acme Ltd", "  acme    ltd  ", true)]
    [InlineData("Acme  Ltd", "Acme Ltd", true)]
    [InlineData("Acme Ltd", "Acme", false)]
    [InlineData("Acme", "Acme Ltd", false)]
    [InlineData("Acme", "   ", false)]
    [InlineData(null, "Acme", false)]
    public void Title_matches_exactly_ignoring_case_and_spaces(string? title, string query, bool expected) =>
        Assert.Equal(expected, CreateFromSearch.MatchesExactly(title, query));

    [Theory]
    [InlineData("Lindqvist, Maria", "Lindqvist, Maria", true)]
    [InlineData("Lindqvist, Maria", "maria lindqvist", true)]
    [InlineData("Lindqvist, Maria", " Maria   Lindqvist ", true)]
    [InlineData("Lindqvist, Maria", "Lindqvist", false)]
    [InlineData("Lindqvist, Maria", "Lindqvist Maria", false)]
    [InlineData("Lindqvist", "lindqvist", true)]
    public void Contact_matches_as_last_first_and_as_first_last(string title, string query, bool expected) =>
        Assert.Equal(expected, CreateFromSearch.MatchesExactly(Result(RecordType.Contact, title), query));

    [Fact]
    public void Only_contacts_match_as_first_last()
    {
        Assert.False(CreateFromSearch.MatchesExactly(Result(RecordType.Organization, "Smith, John"), "John Smith"));
        Assert.Null(CreateFromSearch.FirstLastTitle(Result(RecordType.Organization, "Smith, John")));
        Assert.Equal("John Smith", CreateFromSearch.FirstLastTitle(Result(RecordType.Contact, "Smith, John")));
    }

    [Theory]
    [InlineData("Lindqvist, Maria", "maria lind", true)]
    [InlineData("Lindqvist, Maria", "qvist, ma", true)]
    [InlineData("Acme  Ltd", "acme ltd", true)]
    [InlineData("Acme Ltd", "  ltd ", true)]
    [InlineData("Acme Ltd", "Beta", false)]
    public void Search_finds_contacts_as_first_last_too(string title, string query, bool expected) =>
        Assert.Equal(expected, CreateFromSearch.ResultContains(Result(RecordType.Contact, title), query));

    [Fact]
    public void Offer_needs_three_characters()
    {
        Assert.Equal(CreateFromSearchOffer.None, CreateFromSearch.OfferFor("", []));
        Assert.Equal(CreateFromSearchOffer.None, CreateFromSearch.OfferFor("ab", []));
        Assert.Equal(CreateFromSearchOffer.None, CreateFromSearch.OfferFor("  ab  ", []));
        Assert.Equal(CreateFromSearchOffer.NoResults, CreateFromSearch.OfferFor("abc", []));
    }

    [Fact]
    public void Offer_after_results_unless_one_matches_exactly()
    {
        var results = new[] { Result(RecordType.Organization, "Acme Ltd"), Result(RecordType.Contact, "Acme, Jo") };
        Assert.Equal(CreateFromSearchOffer.AfterResults, CreateFromSearch.OfferFor("Acme", results));
        Assert.Equal(CreateFromSearchOffer.None, CreateFromSearch.OfferFor("acme ltd", results));
        Assert.Equal(CreateFromSearchOffer.None, CreateFromSearch.OfferFor("Jo Acme", results));
    }

    [Fact]
    public void Search_offers_every_type_and_the_picker_all_but_the_sources()
    {
        Assert.Equal(
            [RecordType.Note, RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item],
            CreateFromSearch.TypesFor(null));
        Assert.Equal(
            [RecordType.Note, RecordType.Organization, RecordType.Category, RecordType.Item],
            CreateFromSearch.TypesFor(RecordType.Contact));
        Assert.Equal(
            [RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item],
            CreateFromSearch.TypesFor(RecordType.Note));
    }

    [Theory]
    [InlineData("Lindqvist", null, "Lindqvist")]
    [InlineData("  Maria   Lindqvist ", "Maria", "Lindqvist")]
    [InlineData("Anna Maria Lindqvist", "Anna Maria", "Lindqvist")]
    [InlineData("Lindqvist, Maria", "Maria", "Lindqvist")]
    [InlineData("van der Berg, Anna Maria", "Anna Maria", "van der Berg")]
    [InlineData("Lindqvist,Maria", "Maria", "Lindqvist")]
    [InlineData("Lindqvist,", null, "Lindqvist")]
    [InlineData(", Maria Lindqvist", "Maria", "Lindqvist")]
    public void Contact_name_split(string query, string? first, string last) =>
        Assert.Equal(new ContactNamePrefill(first, last), CreateFromSearch.PrefillContactName(query));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ")]
    public void No_contact_prefill_for_an_empty_query(string query) =>
        Assert.Null(CreateFromSearch.PrefillContactName(query));

    [Fact]
    public void Contact_last_name_is_cut_to_its_length()
    {
        var longWord = new string('x', TitleLength.Contact + 10);
        var prefill = CreateFromSearch.PrefillContactName($"Maria {longWord}")!;
        Assert.Equal("Maria", prefill.NameFirst);
        Assert.Equal(TitleLength.Contact, prefill.NameLast.Length);
    }

    [Theory]
    [InlineData(RecordType.Note, TitleLength.Note)]
    [InlineData(RecordType.Organization, TitleLength.Organization)]
    [InlineData(RecordType.Category, TitleLength.Category)]
    [InlineData(RecordType.Item, TitleLength.Item)]
    public void Name_is_trimmed_and_cut_to_the_title_length(RecordType type, int length)
    {
        Assert.Equal("Acme Ltd", CreateFromSearch.PrefillName("  Acme Ltd  ", type));
        var cut = CreateFromSearch.PrefillName(new string('y', length + 5), type)!;
        Assert.Equal(length, cut.Length);
        Assert.Null(CreateFromSearch.PrefillName("   ", type));
    }
}
