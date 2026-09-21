using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Traversal projection of compiled child activities owned by a specific executable activity contract.
/// </summary>
public sealed class ExecutableChildSlot
{
    public ExecutableChildSlot(
        string name,
        IReadOnlyCollection<ExecutableNode> activities,
        OperatorActivitySchedulingCapability? operatorSchedulingCapability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(activities);

        Name = name;
        Activities = Array.AsReadOnly(activities.ToArray());
        OperatorSchedulingCapability = operatorSchedulingCapability;
    }

    public string Name { get; }
    public IReadOnlyCollection<ExecutableNode> Activities { get; }

    /// <summary>
    /// Exact activity-owned authority for an operator to schedule this compiled relation. A missing value is a
    /// deliberate denial; callers must not substitute generic child-slot logic.
    /// </summary>
    public OperatorActivitySchedulingCapability? OperatorSchedulingCapability { get; }
}
