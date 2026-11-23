using System.Collections.Concurrent;
using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.UserTasks;

/// <summary>Singleton scheduling shell; every bounded partition/page/task operation uses fresh scoped services.</summary>
internal sealed class UserTaskRecoveryTask(IPersistenceScopeRunner scopes, ILogger<UserTaskRecoveryTask> logger) : BackoffSweepWorkerTask(logger)
{
    private readonly ConcurrentDictionary<string, string> after = new(StringComparer.Ordinal);
    protected override TimeSpan SweepInterval => TimeSpan.FromSeconds(2);
    protected override TimeSpan MaxBackoffInterval => TimeSpan.FromSeconds(30);
    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        await scopes.RunAsync(async (scope, operation, ct) =>
        {
            var page = await operation.ServiceProvider.GetRequiredService<IUserTaskStore>().RecoverAsync(50, after.GetValueOrDefault(scope.Value), ct);
            if (page.Next is { } next) after[scope.Value] = next; else after.TryRemove(scope.Value, out _);
            foreach (var task in page.Items)
            {
                try { await operation.ServiceProvider.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(task, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception exception) { logger.LogWarning(exception, "User task recovery remains owed for {TaskId}", task.Id); }
            }
        }, cancellationToken);
    }
    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        logger.LogWarning(exception, "User task recovery failed; retry after {Backoff}", backoffInterval);
}
