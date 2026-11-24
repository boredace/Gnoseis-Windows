/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Text.RegularExpressions;

namespace Gnoseis.Core.Tests;

/// <summary>
/// No shown date or time uses a fixed pattern: a check across src/ (the app and Core), outside the
/// file names, and no time picker fixed to the 24-hour clock.
/// </summary>
public sealed class NoFixedDatePatternsTests
{
    // Fixed day, month and time patterns that shown text must not use.
    private static readonly Regex FixedPattern = new(@"HH:mm|\bd MMM|MMM yyyy|yyyy-MM-dd|ddd d MMM");

    // Allowed: file names (gnoseis_data_…, before-restore-…, unusable-…, before-v4-…), never shown.
    private static readonly Regex FileNamePattern = new(@"yyyy-MM-dd_HH-mm-ss");

    [Fact]
    public void No_fixed_date_or_time_pattern_is_left_in_the_source()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var found = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(d => d is "obj" or "bin"))
            .SelectMany(p => File.ReadLines(p).Select((line, i) => (Path: Path.GetRelativePath(src, p), Line: i + 1, Text: line)))
            .Where(l => FixedPattern.IsMatch(FileNamePattern.Replace(l.Text, "")) || l.Text.Contains("ClockIdentifier=\"24HourClock\"", StringComparison.Ordinal))
            .Select(l => $"{l.Path}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.Empty(found);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gnoseis.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("Gnoseis.sln not found above " + AppContext.BaseDirectory);
    }
}
