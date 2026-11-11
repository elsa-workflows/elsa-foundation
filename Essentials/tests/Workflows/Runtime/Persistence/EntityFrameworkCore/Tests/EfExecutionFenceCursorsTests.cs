using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The fence cursors of a process (#2564). Their writers do not coordinate: the lease heartbeat reports from a
/// service scope of its own while a commit reports from the drain's. These tests pin what each write does when it
/// arrives out of order, late, or for a lease that has no entry.
/// </summary>
public sealed class EfExecutionFenceCursorsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly EfExecutionFenceCursors.Key Lease = new("tenant-a", "workflow-a", "lease-a", "node-a", 1);

    [Fact]
    public void Of_two_cursors_for_one_lease_the_higher_revision_is_kept_in_either_order()
    {
        var cursors = new EfExecutionFenceCursors();

        cursors.Hold(Lease, At(5, expiresInSeconds: 80));
        cursors.Hold(Lease, At(4, expiresInSeconds: 60));
        Assert.Equal(At(5, 80), Get(cursors));

        cursors.Advance(Lease, At(6, 80));
        cursors.Advance(Lease, At(3, 60));
        Assert.Equal(At(6, 80), Get(cursors));

        cursors.Hold(Lease, At(7, 100));
        Assert.Equal(At(7, 100), Get(cursors));
    }

    [Fact]
    public void A_commit_changes_a_cursor_that_exists_and_never_creates_one()
    {
        var cursors = new EfExecutionFenceCursors();

        cursors.Advance(Lease, At(3, 60));
        cursors.Distrust(Lease);

        Assert.False(cursors.TryGet(Lease, out _));
        Assert.Equal(0, cursors.Count);
    }

    [Fact]
    public void A_distrusted_cursor_keeps_its_entry_and_takes_the_next_revision_it_is_given()
    {
        var cursors = new EfExecutionFenceCursors();
        cursors.Hold(Lease, At(5, 60));

        cursors.Distrust(Lease);
        Assert.Equal(At(0, 60), Get(cursors));

        // Any revision reported for the lease is one at which the row held it, so an older one is safe to take.
        cursors.Advance(Lease, At(4, 60));
        Assert.Equal(At(4, 60), Get(cursors));
        cursors.Distrust(Lease);
        cursors.Hold(Lease, At(6, 80));
        Assert.Equal(At(6, 80), Get(cursors));
    }

    [Fact]
    public void A_removed_lease_is_not_brought_back_by_a_commit_that_reports_late()
    {
        var cursors = new EfExecutionFenceCursors();
        cursors.Hold(Lease, At(5, 60));

        cursors.Remove(Lease);
        cursors.Advance(Lease, At(6, 60));
        cursors.Distrust(Lease);

        Assert.False(cursors.TryGet(Lease, out _));
    }

    [Fact]
    public void Leases_that_differ_in_any_part_of_their_identity_have_separate_cursors()
    {
        var cursors = new EfExecutionFenceCursors();
        EfExecutionFenceCursors.Key[] others =
        [
            Lease with { Scope = "tenant-b" },
            Lease with { WorkflowExecutionId = "workflow-b" },
            Lease with { LeaseId = "lease-b" },
            Lease with { OwnerId = "node-b" },
            Lease with { FencingToken = 2 }
        ];

        cursors.Hold(Lease, At(5, 60));

        Assert.All(others, other => Assert.False(cursors.TryGet(other, out _)));
    }

    [Fact]
    public void Cursors_past_their_lease_time_are_removed_as_new_ones_are_held()
    {
        // A lease that is never released leaves its cursor behind. Past its lease time a cursor is never used.
        var clock = new FakeTimeProvider(Now);
        var cursors = new EfExecutionFenceCursors(clock);
        var abandoned = Lease with { LeaseId = "lease-abandoned" };
        cursors.Hold(abandoned, At(1, expiresInSeconds: 60));
        cursors.Hold(Lease, At(1, expiresInSeconds: 60 * 60));
        clock.Advance(TimeSpan.FromMinutes(2));

        for (var hold = 0; hold < EfExecutionFenceCursors.SweepInterval; hold++)
            cursors.Hold(Lease, At(2 + hold, expiresInSeconds: 60 * 60));

        Assert.False(cursors.TryGet(abandoned, out _));
        Assert.Equal(At(1 + EfExecutionFenceCursors.SweepInterval, 60 * 60), Get(cursors));
        Assert.Equal(1, cursors.Count);
    }

    private static ExecutionFenceCursor At(long revision, int expiresInSeconds) => new(revision, Now, Now.AddSeconds(expiresInSeconds));

    private static ExecutionFenceCursor Get(EfExecutionFenceCursors cursors)
    {
        Assert.True(cursors.TryGet(Lease, out var cursor));
        return cursor;
    }
}
