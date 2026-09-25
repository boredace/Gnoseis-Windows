/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.DemoData;

/// <summary>Seeded random helpers, so the same seed always produces the same database.</summary>
internal sealed class Rng(int seed)
{
    private readonly Random _random = new(seed);

    public int Between(int min, int maxInclusive) => _random.Next(min, maxInclusive + 1);

    public double Double() => _random.NextDouble();

    public bool Chance(double probability) => _random.NextDouble() < probability;

    public T Pick<T>(IReadOnlyList<T> list) => list[_random.Next(list.Count)];

    public List<T> PickDistinct<T>(IReadOnlyList<T> list, int count)
    {
        count = Math.Min(count, list.Count);
        var indexes = new HashSet<int>();
        while (indexes.Count < count)
        {
            indexes.Add(_random.Next(list.Count));
        }
        return indexes.Select(i => list[i]).ToList();
    }

    public string Digits(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + _random.Next(10))));

    public string Letters(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(_ => (char)('A' + _random.Next(26))));

    public string Guid()
    {
        Span<byte> bytes = stackalloc byte[16];
        _random.NextBytes(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40); // version 4, like java.util.UUID.randomUUID()
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new System.Guid(bytes).ToString();
    }

    public DateOnly DateBetween(DateOnly from, DateOnly to) =>
        to <= from ? from : from.AddDays(_random.Next(to.DayNumber - from.DayNumber + 1));
}

/// <summary>Picks items in proportion to a weight (e.g. busy clients get more notes).</summary>
internal sealed class WeightedPicker<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly double[] _cumulative;

    public WeightedPicker(IReadOnlyList<T> items, Func<T, double> weight)
    {
        _items = items;
        _cumulative = new double[items.Count];
        double total = 0;
        for (var i = 0; i < items.Count; i++)
        {
            total += Math.Max(0, weight(items[i]));
            _cumulative[i] = total;
        }
    }

    public bool IsEmpty => _items.Count == 0 || _cumulative[^1] <= 0;

    public T Pick(Rng rng)
    {
        var target = rng.Double() * _cumulative[^1];
        var index = Array.BinarySearch(_cumulative, target);
        if (index < 0)
        {
            index = ~index;
        }
        return _items[Math.Min(index, _items.Count - 1)];
    }
}
