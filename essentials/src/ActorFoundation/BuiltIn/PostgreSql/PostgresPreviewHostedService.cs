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
    private readonly TimeSpan _disposeDeadline = RuntimeNode.ValidDisposeDeadline((options ?? new LoopOptions()).DisposeDeadline);
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

    /// <summary>The last error recorded by the node, if any: its loops' errors and its stop's fault, whoever waited for it.</summary>
    public Exception? LastError => Volatile.Read(ref _startup) is { IsCompletedSuccessfully: true } started
        ? started.Result.LastError
        : null;

    /// <param name="cancellationToken">The application's shutdown deadline (<c>HostOptions.ShutdownTimeout</c>). It also bounds
    /// the node's drain: at the deadline the stop becomes a kill, nothing renews, and what is still owned runs out by its lease.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // It throws its own outcome: the stop's fault (also recorded on the node, LastError) or the deadline's cancellation.
        var shutdown = RequestStop(cancellationToken);
        try { await shutdown.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await EscalateUnlessStoppedAsync(shutdown, "The PostgreSQL preview node did not stop within the application shutdown deadline.");
            throw;
        }
    }

    /// <summary>The grace the deadline's kill gets to end the stop before the process policy runs.</summary>
    public static readonly TimeSpan KillGrace = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _killNode = new();     // a dispose deadline kills the node's stop, whoever started it

    private async Task EscalateUnlessStoppedAsync(Task shutdown, string reason)
    {
        await Task.WhenAny(shutdown, Task.Delay(KillGrace)).ConfigureAwait(false);
        if (!shutdown.IsCompleted) Escalate(reason);
    }

    /// <summary>
    /// A stop bounded by <see cref="LoopOptions.DisposeDeadline"/>, for a host disposed without <see cref="StopAsync"/>
    /// (a later service failed to start): at the deadline the stop becomes a kill, also when a stop was already under way,
    /// and the process policy runs if it still has not finished after <see cref="KillGrace"/>. It never throws the stop's
    /// fault, which the node records (<see cref="LastError"/>).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(_disposeDeadline);
        using var kill = deadline.Token.Register(static state => ((CancellationTokenSource)state!).Cancel(), _killNode);
        var shutdown = RequestStop(deadline.Token);
        try { await shutdown.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            await EscalateUnlessStoppedAsync(shutdown, "The PostgreSQL preview node did not stop within its dispose deadline.");
        }
        catch (Exception) { }     // recorded by the node
    }

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
                // The caller's deadline and a dispose deadline both end the drain as a kill.
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline, _killNode.Token);
                if (startup.IsCompletedSuccessfully) await startup.Result.StopAsync(stop.Token);
            }
        }
        finally { _cancelStartup.Dispose(); }
    }

    private void Escalate(string reason)
    {
        if (Interlocked.Exchange(ref _escalated, 1) == 0) _endProcess(reason);
    }
}
