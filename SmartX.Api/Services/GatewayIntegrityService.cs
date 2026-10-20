using SmartX.Shared.Sensors;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Services;

public sealed class GatewayIntegrityService
{
    private readonly SensorRegistry _sensors;
    private readonly TelemetryIngestionService _telemetry;

    public GatewayIntegrityService(SensorRegistry sensors, TelemetryIngestionService telemetry)
    {
        _sensors = sensors;
        _telemetry = telemetry;
    }

    public GatewayIntegrityReport Build()
    {
        var sensors = _sensors.All();
        var nodes = new List<MeshNodeView>();
        var i = 0;
        foreach (var sensor in sensors)
        {
            var angle = (i / (double)Math.Max(sensors.Count, 1)) * Math.PI * 2;
            var radius = sensor.Category switch
            {
                SensorCategory.Actuator => 78,
                SensorCategory.PowerConsumption => 58,
                _ => 38
            };

            _telemetry.TryGetLatest(sensor.MacAddress, out var latest);
            var status = Classify(sensor, latest);
            nodes.Add(new MeshNodeView
            {
                DeviceId = sensor.MacAddress,
                DisplayName = sensor.DisplayName,
                Category = sensor.Category,
                Location = sensor.Location,
                X = 120 + Math.Cos(angle) * radius,
                Y = 120 + Math.Sin(angle) * radius,
                Status = status.status,
                LatestValue = latest?.DisplayValue ?? "awaiting",
                Alert = status.alert
            });
            i++;
        }

        var alarms = _telemetry.DetectCouplingAlarms(sensors);
        var streak = _telemetry.ValidPacketStreak;
        var fractured = nodes.Any(n => n.Status == "fracture");
        var rank = alarms.Count > 0 || fractured ? "Alert" : streak >= 80 ? "Stable" : "Watch";
        return new GatewayIntegrityReport
        {
            Nodes = nodes,
            Alarms = alarms,
            StewardRank = rank,
            ValidPacketStreak = streak,
            TotalPackets = _telemetry.TotalPackets,
            FractureCount = nodes.Count(n => n.Status == "fracture"),
            Narrative = alarms.Count == 0 && !fractured
                ? "All nodes inside safe bands."
                : "Check highlighted nodes."
        };
    }

    private static (string status, string alert) Classify(SensorProfile sensor, TelemetrySnapshot? latest)
    {
        if (latest is null) return ("silent", "No packet since last gateway sweep.");

        if (sensor.Category == SensorCategory.Environmental &&
            double.TryParse(latest.DisplayValue, out var vwc) &&
            latest.Unit.Contains("VWC", StringComparison.OrdinalIgnoreCase) &&
            (vwc < 28 || vwc > 82))
        {
            return ("fracture", $"VWC {vwc:0.0}% outside hydroponic comfort band 28–82%.");
        }

        if (sensor.Category == SensorCategory.PowerConsumption &&
            int.TryParse(latest.DisplayValue, out var watts) && watts > 2800)
        {
            return ("fracture", $"Clamp {watts} W approaching CT saturation.");
        }

        return ("nominal", string.Empty);
    }
}
