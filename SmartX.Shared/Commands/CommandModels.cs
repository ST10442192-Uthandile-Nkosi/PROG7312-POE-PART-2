namespace SmartX.Shared.Commands;

public sealed class CommandRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public bool ActuatorOn { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class CommandRecord
{
    public Guid Id { get; init; }
    public string MacAddress { get; init; } = string.Empty;
    public bool PreviousActuatorOn { get; init; }
    public bool ResultingActuatorOn { get; init; }
    public DateTimeOffset AppliedAtUtc { get; init; }
    public string Note { get; init; } = string.Empty;
    public bool IsUndo { get; init; }
}

public sealed class CommandResult
{
    public bool Accepted { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public CommandRecord? Command { get; init; }
}
