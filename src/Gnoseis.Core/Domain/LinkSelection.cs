/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>
/// The records ticked in the link picker, in the order picked. Ticks don't depend on the query or the
/// chips: a record stays ticked until it is unticked or linked, and records of any types can be ticked
/// together. Records already linked to the source can't be ticked.
/// </summary>
public sealed class LinkSelection
{
    private readonly List<SearchResult> _picked = [];
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private HashSet<string> _linked = new(StringComparer.Ordinal);

    /// <summary>The ticked records, in the order picked.</summary>
    public IReadOnlyList<SearchResult> Picked => _picked;

    public int Count => _picked.Count;

    public bool IsEmpty => _picked.Count == 0;

    public bool Contains(string recordId) => _ids.Contains(recordId);

    public bool IsLinked(string recordId) => _linked.Contains(recordId);

    /// <summary>Ticks or unticks a record. Returns true when that changed anything.</summary>
    public bool Set(SearchResult record, bool ticked)
    {
        if (ticked)
        {
            if (_linked.Contains(record.RecordId) || !_ids.Add(record.RecordId))
            {
                return false;
            }
            _picked.Add(record);
            return true;
        }
        if (!_ids.Remove(record.RecordId))
        {
            return false;
        }
        _picked.RemoveAll(r => r.RecordId == record.RecordId);
        return true;
    }

    /// <summary>
    /// The records linked to the source now; a ticked record that became linked is unticked.
    /// Returns true when a tick was removed.
    /// </summary>
    public bool SetLinked(IEnumerable<string> linkedIds)
    {
        _linked = new HashSet<string>(linkedIds, StringComparer.Ordinal);
        return Keep(r => !_linked.Contains(r.RecordId));
    }

    /// <summary>
    /// Updates the ticked records from the current records (titles and counts may have changed) and
    /// unticks those that no longer exist. Returns true when a tick was removed.
    /// </summary>
    public bool Refresh(IEnumerable<SearchResult> current)
    {
        var byId = current.ToDictionary(r => r.RecordId, StringComparer.Ordinal);
        var removed = Keep(r => byId.ContainsKey(r.RecordId));
        for (var i = 0; i < _picked.Count; i++)
        {
            _picked[i] = byId[_picked[i].RecordId];
        }
        return removed;
    }

    /// <summary>The ticked records to link: every one, shown or not.</summary>
    public IReadOnlyList<SearchResult> ToLink() => _picked.Where(r => !_linked.Contains(r.RecordId)).ToList();

    /// <summary>Unticks everything (after linking).</summary>
    public void Clear()
    {
        _picked.Clear();
        _ids.Clear();
    }

    private bool Keep(Predicate<SearchResult> keep)
    {
        var removed = _picked.RemoveAll(r => !keep(r));
        if (removed > 0)
        {
            _ids.Clear();
            _ids.UnionWith(_picked.Select(r => r.RecordId));
        }
        return removed > 0;
    }
}
