using System.Collections.Concurrent;
using Elsa.Persistence.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Schedules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Tenant-scope-aware recurring activation for durable alteration plans. The coordinator does no in-memory queueing:
/// every tick re-discovers bounded active-plan pages, so capture and leases safely resume after a process restart.
/// </summary>
public sealed class WorkflowAlterationOrchestrationPumpTask : IRecurringTask
{
    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly WorkflowAlterationOrchestrationSweep? _directSweep;
    private readonly IOptions<WorkflowAlterationOrchestrationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkflowAlterationOrchestrationPumpTask> _logger;
    private readonly ConcurrentDictionary<string, string> _scopeCursors = new(StringComparer.Ordinal);
    private string? _directCursor;
    private int _consecutiveFailures;

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
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public TimeSpan CurrentSweepInterval => ComputeInterval(_options.Value, Volatile.Read(ref _consecutiveFailures));

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        options.Validate();
        var workerId = options.WorkerId ?? $"runtime-alteration:{Environment.MachineName}";
        try
        {
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

            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var failures = Interlocked.Increment(ref _consecutiveFailures);
            _logger.LogError(exception,
                "Workflow alteration orchestration sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
                failures,
                CurrentSweepInterval);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ITaskSchedule GetSchedule() => new AdaptiveIntervalSchedule(() => CurrentSweepInterval, _logger);

    private static TimeSpan ComputeInterval(WorkflowAlterationOrchestrationOptions options, int failures)
    {
        options.Validate();
        if (failures <= 0)
            return options.SweepInterval;

        var baseTicks = options.SweepInterval.Ticks;
        var maxTicks = options.MaxBackoffInterval.Ticks;
        var multiplier = 1L << Math.Min(failures - 1, 30);
        return multiplier > maxTicks / baseTicks
            ? options.MaxBackoffInterval
            : TimeSpan.FromTicks(Math.Min(maxTicks, baseTicks * multiplier));
    }
}
