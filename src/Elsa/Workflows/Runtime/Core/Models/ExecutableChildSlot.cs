namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Traversal projection of compiled child activities owned by a specific executable activity contract.
/// </summary>
public sealed class ExecutableChildSlot
{
    public ExecutableChildSlot(
        string name,
        IReadOnlyCollection<ExecutableNode> activities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(activities);

        Name = name;
        Activities = Array.AsReadOnly(activities.ToArray());
    }

    public string Name { get; }
    public IReadOnlyCollection<ExecutableNode> Activities { get; }
}
