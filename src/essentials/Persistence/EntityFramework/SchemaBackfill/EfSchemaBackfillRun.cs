using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// One run's facts: the family, the version it upgrades to, its declaration and tables, and the finish record it examined
/// when it began, which stands as far as this run knows until the run withdraws it. An audit is a run whose target is the
/// completion it examines.
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
    /// another is found standing in its place. A row below it is a straggler.
    /// </summary>
    public SchemaFinishRecord? Completion { get; set; } = completion;

    /// <summary>Whether a pass of this run rewrites <paramref name="table"/>'s rows: it is not content-addressed, and the family names a rewriter.</summary>
    public bool Rewrites(EfSchemaStampedTable table) => !table.ContentAddressed && Declaration.Rewriter is not null;

    public DateTimeOffset? ClaimRenewedAt { get; set; }

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

/// <summary>One verification pass (FR-013): when it began and ended, where the finish history stood as it began, the rows it found below the target, and what blocks completion.</summary>
/// <param name="HistoryMark">
/// How many finish history entries stood when the pass began, so a withdrawal during it is told apart by position, not by
/// clock: two hosts' clocks, or one that has not moved, cannot order them.
/// </param>
internal sealed record EfSchemaBackfillVerification(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int HistoryMark,
    long Found,
    IReadOnlyList<EfSchemaBackfillBlocker> Blockers);

/// <summary>This worker's claim lapsed and another worker took the run over: this one stops, which spares the other its reads.</summary>
internal sealed class EfSchemaBackfillClaimLostException(string message) : Exception(message);
