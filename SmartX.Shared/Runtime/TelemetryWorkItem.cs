using SmartX.Shared.Sensors;

namespace SmartX.Shared.Runtime;

public sealed class TelemetryWorkItem
{
    public long Sequence { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string ValueKind { get; init; } = "float";
    public double? NumericValue { get; init; }
    public bool? BooleanValue { get; init; }
    public string MetricName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public SensorCategory? KnownCategory { get; init; }
    public int AttemptCount { get; init; }
}

public sealed class AlertWorkItem
{
    public long Sequence { get; init; }
    public int Priority { get; init; }
    public string FaultId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public DateTimeOffset RaisedAtUtc { get; init; }
}

public sealed class ProcessedReading
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public string ValueKind { get; init; } = string.Empty;
    public string DisplayValue { get; init; } = string.Empty;
    public string MetricName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
}

public sealed class LiveDeviceState
{
    public string MacAddress { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public SensorCategory Category { get; init; }
    public string? LatestDisplay { get; init; }
    public string? ValueKind { get; init; }
    public double? LatestNumeric { get; init; }
    public bool? ActuatorOn { get; init; }
    public bool? CommandedActuatorOn { get; init; }
    public bool? ReportedActuatorOn { get; init; }
    public DateTimeOffset? LastSeenUtc { get; init; }
    public bool HasActiveAlert { get; init; }
    public string Unit { get; init; } = string.Empty;
}

public sealed class ProcessingJournalEntry
{
    public DateTimeOffset AtUtc { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class ProcessingStatsDto
{
    public int QueueCapacity { get; init; }
    public int PendingCount { get; init; }
    public int DequeuedCount { get; init; }
    public int ProcessedCount { get; init; }
    public int RejectedCount { get; init; }
    public int ProcessingErrors { get; init; }
    public int DeadLetteredCount { get; init; }
    public int PendingAlerts { get; init; }
    public int ActiveFaults { get; init; }
    public int HistoryCount { get; init; }
}

public sealed class DeadLetterItem
{
    public string DeviceId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public int AttemptCount { get; init; }
    public string Error { get; init; } = string.Empty;
    public DateTimeOffset FailedAtUtc { get; init; }
}

public sealed class QueueFullException : InvalidOperationException
{
    public QueueFullException()
        : base("The telemetry processing queue is full. The packet was not accepted.")
    {
    }
}
