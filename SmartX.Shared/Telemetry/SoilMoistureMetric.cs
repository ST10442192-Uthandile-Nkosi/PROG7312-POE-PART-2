namespace SmartX.Shared.Telemetry;

/// <summary>
/// Volumetric water content (VWC) from a capacitive soil probe. Adding two beds yields a
/// combined reservoir load used when blending NFT gutter lines.
/// </summary>
public sealed class SoilMoistureMetric
{
    public double PercentVwc { get; set; }
    public string BedId { get; set; } = string.Empty;

    public static SoilMoistureMetric operator +(SoilMoistureMetric left, SoilMoistureMetric right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new SoilMoistureMetric
        {
            PercentVwc = (left.PercentVwc + right.PercentVwc) / 2.0,
            BedId = $"{left.BedId}|{right.BedId}"
        };
    }

    public static bool operator >(SoilMoistureMetric left, SoilMoistureMetric right) =>
        left.PercentVwc > right.PercentVwc;

    public static bool operator <(SoilMoistureMetric left, SoilMoistureMetric right) =>
        left.PercentVwc < right.PercentVwc;
}
