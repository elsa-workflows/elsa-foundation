using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Outcome of evaluating one declared requirement against the installed runtime registries.
/// </summary>
/// <remarks>
/// Ordering is significant: <see cref="RuntimeRequirementStatusEntry"/> aggregation takes the worst
/// status via <c>Max()</c>, mirroring the publishing preflight this check was extracted from.
/// </remarks>
public enum RuntimeRequirementStatus
{
    /// <summary>The requirement is satisfied by an installed registry entry.</summary>
    Available,

    /// <summary>Nothing in this runtime advertises the required key.</summary>
    Missing,

    /// <summary>The key is advertised, but not at the required schema version.</summary>
    UnsupportedSchema,

    /// <summary>
    /// The node's CLR activity type is not registered in this runtime's well-known type registry.
    /// The second axis of the import gate (FR-B-005a) — independent of consumer capabilities, which
    /// are per activation mechanism and do not cover type availability.
    /// </summary>
    MissingActivityType
}

/// <summary>Per consumer-capability requirement outcome. Exact ordinal set-membership semantics.</summary>
public sealed record RuntimeRequirementStatusEntry(
    string ConsumerKey,
    string SchemaVersion,
    RuntimeRequirementStatus Status,
    IReadOnlyList<string> SupportedSchemaVersions);

/// <summary>Per durable-value storage-driver requirement outcome. Keys are exact and unversioned.</summary>
public sealed record StorageDriverStatusEntry(string DriverKey, RuntimeRequirementStatus Status);

/// <summary>
/// Per distinct CLR activity type alias outcome, with the nodes that carry it so a diagnostic can
/// name where the missing type is used.
/// </summary>
public sealed record ActivityTypeStatusEntry(
    string TypeAlias,
    IReadOnlyList<string> NodeIds,
    RuntimeRequirementStatus Status);

/// <summary>
/// The subject of a requirements check. Neutral over executables and reusable-activity templates —
/// both declare the same requirement sets and both carry nodes, so the shared contract loses nothing
/// by not naming either type (2026-08-15 architect review, FR-B-005).
/// </summary>
public sealed record RuntimeRequirementCheckSubject(
    string ArtifactId,
    IReadOnlyCollection<RuntimeRequirement> RuntimeRequirements,
    IReadOnlyCollection<RuntimeStorageDriverRequirement> StorageDriverRequirements,
    IReadOnlyCollection<ExecutableNode> Nodes)
{
    /// <summary>Builds a subject from a compiled workflow executable.</summary>
    public static RuntimeRequirementCheckSubject FromExecutable(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return new(
            executable.Identity.ArtifactId,
            executable.RuntimeRequirements,
            executable.StorageDriverRequirements,
            executable.Nodes);
    }

    /// <summary>Builds a subject from a reusable-activity template.</summary>
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

/// <summary>
/// Runtime-layer verdict covering both gate axes. Carries no Publishing view and no Design
/// <c>ActivityDiagnostic</c> — callers project it into their own diagnostic vocabulary.
/// </summary>
public sealed record RuntimeRequirementCheckResult(
    string ArtifactId,
    IReadOnlyList<RuntimeRequirementStatusEntry> Requirements,
    IReadOnlyList<StorageDriverStatusEntry> StorageDrivers,
    IReadOnlyList<ActivityTypeStatusEntry> ActivityTypes)
{
    /// <summary>
    /// True only when every entry across all three collections is <see cref="RuntimeRequirementStatus.Available"/>.
    /// Failing <em>either</em> axis fails the gate (FR-B-005a).
    /// </summary>
    public bool IsSatisfied =>
        Requirements.All(entry => entry.Status == RuntimeRequirementStatus.Available) &&
        StorageDrivers.All(entry => entry.Status == RuntimeRequirementStatus.Available) &&
        ActivityTypes.All(entry => entry.Status == RuntimeRequirementStatus.Available);
}
