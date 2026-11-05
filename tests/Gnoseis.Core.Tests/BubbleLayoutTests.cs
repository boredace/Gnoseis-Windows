/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.Core.Tests;

public class BubbleLayoutTests
{
    private const double Width = 680;
    private const double Height = 440;

    private static void AssertInsideAndApart(IReadOnlyList<PlacedBubble> bubbles, double width = Width, double height = Height)
    {
        foreach (var b in bubbles)
        {
            Assert.True(b.X - b.Diameter / 2 >= -0.01 && b.X + b.Diameter / 2 <= width + 0.01, $"{b} is outside horizontally");
            Assert.True(b.Y - b.Diameter / 2 >= -0.01 && b.Y + b.Diameter / 2 <= height + 0.01, $"{b} is outside vertically");
        }
        for (var i = 0; i < bubbles.Count; i++)
        {
            for (var j = i + 1; j < bubbles.Count; j++)
            {
                var (a, b) = (bubbles[i], bubbles[j]);
                var distance = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
                Assert.True(distance >= (a.Diameter + b.Diameter) / 2 - 0.01, $"{a} and {b} overlap");
            }
        }
    }

    private static void AssertCentred(IReadOnlyList<PlacedBubble> bubbles)
    {
        var left = bubbles.Min(b => b.X - b.Diameter / 2);
        var right = bubbles.Max(b => b.X + b.Diameter / 2);
        var top = bubbles.Min(b => b.Y - b.Diameter / 2);
        var bottom = bubbles.Max(b => b.Y + b.Diameter / 2);
        Assert.Equal(Width / 2, (left + right) / 2, 3);
        Assert.Equal(Height / 2, (top + bottom) / 2, 3);
    }

    [Fact]
    public void No_bubbles_give_no_layout() =>
        Assert.Empty(BubbleLayout.Layout([], Width, Height));

    [Fact]
    public void One_bubble_is_centred_at_62_percent_of_the_shorter_side()
    {
        var bubble = Assert.Single(BubbleLayout.Layout([7], Width, Height));

        Assert.Equal(new PlacedBubble(Width / 2, Height / 2, 0.62 * Height), bubble, (a, b) =>
            Math.Abs(a.X - b.X) < 0.001 && Math.Abs(a.Y - b.Y) < 0.001 && Math.Abs(a.Diameter - b.Diameter) < 0.001);
    }

    [Fact]
    public void Two_bubbles_start_at_52_percent_and_size_by_area()
    {
        var diameters = BubbleLayout.DiametersFor([16, 4], Width, Height);
        Assert.Equal(0.52 * Height, diameters[0], 6);
        Assert.Equal(0.52 * Height * 0.5, diameters[1], 6);

        var bubbles = BubbleLayout.Layout([16, 4], Width, Height);
        Assert.Equal(2, bubbles.Count);
        Assert.True(bubbles[0].Diameter > bubbles[1].Diameter);
        AssertInsideAndApart(bubbles);
        AssertCentred(bubbles);
    }

    [Fact]
    public void Three_bubbles_start_at_48_percent_with_a_36_percent_floor()
    {
        var diameters = BubbleLayout.DiametersFor([100, 25, 1], Width, Height);

        Assert.Equal(0.48 * Height, diameters[0], 6);
        Assert.Equal(0.48 * Height * 0.5, diameters[1], 6);
        Assert.Equal(0.48 * Height * 0.36, diameters[2], 6);

        var bubbles = BubbleLayout.Layout([100, 25, 1], Width, Height);
        AssertInsideAndApart(bubbles);
        AssertCentred(bubbles);
    }

    [Fact]
    public void Ten_bubbles_fit_without_overlapping_and_the_largest_is_near_the_middle()
    {
        int[] counts = [24, 19, 15, 11, 10, 9, 8, 7, 6, 5];

        var bubbles = BubbleLayout.Layout(counts, Width, Height);

        Assert.Equal(10, bubbles.Count);
        AssertInsideAndApart(bubbles);
        AssertCentred(bubbles);
        var largest = bubbles[0];
        Assert.Equal(bubbles.Max(b => b.Diameter), largest.Diameter);
        Assert.True(Math.Abs(largest.X - Width / 2) < Width / 4 && Math.Abs(largest.Y - Height / 2) < Height / 4);
    }

    [Fact]
    public void Ten_bubbles_shrink_to_fit_a_small_area()
    {
        int[] counts = [10, 10, 10, 10, 10, 10, 10, 10, 10, 10];

        var bubbles = BubbleLayout.Layout(counts, 300, 200);

        AssertInsideAndApart(bubbles, 300, 200);
        Assert.True(bubbles[0].Diameter < 0.48 * 200);
    }

    [Fact]
    public void The_same_counts_and_area_give_the_same_layout()
    {
        int[] counts = [24, 19, 15, 11, 10, 9, 8, 7, 6, 5];

        Assert.Equal(BubbleLayout.Layout(counts, Width, Height), BubbleLayout.Layout(counts, Width, Height));
        Assert.Equal(BubbleLayout.Layout([3, 3, 3], Width, Height), BubbleLayout.Layout([3, 3, 3], Width, Height));
    }
}
