using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa3.Activities.Design.Import.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa3.Activities.Design.Import.Services;

/// <summary>
/// Recurring sweep that deletes collection uploads whose lifetime has run out. When an upload is applied, refused or
/// read after its expiry, <see cref="ReusableActivityImportOperationService"/> deletes it; this sweep covers the upload
/// nobody comes back to, and the one whose delete there failed, so it is driven by time rather than by the next
/// import request. It visits tenant partitions only (see "Upload retention" in the import's
/// <c>EXTENSION_POINTS.md</c>). Each tick visits
/// every persistence scope the host supplies and deletes at most
/// <see cref="ReusableActivityImportOptions.ExpiredCollectionSweepBatchSize"/> expired uploads in each. Failure
/// backoff comes from <see cref="BackoffSweepPumpTask"/>.
/// </summary>
/// <remarks>
/// Every node runs it. The delete is idempotent, so two nodes sweeping the same rows need no coordination.
/// </remarks>
public sealed class ExpiredImportCollectionSweepTask(
    IPersistenceScopeRunner scopeRunner,
    IOptions<ReusableActivityImportOptions> options,
    TimeProvider timeProvider,
    ILogger<ExpiredImportCollectionSweepTask> logger) : BackoffSweepPumpTask(logger)
{
    private static readonly TimeSpan MinimumMaxBackoffInterval = TimeSpan.FromHours(1);
    private readonly ReusableActivityImportOptions _options = ReusableActivityImportOperationService.ValidateOptions(options.Value);

    protected override TimeSpan SweepInterval => _options.ExpiredCollectionSweepInterval;

    protected override TimeSpan MaxBackoffInterval =>
        _options.ExpiredCollectionSweepInterval > MinimumMaxBackoffInterval ? _options.ExpiredCollectionSweepInterval : MinimumMaxBackoffInterval;

    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await scopeRunner.RunAsync(async (_, operationScope, operationCancellationToken) =>
        {
            var deleted = await operationScope.ServiceProvider.GetRequiredService<IReusableActivityImportOperationStore>()
                .DeleteExpiredCollectionsAsync(now, _options.ExpiredCollectionSweepBatchSize, operationCancellationToken);
            if (deleted > 0)
                Logger.LogInformation("Deleted {Count} expired Elsa 3 import collection uploads", deleted);
        }, cancellationToken);
    }

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        Logger.LogError(
            exception,
            "Elsa 3 import collection sweep failed ({ConsecutiveFailures} consecutive); expired uploads stay in the import ledger until a sweep succeeds; backing off to {Interval}",
            consecutiveFailures,
            backoffInterval);

    protected override bool IsHandledSweepException(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    protected override bool ShouldRethrowCancellation(OperationCanceledException exception, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested;
}
