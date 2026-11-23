namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;

/// <summary>
/// The Workflows Design EF workflow-draft row version: the
/// draft's persisted <c>LastModifiedAt</c>, projected to whole microseconds.
/// </summary>
/// <remarks>
/// <para>
/// A document store derives upgrade discovery order, the plan's expected-snapshot revision and the apply-time
/// compare-and-swap from one monotonically increasing document version. The Workflows Design EF model
/// persists no such counter for a draft: <c>WorkflowDefinitionDraft</c> has no revision column, and its
/// EF configuration deliberately leaves it without a concurrency token because ordinary draft updates
/// are last-writer-wins. Of what the model does persist, <c>LastModifiedAt</c> is the only property that
/// changes on every write and is already the module's own concurrency token on the draft's siblings —
/// the definition, the version, the draft layout and the version layout all declare
/// <c>Property(x =&gt; x.LastModifiedAt).IsConcurrencyToken()</c> — and it is already the draft lane's own
/// ordering key (<c>EfWorkflowDefinitionDraftStore</c> orders drafts by <c>LastModifiedAt</c> with a
/// unique identity tie-break). Using it here therefore reuses the Workflows Design EF concurrency and
/// ordering mechanism rather than inventing a second one.
/// </para>
/// <para>
/// The projection to microseconds is what makes the value provider-neutral. SQLite (TEXT) and SQL Server
/// (<c>datetimeoffset</c>) keep 100ns ticks, but PostgreSQL (<c>timestamp with time zone</c>) and MySQL
/// (<c>datetime(6)</c>) keep only microseconds and <em>round</em> to them on write. A raw tick count would
/// therefore be a different number after a round trip on two of the four providers. One microsecond is the
/// coarsest resolution any supported provider keeps, and it is the same floor the Elsa 3 import path
/// already compares stored instants at.
/// </para>
/// <para>
/// Monotonicity is not assumed from the wall clock. Every upgrade write stamps
/// <see cref="Next"/>, which is microsecond-aligned and strictly greater than the instant the row was
/// read at, so a stored revision always advances even when the system clock did not — a real hazard,
/// because <c>DateTimeOffset.UtcNow</c> has ~15ms granularity on Windows. Alignment also makes the write
/// idempotent under provider rounding: the value read back is bit-identical to the value written, so the
/// revision reported to the caller is exactly the one another process will observe.
/// </para>
/// </remarks>
public static class EfWorkflowDraftRevision
{
    /// <summary>Ticks per microsecond. One microsecond is the storage floor across the four providers.</summary>
    private const long TicksPerMicrosecond = 10;

    /// <summary>The revision a persisted <paramref name="lastModifiedAt"/> represents.</summary>
    public static long Of(DateTimeOffset lastModifiedAt) => lastModifiedAt.UtcTicks / TicksPerMicrosecond;

    /// <summary>The microsecond-aligned UTC instant a revision represents. Round-trips <see cref="Of"/>.</summary>
    public static DateTimeOffset ToInstant(long revision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        return new DateTimeOffset(checked(revision * TicksPerMicrosecond), TimeSpan.Zero);
    }

    /// <summary>
    /// The instant an upgrade write stamps on a draft it observed at <paramref name="observed"/>. It is
    /// microsecond-aligned, and strictly later than <paramref name="observed"/> even when
    /// <paramref name="now"/> is not, so the draft's revision is strictly increasing across upgrades.
    /// </summary>
    public static DateTimeOffset Next(DateTimeOffset observed, DateTimeOffset now)
    {
        var observedRevision = Of(observed);
        var candidate = Of(now);
        return ToInstant(candidate > observedRevision ? candidate : checked(observedRevision + 1));
    }

    /// <summary>The microsecond-aligned instant a newly created draft is stamped with.</summary>
    public static DateTimeOffset Align(DateTimeOffset now) => ToInstant(Of(now));
}
