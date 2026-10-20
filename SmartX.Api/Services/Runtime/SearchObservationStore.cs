using SmartX.Shared.Runtime;

namespace SmartX.Api.Services;

/// <summary>
/// Bounded operator search log. Oldest entries drop when capacity is exceeded.
/// Does not issue commands.
/// </summary>
public sealed class SearchObservationStore
{
    public const int Capacity = 32;

    private readonly Queue<SearchObservation> _items = new();
    private readonly object _gate = new();

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    public void Record(string term, DateTimeOffset? atUtc = null)
    {
        var trimmed = term.Trim();
        if (trimmed.Length is < 2 or > 64)
        {
            return;
        }

        var item = new SearchObservation
        {
            Term = trimmed,
            AtUtc = (atUtc ?? DateTimeOffset.UtcNow).ToUniversalTime()
        };

        lock (_gate)
        {
            _items.Enqueue(item);
            while (_items.Count > Capacity)
            {
                _items.Dequeue();
            }
        }
    }

    public IReadOnlyList<SearchObservation> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToList();
        }
    }
}
