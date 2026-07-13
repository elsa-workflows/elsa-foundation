using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

public enum WorkflowExecutableListScope
{
    Published,
    TestRuns,
    All
}

public sealed record ExecutableSourceReferenceView(
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
{
    public static ExecutableSourceReferenceView From(WorkflowExecutableSourceReference reference, DateTimeOffset now) =>
        new(
            reference.SourceReferenceId,
            reference.ArtifactId,
            reference.Scope.ToString(),
            reference.SourceKind,
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
