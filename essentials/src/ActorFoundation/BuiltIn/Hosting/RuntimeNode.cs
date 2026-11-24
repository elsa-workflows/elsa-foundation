using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Kernel.Runtime.Hosting;

/// <summary>When the loop of a <see cref="RuntimeNode"/> does what.</summary>
public sealed record LoopOptions
{
    /// <summary>A round of the fair share: the membership heartbeat, and the partitions it gives up or asks for.</summary>
    public TimeSpan BalanceEvery { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// K1b: the node lease's heartbeat, on a loop of its own. At least three per lease and at most half the window apart;
    /// null takes the longest such cadence up to a second. Unused while the store keeps a lease per partition.
    /// </summary>
    public TimeSpan? HeartbeatEvery { get; init; }

    /// <summary>The nodes heard from within this window count for the fair share. Two rounds or more.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Every held scope reads its inbox at least this often: the guarantee behind a lost wake hint.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long <see cref="RuntimeNode.DisposeAsync"/> lets the graceful stop drain before it becomes a kill.</summary>
    public TimeSpan DisposeDeadline { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// One node of the built-in runtime, on a clock: a <see cref="PartitionRuntime"/> and the loops that the kernel leaves to
/// its host. The pass loop runs a pass of every scope it holds when the last pass asked for one (at once, or after a
/// timer's delay), runs one at once when a hint comes, tells every held scope to read its inbox every
/// <see cref="LoopOptions.PollInterval"/>, and runs the passes no active scope gives (a Worker whose partitions are all
/// pending). Its passes run one after another, so a scope never has two at once and the workers of the node may share
/// their trial gate. The balancing loop runs a round every <see cref="LoopOptions.BalanceEvery"/>, apart from the
/// passes, so a long round of moves never holds them back beyond the lease. Both run on a scheduler of the node's own
/// that runs one task at a time: what a pass starts and awaits elsewhere (a background job's continuation) comes back on
/// it, between the loops' awaits, and never beside them. A worker is a single-threaded state machine.
/// <para>
/// K1b: under the node lease a third loop sends the node's heartbeat on its own cadence
/// (<see cref="LoopOptions.HeartbeatEvery"/>), off that scheduler, never behind a pass or a round: the host's pass gate
/// decides whether it renews, and any failure is a missed renewal. Its answer renews the Workers' guards at once; the
/// stand-down it asks for runs at their next pass, which the heartbeat wakes the pass loop for.
/// </para>
/// </summary>
public sealed class RuntimeNode : IActorRuntime, IAsyncDisposable
{
    private readonly PartitionRuntime _runtime;
    private readonly LoopOptions _options;
    private readonly long[] _due;        // when each held scope's next pass is due, on the node's clock; long.MaxValue: when woken
    private readonly long?[] _oldestWaiting; // when the waiting work was first known ready, on the node's clock
    private readonly IScopeActivation?[] _seen;     // the activation each scope had at the last look: a new one gets a pass at once
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _exit = new(), _kill = new();     // leave the loop between passes; stop a pass where it is
    private readonly CancellationTokenSource _heartbeatsExit;
    private readonly TaskScheduler _scheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Task? _loop, _heartbeats, _halted;
    private readonly Func<CancellationToken, Task>? _beside;
    private readonly Func<CancellationToken, Task>? _beforeRelease;
    private readonly CancellationTokenSource _besideEnd = new();     // A1: cancelled at the end of the stop, or on a kill
    private Task? _besideRun;
    private readonly object _halting = new();
    private volatile bool _gone;

    /// <param name="beside">K1b slice 5b: a loop of the host's own that runs beside the node's loops, off the node's
    /// scheduler, and ends when a stop has ended or on a kill: layer 2's watchdog (<c>Watchdog.RunAsync</c>), which closes a stuck
    /// owner-path call's connection at twice the lease and ends the process, by the host's policy, only when it cannot.</param>
    /// <param name="beforeRelease">Optional graceful drain after the last scheduler pass and before returning
    /// ownership. It may await work entered by a pass that has already returned.</param>
    public RuntimeNode(string node, IMembership membership, int partitions, LoopOptions? options = null, IWakeSignal? wake = null,
        Func<CancellationToken, Task>? beside = null, Func<CancellationToken, Task>? beforeRelease = null)
    {
        _beside = beside;
        _beforeRelease = beforeRelease;
        _heartbeatsExit = CancellationTokenSource.CreateLinkedTokenSource(_kill.Token);
        _options = options ?? new LoopOptions();
        _runtime = new PartitionRuntime(node, membership, partitions, _options.Window, wake);
        _due = new long[partitions];
        _oldestWaiting = new long?[partitions];
        _seen = new IScopeActivation?[partitions];
    }

    public string Node => _runtime.Node;
    public PartitionRuntime Runtime => _runtime;
    public int ScopeOf(InstanceRef actor) => _runtime.ScopeOf(actor);
    public ValueTask WakeAsync(InstanceRef actor, CancellationToken cancellationToken = default) => _runtime.WakeAsync(actor, cancellationToken);

    public async ValueTask StartAsync(IScopeHost host, CancellationToken cancellationToken = default)
    {
        var heartbeatEvery = host is INodeLeaseHost { NodeLease: { } lease } ? Cadence(lease, _options) : (TimeSpan?)null;
        await _runtime.StartAsync(host, cancellationToken);
        _runtime.Wake.Listen(actor =>
        {
            var scope = ScopeOf(actor);
            if (_gone || (uint)scope >= (uint)_due.Length) return;
            Volatile.Write(ref _due[scope], 0);
            Signal();
        });
        lock (_halting)
            if (_halted is null)     // a stop or kill that came first leaves the node unstarted
            {
                _loop = Task.WhenAll(OnNode(LoopAsync), OnNode(BalanceLoopAsync));
                if (heartbeatEvery is { } every) _heartbeats = Task.Run(() => HeartbeatLoopAsync(every));
                if (_beside is { } beside) _besideRun = Task.Run(() => Beside(beside));     // not in _loop: the stop must not wait on it
                return;
            }
        // A stop or kill that landed while the start validated its scopes found no host to release. The start has attached
        // one since: end that run here, before any scope was taken, so the host is free for a later runtime. Joining a
        // shutdown a kill abandoned reports a cancellation: ownership then runs out by lease, and the start returns.
        try { await OnNode(() => _runtime.StopAsync(CancellationToken.None).AsTask()); }
        catch (OperationCanceledException) { }
    }

    /// <summary>The heartbeat's cadence: at least three per lease and at most half the window apart, a second at most by default.</summary>
    public static TimeSpan Cadence(TimeSpan lease, LoopOptions options)
    {
        var most = TimeSpan.FromTicks(Math.Min(lease.Ticks / 3, options.Window.Ticks / 2));
        if (options.HeartbeatEvery is not { } every) return TimeSpan.FromTicks(Math.Min(most.Ticks, TimeSpan.TicksPerSecond));
        if (every <= TimeSpan.Zero || every > most)
            throw new ArgumentException($"The heartbeat every {every.TotalMilliseconds} ms must be at most a third of the lease ({lease.TotalMilliseconds} ms) and half the window ({options.Window.TotalMilliseconds} ms).", nameof(options));
        return every;
    }

    /// <summary>K1b: the heartbeats sent, and those that renewed.</summary>
    public (long Sent, long Renewed) Heartbeats => (Interlocked.Read(ref _beats), Interlocked.Read(ref _renewals));
    private long _beats, _renewals;

    private async Task HeartbeatLoopAsync(TimeSpan every)
    {
        var exit = _heartbeatsExit.Token;
        while (!exit.IsCancellationRequested)
        {
            var started = Now;
            try
            {
                Interlocked.Increment(ref _beats);
                if (await _runtime.HeartbeatAsync(exit)) Interlocked.Increment(ref _renewals);
                Signal();       // an answer may have asked a Worker for a pass: its stand-down, release or retake
            }
            catch (OperationCanceledException) when (exit.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                RecordError(e);    // the host counts a failed heartbeat as missed; anything else is a bug
            }
            try { await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, (long)every.TotalMilliseconds - (Now - started))), exit); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task Beside(Func<CancellationToken, Task> loop)
    {
        try { await loop(_besideEnd.Token); }
        catch (OperationCanceledException) when (_besideEnd.IsCancellationRequested) { }
        catch (Exception e)
        {
            RecordError(e);
        }
    }

    private async Task BalanceLoopAsync()
    {
        var (exit, kill) = (_exit.Token, _kill.Token);
        while (!exit.IsCancellationRequested)
        {
            var started = Now;
            try { await _runtime.BalanceAsync(kill); }
            catch (OperationCanceledException) when (kill.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                RecordError(e);
            }
            Signal();       // a scope taken gets its first pass at once
            var ms = Math.Max(0, (long)_options.BalanceEvery.TotalMilliseconds - (Now - started));
            if (ms == 0)
            {
                await Task.Yield();     // CodeRabbit 4644811407: a round that used up its interval must not hold this exclusive scheduler: LoopAsync runs on it
                continue;
            }
            try { await Task.Delay(TimeSpan.FromMilliseconds(ms), exit); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Finish the pass under way, end admitted off-turn work, then give every scope up: whoever takes them next finds no death.
    /// The call that starts the stop sets its deadline: when <paramref name="cancellationToken"/> cancels, the stop becomes
    /// a kill, also during the release, and what is still owned runs out by its lease. A call that joins a stop under way
    /// waits for it; its token ends only its own wait and never shortens the deadline the first caller set.
    /// </summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenRegistration deadline = default;
        Task halted;
        bool first;
        lock (_halting)
        {
            first = _halted is null;
            if (first) deadline = cancellationToken.Register(_kill.Cancel);
            halted = _halted ??= HaltOnceAsync();
        }
        using (deadline) await ObserveAsync(halted, first ? CancellationToken.None : cancellationToken);
    }

    // A stop's outcome is reported to the callers that wait for it. A dispose after one of them saw a failure does not
    // report the same failure again.
    private async Task ObserveAsync(Task halted, CancellationToken wait)
    {
        try { await halted.WaitAsync(wait); }
        catch (OperationCanceledException) when (wait.IsCancellationRequested) { }
        catch (Exception)
        {
            Volatile.Write(ref _outcomeSeen, 1);
            throw;
        }
    }

    private int _outcomeSeen;

    /// <summary>
    /// A crash: the loop stops (a pass under way stops at a turn boundary, before its commit) and nothing more is given
    /// up, also when a stop is under way: a kill during a stop ends its release. A stop or dispose after a kill does
    /// nothing. Background jobs already under way run on, as in a process that lives on after it was declared dead;
    /// their results are dropped with the worker's memory.
    /// </summary>
    public async ValueTask KillAsync()
    {
        _kill.Cancel();
        await HaltAsync();
    }

    /// <summary>A stop bounded by <see cref="LoopOptions.DisposeDeadline"/>. It does not rethrow a failure a stop already reported.</summary>
    public async ValueTask DisposeAsync()
    {
        var reported = Volatile.Read(ref _outcomeSeen) == 1;
        using var deadline = new CancellationTokenSource(_options.DisposeDeadline);
        try { await StopAsync(deadline.Token); }
        catch (Exception) when (reported) { }
    }

    /// <summary>The first stop or kill leaves the loop and, unless killed, gives every scope up; every caller waits for it.</summary>
    private Task HaltAsync()
    {
        lock (_halting) return _halted ??= HaltOnceAsync();
    }

    private async Task HaltOnceAsync()     // A1: the watchdog guards the last pass and the releases, so it ends after them
    {
        try { await HaltLoopsAsync(); }
        finally { _besideEnd.Cancel(); if (_besideRun is not null) await _besideRun; }
    }

    private async Task HaltLoopsAsync()
    {
        _gone = true;
        _exit.Cancel();

        // The node lease keeps renewing through both drains: the entered actor turns here, then the host's entered jobs
        // inside the runtime's stop. Heartbeats end just before the first release, or at the deadline (the kill token).
        Exception? drainError = null;
        try
        {
            if (_loop is not null) await _loop;
            if (_beforeRelease is { } drain && !_kill.IsCancellationRequested)
            {
                try { await OnNode(() => drain(_kill.Token)).WaitAsync(_kill.Token); }
                catch (OperationCanceledException) when (_kill.IsCancellationRequested) { }
                catch (Exception error) { drainError = error; }
            }

            // A kill during shutdown leaves remaining ownership to expire instead of returning it as clean.
            if (!_kill.IsCancellationRequested)
            {
                try { await OnNode(() => _runtime.StopAsync(_kill.Token, StopHeartbeatsAsync).AsTask()).WaitAsync(_kill.Token); }
                catch (OperationCanceledException) when (_kill.IsCancellationRequested) { }
                catch (Exception releaseError) when (drainError is not null) { throw new AggregateException(drainError, releaseError); }
            }
        }
        finally { await StopHeartbeatsAsync(); }
        if (drainError is not null) ExceptionDispatchInfo.Throw(drainError);
    }

    private async Task StopHeartbeatsAsync()
    {
        _heartbeatsExit.Cancel();
        if (_heartbeats is not null) await _heartbeats;
    }

    /// <summary>Run on the node's scheduler, one task at a time with the loop and what it started.</summary>
    private Task OnNode(Func<Task> work) => Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.DenyChildAttach, _scheduler).Unwrap();

    private long Now => _clock.ElapsedMilliseconds;
    private long _errors;
    private Exception? _lastError;

    /// <summary>What the loop caught that was not a cancellation: zero unless something is broken.</summary>
    public long Errors => Interlocked.Read(ref _errors);
    public Exception? LastError => Volatile.Read(ref _lastError);

    private void RecordError(Exception error)
    {
        // Readers may use the count to decide when there is an error to inspect.
        Volatile.Write(ref _lastError, error);
        Interlocked.Increment(ref _errors);
    }

    /// <summary>Moves a due time earlier, never later: a hint that sets it to now survives a pass that asks for later.</summary>
    private static void Sooner(ref long due, long at)
    {
        for (var seen = Volatile.Read(ref due); at < seen; seen = Volatile.Read(ref due))
            if (Interlocked.CompareExchange(ref due, at, seen) == seen) return;
    }

    private void Signal()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* already signalled */ }
    }

    private async Task LoopAsync()
    {
        var (exit, kill) = (_exit.Token, _kill.Token);
        var partitions = _due.Length;
        long pollAt = Now + (long)_options.PollInterval.TotalMilliseconds;
        var ready = new List<(int Scope, IScopeActivation Activation)>();
        while (!exit.IsCancellationRequested)
        {
            try
            {
                await _runtime.NodePassAsync(kill);     // K1b: give pending scopes and heartbeat answers a pass
                if (Now >= pollAt)
                {
                    pollAt = Now + (long)_options.PollInterval.TotalMilliseconds;
                    for (var scope = 0; scope < partitions; scope++)
                        if (_runtime.Activation(scope) is { } activation)
                        {
                            await activation.WokenAsync(null, kill);
                            Volatile.Write(ref _due[scope], 0);
                        }
                }
                ready.Clear();
                for (var scope = 0; scope < partitions; scope++)
                {
                    var activation = _runtime.Activation(scope);
                    if (activation != _seen[scope])
                    {
                        _seen[scope] = activation;
                        _oldestWaiting[scope] = null;
                        if (activation is not null) Volatile.Write(ref _due[scope], 0);    // a scope taken (again) gets its first pass at once
                    }
                    if (activation is not null && Volatile.Read(ref _due[scope]) <= Now)
                        ready.Add((scope, activation));
                }
                ready.Sort((left, right) =>
                {
                    var order = (_oldestWaiting[left.Scope] ?? long.MaxValue)
                        .CompareTo(_oldestWaiting[right.Scope] ?? long.MaxValue);
                    return order != 0 ? order : left.Scope.CompareTo(right.Scope);
                });
                foreach (var (scope, activation) in ready)
                {
                    if (exit.IsCancellationRequested) break;
                    if (_runtime.Activation(scope) != activation) continue;
                    Interlocked.Exchange(ref _due[scope], long.MaxValue);
                    ScopePass next = new(TimeSpan.FromMilliseconds(100)); // retry a throwing pass before the next periodic poll
                    try { next = await _runtime.PassWithAgeAsync(scope, kill); }
                    finally
                    {
                        var sampledAt = Now;
                        _oldestWaiting[scope] = next.OldestWaitingAge is { } age && next.Delay == TimeSpan.Zero
                            ? sampledAt - (long)Math.Max(0, age.TotalMilliseconds) : null;
                        if (next.Delay is { } delay) Sooner(ref _due[scope], sampledAt + (long)delay.TotalMilliseconds);
                        else if (!_runtime.Owns(scope)) _seen[scope] = null;   // lost: the node gets the same activation back when it takes the scope again
                    }
                }
            }
            catch (OperationCanceledException) when (kill.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                // The kernel catches what its store throws; anything else is a bug the loop must survive and count.
                RecordError(e);
                try { await Task.Delay(TimeSpan.FromMilliseconds(100), exit); }      // no spin on a bug that throws every time
                catch (OperationCanceledException) { break; }
            }
            var wait = pollAt;
            for (var scope = 0; scope < partitions; scope++)
                if (_runtime.Owns(scope)) wait = Math.Min(wait, Volatile.Read(ref _due[scope]));
            var ms = Math.Clamp(wait - Now, 0, int.MaxValue);
            if (ms == 0)
            {
                await Task.Yield();     // a Zero-delay pass must not hold this exclusive scheduler: BalanceLoopAsync runs on it
                continue;
            }
            try { await _signal.WaitAsync(TimeSpan.FromMilliseconds(ms), exit); }
            catch (OperationCanceledException) { break; }
        }
    }
}
