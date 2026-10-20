namespace SmartX.Shared.Telemetry;

/// <summary>
/// Smart-meter / CT-clamp reading. Overloaded operators let the gateway aggregate feeder load
/// (Meter3 = Meter1 + Meter2) or compute a delta without ad-hoc arithmetic at each call site.
/// </summary>
public sealed class PowerMetric : IEquatable<PowerMetric>
{
    public double Watts { get; set; }
    public string MeterId { get; set; } = string.Empty;

    // Meter3 = Meter1 + Meter2
    public static PowerMetric operator +(PowerMetric left, PowerMetric right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new PowerMetric
        {
            Watts = left.Watts + right.Watts,
            MeterId = $"{left.MeterId}+{right.MeterId}"
        };
    }

    public static PowerMetric operator -(PowerMetric left, PowerMetric right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new PowerMetric
        {
            Watts = left.Watts - right.Watts,
            MeterId = $"{left.MeterId}-{right.MeterId}"
        };
    }

    public static bool operator >(PowerMetric left, PowerMetric right) => left.Watts > right.Watts;
    public static bool operator <(PowerMetric left, PowerMetric right) => left.Watts < right.Watts;
    public static bool operator >=(PowerMetric left, PowerMetric right) => left.Watts >= right.Watts;
    public static bool operator <=(PowerMetric left, PowerMetric right) => left.Watts <= right.Watts;
    public static bool operator ==(PowerMetric? left, PowerMetric? right) => Equals(left, right);
    public static bool operator !=(PowerMetric? left, PowerMetric? right) => !Equals(left, right);

    public bool Equals(PowerMetric? other) => other is not null && Watts.Equals(other.Watts) && MeterId == other.MeterId;
    public override bool Equals(object? obj) => obj is PowerMetric other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Watts, MeterId);
}
