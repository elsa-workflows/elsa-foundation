using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Self-contained, per-publish record pointing at a content-addressed <see cref="WorkflowExecutable"/> artifact
/// (ADR 0038/0039/0040). Behaviorally identical publishes produce distinct references to the same artifact
/// ("image-and-tags" in a container registry).
/// </summary>
/// <remarks>
/// Everything that is not behavior lives here: source identity (which may dangle across environments), the
/// artifact-version label, publish/creation timestamps, the reference scope and optional expiry, retirement
/// facts, publication/slot provenance for Published references, and the embedded <b>Layout Sidecar</b>
/// (<see cref="Layout"/>). TestRun references remain publication- and slot-less. The layout is a verbatim publish-time
/// copy of the definition version's graph geometry (ADR 0035 discipline: opaque, never canonicalized) and
/// NEVER contributes to the artifact hash — visual arrangement is not behavior (ADR 0039).
/// </remarks>
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
    string? PublicationId = null,
    string? SlotId = null,
    ExecutableLayoutSidecar? LayoutSidecar = null)
{
    /// <summary>The publish-time layout sidecar copied from the definition version's layout store; may be empty.</summary>
    public IReadOnlyList<WorkflowExecutableLayoutRecord> Layout { get; init; } = Layout ?? [];

    /// <summary>Boundary-scoped historical layout used for reusable-activity click-through inspection.</summary>
    public ExecutableLayoutSidecar LayoutSidecar { get; init; } = LayoutSidecar ?? ExecutableLayoutSidecar.Empty;

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
