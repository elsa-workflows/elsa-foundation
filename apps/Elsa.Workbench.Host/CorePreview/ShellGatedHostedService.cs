namespace Elsa.Workbench.CorePreview;

/// <summary>
/// Starts a hosted service once a gate opens (the Core Preview node, once the default shell is active), without holding up
/// the application's own start: the host's other services, the shell's warm-up among them, start meanwhile. A failure to
/// pass the gate or to start ends the process, as the preview node's own escalations do.
/// </summary>
/// <param name="starting">Runs once the gate opens, before the inner service starts. It reports its own failures, which end nothing.</param>
internal sealed class ShellGatedHostedService(IHostedService inner, Func<CancellationToken, Task> gate, Action<string> endProcess,
    Func<CancellationToken, Task>? starting = null) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _started;
    private int _entered;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _started = Task.Run(async () =>
        {
            try
            {
                await gate(_stopping.Token);
                // What runs before the node starts reports its own failures; the node starts either way.
                if (starting is not null)
                    try { await starting(_stopping.Token); }
                    catch (Exception) when (!_stopping.IsCancellationRequested) { /* reported by the callback */ }
                Volatile.Write(ref _entered, 1);
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
        // The start's own outcome is reported where it happens; here it only decides whether there is a node to stop.
        try { await _started.WaitAsync(cancellationToken); }
        catch (Exception) { /* cancelled, failed, or the host's stop deadline passed while it was still starting */ }
        // Once the node's start was entered, stop it, even part-way: its stop cancels a start still running.
        if (Volatile.Read(ref _entered) == 1) await inner.StopAsync(cancellationToken);
    }
}
