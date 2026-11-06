/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;
using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>The organization page: header line, copy text, people order, person filter, not-linked contacts.</summary>
public class OrganizationPageTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public OrganizationPageTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    // ---------------------------------------------------------------- Header line

    [Fact]
    public void Header_line_shows_people_notes_and_other_linked()
    {
        Assert.Equal(("Organization · 7 people · 64 notes · 21 other linked", false), OrganizationPage.HeaderLine(7, 64, 21));
    }

    [Fact]
    public void Header_line_uses_singular_for_one()
    {
        Assert.Equal(("Organization · 1 person · 1 note · 1 other linked", false), OrganizationPage.HeaderLine(1, 1, 1));
    }

    [Fact]
    public void Header_line_omits_parts_at_zero()
    {
        Assert.Equal(("Organization · 7 people · 64 notes", false), OrganizationPage.HeaderLine(7, 64, 0));
        Assert.Equal(("Organization · 7 people · 21 other linked", false), OrganizationPage.HeaderLine(7, 0, 21));
        Assert.Equal(("Organization · 64 notes · 21 other linked", false), OrganizationPage.HeaderLine(0, 64, 21));
        Assert.Equal(("Organization · 7 people", false), OrganizationPage.HeaderLine(7, 0, 0));
    }

    [Fact]
    public void Header_line_placeholder_when_no_notes_or_other_linked()
    {
        Assert.Equal(("Organization · no notes or links yet", true), OrganizationPage.HeaderLine(0, 0, 0));
    }

    // ---------------------------------------------------------------- Copy text

    [Fact]
    public void Copy_text_has_name_and_comments()
    {
        var org = new Organization { OrganizationName = "Kok Catering B.V.", Comments = "Main vendor for events" };

        Assert.Equal(
            string.Join(Environment.NewLine, "Kok Catering B.V.", "", "Main vendor for events"),
            OrganizationPage.CopyText(org));
    }

    [Fact]
    public void Copy_text_leaves_out_blank_comments()
    {
        var org = new Organization { OrganizationName = "Kok Catering B.V." };

        Assert.Equal("Kok Catering B.V.", OrganizationPage.CopyText(org));
    }

    [Fact]
    public void Copy_text_trims_name_and_comments()
    {
        var org = new Organization { OrganizationName = "  Kok Catering  ", Comments = "  Main vendor  " };

        Assert.Equal(string.Join(Environment.NewLine, "Kok Catering", "", "Main vendor"), OrganizationPage.CopyText(org));
    }

    // ---------------------------------------------------------------- Side types

    [Fact]
    public void Side_types_are_categories_items_and_organizations()
    {
        Assert.Equal(
            [RecordType.Category, RecordType.Item, RecordType.Organization],
            OrganizationPage.SideTypes);
    }

    // ---------------------------------------------------------------- People

    private static HistoryNote NoteWith(string id, DateOnly day, params string[] linkedIds) => new()
    {
        Id = id,
        Title = id,
        CreateDate = EpochTime.UtcMidnight(day),
        CreateDateTime = EpochTime.UtcMidnight(day),
        Links = linkedIds.Select(l => new LinkedRecordSummary { Id = l, Type = RecordType.Contact, Name = l }).ToList(),
    };

    private static PersonSummary Person(string id, string sortKey, int count, DateOnly? latest) =>
        new(id, id, sortKey, "", null, null, count, latest);

    [Fact]
    public void People_count_shared_notes_and_the_latest_note_day()
    {
        var emma = new Contact { Id = "c1", NameFirst = "Emma", NameLast = "van Leeuwen", JobTitle = " Owner " };
        var daan = new Contact { Id = "c2", NameFirst = "Daan", NameLast = "Bakker" };
        var history = new[]
        {
            NoteWith("n1", new DateOnly(2026, 10, 15), "c1"),
            NoteWith("n2", new DateOnly(2026, 9, 1), "c1", "c2"),
            NoteWith("n3", new DateOnly(2026, 11, 2)),
        };

        var people = OrganizationPage.People([emma, daan], history);

        Assert.Equal(["c1", "c2"], people.Select(p => p.Id));
        Assert.Equal((2, new DateOnly(2026, 10, 15)), (people[0].SharedNoteCount, people[0].LatestSharedNoteDay));
        Assert.Equal((1, new DateOnly(2026, 9, 1)), (people[1].SharedNoteCount, people[1].LatestSharedNoteDay));
        Assert.Equal("Emma van Leeuwen", people[0].Name);
        Assert.Equal("Owner", people[0].JobTitle);
    }

    [Fact]
    public void People_phone_is_the_main_number_else_the_mobile_and_email_the_first_address()
    {
        var withMain = new Contact { Id = "c1", NameLast = "A", PhoneMain = "030 1", PhoneMobile = "06 1", EmailWork = "a@example.org" };
        var mobileOnly = new Contact { Id = "c2", NameLast = "B", PhoneMobile = "06 2" };
        var none = new Contact { Id = "c3", NameLast = "C" };

        var people = OrganizationPage.People([withMain, mobileOnly, none], []).ToDictionary(p => p.Id);

        Assert.Equal("030 1", people["c1"].Phone?.Value);
        Assert.Equal("a@example.org", people["c1"].Email?.Value);
        Assert.Equal(ContactChipKind.Mobile, people["c2"].Phone?.Kind);
        Assert.Null(people["c2"].Email);
        Assert.True(people["c3"].HasNoWayToReach);
        Assert.False(people["c1"].HasNoWayToReach);
    }

    [Fact]
    public void People_order_latest_shared_note_first_then_by_name()
    {
        var people = new[]
        {
            Person("c3", "Berg\u0001Anna", 0, null),
            Person("c1", "van Leeuwen\u0001Emma", 5, new DateOnly(2026, 10, 15)),
            Person("c2", "Bakker\u0001Daan", 3, new DateOnly(2026, 11, 1)),
            Person("c4", "Aalders\u0001Bo", 0, null),
        };

        Assert.Equal(["c2", "c1", "c4", "c3"], OrganizationPage.PeopleOrder(people).Select(p => p.Id));
    }

    [Fact]
    public void Notes_line_counts_and_names_the_latest_day()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        Assert.Equal("15 notes · last 15 Oct 2026", OrganizationPage.NotesLine(Person("c1", "", 15, new DateOnly(2026, 10, 15)), culture));
        Assert.Equal("1 note · last 3 Jan 2025", OrganizationPage.NotesLine(Person("c1", "", 1, new DateOnly(2025, 1, 3)), culture));
        Assert.Equal("no notes yet", OrganizationPage.NotesLine(Person("c1", "", 0, null), culture));
    }

    [Fact]
    public void Shown_people_by_band()
    {
        Assert.Equal(6, OrganizationPage.ShownPeopleCount(RecordPageBand.L));
        Assert.Equal(8, OrganizationPage.ShownPeopleCount(RecordPageBand.XL));
        Assert.Equal(10, OrganizationPage.ShownPeopleCount(RecordPageBand.XXL));
        Assert.Equal(5, OrganizationPage.ShownPeopleCount(RecordPageBand.M));
    }

    // ---------------------------------------------------------------- Person filter

    [Fact]
    public void Person_filter_keeps_the_notes_linked_to_the_person()
    {
        var notes = new[]
        {
            NoteWith("n1", new DateOnly(2026, 1, 1), "c1"),
            NoteWith("n2", new DateOnly(2026, 1, 2), "c2"),
            NoteWith("n3", new DateOnly(2026, 1, 3), "c1", "c2"),
        };

        Assert.Equal(["n1", "n3"], OrganizationPage.PeopleFilter(notes, "c1").Select(n => n.Id));
        Assert.Empty(OrganizationPage.PeopleFilter(notes, "c9"));
    }

    [Fact]
    public void History_count_while_a_person_is_picked()
    {
        Assert.Equal("15 of 64 notes", OrganizationPage.HistoryCount(15, 64, personPicked: true));
        Assert.Equal("64 notes", OrganizationPage.HistoryCount(64, 64, personPicked: false));
        Assert.Equal("1 note", OrganizationPage.HistoryCount(1, 1, personPicked: false));
        Assert.Equal("nothing yet", OrganizationPage.HistoryCount(0, 0, personPicked: false));
    }

    // ---------------------------------------------------------------- Not-linked contacts

    [Fact]
    public async Task Not_linked_contacts_match_company_trimmed_case_insensitive_runs_of_spaces()
    {
        var org = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "  Ruby   Eye Clinic  " });
        var c1 = await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Emma", NameLast = "van Leeuwen", Company = "ruby   EYE clinic ", JobTitle = "Owner" });
        var c2 = await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Daan", NameLast = "Bakker", Company = "Ruby Eye Clinic" });
        await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Iris", NameLast = "Smit", Company = "Ruby Eye Clinic Network" });
        await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Bas", NameLast = "Vos", Company = "Ruby Clinic" });

        var candidates = await _data.Contacts.GetContactsByCompanyAsync(org.OrganizationName);
        var (shown, total) = OrganizationPage.NotLinkedContacts(org, [c2.Id], candidates);

        Assert.Equal(1, total);
        Assert.Equal(c1.Id, Assert.Single(shown).Id);
        Assert.Equal("Owner", shown[0].JobTitle);
        Assert.Equal("Emma van Leeuwen", shown[0].Name);
    }

    [Fact]
    public async Task Company_query_finds_every_candidate_and_escapes_like_characters()
    {
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "A", Company = "100% Fresh" });
        await _data.Contacts.AddContactAsync(new Contact { NameLast = "B", Company = "100 Fresh" });

        var candidates = await _data.Contacts.GetContactsByCompanyAsync("100%   fresh");

        Assert.Equal(["A"], candidates.Select(c => c.NameLast));
        Assert.Empty(await _data.Contacts.GetContactsByCompanyAsync("   "));
    }

    [Fact]
    public void Not_linked_contacts_at_most_three_by_name_plus_count()
    {
        var org = new Organization { OrganizationName = "Test Corp" };
        var contacts = Enumerable.Range(0, 7).Select(i => new Contact { Id = $"c{i}", NameFirst = "F", NameLast = $"Last{6 - i}", Company = "Test Corp" }).ToList();

        var (shown, total) = OrganizationPage.NotLinkedContacts(org, [], contacts);

        Assert.Equal(7, total);
        Assert.Equal(["c6", "c5", "c4"], shown.Select(c => c.Id));
    }

    [Fact]
    public void Not_linked_contacts_empty_when_no_match_or_no_name()
    {
        var contacts = new[] { new Contact { Id = "c1", NameLast = "A", Company = "Other Corp" }, new Contact { Id = "c2", NameLast = "B", Company = "" } };

        Assert.Equal(0, OrganizationPage.NotLinkedContacts(new Organization { OrganizationName = "Test Corp" }, [], contacts).TotalCount);
        Assert.Equal(0, OrganizationPage.NotLinkedContacts(new Organization { OrganizationName = " " }, [], contacts).TotalCount);
    }
}