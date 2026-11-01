using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.TimerPerformanceProbe;
using CShells.Lifecycle;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Tests;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Seq = Elsa.Activities.Sequence.Activities.Sequence;

var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(args[0]))!;
if (config.Count is < 1 or > 100 || config.Degree is < 1 or > 8 || config.Mode is not ("Durable" or "Balanced" or "Ephemeral") || config.Provider is not ("Sqlite" or "PostgreSql") || !Path.IsPathFullyQualified(config.Output) || Directory.Exists(config.Output)) throw new ArgumentException("Use a bounded fresh qualification case.");
Directory.CreateDirectory(config.Output);
var database = "timer_perf_" + Guid.NewGuid().ToString("N");
var connection = "Data Source=" + Path.Combine(config.Output, "runtime.db") + ";Pooling=False";
if (config.Provider == "PostgreSql")
{
    var supplied = Environment.GetEnvironmentVariable("ELSA_EFFICIENCY_POSTGRES_CONNECTION") ?? throw new InvalidOperationException("Protected fixture connection is absent.");
    var admin = new NpgsqlConnectionStringBuilder(supplied) { Database = "postgres" };
    await using var db = new NpgsqlConnection(admin.ConnectionString); await db.OpenAsync();
    await using var create = new NpgsqlCommand("CREATE DATABASE " + database, db); await create.ExecuteNonQueryAsync();
    connection = new NpgsqlConnectionStringBuilder(supplied) { Database = database, MaxPoolSize = 100 }.ConnectionString;
}
var clock = new MutableClock(DateTimeOffset.UtcNow);
var productionIds = new ServiceCollection().AddWorkflowRuntime().Last(d => d.ServiceType == typeof(IRuntimeExecutionIdGenerator));
await using var host = WorkflowExecutionHarness.Create().ConfigureServices(services =>
{
    new SerializationFeature().ConfigureServices(services); new ActivitiesPrimitivesFeature().ConfigureServices(services); new ActivitiesSequenceFeature().ConfigureServices(services);
    new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);
    services.Replace(productionIds); services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
    services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions { Provider = config.Provider, ConnectionString = connection,
        RecoveryContinuationSigningKey = "private-timer-profile-recovery-key", HierarchyCursorSigningKey = "private-timer-profile-hierarchy-key" }).AddEfModuleMigrations<RuntimeDbContext>(config.Provider);
    services.AddPersistenceModes(o => o.Mode = Enum.Parse<PersistenceMode>(config.Mode));
}).Build();
foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
var delay = DelayTestExecutable.Create(TimeSpan.FromDays(1), clock.GetUtcNow());
ExecutableNode Noop(int i) => new("after-" + i, "authored-after-" + i, typeof(ProbeLeaf).FullName!, "1.0.0", "test", JsonSerializer.SerializeToElement(new {}), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
var children = new[] { delay.RootActivity }.Concat(Enumerable.Range(0, 10).Select(Noop)).ToArray();
var root = new ExecutableNode("sequence", "authored-sequence", typeof(Seq).FullName!, "1.0.0", "test", JsonSerializer.SerializeToElement(new {}), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
    childSlots: [new ExecutableChildSlot(Seq.ActivitiesSlotName, children)], structure: new ExecutableActivityStructure(Seq.StructureKind, Seq.StructureSchemaVersion, JsonSerializer.SerializeToElement(new { activities = children.Select(c => c.ExecutableNodeId).ToArray() })));
var executable = new WorkflowExecutable(delay.Identity, root, delay.ResumeTargets, delay.CreatedAt, delay.CompatibilityMetadata, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
var source = await host.PublishAsync(executable, "timer-profile-source");
var partition = new WorkflowExecutionPartition(PersistenceScope.DefaultValue); var ids = host.Services.GetRequiredService<IRuntimeExecutionIdGenerator>();
using var logger = LoggerFactory.Create(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
DurableTimerPumpTask Pump(int degree) => new(host.Services.GetRequiredService<IPersistenceScopeRunner>(), Options.Create(new DurableTimerPumpOptions { MaxConcurrentDispatches = degree, MaxTimersPerTick = 100 }), clock, logger.CreateLogger<DurableTimerPumpTask>());
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
await host.StartPublishedAsync(source, ids.NewWorkflowExecutionId(), partition: partition); clock.Advance(TimeSpan.FromDays(2)); await Pump(1).ExecuteAsync(deadline.Token);
if (ProbeLeaf.Count != 10) throw new InvalidOperationException("Warm timer did not resume through the real dispatcher.");
await using (var operation = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(PersistenceScope.DefaultValue)))
{
    var db = operation.ServiceProvider.GetRequiredService<RuntimeDbContext>();
    if (await db.WorkflowExecutionStates.CountAsync() != 1 || await db.WorkflowExecutionStates.CountAsync(x => x.Status == (int)WorkflowExecutionStatus.Completed) != 1 ||
        await db.ActivityExecutionStates.CountAsync() != 12 || await db.DurableTimers.AnyAsync() || await db.Bookmarks.AnyAsync() || await db.SchedulerWorkItems.AnyAsync() ||
        await db.IncidentStates.AnyAsync() || await db.RuntimePostCommitOutbox.AnyAsync(x => x.Status != (int)RuntimePostCommitOutboxStatus.Delivered))
        throw new InvalidOperationException("The warmup must fully settle before qualifying another population.");
}

var executions = new List<string>();
for (var i = 0; i < config.Count; i++) { var id = ids.NewWorkflowExecutionId(); executions.Add(id); await host.StartPublishedAsync(source, id, partition: partition); }
var encoded = executions.Select(EfRelationalIdentity.Encode).ToArray();
async Task<Counts> ReadCounts()
{
    await using var operation = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(PersistenceScope.DefaultValue));
    var db = operation.ServiceProvider.GetRequiredService<RuntimeDbContext>();
    return new(await db.WorkflowExecutionStates.CountAsync(x => encoded.Contains(x.WorkflowExecutionId) && x.Status == (int)WorkflowExecutionStatus.Completed),
        await db.DurableTimers.CountAsync(x => encoded.Contains(x.WorkflowExecutionId)), await db.Bookmarks.CountAsync(x => executions.Contains(x.WorkflowExecutionId)),
        await db.ActivityExecutionStates.CountAsync(x => encoded.Contains(x.WorkflowExecutionId)), await db.SchedulerWorkItems.CountAsync(x => encoded.Contains(x.WorkflowExecutionId)),
        await db.IncidentStates.CountAsync(x => encoded.Contains(x.WorkflowExecutionId)), await db.RuntimePostCommitOutbox.CountAsync(x => encoded.Contains(x.WorkflowExecutionId) && x.Status != (int)RuntimePostCommitOutboxStatus.Delivered));
}
async Task<string> Snapshot()
{
    await using var operation = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(PersistenceScope.DefaultValue));
    var db = operation.ServiceProvider.GetRequiredService<RuntimeDbContext>();
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { workflows = await db.WorkflowExecutionStates.AsNoTracking().Where(x => encoded.Contains(x.WorkflowExecutionId)).OrderBy(x => x.Id).ToArrayAsync(), activities = await db.ActivityExecutionStates.AsNoTracking().Where(x => encoded.Contains(x.WorkflowExecutionId)).OrderBy(x => x.Id).ToArrayAsync() }))));
}
var before = await ReadCounts();
if (before.Completed != 0 || before.Timers != config.Count || before.Bookmarks != config.Count || ProbeLeaf.Count != 10) throw new InvalidOperationException("Timer setup is not the expected durable suspended population: " + JsonSerializer.Serialize(before) + "; bodies=" + ProbeLeaf.Count);
ProbeLeaf.Count = 0; clock.Advance(TimeSpan.FromDays(2));
using var process = Process.GetCurrentProcess(); var cpuBefore = process.TotalProcessorTime; var allocationBefore = GC.GetTotalAllocatedBytes(true);
var startedAt = DateTimeOffset.UtcNow; var elapsed = Stopwatch.StartNew(); await Pump(config.Degree).ExecuteAsync(deadline.Token); elapsed.Stop(); var endedAt = DateTimeOffset.UtcNow;
var cpu = (process.TotalProcessorTime - cpuBefore).TotalSeconds; var allocated = GC.GetTotalAllocatedBytes(true) - allocationBefore; var peak = process.PeakWorkingSet64;
var after = await ReadCounts(); var snapshot = await Snapshot(); await Pump(config.Degree).ExecuteAsync(deadline.Token);
var unchanged = snapshot == await Snapshot();
var correct = after.Completed == config.Count && after.Timers == 0 && after.Bookmarks == 0 && after.Activities == config.Count * 12 && after.Queue == 0 && after.Incidents == 0 && after.PendingOutbox == 0 && ProbeLeaf.Count == config.Count * 10 && unchanged;
var result = new { config, database, Runtime = RuntimeInformation.FrameworkDescription, ProcessorCount = Environment.ProcessorCount, Shape = "Sequence(Delay, ten ReplaySafe no-op leaves)",
    startedAt, endedAt, Seconds = elapsed.Elapsed.TotalSeconds, ResumesPerSecond = config.Count / elapsed.Elapsed.TotalSeconds, CpuSeconds = cpu, AllocatedBytesPerResume = allocated / (double)config.Count,
    PeakWorkingSet = peak, Correct = correct, Before = before, After = after, BodyCalls = ProbeLeaf.Count, EmptySecondSweepKeptWorkflowAndActivityBytes = unchanged,
    Boundary = "One warmed actual due-timer sweep through ordinary scoped provider/dispatcher/mailbox to completion; setup, validation and second sweep excluded. Clock advanced only to make timers due; no deadline-latency claim." };
await File.WriteAllTextAsync(Path.Combine(config.Output, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { result.Correct, result.ResumesPerSecond, result.BodyCalls })); if (!correct) Environment.ExitCode = 2;
public record Config(string Output, string Provider = "PostgreSql", string Mode = "Balanced", int Count = 64, int Degree = 1);
public record Counts(int Completed, int Timers, int Bookmarks, int Activities, int Queue, int Incidents, int PendingOutbox);
public sealed class MutableClock(DateTimeOffset initial) : TimeProvider
{
    private long ticks = initial.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan span) => Interlocked.Add(ref ticks, span.Ticks);
}
namespace Elsa.TimerPerformanceProbe
{
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class ProbeLeaf : Activity<ActivityUnit>
{
    public static long Count;
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    { Interlocked.Increment(ref Count); return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done)); }
}

}
