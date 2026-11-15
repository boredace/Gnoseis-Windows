/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>
/// The note time rule: a local day and time becomes a UTC instant in the device's time zone, and an
/// upgraded note's old value (local clock time stored as if UTC) is converted the same way. The rows
/// are the shared conversion examples, computed with java.time on Android; zones by IANA id.
/// </summary>
public class NoteDatesTests
{
    /// <summary>Zone, written (local), v3 createDateTime, v3 createDate, v4 createDateTime, v4 createDate, shown.</summary>
    public static TheoryData<string, string, long, long, long, long, string> Examples => new()
    {
        { "UTC", "2026-11-14 14:30", 1794666600000, 1794614400000, 1794666600000, 1794614400000, "2026-11-14 14:30 +00:00" },
        { "America/Toronto", "2026-07-15 14:30", 1784125800000, 1784073600000, 1784140200000, 1784088000000, "2026-07-15 14:30 -04:00" },
        { "America/Toronto", "2026-01-15 14:30", 1768487400000, 1768435200000, 1768505400000, 1768453200000, "2026-01-15 14:30 -05:00" },
        { "America/Toronto", "2026-11-14 00:00", 1794614400000, 1794614400000, 1794632400000, 1794632400000, "2026-11-14 00:00 -05:00" },
        { "America/Toronto", "2026-03-08 02:30", 1772937000000, 1772928000000, 1772955000000, 1772946000000, "2026-03-08 03:30 -04:00" },
        { "America/Toronto", "2026-11-01 01:30", 1793496600000, 1793491200000, 1793511000000, 1793505600000, "2026-11-01 01:30 -04:00" },
        { "Asia/Tokyo", "2026-11-14 08:15", 1794644100000, 1794614400000, 1794611700000, 1794582000000, "2026-11-14 08:15 +09:00" },
        { "Asia/Kolkata", "2026-11-14 23:59", 1794700740000, 1794614400000, 1794680940000, 1794594600000, "2026-11-14 23:59 +05:30" },
        { "Europe/London", "2026-06-01 00:30", 1780273800000, 1780272000000, 1780270200000, 1780268400000, "2026-06-01 00:30 +01:00" },
        { "America/Santiago", "2026-09-06 00:00", 1788652800000, 1788652800000, 1788667200000, 1788667200000, "2026-09-06 01:00 -03:00" },
        { "America/Santiago", "2026-09-06 00:30", 1788654600000, 1788652800000, 1788669000000, 1788667200000, "2026-09-06 01:30 -03:00" },
        { "Australia/Lord_Howe", "2026-10-04 02:15", 1791080100000, 1791072000000, 1791042300000, 1791034200000, "2026-10-04 02:45 +11:00" },
    };

    internal static TimeZoneInfo Zone(string ianaId) => TimeZoneInfo.FindSystemTimeZoneById(ianaId);

    private static DateTime Local(string text) =>
        DateTime.ParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static Note NoteAt(long createDate, long createDateTime) =>
        new() { CreateDate = createDate, CreateDateTime = createDateTime };

    [Theory]
    [MemberData(nameof(Examples))]
    public void Upgrading_a_v3_note_converts_both_columns_in_the_zone(
        string zone, string written, long v3DateTime, long v3Date, long v4DateTime, long v4Date, string shown)
    {
        _ = (written, v3Date, shown);
        Assert.Equal((v4Date, v4DateTime), NoteDates.UpgradeFromV3(v3DateTime, Zone(zone)));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void A_note_saved_at_a_local_day_and_time_stores_the_v4_values(
        string zone, string written, long v3DateTime, long v3Date, long v4DateTime, long v4Date, string shown)
    {
        _ = (v3DateTime, v3Date, shown);
        var local = Local(written);

        var stored = NoteDates.ToStored(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), Zone(zone));

        Assert.Equal((v4Date, v4DateTime), stored);
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void A_v4_note_shows_in_its_zone_as_the_example_says(
        string zone, string written, long v3DateTime, long v3Date, long v4DateTime, long v4Date, string shown)
    {
        _ = (written, v3DateTime, v3Date, v4Date);
        var tz = Zone(zone);
        var local = NoteDates.Shown(v4DateTime, tz);
        var offset = tz.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(v4DateTime));
        var text = new DateTimeOffset(local, offset).ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);

        Assert.Equal(shown, text);
    }

    [Theory]
    [InlineData("America/Toronto", "2026-11-13 18:15")]
    [InlineData("Asia/Kolkata", "2026-11-14 04:45")]
    [InlineData("Asia/Tokyo", "2026-11-14 08:15")]
    public void The_Tokyo_note_shows_in_each_zones_own_time(string zone, string shown)
    {
        Assert.Equal(Local(shown), NoteDates.Shown(1794611700000, Zone(zone)));
    }

    [Fact]
    public void A_new_note_nobody_changed_is_the_moment_of_saving()
    {
        var now = new DateTimeOffset(2026, 11, 13, 23, 15, 42, 123, TimeSpan.Zero);

        var stored = NoteDates.ForNewNote(null, null, now, Zone("Asia/Tokyo"));

        Assert.Equal((1794582000000, now.ToUnixTimeMilliseconds()), stored);
    }

    [Fact]
    public void A_new_note_with_a_picked_day_keeps_the_time_of_saving_without_seconds()
    {
        var tokyo = Zone("Asia/Tokyo");
        var now = new DateTimeOffset(2026, 11, 13, 23, 15, 42, 123, TimeSpan.Zero); // 08:15:42 on 14 Nov in Tokyo

        var stored = NoteDates.ForNewNote(new DateOnly(2026, 11, 10), null, now, tokyo);

        Assert.Equal(NoteDates.ToStored(new DateOnly(2026, 11, 10), new TimeOnly(8, 15), tokyo), stored);
    }

    [Fact]
    public void A_new_note_with_a_picked_time_keeps_the_day_of_saving()
    {
        var tokyo = Zone("Asia/Tokyo");
        var now = new DateTimeOffset(2026, 11, 13, 23, 15, 42, 123, TimeSpan.Zero);

        var stored = NoteDates.ForNewNote(null, new TimeOnly(17, 5, 30), now, tokyo);

        Assert.Equal(NoteDates.ToStored(new DateOnly(2026, 11, 14), new TimeOnly(17, 5), tokyo), stored);
    }

    [Fact]
    public void An_unchanged_note_keeps_both_columns_exactly_even_in_another_zone()
    {
        // Saved in Tokyo, with a createDate from Tokyo's day; edited in Toronto, where it shows on 13 Nov.
        var note = NoteAt(1794582000000, 1794611700123);
        var toronto = Zone("America/Toronto");

        Assert.Equal((note.CreateDate, note.CreateDateTime), NoteDates.ForEditedNote(note, null, null, toronto));
        Assert.Equal((note.CreateDate, note.CreateDateTime),
            NoteDates.ForEditedNote(note, new DateOnly(2026, 11, 13), new TimeOnly(18, 15), toronto));
    }

    [Fact]
    public void Changing_the_day_keeps_the_shown_time()
    {
        var toronto = Zone("America/Toronto");
        var note = NoteAt(1784088000000, 1784140200000 + 12_345); // 2026-07-15 14:30:12.345 in Toronto

        var stored = NoteDates.ForEditedNote(note, new DateOnly(2026, 1, 15), null, toronto);

        Assert.Equal((1768453200000, 1768505400000 + 12_345), stored);
    }

    [Fact]
    public void Changing_the_time_keeps_the_shown_day()
    {
        var toronto = Zone("America/Toronto");
        var note = NoteAt(1784088000000, 1784140200000); // 2026-07-15 14:30 in Toronto

        var stored = NoteDates.ForEditedNote(note, new DateOnly(2026, 7, 15), new TimeOnly(9, 5), toronto);

        Assert.Equal(NoteDates.ToStored(new DateOnly(2026, 7, 15), new TimeOnly(9, 5), toronto), stored);
        Assert.Equal(new DateTime(2026, 7, 15, 9, 5, 0), NoteDates.Shown(stored.CreateDateTime, toronto));
    }

    [Fact]
    public void A_change_into_a_gap_follows_the_rule()
    {
        var toronto = Zone("America/Toronto");
        var note = NoteAt(1772859600000, 1772955000000 - 3_600_000 * 4); // 2026-03-07 22:30 in Toronto

        var stored = NoteDates.ForEditedNote(note, new DateOnly(2026, 3, 8), new TimeOnly(2, 30), toronto);

        Assert.Equal((1772946000000, 1772955000000), stored);
    }

    [Fact]
    public void The_new_and_edit_rules_store_createDate_as_the_start_of_the_local_day()
    {
        var santiago = Zone("America/Santiago");
        var (createDate, createDateTime) = NoteDates.ForInstant(1788669000000, santiago); // 01:30, after a midnight gap

        Assert.Equal(1788669000000, createDateTime);
        Assert.Equal(1788667200000, createDate);
        Assert.Equal(new DateTime(2026, 9, 6, 1, 0, 0), NoteDates.Shown(createDate, santiago));
    }
}
