using System.Globalization;
using SmartX.Shared.Commands;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;

namespace SmartX.Api.Services;

/// <summary>
/// Read-only operator insights. Does not enqueue telemetry, raise faults, or apply commands.
/// </summary>
public sealed class RecommendationService
{
    public const int MinTrendReadings = 4;
    public const int MinPatternObservations = 3;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);
    public const double TrendStep = 0.5;
    public const double MoistureSpan = 5;

    private readonly LiveDeviceStore _devices;
    private readonly TelemetryHistoryStore _history;
    private readonly ActiveFaultSet _faults;
    private readonly CommandUndoStore _commands;
    private readonly ThresholdEvaluator _thresholds;
    private readonly TimeProvider _clock;

    public RecommendationService(
        LiveDeviceStore devices,
        TelemetryHistoryStore history,
        ActiveFaultSet faults,
        CommandUndoStore commands,
        ThresholdEvaluator thresholds,
        TimeProvider? clock = null)
    {
        _devices = devices;
        _history = history;
        _faults = faults;
        _commands = commands;
        _thresholds = thresholds;
        _clock = clock ?? TimeProvider.System;
    }

    public IReadOnlyList<RecommendationDto> Evaluate() =>
        Evaluate(_clock.GetUtcNow());

    public IReadOnlyList<RecommendationDto> Evaluate(DateTimeOffset nowUtc)
    {
        nowUtc = nowUtc.ToUniversalTime();
        var devices = _devices.Snapshot();
        var history = _history.SnapshotOldestFirst();
        var commands = _commands.AuditSnapshot();
        var byId = new Dictionary<string, RecommendationDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var rec in ThresholdInsights(devices, nowUtc))
        {
            byId[rec.Id] = rec;
        }

        foreach (var rec in TrendInsights(devices, history, nowUtc))
        {
            byId[rec.Id] = rec;
        }

        foreach (var rec in CouplingInsights(devices, nowUtc))
        {
            byId[rec.Id] = rec;
        }

        foreach (var rec in PatternInsights(devices, history, commands, nowUtc))
        {
            byId[rec.Id] = rec;
        }

        return byId.Values
            .OrderBy(r => SeverityRank(r.Severity))
            .ThenBy(r => r.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<RecommendationDto> ThresholdInsights(IReadOnlyList<LiveDeviceState> devices, DateTimeOffset now)
    {
        foreach (var device in devices)
        {
            if (device.LastSeenUtc is null || now - device.LastSeenUtc.Value > StaleAfter)
            {
                continue;
            }

            var item = new TelemetryWorkItem
            {
                DeviceId = device.MacAddress,
                TimestampUtc = device.LastSeenUtc.Value,
                ValueKind = device.ValueKind ?? "float",
                NumericValue = device.LatestNumeric,
                Unit = device.Unit,
                KnownCategory = device.Category
            };

            foreach (var alert in _thresholds.EvaluatePacket(item, device.Category))
            {
                if (_faults.Contains(alert.Code, device.MacAddress))
                {
                    continue;
                }

                var id = $"threshold:{alert.Code}:{device.MacAddress}";
                yield return new RecommendationDto
                {
                    Id = id,
                    Category = "threshold",
                    Severity = alert.Priority == AlertPriority.Critical ? "critical" : "warning",
                    DeviceId = device.MacAddress,
                    Title = $"{alert.Code} on {device.DisplayName}",
                    Explanation = alert.Detail,
                    SuggestedAction = "Review the live reading on the command stream. Do not assume a physical actuator moved.",
                    Evidence = $"Observed {device.LatestDisplay} {device.Unit} at {device.LastSeenUtc:u} (threshold from ThresholdEvaluator).",
                    EvidenceUtc = device.LastSeenUtc.Value,
                    EvidenceCount = 1
                };
            }
        }
    }

    private static IEnumerable<RecommendationDto> TrendInsights(
        IReadOnlyList<LiveDeviceState> devices,
        IReadOnlyList<ProcessedReading> history,
        DateTimeOffset now)
    {
        foreach (var device in devices)
        {
            IReadOnlyList<(DateTimeOffset At, double Value, string Unit)> series;
            if (device.Category == SensorCategory.Environmental)
            {
                series = NumericSeries(history, device.MacAddress, "VWC");
            }
            else if (device.Category == SensorCategory.PowerConsumption)
            {
                series = NumericSeries(history, device.MacAddress, null);
            }
            else
            {
                continue;
            }

            if (series.Count < MinTrendReadings)
            {
                continue;
            }

            var window = series.TakeLast(MinTrendReadings).ToList();
            if (now - window[^1].At > StaleAfter)
            {
                continue;
            }

            var falling = IsMonotone(window, down: true);
            var rising = IsMonotone(window, down: false);
            var span = window[^1].Value - window[0].Value;
            if (device.Category == SensorCategory.Environmental && falling && span <= -MoistureSpan)
            {
                yield return Trend(device, "fall", "warning", window,
                    $"VWC fell from {window[0].Value:0.0} to {window[^1].Value:0.0} over {MinTrendReadings} readings.",
                    "Inspect irrigation in this device's deployment zone. Use the command panel only if a paired actuator exists.");
            }
            else if (device.Category == SensorCategory.Environmental && rising && span >= MoistureSpan)
            {
                yield return Trend(device, "rise", "info", window,
                    $"VWC rose from {window[0].Value:0.0} to {window[^1].Value:0.0} over {MinTrendReadings} readings.",
                    "Watch for over-watering if this continues toward the 82% VWC high band.");
            }
            else if (device.Category == SensorCategory.PowerConsumption && rising && window[^1].Value > ThresholdEvaluator.PowerWarningWatts * 0.7)
            {
                yield return Trend(device, "rise", "warning", window,
                    $"Power rose from {window[0].Value:0} W to {window[^1].Value:0} W over {MinTrendReadings} readings.",
                    "Inspect feeder load. Suggested action is diagnostic only.");
            }
        }
    }

    private static IEnumerable<RecommendationDto> CouplingInsights(
        IReadOnlyList<LiveDeviceState> devices,
        DateTimeOffset now)
    {
        foreach (var bed in devices.Where(d => d.Category == SensorCategory.Environmental))
        {
            if (bed.LatestNumeric is null || bed.LatestNumeric >= ThresholdEvaluator.VwcLow)
            {
                continue;
            }

            if (bed.LastSeenUtc is null || now - bed.LastSeenUtc.Value > StaleAfter)
            {
                continue;
            }

            if (!bed.Unit.Contains("VWC", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var valve = devices.FirstOrDefault(v =>
                v.Category == SensorCategory.Actuator &&
                ThresholdEvaluator.SharesDeploymentZone(bed.Location, v.Location));

            if (valve is null)
            {
                yield return new RecommendationDto
                {
                    Id = $"diagnostic-dry:{bed.MacAddress}",
                    Category = "diagnostic",
                    Severity = "warning",
                    DeviceId = bed.MacAddress,
                    Title = $"{bed.DisplayName} is below the VWC low band",
                    Explanation = "No actuator shares this Facility -> Zone path, so no command target is suggested.",
                    SuggestedAction = "Inspect the bed physically. Do not command an unrelated actuator.",
                    Evidence = $"VWC {bed.LatestNumeric:0.0}% at {bed.LastSeenUtc:u}; location {bed.Location}.",
                    EvidenceUtc = bed.LastSeenUtc.Value,
                    EvidenceCount = 1
                };
                continue;
            }

            if (valve.ActuatorOn == false)
            {
                yield return new RecommendationDto
                {
                    Id = $"coupling-irr:{bed.MacAddress}:{valve.MacAddress}",
                    Category = "coupling",
                    Severity = "critical",
                    DeviceId = bed.MacAddress,
                    RelatedDeviceId = valve.MacAddress,
                    Title = $"{bed.DisplayName} is dry while {valve.DisplayName} is OFF",
                    Explanation = "Paired by shared Facility and Zone, not by similar names.",
                    SuggestedAction = $"If you agree, use the command panel to turn {valve.DisplayName} ON. This insight does not send the command.",
                    Evidence = $"VWC {bed.LatestNumeric:0.0}% (band {ThresholdEvaluator.VwcLow}%) at {bed.LastSeenUtc:u}; actuator {valve.MacAddress} OFF.",
                    EvidenceUtc = bed.LastSeenUtc.Value,
                    EvidenceCount = 2
                };
            }
        }
    }

    private static IEnumerable<RecommendationDto> PatternInsights(
        IReadOnlyList<LiveDeviceState> devices,
        IReadOnlyList<ProcessedReading> history,
        IReadOnlyList<CommandRecord> commands,
        DateTimeOffset now)
    {
        var ons = commands.Where(c => !c.IsUndo && c.ResultingActuatorOn).ToList();
        foreach (var valve in devices.Where(d => d.Category == SensorCategory.Actuator))
        {
            var beds = devices.Where(d =>
                d.Category == SensorCategory.Environmental &&
                d.Unit.Contains("VWC", StringComparison.OrdinalIgnoreCase) &&
                ThresholdEvaluator.SharesDeploymentZone(d.Location, valve.Location)).ToList();
            if (beds.Count == 0)
            {
                continue;
            }

            var hits = 0;
            DateTimeOffset lastHit = default;
            foreach (var command in ons.Where(c =>
                         c.MacAddress.Equals(valve.MacAddress, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var bed in beds)
                {
                    var atCommand = LatestNumericAt(history, bed.MacAddress, "VWC", command.AppliedAtUtc);
                    if (atCommand is { } vwc && vwc < ThresholdEvaluator.VwcLow)
                    {
                        hits++;
                        lastHit = command.AppliedAtUtc;
                        break;
                    }
                }
            }

            if (hits < MinPatternObservations)
            {
                continue;
            }

            yield return new RecommendationDto
            {
                Id = $"pattern-on-when-dry:{valve.MacAddress}",
                Category = "pattern",
                Severity = "info",
                DeviceId = valve.MacAddress,
                RelatedDeviceId = beds[0].MacAddress,
                Title = $"Operators often turn {valve.DisplayName} ON when the zone is dry",
                Explanation = $"At least {MinPatternObservations} accepted ON commands occurred while a zone-paired VWC sensor was below {ThresholdEvaluator.VwcLow}%.",
                SuggestedAction = "Treat as an operator habit, not an automatic command. Review the next dry event before acting.",
                Evidence = $"{hits} ON commands matched dry VWC in the same Facility/Zone. Last match {lastHit:u}.",
                EvidenceUtc = lastHit == default ? now : lastHit.ToUniversalTime(),
                EvidenceCount = hits
            };
        }
    }

    private static RecommendationDto Trend(
        LiveDeviceState device,
        string direction,
        string severity,
        List<(DateTimeOffset At, double Value, string Unit)> window,
        string explanation,
        string action) =>
        new()
        {
            Id = $"trend-{direction}:{device.MacAddress}",
            Category = "trend",
            Severity = severity,
            DeviceId = device.MacAddress,
            Title = $"{device.DisplayName} {direction}ing",
            Explanation = explanation,
            SuggestedAction = action,
            Evidence = string.Join(" → ", window.Select(p => $"{p.Value.ToString("0.0", CultureInfo.InvariantCulture)}@{p.At:HH:mm}")),
            EvidenceUtc = window[^1].At,
            EvidenceCount = window.Count
        };

    private static bool IsMonotone(List<(DateTimeOffset At, double Value, string Unit)> window, bool down)
    {
        for (var i = 1; i < window.Count; i++)
        {
            var delta = window[i].Value - window[i - 1].Value;
            if (down && delta > -TrendStep)
            {
                return false;
            }

            if (!down && delta < TrendStep)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<(DateTimeOffset At, double Value, string Unit)> NumericSeries(
        IReadOnlyList<ProcessedReading> history,
        string mac,
        string? unitFragment)
    {
        var rows = new List<(DateTimeOffset At, double Value, string Unit)>();
        foreach (var reading in history.Where(r =>
                     r.DeviceId.Equals(mac, StringComparison.OrdinalIgnoreCase)))
        {
            if (reading.ValueKind.Equals("bool", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (unitFragment is not null &&
                reading.Unit.IndexOf(unitFragment, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (!double.TryParse(reading.DisplayValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            rows.Add((reading.TimestampUtc.ToUniversalTime(), value, reading.Unit));
        }

        return rows;
    }

    private static double? LatestNumericAt(
        IReadOnlyList<ProcessedReading> history,
        string mac,
        string unitFragment,
        DateTimeOffset at)
    {
        at = at.ToUniversalTime();
        foreach (var point in NumericSeries(history, mac, unitFragment).Reverse())
        {
            if (point.At <= at)
            {
                return point.Value;
            }
        }

        return null;
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "critical" => 0,
        "warning" => 1,
        _ => 2
    };
}
