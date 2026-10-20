using SmartX.Shared.Sensors;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Services;

/// <summary>
/// Seeds a South African mesh: Stellenbosch hydroponics, Midrand smart-grid, Umhlanga utilities.
/// </summary>
public sealed class MeshSeedHostedService : IHostedService
{
    private readonly SensorRegistry _sensors;
    private readonly TelemetryIngestionService _telemetry;
    private readonly TelemetryBatchProcessor _batches;

    public MeshSeedHostedService(
        SensorRegistry sensors,
        TelemetryIngestionService telemetry,
        TelemetryBatchProcessor batches)
    {
        _sensors = sensors;
        _telemetry = telemetry;
        _batches = batches;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var devices = new List<SensorProfile>
        {
            Profile("24:6F:28:A1:B2:C3", "NFT-Gutter-A moisture", "Facility A -> Zone 1 -> Sub-Zone B", SensorCategory.Environmental),
            Profile("24:6F:28:A1:B2:C4", "NFT-Gutter-B moisture", "Facility A -> Zone 1 -> Sub-Zone B", SensorCategory.Environmental),
            Profile("24:6F:28:A1:B2:D1", "Irrigation solenoid", "Facility A -> Zone 1 -> Sub-Zone B", SensorCategory.Actuator),
            Profile("24:6F:28:A1:B2:E1", "Reservoir EC probe", "Facility A -> Zone 1 -> Sub-Zone Reservoir", SensorCategory.Environmental),
            Profile("24:6F:28:A1:B2:F1", "Canopy DHT22", "Facility A -> Zone 2 -> Sub-Zone Canopy", SensorCategory.Environmental),
            Profile("30:AE:A4:10:01:01", "Bay-1 CT clamp", "Facility B -> Zone 1 -> Transformer Bay", SensorCategory.PowerConsumption),
            Profile("30:AE:A4:10:01:02", "Bay-2 CT clamp", "Facility B -> Zone 1 -> Transformer Bay", SensorCategory.PowerConsumption),
            Profile("30:AE:A4:10:01:03", "Feeder breaker", "Facility B -> Zone 1 -> Transformer Bay", SensorCategory.Actuator),
            Profile("3C:71:BF:22:10:AA", "Unit 1204 water pulse", "Facility C -> Zone Lobby -> Stack B", SensorCategory.Environmental),
            Profile("3C:71:BF:22:10:AB", "HVAC damper", "Facility C -> Zone Lobby -> Stack B", SensorCategory.Actuator)
        };
        _sensors.Seed(devices);

        var rng = new Random(7321);
        foreach (var device in devices)
        {
            for (var i = 0; i < 80; i++)
            {
                var ts = now.AddMinutes(-i * 3);
                switch (device.Category)
                {
                    case SensorCategory.Environmental:
                        _telemetry.IngestFloat(new TelemetryPacket<float>
                        {
                            DeviceId = device.MacAddress,
                            Timestamp = ts,
                            Value = device.DisplayName.Contains("EC")
                                ? (float)(1.4 + rng.NextDouble() * 0.6)
                                : (float)(35 + rng.NextDouble() * 40),
                            Unit = device.DisplayName.Contains("EC") ? "mS/cm" : "%VWC",
                            MetricName = device.DisplayName.Contains("EC") ? "nutrient_ec" : "soil_moisture"
                        });
                        break;
                    case SensorCategory.PowerConsumption:
                        _telemetry.IngestInt(new TelemetryPacket<int>
                        {
                            DeviceId = device.MacAddress,
                            Timestamp = ts,
                            Value = 900 + rng.Next(0, 1600),
                            Unit = "W",
                            MetricName = "active_power"
                        });
                        break;
                    case SensorCategory.Actuator:
                        _telemetry.IngestBool(new TelemetryPacket<bool>
                        {
                            DeviceId = device.MacAddress,
                            Timestamp = ts,
                            Value = rng.NextDouble() > 0.35,
                            Unit = "state",
                            MetricName = "actuator_state"
                        });
                        break;
                }
            }
        }

        for (var window = 0; window < 8; window++)
        {
            var length = rng.Next(6, 18);
            var burst = Enumerable.Range(0, length).Select(_ => 20 + rng.NextDouble() * 70).ToArray();
            _batches.AddBatch(burst, facilityIndex: window % 3);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static SensorProfile Profile(string mac, string name, string location, SensorCategory category) => new()
    {
        MacAddress = mac,
        DisplayName = name,
        Location = location,
        Category = category,
        RegisteredAt = DateTimeOffset.UtcNow.AddDays(-2)
    };
}
