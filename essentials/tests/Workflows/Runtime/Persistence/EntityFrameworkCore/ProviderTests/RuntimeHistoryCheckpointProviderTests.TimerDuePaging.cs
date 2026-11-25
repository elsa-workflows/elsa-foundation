using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

internal static partial class RuntimeHistoryCheckpointProviderContract
{
    private static async Task TimerDuePages(Database database)
    {
        await using var context = database.Open();
        var store = new EfDurableTimerStore(context, new Access(), new Codec(), database.Clock);
        foreach (var run in new[] { "due-run-1", "due-run-2", "due-run-3" })
            await store.SaveAsync(new("same", run, "Timer", "same", At, At));
        await store.SaveAsync(new("future", "due-future", "Timer", "future", At.AddHours(1), At));
        var first = await store.ListDuePageAsync(At, new(1));
        Assert.Equal("due-run-1", Assert.Single(first.Items).WorkflowExecutionId); Assert.NotNull(first.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListDuePageAsync(At.AddTicks(1), new(1, first.NextContinuationToken)).AsTask());
        var foreign = new EfDurableTimerStore(context, new DuePageAccess(ForeignScope), new Codec(), database.Clock);
        await Assert.ThrowsAsync<ArgumentException>(() => foreign.ListDuePageAsync(At, new(1, first.NextContinuationToken)).AsTask());
        await store.DeleteAsync("due-run-1", "same");
        var second = await store.ListDuePageAsync(At, new(1, first.NextContinuationToken));
        Assert.Equal("due-run-2", Assert.Single(second.Items).WorkflowExecutionId); Assert.NotNull(second.NextContinuationToken);
        var last = await store.ListDuePageAsync(At, new(1, second.NextContinuationToken));
        Assert.Equal("due-run-3", Assert.Single(last.Items).WorkflowExecutionId); Assert.Null(last.NextContinuationToken);
    }

    private sealed class DuePageAccess(string scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(scope)); }
}
