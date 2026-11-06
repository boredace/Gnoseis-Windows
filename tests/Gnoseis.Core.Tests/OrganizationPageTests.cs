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

    // ---------------------------------------------------------------- People order

    [Fact]
    public void People_order_latest_shared_note_first_then_by_name()
    {
        var people = new[]
        {
            new PersonSummary("c3", "Anna Berg", "Berg\u0001Anna", "", "", "", 0, null),
            new PersonSummary("c1", "Emma van Leeuwen", "van Leeuwen\u0001Emma", "", "", "", 5, new DateOnly(2026, 10, 15)),
            new PersonSummary("c2", "Daan Bakker", "Bakker\u0001Daan", "", "", "", 3, new DateOnly(2026, 11, 1)),
        };

        var ordered = OrganizationPage.PeopleOrder(people);

        Assert.Equal(["c2", "c1", "c3"], ordered.Select(p => p.Id));
    }

    [Fact]
    public void People_with_no_shared_notes_sorted_by_name_after_those_with_notes()
    {
        var people = new[]
        {
            new PersonSummary("c1", "Zara", "Zara", "", "", "", 2, new DateOnly(2026, 5, 1)),
            new PersonSummary("c2", "Alice", "Alice", "", "", "", 0, null),
            new PersonSummary("c3", "Bob", "Bob", "", "", "", 0, null),
        };

        var ordered = OrganizationPage.PeopleOrder(people);

        Assert.Equal(["c1", "c2", "c3"], ordered.Select(p => p.Id));
    }

    // ---------------------------------------------------------------- Person filter

    [Fact]
    public void Person_filter_returns_notes_linked_to_both_org_and_person()
    {
        var note1 = new HistoryNote { Id = "n1", Title = "Call", Links = [new LinkedRecordSummary { Id = "c1", Type = RecordType.Contact, Name = "Emma" }] };
        var note2 = new HistoryNote { Id = "n2", Title = "Meeting", Links = [new LinkedRecordSummary { Id = "c2", Type = RecordType.Contact, Name = "Daan" }] };
        var note3 = new HistoryNote { Id = "n3", Title = "Email", Links = [new LinkedRecordSummary { Id = "c1", Type = RecordType.Contact, Name = "Emma" }, new LinkedRecordSummary { Id = "c2", Type = RecordType.Contact, Name = "Daan" }] };

        var person = new PersonSummary("c1", "Emma", "Emma", "", "", "", 2, new DateOnly(2026, 10, 1));

        var filtered = OrganizationPage.PeopleFilter([note1, note2, note3], person);

        Assert.Equal(2, filtered.Count);
        Assert.Equal(new[] { "n1", "n3" }, filtered.Select(n => n.Id));
    }

    // ---------------------------------------------------------------- Not-linked contacts

    [Fact]
    public async Task Not_linked_contacts_match_company_trimmed_case_insensitive_runs_of_spaces()
    {
        var org = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "  Ruby   Eye Clinic  " });
        var c1 = await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Emma", NameLast = "van Leeuwen", Company = "ruby   EYE clinic " });
        var c2 = await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Daan", NameLast = "Bakker", Company = "Ruby Eye Clinic" });

        var linked = new List<PersonSummary> { new(c2.Id, "Daan Bakker", "Bakker\u0001Daan", "", "", "", 0, null) };

        var allContacts = await _data.Contacts.GetContactsAsync();

        var (shown, total) = OrganizationPage.NotLinkedContacts(org, linked, allContacts);

        Assert.Equal(1, total);
        Assert.Single(shown);
        Assert.Equal(c1.Id, shown[0].Id);
    }

    [Fact]
    public async Task Not_linked_contacts_at_most_three_plus_count()
    {
        var org = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Test Corp" });
        var linked = new List<PersonSummary>();

        for (var i = 0; i < 7; i++)
        {
            await _data.Contacts.AddContactAsync(new Contact
            {
                NameFirst = $"First{i}", NameLast = $"Last{i}", Company = "Test Corp",
            });
        }

        var allContacts = await _data.Contacts.GetContactsAsync();

        var (shown, total) = OrganizationPage.NotLinkedContacts(org, linked, allContacts);

        Assert.Equal(7, total);
        Assert.Equal(3, shown.Count);
    }

    [Fact]
    public async Task Not_linked_contacts_leaves_out_already_linked()
    {
        var org = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Test Corp" });
        var c1 = await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Emma", NameLast = "van Leeuwen", Company = "Test Corp" });
        var c2 = await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Daan", NameLast = "Bakker", Company = "Test Corp" });

        var linked = new List<PersonSummary>
        {
            new(c1.Id, "Emma van Leeuwen", "van Leeuwen\u0001Emma", "", "", "", 0, null),
        };

        var allContacts = await _data.Contacts.GetContactsAsync();

        var (shown, total) = OrganizationPage.NotLinkedContacts(org, linked, allContacts);

        Assert.Equal(1, total);
        Assert.Single(shown);
        Assert.Equal(c2.Id, shown[0].Id);
    }

    [Fact]
    public async Task Not_linked_contacts_empty_when_no_match()
    {
        var org = await _data.Organizations.AddOrganizationAsync(new Organization { OrganizationName = "Test Corp" });
        await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Emma", NameLast = "van Leeuwen", Company = "Other Corp" });

        var allContacts = await _data.Contacts.GetContactsAsync();

        var (shown, total) = OrganizationPage.NotLinkedContacts(org, [], allContacts);

        Assert.Equal(0, total);
        Assert.Empty(shown);
    }
}
