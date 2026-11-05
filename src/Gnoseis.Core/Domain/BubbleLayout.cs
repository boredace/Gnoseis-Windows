/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Domain;

/// <summary>A bubble's centre and diameter, in the units of the area it was laid out in.</summary>
public readonly record struct PlacedBubble(double X, double Y, double Diameter);

/// <summary>
/// Places the Most linked bubbles, as the Android app does. Sizes are relative: the most linked
/// record gets the largest bubble, bigger when there are fewer bubbles, and each other bubble's
/// area is its share of the top link count. The largest goes in the middle, and each next one at
/// the free spot nearest the middle, touching one already placed. Then the whole group is moved so
/// it's centred in the area. The same counts and area always give the same layout. If they don't
/// fit, every bubble shrinks until they do.
/// </summary>
public static class BubbleLayout
{
    // Largest bubble's diameter as a share of the shorter side, for 1, 2 and more bubbles
    public const double OneShare = 0.62;
    public const double TwoShare = 0.52;
    public const double ManyShare = 0.48;

    /// <summary>Smallest bubble's diameter as a share of the largest, so names stay readable.</summary>
    public const double MinOfLargest = 0.36;

    /// <summary>Space between neighbouring bubbles.</summary>
    public const double Gap = 4;

    private const double Fill = 0.5;
    private const double Shrink = 0.92;
    private const int MaxAttempts = 30;
    private const int Angles = 120;
    private const double Epsilon = 0.01;

    /// <summary>Diameters for <paramref name="linkCounts"/>, in the same order, before fitting.</summary>
    public static IReadOnlyList<double> DiametersFor(IReadOnlyList<int> linkCounts, double width, double height)
    {
        var share = linkCounts.Count switch
        {
            1 => OneShare,
            2 => TwoShare,
            _ => ManyShare,
        };
        var largest = share * Math.Min(width, height);
        var top = Math.Max(1, linkCounts.Count == 0 ? 0 : linkCounts.Max());
        return linkCounts.Select(c => Math.Max(MinOfLargest * largest, largest * Math.Sqrt((double)Math.Max(0, c) / top))).ToList();
    }

    /// <summary>One bubble per entry of <paramref name="linkCounts"/>, in the same order.</summary>
    public static IReadOnlyList<PlacedBubble> Layout(IReadOnlyList<int> linkCounts, double width, double height)
    {
        if (linkCounts.Count == 0 || width <= 0 || height <= 0)
        {
            return [];
        }
        var wanted = DiametersFor(linkCounts, width, height);
        var area = wanted.Sum(d => Math.PI * d * d / 4);
        var scale = Math.Min(1, Math.Sqrt(Fill * width * height / area));
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (Pack(wanted.Select(d => d * scale).ToList(), width, height) is { } packed)
            {
                return Centred(packed, width, height);
            }
            scale *= Shrink;
        }
        // Not reached for 10 bubbles in a usable area; overlap rather than leave one out.
        return wanted.Select(d => new PlacedBubble(width / 2, height / 2, d * scale)).ToList();
    }

    /// <summary>Moves the bubbles so their bounds are centred. They already fit, so they still do.</summary>
    private static List<PlacedBubble> Centred(List<PlacedBubble> bubbles, double width, double height)
    {
        var left = bubbles.Min(b => b.X - b.Diameter / 2);
        var right = bubbles.Max(b => b.X + b.Diameter / 2);
        var top = bubbles.Min(b => b.Y - b.Diameter / 2);
        var bottom = bubbles.Max(b => b.Y + b.Diameter / 2);
        var dx = width / 2 - (left + right) / 2;
        var dy = height / 2 - (top + bottom) / 2;
        return bubbles.Select(b => b with { X = b.X + dx, Y = b.Y + dy }).ToList();
    }

    private static List<PlacedBubble>? Pack(List<double> diameters, double width, double height)
    {
        var cx = width / 2;
        var cy = height / 2;
        // Largest first; OrderByDescending is stable, so ties keep their order.
        var order = Enumerable.Range(0, diameters.Count).OrderByDescending(i => diameters[i]).ToList();
        var placed = new PlacedBubble[diameters.Count];
        var done = new List<PlacedBubble>();

        bool Fits(double x, double y, double r) =>
            x - r >= 0 && y - r >= 0 && x + r <= width && y + r <= height &&
            done.All(o => Math.Sqrt((o.X - x) * (o.X - x) + (o.Y - y) * (o.Y - y)) >= o.Diameter / 2 + r + Gap - Epsilon);

        foreach (var i in order)
        {
            var r = diameters[i] / 2;
            PlacedBubble? spot = null;
            if (done.Count == 0)
            {
                if (Fits(cx, cy, r))
                {
                    spot = new PlacedBubble(cx, cy, diameters[i]);
                }
            }
            else
            {
                var best = double.MaxValue;
                foreach (var other in done)
                {
                    var distance = other.Diameter / 2 + r + Gap;
                    for (var step = 0; step < Angles; step++)
                    {
                        var angle = 2 * Math.PI * step / Angles;
                        var x = other.X + distance * Math.Cos(angle);
                        var y = other.Y + distance * Math.Sin(angle);
                        var fromCentre = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        // Strictly nearer only, so the first of equally near spots wins (as minBy).
                        if (fromCentre < best && Fits(x, y, r))
                        {
                            best = fromCentre;
                            spot = new PlacedBubble(x, y, diameters[i]);
                        }
                    }
                }
            }
            if (spot is not { } found)
            {
                return null;
            }
            placed[i] = found;
            done.Add(found);
        }
        return [.. placed];
    }
}
