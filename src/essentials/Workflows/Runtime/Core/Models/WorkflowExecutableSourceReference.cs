using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Self-contained, per-publish record pointing at a content-addressed <see cref="WorkflowExecutable"/> artifact
/// (ADR 0038/0039/0040). Behaviorally identical publishes produce distinct references to the same artifact
/// ("image-and-tags" in a container registry).
/// </summary>
/// <remarks>
/// Everything that is not behavior lives here: source identity (which may dangle across environments), the
/// artifact-version label, publish/creation timestamps, the reference scope and optional expiry, retirement
/// facts, activation/slot provenance for Published references, and the embedded <b>Layout Sidecar</b>
/// (<see cref="Layout"/>). TestRun references remain activation- and slot-less. The layout is a verbatim publish-time
/// copy of the definition version's graph geometry (ADR 0035 discipline: opaque, never canonicalized) and
/// NEVER contributes to the artifact hash — visual arrangement is not behavior (ADR 0039).
/// </remarks>
[method: JsonConstructor]
public sealed record WorkflowExecutableSourceReference(
    string SourceReferenceId,
    string ArtifactId,
    string SourceKind,
    string SourceId,
    string? SourceVersion,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    WorkflowExecutableReferenceScope Scope,
    DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? DeletedAt = null,
    string? DeletedReason = null,
    IReadOnlyList<WorkflowExecutableLayoutRecord>? Layout = null,
    string? ActivationId = null,
    string? SlotId = null,
    ExecutableLayoutSidecar? LayoutSidecar = null,
    IReadOnlyList<WorkflowExecutableAuthoredInputRecord>? AuthoredInputs = null,
    string? TenantId = null,
    IReadOnlyList<WorkflowExecutableActivityPresentationRecord>? ActivityPresentation = null)
{
    /// <summary>
    /// Preserves the authored-input constructor introduced before tenant scope became part of the reference.
    /// </summary>
    public WorkflowExecutableSourceReference(
        string sourceReferenceId,
        string artifactId,
        string sourceKind,
        string sourceId,
        string? sourceVersion,
        string definitionId,
        string definitionVersionId,
        string artifactVersion,
        DateTimeOffset createdAt,
        DateTimeOffset? publishedAt,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset? expiresAt,
        DateTimeOffset? deletedAt,
        string? deletedReason,
        IReadOnlyList<WorkflowExecutableLayoutRecord>? layout,
        string? activationId,
        string? slotId,
        IReadOnlyList<WorkflowExecutableAuthoredInputRecord>? authoredInputs)
        : this(
            sourceReferenceId,
            artifactId,
            sourceKind,
            sourceId,
            sourceVersion,
            definitionId,
            definitionVersionId,
            artifactVersion,
            createdAt,
            publishedAt,
            scope,
            expiresAt,
            deletedAt,
            deletedReason,
            layout,
            activationId,
            slotId,
            LayoutSidecar: null,
            authoredInputs,
            TenantId: null)
    {
    }

    /// <summary>
    /// Preserves the pre-tenant constructor for already-compiled callers while the primary constructor provides
    /// the additive tenant member.
    /// </summary>
    public WorkflowExecutableSourceReference(
        string sourceReferenceId,
        string artifactId,
        string sourceKind,
        string sourceId,
        string? sourceVersion,
        string definitionId,
        string definitionVersionId,
        string artifactVersion,
        DateTimeOffset createdAt,
        DateTimeOffset? publishedAt,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset? expiresAt,
        DateTimeOffset? deletedAt,
        string? deletedReason,
        IReadOnlyList<WorkflowExecutableLayoutRecord>? layout,
        string? activationId,
        string? slotId)
        : this(
            sourceReferenceId,
            artifactId,
            sourceKind,
            sourceId,
            sourceVersion,
            definitionId,
            definitionVersionId,
            artifactVersion,
            createdAt,
            publishedAt,
            scope,
            expiresAt,
            deletedAt,
            deletedReason,
            layout,
            activationId,
            slotId,
            LayoutSidecar: null,
            AuthoredInputs: null,
            TenantId: null)
    {
    }

    /// <summary>The publish-time layout sidecar copied from the definition version's layout store; may be empty.</summary>
    public IReadOnlyList<WorkflowExecutableLayoutRecord> Layout { get; init; } = Layout ?? [];

    /// <summary>Boundary-scoped historical layout used for reusable-activity click-through inspection.</summary>
    public ExecutableLayoutSidecar LayoutSidecar { get; init; } = LayoutSidecar ?? ExecutableLayoutSidecar.Empty;

    /// <summary>
    /// Publish-time authored input sources. These are source provenance, not execution material, and therefore
    /// live on this per-publish reference and never contribute to the content-addressed artifact hash.
    /// </summary>
    public IReadOnlyList<WorkflowExecutableAuthoredInputRecord> AuthoredInputs { get; init; } = AuthoredInputs ?? [];

    /// <summary>
    /// Frozen author-facing presentation keyed by flattened executable node id. This is source
    /// provenance and never contributes to the content-addressed artifact hash.
    /// </summary>
    public IReadOnlyList<WorkflowExecutableActivityPresentationRecord> ActivityPresentation { get; init; } =
        ActivityPresentation ?? [];

    /// <summary>Preserves the pre-tenant positional deconstruction shape including the reusable-activity layout sidecar.</summary>
    public void Deconstruct(
        out string sourceReferenceId,
        out string artifactId,
        out string sourceKind,
        out string sourceId,
        out string? sourceVersion,
        out string definitionId,
        out string definitionVersionId,
        out string artifactVersion,
        out DateTimeOffset createdAt,
        out DateTimeOffset? publishedAt,
        out WorkflowExecutableReferenceScope scope,
        out DateTimeOffset? expiresAt,
        out DateTimeOffset? deletedAt,
        out string? deletedReason,
        out IReadOnlyList<WorkflowExecutableLayoutRecord> layout,
        out string? activationId,
        out string? slotId,
        out ExecutableLayoutSidecar layoutSidecar,
        out IReadOnlyList<WorkflowExecutableAuthoredInputRecord> authoredInputs)
    {
        sourceReferenceId = SourceReferenceId;
        artifactId = ArtifactId;
        sourceKind = SourceKind;
        sourceId = SourceId;
        sourceVersion = SourceVersion;
        definitionId = DefinitionId;
        definitionVersionId = DefinitionVersionId;
        artifactVersion = ArtifactVersion;
        createdAt = CreatedAt;
        publishedAt = PublishedAt;
        scope = Scope;
        expiresAt = ExpiresAt;
        deletedAt = DeletedAt;
        deletedReason = DeletedReason;
        layout = Layout;
        activationId = ActivationId;
        slotId = SlotId;
        layoutSidecar = LayoutSidecar;
        authoredInputs = AuthoredInputs;
    }

    /// <summary>Preserves the authored-input positional deconstruction shape for source compatibility.</summary>
    public void Deconstruct(
        out string sourceReferenceId,
        out string artifactId,
        out string sourceKind,
        out string sourceId,
        out string? sourceVersion,
        out string definitionId,
        out string definitionVersionId,
        out string artifactVersion,
        out DateTimeOffset createdAt,
        out DateTimeOffset? publishedAt,
        out WorkflowExecutableReferenceScope scope,
        out DateTimeOffset? expiresAt,
        out DateTimeOffset? deletedAt,
        out string? deletedReason,
        out IReadOnlyList<WorkflowExecutableLayoutRecord> layout,
        out string? activationId,
        out string? slotId,
        out IReadOnlyList<WorkflowExecutableAuthoredInputRecord> authoredInputs)
    {
        sourceReferenceId = SourceReferenceId;
        artifactId = ArtifactId;
        sourceKind = SourceKind;
        sourceId = SourceId;
        sourceVersion = SourceVersion;
        definitionId = DefinitionId;
        definitionVersionId = DefinitionVersionId;
        artifactVersion = ArtifactVersion;
        createdAt = CreatedAt;
        publishedAt = PublishedAt;
        scope = Scope;
        expiresAt = ExpiresAt;
        deletedAt = DeletedAt;
        deletedReason = DeletedReason;
        layout = Layout;
        activationId = ActivationId;
        slotId = SlotId;
        authoredInputs = AuthoredInputs;
    }

    /// <summary>Preserves the pre-tenant positional deconstruction shape for source compatibility.</summary>
    public void Deconstruct(
        out string sourceReferenceId,
        out string artifactId,
        out string sourceKind,
        out string sourceId,
        out string? sourceVersion,
        out string definitionId,
        out string definitionVersionId,
        out string artifactVersion,
        out DateTimeOffset createdAt,
        out DateTimeOffset? publishedAt,
        out WorkflowExecutableReferenceScope scope,
        out DateTimeOffset? expiresAt,
        out DateTimeOffset? deletedAt,
        out string? deletedReason,
        out IReadOnlyList<WorkflowExecutableLayoutRecord> layout,
        out string? activationId,
        out string? slotId)
    {
        sourceReferenceId = SourceReferenceId;
        artifactId = ArtifactId;
        sourceKind = SourceKind;
        sourceId = SourceId;
        sourceVersion = SourceVersion;
        definitionId = DefinitionId;
        definitionVersionId = DefinitionVersionId;
        artifactVersion = ArtifactVersion;
        createdAt = CreatedAt;
        publishedAt = PublishedAt;
        scope = Scope;
        expiresAt = ExpiresAt;
        deletedAt = DeletedAt;
        deletedReason = DeletedReason;
        layout = Layout;
        activationId = ActivationId;
        slotId = SlotId;
    }

    /// <summary>True while the reference is neither retired nor past its expiry at <paramref name="now"/>.</summary>
    public bool IsLive(DateTimeOffset now) => DeletedAt is null && !IsExpired(now);

    /// <summary>True when the reference carries an expiry that has passed at <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiresAt && expiresAt <= now;

    /// <summary>Returns a retired copy stamped with the given deletion facts.</summary>
    public WorkflowExecutableSourceReference Retire(DateTimeOffset deletedAt, string? reason = null) =>
        this with { DeletedAt = deletedAt, DeletedReason = reason };
}

/// <summary>
/// The scope a <see cref="WorkflowExecutableSourceReference"/> was created under (ADR 0040). Scope and expiry
/// are per-publish facts that live on the reference, not on the immutable artifact.
/// </summary>
public enum WorkflowExecutableReferenceScope
{
    /// <summary>A durable publish reference. Does not expire.</summary>
    Published,

    /// <summary>An expiring reference minted from a draft snapshot for a Test Run.</summary>
    TestRun
}

/// <summary>
/// One node of the Layout Sidecar embedded on a <see cref="WorkflowExecutableSourceReference"/>: the verbatim,
/// runtime-owned copy of a design-time <c>DesignMetadataRecord</c> made at publish time. Kept structurally
/// identical to the Design record but declared in the runtime layer so the reference model does not depend on
/// Design persistence; <c>AdditionalProperties</c> stays an opaque <see cref="JsonElement"/> (ADR 0035 D3),
/// stored as-received and never canonicalized or hashed.
/// </summary>
public sealed record WorkflowExecutableLayoutRecord(
    string NodeId,
    double X,
    double Y,
    double? Width = null,
    double? Height = null,
    JsonElement? AdditionalProperties = null);

/// <summary>
/// One opaque authored input source captured for a source reference. <see cref="Value"/> is copied as JSON so
/// future expression kinds can round-trip without the runtime model interpreting or normalizing their payload.
/// </summary>
public sealed record WorkflowExecutableAuthoredInputRecord(
    string ExecutableNodeId,
    string InputKey,
    string? ExpressionType,
    JsonElement Value,
    bool IsSensitive = false);

/// <summary>Frozen presentation for one node in a source reference's flattened executable graph.</summary>
public sealed record WorkflowExecutableActivityPresentationRecord(
    string ExecutableNodeId,
    string? DisplayName = null,
    string? Description = null);
