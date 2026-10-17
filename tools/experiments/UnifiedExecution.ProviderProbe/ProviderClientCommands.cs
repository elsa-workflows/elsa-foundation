using System.Text;
using System.Text.Json;
using Elsa.UnifiedExecution.WorkProof;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;

internal static class ProviderClientCommands
{
    public const string ProofKey = "unified-work-proof-local-key-2026";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args[0] == "canonical-key")
        {
            WorkScope keyScope = JsonSerializer.Deserialize<WorkScope>(Decode(args[1]))
                ?? throw new InvalidDataException("The canonical-key scope was empty.");
            Console.WriteLine(ExecutionGrainKey.Create(keyScope, args[2], args[3]));
            return 0;
        }
        if (args[0].StartsWith("barrier-", StringComparison.Ordinal))
            return RunBarrierCommand(args);
        if (args[0] == "capability-probe")
            return await ProbeCapabilityAsync(args);
        if (args[0] == "proof-mismatch")
            return await ProbeIdentityMismatchAsync(args);

        string command = args[0];
        int gateway = int.Parse(args[1]);
        string clusterId = args[2];
        string scopeJson = Decode(args[3]);
        WorkScope scope = JsonSerializer.Deserialize<WorkScope>(scopeJson)
            ?? throw new InvalidDataException("The supplied scope is empty.");
        string executionId = args[4];
        string policyName = args[5];
        using IHost host = await ConnectAsync(gateway, clusterId);
        IClusterClient client = host.Services.GetRequiredService<IClusterClient>();
        IExecutionGrain grain = client.GetGrain<IExecutionGrain>(ExecutionGrainKey.Create(scope, executionId, policyName));

        switch (command)
        {
            case "proof-start":
                Console.WriteLine(await grain.Start(scopeJson, executionId, policyName, Decode(args[6])));
                return 0;
            case "proof-read":
                Console.WriteLine(await grain.Read(scopeJson, executionId, policyName));
                return 0;
            case "proof-pump":
                Console.WriteLine(await grain.Pump(scopeJson, executionId, policyName));
                return 0;
            case "proof-stale":
                Console.WriteLine(await grain.ProbeStaleOwner(scopeJson, executionId, policyName, Decode(args[6]), long.Parse(args[7])));
                return 0;
            default:
                throw new ArgumentException($"Unknown provider client command '{command}'.");
        }
    }

    private static int RunBarrierCommand(string[] args)
    {
        string workDatabase = Environment.GetEnvironmentVariable("ELSA_WORK_DB")
            ?? throw new InvalidOperationException("ELSA_WORK_DB is required for barrier inspection.");
        string key = Environment.GetEnvironmentVariable("ELSA_WORK_PROOF_KEY") ?? ProofKey;
        var store = new DurableWorkStore(workDatabase, new HmacWorkScopeAuthorizer(key));
        string barrierId = args[1];
        switch (args[0])
        {
            case "barrier-create":
                store.CreateBarrier(barrierId);
                Console.WriteLine("BARRIER_CREATED " + barrierId);
                return 0;
            case "barrier-state":
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    barrierId,
                    count = store.BarrierEntryCount(barrierId),
                    processIds = store.BarrierProcessIds(barrierId),
                    released = store.IsBarrierReleased(barrierId)
                }));
                return 0;
            case "barrier-release":
                store.ReleaseBarrier(barrierId);
                Console.WriteLine("BARRIER_RELEASED " + barrierId);
                return 0;
            default:
                throw new ArgumentException($"Unknown barrier command '{args[0]}'.");
        }
    }

    private static async Task<int> ProbeCapabilityAsync(string[] args)
    {
        try
        {
            int gateway = int.Parse(args[1]);
            string clusterId = args[2];
            string unsupportedCapability = args[3];
            using IHost host = await ConnectAsync(gateway, clusterId);
            IExecutionGrain grain = host.Services.GetRequiredService<IClusterClient>()
                .GetGrain<IExecutionGrain>($"{unsupportedCapability}|unsupported-{Guid.NewGuid():N}");
            Console.WriteLine(await grain.GetLocation());
            Console.Error.WriteLine("Expected an unsupported capability to be refused, but the grain activated.");
            return 1;
        }
        catch (Exception exception) when (exception.ToString().Contains("No active Orleans silo advertises execution capability", StringComparison.Ordinal))
        {
            Console.WriteLine("CAPABILITY_REFUSED " + exception.GetType().Name + ": " + exception.Message);
            return 0;
        }
    }

    private static async Task<int> ProbeIdentityMismatchAsync(string[] args)
    {
        int gateway = int.Parse(args[1]);
        string clusterId = args[2];
        string addressedKey = Decode(args[3]);
        string payloadScopeJson = Decode(args[4]);
        string payloadExecutionId = args[5];
        string policyName = args[6];
        WorkScope payloadScope = JsonSerializer.Deserialize<WorkScope>(payloadScopeJson)
            ?? throw new InvalidDataException("The mismatch probe scope is empty.");
        using IHost host = await ConnectAsync(gateway, clusterId);
        IExecutionGrain grain = host.Services.GetRequiredService<IClusterClient>().GetGrain<IExecutionGrain>(addressedKey);
        try
        {
            Console.WriteLine(await grain.Read(payloadScopeJson, payloadExecutionId, policyName));
            Console.Error.WriteLine("Expected the addressed grain key and supplied execution identity to differ.");
            return 1;
        }
        catch (Exception exception) when (exception.ToString().Contains("Execution grain identity mismatch", StringComparison.Ordinal))
        {
            Console.WriteLine($"IDENTITY_MISMATCH_REJECTED tenant={payloadScope.TenantId} execution={payloadExecutionId}");
            return 0;
        }
        catch (Exception exception) when (exception.ToString().Contains("must not contain CR or LF", StringComparison.Ordinal))
        {
            Console.WriteLine("CANONICAL_SCOPE_REJECTED before durable access");
            return 0;
        }
    }

    private static async Task<IHost> ConnectAsync(int gateway, string clusterId)
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .UseOrleansClient(client => client.UseLocalhostClustering(
                gatewayPorts: [gateway],
                serviceId: "elsa-evidence",
                clusterId: clusterId))
            .Build();
        try
        {
            await host.StartAsync();
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private static string Decode(string encoded) => Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
}
