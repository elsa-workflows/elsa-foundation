using Elsa.Persistence.EntityFramework.SchemaFinalization;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// One family's post-finalization backfill as this host's worker last saw it (spec 186, FR-021): what it is doing, the
/// version it upgrades to, how far it has got, the counted members its settle condition waits for, and what blocks
/// completion. The completion version itself is the finish record's, which the gate's status carries beside this.
/// </summary>
/// <param name="TargetVersion">The version the run upgrades to: this host's write version when it began, or null when idle.</param>
/// <param name="RowsRewritten">Rows this worker rewrote in the current run.</param>
/// <param name="SettleWaitingFor">
/// The counted members that have not reported observing the target, for operator surfaces only (spec 182, FR-011).
/// </param>
/// <param name="Blockers">What keeps the family from being recorded complete at the target (FR-006, FR-011a and FR-011b).</param>
/// <param name="ClaimedBy">Another worker's live claim on the run, when this one is leaving it alone (FR-008).</param>
/// <param name="Detail">A sentence on the state, for operators: why a run stopped, or what it waits for.</param>
public sealed record EfSchemaBackfillStatus(
    string Family,
    EfSchemaBackfillState State,
    string? TargetVersion,
    long RowsRewritten,
    DateTimeOffset? RunStartedAt,
    IReadOnlyList<string> SettleWaitingFor,
    IReadOnlyList<EfSchemaBackfillBlocker> Blockers,
    SchemaBackfillClaim? ClaimedBy,
    DateTimeOffset? LastAuditAt,
    string? Detail)
{
    /// <summary>A family this worker has not looked at yet.</summary>
    public static EfSchemaBackfillStatus Idle(string family) => new(family, EfSchemaBackfillState.Idle, null, 0, null, [], [], null, null, null);

    /// <summary>Whether content-addressed rows below the target keep the family from ever being complete at it (FR-011b).</summary>
    public bool BlockedByContentAddressedRows => Blockers.Any(blocker => blocker.Kind == EfSchemaBackfillBlockerKind.ContentAddressed);
}

/// <summary>What a family's backfill is doing on this host.</summary>
public enum EfSchemaBackfillState
{
    /// <summary>Nothing observed yet, or the family's writes are refused on this host.</summary>
    Idle,

    /// <summary>The finish record names this host's write version: only the audit runs (FR-018).</summary>
    Complete,

    /// <summary>The upgrade pass is rewriting rows below the target (FR-005).</summary>
    Upgrading,

    /// <summary>Waiting for every counted member to report the target, and then for the settle margin (FR-012).</summary>
    Settling,

    /// <summary>The verification pass is proving no row below the target remains (FR-013).</summary>
    Verifying,

    /// <summary>Skew, corruption, content-addressed rows, or a missing rewriter keep the family from completion.</summary>
    Blocked,

    /// <summary>Another worker holds a live claim on the run, so this one leaves it alone (FR-008).</summary>
    ClaimedElsewhere
}

/// <summary>
/// One reason a family cannot be recorded complete: its kind, the table, how many rows, and a sentence naming the family,
/// the table and, for corruption, the rows (FR-006).
/// </summary>
public sealed record EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind Kind, string? Table, long Count, string Detail);

/// <summary>Why a family cannot be recorded complete.</summary>
public enum EfSchemaBackfillBlockerKind
{
    /// <summary>Rows whose stamp is missing or outside this host's readable set: never rewritten (FR-006).</summary>
    Skew,

    /// <summary>Rows an upcaster failed on: never rewritten, never skipped silently (FR-006).</summary>
    Corruption,

    /// <summary>Content-addressed rows below the target, which nothing may upgrade (FR-011a, FR-011b).</summary>
    ContentAddressed,

    /// <summary>Rows below the target in a family that names no rewriter, or one that cannot be constructed (FR-004).</summary>
    NoRewriter,

    /// <summary>No fleet is composed, so the settle condition can never be shown to hold (FR-012).</summary>
    NoFleet
}
