using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Runtime.Hosting;
using Microsoft.Extensions.Hosting;

namespace Kernel.Foundation.Postgres;

/// <summary>Runs one PostgreSQL Core Preview node for the lifetime of its application host.</summary>
public sealed class PostgresPreviewHostedService(Func<KernelDb> create, string node, IActorType actor,
    Action<string> endProcess, LoopOptions? options = null) : IHostedService, IAsyncDisposable
{
    private readonly Action<string> _endProcess = endProcess ?? throw new ArgumentNullException(nameof(endProcess));
    private readonly object _lifecycle = new();
    private readonly CancellationTokenSource _cancelStartup = new();
    private Task<RuntimeNode>? _startup;
    private Task? _shutdown;
    private int _escalated;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_shutdown is not null)
                throw new InvalidOperationException("The PostgreSQL preview service has stopped.");
            if (_startup is null || _startup.IsCanceled || _startup.IsFaulted)
                _startup = Task.Run(() => StartNodeAsync(cancellationToken));
            return _startup;
        }
    }

    private async Task<RuntimeNode> StartNodeAsync(CancellationToken cancellationToken)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancelStartup.Token);
        return await PostgresPreviewHosting.StartAsync(create, node, actor, Escalate, options, startup.Token);
    }

    /// <summary>The last error recorded by the node's runtime loops, if any.</summary>
    public Exception? LastError => Volatile.Read(ref _startup) is { IsCompletedSuccessfully: true } started
        ? started.Result.LastError
        : null;

    /// <param name="cancellationToken">The application's shutdown deadline (<c>HostOptions.ShutdownTimeout</c>). It also bounds
    /// the node's drain: at the deadline the stop becomes a kill, nothing renews, and what is still owned runs out by its lease.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var shutdown = RequestStop(cancellationToken);
        try { await shutdown.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The deadline also killed the node's stop, which may then finish at once: escalate on the expiry itself.
            Escalate("The PostgreSQL preview node did not stop within the application shutdown deadline.");
            throw;
        }
    }

    public ValueTask DisposeAsync() => new(RequestStop(CancellationToken.None));

    private Task RequestStop(CancellationToken deadline)
    {
        lock (_lifecycle)
        {
            if (_shutdown is not null) return _shutdown;
            if (_startup is { IsCanceled: false, IsFaulted: false } startup)
                return _shutdown = Task.Run(() => StopNodeAsync(startup, deadline));

            _cancelStartup.Dispose();
            return _shutdown = Task.CompletedTask;
        }
    }

    private async Task StopNodeAsync(Task<RuntimeNode> startup, CancellationToken deadline)
    {
        try
        {
            try { await _cancelStartup.CancelAsync(); }
            finally
            {
                // Startup reports its own error. An owner that did start must be stopped even if cancellation failed.
                await ((Task)startup).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                if (startup.IsCompletedSuccessfully) await startup.Result.StopAsync(deadline);
            }
        }
        finally { _cancelStartup.Dispose(); }
    }

    private void Escalate(string reason)
    {
        if (Interlocked.Exchange(ref _escalated, 1) == 0) _endProcess(reason);
    }
}
