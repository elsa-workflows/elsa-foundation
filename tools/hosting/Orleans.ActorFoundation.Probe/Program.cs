using System.Net;
using System.Net.Sockets;
using Kernel.Foundation.Contracts;
using Elsa.Orleans.ActorFoundation.Probe;

using Elsa.ActorFoundation.Hosting;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orleans;
using Orleans.Configuration;
using Testcontainers.PostgreSql;

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = timeout.Token;
await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
    .WithDatabase("orleans_actor_probe")
    .WithUsername("probe")
    .WithPassword(Guid.NewGuid().ToString("N"))
    .Build();
await postgres.StartAsync(token);
await using var database = NpgsqlDataSource.Create(postgres.GetConnectionString());
var store = new ProbeStore(database);
await store.InstallAsync(token);

const string workflowId = "orleans-durable-delay";
const string operation = "start";
var workflow = Wf.Workflow(workflowId, Wf.Sequence("sequence",
    Wf.Delay("delay", Binding.Literal(12_000)),
    Wf.WriteLine("after-delay", Binding.Literal("resumed by the other silo"))));
var fault = new ProbeFault();
var ports = AllocatePorts();
var scope = EmbeddedTenant.Bind("probe");
var actor = OrleansFoundation.Reference(scope, operation);
var start = NativeExecutionModel.Start(workflowId);

using var first = CreateSilo("first", ports[0], ports[1]);
await first.StartAsync(token);
var runtime = first.Services.GetRequiredService<WorkflowRuntime>();
var firstFoundation = first.Services.GetRequiredService<OrleansFoundation>();

fault.Arm("before commit");
await ExpectInterruptionAsync(runtime, "before commit");
Require(await store.LoadAsync(actor, scope.Tenant, token) is null, "An interrupted turn exposed a checkpoint or timer.");

fault.Arm("after commit");
await ExpectInterruptionAsync(runtime, "after commit");
var waiting = await store.LoadAsync(actor, scope.Tenant, token);
Require(waiting is { Revision: 1, Header.Status: ActorStatus.Running, Timer: not null, Silo: "first" },
    "The first silo did not commit workflow state with its timer.");
var timer = waiting!.Timer!;
var replay = await runtime.StartAsync(scope, operation, start, cancellationToken: token);
Require(replay is { Outcome: AdmissionOutcome.Duplicate } && replay.Actor == actor,
    "Retry after the committed response was lost changed the actor identity.");
Require(!await firstFoundation.DeliverTimerAsync(scope, actor, timer.Id, token), "The timer became eligible before its due time.");

using var second = CreateSilo("second", ports[2], ports[3]);
await second.StartAsync(token);
await first.StopAsync(token);

var secondFoundation = second.Services.GetRequiredService<OrleansFoundation>();
var read = await secondFoundation.GetAsync(scope, actor, token);
Require(read?.Header.Ref == actor && read.Header.Status == ActorStatus.Running, "The successor could not read the original running actor.");
while (await secondFoundation.DispatchDueTimersAsync(token) == 0)
    await Task.Delay(100, token);

var completed = await store.LoadAsync(actor, scope.Tenant, token);
Require(completed is { Revision: 2, Header.Status: ActorStatus.Finished, Timer: null, Silo: "second" },
    "The successor did not commit the terminal checkpoint and consume its timer together.");
Require(completed!.Header.EndedAt >= timer.Due, "The successor delivered a timer early.");
Require(!await secondFoundation.DeliverTimerAsync(scope, actor, timer.Id, token), "The successor delivered the same timer twice.");
Require(await secondFoundation.DispatchDueTimersAsync(token) == 0, "The completed timer is still scheduled.");
var history = await secondFoundation.TailAsync(scope, actor, cancellationToken: token);
Require(history.Items.Count(entry => entry.Entry.StartsWith("\"c:", StringComparison.Ordinal) &&
                                    entry.Entry.EndsWith(":after-delay\"", StringComparison.Ordinal)) == 1,
    "Expected one completion of the final activity. Journal: " + string.Join(", ", history.Items.Select(entry => entry.Entry)));
Require(history.Items.Count(entry => entry.Entry == "\"end\"") == 1, "The workflow did not commit exactly one terminal event.");
await second.StopAsync(token);
Console.WriteLine("Two Orleans silos passed the Native Delay workflow, atomic checkpoint and timer, start replay, and timer replay.");

IHost CreateSilo(string name, int siloPort, int gatewayPort) => Host.CreateDefaultBuilder()
    .ConfigureLogging(logging => logging.ClearProviders())
    .UseOrleans(builder => builder
        .Configure<SiloOptions>(options => options.SiloName = name)
        .Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "actor-foundation-probe";
            options.ServiceId = "actor-foundation-probe";
        })
        .Configure<EndpointOptions>(options =>
        {
            options.AdvertisedIPAddress = IPAddress.Loopback;
            options.SiloPort = siloPort;
            options.GatewayPort = gatewayPort;
        })
        .UseAdoNetClustering(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = postgres.GetConnectionString();
        }))
    .ConfigureServices(services =>
    {
        services.AddSingleton(store);
        services.AddSingleton(fault);
        services.AddSingleton(new ProbeSilo(name));
        services.AddSingleton<IActorType>(new WorkflowActor([new NativeExecutionModel(new WorkflowRegistry().Add(workflow),
            new NativeExecutionOptions { SelfSends = true, ConsoleOutput = TextWriter.Null })]));
        services.AddSingleton<OrleansFoundation>();
        services.AddSingleton<IIngress>(provider => provider.GetRequiredService<OrleansFoundation>());
        services.AddSingleton<IActorReads>(provider => provider.GetRequiredService<OrleansFoundation>());
        services.AddSingleton<WorkflowRuntime>();
    })
    .Build();

async Task ExpectInterruptionAsync(WorkflowRuntime selected, string stage)
{
    try
    {
        await selected.StartAsync(scope, operation, start, cancellationToken: token);
    }
    catch (InvalidOperationException exception) when (exception.Message == "Probe interruption " + stage)
    {
        return;
    }
    throw new InvalidOperationException("The expected interruption did not happen: " + stage);
}

static void Require(bool condition, string detail)
{
    if (!condition) throw new InvalidOperationException(detail);
}

static int[] AllocatePorts()
{
    var listeners = new List<TcpListener>();
    try
    {
        while (listeners.Count < 4)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            listeners.Add(listener);
        }
        return listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToArray();
    }
    finally
    {
        foreach (var listener in listeners) listener.Stop();
    }
}
