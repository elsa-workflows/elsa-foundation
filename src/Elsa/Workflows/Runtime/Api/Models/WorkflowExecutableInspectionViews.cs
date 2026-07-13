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
    string? SourceId,
    string? PublicationId,
    string? SlotId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DeletedAt,
    bool Live)
{
    public static ExecutableSourceReferenceView From(WorkflowExecutableSourceReference reference, DateTimeOffset now) =>
        new(
            reference.SourceReferenceId,
            reference.ArtifactId,
            reference.Scope.ToString(),
            reference.SourceKind,
            reference.SourceId,
            reference.PublicationId,
            reference.SlotId,
            reference.CreatedAt,
            reference.ExpiresAt,
            reference.DeletedAt,
            reference.IsLive(now));
}

public sealed record WorkflowExecutableSummaryView(
    string ArtifactId,
    string ArtifactHash,
    DateTimeOffset CreatedAt,
    string RootActivityType,
    int NodeCount,
    int LiveSourceReferenceCount,
    int RetainedExecutionCount);

public sealed record WorkflowExecutablesListView(IReadOnlyCollection<WorkflowExecutableSummaryView> Items);

public sealed record WorkflowExecutableNodeView(
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string? StructureKind,
    IReadOnlyCollection<WorkflowExecutableInputBindingView> InputBindings,
    IReadOnlyCollection<WorkflowExecutableChildSlotView> ChildSlots);

public sealed record WorkflowExecutableInputBindingView(string InputName, string Source, string? Summary);

public sealed record WorkflowExecutableChildSlotView(string Name, IReadOnlyCollection<WorkflowExecutableNodeView> Activities);

public sealed record WorkflowExecutableDetailsView(
    string ArtifactId,
    string ArtifactHash,
    DateTimeOffset CreatedAt,
    int LiveSourceReferenceCount,
    int RetainedExecutionCount,
    WorkflowExecutableNodeView RootActivity,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ExecutableProvenanceView(
    string ArtifactId,
    IReadOnlyCollection<ExecutableSourceReferenceView> SourceReferences,
    int RetainedExecutionCount,
    bool ProtectedFromCollection);
