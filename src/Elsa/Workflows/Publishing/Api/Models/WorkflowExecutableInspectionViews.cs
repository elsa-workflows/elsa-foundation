using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

/// <summary>
/// The scope filter accepted by the executables list surface. Mirrors the Studio "Published | Test runs | All"
/// filter (plan §3): the default view shows only artifacts that carry at least one live Published reference.
/// </summary>
public enum WorkflowExecutableListScope
{
    /// <summary>Only artifacts with at least one live Published reference (the default).</summary>
    Published,

    /// <summary>Only artifacts with at least one live TestRun reference.</summary>
    TestRuns,

    /// <summary>Every artifact with at least one reference in any scope.</summary>
    All
}

/// <summary>
/// One reference row nested under an artifact in the executables list / detail responses. A verbatim projection of
/// a <see cref="WorkflowExecutableSourceReference"/>'s provenance and lifecycle facts (ADR 0038/0040); the layout
/// sidecar is intentionally omitted here (it is large and only the detail endpoint's chosen reference carries it).
/// </summary>
public sealed record WorkflowExecutableReferenceView(
    string SourceReferenceId,
    string ArtifactId,
    string? SourceKind,
    string? SourceId,
    string? SourceVersion,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    string Scope,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DeletedAt,
    string? DeletedReason)
{
    public static WorkflowExecutableReferenceView From(WorkflowExecutableSourceReference reference) =>
        new(
            reference.SourceReferenceId,
            reference.ArtifactId,
            reference.SourceKind,
            reference.SourceId,
            reference.SourceVersion,
            reference.DefinitionId,
            reference.DefinitionVersionId,
            reference.ArtifactVersion,
            reference.CreatedAt,
            reference.PublishedAt,
            reference.Scope.ToString(),
            reference.ExpiresAt,
            reference.DeletedAt,
            reference.DeletedReason);
}

/// <summary>
/// One artifact row in the executables list (plan §3): the content-addressed artifact identity plus its references
/// nested newest-first. The top-level <c>Definition*</c>/<c>Published*</c>/<c>Source*</c> fields mirror the newest
/// reference so the current Studio table keeps working — additive evolution over the legacy summary shape.
/// </summary>
public sealed record WorkflowExecutableRowView(
    string ArtifactId,
    string ArtifactHash,
    DateTimeOffset CreatedAt,
    string RootActivityType,
    string RootActivityVersion,
    int NodeCount,
    int ResumeTargetCount,
    // Convenience mirror of the newest reference (legacy WorkflowExecutableSummary compatibility).
    string ArtifactVersion,
    string DefinitionId,
    string DefinitionVersionId,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? DeletedAt,
    string? SourceKind,
    string? SourceId,
    string? SourceVersion,
    IReadOnlyList<WorkflowExecutableReferenceView> References);

/// <summary>The executables list response: artifact rows, each carrying its nested references.</summary>
public sealed record WorkflowExecutablesListView(IReadOnlyList<WorkflowExecutableRowView> Executables);

/// <summary>
/// A node in the Execution Material tree returned by the detail endpoint (plan §3 "renderable node tree"). Carries
/// the structural identity (executable + authored ids, activity type/version), the activity-owned structure kind,
/// child slots, and input-binding <b>summaries</b> — the descriptor payload is deliberately omitted to keep the
/// response lean (secrets remain references, never literals: plan §6). Behavior-shaping literals surface as a
/// short JSON preview so the Inspector can render bindings without pulling full descriptor payloads.
/// </summary>
public sealed record WorkflowExecutableNodeView(
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string? StructureKind,
    IReadOnlyList<WorkflowExecutableInputBindingView> InputBindings,
    IReadOnlyList<WorkflowExecutableChildSlotView> ChildSlots);

/// <summary>A named child slot holding the ordered child nodes owned by a container activity contract.</summary>
public sealed record WorkflowExecutableChildSlotView(
    string Name,
    IReadOnlyList<WorkflowExecutableNodeView> Activities);

/// <summary>
/// Input-binding summary for a node: the binding source (Literal/Expression/ActivityOutput/DurableValue/Reference)
/// and a compact human-readable rendering. Literal values are truncated to a preview; expression bindings show the
/// language and a truncated expression; reference-typed values show only their type/id (never inlined secrets).
/// </summary>
public sealed record WorkflowExecutableInputBindingView(
    string InputName,
    string Source,
    string? Summary);

/// <summary>The reference the detail endpoint rendered the layout from, and how it was chosen.</summary>
public sealed record WorkflowExecutableChosenReferenceView(
    string SourceReferenceId,
    string Selection,
    IReadOnlyList<WorkflowExecutableLayoutRecord> Layout);

/// <summary>
/// The executable detail response (plan §3, P1 bridge contract §4): the artifact identity block, the full Execution
/// Material as a renderable node tree, the chosen reference's layout sidecar, and the full reference list. Assembled
/// purely from the artifact + reference stores — no workflow-definition table is consulted (the self-containment
/// that makes an artifact promotable across environments).
/// </summary>
public sealed record WorkflowExecutableDetailView(
    string ArtifactId,
    string ArtifactHash,
    DateTimeOffset CreatedAt,
    string RootActivityType,
    string RootActivityVersion,
    int NodeCount,
    int ResumeTargetCount,
    WorkflowExecutableNodeView RootActivity,
    WorkflowExecutableChosenReferenceView? ChosenReference,
    IReadOnlyList<WorkflowExecutableReferenceView> References);
