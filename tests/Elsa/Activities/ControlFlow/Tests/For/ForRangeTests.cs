using Elsa.Activities.For.Exceptions;
using Elsa.Activities.For.Internal;
using Xunit;

namespace Elsa.Activities.For.Tests;

/// <summary>
/// Focused unit coverage for the counted-loop range arithmetic (§2.23). Locks the documented boundary
/// semantics — start inclusive, end exclusive/inclusive, step direction, zero/negative step, empty range,
/// and overflow-safe advance — independently of the runtime.
/// </summary>
public sealed class ForRangeTests
{
    [Fact]
    public void Walk_AscendingExclusiveEnd_VisitsStartInclusive_EndExclusive()
    {
        Assert.Equal([0, 1, 2], Walk(start: 0, end: 3, step: 1));
    }

    [Fact]
    public void Walk_AscendingInclusiveEnd_AlsoVisitsEnd()
    {
        Assert.Equal([0, 1, 2, 3], Walk(start: 0, end: 3, step: 1, endInclusive: true));
    }

    [Fact]
    public void Walk_StepGreaterThanOne_SkipsBetweenIndices()
    {
        Assert.Equal([1, 3, 5], Walk(start: 1, end: 6, step: 2));
    }

    [Fact]
    public void Walk_InclusiveEndNotLandedOnExactly_StopsBeforeEnd()
    {
        // step 2 over [1,6] lands on 1,3,5; 7 would overshoot the inclusive end, so it stops at 5.
        Assert.Equal([1, 3, 5], Walk(start: 1, end: 6, step: 2, endInclusive: true));
    }

    [Fact]
    public void Walk_NegativeStep_CountsDown()
    {
        Assert.Equal([5, 4, 3, 2, 1], Walk(start: 5, end: 0, step: -1));
    }

    [Fact]
    public void Walk_NegativeStepInclusiveEnd_VisitsEnd()
    {
        Assert.Equal([5, 4, 3, 2, 1, 0], Walk(start: 5, end: 0, step: -1, endInclusive: true));
    }

    [Fact]
    public void HasFirst_IsFalse_WhenStartEqualsExclusiveEnd()
    {
        Assert.False(ForRange.Create(start: 3, end: 3, step: 1).HasFirst);
        Assert.Empty(Walk(start: 3, end: 3, step: 1));
    }

    [Fact]
    public void HasFirst_IsTrue_WhenStartEqualsInclusiveEnd()
    {
        Assert.Equal([3], Walk(start: 3, end: 3, step: 1, endInclusive: true));
    }

    [Fact]
    public void Walk_PositiveStepButEndBelowStart_IsEmpty()
    {
        // Step points away from the end: empty range, no iterations, no throw, no infinite loop.
        Assert.Empty(Walk(start: 5, end: 0, step: 1));
    }

    [Fact]
    public void Walk_NegativeStepButEndAboveStart_IsEmpty()
    {
        Assert.Empty(Walk(start: 0, end: 5, step: -1));
    }

    [Fact]
    public void Create_Throws_OnZeroStep()
    {
        Assert.Throws<ForExecutionException>(() => ForRange.Create(start: 0, end: 5, step: 0));
    }

    [Fact]
    public void Next_NearIntMaxValue_DoesNotOverflow()
    {
        // Advancing past int.MaxValue must end the loop rather than wrap around.
        var range = ForRange.Create(start: int.MaxValue - 1, end: int.MaxValue, step: 2, endInclusive: true);
        Assert.True(range.HasFirst);
        Assert.Null(range.Next(int.MaxValue - 1));
    }

    private static IReadOnlyList<int> Walk(int start, int end, int step, bool endInclusive = false)
    {
        var range = ForRange.Create(start, end, step, endInclusive);
        var visited = new List<int>();

        if (!range.HasFirst)
            return visited;

        var current = start;
        while (true)
        {
            visited.Add(current);

            // Defensive cap so a logic regression can't hang the test run.
            Assert.True(visited.Count < 10_000, "ForRange produced an unexpectedly long sequence.");

            if (range.Next(current) is not { } next)
                break;

            current = next;
        }

        return visited;
    }
}
