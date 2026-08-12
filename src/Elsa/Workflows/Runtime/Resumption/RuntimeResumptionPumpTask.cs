using System.Collections.Concurrent;
using Elsa.Persistence.Core;
using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Resumption;

/// <summary>
/// Recurring background pump that drives <see cref="IRuntimeResumptionService"/>. Each tick runs one
/// bounded sweep. Whole-sweep failure backoff comes from <see cref="BackoffSweepPumpTask"/>; on top of
/// it this pump parks individual executions: an execution whose re-drive faults or is rejected is
/// excluded for a geometrically growing window, so a single poisoned execution cannot occupy a re-drive
/// slot on every tick and starve healthy executions out of the per-sweep cap
/// (<see cref="RuntimeResumptionOptions.MaxExecutionsPerSweep"/>).
/// </summary>
public sealed class RuntimeResumptionPumpTask : BackoffSweepPumpTask
{
    private static readonly IReadOnlySet<string> EmptyExcluded = new HashSet<string>(StringComparer.Ordinal);

    private static readonly PersistenceScope DirectConstructionScope = new(PersistenceScope.DefaultValue);

    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly IRuntimeResumptionService? _resumptionService;
    private readonly IOptions<RuntimeResumptionOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<ExecutionBackoffKey, ExecutionBackoff> _executionBackoff = new();

    [ActivatorUtilitiesConstructor]
    public RuntimeResumptionPumpTask(
        IPersistenceScopeRunner scopeRunner,
        IOptions<RuntimeResumptionOptions> options,
        TimeProvider timeProvider,
        ILogger<RuntimeResumptionPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(scopeRunner);
        _scopeRunner = scopeRunner;
    }

    /// <summary>Direct-construction seam retained for focused pump tests and custom hosts.</summary>
    public RuntimeResumptionPumpTask(
        IRuntimeResumptionService resumptionService,
        IOptions<RuntimeResumptionOptions> options,
        TimeProvider timeProvider,
        ILogger<RuntimeResumptionPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(resumptionService);

        _resumptionService = resumptionService;
    }

    private RuntimeResumptionPumpTask(
        IOptions<RuntimeResumptionOptions> options,
        TimeProvider timeProvider,
        ILogger<RuntimeResumptionPumpTask> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options;
        _timeProvider = timeProvider;
    }

    protected override TimeSpan SweepInterval => _options.Value.SweepInterval;

    protected override TimeSpan MaxBackoffInterval => _options.Value.MaxBackoffInterval;

    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow();

        if (_scopeRunner is null)
        {
            await SweepAsync(DirectConstructionScope, _resumptionService!, options, now, cancellationToken);
        }
        else
        {
            await _scopeRunner.RunAsync(async (persistenceScope, operationScope, operationCancellationToken) =>
            {
                if (operationScope.ServiceProvider.GetService<IWorkflowTestScopeCleaner>() is { } scopeCleaner)
                    await scopeCleaner.SweepAsync(now, operationCancellationToken);
                await SweepAsync(
                    persistenceScope,
                    operationScope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>(),
                    options,
                    now,
                    operationCancellationToken);
            }, cancellationToken);
        }
    }

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval)
    {
        if (exception is OutboxProcessingException outboxException)
        {
            Logger.LogError(
                new EventId(68107, "RuntimePostCommitResultRecordingFailed"),
                "Runtime resumption sweep could not record a post-commit delivery result. OutboxItemId={OutboxItemId} IntentId={IntentId} ConsecutiveFailures={ConsecutiveFailures} BackoffInterval={BackoffInterval}",
                outboxException.OutboxItemId,
                outboxException.IntentId,
                consecutiveFailures,
                backoffInterval);
            return;
        }

        Logger.LogError(
            exception,
            "Runtime resumption sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
            consecutiveFailures,
            backoffInterval);
    }

    private async ValueTask SweepAsync(
        PersistenceScope persistenceScope,
        IRuntimeResumptionService resumptionService,
        RuntimeResumptionOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var request = new RuntimeResumptionSweepRequest(
            outboxBatchSize: options.OutboxBatchSize,
            backlogBatchSize: options.BacklogBatchSize,
            recoveryScanBatchSize: options.RecoveryScanBatchSize,
            leaseTimeout: options.LeaseTimeout,
            heartbeatTimeout: options.HeartbeatTimeout,
            maxExecutionsPerSweep: options.MaxExecutionsPerSweep,
            excludedWorkflowExecutionIds: SnapshotExcluded(persistenceScope, options, now));
        var result = await resumptionService.SweepAsync(request, cancellationToken);

        ApplyPerExecutionBackoff(persistenceScope, result, options, now);
        if (result.DidWork && Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug(
                "Resumption sweep delivered {Delivered}/{Attempted} outbox items, re-drove {Dispatches} execution(s), and purged {PurgedItems} residual work item(s) from {PurgedExecutions} terminal execution(s)",
                result.OutboxDeliveredCount,
                result.OutboxAttemptedCount,
                result.Dispatches.Count,
                result.PurgedWorkItemCount,
                result.TerminalExecutionsPurged);
    }

    private IReadOnlySet<string> SnapshotExcluded(
        PersistenceScope persistenceScope,
        RuntimeResumptionOptions options,
        DateTimeOffset now)
    {
        HashSet<string>? excluded = null;
        var pruneBefore = now - options.MaxBackoffInterval;

        foreach (var pair in _executionBackoff)
        {
            if (pair.Key.PersistenceScope == persistenceScope && pair.Value.NextEligibleAt > now)
                (excluded ??= new HashSet<string>(StringComparer.Ordinal)).Add(pair.Key.WorkflowExecutionId);
            else if (pair.Value.NextEligibleAt <= pruneBefore)
                // Eligible for a full max-backoff window without reappearing: the execution is gone, drop it so the map stays bounded.
                _executionBackoff.TryRemove(pair.Key, out _);
        }

        return excluded ?? EmptyExcluded;
    }

    private void ApplyPerExecutionBackoff(
        PersistenceScope persistenceScope,
        RuntimeResumptionSweepResult result,
        RuntimeResumptionOptions options,
        DateTimeOffset now)
    {
        foreach (var dispatch in result.Dispatches)
        {
            var key = new ExecutionBackoffKey(persistenceScope, dispatch.WorkflowExecutionId);
            if (dispatch.Outcome is RuntimeResumptionDispatchOutcome.Faulted or RuntimeResumptionDispatchOutcome.Rejected)
            {
                var failures = _executionBackoff.TryGetValue(key, out var current) ? current.Failures + 1 : 1;
                var delay = ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, failures);
                _executionBackoff[key] = new ExecutionBackoff(now + delay, failures);
            }
            else
            {
                _executionBackoff.TryRemove(key, out _);
            }
        }
    }

    private readonly record struct ExecutionBackoffKey(PersistenceScope PersistenceScope, string WorkflowExecutionId);

    private readonly record struct ExecutionBackoff(DateTimeOffset NextEligibleAt, int Failures);
}
