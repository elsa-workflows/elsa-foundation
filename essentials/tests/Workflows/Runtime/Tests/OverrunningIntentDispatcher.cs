using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Records every dispatched intent. The first dispatch of <paramref name="overrunningIntentId"/>, or of any intent when
/// none is named, runs <see cref="WhileOverrunning"/> before it returns: a stand-in for a delivery that outlives its
/// claim, during which a peer gets to act (#2195).
/// </summary>
internal sealed class OverrunningIntentDispatcher(string? overrunningIntentId = null) : IRuntimePostCommitIntentDispatcher
{
    private int _overruns;

    public List<string> Dispatched { get; } = [];
    public Func<Task>? WhileOverrunning { get; set; }

    public async ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        Dispatched.Add(intent.IntentId);
        if ((overrunningIntentId is null || intent.IntentId == overrunningIntentId) &&
            Interlocked.Increment(ref _overruns) == 1 &&
            WhileOverrunning is { } overrun)
        {
            await overrun();
        }
    }
}
