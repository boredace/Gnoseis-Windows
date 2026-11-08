/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.Core.Tests;

public class PaneCycleTests
{
    private static readonly Pane[] All = [Pane.Search, Pane.Navigation, Pane.List, Pane.Record];

    [Theory]
    [InlineData(Pane.Search, Pane.Navigation)]
    [InlineData(Pane.Navigation, Pane.List)]
    [InlineData(Pane.List, Pane.Record)]
    [InlineData(Pane.Record, Pane.Search)]
    public void F6_GoesForward_AndWraps(Pane current, Pane expected)
    {
        Assert.Equal(expected, PaneCycle.Next(current, backwards: false, All));
    }

    [Theory]
    [InlineData(Pane.Search, Pane.Record)]
    [InlineData(Pane.Navigation, Pane.Search)]
    [InlineData(Pane.List, Pane.Navigation)]
    [InlineData(Pane.Record, Pane.List)]
    public void ShiftF6_GoesBackward_AndWraps(Pane current, Pane expected)
    {
        Assert.Equal(expected, PaneCycle.Next(current, backwards: true, All));
    }

    [Fact]
    public void NoList_SkipsIt_BothWays()
    {
        // Home, Settings, the search page, or a narrow window without the list column.
        Pane[] showing = [Pane.Search, Pane.Navigation, Pane.Record];
        Assert.Equal(Pane.Record, PaneCycle.Next(Pane.Navigation, backwards: false, showing));
        Assert.Equal(Pane.Navigation, PaneCycle.Next(Pane.Record, backwards: true, showing));
    }

    [Fact]
    public void NoRecord_WrapsFromListToSearch()
    {
        Pane[] showing = [Pane.Search, Pane.Navigation, Pane.List];
        Assert.Equal(Pane.Search, PaneCycle.Next(Pane.List, backwards: false, showing));
        Assert.Equal(Pane.List, PaneCycle.Next(Pane.Search, backwards: true, showing));
    }

    [Fact]
    public void FocusOutsideEveryPane_StartsAtFirstOrLast()
    {
        Assert.Equal(Pane.Search, PaneCycle.Next(null, backwards: false, All));
        Assert.Equal(Pane.Record, PaneCycle.Next(null, backwards: true, All));
    }

    [Fact]
    public void CurrentPaneNotShowing_StillMovesOn()
    {
        Pane[] showing = [Pane.Search, Pane.Record];
        Assert.Equal(Pane.Record, PaneCycle.Next(Pane.Navigation, backwards: false, showing));
    }

    [Fact]
    public void OnlyOnePane_StaysThere()
    {
        Assert.Equal(Pane.Record, PaneCycle.Next(Pane.Record, backwards: false, [Pane.Record]));
    }

    [Fact]
    public void NothingShowing_ReturnsNull()
    {
        Assert.Null(PaneCycle.Next(Pane.Search, backwards: false, []));
    }
}
