/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;

namespace Gnoseis.Core.Tests;

public class KnownDatabasesTests
{
    private const string DefaultFolder = @"C:\Users\me\AppData\Local\Gnoseis";

    private static void AssertJustTheDefaultFolder(KnownDatabases list)
    {
        var entry = Assert.Single(list.Entries);
        Assert.Equal(new KnownDatabase(KnownDatabases.DefaultFolderName, DefaultFolder), entry);
        Assert.Equal(entry, list.Default);
        Assert.False(list.AskAtStartup);
    }

    [Fact]
    public void A_missing_file_gives_just_the_default_folder_as_default()
    {
        var list = KnownDatabases.FromJson(null, DefaultFolder);

        AssertJustTheDefaultFolder(list);
        Assert.False(list.CouldNotBeRead);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "databases": [ { "name": "A" } ] }""")]
    [InlineData("""{ "databases": [ { "name": "A", "folder": "relative" } ] }""")]
    [InlineData("""{ "databases": [ { "name": " ", "folder": "D:\\A" } ] }""")]
    [InlineData("""{ "databases": [ { "name": "A", "folder": "D:\\A" }, { "name": "B", "folder": "d:\\a\\" } ] }""")]
    [InlineData("""{ "databases": [ { "name": "A", "folder": "D:\\A" }, { "name": "a", "folder": "D:\\B" } ] }""")]
    public void A_damaged_file_gives_just_the_default_folder_and_says_so(string json)
    {
        var list = KnownDatabases.FromJson(json, DefaultFolder);

        AssertJustTheDefaultFolder(list);
        Assert.True(list.CouldNotBeRead);
    }

    [Fact]
    public void An_unreadable_file_is_treated_as_missing_and_says_so()
    {
        // A folder where the file should be: exists, but can't be read as a file.
        var folder = Path.Combine(Path.GetTempPath(), $"gnoseis-databases-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var list = KnownDatabases.Load(folder, DefaultFolder);

            AssertJustTheDefaultFolder(list);
            Assert.True(list.CouldNotBeRead);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void The_default_folder_can_be_renamed_but_not_removed()
    {
        var list = KnownDatabases.Create(DefaultFolder);

        Assert.False(list.Remove(DefaultFolder));
        Assert.False(list.Remove(DefaultFolder.ToUpperInvariant() + @"\"));
        Assert.Null(list.Rename(DefaultFolder, "Personal"));

        Assert.Equal("Personal", Assert.Single(list.Entries).Name);
    }

    [Theory]
    [InlineData(@"d:\work")]
    [InlineData(@"D:\Work\")]
    [InlineData(@"D:\Work\.")]
    [InlineData(@"D:\Other\..\Work")]
    public void A_folder_already_in_the_list_is_refused(string sameFolder)
    {
        var list = KnownDatabases.Create(DefaultFolder);
        Assert.Null(list.Add("Work", @"D:\Work"));

        Assert.NotNull(list.Add("Other name", sameFolder));
        Assert.Equal(2, list.Entries.Count);
    }

    [Theory]
    [InlineData("Work")]
    [InlineData("work")]
    [InlineData("  WORK ")]
    public void A_name_already_in_the_list_is_refused(string sameName)
    {
        var list = KnownDatabases.Create(DefaultFolder);
        Assert.Null(list.Add("Work", @"D:\Work"));
        Assert.Null(list.Add("Home", @"D:\Home"));

        Assert.NotNull(list.Add(sameName, @"D:\Other"));
        Assert.NotNull(list.Rename(@"D:\Home", sameName));
        Assert.NotNull(list.Add(KnownDatabases.DefaultFolderName.ToLowerInvariant(), @"D:\Third"));
        Assert.Equal(3, list.Entries.Count);
        Assert.Equal("Home", list.Find(@"D:\Home")!.Name);
    }

    [Fact]
    public void A_database_can_keep_its_own_name_in_another_case()
    {
        var list = KnownDatabases.Create(DefaultFolder);
        Assert.Null(list.Add("Work", @"D:\Work"));

        Assert.Null(list.Rename(@"D:\Work", "WORK"));
        Assert.Equal("WORK", list.Find(@"D:\Work")!.Name);
    }

    [Fact]
    public void A_blank_name_is_refused()
    {
        var list = KnownDatabases.Create(DefaultFolder);

        Assert.NotNull(list.Add("  ", @"D:\Work"));
        Assert.NotNull(list.Rename(DefaultFolder, ""));
        Assert.Single(list.Entries);
    }

    [Fact]
    public void Removing_the_default_entry_makes_the_default_folder_the_default()
    {
        var list = KnownDatabases.Create(DefaultFolder);
        Assert.Null(list.Add("Work", @"D:\Work"));
        list.SetDefault(@"D:\Work");
        Assert.Equal(@"D:\Work", list.Default.Folder);

        Assert.True(list.Remove(@"d:\work\"));

        Assert.Equal(DefaultFolder, list.Default.Folder);
        Assert.Single(list.Entries);
    }

    [Fact]
    public void A_round_trip_through_the_file_keeps_everything()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gnoseis-databases-{Guid.NewGuid():N}", KnownDatabases.FileName);
        try
        {
            var list = KnownDatabases.Create(DefaultFolder);
            Assert.Null(list.Rename(DefaultFolder, "Personal"));
            Assert.Null(list.Add("Work", @"D:\Work"));
            Assert.Null(list.Add("Demo", @"E:\Gnoseis demo\"));
            list.SetDefault(@"D:\Work");
            list.AskAtStartup = true;

            list.Save(path);
            var read = KnownDatabases.Load(path, DefaultFolder);

            Assert.False(read.CouldNotBeRead);
            Assert.True(read.AskAtStartup);
            Assert.Equal(list.Entries, read.Entries);
            Assert.Equal(@"E:\Gnoseis demo", read.Find(@"E:\Gnoseis demo")!.Folder);
            Assert.Equal(new KnownDatabase("Work", @"D:\Work"), read.Default);
            // Nothing left over from writing through a temporary file.
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void A_file_without_the_default_folder_gets_it_back()
    {
        var list = KnownDatabases.FromJson("""
            { "askAtStartup": false, "defaultFolder": "D:\\Work", "databases": [ { "name": "Work", "folder": "D:\\Work" } ] }
            """, DefaultFolder);

        Assert.False(list.CouldNotBeRead);
        Assert.Equal([KnownDatabases.DefaultFolderName, "Work"], list.Entries.Select(e => e.Name));
        Assert.Equal(@"D:\Work", list.Default.Folder);
    }

    [Fact]
    public void A_default_not_in_the_list_falls_back_to_the_default_folder()
    {
        var list = KnownDatabases.FromJson("""
            { "defaultFolder": "D:\\Gone", "databases": [ { "name": "Work", "folder": "D:\\Work" } ] }
            """, DefaultFolder);

        Assert.Equal(DefaultFolder, list.Default.Folder);
    }

    [Fact]
    public void An_argument_folder_not_in_the_list_is_added_once_named_after_the_folder()
    {
        var list = KnownDatabases.Create(DefaultFolder);

        Assert.True(list.AddIfMissing(@"D:\Gnoseis data\"));
        Assert.False(list.AddIfMissing(@"d:\gnoseis data"));
        Assert.False(list.AddIfMissing(DefaultFolder));

        Assert.Equal(2, list.Entries.Count);
        Assert.Equal(new KnownDatabase("Gnoseis data", @"D:\Gnoseis data"), list.Entries[1]);
        // Opening it doesn't make it the default.
        Assert.Equal(DefaultFolder, list.Default.Folder);
    }

    [Fact]
    public void An_added_folder_whose_name_is_taken_gets_a_number()
    {
        var list = KnownDatabases.Create(DefaultFolder);
        Assert.True(list.AddIfMissing(@"D:\Data"));
        Assert.True(list.AddIfMissing(@"E:\Data"));
        Assert.True(list.AddIfMissing(@"F:\"));

        Assert.Equal(["Data", "Data (2)", "F:"], list.Entries.Skip(1).Select(e => e.Name));
    }
}
