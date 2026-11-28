using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Orders synchronous requests to one private Test Run through response retention.</summary>
internal sealed class TestRunHeldDispatches
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _active = new(StringComparer.Ordinal);

    internal async ValueTask<TestRunTargetDispatchResult> RunAsync(string testRunId,
        Func<ValueTask<TestRunTargetDispatchResult>> dispatch, CancellationToken cancellationToken)
    {
        var ours = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = _active.GetOrAdd(testRunId, ours);
            if (ReferenceEquals(active, ours)) break;
            await active.Task.WaitAsync(cancellationToken);
        }

        try { return await dispatch(); }
        finally
        {
            _active.TryRemove(testRunId, out _);
            ours.SetResult();
        }
    }
}
