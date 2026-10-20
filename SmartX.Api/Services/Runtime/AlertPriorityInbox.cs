using SmartX.Shared.Runtime;

namespace SmartX.Api.Services;

/// <summary>
/// Critical alerts processed before ordinary FIFO telemetry.
/// Priority is (level, sequence): lower level first; equal level keeps enqueue order.
/// </summary>
public sealed class AlertPriorityInbox
{
    private readonly PriorityQueue<AlertWorkItem, (int Priority, long Sequence)> _queue = new();
    private readonly object _gate = new();
    private long _sequence;
    private int _processed;

    public int PendingCount
    {
        get { lock (_gate) return _queue.Count; }
    }

    public int ProcessedCount => Volatile.Read(ref _processed);

    public void Enqueue(AlertWorkItem alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        lock (_gate)
        {
            var sequence = ++_sequence;
            var item = new AlertWorkItem
            {
                Sequence = sequence,
                Priority = alert.Priority,
                FaultId = alert.FaultId,
                Code = alert.Code,
                DeviceId = alert.DeviceId,
                Detail = alert.Detail,
                RaisedAtUtc = alert.RaisedAtUtc.ToUniversalTime()
            };
            _queue.Enqueue(item, (item.Priority, sequence));
        }
    }

    public bool TryDequeue(out AlertWorkItem? alert)
    {
        lock (_gate)
        {
            if (_queue.TryDequeue(out var item, out _))
            {
                _processed++;
                alert = item;
                return true;
            }

            alert = null;
            return false;
        }
    }

    public bool HasPending
    {
        get { lock (_gate) return _queue.Count > 0; }
    }
}
