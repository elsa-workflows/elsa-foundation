using Elsa.Jobs.Core;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>One shared host/Tasks lifecycle, bounded pages and fair scope rotation; no physical handler slots.</summary>
internal sealed class JobsResultDeliveryWorker(IServiceScopeFactory scopes, JobWorkflowRuntimeSettings settings,
    TimeProvider clock, ILogger<JobsResultDeliveryWorker> logger, IHostApplicationLifetime? lifetime = null)
    : IHostedService, IBackgroundTask, IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _running;
    private Task? _draining;
    public Task StartAsync(CancellationToken cancellationToken) => Start(cancellationToken, waitForHost: true);
    Task IBackgroundTask.StartAsync(CancellationToken cancellationToken) => Start(cancellationToken, waitForHost: false);
    private Task Start(CancellationToken cancellationToken, bool waitForHost)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_draining is not null) throw new InvalidOperationException("A stopped Jobs result delivery loop cannot restart.");
            _running ??= Task.Run(() => RunAsync(waitForHost));
        }
        return Task.CompletedTask;
    }
    Task ITask.ExecuteAsync(CancellationToken cancellationToken)
    {
        lock (_gate) return _running ?? throw new InvalidOperationException("Jobs result delivery has not started.");
    }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate) { _draining ??= DrainAsync(); return _draining.WaitAsync(cancellationToken); }
    }
    private async Task DrainAsync() { await _stopping.CancelAsync(); if (_running is not null) await _running; }
    private async Task RunAsync(bool waitForHost)
    {
        try
        {
            if (waitForHost && lifetime is not null)
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using (lifetime.ApplicationStarted.Register(() => ready.TrySetResult())) await ready.Task.WaitAsync(_stopping.Token);
            }
            var cursors = new string?[settings.Scopes.Count]; var next = 0;
            while (!_stopping.IsCancellationRequested)
            {
                var index = next; next = (next + 1) % settings.Scopes.Count;
                try
                {
                    JobPage<JobDeliverySnapshot> page;
                    await using (var scope = scopes.CreateAsyncScope())
                    {
                        using var timeout = new CancellationTokenSource(settings.OperationTimeout, clock);
                        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, timeout.Token);
                        var queries = scope.ServiceProvider.GetRequiredService<IJobQueries>();
                        page = await queries.ListPendingDeliveriesAsync(settings.Scopes[index], new(settings.PageSize, cursors[index]), operation.Token);
                    }
                    foreach (var delivery in page.Items)
                    {
                        _stopping.Token.ThrowIfCancellationRequested();
                        // Each responsibility gets a fresh scope and cooperative budget. A timed-out
                        // item remains owed, while the rest of this same page can still progress.
                        try
                        {
                            await using var scope = scopes.CreateAsyncScope();
                            using var timeout = new CancellationTokenSource(settings.OperationTimeout, clock);
                            using var operation = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, timeout.Token);
                            _ = await scope.ServiceProvider.GetRequiredService<JobsResultDeliveryExecution>().ExecuteAsync(settings.Scopes[index], delivery, operation.Token);
                        }
                        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { throw; }
                        catch (Exception) { logger.LogWarning("Jobs native result consumption is not confirmed; the selected result and delivery remain retained."); }
                    }
                    if (page.ContinuationToken is not null && page.ContinuationToken == cursors[index]) throw JobsNativeBoundary.Invalid();
                    cursors[index] = page.ContinuationToken;
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { return; }
                catch (Exception) { logger.LogWarning("Jobs native delivery authority is unavailable; pending results remain owed."); }
                await Task.Delay(settings.PollInterval, clock, _stopping.Token);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }
    public void Dispose() => _stopping.Cancel();
}
