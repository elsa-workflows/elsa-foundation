using Elsa.Jobs.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Jobs;

/// <summary>One bounded host loop; durable materialization and recovery remain entirely provider-owned.</summary>
internal sealed class JobSchedulePump(IServiceScopeFactory scopes, JobRuntimeSettings settings,
    TimeProvider clock, ILogger<JobSchedulePump> logger)
{
    internal async Task RunAsync(CancellationToken stoppingToken)
    {
        var nextScope = 0;
        var failedScopes = new HashSet<JobScope>();
        while (!stoppingToken.IsCancellationRequested)
        {
            var jobScope = settings.WorkerScopes[nextScope];
            nextScope = (nextScope + 1) % settings.WorkerScopes.Count;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var timeout = new CancellationTokenSource(settings.MaterializationTimeout, clock);
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
                var result = await JobAuthority.Commands(scope.ServiceProvider).MaterializeDueAsync(
                    new(jobScope, settings.MaximumOccurrencesPerMaterialization), operation.Token);
                if (result.Status is JobCommandStatus.Applied or JobCommandStatus.NoEligibleWork or JobCommandStatus.Held)
                    failedScopes.Remove(jobScope);
                else if (failedScopes.Add(jobScope))
                    logger.LogWarning("Jobs schedule materialization was not confirmed; due work remains with its authority.");
                // A lost reply can hide committed cursor/occurrence progress. The next bounded drain
                // reconciles provider state; the host never reconstructs or resubmits occurrence jobs.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                if (failedScopes.Add(jobScope))
                    logger.LogWarning("Jobs schedule materialization is unavailable; retained schedules and owed occurrences remain with their authority.");
            }
            try { await Task.Delay(settings.MaterializationInterval, clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
