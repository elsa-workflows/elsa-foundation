using System.Text.Json.Serialization;

namespace Elsa.Activities.Compensation.Models;

/// <summary>
/// Compiled executable structure for a <c>CompensationScope</c> node: the ordered steps with the executable node
/// ids of their forward activity and optional handler. The activities themselves are carried in named child slots;
/// this structure records which slot child belongs to which step.
/// </summary>
public sealed class CompensationScopeExecutableStructure
{
    [JsonConstructor]
    public CompensationScopeExecutableStructure(IReadOnlyCollection<CompensationScopeExecutableStep>? steps = null)
    {
        Steps = steps ?? [];
    }

    [JsonPropertyName("steps")]
    public IReadOnlyCollection<CompensationScopeExecutableStep> Steps { get; }
}

/// <summary>One compiled step: its name and the executable node ids of its forward activity and optional handler.</summary>
public sealed class CompensationScopeExecutableStep
{
    [JsonConstructor]
    public CompensationScopeExecutableStep(string name, string? activity = null, string? compensation = null)
    {
        Name = name;
        Activity = activity;
        Compensation = compensation;
    }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("activity")]
    public string? Activity { get; }

    [JsonPropertyName("compensation")]
    public string? Compensation { get; }
}
