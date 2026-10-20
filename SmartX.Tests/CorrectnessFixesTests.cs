using SmartX.Api.Services;
using SmartX.Shared.Commands;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Tests;

public sealed class CorrectnessFixesTests
{
    [Fact]
    public void Newer_live_state_is_not_overwritten_by_older_telemetry()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        var newer = DateTimeOffset.UnixEpoch.AddHours(2);
        var older = DateTimeOffset.UnixEpoch.AddHours(1);

        h.Devices.ApplyTelemetry(h.Item(TestMacs.Moisture, 40, at: newer, sequence: 1));
        h.Devices.ApplyTelemetry(h.Item(TestMacs.Moisture, 10, at: older, sequence: 2));

        Assert.True(h.Devices.TryGet(TestMacs.Moisture, out var live));
        Assert.Equal("40.00", live.LatestDisplay);
        Assert.Equal(newer, live.LastSeenUtc);
    }

    [Fact]
    public void Older_readings_remain_in_timestamp_ordered_history()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        var newer = DateTimeOffset.UnixEpoch.AddHours(2);
        var older = DateTimeOffset.UnixEpoch.AddHours(1);

        Assert.True(h.Pending.TryEnqueue(h.Item(TestMacs.Moisture, 40, at: newer), out _));
        Assert.True(h.Pending.TryEnqueue(h.Item(TestMacs.Moisture, 11, at: older), out _));
        h.Engine.Drain();

        var history = h.History.Latest(10, TestMacs.Moisture);
        Assert.Equal(2, history.Count);
        Assert.Equal(newer, history[0].TimestampUtc);
        Assert.Equal("40", history[0].DisplayValue);
        Assert.Equal(older, history[1].TimestampUtc);
        Assert.Equal("11", history[1].DisplayValue);
        Assert.True(h.Devices.TryGet(TestMacs.Moisture, out var live));
        Assert.Equal("40.00", live.LatestDisplay);
        Assert.Equal(newer, live.LastSeenUtc);
    }

    [Fact]
    public void Last_seen_never_moves_backwards()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        var t1 = DateTimeOffset.UnixEpoch.AddMinutes(30);
        var t0 = DateTimeOffset.UnixEpoch.AddMinutes(5);
        h.Devices.ApplyTelemetry(h.Item(TestMacs.Moisture, 50, at: t1, sequence: 1));
        var seen = h.Devices.TryGet(TestMacs.Moisture, out var afterNew);
        Assert.True(seen);
        h.Devices.ApplyTelemetry(h.Item(TestMacs.Moisture, 1, at: t0, sequence: 2));
        Assert.True(h.Devices.TryGet(TestMacs.Moisture, out var afterOld));
        Assert.Equal(t1, afterNew.LastSeenUtc);
        Assert.Equal(t1, afterOld.LastSeenUtc);
    }

    [Fact]
    public void Manual_command_is_not_overwritten_by_older_queued_telemetry()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Valve, SensorCategory.Actuator, "solenoid", "Facility A -> Zone 1 -> Sub-Zone B");
        var older = DateTimeOffset.UnixEpoch.AddHours(1);
        Assert.True(h.Pending.TryEnqueue(new TelemetryWorkItem
        {
            DeviceId = TestMacs.Valve,
            TimestampUtc = older,
            ValueKind = "bool",
            BooleanValue = false,
            MetricName = "actuator_state",
            Unit = "state",
            KnownCategory = SensorCategory.Actuator
        }, out _));

        var applied = h.Commands.Apply(new CommandRequest { MacAddress = TestMacs.Valve, ActuatorOn = true, Note = "open" });
        Assert.True(applied.Accepted);
        h.Engine.Drain();

        Assert.True(h.Devices.TryGet(TestMacs.Valve, out var live));
        Assert.True(live.CommandedActuatorOn);
        Assert.True(live.ActuatorOn);
        Assert.Null(live.ReportedActuatorOn);
        Assert.True(live.LastSeenUtc >= applied.Command!.AppliedAtUtc);
        Assert.Equal(2, h.History.Count);
        Assert.Contains(h.History.Latest(5, TestMacs.Valve), r => r.DisplayValue == "OFF");
        Assert.Contains(h.History.Latest(5, TestMacs.Valve), r => r.DisplayValue == "ON");
    }

    [Fact]
    public void Equal_timestamps_use_processing_sequence()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        var stamp = DateTimeOffset.UnixEpoch.AddHours(4);
        h.Devices.ApplyTelemetry(h.Item(TestMacs.Moisture, 10, at: stamp, sequence: 1));
        h.Devices.ApplyTelemetry(h.Item(TestMacs.Moisture, 20, at: stamp, sequence: 2));
        Assert.True(h.Devices.TryGet(TestMacs.Moisture, out var live));
        Assert.Equal("20.00", live.LatestDisplay);
        Assert.Equal(stamp, live.LastSeenUtc);

        h.Devices.ApplyTelemetry(h.Item(TestMacs.Moisture, 1, at: stamp, sequence: 2));
        Assert.True(h.Devices.TryGet(TestMacs.Moisture, out var sameSeq));
        Assert.Equal("1.00", sameSeq.LatestDisplay);

        h.Devices.ApplyTelemetry(h.Item(TestMacs.Moisture, 99, at: stamp, sequence: 1));
        Assert.True(h.Devices.TryGet(TestMacs.Moisture, out var olderSeq));
        Assert.Equal("1.00", olderSeq.LatestDisplay);
    }

    [Fact]
    public void Failed_item_is_not_counted_as_processed_and_is_dead_lettered()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        h.Engine.BeforeApplyTelemetry = _ => throw new InvalidOperationException("synthetic failure");
        Assert.True(h.Pending.TryEnqueue(h.Item(TestMacs.Moisture, 33), out _));

        h.Engine.Drain();

        Assert.Equal(0, h.Pending.ProcessedCount);
        Assert.Equal(0, h.Pending.PendingCount);
        Assert.Equal(2, h.Pending.DequeuedCount);
        Assert.Equal(2, h.Engine.ProcessingErrors);
        Assert.Equal(1, h.Engine.DeadLetteredCount);
        Assert.Equal(0, h.History.Count);
        Assert.Contains("synthetic failure", h.Engine.DeadLetters()[0].Error);
        Assert.Equal(0, h.Engine.Stats().ProcessedCount);
        Assert.Equal(2, h.Engine.Stats().ProcessingErrors);
    }

    [Fact]
    public void Overlapping_undos_cannot_consume_the_same_record()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Valve, SensorCategory.Actuator, "solenoid", "Facility A -> Zone 1 -> Sub-Zone B");
        Assert.True(h.Commands.Apply(new CommandRequest { MacAddress = TestMacs.Valve, ActuatorOn = true }).Accepted);

        var barrier = new Barrier(2);
        var results = new CommandResult[2];
        Parallel.For(0, 2, i =>
        {
            barrier.SignalAndWait();
            results[i] = h.Commands.Undo();
        });

        Assert.Equal(1, results.Count(r => r.Accepted));
        Assert.Equal(1, results.Count(r => !r.Accepted));
        Assert.Equal(0, h.Undo.UndoDepth);
        Assert.True(h.Devices.TryGet(TestMacs.Valve, out var live));
        Assert.False(live.CommandedActuatorOn);
        Assert.False(live.ActuatorOn);
    }

    [Fact]
    public async Task Worker_drains_accepted_items_on_graceful_shutdown()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        Assert.True(h.Pending.TryEnqueue(h.Item(TestMacs.Moisture, 41, at: DateTimeOffset.UnixEpoch.AddHours(3)), out _));
        Assert.True(h.Pending.TryEnqueue(h.Item(TestMacs.Moisture, 42, at: DateTimeOffset.UnixEpoch.AddHours(4)), out _));

        var worker = h.CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, h.Pending.PendingCount);
        Assert.Equal(2, h.Pending.ProcessedCount);
        Assert.Equal(2, h.History.Count);
    }

    [Fact]
    public void Cancelled_drain_leaves_remaining_in_memory_items()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        Assert.True(h.Pending.TryEnqueue(h.Item(TestMacs.Moisture, 41), out _));
        Assert.True(h.Pending.TryEnqueue(h.Item(TestMacs.Moisture, 42), out _));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var n = h.Engine.Drain(100, cts.Token);

        Assert.Equal(0, n);
        Assert.Equal(2, h.Pending.PendingCount);
        Assert.Equal(0, h.Pending.ProcessedCount);
    }
}
