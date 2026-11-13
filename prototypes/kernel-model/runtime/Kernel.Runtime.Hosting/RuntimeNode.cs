using System.Diagnostics;

namespace Kernel.Runtime.Hosting;

/// <summary>When the loop of a <see cref="RuntimeNode"/> does what.</summary>
public sealed record LoopOptions
{
    /// <summary>A round of the fair share: the node's heartbeat, and the partitions it gives up or asks for.</summary>
    public TimeSpan BalanceEvery { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The nodes heard from within this window count for the fair share. Two rounds or more.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Every held scope reads its inbox at least this often: the guarantee behind a lost wake hint.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// One node of the built-in runtime, on a clock: a <see cref="PartitionRuntime"/> and the loop that the kernel leaves to
/// its host. The loop balances every <see cref="LoopOptions.BalanceEvery"/>, runs a pass of every scope it holds when
/// the last pass asked for one (at once, after a timer's delay, or at the next renewal of its lease), runs one at once
/// when a hint comes, and tells every held scope to read its inbox every <see cref="LoopOptions.PollInterval"/>. One
/// loop per node: its passes run one after another, so a scope never has two at once and the workers of the node may
/// share their trial gate. The loop runs on a scheduler of the node's own that runs one task at a time, as a grain's
/// does in Orleans: what a pass starts and awaits elsewhere (a background job's continuation) comes back on it, between
/// the loop's awaits, and never beside them. A worker is a single-threaded state machine.
/// </summary>
public sealed class RuntimeNode : IActorRuntime, IAsyncDisposable
{
    private readonly PartitionRuntime _runtime;
    private readonly LoopOptions _options;
    private readonly long[] _due;        // when each held scope's next pass is due, on the node's clock; long.MaxValue: when woken
    private readonly IScopeActivation?[] _seen;     // the activation each scope had at the last look: a new one gets a pass at once
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _exit = new(), _kill = new();     // leave the loop between passes; stop a pass where it is
    private readonly TaskScheduler _scheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Task? _loop, _halted;
    private readonly object _halting = new();
    private volatile bool _gone;

    public RuntimeNode(string node, IMembership membership, int partitions, LoopOptions? options = null, IWakeSignal? wake = null)
    {
        _options = options ?? new LoopOptions();
        _runtime = new PartitionRuntime(node, membership, partitions, _options.Window, wake);
        _due = new long[partitions];
        _seen = new IScopeActivation?[partitions];
    }

    public string Node => _runtime.Node;
    public PartitionRuntime Runtime => _runtime;
    public int ScopeOf(InstanceRef actor) => _runtime.ScopeOf(actor);
    public ValueTask WakeAsync(InstanceRef actor, CancellationToken cancellationToken = default) => _runtime.WakeAsync(actor, cancellationToken);

    public async ValueTask StartAsync(IScopeHost host, CancellationToken cancellationToken = default)
    {
        await _runtime.StartAsync(host, cancellationToken);
        _runtime.Wake.Listen(actor =>
        {
            var scope = ScopeOf(actor);
            if (_gone || (uint)scope >= (uint)_due.Length) return;
            Volatile.Write(ref _due[scope], 0);
            Signal();
        });
        _loop = OnNode(LoopAsync);
    }

    /// <summary>
    /// Leave the loop once the pass under way has ended (it is not cancelled, so its commit lands or is refused), then give
    /// every scope up: whoever takes them next finds no death.
    /// </summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        using (cancellationToken.Register(_kill.Cancel))      // a stop that may not wait any longer becomes a kill, also during the release
            await HaltAsync();
    }

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

    public ValueTask DisposeAsync() => StopAsync();

    /// <summary>The first stop or kill leaves the loop and, unless killed, gives every scope up; every caller waits for it.</summary>
    private Task HaltAsync()
    {
        lock (_halting) return _halted ??= HaltOnceAsync();
    }

    private async Task HaltOnceAsync()
    {
        _gone = true;
        _exit.Cancel();
        if (_loop is not null) await _loop;
        if (!_kill.IsCancellationRequested) await OnNode(() => _runtime.StopAsync(_kill.Token).AsTask());
    }

    /// <summary>Run on the node's scheduler, one task at a time with the loop and what it started.</summary>
    private Task OnNode(Func<Task> work) => Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.DenyChildAttach, _scheduler).Unwrap();

    private long Now => _clock.ElapsedMilliseconds;
    private long _errors;

    /// <summary>What the loop caught that was not a cancellation: zero unless something is broken.</summary>
    public long Errors => Interlocked.Read(ref _errors);
    public Exception? LastError { get; private set; }

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
        long balanceAt = 0, pollAt = Now + (long)_options.PollInterval.TotalMilliseconds;
        while (!exit.IsCancellationRequested)
        {
            try
            {
                if (Now >= balanceAt)
                {
                    balanceAt = Now + (long)_options.BalanceEvery.TotalMilliseconds;
                    await _runtime.BalanceAsync(kill);
                }
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
                for (var scope = 0; scope < partitions && !exit.IsCancellationRequested; scope++)
                {
                    var activation = _runtime.Activation(scope);
                    if (activation != _seen[scope])
                    {
                        _seen[scope] = activation;
                        if (activation is not null) Volatile.Write(ref _due[scope], 0);    // a scope taken (again) gets its first pass at once
                    }
                    if (activation is null) continue;
                    if (Volatile.Read(ref _due[scope]) > Now) continue;
                    Volatile.Write(ref _due[scope], long.MaxValue);
                    TimeSpan? next = TimeSpan.FromMilliseconds(100);      // what a pass that throws gets: another try soon, not at the next poll
                    try { next = await _runtime.PassAsync(scope, kill); }
                    finally
                    {
                        if (next is { } delay) Sooner(ref _due[scope], Now + (long)delay.TotalMilliseconds);
                        else if (!_runtime.Owns(scope)) _seen[scope] = null;   // lost: the node gets the same activation back when it takes the scope again
                    }
                }
            }
            catch (OperationCanceledException) when (kill.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                // The kernel catches what its store throws; anything else is a bug the loop must survive and count.
                Interlocked.Increment(ref _errors);
                LastError = e;
                try { await Task.Delay(TimeSpan.FromMilliseconds(100), exit); }      // no spin on a bug that throws every time
                catch (OperationCanceledException) { break; }
            }
            var wait = Math.Min(balanceAt, pollAt);
            for (var scope = 0; scope < partitions; scope++)
                if (_runtime.Owns(scope)) wait = Math.Min(wait, Volatile.Read(ref _due[scope]));
            var ms = Math.Clamp(wait - Now, 0, int.MaxValue);
            if (ms == 0) continue;
            try { await _signal.WaitAsync(TimeSpan.FromMilliseconds(ms), exit); }
            catch (OperationCanceledException) { break; }
        }
    }
}
