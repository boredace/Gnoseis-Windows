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
}
