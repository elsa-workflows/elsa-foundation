namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Compiled child activity slot owned by a specific executable activity contract.
/// </summary>
public sealed class ExecutableChildSlot
{
    public ExecutableChildSlot(
        string name,
        IReadOnlyCollection<ExecutableNode> activities,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(activities);

        Name = name;
        Activities = Array.AsReadOnly(activities.ToArray());
        Metadata = RuntimeModelMetadata.Snapshot(metadata ?? new Dictionary<string, string>());
    }

    public string Name { get; }
    public IReadOnlyCollection<ExecutableNode> Activities { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}