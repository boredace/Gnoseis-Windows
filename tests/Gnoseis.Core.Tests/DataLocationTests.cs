/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
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

    [Fact]
    public void Environment_variable_wins_over_the_marker_and_is_not_portable()
    {
        var location = DataLocation.Resolve(@"D:\Elsewhere", ExeFolder, portableMarkerExists: true, DefaultFolder);

        Assert.Equal(@"D:\Elsewhere", location.Folder);
        Assert.False(location.IsPortable);
    }

    [Fact]
    public void Marker_puts_the_data_in_a_folder_next_to_the_exe()
    {
        var location = DataLocation.Resolve(null, ExeFolder, portableMarkerExists: true, DefaultFolder);

        Assert.Equal(@"C:\Apps\Gnoseis\data", location.Folder);
        Assert.True(location.IsPortable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_variable_or_marker_the_default_folder_is_used(string? variable)
    {
        var location = DataLocation.Resolve(variable, ExeFolder, portableMarkerExists: false, DefaultFolder);

        Assert.Equal(DefaultFolder, location.Folder);
        Assert.False(location.IsPortable);
    }

    [Fact]
    public void A_blank_variable_does_not_hide_the_marker()
    {
        var location = DataLocation.Resolve("  ", ExeFolder, portableMarkerExists: true, DefaultFolder);

        Assert.True(location.IsPortable);
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
