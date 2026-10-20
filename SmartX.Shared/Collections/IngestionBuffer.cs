using System.Collections;

namespace SmartX.Shared.Collections;

/// <summary>
/// Custom circular buffer used as the gateway ingestion collection.
/// Oldest packets are overwritten when the window is full, so memory stays bounded.
/// </summary>
public sealed class IngestionBuffer<T> : IEnumerable<T>
{
    private readonly T[] _slots;
    private readonly object _gate = new();
    private int _head;
    private int _count;

    public IngestionBuffer(int capacity = 4096)
    {
        if (capacity < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _slots = new T[capacity];
    }

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    public int Capacity => _slots.Length;

    public void Add(T item)
    {
        lock (_gate)
        {
            // Ring write: overwrite the oldest slot when full.
            _slots[_head] = item;
            _head = (_head + 1) % _slots.Length;
            if (_count < _slots.Length)
            {
                _count++;
            }
        }
    }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    public T? FindLast(Func<T, bool> match)
    {
        lock (_gate)
        {
            for (var i = _count - 1; i >= 0; i--)
            {
                var item = Slot(i);
                if (match(item))
                {
                    return item;
                }
            }
        }

        return default;
    }

    public List<T> Snapshot()
    {
        lock (_gate)
        {
            var copy = new List<T>(_count);
            for (var i = 0; i < _count; i++)
            {
                copy.Add(Slot(i));
            }

            return copy;
        }
    }

    private T Slot(int logicalIndex)
    {
        var start = (_head - _count + _slots.Length) % _slots.Length;
        return _slots[(start + logicalIndex) % _slots.Length];
    }

    public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
