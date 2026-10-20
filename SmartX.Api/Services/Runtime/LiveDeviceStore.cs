using System.Globalization;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Api.Services;

/// <summary>
/// Runtime lookup table. SensorRegistry remains the source of registered profiles.
/// Live fields advance only when the incoming timestamp is not older than LastSeenUtc.
/// Commanded actuator state is stored separately from last reported telemetry.
/// </summary>
public sealed class LiveDeviceStore
{
    private readonly Dictionary<string, MutableState> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly SensorRegistry _registry;

    public LiveDeviceStore(SensorRegistry registry)
    {
        _registry = registry;
    }

    public void EnsureRegistered()
    {
        lock (_gate)
        {
            foreach (var profile in _registry.All())
            {
                var mac = SensorRegistry.NormalizeMac(profile.MacAddress);
                if (_devices.ContainsKey(mac))
                {
                    continue;
                }

                _devices[mac] = FromProfile(profile);
            }
        }
    }

    public bool TryGet(string mac, out LiveDeviceState state)
    {
        lock (_gate)
        {
            EnsureRegisteredUnlocked();
            if (_devices.TryGetValue(SensorRegistry.NormalizeMac(mac), out var mutable))
            {
                state = mutable.ToDto();
                return true;
            }
        }

        state = null!;
        return false;
    }

    public IReadOnlyList<LiveDeviceState> Snapshot()
    {
        lock (_gate)
        {
            EnsureRegisteredUnlocked();
            return _devices.Values.Select(v => v.ToDto()).ToList();
        }
    }

    public LiveDeviceState ApplyTelemetry(TelemetryWorkItem item) =>
        ApplyTelemetry(item, out _);

    public LiveDeviceState ApplyTelemetry(TelemetryWorkItem item, out bool appliedToLive)
    {
        var mac = SensorRegistry.NormalizeMac(item.DeviceId);
        var at = item.TimestampUtc.ToUniversalTime();
        lock (_gate)
        {
            EnsureRegisteredUnlocked();
            if (!_devices.TryGetValue(mac, out var state))
            {
                var profile = _registry.Get(mac);
                state = profile is null
                    ? new MutableState
                    {
                        MacAddress = mac,
                        DisplayName = mac,
                        Location = string.Empty,
                        Category = item.KnownCategory ?? SensorCategory.Environmental
                    }
                    : FromProfile(profile);
                _devices[mac] = state;
            }

            if (!IsEligibleForLiveUpdate(state, at, item.Sequence))
            {
                appliedToLive = false;
                return state.ToDto();
            }

            state.LastSeenUtc = at;
            state.LastLiveSequence = item.Sequence;
            state.ValueKind = item.ValueKind;
            state.Unit = item.Unit;
            state.LatestNumeric = item.NumericValue;
            if (item.ValueKind == "bool")
            {
                state.ReportedActuatorOn = item.BooleanValue;
                if (state.CommandedAtUtc is null || at > state.CommandedAtUtc.Value)
                {
                    state.ActuatorOn = item.BooleanValue;
                }

                state.LatestDisplay = (state.ActuatorOn ?? state.ReportedActuatorOn) == true ? "ON" : "OFF";
            }
            else if (item.NumericValue is { } n)
            {
                state.LatestDisplay = item.ValueKind == "int"
                    ? n.ToString("0", CultureInfo.InvariantCulture)
                    : n.ToString("0.00", CultureInfo.InvariantCulture);
            }

            appliedToLive = true;
            return state.ToDto();
        }
    }

    public void SetActuator(string mac, bool on, DateTimeOffset atUtc)
    {
        mac = SensorRegistry.NormalizeMac(mac);
        var at = atUtc.ToUniversalTime();
        lock (_gate)
        {
            EnsureRegisteredUnlocked();
            if (!_devices.TryGetValue(mac, out var state))
            {
                throw new InvalidOperationException($"Unknown device {mac}.");
            }

            state.CommandedActuatorOn = on;
            state.CommandedAtUtc = at;
            state.ActuatorOn = on;
            state.LatestDisplay = on ? "ON" : "OFF";
            state.ValueKind = "bool";
            if (state.LastSeenUtc is null || at >= state.LastSeenUtc.Value)
            {
                state.LastSeenUtc = at;
                state.LastLiveSequence = long.MaxValue;
            }
        }
    }

    public void SetAlertFlag(string mac, bool hasAlert)
    {
        mac = SensorRegistry.NormalizeMac(mac);
        lock (_gate)
        {
            if (_devices.TryGetValue(mac, out var state))
            {
                state.HasActiveAlert = hasAlert;
            }
        }
    }

    private static bool IsEligibleForLiveUpdate(MutableState state, DateTimeOffset at, long sequence)
    {
        if (state.LastSeenUtc is null)
        {
            return true;
        }

        if (at > state.LastSeenUtc.Value)
        {
            return true;
        }

        // Equal timestamps: later processing sequence wins.
        return at == state.LastSeenUtc.Value && sequence >= state.LastLiveSequence;
    }

    private void EnsureRegisteredUnlocked()
    {
        foreach (var profile in _registry.All())
        {
            var mac = SensorRegistry.NormalizeMac(profile.MacAddress);
            if (!_devices.ContainsKey(mac))
            {
                _devices[mac] = FromProfile(profile);
            }
        }
    }

    private static MutableState FromProfile(SensorProfile profile) => new()
    {
        MacAddress = SensorRegistry.NormalizeMac(profile.MacAddress),
        DisplayName = profile.DisplayName,
        Location = profile.Location,
        Category = profile.Category
    };

    private sealed class MutableState
    {
        public string MacAddress { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Location { get; init; } = string.Empty;
        public SensorCategory Category { get; init; }
        public string? LatestDisplay { get; set; }
        public string? ValueKind { get; set; }
        public double? LatestNumeric { get; set; }
        public bool? ActuatorOn { get; set; }
        public bool? CommandedActuatorOn { get; set; }
        public DateTimeOffset? CommandedAtUtc { get; set; }
        public bool? ReportedActuatorOn { get; set; }
        public DateTimeOffset? LastSeenUtc { get; set; }
        public long LastLiveSequence { get; set; }
        public bool HasActiveAlert { get; set; }
        public string Unit { get; set; } = string.Empty;

        public LiveDeviceState ToDto() => new()
        {
            MacAddress = MacAddress,
            DisplayName = DisplayName,
            Location = Location,
            Category = Category,
            LatestDisplay = LatestDisplay,
            ValueKind = ValueKind,
            LatestNumeric = LatestNumeric,
            ActuatorOn = ActuatorOn,
            CommandedActuatorOn = CommandedActuatorOn,
            ReportedActuatorOn = ReportedActuatorOn,
            LastSeenUtc = LastSeenUtc,
            HasActiveAlert = HasActiveAlert,
            Unit = Unit
        };
    }
}
