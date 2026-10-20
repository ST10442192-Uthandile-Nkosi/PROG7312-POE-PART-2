using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Api.Services;

/// <summary>
/// Same bands as GatewayIntegrityService: VWC 28–82%, clamp 2800 W, feeder 3500 W.
/// </summary>
public sealed class ThresholdEvaluator
{
    public const double VwcLow = 28;
    public const double VwcHigh = 82;
    public const int PowerWarningWatts = 2800;
    public const int FeederLimitWatts = 3500;

    public IReadOnlyList<AlertWorkItem> EvaluatePacket(TelemetryWorkItem item, SensorCategory? category)
    {
        var alerts = new List<AlertWorkItem>();
        var mac = SensorRegistry.NormalizeMac(item.DeviceId);
        var at = item.TimestampUtc.ToUniversalTime();

        if (category == SensorCategory.Environmental &&
            item.Unit.Contains("VWC", StringComparison.OrdinalIgnoreCase) &&
            item.NumericValue is { } vwc)
        {
            if (vwc < VwcLow)
            {
                alerts.Add(Alert("VWC-LOW", mac, AlertPriority.Critical, $"VWC {vwc:0.0}% below {VwcLow}%.", at));
            }
            else if (vwc > VwcHigh)
            {
                alerts.Add(Alert("VWC-HIGH", mac, AlertPriority.Critical, $"VWC {vwc:0.0}% above {VwcHigh}%.", at));
            }
        }

        if (category == SensorCategory.PowerConsumption && item.NumericValue is { } watts)
        {
            if (watts > FeederLimitWatts)
            {
                alerts.Add(Alert("POWER-CRITICAL", mac, AlertPriority.Critical, $"Clamp {watts:0} W over {FeederLimitWatts} W.", at));
            }
            else if (watts > PowerWarningWatts)
            {
                alerts.Add(Alert("POWER-HIGH", mac, AlertPriority.Warning, $"Clamp {watts:0} W approaching saturation.", at));
            }
        }

        return alerts;
    }

    public IReadOnlyList<AlertWorkItem> EvaluateCoupling(IReadOnlyList<LiveDeviceState> snapshot)
    {
        var alerts = new List<AlertWorkItem>();
        var now = DateTimeOffset.UtcNow;
        foreach (var bed in snapshot.Where(s => s.Category == SensorCategory.Environmental))
        {
            if (bed.LatestNumeric is null || bed.LatestNumeric >= VwcLow) continue;
            if (!bed.Unit.Contains("VWC", StringComparison.OrdinalIgnoreCase)) continue;

            var valve = snapshot.FirstOrDefault(v =>
                v.Category == SensorCategory.Actuator && SharesDeploymentZone(bed.Location, v.Location));
            if (valve is { ActuatorOn: false })
            {
                alerts.Add(Alert("IRR-DEADLOCK", bed.MacAddress, AlertPriority.Critical,
                    $"{bed.DisplayName} is dry and {valve.DisplayName} is OFF.", now));
            }
        }

        var meters = snapshot.Where(s => s.Category == SensorCategory.PowerConsumption).ToList();
        if (meters.Count >= 2 && meters[0].LatestNumeric is { } a && meters[1].LatestNumeric is { } b)
        {
            var combined = a + b;
            if (combined > FeederLimitWatts)
            {
                alerts.Add(Alert("GRID-OVERLOAD", meters[0].MacAddress, AlertPriority.Critical,
                    $"Feeder {combined:0} W exceeds {FeederLimitWatts} W.", now));
            }
        }

        return alerts;
    }

    public IEnumerable<string> HealthyCodes(TelemetryWorkItem item, SensorCategory? category)
    {
        if (category == SensorCategory.Environmental &&
            item.Unit.Contains("VWC", StringComparison.OrdinalIgnoreCase) &&
            item.NumericValue is { } vwc &&
            vwc is >= VwcLow and <= VwcHigh)
        {
            yield return "VWC-LOW";
            yield return "VWC-HIGH";
            yield return "IRR-DEADLOCK";
        }

        if (category == SensorCategory.PowerConsumption &&
            item.NumericValue is { } w &&
            w <= PowerWarningWatts)
        {
            yield return "POWER-HIGH";
            yield return "POWER-CRITICAL";
        }
    }

    private static AlertWorkItem Alert(string code, string mac, int priority, string detail, DateTimeOffset at) => new()
    {
        Priority = priority,
        FaultId = ActiveFaultSet.Key(code, mac),
        Code = code,
        DeviceId = mac,
        Detail = detail,
        RaisedAtUtc = at
    };

    public static bool SharesDeploymentZone(string left, string right)
    {
        var l = left.Split("->", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var r = right.Split("->", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return l.Length >= 2 && r.Length >= 2 && l[0] == r[0] && l[1] == r[1];
    }
}
