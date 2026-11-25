using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Runtime.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kernel.Foundation.Postgres;

/// <summary>Runs one PostgreSQL Core Preview node for the lifetime of its application host.</summary>
/// <param name="logger">Each fault the node or this service records about its stop is logged once, at Error: a lost
/// fence or a drain fault, a dispose that returned at its bound, a fault after a kill, and a fault of the shutdown itself.</param>
/// <param name="replies">How the node answers held callers, on a store with held replies (<see cref="PostgresPreviewHosting.StartAsync"/>).</param>
public sealed class PostgresPreviewHostedService(Func<KernelDb> create, string node, IActorType actor,
    Action<string> endProcess, LoopOptions? options = null, ILogger<PostgresPreviewHostedService>? logger = null,
    Kernel.Durable.IReplyTransport? replies = null) : IHostedService, IAsyncDisposable
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Exception, object> _logged = [];     // weak: no exception is kept alive
    private Exception? _shutdownFault;

    // One Error entry per fault, whether the node recorded it or the shutdown around it raised it.
    private void Fault(Exception fault)
    {
        if (!_logged.TryAdd(fault, fault)) return;
        if (fault is AggregateException aggregate)
        {
            // The node has logged an aggregate's inner exceptions already: log only those that are not, never the aggregate.
            foreach (var inner in aggregate.InnerExceptions) Fault(inner);
            return;
        }
        // The phase the node was in: a loop error on a running node is no stop fault.
        var phase = Volatile.Read(ref _shutdown) is null ? "running" : "stopping";
        var repeats = fault.Data["SuppressedRepeats"] is long suppressed ? suppressed : 0;
        logger?.LogError(fault, "The PostgreSQL preview node {Node} recorded an error while {Phase} ({Repeats} earlier repeats not logged).",
            node, phase, repeats);
    }

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
        return await PostgresPreviewHosting.StartAsync(create, node, actor, Escalate, options, startup.Token, Fault, replies);
    }

    /// <summary>
    /// The last error recorded, if any: a fault of the shutdown first, then the node's own (its loops' errors and its stop's
    /// fault), whoever waited for it. Every stop fault is also logged once at Error. Once the shutdown has faulted, a fault
    /// the node records later (after a kill) is logged but not shown here.
    /// </summary>
    public Exception? LastError => Volatile.Read(ref _shutdownFault) ?? (Volatile.Read(ref _startup) is { IsCompletedSuccessfully: true } started
        ? started.Result.LastError
        : null);

    /// <param name="cancellationToken">The application's shutdown deadline (<c>HostOptions.ShutdownTimeout</c>). It also bounds
    /// the node's drain: at the deadline the stop becomes a kill, nothing renews, and what is still owned runs out by its lease.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // It throws its own outcome: the stop's fault (also recorded on the node, LastError) or the deadline's cancellation.
        var shutdown = RequestStop(cancellationToken, out var started);
        try { await shutdown.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (started) DeadlineKilled("application shutdown");     // a stop that joined another's stop kills nothing
            await EscalateUnlessStoppedAsync(shutdown, "The PostgreSQL preview node did not stop within the application shutdown deadline.");
            throw;
        }
    }

    private int _killWarned;

    // A deadline of this call that fired before the stop finished killed the drain (the kill may have ended it since),
    // while the node held ownership: its startup had completed. That is no fault (LastError stays as it is), but the
    // operator is told once that ownership is left to run out by its lease.
    private void DeadlineKilled(string deadline)
    {
        if (Volatile.Read(ref _startup) is not { IsCompletedSuccessfully: true }) return;
        if (Interlocked.Exchange(ref _killWarned, 1) == 1) return;
        logger?.LogWarning("The {Deadline} deadline killed the drain of PostgreSQL preview node {Node}: its ownership is left to run out by lease expiry.",
            deadline, node);
    }

    /// <summary>Runs in the shutdown after the node's stop; for witnesses of a shutdown fault raised outside the node.</summary>
    internal Action? AfterNodeStop { get; init; }

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
        var shutdown = RequestStop(deadline.Token, out _);
        try { await shutdown.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            DeadlineKilled("dispose");     // the dispose deadline kills the stop whoever started it
            await EscalateUnlessStoppedAsync(shutdown, "The PostgreSQL preview node did not stop within its dispose deadline.");
        }
        catch (Exception) { }     // recorded and logged when the shutdown faulted (see RequestStop)
    }

    private Task RequestStop(CancellationToken deadline, out bool started)
    {
        started = false;
        lock (_lifecycle)
        {
            if (_shutdown is not null) return _shutdown;
            started = true;
            if (_startup is { IsCanceled: false, IsFaulted: false } startup)
            {
                _shutdown = Task.Run(() => StopNodeAsync(startup, deadline));
                // The shutdown's fault is recorded and logged whoever waits for it: the node's own is logged once by the
                // node already, and one raised around it (cancelling the startup, say) is recorded here.
                _ = _shutdown.ContinueWith(static (stopped, state) =>
                {
                    var service = (PostgresPreviewHostedService)state!;
                    var fault = stopped.Exception!.InnerException ?? stopped.Exception;
                    Volatile.Write(ref service._shutdownFault, fault);
                    service.Fault(fault);
                }, this, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return _shutdown;
            }

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
                AfterNodeStop?.Invoke();
            }
        }
        finally { _cancelStartup.Dispose(); }
    }

    private void Escalate(string reason)
    {
        if (Interlocked.Exchange(ref _escalated, 1) == 0) _endProcess(reason);
    }
}
