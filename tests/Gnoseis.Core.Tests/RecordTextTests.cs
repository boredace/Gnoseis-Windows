/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>Line breaks in the multi-line text fields: stored as LF, CRLF, CR and LF each read as one break.</summary>
public class RecordTextTests
{
    [Theory]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\r\rb", "a\n\nb")]
    [InlineData("a\r\n\r\nb", "a\n\nb")]
    [InlineData("a\r\nb\rc\nd", "a\nb\nc\nd")]
    [InlineData("a\r", "a\n")]
    [InlineData("\ra", "\na")]
    [InlineData("a\n\rb", "a\n\nb")]
    public void Each_line_break_becomes_one_line_feed(string text, string expected)
    {
        Assert.Equal(expected, RecordText.NormalizeLineBreaks(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("No line break")]
    [InlineData("  Spaces\tand tabs kept  ")]
    [InlineData("Unicode\u2028line\u2029separators kept\u0085")]
    public void Text_without_a_carriage_return_is_unchanged(string text)
    {
        Assert.Same(text, RecordText.NormalizeLineBreaks(text));
    }

    [Theory]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\r\nb", "a\rb")]
    [InlineData(null, "")]
    [InlineData("", null)]
    public void Texts_that_differ_only_in_line_breaks_are_the_same(string? a, string? b)
    {
        Assert.True(RecordText.SameText(a, b));
    }

    [Theory]
    [InlineData("a\rb", "a b")]
    [InlineData("a\r\rb", "a\nb")]
    [InlineData("a", "a ")]
    public void Other_differences_count(string a, string b)
    {
        Assert.False(RecordText.SameText(a, b));
    }

    [Theory]
    [InlineData("a\rb")]
    [InlineData("a\r\nb")]
    [InlineData("a\nb")]
    public void Unchanged_text_keeps_the_stored_string(string stored)
    {
        foreach (var edited in new[] { "a\rb", "a\r\nb", "a\nb" })
        {
            Assert.Same(stored, RecordText.Edited(stored, edited));
            Assert.Same(stored, RecordText.EditedOrNull(stored, edited));
        }
    }

    [Theory]
    [InlineData("a\rb", "a\rb\rc")]
    [InlineData("a\nb", "a\r\nc")]
    [InlineData(null, "a\rb")]
    public void Changed_text_is_saved_with_line_feeds(string? stored, string edited)
    {
        var expected = RecordText.NormalizeLineBreaks(edited);

        Assert.DoesNotContain('\r', expected);
        Assert.Equal(expected, RecordText.Edited(stored, edited));
        Assert.Equal(expected, RecordText.EditedOrNull(stored, edited));
    }

    [Fact]
    public void An_empty_box_keeps_a_missing_value_missing()
    {
        Assert.Null(RecordText.Edited(null, ""));
        Assert.Null(RecordText.EditedOrNull(null, ""));
        Assert.Null(RecordText.EditedOrNull("a\nb", " \r "));
    }
}
