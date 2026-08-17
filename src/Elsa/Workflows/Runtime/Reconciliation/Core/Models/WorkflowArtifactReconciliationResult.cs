namespace Elsa.Workflows.Runtime.Reconciliation.Core.Models;

/// <summary>How one artifact was resolved by a reconciliation pass.</summary>
public enum WorkflowArtifactImportOutcome
{
    /// <summary>The artifact was persisted and is now the live activation of its definition's slot.</summary>
    Imported,

    /// <summary>The slot already served exactly this artifact, so the pass wrote nothing. The steady state of a re-run.</summary>
    AlreadyCurrent,

    /// <summary>The artifact was valid and importable but deliberately not activated. See <see cref="WorkflowArtifactSkipReason"/>.</summary>
    Skipped,

    /// <summary>A gate refused the artifact. It was not activated, and its whole closure unit wrote nothing.</summary>
    Rejected
}

/// <summary>Why an importable artifact was nonetheless not activated.</summary>
public enum WorkflowArtifactSkipReason
{
    /// <summary>Not a skip.</summary>
    None,

    /// <summary>
    /// The slot already serves an artifact whose <c>ArtifactVersion</c> sorts at or above the candidate's, so
    /// activating would move the definition <em>backward</em> (FR-B-007 latest-wins).
    /// </summary>
    OlderVersion
}

/// <summary>
/// The named reason a rejection happened. A discriminator, not a message: the human-readable cause stays in
/// <see cref="WorkflowArtifactImportEntry.Diagnostic"/>.
/// </summary>
/// <remarks>
/// Named rather than free-text because these are operationally different incidents — a broken export
/// (<see cref="ContentHashMismatch"/>) and a runtime that is missing an activity package
/// (<see cref="UnmetRequirement"/>) need different people to act — and because tests must assert on the reason
/// without parsing prose.
/// </remarks>
public enum WorkflowArtifactRejectionKind
{
    /// <summary>Not a rejection.</summary>
    None,

    /// <summary>The envelope's own shape is invalid: no root artifact id, or a root absent from the artifact set.</summary>
    MalformedClosure,

    /// <summary>A <c>Dependencies</c> edge names an artifact the envelope does not carry. The closure is not self-contained.</summary>
    MissingArtifact,

    /// <summary>A <c>Dependencies</c> edge's declared child hash disagrees with the carried member's identity hash.</summary>
    HashMismatch,

    /// <summary>Two carried members claim the same artifact id.</summary>
    ConflictingIdentity,

    /// <summary>The carried dependency edges form a cycle.</summary>
    Cycle,

    /// <summary>
    /// A member's recomputed canonical content hash disagrees with its declared identity hash — the artifact's
    /// payload is not the payload its content-addressed id names (ADR 0038).
    /// </summary>
    ContentHashMismatch,

    /// <summary>The trigger surface recomputed from the payload disagrees with the surface the envelope carries.</summary>
    TriggerSurfaceMismatch,

    /// <summary>
    /// A member's <c>ArtifactVersion</c> is not a semantic version, so latest-wins supersession cannot order it
    /// against whatever the definition already serves (FR-B-007).
    /// </summary>
    UnorderableVersion,

    /// <summary>
    /// The same logical <c>(DefinitionId, ArtifactVersion)</c> is claimed by two different content-addressed
    /// artifacts — a broken source, since same logical identity must imply same content.
    /// </summary>
    VersionHashMismatch,

    /// <summary>This runtime cannot satisfy a declared requirement or is missing an activity type the artifact uses.</summary>
    UnmetRequirement,

    /// <summary>The artifact could not be written to the executable store.</summary>
    PersistenceFailure,

    /// <summary>
    /// The activation authority refused the transition — a concurrent writer, or a definition owned by a different
    /// activation source. The diagnostic names the owning source.
    /// </summary>
    ActivationConflict,

    /// <summary>The activation sequence failed mid-way and was compensated. The next pass retries.</summary>
    ActivationFailure
}

/// <summary>The outcome of one artifact within one reconciliation pass.</summary>
/// <param name="Origin">Where the closure came from, for operator diagnostics.</param>
/// <param name="SourceId">The reconciliation source that offered it — also the activation ownership descriptor.</param>
/// <param name="ArtifactId">
/// The content-addressed artifact id, or the closure's declared root id when the failure happened before any
/// member could be identified. Empty only when the envelope named no root at all.
/// </param>
/// <param name="DefinitionId">The workflow definition the artifact belongs to, when known.</param>
/// <param name="ArtifactVersion">The artifact's version label, when known.</param>
/// <param name="Outcome">How it resolved.</param>
/// <param name="SkipReason">Set only for <see cref="WorkflowArtifactImportOutcome.Skipped"/>.</param>
/// <param name="RejectionKind">Set only for <see cref="WorkflowArtifactImportOutcome.Rejected"/>.</param>
/// <param name="Diagnostic">The human-readable cause. Never null for a rejection.</param>
/// <param name="ActivationId">The activation id the coordinator settled on, for successful outcomes.</param>
public sealed record WorkflowArtifactImportEntry(
    string Origin,
    string SourceId,
    string ArtifactId,
    string? DefinitionId,
    string? ArtifactVersion,
    WorkflowArtifactImportOutcome Outcome,
    WorkflowArtifactSkipReason SkipReason = WorkflowArtifactSkipReason.None,
    WorkflowArtifactRejectionKind RejectionKind = WorkflowArtifactRejectionKind.None,
    string? Diagnostic = null,
    string? ActivationId = null)
{
    public static WorkflowArtifactImportEntry Imported(
        string origin,
        string sourceId,
        string artifactId,
        string definitionId,
        string artifactVersion,
        string activationId) =>
        new(origin, sourceId, artifactId, definitionId, artifactVersion, WorkflowArtifactImportOutcome.Imported, ActivationId: activationId);

    public static WorkflowArtifactImportEntry AlreadyCurrent(
        string origin,
        string sourceId,
        string artifactId,
        string definitionId,
        string artifactVersion,
        string? activationId) =>
        new(origin, sourceId, artifactId, definitionId, artifactVersion, WorkflowArtifactImportOutcome.AlreadyCurrent, ActivationId: activationId);

    public static WorkflowArtifactImportEntry Skipped(
        string origin,
        string sourceId,
        string artifactId,
        string? definitionId,
        string? artifactVersion,
        WorkflowArtifactSkipReason reason,
        string diagnostic) =>
        new(origin, sourceId, artifactId, definitionId, artifactVersion, WorkflowArtifactImportOutcome.Skipped, reason, Diagnostic: diagnostic);

    public static WorkflowArtifactImportEntry Rejected(
        string origin,
        string sourceId,
        string artifactId,
        string? definitionId,
        string? artifactVersion,
        WorkflowArtifactRejectionKind kind,
        string diagnostic) =>
        new(
            origin,
            sourceId,
            artifactId,
            definitionId,
            artifactVersion,
            WorkflowArtifactImportOutcome.Rejected,
            RejectionKind: kind,
            Diagnostic: diagnostic);
}

/// <summary>
/// The result of one complete reconciliation pass over every registered source.
/// </summary>
/// <remarks>
/// A <b>value</b>, not a throw. Rejections are entries here rather than exceptions precisely so that one broken
/// closure unit cannot take the rest of the mounted set down with it (US2-3): the pass completes, every unit is
/// accounted for, and the operator sees all the problems at once instead of only the first.
/// </remarks>
public sealed record WorkflowArtifactReconciliationResult(IReadOnlyList<WorkflowArtifactImportEntry> Entries)
{
    /// <summary>A pass that considered nothing — no sources composed, or every source empty.</summary>
    public static WorkflowArtifactReconciliationResult Empty { get; } = new([]);

    public int ImportedCount => Entries.Count(entry => entry.Outcome == WorkflowArtifactImportOutcome.Imported);

    public int AlreadyCurrentCount => Entries.Count(entry => entry.Outcome == WorkflowArtifactImportOutcome.AlreadyCurrent);

    public int SkippedCount => Entries.Count(entry => entry.Outcome == WorkflowArtifactImportOutcome.Skipped);

    public int RejectedCount => Entries.Count(entry => entry.Outcome == WorkflowArtifactImportOutcome.Rejected);

    /// <summary>True when any artifact was refused. The pass still completed — this is a report, not a failure.</summary>
    public bool HasRejections => RejectedCount > 0;

    public IEnumerable<WorkflowArtifactImportEntry> Rejections =>
        Entries.Where(entry => entry.Outcome == WorkflowArtifactImportOutcome.Rejected);
}
