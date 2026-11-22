using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Spec 1229 FR-016, T003a/T004: the commit-sequence range and lost-update count are store columns, read without row
// documents, and the rows of an instance page by their range.
public sealed partial class EfActivityExecutionStoresTests
{
    [Fact]
    public async Task Rows_page_by_commit_sequence_range_and_lost_updates_sum_from_the_column()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.State.SaveAsync(Ranged("wf", "a", 1, 3, lostUpdates: 0));
        await fixture.State.SaveAsync(Ranged("wf", "b", 2, 9, lostUpdates: 2));
        await fixture.State.SaveAsync(Ranged("wf", "c", 5, 6, lostUpdates: 1));
        await fixture.State.SaveAsync(State("wf", "legacy", 4));
        await fixture.State.SaveAsync(Ranged("other-wf", "a", 1, 9, lostUpdates: 5));
        await new EfActivityExecutionStateStore(fixture.Context, new Accessor("tenant-b"), fixture.RecoveryContinuationCodec)
            .SaveAsync(Ranged("wf", "foreign", 1, 9, lostUpdates: 7));

        var reaching4 = await fixture.State.ListByCommitSequenceAsync(new ActivityExecutionStateCommitSequenceQuery("wf", 4, 10));
        var firstPage = await fixture.State.ListByCommitSequenceAsync(new ActivityExecutionStateCommitSequenceQuery("wf", 0, 2));
        var secondPage = await fixture.State.ListByCommitSequenceAsync(new ActivityExecutionStateCommitSequenceQuery("wf", 0, 2,
            new ActivityExecutionStateCommitSequenceKey(firstPage[^1].FirstCommitSequence!.Value, firstPage[^1].Execution.ActivityExecutionId)));
        var unsequenced = await fixture.State.ListWithoutCommitSequenceAsync("wf");

        Assert.Equal(["b", "c"], reaching4.Select(row => row.Execution.ActivityExecutionId));
        Assert.Equal(["a", "b"], firstPage.Select(row => row.Execution.ActivityExecutionId));
        Assert.Equal(["c"], secondPage.Select(row => row.Execution.ActivityExecutionId));
        Assert.Equal(["legacy"], unsequenced.Select(row => row.Execution.ActivityExecutionId));
        Assert.Equal(3, await fixture.State.SumLostUpdateWarningCountAsync("wf"));

        var columns = await fixture.Context.ActivityExecutionStates.AsNoTracking()
            .Where(row => row.LostUpdateWarningCount != null)
            .Select(row => new { row.FirstCommitSequence, row.LastCommitSequence, row.LostUpdateWarningCount })
            .ToListAsync();
        Assert.Contains(columns, row => row is { FirstCommitSequence: 2, LastCommitSequence: 9, LostUpdateWarningCount: 2 });
    }

    [Fact]
    public void The_runtime_model_snapshot_has_no_pending_model_changes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite(connection, sql => sql.MigrationsHistoryTable(RuntimeEfModule.HistoryTableName)).Options);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static ActivityExecutionState Ranged(string workflow, string id, long first, long last, int lostUpdates) =>
        State(workflow, id, first) with
        {
            Transitions = [new ActivityLifecycleStamp(ActivityLifecycleTransitionKind.Scheduled, first), new ActivityLifecycleStamp(ActivityLifecycleTransitionKind.Completed, last)],
            FirstCommitSequence = first,
            LastCommitSequence = last,
            LostUpdateWarningCount = lostUpdates == 0 ? null : lostUpdates
        };
}
