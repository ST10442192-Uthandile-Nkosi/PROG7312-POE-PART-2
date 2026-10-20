using SmartX.Api.Services;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Tests;

public sealed class DeviceHistoryFaultTests
{
    [Fact]
    public void Dictionary_lookup_uses_normalised_mac()
    {
        var h = RuntimeHarness.Create();
        h.Register("24-6f-28-aa-00-01", SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        h.Devices.EnsureRegistered();

        Assert.True(h.Devices.TryGet("24:6F:28:AA:00:01", out var colon));
        Assert.True(h.Devices.TryGet("24-6f-28-aa-00-01", out var hyphen));
        Assert.Equal("24:6F:28:AA:00:01", colon.MacAddress);
        Assert.Equal(colon.MacAddress, hyphen.MacAddress);

        h.Devices.ApplyTelemetry(h.Item("24:6f:28:aa:00:01", 55));
        Assert.True(h.Devices.TryGet("24-6F-28-AA-00-01", out var after));
        Assert.Equal("55.00", after.LatestDisplay);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddHours(1), after.LastSeenUtc);
    }

    [Fact]
    public void History_orders_by_timestamp_then_sequence()
    {
        var store = new TelemetryHistoryStore(8);
        var stamp = DateTimeOffset.UnixEpoch.AddHours(3);
        store.Add(Reading("AAA", stamp, 10));
        store.Add(Reading("BBB", stamp.AddMinutes(1), 11));
        store.Add(Reading("CCC", stamp, 12));

        var latest = store.Latest(10);
        Assert.Equal(new[] { "BBB", "CCC", "AAA" }, latest.Select(r => r.DeviceId).ToArray());
        Assert.All(latest, r => Assert.Equal(DateTimeKind.Utc, r.TimestampUtc.UtcDateTime.Kind));
    }

    [Fact]
    public void History_bounds_retained_entries()
    {
        var store = new TelemetryHistoryStore(3);
        var t = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < 6; i++)
        {
            store.Add(Reading($"D{i}", t.AddSeconds(i), i));
        }

        Assert.Equal(3, store.Count);
        Assert.Equal(new[] { "D5", "D4", "D3" }, store.Latest(10).Select(r => r.DeviceId).ToArray());
    }

    [Fact]
    public void HashSet_prevents_duplicate_active_faults_and_allows_resolve()
    {
        var faults = new ActiveFaultSet();
        Assert.True(faults.Raise("VWC-LOW", "24-6f-28-aa-00-01"));
        Assert.False(faults.Raise("VWC-LOW", "24:6F:28:AA:00:01"));
        Assert.Equal(1, faults.Count);
        Assert.True(faults.Contains("VWC-LOW", "24:6F:28:AA:00:01"));

        Assert.True(faults.Resolve("VWC-LOW", "24:6F:28:AA:00:01"));
        Assert.False(faults.Contains("VWC-LOW", "24:6F:28:AA:00:01"));
        Assert.Equal(0, faults.Count);
    }

    [Fact]
    public void Engine_does_not_raise_the_same_fault_twice()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        h.Ingestion.IngestFloat(TestPackets.Float(TestMacs.Moisture, 12f));
        h.Ingestion.IngestFloat(TestPackets.Float(TestMacs.Moisture, 10f));
        h.Engine.Drain();
        Assert.Single(h.Faults.Snapshot());
        Assert.Contains("VWC-LOW:", h.Faults.Snapshot()[0]);
    }

    private static TelemetryWorkItem Reading(string mac, DateTimeOffset at, double value) => new()
    {
        DeviceId = mac,
        TimestampUtc = at,
        ValueKind = "float",
        NumericValue = value,
        Unit = "%VWC"
    };
}
