using Kernel.Runtime.Conformance;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Kernel.Runtime.Orleans.Conformance;

/// <summary>
/// The Orleans adapter for the runtime conformance suite, on Orleans' in-process test cluster. A side of a network split
/// is a cluster of its own (its own cluster id, membership and grain directory) over the same store: each side's
/// silos activate every partition grain, so a scope has an activation on each side for the whole time both sides live,
/// as it would while a network is split. Silos are started, stopped and killed through the test cluster.
/// </summary>
public sealed class OrleansProvider : IRuntimeProvider
{
    public string Name => "orleans";

    public Task<IRuntimeCluster> CreateAsync(RuntimeEnvironment environment) => Task.FromResult<IRuntimeCluster>(new Cluster(environment));

    private sealed class Cluster(RuntimeEnvironment environment) : IRuntimeCluster
    {
        private readonly Dictionary<int, InProcessTestCluster> _sides = [];
        private readonly SemaphoreSlim _starting = new(1, 1);
        private int _started;

        public async Task<IRuntimeNode> StartNodeAsync(int side = 0)
        {
            await _starting.WaitAsync();
            try
            {
                var name = $"orleans.{side}.{++_started}";
                InProcessSiloHandle silo;
                if (_sides.TryGetValue(side, out var cluster)) silo = await cluster.StartAdditionalSiloAsync();
                else
                {
                    cluster = _sides[side] = Build(side);
                    await cluster.DeployAsync();
                    silo = cluster.Silos[0];
                }
                var runtime = silo.ServiceProvider.GetRequiredService<OrleansActorRuntime>();
                await runtime.StartAsync(environment.Node(name).Host);
                return new Node(name, side, cluster, silo, runtime);
            }
            finally { _starting.Release(); }
        }

        private InProcessTestCluster Build(int side)
        {
            var lease = environment.Timing.Lease;
            var builder = new InProcessTestClusterBuilder(1);
            builder.Options.ClusterId = $"kernel-side-{side}-{Guid.NewGuid():N}";
            builder.Options.ServiceId = $"kernel-{Guid.NewGuid():N}";
            builder.Options.InitializeClientOnDeploy = false;
            builder.ConfigureSilo((_, silo) =>
            {
                silo.AddKernelRuntime(new OrleansRuntimeOptions
                {
                    Partitions = environment.Partitions, PollInterval = environment.Timing.PollInterval, RetryEvery = lease / 4, KeepEvery = lease / 4,
                });
                // A killed silo is declared dead within about a second, and a call to a grain it held times out soon.
                silo.Configure<ClusterMembershipOptions>(m =>
                {
                    m.ProbeTimeout = TimeSpan.FromMilliseconds(300);
                    m.NumMissedProbesLimit = 2;
                    m.NumVotesForDeathDeclaration = 1;
                    m.TableRefreshTimeout = TimeSpan.FromMilliseconds(500);
                });
                silo.Configure<SiloMessagingOptions>(m => m.ResponseTimeout = TimeSpan.FromSeconds(2));
            });
            return builder.Build();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var cluster in _sides.Values)
            {
                foreach (var silo in cluster.GetActiveSilos().ToList())
                    try { await silo.ServiceProvider.GetRequiredService<OrleansActorRuntime>().StopAsync(); }
                    catch (ObjectDisposedException) { }
                await cluster.StopAllSilosAsync();
                await cluster.DisposeAsync();
            }
        }
    }

    private sealed class Node(string name, int side, InProcessTestCluster cluster, InProcessSiloHandle silo, OrleansActorRuntime runtime) : IRuntimeNode
    {
        public string Name => name;
        public int Side => side;
        public ValueTask WakeAsync(InstanceRef actor) => runtime.WakeAsync(actor);

        public async Task StopAsync()
        {
            await runtime.StopAsync();
            await cluster.StopSiloAsync(silo);
        }

        public async Task KillAsync()
        {
            await runtime.StopAsync();
            await cluster.KillSiloAsync(silo);
        }
    }
}
