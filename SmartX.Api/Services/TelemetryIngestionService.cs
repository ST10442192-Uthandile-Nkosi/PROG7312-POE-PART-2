using SmartX.Shared.Collections;
using SmartX.Shared.Sensors;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Services;

public sealed class TelemetryIngestionService
{
    private readonly IngestionBuffer<TelemetryPacket<float>> _floats = new(4096);
    private readonly IngestionBuffer<TelemetryPacket<int>> _ints = new(4096);
    private readonly IngestionBuffer<TelemetryPacket<bool>> _bools = new(4096);
    private readonly Dictionary<string, TelemetrySnapshot> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _latestGate = new();
    private int _validStreak;
    private int _totalPackets;
    private int _fractures;

    public int ValidPacketStreak => _validStreak;
    public int TotalPackets => _totalPackets;
    public int FractureCount => _fractures;

    public TelemetryPacket<float> IngestFloat(TelemetryPacket<float> packet)
    {
        _floats.Add(packet);
        Record(packet.DeviceId, packet.Timestamp, "float", packet.Value.ToString("0.00"), packet.MetricName, packet.Unit);
        EvaluateRange(packet.Value is >= 0 and <= 100);
        return packet;
    }

    public TelemetryPacket<int> IngestInt(TelemetryPacket<int> packet)
    {
        _ints.Add(packet);
        Record(packet.DeviceId, packet.Timestamp, "int", packet.Value.ToString(), packet.MetricName, packet.Unit);
        EvaluateRange(packet.Value is >= 0 and <= 20000);
        return packet;
    }

    public TelemetryPacket<bool> IngestBool(TelemetryPacket<bool> packet)
    {
        _bools.Add(packet);
        Record(packet.DeviceId, packet.Timestamp, "bool", packet.Value ? "ON" : "OFF", packet.MetricName, packet.Unit);
        EvaluateRange(true);
        return packet;
    }

    public IReadOnlyList<TelemetrySnapshot> Recent(int take = 40)
    {
        var combined = new List<TelemetrySnapshot>();
        combined.AddRange(_floats.Select(p => ToSnapshot(p.DeviceId, p.Timestamp, "float", p.Value.ToString("0.00"), p.MetricName, p.Unit)));
        combined.AddRange(_ints.Select(p => ToSnapshot(p.DeviceId, p.Timestamp, "int", p.Value.ToString(), p.MetricName, p.Unit)));
        combined.AddRange(_bools.Select(p => ToSnapshot(p.DeviceId, p.Timestamp, "bool", p.Value ? "ON" : "OFF", p.MetricName, p.Unit)));
        return combined.OrderByDescending(s => s.Timestamp).Take(take).ToList();
    }

    public bool TryGetLatest(string deviceId, out TelemetrySnapshot snapshot)
    {
        lock (_latestGate)
        {
            return _latest.TryGetValue(deviceId, out snapshot!);
        }
    }

    public float? LatestFloat(string deviceId) =>
        _floats.FindLast(p => p.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))?.Value;

    public int? LatestInt(string deviceId) =>
        _ints.FindLast(p => p.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))?.Value;

    public bool? LatestBool(string deviceId) =>
        _bools.FindLast(p => p.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))?.Value;

    public List<CouplingAlarm> DetectCouplingAlarms(IEnumerable<SensorProfile> sensors)
    {
        var alarms = new List<CouplingAlarm>();
        var list = sensors.ToList();

        foreach (var bed in list.Where(s => s.Category == SensorCategory.Environmental))
        {
            var vwc = LatestFloat(bed.MacAddress);
            if (vwc is null || vwc >= 28) continue;

            var valve = list.FirstOrDefault(v =>
                v.Category == SensorCategory.Actuator && SharesZone(bed.Location, v.Location));
            if (valve is null || LatestBool(valve.MacAddress) != false) continue;

            alarms.Add(new CouplingAlarm
            {
                Code = "IRR-DEADLOCK",
                Title = "Dry bed, valve closed",
                Detail = $"{bed.DisplayName} is {vwc:0.0}% VWC but {valve.DisplayName} is OFF."
            });
        }

        var meters = list.Where(s => s.Category == SensorCategory.PowerConsumption).ToList();
        if (meters.Count >= 2)
        {
            var a = LatestInt(meters[0].MacAddress);
            var b = LatestInt(meters[1].MacAddress);
            if (a is not null && b is not null)
            {
                var combined = new PowerMetric { Watts = a.Value, MeterId = meters[0].DisplayName }
                               + new PowerMetric { Watts = b.Value, MeterId = meters[1].DisplayName };
                if (combined > new PowerMetric { Watts = 3500, MeterId = "TX" })
                {
                    alarms.Add(new CouplingAlarm
                    {
                        Code = "GRID-OVERLOAD",
                        Title = "Feeder overload",
                        Detail = $"{combined.MeterId} = {combined.Watts:0} W (limit 3500 W)."
                    });
                }
            }
        }

        return alarms;
    }

    private void EvaluateRange(bool inRange)
    {
        Interlocked.Increment(ref _totalPackets);
        if (inRange)
        {
            Interlocked.Increment(ref _validStreak);
        }
        else
        {
            Interlocked.Exchange(ref _validStreak, 0);
            Interlocked.Increment(ref _fractures);
        }
    }

    private void Record(string deviceId, DateTimeOffset timestamp, string kind, string display, string metric, string unit)
    {
        lock (_latestGate)
        {
            _latest[deviceId] = ToSnapshot(deviceId, timestamp, kind, display, metric, unit);
        }
    }

    private static TelemetrySnapshot ToSnapshot(string deviceId, DateTimeOffset timestamp, string kind, string display, string metric, string unit) =>
        new()
        {
            DeviceId = deviceId,
            Timestamp = timestamp,
            ValueKind = kind,
            DisplayValue = display,
            MetricName = metric,
            Unit = unit
        };

    private static bool SharesZone(string left, string right)
    {
        var l = left.Split("->", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var r = right.Split("->", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return l.Length >= 2 && r.Length >= 2 && l[0] == r[0] && l[1] == r[1];
    }
}
