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
/// share their trial gate.
/// </summary>
public sealed class RuntimeNode : IActorRuntime, IAsyncDisposable
{
    private readonly PartitionRuntime _runtime;
    private readonly LoopOptions _options;
    private readonly long[] _due;        // when each held scope's next pass is due, on the node's clock; long.MaxValue: when woken
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Task? _loop;
    private volatile bool _gone;

    public RuntimeNode(string node, IMembership membership, int partitions, LoopOptions? options = null, IWakeSignal? wake = null)
    {
        _options = options ?? new LoopOptions();
        _runtime = new PartitionRuntime(node, membership, partitions, _options.Window, wake);
        _due = Enumerable.Repeat(long.MaxValue, partitions).ToArray();
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
            if (_gone) return;
            Volatile.Write(ref _due[ScopeOf(actor)], 0);
            Signal();
        });
        _loop = Task.Run(() => LoopAsync(_stop.Token), CancellationToken.None);
    }

    /// <summary>Stop the loop and give every scope up: whoever takes them next finds no death.</summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (await HaltAsync()) await _runtime.StopAsync(cancellationToken);
    }

    /// <summary>A crash: the loop stops (a pass under way stops at a turn boundary, before its commit) and nothing is given up.</summary>
    public async ValueTask KillAsync() => await HaltAsync();

    public ValueTask DisposeAsync() => StopAsync();

    private async Task<bool> HaltAsync()
    {
        if (_gone) return false;
        _gone = true;
        _stop.Cancel();
        if (_loop is not null) await _loop;
        return true;
    }

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

    private async Task LoopAsync(CancellationToken stop)
    {
        var partitions = _due.Length;
        long balanceAt = 0, pollAt = Now + (long)_options.PollInterval.TotalMilliseconds;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (Now >= balanceAt)
                {
                    await _runtime.BalanceAsync(stop);
                    balanceAt = Now + (long)_options.BalanceEvery.TotalMilliseconds;
                }
                if (Now >= pollAt)
                {
                    for (var scope = 0; scope < partitions; scope++)
                        if (_runtime.Activation(scope) is { } activation)
                        {
                            await activation.WokenAsync(null, stop);
                            Volatile.Write(ref _due[scope], 0);
                        }
                    pollAt = Now + (long)_options.PollInterval.TotalMilliseconds;
                }
                for (var scope = 0; scope < partitions && !stop.IsCancellationRequested; scope++)
                {
                    if (!_runtime.Owns(scope))
                    {
                        Volatile.Write(ref _due[scope], 0);    // a scope taken (again) gets its first pass at once
                        continue;
                    }
                    if (Volatile.Read(ref _due[scope]) > Now) continue;
                    Volatile.Write(ref _due[scope], long.MaxValue);
                    if (await _runtime.PassAsync(scope, stop) is { } delay) Sooner(ref _due[scope], Now + (long)delay.TotalMilliseconds);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                // The kernel catches what its store throws; anything else is a bug the loop must survive and count.
                Interlocked.Increment(ref _errors);
                LastError = e;
            }
            var wait = Math.Min(balanceAt, pollAt);
            for (var scope = 0; scope < partitions; scope++)
                if (_runtime.Owns(scope)) wait = Math.Min(wait, Volatile.Read(ref _due[scope]));
            var ms = Math.Clamp(wait - Now, 0, int.MaxValue);
            if (ms == 0) continue;
            try { await _signal.WaitAsync(TimeSpan.FromMilliseconds(ms), stop); }
            catch (OperationCanceledException) { break; }
        }
    }
}
