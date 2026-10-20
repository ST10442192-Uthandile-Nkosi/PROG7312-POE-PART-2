using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Api.Services;

/// <summary>
/// Drains PriorityQueue alerts first, then Queue FIFO. Does not interrupt an item already in Apply*.
/// Failed telemetry is retried once at the tail of the FIFO, then dead-lettered. No unbounded re-enqueue.
/// </summary>
public sealed class TelemetryProcessingEngine
{
    public const int MaxAttempts = 2;
    public const int DeadLetterCapacity = 64;
    public const int ShutdownDrainLimit = 2048;

    private readonly PendingTelemetryQueue _pending;
    private readonly AlertPriorityInbox _alerts;
    private readonly LiveDeviceStore _devices;
    private readonly TelemetryHistoryStore _history;
    private readonly ActiveFaultSet _faults;
    private readonly ProcessingJournal _journal;
    private readonly ThresholdEvaluator _thresholds;
    private readonly SensorRegistry _registry;
    private readonly List<DeadLetterItem> _deadLetters = [];
    private readonly object _deadLetterGate = new();
    private int _errors;

    public TelemetryProcessingEngine(
        PendingTelemetryQueue pending,
        AlertPriorityInbox alerts,
        LiveDeviceStore devices,
        TelemetryHistoryStore history,
        ActiveFaultSet faults,
        ProcessingJournal journal,
        ThresholdEvaluator thresholds,
        SensorRegistry registry)
    {
        _pending = pending;
        _alerts = alerts;
        _devices = devices;
        _history = history;
        _faults = faults;
        _journal = journal;
        _thresholds = thresholds;
        _registry = registry;
    }

    /// <summary>Test seam. Production leaves this null.</summary>
    public Action<TelemetryWorkItem>? BeforeApplyTelemetry { get; set; }

    public int ProcessingErrors => Volatile.Read(ref _errors);

    public ProcessingStatsDto Stats() => new()
    {
        QueueCapacity = _pending.Capacity,
        PendingCount = _pending.PendingCount,
        DequeuedCount = _pending.DequeuedCount,
        ProcessedCount = _pending.ProcessedCount,
        RejectedCount = _pending.RejectedCount,
        ProcessingErrors = ProcessingErrors,
        DeadLetteredCount = DeadLetteredCount,
        PendingAlerts = _alerts.PendingCount,
        ActiveFaults = _faults.Count,
        HistoryCount = _history.Count
    };

    public int DeadLetteredCount
    {
        get { lock (_deadLetterGate) return _deadLetters.Count; }
    }

    public IReadOnlyList<DeadLetterItem> DeadLetters()
    {
        lock (_deadLetterGate)
        {
            return _deadLetters.ToList();
        }
    }

    public bool ProcessOnce()
    {
        _devices.EnsureRegistered();
        if (_alerts.TryDequeue(out var alert) && alert is not null)
        {
            try
            {
                ApplyAlert(alert);
                return true;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errors);
                _journal.Record("ERROR", $"alert:{alert.FaultId}:{ex.GetType().Name}");
                return true;
            }
        }

        if (!_pending.TryDequeue(out var item) || item is null)
        {
            return false;
        }

        try
        {
            BeforeApplyTelemetry?.Invoke(item);
            ApplyTelemetry(item);
            _pending.MarkSucceeded();
            return true;
        }
        catch (Exception ex)
        {
            HandleFailedTelemetry(item, ex);
            return true;
        }
    }

    public int Drain(int maxSteps = 10_000, CancellationToken cancellationToken = default)
    {
        var n = 0;
        while (n < maxSteps && !cancellationToken.IsCancellationRequested && ProcessOnce())
        {
            n++;
        }

        return n;
    }

    public void EnqueueImmediateAlerts(TelemetryWorkItem item)
    {
        var category = item.KnownCategory ?? _registry.Get(item.DeviceId)?.Category;
        foreach (var alert in _thresholds.EvaluatePacket(item, category))
        {
            _alerts.Enqueue(alert);
        }
    }

    private void HandleFailedTelemetry(TelemetryWorkItem item, Exception ex)
    {
        Interlocked.Increment(ref _errors);
        _journal.Record("ERROR", $"{item.Sequence}:{SensorRegistry.NormalizeMac(item.DeviceId)}:{ex.GetType().Name}");

        if (item.AttemptCount + 1 < MaxAttempts)
        {
            var retry = PendingTelemetryQueue.Clone(item, 0, item.AttemptCount + 1);
            if (_pending.TryEnqueue(retry, out _))
            {
                return;
            }
        }

        DeadLetter(item, ex.Message);
    }

    private void DeadLetter(TelemetryWorkItem item, string error)
    {
        var entry = new DeadLetterItem
        {
            DeviceId = SensorRegistry.NormalizeMac(item.DeviceId),
            Sequence = item.Sequence,
            AttemptCount = item.AttemptCount,
            Error = error,
            FailedAtUtc = DateTimeOffset.UtcNow
        };

        lock (_deadLetterGate)
        {
            _deadLetters.Add(entry);
            while (_deadLetters.Count > DeadLetterCapacity)
            {
                _deadLetters.RemoveAt(0);
            }
        }

        _journal.Record("DEAD", $"{entry.Sequence}:{entry.DeviceId}");
    }

    private void ApplyAlert(AlertWorkItem alert)
    {
        _faults.Raise(alert.Code, alert.DeviceId);
        _devices.SetAlertFlag(alert.DeviceId, true);
        _journal.Record("ALERT", $"{alert.Priority}:{alert.FaultId}:{alert.Sequence}");
    }

    private void ApplyTelemetry(TelemetryWorkItem item)
    {
        var state = _devices.ApplyTelemetry(item, out var appliedToLive);
        _history.Add(item);
        _journal.Record("TELEMETRY", $"{item.Sequence}:{SensorRegistry.NormalizeMac(item.DeviceId)}");

        if (!appliedToLive)
        {
            return;
        }

        var category = item.KnownCategory ?? state.Category;
        foreach (var alert in _thresholds.EvaluatePacket(item, category))
        {
            var isNew = _faults.Raise(alert.Code, alert.DeviceId);
            if (isNew)
            {
                _alerts.Enqueue(alert);
            }
        }

        foreach (var code in _thresholds.HealthyCodes(item, category))
        {
            if (_faults.Resolve(code, item.DeviceId))
            {
                _devices.SetAlertFlag(item.DeviceId, _faults.Snapshot().Any(f =>
                    f.EndsWith(":" + SensorRegistry.NormalizeMac(item.DeviceId), StringComparison.OrdinalIgnoreCase)));
            }
        }

        foreach (var coupling in _thresholds.EvaluateCoupling(_devices.Snapshot()))
        {
            if (_faults.Raise(coupling.Code, coupling.DeviceId))
            {
                _alerts.Enqueue(coupling);
            }
        }
    }
}
