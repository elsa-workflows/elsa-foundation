namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Portable value-boundary choices pinned into the executable's behavioral identity.</summary>
public sealed record WorkflowValuePolicies(bool DefaultOnNull = false, bool UndefinedAsNull = false)
{
    /// <summary>The native workflow defaults.</summary>
    public static WorkflowValuePolicies Off { get; } = new();
}
