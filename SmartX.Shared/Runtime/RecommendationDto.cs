namespace SmartX.Shared.Runtime;

public sealed class RecommendationDto
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Severity { get; init; } = "info";
    public string DeviceId { get; init; } = string.Empty;
    public string? RelatedDeviceId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string SuggestedAction { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public DateTimeOffset EvidenceUtc { get; init; }
    public int EvidenceCount { get; init; }
}
