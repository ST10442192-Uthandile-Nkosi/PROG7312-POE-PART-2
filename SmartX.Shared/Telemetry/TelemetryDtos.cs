using SmartX.Shared.Sensors;

namespace SmartX.Shared.Telemetry;

public sealed class MetricAggregateRequest
{
    public PowerMetric Metric1 { get; set; } = new();
    public PowerMetric Metric2 { get; set; } = new();
    public double TransformerLimitWatts { get; set; } = 4000;
}

public sealed class MetricAggregateResponse
{
    public double AggregatedWatts { get; set; }
    public double DeltaWatts { get; set; }
    public bool ExceedsTransformer { get; set; }
    public string CombinedMeterId { get; set; } = string.Empty;
}

public sealed class TelemetryIngestRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string ValueKind { get; set; } = "float";
    public double? NumericValue { get; set; }
    public bool? BooleanValue { get; set; }
    public string MetricName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}

public sealed class TelemetrySnapshot
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string ValueKind { get; set; } = string.Empty;
    public string DisplayValue { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}

public sealed class HistoryResponse
{
    public int Count { get; set; }
    public List<double> Data { get; set; } = [];
    public double[][] HourlyFacilityLoad { get; set; } = [];
    public int BurstWindowsStored { get; set; }
}

public sealed class MeshNodeView
{
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public string Location { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public string Status { get; set; } = "nominal";
    public string LatestValue { get; set; } = "—";
    public string Alert { get; set; } = string.Empty;
}

public sealed class CouplingAlarm
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset RaisedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GatewayIntegrityReport
{
    public List<MeshNodeView> Nodes { get; set; } = [];
    public List<CouplingAlarm> Alarms { get; set; } = [];
    public string StewardRank { get; set; } = "Bronze";
    public int ValidPacketStreak { get; set; }
    public int TotalPackets { get; set; }
    public int FractureCount { get; set; }
    public string Narrative { get; set; } = string.Empty;
}
