using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.DraftMutationCommandTests;

/// <summary>
/// SC-016 + Unit C FR-027 + FR-027a: two concurrent mutations on the SAME Draft execute
/// serially (the second waits for the first); mutations on DIFFERENT Drafts proceed in
/// parallel without contention.
/// </summary>
public sealed class LockSemanticsTests
{
    [Fact]
    public async Task Two_acquires_on_the_same_lock_name_serialise()
    {
        var provider = new InMemoryDistributedLockProvider();
        var sequence = new List<string>();
        var firstReleased = new TaskCompletionSource();

        var task1 = Task.Run(async () =>
        {
            await using var handle = await provider.AcquireLockAsync("workflow-draft:A");
            sequence.Add("first-acquired");
            // Hold the lock briefly while task2 attempts to acquire.
            await Task.Delay(50);
            sequence.Add("first-releasing");
            firstReleased.SetResult();
        });

        // Make sure task1 acquires first by giving it a head start.
        await Task.Delay(10);

        var task2 = Task.Run(async () =>
        {
            await using var handle = await provider.AcquireLockAsync("workflow-draft:A");
            sequence.Add("second-acquired");
        });

        await Task.WhenAll(task1, task2);

        Assert.Equal(["first-acquired", "first-releasing", "second-acquired"], sequence);
    }

    [Fact]
    public async Task Two_acquires_on_different_lock_names_proceed_in_parallel()
    {
        var provider = new InMemoryDistributedLockProvider();
        var bothInsideHolding = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource();
        var insideCount = 0;

        async Task Race(string name)
        {
            await using var handle = await provider.AcquireLockAsync(name);
            if (Interlocked.Increment(ref insideCount) == 2)
                bothInsideHolding.SetResult();
            await releaseGate.Task;
        }

        var task1 = Race("workflow-draft:A");
        var task2 = Race("workflow-draft:B");

        // If the locks did NOT parallelise, this would time out — the second Race would
        // be waiting outside while the first holds. The success of bothInsideHolding within
        // a sane window proves both holds are concurrent.
        var completedFirst = await Task.WhenAny(bothInsideHolding.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(bothInsideHolding.Task, completedFirst);

        releaseGate.SetResult();
        await Task.WhenAll(task1, task2);
    }
}
