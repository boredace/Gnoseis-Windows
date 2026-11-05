/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Xml.Linq;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>The app's theme resources (Resources/RecordTypeColors.xaml) hold the same colours as the Core table.</summary>
public class RecordTypeColorResourcesTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly Dictionary<string, XElement> Themes =
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources", "RecordTypeColors.xaml"))
            .Root!
            .Element(Ui + "ResourceDictionary.ThemeDictionaries")!
            .Elements(Ui + "ResourceDictionary")
            .ToDictionary(d => (string)d.Attribute(X + "Key")!);

    private static IEnumerable<(RecordType Type, ColorRole Role)> Keys =>
        from type in RecordTypeExtensions.UserTypes
        from role in Enum.GetValues<ColorRole>()
        select (type, role);

    [Fact]
    public void ThereAreLightDarkAndHighContrastThemes()
    {
        Assert.Equal(["Dark", "HighContrast", "Light"], Themes.Keys.Order());
    }

    [Theory]
    [InlineData(ColorTheme.Light)]
    [InlineData(ColorTheme.Dark)]
    public void Colors_MatchTheCoreTable(ColorTheme theme)
    {
        var resources = Themes[theme.ToString()];
        foreach (var (type, role) in Keys)
        {
            var color = Resource(resources, $"{type}{role}Color");
            Assert.Equal("Color", color.Name.LocalName);
            Assert.Equal($"#FF{RecordTypeColors.Get(type, theme, role).ToString()[1..]}", color.Value.Trim());
        }
    }

    [Theory]
    [InlineData(ColorTheme.Light)]
    [InlineData(ColorTheme.Dark)]
    public void Brushes_UseTheirColor(ColorTheme theme)
    {
        var resources = Themes[theme.ToString()];
        foreach (var (type, role) in Keys)
        {
            var brush = Resource(resources, $"{type}{role}Brush");
            Assert.Equal("SolidColorBrush", brush.Name.LocalName);
            Assert.Equal($"{{StaticResource {type}{role}Color}}", (string?)brush.Attribute("Color"));
        }
    }

    [Fact]
    public void HighContrast_HasEveryKey_InSystemColors()
    {
        var resources = Themes["HighContrast"];
        foreach (var (type, role) in Keys)
        {
            var system = role == ColorRole.Outline ? "SystemColorWindowTextColor" : "SystemColorWindowColor";
            Assert.Equal(system, (string?)Resource(resources, $"{type}{role}Color").Attribute("ResourceKey"));
            Assert.Equal($"{{ThemeResource {system}}}", (string?)Resource(resources, $"{type}{role}Brush").Attribute("Color"));
        }
    }

    [Fact]
    public void EveryTheme_HasTheSameKeys()
    {
        var keys = Themes.Values.Select(d => d.Elements().Select(e => (string)e.Attribute(X + "Key")!).Order().ToList()).ToList();
        Assert.All(keys, k => Assert.Equal(keys[0], k));
        Assert.Equal(RecordTypeExtensions.UserTypes.Count * 4, keys[0].Count);
    }

    private static XElement Resource(XElement dictionary, string key) =>
        Assert.Single(dictionary.Elements(), e => (string?)e.Attribute(X + "Key") == key);
}
