using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Tests;

public sealed class DeviceDashboardFilterTests
{
    [Fact]
    public void Search_matches_name_or_mac()
    {
        var devices = Sample();
        var byName = DeviceDashboardFilter.Apply(devices, "solenoid", null, "", "");
        var byMac = DeviceDashboardFilter.Apply(devices, "00:03", null, "", "");
        Assert.Single(byName);
        Assert.Equal("Irrigation solenoid", byName[0].DisplayName);
        Assert.Single(byMac);
        Assert.Equal("Bay clamp", byMac[0].DisplayName);
    }

    [Fact]
    public void Category_and_alert_filters()
    {
        var devices = Sample();
        var actuators = DeviceDashboardFilter.Apply(devices, null, SensorCategory.Actuator, "", "");
        Assert.Equal(2, actuators.Count);
        var alerting = DeviceDashboardFilter.Apply(devices, null, null, DeviceDashboardFilter.StatusAlerting, "");
        Assert.Single(alerting);
        Assert.Equal("NFT moisture", alerting[0].DisplayName);
    }

    [Fact]
    public void Actuator_on_off_and_clear()
    {
        var devices = Sample();
        var on = DeviceDashboardFilter.Apply(devices, null, null, "", DeviceDashboardFilter.ActuatorOn);
        var off = DeviceDashboardFilter.Apply(devices, null, null, "", DeviceDashboardFilter.ActuatorOff);
        var none = DeviceDashboardFilter.Apply(devices, null, null, "", DeviceDashboardFilter.ActuatorNone);
        Assert.Single(on);
        Assert.Equal("Irrigation solenoid", on[0].DisplayName);
        Assert.Single(off);
        Assert.Equal("Feeder breaker", off[0].DisplayName);
        Assert.Equal(2, none.Count);
        Assert.All(none, d => Assert.NotEqual(SensorCategory.Actuator, d.Category));
    }

    [Fact]
    public void Split_fault_key_keeps_mac_colons()
    {
        Assert.True(DeviceDashboardFilter.TrySplitFaultKey("VWC-LOW:24:6F:28:AA:00:01", out var code, out var mac));
        Assert.Equal("VWC-LOW", code);
        Assert.Equal("24:6F:28:AA:00:01", mac);
        Assert.False(DeviceDashboardFilter.TrySplitFaultKey("bad", out _, out _));
    }

    private static List<LiveDeviceState> Sample() =>
    [
        new()
        {
            MacAddress = "24:6F:28:AA:00:01",
            DisplayName = "NFT moisture",
            Category = SensorCategory.Environmental,
            LastSeenUtc = DateTimeOffset.UnixEpoch,
            HasActiveAlert = true
        },
        new()
        {
            MacAddress = "24:6F:28:AA:00:02",
            DisplayName = "Irrigation solenoid",
            Category = SensorCategory.Actuator,
            ActuatorOn = true,
            LastSeenUtc = DateTimeOffset.UnixEpoch
        },
        new()
        {
            MacAddress = "30:AE:A4:AA:00:03",
            DisplayName = "Bay clamp",
            Category = SensorCategory.PowerConsumption,
            LastSeenUtc = DateTimeOffset.UnixEpoch
        },
        new()
        {
            MacAddress = "30:AE:A4:AA:00:04",
            DisplayName = "Feeder breaker",
            Category = SensorCategory.Actuator,
            ActuatorOn = false
        }
    ];
}
