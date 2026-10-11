using Elsa.Workflows.ExecutionEvidence.Models;

namespace Elsa.Workflows.ExecutionEvidence.Endpoints;

/// <summary>
/// Shared long-poll for the read endpoints. Waiting server-side is what lets a test await an assertion instead of
/// sleeping for an arbitrary interval, and both read endpoints need identical semantics for a client to treat them
/// interchangeably.
/// </summary>
internal static class EvidencePolling
{
    internal const int MaxWaitMilliseconds = 60_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    internal static int ClampWait(int waitMs) => Math.Clamp(waitMs, 0, MaxWaitMilliseconds);

    /// <summary>
    /// Re-reads until there is something to report, the run is terminal, the budget expires, or the caller disconnects.
    /// Each read is a fresh consistent snapshot taken from the original cursor, so the returned page is never torn.
    /// </summary>
    internal static async Task<ExecutionEvidencePage> ReadAsync(
        Func<ExecutionEvidencePage> read,
        int waitMs,
        CancellationToken cancellationToken)
    {
        var page = read();

        if (waitMs <= 0)
            return page;

        var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);

        while (page.Records.Count == 0 && !page.Terminal && !cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTime.UtcNow;

            if (remaining <= TimeSpan.Zero)
                break;

            // Never overshoot the caller's budget: a 10ms wait must not cost a full 50ms poll interval.
            await Task.Delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken);
            page = read();
        }

        return page;
    }
}
