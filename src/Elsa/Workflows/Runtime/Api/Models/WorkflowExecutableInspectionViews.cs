using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

public enum WorkflowExecutableListScope
{
    Published,
    TestRuns,
    All
}

/// <summary>Describes one source reference that keeps a workflow executable reachable.</summary>
/// <remarks>
/// <see cref="SourceKind"/> is the canonical source discriminator. <see cref="SourceType"/> is emitted only as
/// a Runtime API v1 compatibility alias and always contains the same value; new clients must use
/// <see cref="SourceKind"/>. The alias is scheduled for removal with Runtime API v2.
/// </remarks>
[method: JsonConstructor]
public sealed record ExecutableSourceReferenceView(
    string SourceReferenceId,
    string ArtifactId,
    string Scope,
    string? SourceKind,
    string? SourceId,
    string? SourceVersion,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string? PublicationId,
    string? SlotId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DeletedAt,
    string? DeletedReason,
    bool Live)
{
    /// <summary>Runtime API v1 compatibility alias for <see cref="SourceKind"/>.</summary>
    [Obsolete("sourceType is a compatibility alias for sourceKind in Runtime API v1 and will be removed in Runtime API v2. Use sourceKind.")]
    [JsonPropertyName("sourceType")]
    public string? SourceType
    {
        get => SourceKind;
        init => SourceKind = ResolveSourceKind(value, SourceKind);
    }

    /// <summary>Runtime API v1 constructor retained for source and binary compatibility.</summary>
    [Obsolete("The sourceType constructor parameter is retained for Runtime API v1 compatibility and will be removed in Runtime API v2. Use the sourceKind constructor overload.")]
    public ExecutableSourceReferenceView(
        string SourceReferenceId,
        string ArtifactId,
        string Scope,
        string? SourceType,
        string? SourceKind,
        string? SourceId,
        string? SourceVersion,
        string DefinitionId,
        string DefinitionVersionId,
        string ArtifactVersion,
        string? PublicationId,
        string? SlotId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? PublishedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? DeletedAt,
        string? DeletedReason,
        bool Live)
        : this(
            SourceReferenceId,
            ArtifactId,
            Scope,
            ResolveSourceKind(SourceType, SourceKind),
            SourceId,
            SourceVersion,
            DefinitionId,
            DefinitionVersionId,
            ArtifactVersion,
            PublicationId,
            SlotId,
            CreatedAt,
            PublishedAt,
            ExpiresAt,
            DeletedAt,
            DeletedReason,
            Live)
    {
    }

    public static ExecutableSourceReferenceView From(WorkflowExecutableSourceReference reference, DateTimeOffset now) =>
        new(
            reference.SourceReferenceId,
            reference.ArtifactId,
            reference.Scope.ToString(),
            reference.SourceKind,
            reference.SourceId,
            reference.SourceVersion,
            reference.DefinitionId,
            reference.DefinitionVersionId,
            reference.ArtifactVersion,
            reference.PublicationId,
            reference.SlotId,
            reference.CreatedAt,
            reference.PublishedAt,
            reference.ExpiresAt,
            reference.DeletedAt,
            reference.DeletedReason,
            reference.IsLive(now));

    private static string? ResolveSourceKind(string? sourceType, string? sourceKind)
    {
        if (sourceType is not null && sourceKind is not null && !string.Equals(sourceType, sourceKind, StringComparison.Ordinal))
            throw new ArgumentException("sourceType and sourceKind must contain the same value.", nameof(sourceType));

        return sourceKind ?? sourceType;
    }
}

public sealed record WorkflowExecutableSummaryView(
    string ArtifactId,
    string ArtifactVersion,
    string ArtifactHash,
    string DefinitionId,
    string DefinitionVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? DeletedAt,
    string? SourceKind,
    string? SourceId,
    string? SourceVersion,
    string RootActivityType,
    string RootActivityVersion,
    int NodeCount,
    int ResumeTargetCount,
    int LiveSourceReferenceCount,
    int RetainedExecutionCount,
    IReadOnlyCollection<ExecutableSourceReferenceView> References);

public sealed record WorkflowExecutablesListView(IReadOnlyCollection<WorkflowExecutableSummaryView> Items);

public sealed record WorkflowExecutableNodeView(
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string? StructureKind,
    IReadOnlyCollection<WorkflowExecutableInputBindingView> InputBindings,
    IReadOnlyCollection<WorkflowExecutableChildSlotView> ChildSlots,
    IReadOnlyCollection<WorkflowExecutableConnectionView> Connections);

public sealed record WorkflowExecutableConnectionEndpointView(string NodeId, string? Port);

public sealed record WorkflowExecutableConnectionView(
    WorkflowExecutableConnectionEndpointView Source,
    WorkflowExecutableConnectionEndpointView Target);

public sealed record WorkflowExecutableInputBindingView(string InputName, string Source, string? Summary);

public sealed record WorkflowExecutableChildSlotView(string Name, IReadOnlyCollection<WorkflowExecutableNodeView> Activities);

public sealed record WorkflowExecutableDetailsView(
    string ArtifactId,
    string ArtifactHash,
    DateTimeOffset CreatedAt,
    string RootActivityType,
    string RootActivityVersion,
    int NodeCount,
    int ResumeTargetCount,
    int LiveSourceReferenceCount,
    int RetainedExecutionCount,
    WorkflowExecutableNodeView RootActivity,
    IReadOnlyDictionary<string, string> Metadata,
    WorkflowExecutableChosenReferenceView? ChosenReference,
    IReadOnlyCollection<ExecutableSourceReferenceView> References);

public sealed record WorkflowExecutableChosenReferenceView(
    string SourceReferenceId,
    string Selection,
    IReadOnlyCollection<WorkflowExecutableLayoutRecord> Layout);

public sealed record ExecutableProvenanceView(
    string ArtifactId,
    IReadOnlyCollection<ExecutableSourceReferenceView> SourceReferences,
    int RetainedExecutionCount,
    bool ProtectedFromCollection);
