using System.Reflection;
using Elsa.Tasks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Tasks.Tests;

public sealed class TaskManagerConcurrencyTests
{
    // Regression test for a non-atomic check-then-create race in GetOrInitializeTaskStateManager:
    // concurrent first callers could each construct their own TaskStateManager (and CancellationTokenSource),
    // with all but the last-assigned instance silently orphaned. Exercises the private method directly via
    // reflection, since it is the sole entry point that lazily initializes the field.
    [Fact]
    public async Task GetOrInitializeTaskStateManager_ConcurrentFirstCallers_AllObserveTheSameInstance()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var manager = new TaskManager(NullLoggerFactory.Instance, provider);

        var method = typeof(TaskManager).GetMethod("GetOrInitializeTaskStateManager", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        const int concurrentCallers = 16;
        using var barrier = new Barrier(concurrentCallers);

        var tasks = Enumerable.Range(0, concurrentCallers)
            .Select(_ => Task.Run(() =>
            {
                barrier.SignalAndWait();
                return (TaskStateManager)method!.Invoke(manager, [CancellationToken.None])!;
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Same(results[0], result));
    }
}
