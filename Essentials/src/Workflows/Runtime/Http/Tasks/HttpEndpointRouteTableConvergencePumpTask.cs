using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Http.Tasks;

/// <summary>
/// Recurring pump that keeps this node's HTTP route table converged with the durable index when another node publishes
/// an endpoint or suspends on, or resumes from, an HTTP bookmark (#2190). The observers that refresh the table fire only
/// on the node that made the change, so every node runs this pump: each tick calls
/// <see cref="IHttpEndpointRouteTableSynchronizer.ConvergeAsync"/>, which reads the stimulus identities alone and rebuilds
/// the table only when they no longer match what it was built from. Failure backoff comes from
/// <see cref="BackoffSweepPumpTask"/>.
/// </summary>
/// <remarks>
/// Only this pump's own cancellation (the shell stopping) escapes as cancellation. Any other
/// <see cref="OperationCanceledException"/>, such as a provider command timeout, is a failed check: it is logged and
/// widens the interval, rather than passing for a shutdown and leaving the table stale with nothing logged.
/// </remarks>
public sealed class HttpEndpointRouteTableConvergencePumpTask(
    IHttpEndpointRouteTableSynchronizer synchronizer,
    IOptions<HttpEndpointRouteTableConvergenceOptions> options,
    ILogger<HttpEndpointRouteTableConvergencePumpTask> logger) : BackoffSweepPumpTask(logger)
{
    protected override TimeSpan SweepInterval => options.Value.Interval;

    protected override TimeSpan MaxBackoffInterval => options.Value.MaxBackoffInterval;

    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        if (await synchronizer.ConvergeAsync(cancellationToken))
            Logger.LogDebug("HTTP route table refreshed: the durable endpoint index changed since the last refresh");
    }

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        Logger.LogError(
            exception,
            "HTTP route-table convergence check failed ({ConsecutiveFailures} consecutive); endpoints changed on other nodes are not served here until a check succeeds; backing off to {Interval}",
            consecutiveFailures,
            backoffInterval);

    protected override bool IsHandledSweepException(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    protected override bool ShouldRethrowCancellation(OperationCanceledException exception, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested;
}
