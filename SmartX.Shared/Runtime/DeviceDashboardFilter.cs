using SmartX.Shared.Sensors;

namespace SmartX.Shared.Runtime;

/// <summary>
/// Client-side filter for live device snapshots. Does not call the API.
/// Operational status is derived from LastSeenUtc and HasActiveAlert.
/// </summary>
public static class DeviceDashboardFilter
{
    public const string StatusAll = "";
    public const string StatusSeen = "seen";
    public const string StatusUnseen = "unseen";
    public const string StatusAlerting = "alerting";

    public const string ActuatorAll = "";
    public const string ActuatorOn = "on";
    public const string ActuatorOff = "off";
    public const string ActuatorNone = "none";

    public static IReadOnlyList<LiveDeviceState> Apply(
        IEnumerable<LiveDeviceState> devices,
        string? search,
        SensorCategory? category,
        string? operationalStatus,
        string? actuator)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var query = devices.AsEnumerable();
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(d =>
                d.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.MacAddress.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (category is { } cat)
        {
            query = query.Where(d => d.Category == cat);
        }

        query = operationalStatus switch
        {
            StatusSeen => query.Where(d => d.LastSeenUtc is not null),
            StatusUnseen => query.Where(d => d.LastSeenUtc is null),
            StatusAlerting => query.Where(d => d.HasActiveAlert),
            _ => query
        };

        query = actuator switch
        {
            ActuatorOn => query.Where(d => d.Category == SensorCategory.Actuator && d.ActuatorOn == true),
            ActuatorOff => query.Where(d => d.Category == SensorCategory.Actuator && d.ActuatorOn == false),
            ActuatorNone => query.Where(d => d.Category != SensorCategory.Actuator),
            _ => query
        };

        return query
            .OrderBy(d => d.Location, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TrySplitFaultKey(string faultKey, out string code, out string mac)
    {
        code = "";
        mac = "";
        if (string.IsNullOrWhiteSpace(faultKey))
        {
            return false;
        }

        var split = faultKey.IndexOf(':');
        if (split <= 0 || split >= faultKey.Length - 1)
        {
            return false;
        }

        code = faultKey[..split];
        mac = faultKey[(split + 1)..];
        return true;
    }
}

public sealed class FaultResolveRequest
{
    public string Code { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
}
