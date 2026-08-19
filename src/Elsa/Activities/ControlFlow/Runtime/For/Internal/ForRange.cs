using Elsa.Activities.For.Exceptions;

namespace Elsa.Activities.For.Internal;

/// <summary>
/// The counted-loop range a <c>For</c> activity walks: a start value, a (configurable inclusive/exclusive)
/// end value, and a non-zero step. Pure, allocation-free iteration arithmetic with no runtime dependency,
/// so the boundary semantics are unit-testable in isolation (§2.23).
/// </summary>
/// <remarks>
/// <para><b>Boundary semantics.</b></para>
/// <list type="bullet">
/// <item><description>
/// <b>Start is inclusive.</b> When the range is non-empty the first index is exactly <c>Start</c>.
/// </description></item>
/// <item><description>
/// <b>End is exclusive by default</b> (the half-open <c>[Start, End)</c> convention, matching most
/// programming-language <c>for (i = start; i &lt; end; i += step)</c> loops). Set
/// <c>endInclusive: true</c> for a closed <c>[Start, End]</c> range that also visits an index landing
/// exactly on <c>End</c>.
/// </description></item>
/// <item><description>
/// <b>Step direction must agree with the range direction.</b> A positive step counts up (toward a
/// greater end); a negative step counts down (toward a lesser end). When the step points away from the
/// end — e.g. a positive step with <c>End &lt; Start</c> — the range is <b>empty</b> and the body never
/// runs (it does not throw and does not loop forever).
/// </description></item>
/// <item><description>
/// <b>A zero step is rejected.</b> It can never advance toward the end, so it is a configuration error
/// rather than an empty or infinite loop; <see cref="Create"/> throws <see cref="ForExecutionException"/>.
/// </description></item>
/// <item><description>
/// <b>An empty range visits no index.</b> <c>Start == End</c> with an exclusive end, or a step pointing
/// away from the end, yields <see cref="HasFirst"/> <c>== false</c>; the loop completes without running
/// the body.
/// </description></item>
/// </list>
/// </remarks>
public readonly record struct ForRange
{
    private ForRange(int start, int end, int step, bool endInclusive)
    {
        Start = start;
        End = end;
        Step = step;
        EndInclusive = endInclusive;
    }

    public int Start { get; }
    public int End { get; }
    public int Step { get; }
    public bool EndInclusive { get; }

    /// <summary>Builds a range, rejecting a zero step (which can never reach the end).</summary>
    public static ForRange Create(int start, int end, int step, bool endInclusive = false)
    {
        if (step == 0)
            throw new ForExecutionException("For step cannot be zero: a zero step can never advance the loop toward its end.");

        return new ForRange(start, end, step, endInclusive);
    }

    /// <summary>True when the range contains at least one index (i.e. the body runs at least once).</summary>
    public bool HasFirst => Contains(Start);

    /// <summary>
    /// The index after <paramref name="current"/>, or <c>null</c> when advancing leaves the range. The
    /// caller passes the index it just ran; a value outside the range (or one that overflows past
    /// <see cref="int"/> bounds when advanced) ends the loop.
    /// </summary>
    public int? Next(int current)
    {
        // Guard against int overflow when stepping near the numeric bounds; an overflowing advance is
        // by definition outside any representable end, so the loop ends.
        var advanced = (long)current + Step;
        if (advanced > int.MaxValue || advanced < int.MinValue)
            return null;

        var next = (int)advanced;
        return Contains(next) ? next : null;
    }

    private bool Contains(int index)
    {
        if (Step > 0)
            return EndInclusive ? index <= End : index < End;

        return EndInclusive ? index >= End : index > End;
    }
}
