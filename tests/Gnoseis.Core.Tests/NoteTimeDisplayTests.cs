/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Data;
using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Notes are shown, grouped, counted and sorted by their createDateTime in the device's time zone,
/// never by createDate or in UTC. Notes either side of local midnight in UTC+9 and UTC−5, and across a
/// daylight saving change.
/// </summary>
public class NoteTimeDisplayTests
{
    private static readonly TimeZoneInfo Tokyo = NoteDatesTests.Zone("Asia/Tokyo");
    private static readonly TimeZoneInfo Bogota = NoteDatesTests.Zone("America/Bogota"); // UTC−5 all year
    private static readonly TimeZoneInfo Toronto = NoteDatesTests.Zone("America/Toronto");
    private static readonly CultureInfo H24 = TestCultures.TwentyFourHour;

    private static long At(TimeZoneInfo zone, int year, int month, int day, int hour, int minute) =>
        NoteDates.ToInstant(new DateTime(year, month, day, hour, minute, 0), zone);

    /// <summary>A note whose createDate is deliberately wrong (UTC midnight), to show nothing reads it.</summary>
    private static (string Id, long CreateDateTime) Note(string id, long createDateTime) => (id, createDateTime);

    private static async Task<List<DayCount>> NotesPerDay(TimeZoneInfo zone, DateOnly today, params (string Id, long CreateDateTime)[] notes)
    {
        using var test = new TestDatabase();
        var data = test.Open(zone);
        TestDatabase.ExecuteRaw(test.FilePath, "DELETE FROM Note;");
        TestDatabase.ExecuteRaw(test.FilePath, notes.Select(n =>
            $"INSERT INTO Note (id, ownerDbId, title, createDate, createDateTime) VALUES ('{n.Id}', 'db1', '{n.Id}', " +
            $"{n.CreateDateTime / 86_400_000 * 86_400_000}, {n.CreateDateTime});").ToArray());
        return await data.Home.GetNotesPerDayAsync(today);
    }

    [Theory]
    [InlineData("Asia/Tokyo")]
    [InlineData("America/Bogota")]
    public async Task Notes_per_day_count_by_the_local_day_either_side_of_midnight(string zoneId)
    {
        var zone = NoteDatesTests.Zone(zoneId);
        var days = await NotesPerDay(zone, new DateOnly(2026, 11, 14),
            Note("before", At(zone, 2026, 11, 13, 23, 59)),
            Note("midnight", At(zone, 2026, 11, 14, 0, 0)),
            Note("after", At(zone, 2026, 11, 14, 0, 1)),
            Note("tomorrow", At(zone, 2026, 11, 15, 0, 0)));

        Assert.Equal(HomeRepository.ChartDays, days.Count);
        Assert.Equal(new DayCount(new DateOnly(2026, 11, 13), 1), days[^2]);
        Assert.Equal(new DayCount(new DateOnly(2026, 11, 14), 2), days[^1]);
        Assert.Equal(3, days.Sum(d => d.Count));
    }

    [Fact]
    public async Task Notes_per_day_count_across_a_daylight_saving_change()
    {
        // Toronto falls back on 1 Nov 2026: that day has 25 hours.
        var days = await NotesPerDay(Toronto, new DateOnly(2026, 11, 2),
            Note("start", At(Toronto, 2026, 11, 1, 0, 0)),
            Note("overlap", At(Toronto, 2026, 11, 1, 1, 30)),
            Note("last", At(Toronto, 2026, 11, 1, 23, 59)),
            Note("next", At(Toronto, 2026, 11, 2, 0, 0)),
            Note("before", At(Toronto, 2026, 10, 31, 23, 59)));

        Assert.Equal(new DayCount(new DateOnly(2026, 10, 31), 1), days[^3]);
        Assert.Equal(new DayCount(new DateOnly(2026, 11, 1), 3), days[^2]);
        Assert.Equal(new DayCount(new DateOnly(2026, 11, 2), 1), days[^1]);
    }

    [Fact]
    public void The_same_note_shows_on_each_zones_own_day()
    {
        var note = new HistoryNote { Id = "n", CreateDate = 0, CreateDateTime = 1794611700000 }; // 13 Nov 23:15 UTC

        Assert.Equal(new DateOnly(2026, 11, 14), ContactHistory.Day(note, Tokyo));
        Assert.Equal(new DateOnly(2026, 11, 13), ContactHistory.Day(note, Bogota));
        Assert.Equal("Note · Saturday 14 November 2026, 08:15", ContactHistory.ReadingCaption(note, H24, Tokyo));
        Assert.Equal("Note · Friday 13 November 2026, 18:15", ContactHistory.ReadingCaption(note, H24, Bogota));
    }

    [Fact]
    public void A_note_at_local_midnight_shows_its_time()
    {
        var note = new Note { CreateDate = At(Toronto, 2026, 11, 14, 0, 0), CreateDateTime = At(Toronto, 2026, 11, 14, 0, 0) };

        Assert.Equal("Saturday 14 November 2026, 00:00", NotePage.DayAndTimeLine(note, H24, Toronto));
        Assert.Equal("Saturday, November 14, 2026, 12:00 AM", NotePage.DayAndTimeLine(note, TestCultures.TwelveHour, Toronto));
        Assert.Equal(TimeSpan.Zero, NotePage.TimeOfDay(note, Toronto));
    }

    [Fact]
    public void History_sorts_by_createDateTime_not_createDate()
    {
        // "late" has the earlier createDate (saved in Tokyo), but the later instant.
        var early = new HistoryNote { Id = "early", CreateDate = 1794614400000, CreateDateTime = 1794600000000 };
        var late = new HistoryNote { Id = "late", CreateDate = 1794582000000, CreateDateTime = 1794611700000 };

        Assert.Equal(["late", "early"], ContactHistory.Order([early, late]).Select(n => n.Id));
        var linked = new LinkedRecordSet(
            [new LinkedRecordSummary { Id = "early", Type = RecordType.Note, NoteDateTime = 1794600000000 },
             new LinkedRecordSummary { Id = "late", Type = RecordType.Note, NoteDateTime = 1794611700000 }]);
        Assert.Equal(["late", "early"], linked.Find(RecordType.Note)!.Records.Select(n => n.Id));
        Assert.Equal(["late", "early"], linked.Find(RecordType.Note)!.Latest.Select(n => n.Id));
    }

    [Fact]
    public void Search_and_text_show_the_note_day_in_the_zone()
    {
        Assert.Equal("14/11/2026 · No links", SearchLogic.ContextLine(new SearchResult("n", 1, "N", NoteDateTime: 1794611700000), Tokyo, H24));
        Assert.Equal("13/11/2026 · No links", SearchLogic.ContextLine(new SearchResult("n", 1, "N", NoteDateTime: 1794611700000), Bogota, H24));
        Assert.Equal("14/11/2026", RecordText.NoteDay(new Note { CreateDateTime = 1794611700000 }, Tokyo, H24));
        Assert.Equal("14 November 2026", NotePage.LinkedNoteDay(new LinkedRecordSummary { Id = "n", Type = RecordType.Note, NoteDateTime = 1794611700000 }, H24, Tokyo));
    }
}
