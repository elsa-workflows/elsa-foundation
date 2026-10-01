using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// One run's facts: the family, the version it upgrades to, its declaration and tables, and the finish record standing as
/// far as it knows, which it examined when it began and reads again before a pass acts on a row. An audit is a run whose
/// target is the completion it examines.
/// </summary>
internal sealed class EfSchemaBackfillRun(
    EfSchemaChain chain,
    string target,
    SchemaFinishRecord? completion,
    EfSchemaFamilyDescriptor declaration,
    IReadOnlyList<EfSchemaStampedTable> tables)
{
    public EfSchemaChain Chain { get; } = chain;

    public string Family => Chain.Family;

    public string Target { get; } = target;

    public int TargetAt { get; } = SchemaVersionChain.PositionOf(chain.ReadableVersions, target);

    public EfSchemaFamilyDescriptor Declaration { get; } = declaration;

    public IReadOnlyList<EfSchemaStampedTable> Tables { get; } = tables;

    /// <summary>The versions this host reads before the target: the stamps a pass selects.</summary>
    public IReadOnlyList<string> Below { get; } = BelowOf(chain, target);

    /// <summary>
    /// The finish record standing as far as this run knows (FR-018): the one it examined, until a withdrawal leaves none, or
    /// a fresh read finds another standing in its place, such as one another worker recorded while this run went on. A row
    /// below it is a straggler.
    /// </summary>
    public SchemaFinishRecord? Completion { get; set; } = completion;

    /// <summary>
    /// Whether <see cref="Completion"/> is at or after the target, so every row a pass selects lies below it; otherwise a
    /// completion another worker recorded since may cover the next row, and only a fresh read can tell.
    /// </summary>
    public bool CompletionCoversTarget =>
        Completion is not null && SchemaVersionChain.PositionOf(Chain.ReadableVersions, Completion.CompletionVersion) >= TargetAt;

    /// <summary>Whether a pass of this run rewrites <paramref name="table"/>'s rows: it is not content-addressed, and the family names a rewriter.</summary>
    public bool Rewrites(EfSchemaStampedTable table) => !table.ContentAddressed && Declaration.Rewriter is not null;

    /// <summary>Why the family's rewriter cannot be constructed, once a row has found out.</summary>
    public string? RewriterFault { get; set; }

    /// <summary>Whether a row stamped <paramref name="stamp"/> lies below the completion that stands as far as this run knows.</summary>
    public bool IsBelowCompletion(string? stamp)
    {
        if (Completion is null)
            return false;
        var position = SchemaVersionChain.PositionOf(Chain.ReadableVersions, stamp ?? "");
        return position >= 0 && position < SchemaVersionChain.PositionOf(Chain.ReadableVersions, Completion.CompletionVersion);
    }

    /// <summary>The versions <paramref name="chain"/> reads before <paramref name="version"/>, or none when it cannot place it.</summary>
    public static IReadOnlyList<string> BelowOf(EfSchemaChain chain, string version) =>
        chain.ReadableVersions.Take(Math.Max(0, SchemaVersionChain.PositionOf(chain.ReadableVersions, version))).ToArray();
}

/// <summary>One verification pass (FR-013): when it began and ended, where the finish history stood when the settle margin it followed began, the rows it found below the target, and what blocks completion.</summary>
/// <param name="HistoryMark">
/// How many finish history entries stood when the settle margin the pass followed began (FR-012), so a withdrawal since,
/// before the pass or during it, is told apart by position, not by clock: two hosts' clocks, or one that has not moved,
/// cannot order them.
/// </param>
internal sealed record EfSchemaBackfillVerification(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int HistoryMark,
    long Found,
    IReadOnlyList<EfSchemaBackfillBlocker> Blockers);

/// <summary>
/// This worker's run of a family stops for <see cref="Reason"/>: it no longer owns the run, or there is nothing left for it
/// to do. It is not a failure: the round says why on the family's status, and releases the claim the run held, so the
/// worker that owns the family now, if any, finishes it.
/// </summary>
internal sealed class EfSchemaBackfillStoppedException(EfSchemaBackfillStop reason, string message) : Exception(message)
{
    public EfSchemaBackfillStop Reason { get; } = reason;
}

/// <summary>Why a worker's run of a family stops before it is done (spec 186, FR-008; spec 183, FR-007).</summary>
internal enum EfSchemaBackfillStop
{
    /// <summary>Another worker's claim holds, taken over since this worker's own lapsed.</summary>
    TakenOver,

    /// <summary>This worker's member has lapsed from the fleet, so others may take its work over.</summary>
    Lapsed,

    /// <summary>The completion moved to or past this worker's target while its run went on.</summary>
    CompletionMovedOn,

    /// <summary>The record no longer has anywhere to hold a claim, which is reported as an error.</summary>
    Unclaimable
}
