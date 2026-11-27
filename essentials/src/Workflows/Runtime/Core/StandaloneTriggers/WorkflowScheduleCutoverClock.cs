using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>The read-only Actor Foundation timer clock bound to one managed Runtime scope.</summary>
public sealed class WorkflowScheduleCutoverClock(PersistenceScope scope, Func<CancellationToken, ValueTask<long>> readMilliseconds)
{
    /// <summary>Reads the clock after the activation transaction holds the Runtime slot's write authority.</summary>
    public async ValueTask<DateTimeOffset> ReadAsync(PersistenceScope selected, CancellationToken cancellationToken = default)
    {
        if (selected != scope)
            throw new InvalidOperationException("The schedule clock differs from the current Runtime publication scope.");
        var instant = DateTimeOffset.FromUnixTimeMilliseconds(await readMilliseconds(cancellationToken));
        return instant > DateTimeOffset.UnixEpoch ? instant :
            throw new InvalidOperationException("The schedule clock did not return a valid activation time.");
    }
}
