using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Microsoft.Data.Sqlite;

var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(args[0]))!;
if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http" || config.Seconds is < 1 or > 60 || config.WarmupSeconds is < 1 or > 20 || config.Concurrency is < 1 or > 16)
    throw new ArgumentException("Use a bounded owned loopback host.");
if (!Path.IsPathFullyQualified(config.Output) || Directory.Exists(config.Output)) throw new IOException("Use a fresh absolute output directory.");
Directory.CreateDirectory(config.Output);
using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false, MaxConnectionsPerServer = config.Concurrency };
using var client = new HttpClient(handler) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(60) };
async Task<JsonElement> Post(string path, object body)
{
    using var response = await client.PostAsJsonAsync(path, body); response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
}
async Task<JsonElement> Get(string path)
{
    using var response = await client.GetAsync(path); response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
}
// The operator composes only an owned development host with this fixed demo account.
await Post("/_elsa/identity/login", new { username = "admin", password = "Password123!" });
var process = new BpmnProcessDefinition("efficiency-empty-bpmn", Elements: [new("start", BpmnElementTypes.StartEvent), new("end", BpmnElementTypes.EndEvent)], SequenceFlows: [new("finish", "start", "end")]);
var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), []);
var publication = await Post("/bpmn/artifacts", new { requestId = Guid.NewGuid().ToString("N"), definition });
var source = publication.GetProperty("sourceReferenceId").GetString()!;
var requests = new ConcurrentBag<(string Request, string Execution)>();
var failures = new ConcurrentBag<string>();
async Task Start()
{
    var request = Guid.NewGuid().ToString("N");
    var started = await Post("/bpmn/executions", new { sourceReferenceId = source, requestId = request });
    if (started.GetProperty("pending").GetBoolean() || started.GetProperty("replay").GetBoolean()) throw new InvalidOperationException("Fresh direct model start did not acknowledge synchronously.");
    requests.Add((request, started.GetProperty("executionId").GetString()!));
}
var cold = Stopwatch.StartNew(); await Start(); cold.Stop();
async Task<double[]> Window(int seconds)
{
    var samples = new ConcurrentBag<double>(); var clock = Stopwatch.StartNew();
    await Task.WhenAll(Enumerable.Range(0, config.Concurrency).Select(_ => Task.Run(async () =>
    {
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            var before = Stopwatch.GetTimestamp();
            try { await Start(); samples.Add(Stopwatch.GetElapsedTime(before).TotalMilliseconds); }
            catch (Exception error) { failures.Add(error.GetType().Name); Console.Error.WriteLine(error); break; }
        }
    })));
    return samples.Order().ToArray();
}
var warmup = await Window(config.WarmupSeconds);
using var host = Process.GetProcessById(config.HostProcessId);
var cpuBefore = host.TotalProcessorTime; var allocationBefore = GC.GetTotalAllocatedBytes(true);
var startedAt = DateTimeOffset.UtcNow; var clock = Stopwatch.StartNew(); var latencies = await Window(config.Seconds); clock.Stop(); var endedAt = DateTimeOffset.UtcNow;
host.Refresh();
var hostCpu = (host.TotalProcessorTime - cpuBefore).TotalSeconds; var clientAllocated = GC.GetTotalAllocatedBytes(true) - allocationBefore;
host.Refresh(); var hostPeak = host.PeakWorkingSet64;
var finalViews = new Dictionary<string, JsonElement>(StringComparer.Ordinal); var allFinal = true;
foreach (var (_, execution) in requests)
{
    var final = await Get("/bpmn/executions/" + execution); var work = await Get("/bpmn/executions/" + execution + "/work");
    allFinal &= final.GetProperty("status").GetString() == "TerminalSettled" && final.GetProperty("result").GetProperty("status").GetString() == "Completed" && work.GetProperty("items").GetArrayLength() == 0;
    finalViews[execution] = final;
}
var first = requests.First(); var replay = await Post("/bpmn/executions", new { sourceReferenceId = source, requestId = first.Request });
var replayCorrect = replay.GetProperty("replay").GetBoolean() && replay.GetProperty("executionId").GetString() == first.Execution &&
    JsonNode.DeepEquals(JsonNode.Parse((await Get("/bpmn/executions/" + first.Execution)).GetRawText()), JsonNode.Parse(finalViews[first.Execution].GetRawText()));
await using var connection = new SqliteConnection($"Data Source={config.RuntimeDatabase};Mode=ReadOnly;Pooling=False"); await connection.OpenAsync();
var counts = new Dictionary<string, long>();
foreach (var table in new[] { "elsa_runtime_model_execution", "elsa_runtime_workflow_execution_state", "elsa_runtime_activity_execution_state", "elsa_runtime_bookmark_state", "elsa_runtime_scheduler_work_item" })
{
    await using var command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM {table}"; counts[table] = Convert.ToInt64(await command.ExecuteScalarAsync());
}
var correct = failures.IsEmpty && requests.Count == warmup.Length + latencies.Length + 1 && finalViews.Count == requests.Count && allFinal && replayCorrect &&
    counts["elsa_runtime_model_execution"] == requests.Count && counts.Where(x => x.Key != "elsa_runtime_model_execution").All(x => x.Value == 0);
double Percentile(double p) => latencies.Length == 0 ? 0 : latencies[Math.Clamp((int)Math.Ceiling(latencies.Length * p) - 1, 0, latencies.Length - 1)];
var result = new { config, Model = "direct-bpmn", Shape = "start event -> end event; two BPMN elements, no native activity or business work", Provider = "Sqlite", Runtime = RuntimeInformation.FrameworkDescription,
    ClientProcessorCount = Environment.ProcessorCount, ColdFirstStartMilliseconds = cold.Elapsed.TotalMilliseconds, WarmupCount = warmup.Length,
    startedAt, endedAt, Seconds = clock.Elapsed.TotalSeconds, Count = latencies.Length, StartsPerSecond = latencies.Length / clock.Elapsed.TotalSeconds,
    P50Milliseconds = Percentile(.5), P95Milliseconds = Percentile(.95), HostCpuSeconds = hostCpu, HostPeakWorkingSet = hostPeak,
    ClientAllocatedBytesPerStart = latencies.Length == 0 ? 0 : clientAllocated / (double)latencies.Length,
    AllocationBoundary = "HTTP driver allocation only; host allocation is not measured by this fixture. Host CPU/peak are sampled separately.",
    Correct = correct, TotalStartsIncludingColdAndWarmup = requests.Count, AllTerminalSettled = allFinal, ExactStartReplay = replayCorrect, Counts = counts, Errors = failures.ToArray(), LatenciesMilliseconds = latencies,
    MeasurementBoundary = "Authenticated HTTP model start/acknowledgement. Publication, final per-execution reads and replay validation excluded. Model/body differs from the native activity probe." };
await File.WriteAllTextAsync(Path.Combine(config.Output, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { result.Correct, result.Count, result.StartsPerSecond, result.TotalStartsIncludingColdAndWarmup }));
if (!correct) Environment.ExitCode = 2;
public record Config(string BaseUrl, string Output, string RuntimeDatabase, int HostProcessId, int Seconds = 15, int WarmupSeconds = 5, int Concurrency = 1);
