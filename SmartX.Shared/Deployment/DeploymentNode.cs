namespace SmartX.Shared.Deployment;

/// <summary>
/// Hierarchical placement used by industrial IoT (facility → zone → sub-zone → edge node).
/// </summary>
public sealed class DeploymentNode
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Zone";
    public string AllowedCategories { get; set; } = "Environmental,PowerConsumption,Actuator";
    public List<DeploymentNode> Children { get; set; } = [];
}

public sealed class ValidationRequest
{
    public string TargetPath { get; set; } = string.Empty;
    public string? SensorCategory { get; set; }
}

public sealed class ValidationResponse
{
    public string TargetPath { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int NodesVisited { get; set; }
    public List<string> WalkTrace { get; set; } = [];
}
