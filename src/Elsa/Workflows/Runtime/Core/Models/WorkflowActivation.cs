namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Explicit ownership descriptor for a definition's activation: which path activated it.
/// </summary>
/// <remarks>
/// <para>
/// Ownership is a <b>first-class field</b> on the slot and MUST NOT be inferred from activation-id
/// prefixes (FR-B-006, 2026-08-15 architect review). Prefixes such as <c>import:{sourceId}:…</c> or
/// <c>publication-…</c> MAY exist for log readability, but prefix-sniffing was the explicitly
/// rejected design: activation ids are opaque strings, and a guard that parses them breaks the
/// moment an id format changes.
/// </para>
/// <para>
/// Note on naming: <c>…Source</c> here is an ownership descriptor record, NOT §E6 R4's pull-contract
/// suffix (<c>I…Source</c> = returns items). The name is spec-pinned; surfaced for reviewer judgment
/// per research D8.
/// </para>
/// </remarks>
public sealed record WorkflowActivationSource(string Kind, string? SourceId = null)
{
    /// <summary>The publish pipeline on a design/build engine.</summary>
    public const string PublishingKind = "publishing";

    /// <summary>The runtime-side executable artifact reconciler.</summary>
    public const string ArtifactReconciliationKind = "artifact-reconciliation";

    public static WorkflowActivationSource Publishing { get; } = new(PublishingKind);

    public static WorkflowActivationSource ArtifactReconciliation(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return new(ArtifactReconciliationKind, sourceId);
    }

    /// <summary>
    /// True when <paramref name="other"/> is the same activation owner. Both the kind and the source
    /// id must match: two artifact-reconciliation sources pointed at different folders are different
    /// owners, so one cannot silently take over the other's definitions.
    /// </summary>
    public bool IsSameOwnerAs(WorkflowActivationSource other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return StringComparer.Ordinal.Equals(Kind, other.Kind) &&
               StringComparer.Ordinal.Equals(SourceId, other.SourceId);
    }

    public string Describe() => SourceId is null ? Kind : $"{Kind}:{SourceId}";
}

/// <summary>
/// The sole start authority for one named activation lane of a workflow definition.
/// </summary>
/// <remarks>
/// Supersedes publishing's <c>PublicationSlot</c>. Deliberately carries no <c>ArtifactId</c>: the
/// slot answers "which activation is live and who owns it", and the artifact behind that activation
/// is resolved from its minted source reference. That keeps the same-artifact idempotency check in
/// the coordinator (which already reads references) rather than duplicating artifact state here.
/// <c>SlotId</c> keeps its name — it is an §E6-protected neutral noun.
/// </remarks>
public sealed record WorkflowActivationSlot(
    string SlotId,
    string WorkflowDefinitionId,
    string SlotName,
    string? ActiveActivationId,
    WorkflowActivationSource? Source,
    long Revision,
    DateTimeOffset UpdatedAt);

/// <summary>Creates deterministic, unambiguous slot identifiers without coupling Core to a persistence provider.</summary>
public static class WorkflowActivationSlotIdentity
{
    public static string Create(string workflowDefinitionId, string slotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        return $"activation-slot:{workflowDefinitionId.Length}:{workflowDefinitionId}:{slotName.Length}:{slotName}";
    }
}

/// <summary>
/// Whether an activation request is entitled to claim a slot a <b>different</b> source owns.
/// </summary>
/// <remarks>
/// <para>
/// The authority decides ownership from the slot's <see cref="WorkflowActivationSource"/> and from this intent
/// alone. It deliberately knows <b>nothing</b> about which sources exist: a rule of the shape
/// <c>if (source.Kind == "publishing")</c> inside the runtime would invert §E2.2 and make the activation ledger
/// responsible for knowing its callers. The caller declares the intent because only the caller knows whether it
/// is acting on an explicit operator command; the authority honours it generically.
/// </para>
/// <para>
/// Adopted for T118 (Joey, 2026-08-17, amending FR-B-006): publishing is an explicit operator command and passes
/// <see cref="TakeOver"/>; the artifact reconciler is a boot-time declarative import and never does. That
/// asymmetry — and only that asymmetry — is what makes "explicit wins, and the next shell reload does not quietly
/// revert it" expressible without the runtime naming either party.
/// </para>
/// </remarks>
public enum WorkflowActivationOwnershipIntent
{
    /// <summary>
    /// The default. A slot owned by a different source is refused with
    /// <see cref="WorkflowActivationConflict.ForeignSource"/>.
    /// </summary>
    RespectExistingOwner,

    /// <summary>
    /// The caller claims the slot even when a different source owns it, and becomes its new owner. Reserved for
    /// requests that carry an explicit operator decision.
    /// </summary>
    TakeOver
}

/// <summary>Why a slot transition was refused.</summary>
public enum WorkflowActivationConflict
{
    /// <summary>No conflict — the transition applied.</summary>
    None,

    /// <summary>The caller's expected revision no longer matches: another writer moved the slot first.</summary>
    RevisionMismatch,

    /// <summary>
    /// A different activation source owns this definition, and the request did not carry
    /// <see cref="WorkflowActivationOwnershipIntent.TakeOver"/>. A loud rejection, never a silent takeover.
    /// </summary>
    ForeignSource
}

/// <summary>Outcome of a CAS-guarded slot transition.</summary>
/// <param name="ReplacedSource">
/// The source that owned the slot before this transition, or <see langword="null"/> when it was unowned. Only a
/// takeover can make this differ from the requesting source, and the coordinator's compensation needs it: putting
/// a displaced activation back means restoring its <b>owner</b> too, or a rolled-back takeover would leave the
/// predecessor serving under the claimant's name — invisible until the next pass was wrongly refused.
/// </param>
public sealed record WorkflowActivationTransition(
    bool Succeeded,
    WorkflowActivationSlot Slot,
    string? ReplacedActivationId = null,
    WorkflowActivationConflict Conflict = WorkflowActivationConflict.None,
    string? Diagnostic = null,
    WorkflowActivationSource? ReplacedSource = null);

/// <summary>A request to make one activation live for a definition's slot.</summary>
/// <param name="OwnershipIntent">
/// Whether this request may claim a slot a different source owns. Defaults to
/// <see cref="WorkflowActivationOwnershipIntent.RespectExistingOwner"/>, so a takeover is always something a
/// caller states rather than something it can fall into.
/// </param>
public sealed record WorkflowActivationSlotRequest(
    string WorkflowDefinitionId,
    string SlotName,
    string ActivationId,
    WorkflowActivationSource Source,
    long ExpectedRevision,
    DateTimeOffset UpdatedAt,
    WorkflowActivationOwnershipIntent OwnershipIntent = WorkflowActivationOwnershipIntent.RespectExistingOwner);
