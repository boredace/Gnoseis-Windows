/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>
/// The record type colours stay apart and readable, with the same formulas as Gnoseis for Android's
/// RecordTypeColorsTest, so both give the same numbers.
/// </summary>
public class RecordTypeColorsTests
{
    private static readonly RecordType[] Types = [.. RecordTypeExtensions.UserTypes];

    /// <summary>Each role: light outline, light fill, dark outline, dark fill.</summary>
    public static TheoryData<ColorTheme, ColorRole> Roles => new()
    {
        { ColorTheme.Light, ColorRole.Outline },
        { ColorTheme.Light, ColorRole.Fill },
        { ColorTheme.Dark, ColorRole.Outline },
        { ColorTheme.Dark, ColorRole.Fill },
    };

    [Fact]
    public void EveryOutline_HasEnoughContrastOnItsFill()
    {
        foreach (var theme in Enum.GetValues<ColorTheme>())
        {
            foreach (var type in Types)
            {
                var ratio = Contrast(
                    RecordTypeColors.Get(type, theme, ColorRole.Outline),
                    RecordTypeColors.Get(type, theme, ColorRole.Fill));
                Assert.True(ratio >= 5.0, $"{type} {theme}: {ratio:F2}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void EveryTwoTypes_StayApart(ColorTheme theme, ColorRole role)
    {
        foreach (var (a, b, difference) in Differences(theme, role, nightLight: false))
        {
            Assert.True(difference >= 15.0, $"{a}/{b} {theme} {role}: {difference:F1}");
        }
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void EveryTwoTypes_StayApartWithNightLight(ColorTheme theme, ColorRole role)
    {
        foreach (var (a, b, difference) in Differences(theme, role, nightLight: true))
        {
            Assert.True(difference >= 10.0, $"{a}/{b} {theme} {role}: {difference:F1}");
        }
    }

    [Fact]
    public void WeakestPairs_MatchAndroid()
    {
        var normal = AllDifferences(nightLight: false).MinBy(d => d.Difference);
        var night = AllDifferences(nightLight: true).MinBy(d => d.Difference);

        Assert.Equal("Note/Contact light Fill 17.5", $"{normal.A}/{normal.B} {normal.Name} {normal.Difference:F1}");
        Assert.Equal("Contact/Organization light Fill 10.8", $"{night.A}/{night.B} {night.Name} {night.Difference:F1}");
    }

    [Fact]
    public void Ciede2000_MatchesReferenceValues()
    {
        // Pairs 1 and 7 from Sharma, Wu and Dalal's CIEDE2000 test data.
        Assert.Equal(2.0425, Ciede2000([50.0, 2.6772, -79.7751], [50.0, 0.0, -82.7485]), 4);
        Assert.Equal(2.3669, Ciede2000([50.0, 0.0, 0.0], [50.0, -1.0, 2.0]), 4);
    }

    [Fact]
    public void TypesWithoutColour_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecordTypeColors.Get(RecordType.LinkedRecord, ColorTheme.Light, ColorRole.Fill));
    }

    // ---------------------------------------------------------------- Helpers

    private static IEnumerable<(RecordType A, RecordType B, string Name, double Difference)> AllDifferences(bool nightLight) =>
        from theme in Enum.GetValues<ColorTheme>()
        from role in Enum.GetValues<ColorRole>()
        from d in Differences(theme, role, nightLight)
        select (d.A, d.B, $"{theme.ToString().ToLowerInvariant()} {role}", d.Difference);

    private static IEnumerable<(RecordType A, RecordType B, double Difference)> Differences(ColorTheme theme, ColorRole role, bool nightLight)
    {
        for (var i = 0; i < Types.Length; i++)
        {
            for (var j = i + 1; j < Types.Length; j++)
            {
                var a = Srgb(RecordTypeColors.Get(Types[i], theme, role));
                var b = Srgb(RecordTypeColors.Get(Types[j], theme, role));
                if (nightLight)
                {
                    a = WithNightLight(a);
                    b = WithNightLight(b);
                }
                yield return (Types[i], Types[j], Ciede2000(Lab(a), Lab(b)));
            }
        }
    }

    private static double[] Srgb(Rgb color) => [color.R / 255.0, color.G / 255.0, color.B / 255.0];

    private static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double ToSrgb(double c) => c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

    /// <summary>Relative luminance (WCAG).</summary>
    private static double Luminance(Rgb color)
    {
        var rgb = Srgb(color);
        return 0.2126 * ToLinear(rgb[0]) + 0.7152 * ToLinear(rgb[1]) + 0.0722 * ToLinear(rgb[2]);
    }

    /// <summary>WCAG contrast ratio between two colours.</summary>
    private static double Contrast(Rgb a, Rgb b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Night light, simulated: the blue channel cut to 40% in linear light.</summary>
    private static double[] WithNightLight(double[] rgb) => [rgb[0], rgb[1], ToSrgb(ToLinear(rgb[2]) * 0.4)];

    /// <summary>sRGB to CIELAB, D65 white.</summary>
    private static double[] Lab(double[] rgb)
    {
        var r = ToLinear(rgb[0]);
        var g = ToLinear(rgb[1]);
        var b = ToLinear(rgb[2]);
        var x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
        return [116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z))];
    }

    /// <summary>CIEDE2000 colour difference between two CIELAB colours.</summary>
    private static double Ciede2000(double[] lab1, double[] lab2)
    {
        static double Rad(double deg) => deg * Math.PI / 180;
        static double Deg(double rad) => rad * 180 / Math.PI;
        static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
        var (l1, a1, b1) = (lab1[0], lab1[1], lab1[2]);
        var (l2, a2, b2) = (lab2[0], lab2[1], lab2[2]);
        var cMean = (Hypot(a1, b1) + Hypot(a2, b2)) / 2;
        var g = 0.5 * (1 - Math.Sqrt(Math.Pow(cMean, 7) / (Math.Pow(cMean, 7) + Math.Pow(25.0, 7))));
        var a1p = (1 + g) * a1;
        var a2p = (1 + g) * a2;
        var c1p = Hypot(a1p, b1);
        var c2p = Hypot(a2p, b2);
        var h1p = c1p == 0.0 ? 0.0 : (Deg(Math.Atan2(b1, a1p)) + 360) % 360;
        var h2p = c2p == 0.0 ? 0.0 : (Deg(Math.Atan2(b2, a2p)) + 360) % 360;

        var dLp = l2 - l1;
        var dCp = c2p - c1p;
        var dhp = h2p - h1p;
        if (c1p * c2p == 0.0) dhp = 0.0;
        else if (dhp > 180) dhp -= 360;
        else if (dhp < -180) dhp += 360;
        var dHp = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Rad(dhp / 2));

        var lpMean = (l1 + l2) / 2;
        var cpMean = (c1p + c2p) / 2;
        var hpMean =
            c1p * c2p == 0.0 ? h1p + h2p
            : Math.Abs(h1p - h2p) <= 180 ? (h1p + h2p) / 2
            : h1p + h2p < 360 ? (h1p + h2p + 360) / 2
            : (h1p + h2p - 360) / 2;
        var t = 1 - 0.17 * Math.Cos(Rad(hpMean - 30)) + 0.24 * Math.Cos(Rad(2 * hpMean)) +
            0.32 * Math.Cos(Rad(3 * hpMean + 6)) - 0.20 * Math.Cos(Rad(4 * hpMean - 63));
        var sl = 1 + 0.015 * Math.Pow(lpMean - 50, 2) / Math.Sqrt(20 + Math.Pow(lpMean - 50, 2));
        var sc = 1 + 0.045 * cpMean;
        var sh = 1 + 0.015 * cpMean * t;
        var rt = -2 * Math.Sqrt(Math.Pow(cpMean, 7) / (Math.Pow(cpMean, 7) + Math.Pow(25.0, 7))) *
            Math.Sin(Rad(60 * Math.Exp(-Math.Pow((hpMean - 275) / 25, 2))));
        return Math.Sqrt(
            Math.Pow(dLp / sl, 2) + Math.Pow(dCp / sc, 2) + Math.Pow(dHp / sh, 2) + rt * (dCp / sc) * (dHp / sh));
    }
}
