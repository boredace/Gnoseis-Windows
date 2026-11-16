/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;

namespace Gnoseis.Core.Tests;

public class DataLocationTests
{
    private const string ExeFolder = @"C:\Apps\Gnoseis";
    private const string DefaultFolder = @"C:\Users\me\AppData\Local\Gnoseis";

    private static readonly KnownDatabases NoFile = KnownDatabases.Create(DefaultFolder);

    private static DataLocation? Resolve(string? argument = null, string? variable = null, bool marker = false, KnownDatabases? list = null) =>
        DataLocation.Resolve(argument, variable, ExeFolder, marker, () => list ?? NoFile);

    private static KnownDatabases ListWithDefaultEntry(bool ask = false)
    {
        var list = KnownDatabases.Create(DefaultFolder);
        Assert.Null(list.Add("Work", @"E:\Work"));
        list.SetDefault(@"E:\Work");
        list.AskAtStartup = ask;
        return list;
    }

    // ---------------------------------------------------------------- Order

    [Fact]
    public void Without_argument_variable_marker_or_list_the_default_folder_is_used()
    {
        var location = Resolve()!;

        Assert.Equal(DefaultFolder, location.Folder);
        Assert.False(location.IsPortable);
        Assert.Equal(DataFolderSource.DefaultFolder, location.Source);
    }

    [Fact]
    public void Argument_wins_over_everything_and_is_not_portable()
    {
        var location = Resolve(@"D:\A", @"D:\Variable", marker: true, ListWithDefaultEntry(ask: true))!;

        Assert.Equal(@"D:\A", location.Folder);
        Assert.False(location.IsPortable);
        Assert.Equal(DataFolderSource.CommandLine, location.Source);
    }

    [Fact]
    public void Variable_wins_over_the_marker_and_the_list_and_is_not_portable()
    {
        var location = Resolve(variable: @"D:\Elsewhere", marker: true, list: ListWithDefaultEntry(ask: true))!;

        Assert.Equal(@"D:\Elsewhere", location.Folder);
        Assert.False(location.IsPortable);
        Assert.Equal(DataFolderSource.EnvironmentVariable, location.Source);
    }

    [Fact]
    public void Marker_wins_over_the_list_and_never_reads_it()
    {
        var location = DataLocation.Resolve(null, null, ExeFolder, portableMarkerExists: true,
            () => throw new InvalidOperationException("A portable copy doesn't read the list."))!;

        Assert.Equal(@"C:\Apps\Gnoseis\data", location.Folder);
        Assert.True(location.IsPortable);
        Assert.Equal(DataFolderSource.Portable, location.Source);
    }

    [Fact]
    public void Always_ask_wins_over_the_default_entry()
    {
        Assert.Null(Resolve(list: ListWithDefaultEntry(ask: true)));
    }

    [Fact]
    public void Default_entry_wins_over_the_default_folder()
    {
        var location = Resolve(list: ListWithDefaultEntry())!;

        Assert.Equal(@"E:\Work", location.Folder);
        Assert.Equal(DataFolderSource.DefaultEntry, location.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_argument_or_variable_does_not_hide_the_marker(string? blank)
    {
        Assert.True(Resolve(blank, blank, marker: true)!.IsPortable);
    }

    [Fact]
    public void Only_a_folder_from_the_argument_picker_or_default_entry_asks_before_creating()
    {
        Assert.True(new DataLocation(@"D:\A", false, DataFolderSource.CommandLine).AsksBeforeCreating(DefaultFolder));
        Assert.True(new DataLocation(@"D:\A", false, DataFolderSource.Picker).AsksBeforeCreating(DefaultFolder));
        Assert.True(new DataLocation(@"D:\A", false, DataFolderSource.DefaultEntry).AsksBeforeCreating(DefaultFolder));
        Assert.False(new DataLocation(@"D:\A", false, DataFolderSource.EnvironmentVariable).AsksBeforeCreating(DefaultFolder));
        Assert.False(new DataLocation(@"D:\A", true, DataFolderSource.Portable).AsksBeforeCreating(DefaultFolder));
        Assert.False(new DataLocation(DefaultFolder, false, DataFolderSource.DefaultFolder).AsksBeforeCreating(DefaultFolder));
        // The default folder chosen in the picker is still the default folder: first run, as today.
        Assert.False(new DataLocation(DefaultFolder + @"\", false, DataFolderSource.Picker).AsksBeforeCreating(DefaultFolder));
    }

    // ---------------------------------------------------------------- Argument

    private const string WorkingFolder = @"C:\Shortcuts\Start";

    [Theory]
    [InlineData("--data-folder", @"D:\A")]
    [InlineData("--DATA-FOLDER", @"D:\A")]
    [InlineData("--Data-Folder", @"D:\A")]
    public void Argument_takes_the_next_value(string option, string value)
    {
        var argument = DataLocation.ParseArguments([option, value], WorkingFolder);

        Assert.Equal(@"D:\A", argument.Folder);
        Assert.False(argument.IsError);
    }

    [Theory]
    [InlineData(@"--data-folder=D:\A")]
    [InlineData(@"--DATA-FOLDER=D:\A")]
    public void Argument_takes_a_value_after_an_equals_sign(string arg)
    {
        Assert.Equal(@"D:\A", DataLocation.ParseArguments([arg], WorkingFolder).Folder);
    }

    [Fact]
    public void Argument_folder_is_not_portable()
    {
        var argument = DataLocation.ParseArguments(["--data-folder", @"D:\A"], WorkingFolder);

        Assert.False(Resolve(argument.Folder, marker: true)!.IsPortable);
    }

    [Theory]
    [InlineData("Data", @"C:\Shortcuts\Start\Data")]
    [InlineData(@"..\Data", @"C:\Shortcuts\Data")]
    [InlineData(".", @"C:\Shortcuts\Start")]
    public void A_relative_folder_resolves_against_the_working_folder(string value, string expected)
    {
        Assert.Equal(expected, DataLocation.ParseArguments(["--data-folder", value], WorkingFolder).Folder);
    }

    [Theory]
    [InlineData(@"D:\A""", @"D:\A")]
    [InlineData(@"D:\Gnoseis data""", @"D:\Gnoseis data")]
    public void A_trailing_quote_is_removed(string value, string expected)
    {
        Assert.Equal(expected, DataLocation.ParseArguments(["--data-folder", value], WorkingFolder).Folder);
        Assert.Equal(expected, DataLocation.ParseArguments(["--data-folder=" + value], WorkingFolder).Folder);
    }

    [Fact]
    public void No_arguments_give_no_folder_and_no_error()
    {
        var argument = DataLocation.ParseArguments([], WorkingFolder);

        Assert.Null(argument.Folder);
        Assert.False(argument.IsError);
    }

    public static TheoryData<string[]> BadArguments => new()
    {
        new[] { "--data-foler", @"D:\A" },
        new[] { @"D:\A" },
        new[] { "--data-folder", @"D:\A", "extra" },
        new[] { "--data-folder" },
        new[] { "--data-folder", "" },
        new[] { "--data-folder", "   " },
        new[] { "--data-folder", "\"" },
        new[] { "--data-folder=" },
        new[] { "--data-folder=  " },
        new[] { "--data-folder", @"D:\A", "--data-folder", @"D:\B" },
        new[] { "--data-folder", @"D:\A", @"--data-folder=D:\A" },
        new[] { @"--data-folder=D:\A", "--data-folder" },
    };

    [Theory]
    [MemberData(nameof(BadArguments))]
    public void Anything_else_is_an_error_and_gives_no_folder(string[] args)
    {
        var argument = DataLocation.ParseArguments(args, WorkingFolder);

        Assert.True(argument.IsError);
        Assert.Null(argument.Folder);
    }

    [Fact]
    public void CanWrite_creates_the_folder_and_leaves_nothing_in_it()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"gnoseis-location-{Guid.NewGuid():N}", "data");
        try
        {
            Assert.True(DataLocation.CanWrite(folder, out var error));
            Assert.Null(error);
            Assert.Empty(Directory.GetFileSystemEntries(folder));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    [Fact]
    public void CanWrite_reports_a_folder_that_cannot_be_created()
    {
        // A file where the folder should be, as an unwritable location that needs no special rights.
        var file = Path.GetTempFileName();
        try
        {
            Assert.False(DataLocation.CanWrite(Path.Combine(file, "data"), out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
