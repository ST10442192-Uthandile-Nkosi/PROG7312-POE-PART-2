using SmartX.Shared.Commands;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Tests;

public sealed class CommandAndIngestionTests
{
    [Fact]
    public void Undo_restores_recorded_previous_actuator_state()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Valve, SensorCategory.Actuator, "solenoid", "Facility A -> Zone 1 -> Sub-Zone B");
        h.Devices.EnsureRegistered();

        var on = h.Commands.Apply(new CommandRequest { MacAddress = TestMacs.Valve, ActuatorOn = true, Note = "open" });
        Assert.True(on.Accepted);
        Assert.False(on.Command!.PreviousActuatorOn);
        Assert.True(on.Command.ResultingActuatorOn);

        var off = h.Commands.Apply(new CommandRequest { MacAddress = TestMacs.Valve, ActuatorOn = false, Note = "close" });
        Assert.True(off.Accepted);
        Assert.True(off.Command!.PreviousActuatorOn);
        Assert.False(off.Command.ResultingActuatorOn);

        var undo = h.Commands.Undo();
        Assert.True(undo.Accepted);
        Assert.True(h.Devices.TryGet(TestMacs.Valve, out var live));
        Assert.True(live.ActuatorOn);
        Assert.Equal(true, h.Ingestion.LatestBool(TestMacs.Valve));
        Assert.Equal(1, h.Undo.UndoDepth);
    }

    [Fact]
    public void Rejected_commands_do_not_enter_the_undo_stack()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");

        var unknown = h.Commands.Apply(new CommandRequest { MacAddress = "00:00:00:00:00:99", ActuatorOn = true });
        var notActuator = h.Commands.Apply(new CommandRequest { MacAddress = TestMacs.Moisture, ActuatorOn = true });
        var empty = h.Commands.Undo();

        Assert.False(unknown.Accepted);
        Assert.False(notActuator.Accepted);
        Assert.False(empty.Accepted);
        Assert.Equal(0, h.Undo.UndoDepth);
        Assert.Empty(h.Commands.History());
    }

    [Fact]
    public void Stale_device_state_blocks_undo_without_dropping_the_stack()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Valve, SensorCategory.Actuator, "solenoid", "Facility A -> Zone 1 -> Sub-Zone B");
        Assert.True(h.Commands.Apply(new CommandRequest { MacAddress = TestMacs.Valve, ActuatorOn = true }).Accepted);
        h.Devices.SetActuator(TestMacs.Valve, false, DateTimeOffset.UtcNow);

        var undo = h.Commands.Undo();
        Assert.False(undo.Accepted);
        Assert.Equal(1, h.Undo.UndoDepth);
    }

    [Fact]
    public void Part1_ingest_still_fills_the_ingestion_buffer()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        var packet = h.Ingestion.IngestFloat(TestPackets.Float(TestMacs.Moisture, 44.5f));

        Assert.Equal(44.5f, packet.Value);
        Assert.Equal(1, h.Ingestion.TotalPackets);
        Assert.Equal(1, h.Pending.PendingCount);
        var recent = h.Ingestion.Recent(5);
        Assert.Contains(recent, s => s.DeviceId == TestMacs.Moisture && s.DisplayValue.StartsWith("44.50"));
        Assert.Equal(44.5f, h.Ingestion.LatestFloat(TestMacs.Moisture));
    }

    [Fact]
    public void Ingest_throws_when_the_processing_queue_is_full()
    {
        var h = RuntimeHarness.Create(queueCapacity: 1);
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        h.Ingestion.IngestFloat(TestPackets.Float(TestMacs.Moisture, 40f));
        Assert.Throws<QueueFullException>(() => h.Ingestion.IngestFloat(TestPackets.Float(TestMacs.Moisture, 41f)));
        Assert.Equal(1, h.Pending.RejectedCount);
        Assert.Equal(1, h.Ingestion.TotalPackets);
    }

    [Fact]
    public async Task Worker_stops_when_cancellation_is_requested()
    {
        var h = RuntimeHarness.Create();
        var worker = h.CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        var stop = worker.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(stop, completed);
        await stop;
    }
}
