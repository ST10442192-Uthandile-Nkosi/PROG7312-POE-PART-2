using SmartX.Shared.Deployment;

namespace SmartX.Api.Services;

/// <summary>
/// Depth-first recursion over the campus deployment tree. Used to prove a node sits inside
/// a legal Facility → Zone → Sub-Zone path before the gateway accepts its MQTT-style traffic.
/// </summary>
public sealed class DeploymentValidator
{
    public DeploymentNode Root { get; }

    public DeploymentValidator()
    {
        Root = BuildCampus();
    }

    public ValidationResponse ValidateNodePath(string targetPath, string? category = null)
    {
        var trace = new List<string>();
        var visited = 0;
        var found = Walk(Root, targetPath, "", trace, ref visited);
        var response = new ValidationResponse
        {
            TargetPath = targetPath,
            NodesVisited = visited,
            WalkTrace = trace,
            IsValid = found
        };

        if (!found)
        {
            response.Reason = "Path does not exist in the mesh deployment tree.";
            return response;
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var node = FindByPath(Root, targetPath, "");
            if (node is not null &&
                !node.AllowedCategories.Contains(category, StringComparison.OrdinalIgnoreCase))
            {
                response.IsValid = false;
                response.Reason = $"Category {category} is not permitted in {node.Name} ({node.Kind}).";
                return response;
            }
        }

        response.Reason = "Node is safely nested in the campus tree.";
        return response;
    }

    public int CountNodes()
    {
        var count = 0;
        CountRecursive(Root, ref count);
        return count;
    }

    // Recursively walk children until the target path is found.
    private static bool Walk(DeploymentNode current, string targetPath, string currentPath, List<string> trace, ref int visited)
    {
        visited++;
        var newPath = string.IsNullOrEmpty(currentPath) ? current.Name : $"{currentPath} -> {current.Name}";
        trace.Add($"visit[{visited}]: {newPath}");

        if (newPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var child in current.Children)
        {
            if (Walk(child, targetPath, newPath, trace, ref visited))
            {
                return true;
            }
        }

        return false;
    }

    private static DeploymentNode? FindByPath(DeploymentNode current, string targetPath, string currentPath)
    {
        var newPath = string.IsNullOrEmpty(currentPath) ? current.Name : $"{currentPath} -> {current.Name}";
        if (newPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        foreach (var child in current.Children)
        {
            var match = FindByPath(child, targetPath, newPath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static void CountRecursive(DeploymentNode node, ref int count)
    {
        count++;
        foreach (var child in node.Children)
        {
            CountRecursive(child, ref count);
        }
    }

    private static DeploymentNode BuildCampus() => new()
    {
        Name = "Facility A",
        Kind = "Facility",
        AllowedCategories = "Environmental,PowerConsumption,Actuator",
        Children =
        [
            new DeploymentNode
            {
                Name = "Zone 1",
                Kind = "HydroponicZone",
                AllowedCategories = "Environmental,Actuator",
                Children =
                [
                    new DeploymentNode
                    {
                        Name = "Sub-Zone B",
                        Kind = "NftGutter",
                        AllowedCategories = "Environmental,Actuator"
                    },
                    new DeploymentNode
                    {
                        Name = "Sub-Zone Reservoir",
                        Kind = "NutrientTank",
                        AllowedCategories = "Environmental,Actuator"
                    }
                ]
            },
            new DeploymentNode
            {
                Name = "Zone 2",
                Kind = "ClimateZone",
                AllowedCategories = "Environmental,Actuator",
                Children =
                [
                    new DeploymentNode
                    {
                        Name = "Sub-Zone Canopy",
                        Kind = "Greenhouse",
                        AllowedCategories = "Environmental,Actuator"
                    }
                ]
            }
        ]
    };
}
