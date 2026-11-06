/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Domain;

/// <summary>What <see cref="RecordTrail{T}.Back"/> did.</summary>
public enum TrailBackResult
{
    /// <summary>No trail is open; Back has nothing to do here.</summary>
    None,

    /// <summary>The previous record is now current.</summary>
    Stepped,

    /// <summary>The first record was current: the trail closes and the list comes back.</summary>
    Close,
}

/// <summary>
/// The records opened from one another in a section, with a current position. The first record is the
/// one selected in the section's list; each later one was opened from a link on a record page. A record
/// is never in the trail twice. The trail is open ("trail mode") only while it has two or more records.
/// Records after the current position are kept for Forward until another record is opened.
/// </summary>
public sealed class RecordTrail<T> where T : notnull
{
    private readonly List<T> _items = [];
    private readonly IEqualityComparer<T> _comparer;

    public RecordTrail(IEqualityComparer<T>? comparer = null) => _comparer = comparer ?? EqualityComparer<T>.Default;

    /// <summary>All records in the trail, first to last, including those after the current one.</summary>
    public IReadOnlyList<T> Items => _items;

    public int Count => _items.Count;

    /// <summary>Index of the current record, or -1 when the trail is empty.</summary>
    public int CurrentIndex { get; private set; } = -1;

    /// <summary>True while there are two or more records.</summary>
    public bool IsOpen => _items.Count >= 2;

    public T? Current => CurrentIndex >= 0 ? _items[CurrentIndex] : default;

    public T? First => _items.Count > 0 ? _items[0] : default;

    /// <summary>The record the current one was opened from, if it isn't the first.</summary>
    public T? Previous => CurrentIndex > 0 ? _items[CurrentIndex - 1] : default;

    public bool CanGoForward => CurrentIndex >= 0 && CurrentIndex < _items.Count - 1;

    /// <summary>True when the record is in the trail at an index after the current one.</summary>
    public bool IsAfterCurrent(int index) => index > CurrentIndex && index < _items.Count;

    public int IndexOf(T record) => _items.FindIndex(x => _comparer.Equals(x, record));

    public bool Contains(T record) => IndexOf(record) >= 0;

    /// <summary>Starts a new trail with only the record selected in the list.</summary>
    public void Start(T first)
    {
        _items.Clear();
        _items.Add(first);
        CurrentIndex = 0;
    }

    /// <summary>Drops every record.</summary>
    public void Clear()
    {
        _items.Clear();
        CurrentIndex = -1;
    }

    /// <summary>
    /// Opens a record from the current one. A record already in the trail becomes current, with nothing
    /// added or removed. Otherwise the records after the current one are dropped and the record is added
    /// after it. With an empty trail the record starts it.
    /// </summary>
    public void Open(T record)
    {
        var index = IndexOf(record);
        if (index >= 0)
        {
            CurrentIndex = index;
            return;
        }
        if (_items.Count == 0)
        {
            Start(record);
            return;
        }
        _items.RemoveRange(CurrentIndex + 1, _items.Count - CurrentIndex - 1);
        _items.Add(record);
        CurrentIndex = _items.Count - 1;
    }

    /// <summary>
    /// One step back. From the first record the trail closes (<see cref="Close"/> is called) and
    /// <see cref="TrailBackResult.Close"/> is returned.
    /// </summary>
    public TrailBackResult Back()
    {
        if (!IsOpen)
        {
            return TrailBackResult.None;
        }
        if (CurrentIndex == 0)
        {
            Close();
            return TrailBackResult.Close;
        }
        CurrentIndex--;
        return TrailBackResult.Stepped;
    }

    /// <summary>One step forward, if there's a record after the current one.</summary>
    public bool Forward()
    {
        if (!CanGoForward)
        {
            return false;
        }
        CurrentIndex++;
        return true;
    }

    /// <summary>Makes the record at <paramref name="index"/> current; the records after it stay.</summary>
    public bool GoTo(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }
        CurrentIndex = index;
        return true;
    }

    /// <summary>Closes the trail: only the first record is left, and it's current.</summary>
    public void Close()
    {
        if (_items.Count == 0)
        {
            return;
        }
        _items.RemoveRange(1, _items.Count - 1);
        CurrentIndex = 0;
    }

    /// <summary>
    /// Removes a record (it was deleted); the others keep their order. If it was current, the record
    /// before it becomes current (the next one when it was the first). Returns false if it wasn't in the trail.
    /// </summary>
    public bool Remove(T record)
    {
        var index = IndexOf(record);
        if (index < 0)
        {
            return false;
        }
        _items.RemoveAt(index);
        if (_items.Count == 0)
        {
            CurrentIndex = -1;
        }
        else if (index < CurrentIndex || (index == CurrentIndex && CurrentIndex > 0))
        {
            CurrentIndex--;
        }
        return true;
    }

    /// <summary>Puts a new copy of a record (for example with a changed title) in its place.</summary>
    public bool Replace(T record)
    {
        var index = IndexOf(record);
        if (index < 0)
        {
            return false;
        }
        _items[index] = record;
        return true;
    }
}
