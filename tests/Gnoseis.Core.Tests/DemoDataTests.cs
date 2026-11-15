/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.DemoData;

namespace Gnoseis.Core.Tests;

/// <summary>The demo database generator writes text the way the apps store it.</summary>
public class DemoDataTests
{
    [Fact]
    public void Generated_text_has_line_feeds_only()
    {
        var data = new DemoDataGenerator(300, 42, new DateOnly(2026, 11, 14)).Generate();

        var texts = data.Notes.Select(n => n.TextContents)
            .Concat(data.Contacts.Select(c => c.Comments))
            .Concat(data.Organizations.Select(o => o.Comments))
            .Concat(data.Categories.Select(c => c.Comments))
            .Concat(data.Items.Select(i => i.Comments))
            .OfType<string>()
            .ToList();

        Assert.Contains(texts, t => t.Contains('\n'));
        Assert.DoesNotContain(texts, t => t.Contains('\r'));
    }

    [Fact]
    public void Generated_notes_are_dated_by_the_rule_in_Toronto()
    {
        var toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        var data = new DemoDataGenerator(300, 42, new DateOnly(2026, 11, 14)).Generate();

        Assert.NotEmpty(data.Notes);
        foreach (var note in data.Notes)
        {
            var local = Domain.NoteDates.Shown(note.CreateDateTime, toronto);
            Assert.Equal(Domain.NoteDates.StartOfDay(DateOnly.FromDateTime(local), toronto), note.CreateDate);
            Assert.Equal(0, local.Second);
        }
    }
}
