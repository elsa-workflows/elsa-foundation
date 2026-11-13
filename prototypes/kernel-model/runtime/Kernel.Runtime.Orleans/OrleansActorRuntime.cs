using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;

namespace Kernel.Runtime.Orleans;

/// <summary>How the partition grains of one silo behave.</summary>
public sealed record OrleansRuntimeOptions
{
    /// <summary>The partitions of the store: one grain each, keyed by the partition.</summary>
    public int Partitions { get; init; } = Defaults.ServerPartitions;

    /// <summary>Every held scope reads its inbox at least this often: the guarantee behind a lost wake hint.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>A grain whose activation layer 2 declined (another node holds the fence) asks again after this.</summary>
    public TimeSpan RetryEvery { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often the silo's keeper calls every partition grain, so that each is active somewhere, also after a silo died.</summary>
    public TimeSpan KeepEvery { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Layer 1 on Orleans, one per silo: each partition is a grain (<see cref="PartitionGrain"/>), keyed by the partition,
/// that Orleans places and activates; there is no grain per instance and no sweep. A partition grain activates its
/// scope through layer 2's <see cref="IScopeHost"/>, which takes the fence or declines, and runs its passes on a grain
/// timer, one at a time, because a grain is single-threaded. A wake hint is a one-way grain call. Orleans may activate a
/// grain twice for a while (a network split, a silo declared dead too early); the fence lets one of them write.
/// </summary>
public sealed class OrleansActorRuntime(IGrainFactory grains, OrleansRuntimeOptions options) : IActorRuntime
{
    private readonly TaskCompletionSource<IScopeHost> _host = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stopping = new();

    public OrleansRuntimeOptions Options => options;

    /// <summary>Layer 2's host, once <see cref="StartAsync"/> gave it; until then a grain activated on this silo waits.</summary>
    internal IScopeHost? Host => _host.Task.IsCompletedSuccessfully ? _host.Task.Result : null;

    internal Task Started => _host.Task;

    /// <summary>Cancelled when the silo stops: a pass under way stops at a turn boundary, before its commit.</summary>
    internal CancellationToken Stopping => _stopping.Token;

    public int ScopeOf(InstanceRef actor) => actor.Partition;

    public ValueTask StartAsync(IScopeHost host, CancellationToken cancellationToken = default)
    {
        _host.TrySetResult(host);
        return ValueTask.CompletedTask;
    }

    public async ValueTask WakeAsync(InstanceRef actor, CancellationToken cancellationToken = default) =>
        await grains.GetGrain<IPartitionGrain>(ScopeOf(actor)).WakeAsync(actor.Id);

    /// <summary>
    /// Stops the passes of this silo's grains. Called before the silo stops: a graceful stop then deactivates the grains,
    /// which give their scopes up; a crash gives nothing up.
    /// </summary>
    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _stopping.Cancel();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Calls every partition grain every <see cref="OrleansRuntimeOptions.KeepEvery"/>, so that each is active on some silo of the cluster.</summary>
internal sealed class PartitionKeeper(OrleansActorRuntime runtime, IGrainFactory grains) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await runtime.Started.WaitAsync(stoppingToken); }
        catch (OperationCanceledException) { return; }
        var options = runtime.Options;
        while (!stoppingToken.IsCancellationRequested && !runtime.Stopping.IsCancellationRequested)
        {
            var calls = Enumerable.Range(0, options.Partitions).Select(async p =>
            {
                try { await grains.GetGrain<IPartitionGrain>(p).EnsureAsync(); }
                catch (Exception) { /* a silo on its way out: the next round calls again */ }
            });
            await Task.WhenAll(calls);
            try { await Task.Delay(options.KeepEvery, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}

public static class SiloBuilderExtensions
{
    /// <summary>The kernel's actor runtime on this silo. The host starts it with layer 2's <see cref="IScopeHost"/> (<see cref="IActorRuntime.StartAsync"/>).</summary>
    public static ISiloBuilder AddKernelRuntime(this ISiloBuilder silo, OrleansRuntimeOptions options)
    {
        silo.Services.AddSingleton(options);
        silo.Services.AddSingleton<OrleansActorRuntime>();
        silo.Services.AddHostedService<PartitionKeeper>();
        return silo;
    }
}
