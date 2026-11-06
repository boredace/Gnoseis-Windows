/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Data;
using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>The contact page: header line, company link, chips, copy text, bands and the history.</summary>
public class ContactPageTests : IDisposable
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public ContactPageTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    private static Contact Emma(string? job = "Owner", string? company = "Ruby Eye Clinic") => new()
    {
        NameFirst = "Emma",
        NameLast = "van Leeuwen",
        JobTitle = job,
        Company = company,
    };

    private static LinkedRecordSummary Organization(string name, string id = "o1") =>
        new() { Id = id, Type = RecordType.Organization, Name = name, SortName = name };

    // ---------------------------------------------------------------- Header line

    [Theory]
    [InlineData("Owner", "Ruby Eye Clinic", "Owner at Ruby Eye Clinic", false)]
    [InlineData(" Owner ", null, "Owner", false)]
    [InlineData(null, " Ruby Eye Clinic ", "At Ruby Eye Clinic", false)]
    [InlineData("  ", "", "Contact", true)]
    public void Header_line_covers_job_title_and_company(string? job, string? company, string text, bool placeholder)
    {
        Assert.Equal((text, placeholder), ContactPage.HeaderLine(Emma(job, company)));
    }

    // ---------------------------------------------------------------- Company and organization

    [Fact]
    public void Company_matches_a_linked_organization_trimmed_ignoring_case_and_runs_of_spaces()
    {
        var link = ContactPage.Company(Emma(company: "  ruby   EYE clinic "), [Organization("Ruby Eye  Clinic")]);

        Assert.True(link.IsLinked);
        Assert.Equal("o1", link.Organization!.Id);
        Assert.Equal("ruby   EYE clinic", link.Company);
        Assert.False(link.CanLinkAsOrganization);
    }

    [Fact]
    public void No_company_neither_links_nor_offers_to_link()
    {
        var link = ContactPage.Company(Emma(company: " "), [Organization("Ruby Eye Clinic")]);

        Assert.False(link.HasCompany);
        Assert.False(link.IsLinked);
        Assert.False(link.CanLinkAsOrganization);
    }

    [Fact]
    public void Company_without_a_linked_organization_offers_to_link_it()
    {
        Assert.True(ContactPage.Company(Emma(), []).CanLinkAsOrganization);
    }

    [Fact]
    public void Company_with_a_different_linked_organization_offers_to_link_it()
    {
        LinkedRecordSummary category = new() { Id = "c1", Type = RecordType.Category, Name = "Ruby Eye Clinic" };

        var link = ContactPage.Company(Emma(), [Organization("Ruby Eye"), category]);

        Assert.False(link.IsLinked);
        Assert.True(link.CanLinkAsOrganization);
    }

    // ---------------------------------------------------------------- Chips

    private static Contact AllFields() => Emma() with
    {
        PhoneMain = "1", PhoneMobile = "2", PhoneHome = "3", PhoneWork = "4",
        EmailMain = "a@x.nl", EmailMobile = "b@x.nl", EmailHome = "c@x.nl", EmailWork = "d@x.nl",
    };

    [Fact]
    public void Chips_follow_the_field_order_with_their_labels()
    {
        var chips = ContactPage.Chips(AllFields());

        Assert.Equal(
            ["PhoneMain", "PhoneMobile", "PhoneWork", "PhoneHome", "EmailMain", "EmailWork", "EmailHome", "EmailMobile"],
            chips.Select(c => c.Field));
        Assert.Equal(["main", "mobile", "work", "home", "main", "work", "home", "mobile"], chips.Select(c => c.Label));
        Assert.Equal(ContactChipKind.Mobile, chips[1].Kind);
        Assert.Equal(ContactChipKind.Email, chips[4].Kind);
    }

    [Fact]
    public void Chips_leave_out_blank_fields_and_trim_values()
    {
        var chips = ContactPage.Chips(Emma() with { PhoneWork = " +31 30 816 8888 ", PhoneMain = "  ", EmailWork = "emma@rubyeyeclinic.nl" });

        Assert.Equal(["PhoneWork", "EmailWork"], chips.Select(c => c.Field));
        Assert.Equal("+31 30 816 8888", chips[0].Value);
        Assert.Equal("tel:+31308168888", chips[0].Target!.OriginalString);
        Assert.Equal("mailto:emma@rubyeyeclinic.nl", chips[1].Target!.OriginalString);
        Assert.Equal("Call +31 30 816 8888, work", chips[0].AutomationName);
        Assert.Equal("Send e-mail to emma@rubyeyeclinic.nl, work", chips[1].AutomationName);
    }

    [Theory]
    [InlineData(ContactPageBand.S, 2)]
    [InlineData(ContactPageBand.M, 3)]
    [InlineData(ContactPageBand.L, 4)]
    [InlineData(ContactPageBand.XL, 8)]
    [InlineData(ContactPageBand.XXL, 8)]
    public void Chips_fold_per_band(ContactPageBand band, int shown)
    {
        var folding = ContactPage.Fold(ContactPage.Chips(AllFields()), band);

        Assert.Equal(shown, folding.Shown.Count);
        Assert.Equal(8 - shown, folding.Folded.Count);
        Assert.Equal(shown < 8, folding.HasMore);
        if (folding.HasMore)
        {
            Assert.Equal($"+{8 - shown} more", folding.MoreText);
        }
    }

    [Fact]
    public void Chips_that_fit_do_not_fold()
    {
        var folding = ContactPage.Fold(ContactPage.Chips(Emma() with { PhoneMain = "1", EmailMain = "a@x.nl" }), ContactPageBand.S);

        Assert.Equal(2, folding.Shown.Count);
        Assert.False(folding.HasMore);
    }

    [Fact]
    public void Missing_fact_chips_for_job_title_company_phone_and_email()
    {
        Assert.Equal(
            [MissingContactFact.JobTitle, MissingContactFact.Company, MissingContactFact.Phone, MissingContactFact.Email],
            ContactPage.MissingFacts(new Contact { NameLast = "Bakker" }));
        Assert.Equal([MissingContactFact.Email], ContactPage.MissingFacts(Emma() with { PhoneHome = "1" }));
        Assert.Equal([MissingContactFact.Phone], ContactPage.MissingFacts(Emma() with { EmailMobile = "a@x.nl" }));
        Assert.Empty(ContactPage.MissingFacts(AllFields()));
        Assert.Equal("Add job title", ContactPage.AutomationName(MissingContactFact.JobTitle));
        Assert.Equal("PhoneMain", ContactPage.FieldOf(MissingContactFact.Phone));
        Assert.Equal("EmailMain", ContactPage.FieldOf(MissingContactFact.Email));
    }

    // ---------------------------------------------------------------- Copy contact details

    [Fact]
    public void Copy_text_has_the_name_the_header_line_and_the_fields_in_chip_order()
    {
        var contact = Emma() with
        {
            EmailWork = "emma.vanleeuwen@rubyeyeclinic.nl",
            PhoneWork = "+31 30 816 8888",
            Comments = "Not copied",
        };

        Assert.Equal(
            string.Join(Environment.NewLine,
                "Emma van Leeuwen", "Owner at Ruby Eye Clinic",
                "Phone (work): +31 30 816 8888", "E-mail (work): emma.vanleeuwen@rubyeyeclinic.nl"),
            ContactPage.CopyText(contact));
    }

    [Fact]
    public void Copy_text_leaves_out_the_placeholder_line()
    {
        Assert.Equal("Daan Bakker", ContactPage.CopyText(new Contact { NameFirst = "Daan", NameLast = "Bakker" }));
    }

    // ---------------------------------------------------------------- Bands

    [Theory]
    [InlineData(859, ContactPageBand.S)]
    [InlineData(860, ContactPageBand.M)]
    [InlineData(1099, ContactPageBand.M)]
    [InlineData(1100, ContactPageBand.L)]
    [InlineData(1699, ContactPageBand.L)]
    [InlineData(1700, ContactPageBand.XL)]
    [InlineData(2599, ContactPageBand.XL)]
    [InlineData(2600, ContactPageBand.XXL)]
    public void Band_follows_the_pane_width(double width, ContactPageBand band)
    {
        Assert.Equal(band, ContactPage.BandFor(width));
    }

    [Fact]
    public void Side_column_rows_latest_linked_first_then_undated_by_name()
    {
        var records = new[]
        {
            Organization("B", "1") with { LinkedAt = null },
            Organization("A", "2") with { LinkedAt = null },
            Organization("C", "3") with { LinkedAt = 10 },
            Organization("D", "4") with { LinkedAt = 20 },
        };

        Assert.Equal(["4", "3", "2", "1"], ContactPage.SideOrder(records).Select(r => r.Id));
        Assert.Equal(5, ContactPage.SideRowCount(ContactPageBand.L));
        Assert.Equal(6, ContactPage.SideRowCount(ContactPageBand.XL));
        Assert.Equal(8, ContactPage.SideRowCount(ContactPageBand.XXL));
    }

    [Fact]
    public void Day_and_time_text()
    {
        var today = new DateOnly(2026, 11, 5);
        var time = new TimeSpan(15, 40, 0);

        Assert.Equal("Today, 15:40", ContactPage.DayAndTimeText(today, time, today, Invariant));
        Assert.Equal("Yesterday, 15:40", ContactPage.DayAndTimeText(today.AddDays(-1), time, today, Invariant));
        Assert.Equal("Tue 26 May, 15:40", ContactPage.DayAndTimeText(new DateOnly(2026, 5, 26), time, today, Invariant));
        Assert.Equal("Mon 26 May 2025, 15:40", ContactPage.DayAndTimeText(new DateOnly(2025, 5, 26), time, today, Invariant));
    }

    // ---------------------------------------------------------------- History

    private static HistoryNote Note(string id, DateOnly day, int minutes = 0, string title = "", string text = "") => new()
    {
        Id = id,
        Title = title,
        Text = text,
        CreateDate = EpochTime.UtcMidnight(day),
        CreateDateTime = EpochTime.UtcMidnight(day) + minutes * 60_000L,
    };

    [Fact]
    public void History_is_newest_first_by_day_then_time_then_title()
    {
        var day = new DateOnly(2026, 5, 26);
        var notes = new[]
        {
            Note("old", day.AddDays(-1), 900),
            Note("b", day, 60, "Beta"),
            Note("a", day, 60, "alpha"),
            Note("late", day, 120),
        };

        Assert.Equal(["late", "a", "b", "old"], ContactHistory.Order(notes).Select(n => n.Id));
    }

    [Fact]
    public void Month_headers_from_13_notes()
    {
        Assert.False(ContactHistory.IsGrouped(12));
        Assert.True(ContactHistory.IsGrouped(13));
        Assert.Equal("Oct 2026", ContactHistory.MonthText(new DateOnly(2026, 10, 14), Invariant));
    }

    [Fact]
    public void Year_counts_newest_year_first_only_over_more_than_one_year()
    {
        var notes = new[]
        {
            Note("1", new DateOnly(2026, 1, 2)), Note("2", new DateOnly(2026, 6, 2)),
            Note("3", new DateOnly(2024, 6, 2)), Note("4", new DateOnly(2025, 12, 31)),
        };

        Assert.Equal([(2026, 2), (2025, 1), (2024, 1)], ContactHistory.YearCounts(notes));
        Assert.Empty(ContactHistory.YearCounts(notes.Take(2)));
    }

    [Fact]
    public void Filter_matches_title_and_text_ignoring_case_from_one_character()
    {
        var note = Note("1", new DateOnly(2026, 1, 2), title: "Call about lenses", text: "Wants the NEW frames");

        Assert.True(ContactHistory.Matches(note, "LENS"));
        Assert.True(ContactHistory.Matches(note, "new"));
        Assert.True(ContactHistory.Matches(note, "w"));
        Assert.True(ContactHistory.Matches(note, " "));
        Assert.False(ContactHistory.Matches(note, "invoice"));
    }

    [Fact]
    public void Row_title_and_text_with_and_without_a_title()
    {
        var titled = Note("1", new DateOnly(2026, 5, 26), title: "Visit", text: "\nFirst line\nSecond line");
        var untitled = titled with { Title = "" };

        Assert.Equal(("Visit", "First line"), (ContactHistory.RowTitle(titled), ContactHistory.RowText(titled)));
        Assert.Equal(("First line", "Second line"), (ContactHistory.RowTitle(untitled), ContactHistory.RowText(untitled)));
        Assert.Equal("26 May", ContactHistory.DayText(titled, Invariant));
        Assert.Equal("Tue · 2026", ContactHistory.WeekdayAndYearText(titled, Invariant));
    }

    [Fact]
    public void Row_links_leave_out_the_contact_and_show_three_then_a_count()
    {
        LinkedRecordSummary Linked(string id, string name) => new() { Id = id, Type = RecordType.Item, Name = name };
        var note = Note("1", new DateOnly(2026, 5, 26)) with
        {
            Links = [Linked("me", "Emma"), Linked("a", "A"), Linked("b", "B"), Linked("c", "C"), Linked("d", "D")],
        };

        Assert.Equal("A · B · C +1", ContactHistory.RowLinks(note, "me"));
        Assert.Equal("A", ContactHistory.RowLinks(note with { Links = note.Links.Take(2).ToList() }, "me"));
    }

    [Fact]
    public void Reading_caption_shows_the_time_only_when_the_note_has_one()
    {
        var day = new DateOnly(2026, 5, 26);

        Assert.Equal("Note · Tue 26 May 2026, 15:00", ContactHistory.ReadingCaption(Note("1", day, 900), Invariant));
        Assert.Equal("Note · Tue 26 May 2026", ContactHistory.ReadingCaption(Note("1", day), Invariant));
    }

    [Fact]
    public async Task Linked_notes_come_with_their_text_and_every_record_linked_to_them()
    {
        var contact = await _data.Contacts.AddContactAsync(Emma());
        var other = await _data.Contacts.AddContactAsync(new Contact { NameFirst = "Daan", NameLast = "Bakker" });
        var item = await _data.Items.AddItemAsync(new Item { ItemName = "Lenses" });
        var note = await _data.Notes.AddNoteAsync(new Note { Title = " Visit ", TextContents = "Line 1\nLine 2" });
        var unrelated = await _data.Notes.AddNoteAsync(new Note { Title = "Elsewhere" });
        async Task Link(string a, RecordType ta, string b, RecordType tb) =>
            await _data.LinkedRecords.AddLinkedRecordAsync(new LinkedRecord
            {
                OwnerDbId = "db1", Record1Id = a, Record1TypeId = (int)ta, Record2Id = b, Record2TypeId = (int)tb,
            });
        await Link(contact.Id, RecordType.Contact, note.Id, RecordType.Note);
        await Link(item.Id, RecordType.Item, note.Id, RecordType.Note);
        await Link(note.Id, RecordType.Note, other.Id, RecordType.Contact);
        await Link(unrelated.Id, RecordType.Note, item.Id, RecordType.Item);

        var notes = await _data.LinkedRecords.GetLinkedNotesWithLinksAsync(contact.Id);

        var found = Assert.Single(notes);
        Assert.Equal(("Visit", "Line 1\nLine 2"), (found.Title, found.Text));
        Assert.Equal(["Daan Bakker", "Emma van Leeuwen", "Lenses"], found.Links.Select(l => l.Name));
        Assert.Equal("Daan Bakker · Lenses", ContactHistory.RowLinks(found, contact.Id));
    }

    [Fact]
    public async Task Contact_created_at_is_read()
    {
        var before = EpochTime.UtcNowMillis();
        var contact = await _data.Contacts.AddContactAsync(Emma());

        Assert.InRange((await _data.Contacts.GetCreatedAtAsync(contact.Id))!.Value, before, EpochTime.UtcNowMillis());
        Assert.Null(await _data.Contacts.GetCreatedAtAsync("missing"));
    }
}
