/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using System.Text.RegularExpressions;
using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Every shown date and time follows the current culture's standard formats (Windows' regional
/// format): the 12- or 24-hour clock and the order of day and month, never a fixed pattern.
/// </summary>
public sealed class DateTextTests
{
    private static readonly CultureInfo H12 = TestCultures.TwelveHour;
    private static readonly CultureInfo H24 = TestCultures.TwentyFourHour;
    private static readonly TimeZoneInfo Tokyo = NoteDatesTests.Zone("Asia/Tokyo");
    private static readonly TimeZoneInfo Toronto = NoteDatesTests.Zone("America/Toronto");

    // A 12-hour time ("1:40 PM") and a 24-hour time ("13:40" or "09:30", never followed by AM or PM).
    private static readonly Regex TwelveHourTime = new(@"\b\d{1,2}:\d{2} (AM|PM)\b");
    private static readonly Regex TwentyFourHourTime = new(@"\b\d{2}:\d{2}\b(?! (AM|PM))");

    private static long Utc(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    // 15 Oct 2026 13:40 in UTC.
    private static readonly long Afternoon = Utc(2026, 10, 15, 13, 40);

    private static IEnumerable<Func<CultureInfo, string>> TimedTexts()
    {
        var note = new Note { CreateDate = Afternoon, CreateDateTime = Afternoon };
        var history = new HistoryNote { Id = "n", CreateDateTime = Afternoon };
        yield return c => NotePage.DayAndTimeLine(note, c, TimeZoneInfo.Utc);
        yield return c => NotePage.TimesCaption(Afternoon, Utc(2026, 10, 20, 17, 5), TimeZoneInfo.Utc, c);
        yield return c => ContactPage.DayAndTimeText(new DateOnly(2026, 10, 15), new TimeSpan(13, 40, 0), new DateOnly(2026, 11, 5), c);
        yield return c => ContactPage.DayAndTimeText(new DateOnly(2026, 11, 5), new TimeSpan(13, 40, 0), new DateOnly(2026, 11, 5), c);
        yield return c => ContactHistory.ReadingCaption(history, c, TimeZoneInfo.Utc);
    }

    [Fact]
    public void Times_follow_the_12_hour_clock()
    {
        foreach (var text in TimedTexts().Select(f => f(H12)))
        {
            Assert.Matches(TwelveHourTime, text);
            Assert.DoesNotMatch(TwentyFourHourTime, text);
        }
    }

    [Fact]
    public void Times_follow_the_24_hour_clock()
    {
        foreach (var text in TimedTexts().Select(f => f(H24)))
        {
            Assert.Matches(TwentyFourHourTime, text);
            Assert.DoesNotMatch(TwelveHourTime, text);
            Assert.DoesNotContain("AM", text);
            Assert.DoesNotContain("PM", text);
        }
    }

    [Fact]
    public void Caption_moves_with_the_time_zone()
    {
        // Added at 05:57 UTC on 14 Nov 2026: 14:57 in Tokyo (UTC+9), 00:57 in Toronto (UTC-5), 14 hours apart.
        var added = Utc(2026, 11, 14, 5, 57);

        Assert.Equal("Added 14/11/2026, 14:57", NotePage.TimesCaption(added, added, Tokyo, H24));
        Assert.Equal("Added 14/11/2026, 00:57", NotePage.TimesCaption(added, added, Toronto, H24));
        var tokyo = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(added), Tokyo);
        var toronto = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(added), Toronto);
        Assert.Equal(TimeSpan.FromHours(14), tokyo.DateTime - toronto.DateTime);
    }

    [Fact]
    public void Caption_says_added_and_last_modified()
    {
        var added = Afternoon;
        var modified = Utc(2026, 10, 20, 8, 55);

        Assert.Equal("Added 10/15/2026, 1:40 PM", NotePage.TimesCaption(added, added + 30_000, TimeZoneInfo.Utc, H12));
        Assert.Equal("Added 10/15/2026, 1:40 PM · Last modified 10/20/2026, 8:55 AM", NotePage.TimesCaption(added, modified, TimeZoneInfo.Utc, H12));
        Assert.Equal("Last modified 10/20/2026, 8:55 AM", NotePage.TimesCaption(null, modified, TimeZoneInfo.Utc, H12));
        Assert.Equal("", NotePage.TimesCaption(null, null, TimeZoneInfo.Utc, H12));
    }

    [Fact]
    public void Caption_shows_added_even_at_the_notes_own_day_and_time()
    {
        var note = new Note { CreateDate = Afternoon, CreateDateTime = Afternoon };

        Assert.Equal("Thursday, October 15, 2026, 1:40 PM", NotePage.DayAndTimeLine(note, H12, TimeZoneInfo.Utc));
        Assert.Equal("Added 10/15/2026, 1:40 PM", NotePage.TimesCaption(note.CreateDateTime, null, TimeZoneInfo.Utc, H12));
    }

    [Fact]
    public void Caption_no_longer_says_created_or_changed()
    {
        foreach (var culture in new[] { H12, H24 })
        {
            foreach (var text in new[]
            {
                NotePage.TimesCaption(Afternoon, Utc(2026, 10, 20, 8, 55), TimeZoneInfo.Utc, culture),
                NotePage.TimesCaption(Afternoon, null, TimeZoneInfo.Utc, culture),
                NotePage.TimesCaption(null, Afternoon, TimeZoneInfo.Utc, culture),
            })
            {
                Assert.DoesNotContain("reated", text);
                Assert.DoesNotContain("hanged", text);
            }
        }
    }

    [Fact]
    public void Month_and_day_and_year_and_month_follow_the_cultures_order()
    {
        var day = new DateOnly(2026, 5, 26);
        var history = new HistoryNote { Id = "n", CreateDateTime = LocalNoteTimes.At(day, 9 * 60) };

        Assert.Equal("May 26", ContactHistory.DayText(history, H12));
        Assert.Equal("26 May", ContactHistory.DayText(history, H24));
        Assert.Equal("May 2026", ContactHistory.MonthText(day, H12));
        Assert.Equal("2026 May", ContactHistory.MonthText(day, H24));
        Assert.Equal("Tue May 26", DateText.WeekdayMonthDay(day, H12));
        Assert.Equal("Tue 26 May", DateText.WeekdayMonthDay(day, H24));
        Assert.Equal("5/26/2026", DateText.ShortDate(day, H12));
        Assert.Equal("26/05/2026", DateText.ShortDate(day, H24));
    }

    [Theory]
    [InlineData("dddd, MMMM d, yyyy", "MMMM d, yyyy")]
    [InlineData("dddd d MMMM yyyy", "d MMMM yyyy")]
    [InlineData("dddd, d. MMMM yyyy", "d. MMMM yyyy")]
    [InlineData("yyyy'年'M'月'd'日'dddd", "yyyy'年'M'月'd'日'")]
    [InlineData("d MMMM yyyy", "d MMMM yyyy")]
    [InlineData("d dddd MMMM yyyy", "d dddd MMMM yyyy")]
    public void Long_date_without_weekday_takes_out_a_leading_or_trailing_weekday(string longDate, string expected)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.LongDatePattern = longDate;

        Assert.Equal(expected, DateText.LongDatePatternWithoutWeekday(culture));
    }

    [Fact]
    public void Format_key_changes_with_the_regional_format()
    {
        Assert.NotEqual(DateText.FormatKey(H12), DateText.FormatKey(H24));
        Assert.Equal(DateText.FormatKey(H12), DateText.FormatKey((CultureInfo)H12.Clone()));

        var firstDay = (CultureInfo)H24.Clone();
        firstDay.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
        Assert.NotEqual(DateText.FormatKey(H24), DateText.FormatKey(firstDay));
    }
}
