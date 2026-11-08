/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.Core.Tests;

public class FindOnScreenTests
{
    [Fact]
    public void FocusInRecord_WithRecordBox_PicksRecordBox_EvenWithListFilter()
    {
        Assert.Equal(FindTarget.RecordPageBox, FindOnScreen.Choose(focusInRecordPane: true, recordFindBoxShowing: true, listFilterShowing: true));
    }

    [Fact]
    public void FocusInRecord_WithoutRecordBox_PicksListFilter()
    {
        Assert.Equal(FindTarget.ListFilterBox, FindOnScreen.Choose(focusInRecordPane: true, recordFindBoxShowing: false, listFilterShowing: true));
    }

    [Fact]
    public void FocusOutsideRecord_WithListFilter_PicksListFilter()
    {
        Assert.Equal(FindTarget.ListFilterBox, FindOnScreen.Choose(focusInRecordPane: false, recordFindBoxShowing: true, listFilterShowing: true));
    }

    [Fact]
    public void NoListFilter_WithRecordBox_PicksRecordBox()
    {
        // The trail column (or a narrow window showing only the record) has no list filter box.
        Assert.Equal(FindTarget.RecordPageBox, FindOnScreen.Choose(focusInRecordPane: false, recordFindBoxShowing: true, listFilterShowing: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeitherBox_SearchesEverything(bool focusInRecordPane)
    {
        // Home, Settings, the search page, or the trail column with no find box on the record.
        Assert.Equal(FindTarget.SearchEverything, FindOnScreen.Choose(focusInRecordPane, recordFindBoxShowing: false, listFilterShowing: false));
    }
}
