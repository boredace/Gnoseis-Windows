/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.Core.Tests;

public class RecordTrailTests
{
    private static RecordTrail<string> Trail(params string[] records)
    {
        var trail = new RecordTrail<string>();
        trail.Start(records[0]);
        foreach (var record in records.Skip(1))
        {
            trail.Open(record);
        }
        return trail;
    }

    [Fact]
    public void Start_HasOneRecord_NotOpen()
    {
        var trail = Trail("a");

        Assert.Equal(["a"], trail.Items);
        Assert.Equal(0, trail.CurrentIndex);
        Assert.Equal("a", trail.Current);
        Assert.False(trail.IsOpen);
        Assert.Null(trail.Previous);
    }

    [Fact]
    public void Empty_HasNoCurrent()
    {
        var trail = new RecordTrail<string>();

        Assert.Equal(-1, trail.CurrentIndex);
        Assert.Null(trail.Current);
        Assert.False(trail.IsOpen);
        Assert.Equal(TrailBackResult.None, trail.Back());
        Assert.False(trail.Forward());
    }

    [Fact]
    public void Open_AddsAfterCurrent_AndOpensTrailFromTwoRecords()
    {
        var trail = Trail("a", "b", "c");

        Assert.Equal(["a", "b", "c"], trail.Items);
        Assert.Equal("c", trail.Current);
        Assert.Equal("b", trail.Previous);
        Assert.Equal("a", trail.First);
        Assert.True(trail.IsOpen);
    }

    [Fact]
    public void Open_OnEmptyTrail_StartsIt()
    {
        var trail = new RecordTrail<string>();
        trail.Open("a");

        Assert.Equal(["a"], trail.Items);
        Assert.Equal("a", trail.Current);
    }

    [Fact]
    public void Open_DropsRecordsAfterCurrentFirst()
    {
        var trail = Trail("a", "b", "c", "d");
        trail.Back();
        trail.Back();

        trail.Open("e");

        Assert.Equal(["a", "b", "e"], trail.Items);
        Assert.Equal("e", trail.Current);
        Assert.False(trail.CanGoForward);
    }

    [Fact]
    public void Open_RecordAlreadyInTrail_MovesPosition_AddsAndDropsNothing()
    {
        var trail = Trail("a", "b", "c", "d");
        trail.Back();

        trail.Open("a");

        Assert.Equal(["a", "b", "c", "d"], trail.Items);
        Assert.Equal(0, trail.CurrentIndex);

        trail.Open("d");

        Assert.Equal(["a", "b", "c", "d"], trail.Items);
        Assert.Equal(3, trail.CurrentIndex);
    }

    [Fact]
    public void Open_UsesTheComparer()
    {
        var trail = new RecordTrail<string>(StringComparer.OrdinalIgnoreCase);
        trail.Start("a");
        trail.Open("B");
        trail.Open("b");

        Assert.Equal(["a", "B"], trail.Items);
    }

    [Fact]
    public void Back_StepsBack_KeepingLaterRecords()
    {
        var trail = Trail("a", "b", "c");

        Assert.Equal(TrailBackResult.Stepped, trail.Back());
        Assert.Equal("b", trail.Current);
        Assert.Equal(["a", "b", "c"], trail.Items);
        Assert.True(trail.CanGoForward);
        Assert.True(trail.IsAfterCurrent(2));
        Assert.False(trail.IsAfterCurrent(1));
    }

    [Fact]
    public void Back_FromFirstRecord_ReportsClose_AndClosesTrail()
    {
        var trail = Trail("a", "b", "c");
        trail.Back();
        trail.Back();

        Assert.Equal(TrailBackResult.Close, trail.Back());
        Assert.Equal(["a"], trail.Items);
        Assert.Equal("a", trail.Current);
        Assert.False(trail.IsOpen);
        Assert.Equal(TrailBackResult.None, trail.Back());
    }

    [Fact]
    public void Back_WithOneRecord_DoesNothing()
    {
        var trail = Trail("a");

        Assert.Equal(TrailBackResult.None, trail.Back());
        Assert.Equal(["a"], trail.Items);
    }

    [Fact]
    public void Forward_StepsForward_AndStopsAtTheEnd()
    {
        var trail = Trail("a", "b", "c");
        trail.Back();
        trail.Back();

        Assert.True(trail.Forward());
        Assert.Equal("b", trail.Current);
        Assert.True(trail.Forward());
        Assert.Equal("c", trail.Current);
        Assert.False(trail.Forward());
        Assert.Equal("c", trail.Current);
    }

    [Fact]
    public void GoTo_MakesRowCurrent_KeepingLaterRows()
    {
        var trail = Trail("a", "b", "c", "d");

        Assert.True(trail.GoTo(1));
        Assert.Equal("b", trail.Current);
        Assert.Equal(["a", "b", "c", "d"], trail.Items);
        Assert.True(trail.CanGoForward);

        Assert.False(trail.GoTo(4));
        Assert.False(trail.GoTo(-1));
        Assert.Equal(1, trail.CurrentIndex);
    }

    [Fact]
    public void Close_LeavesOnlyFirstRecord()
    {
        var trail = Trail("a", "b", "c");
        trail.Back();

        trail.Close();

        Assert.Equal(["a"], trail.Items);
        Assert.Equal("a", trail.Current);
        Assert.False(trail.IsOpen);
    }

    [Fact]
    public void Clear_DropsEverything()
    {
        var trail = Trail("a", "b");

        trail.Clear();

        Assert.Empty(trail.Items);
        Assert.Null(trail.Current);
    }

    [Fact]
    public void Remove_CurrentRecord_PreviousBecomesCurrent()
    {
        var trail = Trail("a", "b", "c", "d");
        trail.Back();

        Assert.True(trail.Remove("c"));

        Assert.Equal(["a", "b", "d"], trail.Items);
        Assert.Equal("b", trail.Current);
    }

    [Fact]
    public void Remove_LastRecordWhileCurrent_PreviousBecomesCurrent()
    {
        var trail = Trail("a", "b", "c");

        trail.Remove("c");

        Assert.Equal(["a", "b"], trail.Items);
        Assert.Equal("b", trail.Current);
        Assert.True(trail.IsOpen);
    }

    [Fact]
    public void Remove_RecordBeforeCurrent_KeepsCurrent()
    {
        var trail = Trail("a", "b", "c", "d");

        trail.Remove("b");

        Assert.Equal(["a", "c", "d"], trail.Items);
        Assert.Equal("d", trail.Current);
    }

    [Fact]
    public void Remove_RecordAfterCurrent_KeepsCurrent()
    {
        var trail = Trail("a", "b", "c", "d");
        trail.GoTo(1);

        trail.Remove("d");

        Assert.Equal(["a", "b", "c"], trail.Items);
        Assert.Equal("b", trail.Current);
    }

    [Fact]
    public void Remove_FirstRecordWhileCurrent_NextBecomesFirstAndCurrent()
    {
        var trail = Trail("a", "b", "c");
        trail.GoTo(0);

        trail.Remove("a");

        Assert.Equal(["b", "c"], trail.Items);
        Assert.Equal("b", trail.Current);
        Assert.Equal("b", trail.First);
    }

    [Fact]
    public void Remove_FirstRecordWhileNotCurrent_KeepsCurrent()
    {
        var trail = Trail("a", "b", "c");

        trail.Remove("a");

        Assert.Equal(["b", "c"], trail.Items);
        Assert.Equal("c", trail.Current);
    }

    [Fact]
    public void Remove_LeavingOneRecord_ClosesTrail()
    {
        var trail = Trail("a", "b");

        trail.Remove("b");

        Assert.Equal(["a"], trail.Items);
        Assert.Equal("a", trail.Current);
        Assert.False(trail.IsOpen);
    }

    [Fact]
    public void Remove_RecordNotInTrail_ChangesNothing()
    {
        var trail = Trail("a", "b");

        Assert.False(trail.Remove("x"));
        Assert.Equal(["a", "b"], trail.Items);
        Assert.Equal("b", trail.Current);
    }

    [Fact]
    public void Remove_OnlyRecord_EmptiesTrail()
    {
        var trail = Trail("a");

        trail.Remove("a");

        Assert.Empty(trail.Items);
        Assert.Equal(-1, trail.CurrentIndex);
    }

    [Fact]
    public void Replace_PutsNewCopyInPlace()
    {
        var trail = new RecordTrail<(string Id, string Title)>(new IdComparer());
        trail.Start(("1", "Anna"));
        trail.Open(("2", "Acme"));

        Assert.True(trail.Replace(("1", "Anna Berg")));
        Assert.False(trail.Replace(("3", "Other")));

        Assert.Equal([("1", "Anna Berg"), ("2", "Acme")], trail.Items);
        Assert.Equal(1, trail.CurrentIndex);
    }

    private sealed class IdComparer : IEqualityComparer<(string Id, string Title)>
    {
        public bool Equals((string Id, string Title) x, (string Id, string Title) y) => x.Id == y.Id;

        public int GetHashCode((string Id, string Title) obj) => obj.Id.GetHashCode();
    }
}
