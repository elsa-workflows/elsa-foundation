using System.Collections.Concurrent;
using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Services;

internal sealed class AgentRetentionPumpTask(IPersistenceScopeRunner scopes, ILogger<AgentRetentionPumpTask> logger) : BackoffSweepWorkerTask(logger)
{
    private readonly ConcurrentDictionary<(string Partition, bool Legacy), string> cursors = new();
    protected override TimeSpan SweepInterval => TimeSpan.FromMinutes(5);
    protected override TimeSpan MaxBackoffInterval => TimeSpan.FromHours(1);
    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        await scopes.RunAsync(async (scope, operation, token) =>
        {
            var pruner = operation.ServiceProvider.GetRequiredService<AgentRetentionPruner>();
            // Explicitly select each supported tenant key in this ordinary partition. A missing
            // legacy tenant remains distinct; neither query admits global/across-scope access.
            foreach (var legacy in new[] { false, true })
            {
                var key = (scope.Value, legacy);
                cursors.TryGetValue(key, out var cursor);
                var result = await pruner.PrunePageAsync(new(legacy ? null : scope.Value, scope.Value, cursor), token);
                if (result.NextHandle is { } next) cursors[key] = next;
                else cursors.TryRemove(key, out _);
                if (result.Purged > 0) Logger.LogInformation("Agent retention purged content from {Count} settled operations", result.Purged);
                foreach (var pin in result.Pinned) Logger.LogDebug("Agent retention kept {Count} operations for {Reason}", pin.Value, pin.Key);
            }
        }, cancellationToken);
    }
    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        Logger.LogError(exception, "Agent retention sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}", consecutiveFailures, backoffInterval);
    protected override bool IsHandledSweepException(Exception exception) => exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
}
