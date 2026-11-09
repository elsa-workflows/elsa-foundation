using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Compensation.Models;

/// <summary>
/// Authored design-time structure for a <c>CompensationScope</c> node: the ordered steps, each a named forward
/// activity with an optional compensation handler. Each forward activity and each handler is a single child placed
/// in its own named slot.
/// </summary>
public sealed class CompensationScopeAuthoredStructure
{
    [JsonConstructor]
    public CompensationScopeAuthoredStructure(IReadOnlyCollection<CompensationScopeAuthoredStep>? steps = null)
    {
        Steps = steps ?? [];
    }

    [JsonPropertyName("steps")]
    public IReadOnlyCollection<CompensationScopeAuthoredStep> Steps { get; }
}

/// <summary>One authored step: its unique name, the forward activity and the optional compensation handler.</summary>
public sealed class CompensationScopeAuthoredStep
{
    [JsonConstructor]
    public CompensationScopeAuthoredStep(string name, ActivityNode? activity = null, ActivityNode? compensation = null)
    {
        Name = name;
        Activity = activity;
        Compensation = compensation;
    }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("activity")]
    public ActivityNode? Activity { get; }

    [JsonPropertyName("compensation")]
    public ActivityNode? Compensation { get; }
}
