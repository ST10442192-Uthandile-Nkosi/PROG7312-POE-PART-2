using SmartX.Api.Services;
using SmartX.Shared.Runtime;

namespace SmartX.Tests;

public sealed class PendingQueueTests
{
    [Fact]
    public void Fifo_order_is_preserved()
    {
        var queue = new PendingTelemetryQueue(8);
        Assert.True(queue.TryEnqueue(Work("A"), out _));
        Assert.True(queue.TryEnqueue(Work("B"), out _));
        Assert.True(queue.TryEnqueue(Work("C"), out _));

        Assert.True(queue.TryDequeue(out var first));
        Assert.True(queue.TryDequeue(out var second));
        Assert.True(queue.TryDequeue(out var third));

        Assert.Equal("A", first!.DeviceId);
        Assert.Equal("B", second!.DeviceId);
        Assert.Equal("C", third!.DeviceId);
        Assert.Equal(3, queue.DequeuedCount);
        Assert.Equal(0, queue.ProcessedCount);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Full_queue_rejects_without_discarding_existing_items()
    {
        var queue = new PendingTelemetryQueue(2);
        Assert.True(queue.TryEnqueue(Work("KEEP-1"), out _));
        Assert.True(queue.TryEnqueue(Work("KEEP-2"), out _));
        Assert.False(queue.TryEnqueue(Work("REJECT"), out _));

        Assert.Equal(1, queue.RejectedCount);
        Assert.Equal(2, queue.PendingCount);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal("KEEP-1", first!.DeviceId);
        Assert.True(queue.TryDequeue(out var second));
        Assert.Equal("KEEP-2", second!.DeviceId);
    }

    [Fact]
    public void Concurrent_enqueue_never_exceeds_capacity()
    {
        var queue = new PendingTelemetryQueue(50);
        Parallel.For(0, 200, i => queue.TryEnqueue(Work($"D{i:000}"), out _));

        Assert.Equal(50, queue.PendingCount);
        Assert.Equal(150, queue.RejectedCount);
        Assert.True(queue.PendingCount + queue.RejectedCount == 200);
    }

    private static TelemetryWorkItem Work(string id) => new()
    {
        DeviceId = id,
        TimestampUtc = DateTimeOffset.UnixEpoch,
        ValueKind = "float",
        NumericValue = 1
    };
}
