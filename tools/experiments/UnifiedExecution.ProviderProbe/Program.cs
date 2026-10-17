using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime.MembershipService.SiloMetadata;
using Elsa.UnifiedExecution.WorkProof;

if (args.Length == 0)
    throw new ArgumentException("Expected 'silo', 'client', or a provider proof client command.");

if (args[0] == "silo")
{
    int siloPort = int.Parse(args[1]);
    string clusterId = args.Length > 2 ? args[2] : "elsa-preflight";
    string capabilities = args.Length > 3 ? args[3] : "sequence-v1,flowchart-v1";
    int primarySiloPort = args.Length > 4 ? int.Parse(args[4]) : 19111;
    string workDatabase = Environment.GetEnvironmentVariable("ELSA_WORK_DB")
        ?? Path.Combine(Path.GetTempPath(), $"elsa-provider-probe-{Environment.ProcessId}.sqlite");
    string proofKey = Environment.GetEnvironmentVariable("ELSA_WORK_PROOF_KEY") ?? ProviderClientCommands.ProofKey;

    using IHost host = Host.CreateDefaultBuilder()
        .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
        .UseOrleans(silo =>
        {
            silo.UseLocalhostClustering(
                siloPort: siloPort,
                gatewayPort: siloPort + 100,
                primarySiloEndpoint: new IPEndPoint(IPAddress.Loopback, primarySiloPort),
                serviceId: "elsa-evidence",
                clusterId: clusterId);
            silo.UseSiloMetadata(new Dictionary<string, string>
            {
                [CapabilityPlacementDirector.MetadataKey] = capabilities
            });
            silo.AddPlacementDirector<CapabilityPlacementStrategy, CapabilityPlacementDirector>();
            silo.ConfigureServices(services =>
            {
                services.AddSingleton(new HmacWorkScopeAuthorizer(proofKey));
                services.AddSingleton(serviceProvider => new DurableWorkStore(
                    workDatabase,
                    serviceProvider.GetRequiredService<HmacWorkScopeAuthorizer>()));
                services.AddSingleton(serviceProvider => new IndependentSink(
                    Environment.GetEnvironmentVariable("ELSA_SINK_DB") ?? workDatabase + ".sink",
                    serviceProvider.GetRequiredService<HmacWorkScopeAuthorizer>()));
            });
        })
        .Build();

    await host.StartAsync();
    Console.WriteLine($"SILO_READY {siloPort} pid={Environment.ProcessId} capabilities={capabilities}");
    await host.WaitForShutdownAsync();
    return 0;
}

if (args[0] == "client")
{
    using IHost host = Host.CreateDefaultBuilder()
        .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
        .UseOrleansClient(client => client.UseLocalhostClustering(
            gatewayPorts: args.Length > 2 ? [int.Parse(args[2])] : [19211, 19212],
            serviceId: "elsa-evidence",
            clusterId: "elsa-preflight"))
        .Build();

    await host.StartAsync();
    IClusterClient client = host.Services.GetRequiredService<IClusterClient>();
    IProbe grain = client.GetGrain<IProbe>(args.Length > 1 ? args[1] : "same-execution");
    Console.WriteLine("GRAIN_LOCATION " + await grain.Ping());
    await host.StopAsync();
    return 0;
}

if (args[0] == "builtin-run")
    return await BuiltInCoordinatorProbe.RunAsync(args);

return await ProviderClientCommands.RunAsync(args);

public interface IProbe : IGrainWithStringKey
{
    Task<string> Ping();
}

[Orleans.Placement.PreferLocalPlacement]
public sealed class ProbeGrain : Grain, IProbe
{
    public Task<string> Ping() => Task.FromResult($"{this.GetPrimaryKeyString()} pid={Environment.ProcessId}");
}
