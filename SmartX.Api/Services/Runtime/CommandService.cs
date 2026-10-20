using SmartX.Shared.Commands;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Services;

public sealed class CommandService
{
    private readonly SensorRegistry _registry;
    private readonly LiveDeviceStore _devices;
    private readonly CommandUndoStore _undo;
    private readonly TelemetryIngestionService _ingestion;
    private readonly TelemetryHistoryStore _history;
    private readonly ProcessingJournal _journal;

    public CommandService(
        SensorRegistry registry,
        LiveDeviceStore devices,
        CommandUndoStore undo,
        TelemetryIngestionService ingestion,
        TelemetryHistoryStore history,
        ProcessingJournal journal)
    {
        _registry = registry;
        _devices = devices;
        _undo = undo;
        _ingestion = ingestion;
        _history = history;
        _journal = journal;
    }

    public CommandResult Apply(CommandRequest request, bool isUndo = false, CommandRecord? restoring = null)
    {
        if (string.IsNullOrWhiteSpace(request.MacAddress))
        {
            return Reject("MAC is required.");
        }

        var mac = SensorRegistry.NormalizeMac(request.MacAddress);
        var profile = _registry.Get(mac);
        if (profile is null)
        {
            return Reject($"Unknown device {mac}.");
        }

        if (profile.Category != SensorCategory.Actuator)
        {
            return Reject($"{profile.DisplayName} is {profile.Category}, not an actuator.");
        }

        _devices.EnsureRegistered();
        _devices.TryGet(mac, out var live);
        var previous = live?.CommandedActuatorOn ?? live?.ActuatorOn ?? _ingestion.LatestBool(mac) ?? false;

        if (isUndo && restoring is not null && live?.CommandedActuatorOn is { } current && current != restoring.ResultingActuatorOn)
        {
            return Reject("Device state changed since that command; undo was not applied.");
        }

        var at = DateTimeOffset.UtcNow;
        _ingestion.ApplyActuatorState(mac, request.ActuatorOn, at);
        _devices.SetActuator(mac, request.ActuatorOn, at);
        _history.Add(new TelemetryWorkItem
        {
            DeviceId = mac,
            TimestampUtc = at,
            ValueKind = "bool",
            BooleanValue = request.ActuatorOn,
            MetricName = "actuator_state",
            Unit = "state",
            KnownCategory = SensorCategory.Actuator
        });

        var record = new CommandRecord
        {
            Id = Guid.NewGuid(),
            MacAddress = mac,
            PreviousActuatorOn = previous,
            ResultingActuatorOn = request.ActuatorOn,
            AppliedAtUtc = at,
            Note = request.Note,
            IsUndo = isUndo
        };
        if (!isUndo)
        {
            _undo.RecordApplied(record);
        }

        _journal.Record(isUndo ? "UNDO" : "COMMAND", $"{mac}:{previous}->{request.ActuatorOn}");
        return new CommandResult { Accepted = true, Outcome = isUndo ? "Undone." : "Applied.", Command = record };
    }

    public CommandResult Undo()
    {
        if (!_undo.TryLeaseUndo(out var previous) || previous is null)
        {
            return Reject("No command available to undo.");
        }

        try
        {
            var result = Apply(new CommandRequest
            {
                MacAddress = previous.MacAddress,
                ActuatorOn = previous.PreviousActuatorOn,
                Note = $"undo:{previous.Id:N}"
            }, isUndo: true, restoring: previous);

            if (result.Accepted && result.Command is not null)
            {
                _undo.CompleteLease(previous, result.Command);
            }
            else
            {
                _undo.RollbackLease(previous);
            }

            return result;
        }
        catch
        {
            _undo.RollbackLease(previous);
            throw;
        }
    }

    public IReadOnlyList<CommandRecord> History() => _undo.AuditSnapshot();

    private static CommandResult Reject(string outcome) => new() { Accepted = false, Outcome = outcome };
}
