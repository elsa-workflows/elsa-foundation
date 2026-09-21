using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Tasks.Schedules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Tenant-scope-aware recurring activation for durable alteration plans. The coordinator does no in-memory queueing:
/// every tick re-discovers bounded active-plan pages, so capture and leases safely resume after a process restart.
/// Failure backoff comes from <see cref="BackoffSweepPumpTask"/>; only a cancellation of the pump's own token
/// escapes, so a dependency's unrelated timeout feeds the backoff instead of crashing the recurring-task host.
/// </summary>
public sealed class WorkflowAlterationOrchestrationPumpTask : BackoffSweepPumpTask
{
    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly WorkflowAlterationOrchestrationSweep? _directSweep;
    private readonly IOptions<WorkflowAlterationOrchestrationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, string> _scopeCursors = new(StringComparer.Ordinal);
    private string? _directCursor;

    [ActivatorUtilitiesConstructor]
    public WorkflowAlterationOrchestrationPumpTask(
        IPersistenceScopeRunner scopeRunner,
        IOptions<WorkflowAlterationOrchestrationOptions> options,
        TimeProvider timeProvider,
        ILogger<WorkflowAlterationOrchestrationPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        _scopeRunner = scopeRunner ?? throw new ArgumentNullException(nameof(scopeRunner));
    }

    private WorkflowAlterationOrchestrationPumpTask(
        WorkflowAlterationOrchestrationSweep sweep,
        IOptions<WorkflowAlterationOrchestrationOptions> options,
        TimeProvider timeProvider,
        ILogger<WorkflowAlterationOrchestrationPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        _directSweep = sweep ?? throw new ArgumentNullException(nameof(sweep));
    }

    /// <summary>Creates a directly scoped pump for lifecycle tests and explicit host-driven execution.</summary>
    public static WorkflowAlterationOrchestrationPumpTask CreateForSweep(
        WorkflowAlterationOrchestrationSweep sweep,
        IOptions<WorkflowAlterationOrchestrationOptions> options,
        TimeProvider timeProvider,
        ILogger<WorkflowAlterationOrchestrationPumpTask> logger) =>
        new(sweep, options, timeProvider, logger);

    private WorkflowAlterationOrchestrationPumpTask(
        IOptions<WorkflowAlterationOrchestrationOptions> options,
        TimeProvider timeProvider,
        ILogger<WorkflowAlterationOrchestrationPumpTask> logger)
        : base(logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override TimeSpan SweepInterval => ValidatedOptions.SweepInterval;

    protected override TimeSpan MaxBackoffInterval => ValidatedOptions.MaxBackoffInterval;

    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        var options = ValidatedOptions;
        var workerId = options.WorkerId ?? $"runtime-alteration:{Environment.MachineName}";

        if (_scopeRunner is null)
        {
            var result = await _directSweep!.ExecuteAsync(options, workerId, _directCursor, cancellationToken);
            _directCursor = result.NextCursor;
        }
        else
        {
            await _scopeRunner.RunAsync(async (scope, operationScope, operationCancellationToken) =>
            {
                _scopeCursors.TryGetValue(scope.Value, out var cursor);
                var result = await operationScope.ServiceProvider
                    .GetRequiredService<WorkflowAlterationOrchestrationSweep>()
                    .ExecuteAsync(options, workerId, cursor, operationCancellationToken);
                if (result.NextCursor is null)
                    _scopeCursors.TryRemove(scope.Value, out _);
                else
                    _scopeCursors[scope.Value] = result.NextCursor;
            }, cancellationToken);
        }
    }

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        Logger.LogError(exception,
            "Workflow alteration orchestration sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
            consecutiveFailures,
            backoffInterval);

    protected override bool IsHandledSweepException(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    protected override bool ShouldRethrowCancellation(OperationCanceledException exception, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested;

    private WorkflowAlterationOrchestrationOptions ValidatedOptions
    {
        get
        {
            var options = _options.Value;
            options.Validate();
            return options;
        }
    }
}
