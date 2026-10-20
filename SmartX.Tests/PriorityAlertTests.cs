using SmartX.Api.Services;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Tests;

public sealed class PriorityAlertTests
{
    [Fact]
    public void Critical_alert_is_processed_before_pending_fifo_telemetry()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");

        Assert.True(h.Pending.TryEnqueue(h.Item(TestMacs.Moisture, 40), out _));
        Assert.True(h.Pending.TryEnqueue(h.Item(TestMacs.Moisture, 41), out _));
        h.Alerts.Enqueue(new AlertWorkItem
        {
            Priority = AlertPriority.Critical,
            FaultId = ActiveFaultSet.Key("VWC-LOW", TestMacs.Moisture),
            Code = "VWC-LOW",
            DeviceId = TestMacs.Moisture,
            Detail = "dry",
            RaisedAtUtc = DateTimeOffset.UnixEpoch
        });

        h.Engine.Drain();
        var kinds = h.Journal.Snapshot().Select(e => e.Kind).ToList();
        var firstAlert = kinds.IndexOf("ALERT");
        var firstTelemetry = kinds.IndexOf("TELEMETRY");
        Assert.True(firstAlert >= 0 && firstTelemetry >= 0);
        Assert.True(firstAlert < firstTelemetry);
        Assert.Equal(0, h.Pending.PendingCount);
        Assert.Equal(2, h.Pending.ProcessedCount);
    }

    [Fact]
    public void Equal_priority_alerts_keep_enqueue_order()
    {
        var h = RuntimeHarness.Create();
        h.Alerts.Enqueue(Warning("first"));
        h.Alerts.Enqueue(Warning("second"));
        h.Alerts.Enqueue(Warning("third"));

        Assert.True(h.Alerts.TryDequeue(out var a));
        Assert.True(h.Alerts.TryDequeue(out var b));
        Assert.True(h.Alerts.TryDequeue(out var c));
        Assert.Equal("first", a!.DeviceId);
        Assert.Equal("second", b!.DeviceId);
        Assert.Equal("third", c!.DeviceId);
    }

    [Fact]
    public void Critical_dequeues_ahead_of_warning()
    {
        var h = RuntimeHarness.Create();
        h.Alerts.Enqueue(new AlertWorkItem
        {
            Priority = AlertPriority.Warning,
            Code = "POWER-HIGH",
            DeviceId = "warn",
            FaultId = "POWER-HIGH:warn",
            RaisedAtUtc = DateTimeOffset.UnixEpoch
        });
        h.Alerts.Enqueue(new AlertWorkItem
        {
            Priority = AlertPriority.Critical,
            Code = "POWER-CRITICAL",
            DeviceId = "crit",
            FaultId = "POWER-CRITICAL:crit",
            RaisedAtUtc = DateTimeOffset.UnixEpoch
        });

        Assert.True(h.Alerts.TryDequeue(out var first));
        Assert.Equal("crit", first!.DeviceId);
        Assert.True(h.Alerts.TryDequeue(out var second));
        Assert.Equal("warn", second!.DeviceId);
    }

    private static AlertWorkItem Warning(string device) => new()
    {
        Priority = AlertPriority.Warning,
        Code = "POWER-HIGH",
        DeviceId = device,
        FaultId = $"POWER-HIGH:{device}",
        RaisedAtUtc = DateTimeOffset.UnixEpoch
    };
}
