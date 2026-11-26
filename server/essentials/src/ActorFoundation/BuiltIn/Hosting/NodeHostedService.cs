using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kernel.Runtime.Hosting;

/// <summary>Runs a runtime node for the lifetime of its application host.</summary>
internal sealed class NodeHostedService(string displayName, string node, Action<string> endProcess, TimeSpan disposeDeadline,
    Func<Action<Exception>, Action<string>, CancellationToken, Task<RuntimeNode>> startNode, ILogger? logger)
    : IHostedService, IAsyncDisposable
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Exception, object> _logged = [];     // weak: no exception is kept alive
    private Exception? _shutdownFault;

    // One Error entry per fault, whether the node recorded it or the shutdown around it raised it.
    private void Fault(Exception fault) => Log(fault, again: false);

    // The node's own reports: it reports each stop fault once, and a loop error again once its window has passed, with
    // the repeats it suppressed, also when the loop rethrows the same instance. Each is logged as reported.
    internal void NodeFault(Exception fault) => Log(fault, again: true);

    private void Log(Exception fault, bool again)
    {
        if (!_logged.TryAdd(fault, fault) && !again) return;
        if (fault is AggregateException { InnerExceptions.Count: > 0 } aggregate)     // an empty one is logged as itself
        {
            // The node has logged an aggregate's inner exceptions already: log only those that are not, never the aggregate.
            foreach (var inner in aggregate.InnerExceptions) Log(inner, again);
            return;
        }
        // The phase the node was in: a loop error on a running node is no stop fault.
        var phase = Volatile.Read(ref _shutdown) is null ? "running" : "stopping";
        var repeats = fault.Data?["SuppressedRepeats"] is long suppressed ? suppressed : 0;
        // Once the node is killed, what the kill interrupted may end with an error the kill caused (its store torn down,
        // say): a Warning beside the kill's own, still with the error, which LastError keeps too. An error the node recorded
        // before the kill (its drain's fault, recorded when the drain throws it) was logged at Error then.
        if (Volatile.Read(ref _startup) is { IsCompletedSuccessfully: true } started && started.Result.Killed)
            logger?.LogWarning(fault, $"The {displayName} node {{Node}} recorded an error after the deadline killed its drain ({{Repeats}} earlier repeats not logged).",
                node, repeats);
        else
            logger?.LogError(fault, $"The {displayName} node {{Node}} recorded an error while {{Phase}} ({{Repeats}} earlier repeats not logged).",
                node, phase, repeats);
    }

    private readonly Action<string> _endProcess = endProcess ?? throw new ArgumentNullException(nameof(endProcess));
    private readonly TimeSpan _disposeDeadline = RuntimeNode.ValidDisposeDeadline(disposeDeadline);
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
                throw new InvalidOperationException($"The {displayName} service has stopped.");
            if (_startup is null || _startup.IsCanceled || _startup.IsFaulted)
                _startup = Task.Run(() => StartNodeAsync(cancellationToken));
            return _startup;
        }
    }

    private async Task<RuntimeNode> StartNodeAsync(CancellationToken cancellationToken)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancelStartup.Token);
        return await startNode(NodeFault, Escalate, startup.Token);
    }

    /// <summary>
    /// The last error recorded, if any: a fault of the shutdown first, then the node's own (its loops' errors and its stop's
    /// fault), whoever waited for it. Every stop fault is also logged once at Error. Once the shutdown has faulted, a fault
    /// the node records later (after a kill) is logged but not shown here.
    /// </summary>
    public Exception? LastError => Volatile.Read(ref _shutdownFault) ?? (Volatile.Read(ref _startup) is { IsCompletedSuccessfully: true } started
        ? started.Result.LastError
        : null);

    /// <summary>
    /// Stops the node. Only the caller that starts the stop owns it: its deadline kills the drain, and if the killed stop has
    /// not ended after <see cref="KillGrace"/> the process policy runs. A call that joins a stop already under way kills
    /// nothing: its deadline ends only its own wait. <see cref="DisposeAsync"/> kills at its own deadline whoever owns the
    /// stop. An owner with no deadline, and a <see cref="LoopOptions.DisposeDeadline"/> that is infinite, never kill or escalate.
    /// </summary>
    /// <param name="cancellationToken">The application's shutdown deadline (<c>HostOptions.ShutdownTimeout</c>). For the call
    /// that starts the stop it also bounds the node's drain: at the deadline the stop becomes a kill, nothing renews, and what
    /// is still owned runs out by its lease.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // It throws its own outcome: the stop's fault (also recorded on the node, LastError) or the deadline's cancellation.
        var shutdown = RequestStop(cancellationToken, out var started);
        try { await shutdown.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A stop that joined another's stop kills nothing: its deadline ends only its own wait, and the caller that
            // owns the stop decides whether it is killed and the process policy runs.
            if (started)
            {
                DeadlineKilled("application shutdown");
                await EscalateUnlessStoppedAsync(shutdown, $"The {displayName} node did not stop within the application shutdown deadline.");
            }
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
        logger?.LogWarning($"The {{Deadline}} deadline killed the drain of {displayName} node {{Node}}: its ownership is left to run out by lease expiry.",
            deadline, node);
    }

    /// <summary>Runs in the shutdown after the node's stop; for witnesses of a shutdown fault raised outside the node.</summary>
    internal Action? AfterNodeStop { get; set; }

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
    /// fault, which the node records (<see cref="LastError"/>). With an infinite dispose deadline it neither kills nor escalates.
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
            await EscalateUnlessStoppedAsync(shutdown, $"The {displayName} node did not stop within its dispose deadline.");
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
                    var service = (NodeHostedService)state!;
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
                if (startup.IsCompletedSuccessfully)
                {
                    // A kill whose stop then completed inside the kill's own callback is seen by no deadline's wait: it is
                    // warned of here (once, whichever path sees it first).
                    try { await startup.Result.StopAsync(stop.Token); }
                    finally { if (startup.Result.Killed) DeadlineKilled(_killNode.IsCancellationRequested ? "dispose" : "application shutdown"); }
                }
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
