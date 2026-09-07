using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Outcome of evaluating one declared requirement against the installed runtime registries.</summary>
public enum RuntimeRequirementStatus
{
    Available,
    Missing,
    UnsupportedSchema,
    MissingActivityType
}

/// <summary>Per runtime consumer requirement outcome.</summary>
public sealed record RuntimeRequirementStatusEntry(
    string ConsumerKey,
    string SchemaVersion,
    RuntimeRequirementStatus Status,
    IReadOnlyList<string> SupportedSchemaVersions);

/// <summary>Per durable-value storage-driver requirement outcome.</summary>
public sealed record StorageDriverStatusEntry(string DriverKey, RuntimeRequirementStatus Status);

/// <summary>Per distinct CLR activity type alias outcome and the nodes that declare it.</summary>
public sealed record ActivityTypeStatusEntry(
    string TypeAlias,
    IReadOnlyList<string> NodeIds,
    RuntimeRequirementStatus Status);

/// <summary>
/// Neutral requirements-check subject shared by compiled workflow executables and reusable-activity
/// templates. Both carry the same requirement sets and executable nodes.
/// </summary>
public sealed record RuntimeRequirementCheckSubject(
    string ArtifactId,
    IReadOnlyCollection<RuntimeRequirement> RuntimeRequirements,
    IReadOnlyCollection<RuntimeStorageDriverRequirement> StorageDriverRequirements,
    IReadOnlyCollection<ExecutableNode> Nodes)
{
    public static RuntimeRequirementCheckSubject FromExecutable(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return new(
            executable.Identity.ArtifactId,
            executable.RuntimeRequirements,
            executable.StorageDriverRequirements,
            executable.Nodes);
    }

    public static RuntimeRequirementCheckSubject FromTemplate(ExecutableActivityTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new(
            template.TemplateId,
            template.RuntimeRequirements,
            template.StorageDriverRequirements,
            template.NodesById.Values.ToArray());
    }
}

/// <summary>Runtime-layer verdict covering all requirement axes.</summary>
public sealed record RuntimeRequirementCheckResult(
    string ArtifactId,
    IReadOnlyList<RuntimeRequirementStatusEntry> Requirements,
    IReadOnlyList<StorageDriverStatusEntry> StorageDrivers,
    IReadOnlyList<ActivityTypeStatusEntry> ActivityTypes)
{
    public bool IsSatisfied =>
        Requirements.All(entry => entry.Status == RuntimeRequirementStatus.Available) &&
        StorageDrivers.All(entry => entry.Status == RuntimeRequirementStatus.Available) &&
        ActivityTypes.All(entry => entry.Status == RuntimeRequirementStatus.Available);
}
