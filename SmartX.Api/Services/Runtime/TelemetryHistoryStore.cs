using System.Globalization;
using SmartX.Shared.Runtime;

namespace SmartX.Api.Services;

/// <summary>
/// Chronological processed readings. Composite HistoryKey keeps duplicate timestamps distinct.
/// </summary>
public sealed class TelemetryHistoryStore
{
    private readonly SortedDictionary<HistoryKey, ProcessedReading> _history = [];
    private readonly object _gate = new();
    private readonly int _capacity;
    private long _sequence;

    public TelemetryHistoryStore(int capacity = 2000)
    {
        _capacity = capacity;
    }

    public int Count
    {
        get { lock (_gate) return _history.Count; }
    }

    public ProcessedReading Add(TelemetryWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var reading = new ProcessedReading
        {
            TimestampUtc = item.TimestampUtc.ToUniversalTime(),
            DeviceId = SensorRegistry.NormalizeMac(item.DeviceId),
            ValueKind = item.ValueKind,
            DisplayValue = item.ValueKind == "bool"
                ? (item.BooleanValue == true ? "ON" : "OFF")
                : item.NumericValue?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
            MetricName = item.MetricName,
            Unit = item.Unit
        };

        lock (_gate)
        {
            var key = new HistoryKey(reading.TimestampUtc, ++_sequence);
            _history[key] = reading;
            while (_history.Count > _capacity)
            {
                _history.Remove(_history.Keys.First());
            }
        }

        return reading;
    }

    public IReadOnlyList<ProcessedReading> Latest(int take, string? mac = null)
    {
        take = Math.Clamp(take, 1, 500);
        lock (_gate)
        {
            IEnumerable<ProcessedReading> query = _history.Values;
            if (!string.IsNullOrWhiteSpace(mac))
            {
                var normalized = SensorRegistry.NormalizeMac(mac);
                query = query.Where(r => r.DeviceId.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            }

            return query.TakeLast(take).Reverse().ToList();
        }
    }

    public IReadOnlyList<ProcessedReading> SnapshotOldestFirst()
    {
        lock (_gate)
        {
            return _history.Values.ToList();
        }
    }
}
