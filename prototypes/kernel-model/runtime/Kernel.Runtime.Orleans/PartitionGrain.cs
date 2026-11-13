using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Kernel.Runtime.Orleans;

/// <summary>One partition of the store, as an Orleans grain keyed by the partition.</summary>
public interface IPartitionGrain : IGrainWithIntegerKey
{
    /// <summary>Nothing but the call: it activates the grain if no silo has it active. The keeper's.</summary>
    Task EnsureAsync();

    /// <summary>A hint: an actor of this partition may have work. One-way, and not needed for correctness.</summary>
    [OneWay]
    Task WakeAsync(string actor);
}

/// <summary>
/// A partition's scope while Orleans has this grain active on this silo. Every call and every timer tick is one turn of
/// the grain, so passes never overlap. The timer is armed for one tick at a time, at the delay the last pass asked for
/// (at once while there is work, a timer's or the lease renewal's delay otherwise), and no later than the poll interval.
/// When layer 2 declines the activation, or the fence is lost, the grain asks again every
/// <see cref="OrleansRuntimeOptions.RetryEvery"/>; deactivation gives the scope up.
/// </summary>
public sealed class PartitionGrain(OrleansActorRuntime runtime) : Grain, IPartitionGrain
{
    private IScopeActivation? _activation;
    private IGrainTimer? _timer;
    private long _pollAt;

    private int Scope => (int)this.GetPrimaryKeyLong();
    private static long Now => Environment.TickCount64;
    private OrleansRuntimeOptions Options => runtime.Options;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _timer = this.RegisterGrainTimer(TickAsync, new GrainTimerCreationOptions(TimeSpan.Zero, Timeout.InfiniteTimeSpan) { KeepAlive = true });
        return Task.CompletedTask;
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        if (_activation is { } activation)
        {
            _activation = null;
            await activation.DisposeAsync();    // gives the scope up: whoever takes it next finds no death
        }
    }

    public Task EnsureAsync() => Task.CompletedTask;

    public async Task WakeAsync(string actor)
    {
        if (_activation is { Active: true } activation) await activation.WokenAsync(new InstanceRef(actor), runtime.Crashing);
        Arm(TimeSpan.Zero);
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        if (runtime.Stopping.IsCancellationRequested) return;     // the silo is on its way out: no further pass
        var next = Options.RetryEvery;
        try { next = await PassAsync(runtime.Crashing); }
        catch (OperationCanceledException) when (runtime.Crashing.IsCancellationRequested) { return; }
        catch (Exception) { /* layer 2 catches what its store throws; anything else waits for the next try */ }
        if (!runtime.Stopping.IsCancellationRequested) Arm(next);
    }

    /// <summary>Activate the scope if this grain does not hold it, wake it at the poll interval, run one pass. Returns when to tick again.</summary>
    private async Task<TimeSpan> PassAsync(CancellationToken stopping)
    {
        if (_activation is not { Active: true })
        {
            if (runtime.Host is not { } host) return Options.RetryEvery;
            // A lost activation is not given up: layer 2 already dropped its memory, and the node takes the scope again.
            _activation = await host.ActivateAsync(Scope, stopping);
            if (_activation is null) return Options.RetryEvery;
            await _activation.WokenAsync(null, stopping);     // a take starts from empty memory: the first pass reads the inbox
            _pollAt = Now + (long)Options.PollInterval.TotalMilliseconds;
        }
        else if (Now >= _pollAt)
        {
            await _activation.WokenAsync(null, stopping);
            _pollAt = Now + (long)Options.PollInterval.TotalMilliseconds;
        }
        var untilPoll = TimeSpan.FromMilliseconds(Math.Max(0, _pollAt - Now));
        return await _activation.RunAsync(stopping) switch
        {
            null => Options.RetryEvery,         // the fence was lost: take the scope again once it is free
            { } delay when delay < untilPoll => delay,
            _ => untilPoll,
        };
    }

    private void Arm(TimeSpan due) => _timer?.Change(due, Timeout.InfiniteTimeSpan);
}
