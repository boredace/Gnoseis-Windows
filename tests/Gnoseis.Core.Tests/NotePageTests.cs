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

/// <summary>The note page: title, day and time line, the created and changed caption, copy text, side column and saving in place.</summary>
public class NotePageTests : IDisposable
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public NotePageTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    // Thu 15 Oct 2026, 09:30 (stored as UTC midnight of the day plus the time of day).
    private static Note Meeting(string? title = "Kick-off", string? text = "Agenda\nBudget") => new()
    {
        Title = title,
        TextContents = text,
        CreateDate = EpochTime.UtcMidnight(new DateOnly(2026, 10, 15)),
        CreateDateTime = EpochTime.UtcMidnight(new DateOnly(2026, 10, 15)) + (long)new TimeSpan(9, 30, 0).TotalMilliseconds,
    };

    private static long Utc(int year, int month, int day, int hour, int minute, int second = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero).ToUnixTimeMilliseconds();

    // ---------------------------------------------------------------- Header

    [Fact]
    public void Title_is_trimmed()
    {
        Assert.Equal(("Kick-off", false), NotePage.Title(Meeting("  Kick-off ")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_title_shows_untitled_note(string? title)
    {
        Assert.Equal(("Untitled note", true), NotePage.Title(Meeting(title)));
    }

    [Fact]
    public void Day_and_time_line()
    {
        Assert.Equal("Thu 15 Oct 2026, 09:30", NotePage.DayAndTimeLine(Meeting(), Invariant));
    }

    // ---------------------------------------------------------------- Created and changed

    [Fact]
    public void Caption_with_both_times()
    {
        Assert.Equal("Created 15 Oct 2026, 13:40 · changed 20 Oct 2026, 08:55",
            NotePage.TimesCaption(Utc(2026, 10, 15, 13, 40), Utc(2026, 10, 20, 8, 55), TimeZoneInfo.Utc, Invariant));
    }

    [Fact]
    public void Caption_in_the_given_time_zone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
        Assert.Equal("Created 15 Oct 2026, 15:40", NotePage.TimesCaption(Utc(2026, 10, 15, 13, 40), null, zone, Invariant));
    }

    [Fact]
    public void Caption_with_one_time()
    {
        Assert.Equal("Created 15 Oct 2026, 13:40", NotePage.TimesCaption(Utc(2026, 10, 15, 13, 40), null, TimeZoneInfo.Utc, Invariant));
        Assert.Equal("Changed 20 Oct 2026, 08:55", NotePage.TimesCaption(null, Utc(2026, 10, 20, 8, 55), TimeZoneInfo.Utc, Invariant));
    }

    [Fact]
    public void Caption_without_times_is_empty()
    {
        Assert.Equal("", NotePage.TimesCaption(null, null, TimeZoneInfo.Utc, Invariant));
    }

    [Fact]
    public void Caption_leaves_out_changed_when_equal_to_the_minute()
    {
        Assert.Equal("Created 15 Oct 2026, 13:40",
            NotePage.TimesCaption(Utc(2026, 10, 15, 13, 40, 5), Utc(2026, 10, 15, 13, 40, 50), TimeZoneInfo.Utc, Invariant));
    }

    // ---------------------------------------------------------------- Copy note text

    [Fact]
    public void Copy_text_with_a_title()
    {
        var nl = Environment.NewLine;
        Assert.Equal($"Kick-off{nl}Thu 15 Oct 2026, 09:30{nl}{nl}Agenda\nBudget", NotePage.CopyText(Meeting(), Invariant));
    }

    [Fact]
    public void Copy_text_without_a_title()
    {
        var nl = Environment.NewLine;
        Assert.Equal($"Thu 15 Oct 2026, 09:30{nl}{nl}Agenda\nBudget", NotePage.CopyText(Meeting(title: " "), Invariant));
    }

    // ---------------------------------------------------------------- Side column

    private static LinkedRecordSummary Linked(RecordType type, string id, long? linkedAt, string name = "", string firstLine = "") =>
        new() { Id = id, Type = type, Name = name, SortName = type == RecordType.Note ? "" : name, FirstLine = firstLine, LinkedAt = linkedAt };

    [Fact]
    public void Side_sections_contacts_first_then_the_fixed_type_order()
    {
        var linked = new LinkedRecordSet(
        [
            Linked(RecordType.Note, "n1", 1, "Other note"),
            Linked(RecordType.Item, "i1", 1, "Laptop"),
            Linked(RecordType.Item, "i2", 2, "Phone"),
            Linked(RecordType.Category, "c1", 1, "Work"),
            Linked(RecordType.Organization, "o1", 1, "Acme"),
            Linked(RecordType.Contact, "p1", 1, "Emma"),
        ]);

        Assert.Equal(
            [RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item, RecordType.Note],
            NotePage.SideSections(linked).Select(s => s.Type));
    }

    [Fact]
    public void Side_sections_leave_out_empty_types()
    {
        var linked = new LinkedRecordSet([Linked(RecordType.Category, "c1", 1, "Work")]);

        Assert.Equal([RecordType.Category], NotePage.SideSections(linked).Select(s => s.Type));
        Assert.Empty(NotePage.SideSections(LinkedRecordSet.Empty));
    }

    [Fact]
    public void Side_rows_latest_linked_first_then_undated_by_title()
    {
        var linked = new LinkedRecordSet(
        [
            Linked(RecordType.Note, "1", null, firstLine: "zebra"),
            Linked(RecordType.Note, "2", null, "Apple"),
            Linked(RecordType.Note, "3", 10, "Old"),
            Linked(RecordType.Note, "4", 20, "New"),
        ]);

        Assert.Equal(["4", "3", "2", "1"], NotePage.SideSections(linked).Single().Records.Select(r => r.Id));
    }

    [Fact]
    public void Linked_note_name_is_the_title_then_first_line_then_day()
    {
        var day = EpochTime.UtcMidnight(new DateOnly(2026, 10, 15));
        Assert.Equal("Visit", NotePage.LinkedNoteName(Linked(RecordType.Note, "1", null, "Visit", "Line")));
        Assert.Equal("Line", NotePage.LinkedNoteName(Linked(RecordType.Note, "1", null, "", "Line")));
        Assert.Equal("15 Oct 2026", NotePage.LinkedNoteDay(Linked(RecordType.Note, "1", null) with { NoteDate = day }, Invariant));
    }

    // ---------------------------------------------------------------- Saving in place

    [Fact]
    public void Valid_with_a_title_or_some_text()
    {
        Assert.True(NotePage.IsValid("T", ""));
        Assert.True(NotePage.IsValid("", "x"));
        Assert.False(NotePage.IsValid("", ""));
    }

    [Fact]
    public void Nothing_changed_gives_no_edit()
    {
        var note = Meeting(title: null, text: "Agenda");
        Assert.Null(NotePage.Edited(note, "", "Agenda", NoteDates.DisplayedDay(note)));
    }

    [Fact]
    public void An_unchanged_empty_value_stays_as_stored()
    {
        var note = Meeting(title: null, text: "Agenda");
        var edited = NotePage.Edited(note, "", "Agenda, budget", NoteDates.DisplayedDay(note));

        Assert.NotNull(edited);
        Assert.Null(edited.Title);
        Assert.Equal("Agenda, budget", edited.TextContents);
        Assert.Equal(note.CreateDateTime, edited.CreateDateTime);
    }

    [Fact]
    public void A_new_day_keeps_the_time_of_day()
    {
        var note = Meeting();
        var edited = NotePage.Edited(note, "Kick-off", "Agenda\nBudget", new DateOnly(2026, 10, 20))!;

        Assert.Equal(EpochTime.UtcMidnight(new DateOnly(2026, 10, 20)), edited.CreateDate);
        Assert.Equal("Tue 20 Oct 2026, 09:30", NotePage.DayAndTimeLine(edited, Invariant));
    }

    [Fact]
    public async Task Saving_in_place_writes_the_change_and_stamps_updated_at()
    {
        var note = await _data.Notes.AddNoteAsync(Meeting());
        TestDatabase.ExecuteRaw(_test.FilePath, "UPDATE Note SET createdAt = 100, updatedAt = 200;");
        var before = EpochTime.UtcNowMillis();

        var edited = NotePage.Edited(note, "Kick-off", "Agenda\nBudget\nRisks", new DateOnly(2026, 10, 16))!;
        await _data.Notes.UpdateNoteAsync(edited);

        var saved = (await _data.Notes.GetNoteAsync(note.Id))!;
        var (createdAt, updatedAt) = await _data.Notes.GetTimesAsync(note.Id);
        Assert.Equal("Kick-off", saved.Title);
        Assert.Equal("Agenda\nBudget\nRisks", saved.TextContents);
        Assert.Equal("Fri 16 Oct 2026, 09:30", NotePage.DayAndTimeLine(saved, Invariant));
        Assert.Equal(100, createdAt);
        Assert.InRange(updatedAt!.Value, before, EpochTime.UtcNowMillis());
    }

    [Fact]
    public async Task Saving_in_place_without_a_change_writes_nothing()
    {
        var note = await _data.Notes.AddNoteAsync(Meeting());
        TestDatabase.ExecuteRaw(_test.FilePath, "UPDATE Note SET createdAt = 100, updatedAt = 200;");

        Assert.Null(NotePage.Edited(note, "Kick-off", "Agenda\nBudget", NoteDates.DisplayedDay(note)));
        Assert.Equal((100L, 200L), await _data.Notes.GetTimesAsync(note.Id));
    }

    [Fact]
    public async Task Times_of_a_note_from_before_v3_are_unknown()
    {
        var note = await _data.Notes.AddNoteAsync(Meeting());
        TestDatabase.ExecuteRaw(_test.FilePath, "UPDATE Note SET createdAt = NULL, updatedAt = NULL;");

        Assert.Equal(((long?)null, (long?)null), await _data.Notes.GetTimesAsync(note.Id));
        Assert.Equal(((long?)null, (long?)null), await _data.Notes.GetTimesAsync("missing"));
    }
}
