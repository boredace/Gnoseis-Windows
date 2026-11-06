/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.Core.Tests;

/// <summary>The item and organization pages' columns by band, and their copy text.</summary>
public class RecordHistoryPageTests
{
    [Fact]
    public void Copy_text_is_the_name_then_the_comments()
    {
        Assert.Equal(string.Join(Environment.NewLine, "SRV-0007", "", "Hyper-V host"), RecordHistoryPage.CopyText(" SRV-0007 ", " Hyper-V host "));
        Assert.Equal("SRV-0007", RecordHistoryPage.CopyText("SRV-0007", "  "));
        Assert.Equal("SRV-0007", RecordHistoryPage.CopyText("SRV-0007", null));
    }

    [Theory]
    [InlineData(RecordPageBand.S, 700, true, 0, 700, 0, 0)]
    [InlineData(RecordPageBand.M, 1000, true, 0, 728, 0, 240)]
    [InlineData(RecordPageBand.L, 1500, true, 0, 760, 0, 320)]
    [InlineData(RecordPageBand.L, 1100, true, 0, 748, 0, 320)]
    [InlineData(RecordPageBand.XL, 2000, true, 0, 560, 960, 320)]
    [InlineData(RecordPageBand.XL, 1700, true, 0, 560, 756, 320)]
    [InlineData(RecordPageBand.XL, 2000, false, 0, 760, 0, 320)]
    [InlineData(RecordPageBand.XXL, 3600, true, 0, 640, 1100, 680)]
    [InlineData(RecordPageBand.XXL, 3600, false, 0, 760, 0, 680)]
    public void Item_columns(RecordPageBand band, double content, bool hasNotes, double people, double history, double reading, double side)
    {
        var columns = RecordHistoryPage.Columns(band, content, hasNotes, isOrganization: false);

        Assert.Equal((people, history, reading, side), (columns.People, columns.History, columns.Reading, columns.Side));
    }

    [Theory]
    [InlineData(RecordPageBand.S, 700, true, 0, 700, 0, 0)]
    [InlineData(RecordPageBand.M, 1000, true, 0, 728, 0, 240)]
    [InlineData(RecordPageBand.L, 1500, true, 300, 876, 0, 260)]
    [InlineData(RecordPageBand.XL, 2200, true, 340, 560, 720, 280)]
    [InlineData(RecordPageBand.XL, 1700, true, 340, 560, 424, 280)]
    [InlineData(RecordPageBand.XL, 2200, false, 340, 760, 0, 300)]
    [InlineData(RecordPageBand.XXL, 3600, true, 400, 640, 1000, 320)]
    [InlineData(RecordPageBand.XXL, 3600, false, 400, 760, 0, 320)]
    public void Organization_columns(RecordPageBand band, double content, bool hasNotes, double people, double history, double reading, double side)
    {
        var columns = RecordHistoryPage.Columns(band, content, hasNotes, isOrganization: true);

        Assert.Equal((people, history, reading, side), (columns.People, columns.History, columns.Reading, columns.Side));
    }

    [Fact]
    public void Content_is_capped_at_2700_in_XXL()
    {
        var wide = RecordHistoryPage.Columns(RecordPageBand.XXL, 5000, hasNotes: true, isOrganization: false);
        var capped = RecordHistoryPage.Columns(RecordPageBand.XXL, 2700, hasNotes: true, isOrganization: false);

        Assert.Equal(capped, wide);
    }

    [Fact]
    public void Only_an_item_side_column_has_two_columns_in_XXL()
    {
        Assert.Equal(2, RecordHistoryPage.Columns(RecordPageBand.XXL, 3000, true, isOrganization: false).SideColumnCount);
        Assert.Equal(1, RecordHistoryPage.Columns(RecordPageBand.XXL, 3000, true, isOrganization: true).SideColumnCount);
        Assert.Equal(1, RecordHistoryPage.Columns(RecordPageBand.XL, 2000, true, isOrganization: false).SideColumnCount);
    }
}
