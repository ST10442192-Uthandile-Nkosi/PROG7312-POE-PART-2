using SmartX.Shared.Runtime;

namespace SmartX.Api.Services;

/// <summary>
/// Bounded FIFO pending queue. Uses Queue&lt;T&gt; under a lock — not ConcurrentQueue.
/// HTTP threads only TryEnqueue; the worker dequeues.
/// ProcessedCount is successful applies only; DequeuedCount is attempted work.
/// </summary>
public sealed class PendingTelemetryQueue
{
    private readonly Queue<TelemetryWorkItem> _queue;
    private readonly object _gate = new();
    private readonly int _capacity;
    private long _sequence;
    private int _rejected;
    private int _processed;
    private int _dequeued;

    public PendingTelemetryQueue(int capacity = 2048)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _queue = new Queue<TelemetryWorkItem>(capacity);
    }

    public int Capacity => _capacity;

    public int PendingCount
    {
        get { lock (_gate) return _queue.Count; }
    }

    public int RejectedCount => Volatile.Read(ref _rejected);
    public int ProcessedCount => Volatile.Read(ref _processed);
    public int DequeuedCount => Volatile.Read(ref _dequeued);

    public bool TryEnqueue(TelemetryWorkItem item, out long sequence)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            if (_queue.Count >= _capacity)
            {
                _rejected++;
                sequence = 0;
                return false;
            }

            sequence = ++_sequence;
            _queue.Enqueue(Clone(item, sequence, item.AttemptCount));
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public bool TryDequeue(out TelemetryWorkItem? item)
    {
        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                item = null;
                return false;
            }

            item = _queue.Dequeue();
            _dequeued++;
            return true;
        }
    }

    public void MarkSucceeded() => Interlocked.Increment(ref _processed);

    public bool WaitForWork(int millisecondsTimeout, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_queue.Count > 0)
            {
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Monitor.Wait(_gate, millisecondsTimeout);
            return _queue.Count > 0;
        }
    }

    public void Pulse()
    {
        lock (_gate)
        {
            Monitor.PulseAll(_gate);
        }
    }

    public static TelemetryWorkItem Clone(TelemetryWorkItem item, long sequence, int attemptCount) => new()
    {
        Sequence = sequence,
        DeviceId = item.DeviceId,
        TimestampUtc = item.TimestampUtc.ToUniversalTime(),
        ValueKind = item.ValueKind,
        NumericValue = item.NumericValue,
        BooleanValue = item.BooleanValue,
        MetricName = item.MetricName,
        Unit = item.Unit,
        KnownCategory = item.KnownCategory,
        AttemptCount = attemptCount
    };
}
