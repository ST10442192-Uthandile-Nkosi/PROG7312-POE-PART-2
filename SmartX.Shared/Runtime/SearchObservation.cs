namespace SmartX.Shared.Runtime;

public sealed class SearchObservationRequest
{
    public string Term { get; set; } = string.Empty;
}

public sealed class SearchObservation
{
    public string Term { get; init; } = string.Empty;
    public DateTimeOffset AtUtc { get; init; }
}
