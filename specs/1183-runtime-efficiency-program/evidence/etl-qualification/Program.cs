using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(args[0]))!;
if (config.Rows is < 1 or > 100_000 || config.Attempts is < 1 or > 8 || config.Partitions is < 1 or > 16 || config.Batch is < 1 or > 1000 || config.PayloadCharacters is < 1 or > 1024)
    throw new ArgumentOutOfRangeException(nameof(config));
if (!Path.IsPathFullyQualified(config.Output) || Directory.Exists(config.Output)) throw new IOException("Use a fresh absolute task-owned output directory.");
Directory.CreateDirectory(config.Output);
var source = Path.Combine(config.Output, "source.csv");
var lookup = Path.Combine(config.Output, "lookup.csv");
string Name(int i) => $"record-{i:D6}-" + new string('x', config.PayloadCharacters);
await using (var writer = new StreamWriter(source, false, new UTF8Encoding(false)))
{
    await writer.WriteLineAsync("id,name");
    for (var i = 0; i < config.Rows; i++) await writer.WriteLineAsync("1," + Name(i));
}
await File.WriteAllTextAsync(lookup, "id,category\n1,one\n", new UTF8Encoding(false));
var scope = new EtlScope(null, "runtime-efficiency");
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
EtlHostFileBindingOptions Binding(string alias, string role, string path) => new()
{
    PersistencePartition = scope.PersistencePartition, Alias = alias, Role = role, Revision = "v1", Path = path,
    IsCurrent = true, ImmutableInput = role is "Source" or "Lookup", AtomicPublication = role is "MainOutput" or "ErrorOutput",
    ExpectedSha256 = role is "Source" or "Lookup" ? Hash(path) : null
};
var pipeline = new EtlPipeline(new("source", "lookup", "output", "errors"),
    [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, false)],
    [new("id", EtlColumnType.Int64, true), new("category", EtlColumnType.String, false)], "id", "id",
    [new("name", EtlFieldOrigin.Source, "name"), new("category", EtlFieldOrigin.Lookup, "category")], [],
    new EtlLimits { PartitionCount = config.Partitions, MaxBatchRecords = config.Batch });
var designServices = new ServiceCollection();
new EtlDesignFeature().ConfigureServices(designServices);
using var design = designServices.BuildServiceProvider();
using var designScope = design.CreateScope();
var artifact = designScope.ServiceProvider.GetRequiredService<IEtlArtifactCompiler>().Compile("efficiency-profile", 1, pipeline).Artifact
    ?? throw new InvalidOperationException("Qualification pipeline compilation failed.");
var hash = new string('a', 64);
var address = new EtlNativeResultAddress(1, "qualification-parent", "qualification-activity", "qualification-executable", hash,
    "qualification-node", "qualification-bookmark", "qualification-registration", "qualification-target", "etl-completion", hash,
    "etl-result", 1, "once", EtlNativeResultAddress.CompletionProviderId);
var invocation = new EtlInvocation(scope, "qualification-run", artifact, address);
using var sql = new SqlObserver();
using var listener = DiagnosticListener.AllListeners.Subscribe(sql);
var builder = Host.CreateApplicationBuilder([]);
builder.Logging.ClearProviders();
new EtlRuntimeFeature
{
    SnapshotRoot = Path.Combine(config.Output, "snapshots"), MaxConcurrentAttempts = config.Attempts,
    FileBindings = [Binding("source", "Source", source), Binding("lookup", "Lookup", lookup),
        Binding("output", "MainOutput", Path.Combine(config.Output, "output")), Binding("errors", "ErrorOutput", Path.Combine(config.Output, "errors"))]
}.ConfigureServices(builder.Services);
var database = Path.Combine(config.Output, "runtime.db");
new EtlRuntimeEntityFrameworkCoreFeature { ConnectionString = $"Data Source={database};Pooling=False" }.ConfigureServices(builder.Services);
builder.Services.Configure<EfMigrateOptions>(o => o.Policy = EfMigratePolicy.AutoMigrate);
using var host = builder.Build();
var startup = Stopwatch.StartNew(); await host.StartAsync(); startup.Stop();
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
using var process = Process.GetCurrentProcess();
var beforeCpu = process.TotalProcessorTime; var beforeAllocation = GC.GetTotalAllocatedBytes(true);
var beforeGc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
var startedAt = DateTimeOffset.UtcNow; var elapsed = Stopwatch.StartNew(); sql.Enabled = config.CollectCommands;
EtlStoreStatus admission;
await using (var operation = host.Services.CreateAsyncScope())
    admission = (await operation.ServiceProvider.GetRequiredService<IEtlRunAdmissionService>().AdmitAsync(invocation, deadline.Token)).Status;
if (admission != EtlStoreStatus.Applied) throw new InvalidOperationException("Fresh qualification admission was not applied.");
EtlRunView? run;
do
{
    await using var operation = host.Services.CreateAsyncScope();
    run = await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().FindRunAsync(scope, invocation.RunId, deadline.Token);
    if (run?.TerminalResult is not null) break;
    await Task.Delay(100, deadline.Token);
} while (true);
elapsed.Stop(); var endedAt = DateTimeOffset.UtcNow; sql.Enabled = false;
var cpu = (process.TotalProcessorTime - beforeCpu).TotalSeconds;
var allocated = GC.GetTotalAllocatedBytes(true) - beforeAllocation;
var collections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - beforeGc[i]).ToArray();
var peak = process.PeakWorkingSet64;
await host.StopAsync(deadline.Token);
var manifest = new List<EtlManifestEntry>(); var partitions = new List<EtlPartitionView>();
await using (var operation = host.Services.CreateAsyncScope())
{
    var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
    EtlManifestPosition? after = null;
    do
    {
        var page = await store.ListManifestAsync(scope, invocation.RunId, after, 100, deadline.Token);
        manifest.AddRange(page); if (page.Count < 100) break; after = page[^1].Position;
    } while (manifest.Count < 100_000);
    partitions.AddRange(await store.ListPartitionsAsync(scope, invocation.RunId, -1, 100, deadline.Token));
}
var names = new HashSet<string>(StringComparer.Ordinal); var outputValid = true;
foreach (var entry in manifest)
{
    var path = Path.Combine(config.Output, entry.Effect.Route == EtlOutputRoute.Main ? "output" : "errors", entry.Effect.Chunk.Key);
    outputValid &= entry.Effect.Route == EtlOutputRoute.Main && new FileInfo(path).Length == entry.Effect.Chunk.ByteLength && Hash(path) == entry.Effect.Chunk.ContentHash;
    long count = 0;
    foreach (var line in File.ReadLines(path))
    {
        using var row = JsonDocument.Parse(line); count++;
        outputValid &= row.RootElement.GetProperty("category").GetString() == "one" && names.Add(row.RootElement.GetProperty("name").GetString()!);
    }
    outputValid &= count == entry.Effect.RecordCount;
}
for (var i = 0; i < config.Rows; i++) outputValid &= names.Contains(Name(i));
await using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False"); await connection.OpenAsync();
await using var owedCommand = connection.CreateCommand(); owedCommand.CommandText = "SELECT COUNT(*) FROM elsa_etl_runtime_outbox WHERE AcknowledgedAtUtcTicks IS NULL";
var owed = Convert.ToInt64(await owedCommand.ExecuteScalarAsync());
var correct = run!.Status == EtlRunStatus.Succeeded && run.AcceptedRecords == config.Rows && run.RejectedRecords == 0 && run.FilteredRecords == 0 &&
    run.OutstandingRecords == 0 && run.OutstandingBytes == 0 && names.Count == config.Rows && outputValid && owed == 1 &&
    partitions.Count == config.Partitions && partitions.All(p => p.NextOrdinal == p.EndOrdinalExclusive && p.Owner is null && p.OwedBatch is null);
var result = new { config, Model = "finite-file-etl", Provider = "Sqlite", Runtime = RuntimeInformation.FrameworkDescription, ProcessorCount = Environment.ProcessorCount,
    SourceBytes = new FileInfo(source).Length, LookupBytes = new FileInfo(lookup).Length, HostStartupMilliseconds = startup.Elapsed.TotalMilliseconds,
    startedAt, endedAt, Seconds = elapsed.Elapsed.TotalSeconds, RecordsPerSecond = config.Rows / elapsed.Elapsed.TotalSeconds,
    CpuSeconds = cpu, AllocatedBytesPerRecord = allocated / (double)config.Rows, GcCollections = collections, PeakWorkingSet = peak,
    Correct = correct, run.Status, run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords, run.OutstandingRecords, run.OutstandingBytes,
    ManifestEntries = manifest.Count, DistinctOutputNames = names.Count, OutputHashesAndRowsValid = outputValid, OwedNativeCallbacks = owed,
    CallbackBoundary = "Standalone ETL acceptance only. No native adapter is configured; its one durable callback remains owed. Native bridge is qualified separately.",
    Commands = config.CollectCommands ? (long?)sql.Commands : null, Transactions = config.CollectCommands ? (long?)sql.Transactions : null, Sql = sql.Sql.OrderByDescending(x => x.Value).ToArray(),
    TimingEligible = !config.CollectCommands, MeasurementBoundary = "Admission through durable terminal acceptance, including preparation, worker wakeup and 100ms status polling; setup, output validation and native callback excluded." };
await File.WriteAllTextAsync(Path.Combine(config.Output, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { result.Correct, result.AcceptedRecords, result.RecordsPerSecond, result.OwedNativeCallbacks }));
if (!correct) Environment.ExitCode = 2;
public record Config(string Output, int Rows = 100_000, int Attempts = 1, int Partitions = 8, int Batch = 1000, int PayloadCharacters = 128, bool CollectCommands = false);
internal sealed class SqlObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly ConcurrentBag<IDisposable> subscriptions = [];
    public volatile bool Enabled;
    public long Commands; public long Transactions;
    public readonly ConcurrentDictionary<string, long> Sql = new();
    public void OnNext(DiagnosticListener listener) { if (listener.Name == "Microsoft.EntityFrameworkCore") subscriptions.Add(listener.Subscribe(this)); }
    public void OnNext(KeyValuePair<string, object?> item)
    {
        if (!Enabled) return;
        if (item.Value is CommandExecutedEventData data) { Interlocked.Increment(ref Commands); Sql.AddOrUpdate(data.Command.CommandText, 1, (_, n) => n + 1); }
        else if (item.Key.EndsWith(".TransactionCommitted", StringComparison.Ordinal)) Interlocked.Increment(ref Transactions);
    }
    public void OnCompleted() { } public void OnError(Exception error) { }
    public void Dispose() { foreach (var subscription in subscriptions) subscription.Dispose(); }
}
