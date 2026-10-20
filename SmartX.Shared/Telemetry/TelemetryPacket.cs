namespace SmartX.Shared.Telemetry;

/// <summary>
/// Generic ESP32-style telemetry envelope. Constraining <typeparamref name="T"/> to a value type
/// keeps soil-moisture floats, wattage integers, and valve booleans on the stack and avoids
/// boxing when packets are stored in typed collections on the gateway.
/// </summary>
public sealed class TelemetryPacket<T> where T : struct // T is float, int, or bool
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public T Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public string RssiDbm { get; set; } = "-70";
}
