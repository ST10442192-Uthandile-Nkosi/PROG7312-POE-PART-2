using SmartX.Api.Services;
using SmartX.Shared.Commands;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Tests;

public sealed class RecommendationEngineTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch.AddHours(12);

    [Fact]
    public void Empty_history_produces_no_recommendations()
    {
        var h = RuntimeHarness.Create();
        Assert.Empty(Engine(h).Evaluate(T0));
    }

    [Fact]
    public void Out_of_band_vwc_produces_threshold_insight()
    {
        var h = SeedBed();
        Push(h, TestMacs.Moisture, 12, T0, "%VWC");
        var recs = Engine(h).Evaluate(T0);
        Assert.Contains(recs, r => r.Id == $"threshold:VWC-LOW:{TestMacs.Moisture}" && r.Category == "threshold");
    }

    [Fact]
    public void In_band_vwc_does_not_produce_threshold_insight()
    {
        var h = SeedBed();
        Push(h, TestMacs.Moisture, 50, T0, "%VWC");
        Assert.DoesNotContain(Engine(h).Evaluate(T0), r => r.Category == "threshold");
    }

    [Fact]
    public void Fewer_than_four_readings_do_not_produce_a_trend()
    {
        var h = SeedBed();
        Push(h, TestMacs.Moisture, 50, T0.AddMinutes(-6), "%VWC");
        Push(h, TestMacs.Moisture, 40, T0.AddMinutes(-3), "%VWC");
        Push(h, TestMacs.Moisture, 30, T0, "%VWC");
        Assert.DoesNotContain(Engine(h).Evaluate(T0), r => r.Category == "trend");
    }

    [Fact]
    public void Falling_vwc_trend_is_detected()
    {
        var h = SeedBed();
        Push(h, TestMacs.Moisture, 55, T0.AddMinutes(-9), "%VWC");
        Push(h, TestMacs.Moisture, 48, T0.AddMinutes(-6), "%VWC");
        Push(h, TestMacs.Moisture, 40, T0.AddMinutes(-3), "%VWC");
        Push(h, TestMacs.Moisture, 32, T0, "%VWC");
        var recs = Engine(h).Evaluate(T0);
        Assert.Contains(recs, r => r.Id == $"trend-fall:{TestMacs.Moisture}" && r.EvidenceCount == 4);
    }

    [Fact]
    public void Invalid_and_non_numeric_history_does_not_crash()
    {
        var h = SeedBed();
        h.History.Add(new TelemetryWorkItem
        {
            DeviceId = TestMacs.Moisture,
            TimestampUtc = T0,
            ValueKind = "bool",
            BooleanValue = true,
            Unit = "state"
        });
        h.History.Add(new TelemetryWorkItem
        {
            DeviceId = TestMacs.Moisture,
            TimestampUtc = T0.AddMinutes(1),
            ValueKind = "float",
            Unit = "%VWC"
        });
        var recs = Engine(h).Evaluate(T0.AddMinutes(1));
        Assert.DoesNotContain(recs, r => r.Category == "trend");
    }

    [Fact]
    public void Stale_history_does_not_produce_trend_or_threshold()
    {
        var h = SeedBed();
        var old = T0.AddHours(-10);
        Push(h, TestMacs.Moisture, 10, old, "%VWC");
        Push(h, TestMacs.Moisture, 8, old.AddMinutes(1), "%VWC");
        Push(h, TestMacs.Moisture, 6, old.AddMinutes(2), "%VWC");
        Push(h, TestMacs.Moisture, 4, old.AddMinutes(3), "%VWC");
        var recs = Engine(h).Evaluate(T0);
        Assert.DoesNotContain(recs, r => r.Category is "trend" or "threshold");
    }

    [Fact]
    public void Repeated_evaluation_reuses_the_same_ids()
    {
        var h = SeedBed();
        Push(h, TestMacs.Moisture, 12, T0, "%VWC");
        var engine = Engine(h);
        var first = engine.Evaluate(T0).Select(r => r.Id).OrderBy(x => x).ToArray();
        var second = engine.Evaluate(T0).Select(r => r.Id).OrderBy(x => x).ToArray();
        Assert.Equal(first, second);
        Assert.Equal(first.Length, first.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Unrelated_actuator_is_not_suggested_for_a_dry_bed()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        h.Register(TestMacs.Valve, SensorCategory.Actuator, "foreign valve", "Facility B -> Zone 9 -> Sub-Zone X");
        h.Devices.EnsureRegistered();
        Push(h, TestMacs.Moisture, 10, T0, "%VWC");
        h.Devices.SetActuator(TestMacs.Valve, false, T0);

        var recs = Engine(h).Evaluate(T0);
        Assert.DoesNotContain(recs, r => r.RelatedDeviceId == TestMacs.Valve);
        Assert.Contains(recs, r => r.Id == $"diagnostic-dry:{TestMacs.Moisture}");
        Assert.DoesNotContain(recs, r => r.Category == "coupling");
    }

    [Fact]
    public void Evaluate_does_not_mutate_runtime_stores()
    {
        var h = SeedBedAndValve();
        Push(h, TestMacs.Moisture, 10, T0, "%VWC");
        h.Devices.SetActuator(TestMacs.Valve, false, T0);
        var history = h.History.Count;
        var undo = h.Undo.UndoDepth;
        var faults = h.Faults.Count;
        Assert.True(h.Devices.TryGet(TestMacs.Valve, out var before));

        Engine(h).Evaluate(T0);

        Assert.Equal(history, h.History.Count);
        Assert.Equal(undo, h.Undo.UndoDepth);
        Assert.Equal(faults, h.Faults.Count);
        Assert.True(h.Devices.TryGet(TestMacs.Valve, out var after));
        Assert.Equal(before.ActuatorOn, after.ActuatorOn);
        Assert.Equal(before.CommandedActuatorOn, after.CommandedActuatorOn);
    }

    [Fact]
    public void Zone_paired_dry_bed_and_closed_valve_suggests_a_reviewed_command()
    {
        var h = SeedBedAndValve();
        Push(h, TestMacs.Moisture, 10, T0, "%VWC");
        h.Devices.SetActuator(TestMacs.Valve, false, T0);
        var recs = Engine(h).Evaluate(T0);
        Assert.Contains(recs, r =>
            r.Category == "coupling" &&
            r.RelatedDeviceId == TestMacs.Valve &&
            r.SuggestedAction.Contains("command panel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Active_fault_is_not_repeated_as_a_separate_threshold_insight()
    {
        var h = SeedBed();
        Push(h, TestMacs.Moisture, 12, T0, "%VWC");
        h.Faults.Raise("VWC-LOW", TestMacs.Moisture);
        var recs = Engine(h).Evaluate(T0);
        Assert.DoesNotContain(recs, r => r.Category == "threshold" && r.Id.Contains("VWC-LOW"));
    }

    [Fact]
    public void Three_dry_then_on_commands_produce_a_pattern_insight()
    {
        var h = SeedBedAndValve();
        for (var i = 0; i < 3; i++)
        {
            var at = T0.AddMinutes(i * 10);
            Push(h, TestMacs.Moisture, 15, at, "%VWC");
            h.Undo.RecordApplied(new CommandRecord
            {
                Id = Guid.NewGuid(),
                MacAddress = TestMacs.Valve,
                PreviousActuatorOn = false,
                ResultingActuatorOn = true,
                AppliedAtUtc = at.AddMinutes(1)
            });
        }

        var recs = Engine(h).Evaluate(T0.AddHours(1));
        Assert.Contains(recs, r => r.Id == $"pattern-on-when-dry:{TestMacs.Valve}" && r.EvidenceCount == 3);
    }

    private static RecommendationService Engine(RuntimeHarness h) =>
        new(h.Devices, h.History, h.Faults, h.Undo, h.Thresholds);

    private static RuntimeHarness SeedBed()
    {
        var h = RuntimeHarness.Create();
        h.Register(TestMacs.Moisture, SensorCategory.Environmental, "bed", "Facility A -> Zone 1 -> Sub-Zone B");
        h.Devices.EnsureRegistered();
        return h;
    }

    private static RuntimeHarness SeedBedAndValve()
    {
        var h = SeedBed();
        h.Register(TestMacs.Valve, SensorCategory.Actuator, "solenoid", "Facility A -> Zone 1 -> Sub-Zone B");
        h.Devices.EnsureRegistered();
        return h;
    }

    private static void Push(RuntimeHarness h, string mac, double value, DateTimeOffset at, string unit)
    {
        var item = h.Item(mac, value, unit, "float", at);
        h.Devices.ApplyTelemetry(item);
        h.History.Add(item);
    }
}
