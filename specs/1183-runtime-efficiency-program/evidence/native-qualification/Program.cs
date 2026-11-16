using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using CShells.Lifecycle;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.PerformanceProbe;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Seq = Elsa.Activities.Sequence.Activities.Sequence;

var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(args[0]))!;
if (config.Activities is < 1 or > 100 || config.Concurrency is < 1 or > 64 || config.Seconds is < 1 or > 120 || config.Minimum is < 0 or > 10000 || config.WarmupSeconds is < 0 or > 30 || config.WarmupMinimum is < 0 or > 100)
    throw new ArgumentOutOfRangeException(nameof(config), "Qualification must remain within the authorized finite bounds.");
if (!Regex.IsMatch(config.Label, "^[A-Za-z0-9_-]{1,128}$")) throw new ArgumentException("Use a finite case label without path syntax.");
if (File.Exists(Path.Combine(config.Output, config.Label + ".json"))) throw new InvalidOperationException("A qualification result already exists; retain it and choose a fresh label.");
var runId = "perf_" + Guid.NewGuid().ToString("N");
var database = config.Database ?? runId;
var serverConnection = Environment.GetEnvironmentVariable("ELSA_EFFICIENCY_POSTGRES_CONNECTION");
string PostgresConnection(string name)
{
    if (string.IsNullOrWhiteSpace(serverConnection)) throw new InvalidOperationException("The protected PostgreSQL fixture connection is absent.");
    return new NpgsqlConnectionStringBuilder(serverConnection) { Database = name, MaxPoolSize = 100 }.ConnectionString;
}
var connection = config.Provider == "Sqlite" ? $"Data Source={Path.Combine(config.Output, database + ".db")};Pooling=True" :
    config.Provider == "Memory" ? "" : PostgresConnection(database);
Directory.CreateDirectory(config.Output);
if (config.Provider == "PostgreSql" && config.Database is null)
{
    await using var admin = new NpgsqlConnection(PostgresConnection("postgres"));
    await admin.OpenAsync();
    await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
    await create.ExecuteNonQueryAsync();
}
var productionIds = new ServiceCollection().AddWorkflowRuntime().Last(d => d.ServiceType == typeof(IRuntimeExecutionIdGenerator));
var commands = new SqlCounter();
var transactions = new TransactionCounter();
var mode = config.Mode == "EphemeralNoRecord" ? PersistenceMode.Ephemeral : Enum.Parse<PersistenceMode>(config.Mode);
await using var host = WorkflowExecutionHarness.Create().ConfigureServices(services =>
{
    new ActivitiesSequenceFeature().ConfigureServices(services);
    new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
    services.Replace(productionIds);
    if (config.Provider != "Memory")
    {
        services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
        {
            Provider = config.Provider, ConnectionString = connection, Pooling = config.ContextPooling,
            RecoveryContinuationSigningKey = "private-performance-recovery-signing-key-20261031",
            HierarchyCursorSigningKey = "private-performance-hierarchy-signing-key-20261031"
        }).AddEfModuleMigrations<RuntimeDbContext>(config.Provider);
        if (config.Provider == "Sqlite") services.ConfigureDbContext<RuntimeSqliteDbContext>(b => b.AddInterceptors(commands, transactions));
        else services.ConfigureDbContext<RuntimePostgreSqlDbContext>(b => b.AddInterceptors(commands, transactions));
    }
    services.AddPersistenceModes(o => { o.Mode = mode; o.MaxActivitiesPerCommit = config.Cap; o.RecordCompletion = config.Mode != "EphemeralNoRecord"; });
}).Build();
if (!config.SkipInitialize)
    foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();

ExecutableNode Node(string id, Type type, IReadOnlyCollection<ExecutableChildSlot>? slots = null, ExecutableActivityStructure? structure = null) =>
    new(id, "authored-" + id, type.FullName!, "1.0.0", "test", JsonSerializer.SerializeToElement(new {}),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots: slots, structure: structure);
var leafType = config.External ? typeof(ExternalNoop) : typeof(ReplaySafeNoop);
var leaves = Enumerable.Range(0, config.Activities).Select(i => Node("leaf" + i, leafType)).ToArray();
var root = config.Activities == 1 ? leaves[0] : Node("sequence", typeof(Seq),
    [new ExecutableChildSlot(Seq.ActivitiesSlotName, leaves)],
    new ExecutableActivityStructure(Seq.StructureKind, Seq.StructureSchemaVersion,
        JsonSerializer.SerializeToElement(new { activities = leaves.Select(n => n.ExecutableNodeId).ToArray() })));
var artifactKeyName = config.ArtifactKey ?? runId;
var identity = new WorkflowExecutableIdentity("artifact-" + artifactKeyName, "definition-" + artifactKeyName, "version-" + artifactKeyName, "1.0.0", "sha256:" + artifactKeyName);
var reference = await host.PublishAsync(WorkflowExecutionHarness.NewExecutable(root, identity), "source-" + artifactKeyName);
var partition = new WorkflowExecutionPartition(PersistenceScope.DefaultValue);
var ids = new ConcurrentBag<string>();
var idGenerator = host.Services.GetRequiredService<IRuntimeExecutionIdGenerator>();
async Task Execute()
{
    var id = idGenerator.NewWorkflowExecutionId();
    await host.StartPublishedAsync(reference, id, partition: partition);
    ids.Add(id);
}

// Separate cold request, warm-up, accounting probe, and timed sample windows.
var cold = Stopwatch.StartNew(); await Execute(); cold.Stop();
if (LeafCounter.Count != config.Activities) throw new InvalidOperationException("Cold run did not execute the expected leaf bodies.");
var warm = Stopwatch.StartNew(); int warmCount = 0;
while (warmCount < config.WarmupMinimum || warm.Elapsed.TotalSeconds < config.WarmupSeconds) { await Execute(); warmCount++; }
commands.Enabled = true; transactions.Enabled = true;
await Execute();
commands.Enabled = false; transactions.Enabled = false;
var accounting = new { commands.Count, commands.DurationMs, transactions.Commits, transactions.Starts, Sql = commands.Sql.OrderByDescending(p => p.Value).ToArray() };
if (config.StartGate is not null)
{
    await File.WriteAllTextAsync(config.StartGate + "." + config.Label + ".ready", runId);
    var gateWait = Stopwatch.StartNew();
    while (!File.Exists(config.StartGate))
    {
        if (gateWait.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("The finite qualification start gate was not opened.");
        await Task.Delay(25);
    }
}
if (config.StartAtUtc is { } startAt)
{
    var wait = startAt - DateTimeOffset.UtcNow;
    if (wait < TimeSpan.Zero || wait > TimeSpan.FromMinutes(5)) throw new InvalidOperationException("The coordinated sample start is absent or outside its finite window.");
    await Task.Delay(wait);
}
long beforeLeaves = LeafCounter.Count;
var beforeAlloc = GC.GetTotalAllocatedBytes(true);
var beforeGc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
using var process = Process.GetCurrentProcess();
var beforeCpu = process.TotalProcessorTime;
var samples = new ConcurrentBag<double>();
var errors = new ConcurrentBag<string>();
var measuredStartedAt = DateTimeOffset.UtcNow;
var timer = Stopwatch.StartNew();
await Task.WhenAll(Enumerable.Range(0, config.Concurrency).Select(_ => Task.Run(async () =>
{
    while (timer.Elapsed.TotalSeconds < config.Seconds || samples.Count < config.Minimum)
    {
        var start = Stopwatch.GetTimestamp();
        try { await Execute(); samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
        catch (Exception ex) { Console.Error.WriteLine(ex); errors.Add(ex.GetType().FullName!); break; }
    }
})));
timer.Stop();
var measuredEndedAt = DateTimeOffset.UtcNow;
var cpu = (process.TotalProcessorTime - beforeCpu).TotalSeconds;
var allocated = GC.GetTotalAllocatedBytes(true) - beforeAlloc;
var collections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - beforeGc[i]).ToArray();
var measuredLeaves = LeafCounter.Count - beforeLeaves;
var peakWorkingSetAtSampleEnd = process.PeakWorkingSet64;
if (config.ValidationGate is { } validationGate)
{
    await File.WriteAllTextAsync(validationGate + "." + config.Label + ".measured", JsonSerializer.Serialize(new { measuredStartedAt, measuredEndedAt, CompletedSamples = samples.Count, Errors = errors.Count }));
    var gateWait = Stopwatch.StartNew();
    while (!File.Exists(validationGate))
    {
        if (gateWait.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("The coordinator did not release finite validation.");
        await Task.Delay(25);
    }
}
var all = samples.Order().ToArray();
double Percentile(double p) => all.Length == 0 ? 0 : all[Math.Clamp((int)Math.Ceiling(p * all.Length) - 1, 0, all.Length - 1)];
int completed = 0, states = 0, queued = 0, incidents = 0, activityRows = 0, memoryExecutionsWithUndeliveredOutbox = 0;
int? pendingOutbox = null;
if (config.Provider != "Memory")
{
    await using var scope = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(PersistenceScope.DefaultValue));
    var db = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
    var ownedIds = ids.Select(EfRelationalIdentity.Encode).ToArray();
    states = await db.WorkflowExecutionStates.CountAsync(r => ownedIds.Contains(r.WorkflowExecutionId));
    completed = await db.WorkflowExecutionStates.CountAsync(r => ownedIds.Contains(r.WorkflowExecutionId) && r.Status == (int)WorkflowExecutionStatus.Completed);
    queued = await db.SchedulerWorkItems.CountAsync(r => ownedIds.Contains(r.WorkflowExecutionId));
    incidents = await db.IncidentStates.CountAsync(r => ownedIds.Contains(r.WorkflowExecutionId));
    pendingOutbox = await db.RuntimePostCommitOutbox.CountAsync(r => ownedIds.Contains(r.WorkflowExecutionId) && r.Status != (int)RuntimePostCommitOutboxStatus.Delivered);
    activityRows = await db.ActivityExecutionStates.CountAsync(r => ownedIds.Contains(r.WorkflowExecutionId));
}
else
{
    await using var scope = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(PersistenceScope.DefaultValue));
    var stores = scope.ServiceProvider;
    foreach (var id in ids)
    {
        var state = await stores.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(id);
        if (state is not null) { states++; if (state.Status == WorkflowExecutionStatus.Completed) completed++; }
        queued += (await stores.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(id)).Count;
        incidents += await stores.GetRequiredService<IIncidentStateStore>().CountAsync(id);
        activityRows += checked((int)await stores.GetRequiredService<IActivityExecutionStateStore>().CountAsync(id));
        if (await stores.GetRequiredService<IPostCommitOutboxLookupStore>().FindUndeliveredDurableAsync(id, []) is not null)
            memoryExecutionsWithUndeliveredOutbox++;
    }
}
var expectedRecord = config.Mode != "EphemeralNoRecord" || config.External;
var distinctIds = ids.Distinct(StringComparer.Ordinal).Count();
var correct = distinctIds == ids.Count && errors.IsEmpty && measuredLeaves == all.Length * (long)config.Activities && queued == 0 && incidents == 0 && (pendingOutbox ?? memoryExecutionsWithUndeliveredOutbox) == 0 &&
    (expectedRecord ? completed == ids.Count && states == ids.Count : states == 0) &&
    (activityRows == (expectedRecord ? ids.Count * (config.Activities + (config.Activities > 1 ? 1 : 0)) : 0));
var result = new
{
    Config = config, runId, database, Timestamp = DateTimeOffset.UtcNow, measuredStartedAt, measuredEndedAt,
    Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
    ProcessorCount = Environment.ProcessorCount, ServerGC = GCSettings.IsServerGC,
    ColdMs = cold.Elapsed.TotalMilliseconds, WarmupCount = warmCount, WarmupSeconds = warm.Elapsed.TotalSeconds,
    Count = all.Length, Seconds = timer.Elapsed.TotalSeconds, WorkflowsPerSecond = all.Length / timer.Elapsed.TotalSeconds,
    LeafActivitiesPerSecond = measuredLeaves / timer.Elapsed.TotalSeconds,
    MeanMs = all.Length == 0 ? 0 : all.Average(), P50Ms = Percentile(.5), P95Ms = Percentile(.95), P99Ms = Percentile(.99), MaxMs = all.LastOrDefault(),
    AllocatedBytesPerWorkflow = all.Length == 0 ? 0 : allocated / (double)all.Length, CpuSeconds = cpu,
    CpuCoreEquivalent = cpu / timer.Elapsed.TotalSeconds, GcCollections = collections, PeakWorkingSet = peakWorkingSetAtSampleEnd,
    Accounting = accounting, Correct = correct, Completed = completed, States = states, ActivityRows = activityRows,
    Queued = queued, Incidents = incidents, PendingOutbox = pendingOutbox, MemoryExecutionsWithUndeliveredOutbox = memoryExecutionsWithUndeliveredOutbox, TotalRuns = ids.Count, DistinctRunIds = distinctIds, measuredLeaves,
    Errors = errors.ToArray(), SamplesMs = all
};
var path = Path.Combine(config.Output, config.Label + ".json");
await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { config.Label, result.Count, result.WorkflowsPerSecond, result.P95Ms, result.AllocatedBytesPerWorkflow, result.Correct, SqlCommands = commands.Count, transactions.Commits, Errors = errors.Count }));
if (!correct) Environment.ExitCode = 2;

public record Config(string Label, string Provider, string Mode, int Activities, int Concurrency = 1, double Seconds = 15, int Minimum = 100,
    bool External = false, int Cap = 50, bool ContextPooling = false, double WarmupSeconds = 3, int WarmupMinimum = 10,
    string Output = "./results", string? Database = null, bool SkipInitialize = false, string? StartGate = null, string? ArtifactKey = null, DateTimeOffset? StartAtUtc = null, string? ValidationGate = null);
public static class LeafCounter { public static long Count; }
namespace Elsa.PerformanceProbe {
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class ReplaySafeNoop : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext c)
    { Interlocked.Increment(ref LeafCounter.Count); return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done)); }
}
[ActivitySideEffectProfile(SideEffectProfile.External)]
public sealed class ExternalNoop : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext c)
    { Interlocked.Increment(ref LeafCounter.Count); return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done)); }
}
}
public sealed class SqlCounter : DbCommandInterceptor
{
    public bool Enabled;
    public long Count;
    public double DurationMs;
    public ConcurrentDictionary<string, long> Sql = new();
    private void Record(DbCommand command, CommandExecutedEventData data)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref Count);
        lock (Sql) DurationMs += data.Duration.TotalMilliseconds;
        Sql.AddOrUpdate(Regex.Replace(command.CommandText, @"\s+", " ").Trim(), 1, (_, n) => n + 1);
    }
    public override DbDataReader ReaderExecuted(DbCommand c, CommandExecutedEventData d, DbDataReader r) { Record(c,d); return r; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand c, CommandExecutedEventData d, DbDataReader r, CancellationToken t = default) { Record(c,d); return ValueTask.FromResult(r); }
    public override int NonQueryExecuted(DbCommand c, CommandExecutedEventData d, int r) { Record(c,d); return r; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand c, CommandExecutedEventData d, int r, CancellationToken t = default) { Record(c,d); return ValueTask.FromResult(r); }
    public override object? ScalarExecuted(DbCommand c, CommandExecutedEventData d, object? r) { Record(c,d); return r; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand c, CommandExecutedEventData d, object? r, CancellationToken t = default) { Record(c,d); return ValueTask.FromResult(r); }
}
public sealed class TransactionCounter : DbTransactionInterceptor
{
    public bool Enabled; public long Commits; public long Starts;
    public override DbTransaction TransactionStarted(DbConnection c, TransactionEndEventData d, DbTransaction r) { if (Enabled) Interlocked.Increment(ref Starts); return r; }
    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection c, TransactionEndEventData d, DbTransaction r, CancellationToken t=default) { if (Enabled) Interlocked.Increment(ref Starts); return ValueTask.FromResult(r); }
    public override void TransactionCommitted(DbTransaction t, TransactionEndEventData d) { if (Enabled) Interlocked.Increment(ref Commits); }
    public override Task TransactionCommittedAsync(DbTransaction t, TransactionEndEventData d, CancellationToken c=default) { if (Enabled) Interlocked.Increment(ref Commits); return Task.CompletedTask; }
}
