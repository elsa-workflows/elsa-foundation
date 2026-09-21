using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.ReferenceGarbageCollection;

/// <summary>
/// Recurring background pump that drives <see cref="IWorkflowExecutableReferenceGarbageCollector"/> (ADR 0040). Each
/// tick runs one sweep; failure backoff comes from <see cref="BackoffSweepPumpTask"/>.
/// </summary>
public sealed class WorkflowExecutableReferenceGarbageCollectionPumpTask : BackoffSweepPumpTask
{
    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly IWorkflowExecutableReferenceGarbageCollector? _garbageCollector;
    private readonly IOptions<WorkflowExecutableReferenceGarbageCollectionOptions> _options;

    [ActivatorUtilitiesConstructor]
    public WorkflowExecutableReferenceGarbageCollectionPumpTask(
        IPersistenceScopeRunner scopeRunner,
        IOptions<WorkflowExecutableReferenceGarbageCollectionOptions> options,
        ILogger<WorkflowExecutableReferenceGarbageCollectionPumpTask> logger)
        : this(options, logger)
    {
        ArgumentNullException.ThrowIfNull(scopeRunner);
        _scopeRunner = scopeRunner;
    }

    /// <summary>Direct-construction seam retained for focused pump tests and custom hosts.</summary>
    public WorkflowExecutableReferenceGarbageCollectionPumpTask(
        IWorkflowExecutableReferenceGarbageCollector garbageCollector,
        IOptions<WorkflowExecutableReferenceGarbageCollectionOptions> options,
        ILogger<WorkflowExecutableReferenceGarbageCollectionPumpTask> logger)
        : this(options, logger)
    {
        ArgumentNullException.ThrowIfNull(garbageCollector);

        _garbageCollector = garbageCollector;
    }

    private WorkflowExecutableReferenceGarbageCollectionPumpTask(
        IOptions<WorkflowExecutableReferenceGarbageCollectionOptions> options,
        ILogger<WorkflowExecutableReferenceGarbageCollectionPumpTask> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    protected override TimeSpan SweepInterval => _options.Value.SweepInterval;

    protected override TimeSpan MaxBackoffInterval => _options.Value.MaxBackoffInterval;

    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        WorkflowExecutableReferenceSweepResult result;
        if (_scopeRunner is null)
        {
            result = await _garbageCollector!.SweepAsync(cancellationToken);
        }
        else
        {
            var deletedReferences = 0;
            var deletedArtifacts = 0;
            await _scopeRunner.RunAsync(async (_, operationScope, operationCancellationToken) =>
            {
                var scopeResult = await operationScope.ServiceProvider
                    .GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>()
                    .SweepAsync(operationCancellationToken);
                deletedReferences += scopeResult.DeletedReferenceCount;
                deletedArtifacts += scopeResult.DeletedArtifactCount;
            }, cancellationToken);
            result = new WorkflowExecutableReferenceSweepResult(deletedReferences, deletedArtifacts);
        }

        if (result.DidWork && Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug(
                "Reference GC sweep removed {ReferenceCount} reference(s) and {ArtifactCount} artifact(s)",
                result.DeletedReferenceCount,
                result.DeletedArtifactCount);
    }

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        Logger.LogError(
            exception,
            "Reference GC sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
            consecutiveFailures,
            backoffInterval);

    protected override bool IsHandledSweepException(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
}
