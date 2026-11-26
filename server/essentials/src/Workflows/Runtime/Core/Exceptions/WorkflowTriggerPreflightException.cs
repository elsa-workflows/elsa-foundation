namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// A typed publication-time failure raised before trigger or recurring-schedule state is mutated.
/// </summary>
public sealed class WorkflowTriggerPreflightException : Exception
{
    /// <summary>
    /// The facet of an authoring error: a start trigger sits where no run can start at it, because
    /// <see cref="CompositeNodeId"/> does not begin at the path that leads to it (spec 238, the start-path rule).
    /// </summary>
    public const string PlacementFacet = "Placement";

    public WorkflowTriggerPreflightException(
        string artifactId,
        string executableNodeId,
        string activityType,
        IReadOnlyCollection<string> providerIds,
        string facet,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityType);
        ArgumentNullException.ThrowIfNull(providerIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(facet);

        ArtifactId = artifactId;
        ExecutableNodeId = executableNodeId;
        ActivityType = activityType;
        ProviderIds = providerIds.ToArray();
        Facet = facet;
    }

    /// <summary>
    /// A <see cref="PlacementFacet"/> failure: the start trigger <paramref name="executableNodeId"/> lies below
    /// <paramref name="compositeNodeId"/>, which does not begin at the path that leads to it.
    /// </summary>
    public WorkflowTriggerPreflightException(
        string artifactId,
        string executableNodeId,
        string activityType,
        IReadOnlyCollection<string> providerIds,
        string compositeNodeId,
        string? compositeStructureKind,
        string message)
        : this(artifactId, executableNodeId, activityType, providerIds, PlacementFacet, message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compositeNodeId);
        CompositeNodeId = compositeNodeId;
        CompositeStructureKind = compositeStructureKind;
    }

    public string ArtifactId { get; }
    public string ExecutableNodeId { get; }
    public string ActivityType { get; }
    public IReadOnlyCollection<string> ProviderIds { get; }
    public string Facet { get; }

    /// <summary>For a <see cref="PlacementFacet"/> failure, the composite that does not begin at the start trigger; otherwise <c>null</c>.</summary>
    public string? CompositeNodeId { get; }

    /// <summary>For a <see cref="PlacementFacet"/> failure, that composite's structure kind, or <c>null</c> when it has no structure.</summary>
    public string? CompositeStructureKind { get; }
}
