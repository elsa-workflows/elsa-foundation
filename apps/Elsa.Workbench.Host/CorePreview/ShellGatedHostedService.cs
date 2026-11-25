namespace Elsa.Workbench.CorePreview;

/// <summary>
/// Starts a hosted service once a gate opens (the Core Preview node, once the default shell is active), without holding up
/// the application's own start: the host's other services, the shell's warm-up among them, start meanwhile. A failure to
/// pass the gate or to start ends the process, as the preview node's own escalations do.
/// </summary>
internal sealed class ShellGatedHostedService(IHostedService inner, Func<CancellationToken, Task> gate, Action<string> endProcess) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _started;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _started = Task.Run(async () =>
        {
            try
            {
                await gate(_stopping.Token);
                await inner.StartAsync(_stopping.Token);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { throw; }
            catch (Exception error) { endProcess($"The Core Preview did not start: {error.GetType().Name}: {error.Message}"); throw; }
        });
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_started is null) return;
        try { await _started.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { return; }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return; }
        await inner.StopAsync(cancellationToken);
    }
}
