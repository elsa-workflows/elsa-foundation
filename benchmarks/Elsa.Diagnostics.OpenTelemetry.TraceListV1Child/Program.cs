using System.Diagnostics;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Elsa.Diagnostics.Persistence.Observability;
using Groundwork.Core.Capabilities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite;
using Groundwork.Sqlite.DiagnosticRecords;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Extensions.Options;

var options = BenchmarkOptions.Parse(args);
var corpus = TraceCorpus.Create(options.Seed, options.TraceCount);
var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-otel-trace-list-v1-{Guid.NewGuid():N}.db");
var binding = GroundworkOpenTelemetryBinding.Create("benchmark", "otel", "trace-list");

try
{
    var providers = await CreateProvidersAsync(databasePath, binding);
    await using var store = new GroundworkOpenTelemetryStore(
        providers,
        Options.Create(OptionsFor(corpus)),
        binding);
    var provider = new DefaultOpenTelemetryProvider(store, new NullCollectorConfigurationProvider());
    store.Start();
    foreach (var batch in corpus.Batches)
        await store.WriteAsync(batch);
    await store.CompleteDrainingAsync();

    var initial = await provider.GetTracesAsync(corpus.Filter);
    var expectedResultDigest = Validate(initial, corpus.ExpectedMatches, "initial");
    for (var index = 0; index < options.Warmups; index++)
        Validate(await provider.GetTracesAsync(corpus.Filter), corpus.ExpectedMatches, $"warmup-{index}", expectedResultDigest);

    var samples = new double[options.Samples];
    for (var index = 0; index < samples.Length; index++)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await provider.GetTracesAsync(corpus.Filter);
        stopwatch.Stop();
        Validate(result, corpus.ExpectedMatches, $"sample-{index}", expectedResultDigest);
        samples[index] = stopwatch.Elapsed.TotalMilliseconds;
    }

    Console.WriteLine(JsonSerializer.Serialize(
        new Measurement("groundwork-v1-shipping", Statistics.Create(samples), corpus.ExpectedMatches, corpus.Fingerprint, expectedResultDigest, samples)));
}
finally
{
    DeleteDatabase(databasePath);
}

static async Task<GroundworkOpenTelemetryStores> CreateProvidersAsync(
    string databasePath,
    GroundworkOpenTelemetryBinding binding)
{
    var connectionString = $"Data Source={databasePath}";
    var streams = OpenTelemetryGroundworkStorageSchema.CreateStreams(binding);
    var traces = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[0]);
    var spans = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[1]);
    var points = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[2]);
    var logs = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[3]);
    var manifest = OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest();
    var provider = new ProviderIdentity("groundwork-sqlite", "1.0.0");
    var target = PhysicalSchemaTargetCompiler.Compile(manifest, provider, SqliteGroundworkCapabilities.PhysicalNames);
    var documents = await SqliteDocumentStoreFactory.OpenPhysicalAsync(
        connectionString,
        manifest,
        provider,
        DocumentStoreAccess.Scoped(binding.DocumentStorageScope),
        options: new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });
    var queries = new RoutedBoundedDocumentStore(target.Routes.Select(route =>
        KeyValuePair.Create<string, IBoundedDocumentStore>(
            route.StorageUnit.Value,
            SqlitePhysicalQueryRuntime.Create(documents, manifest, route, target.Provider))));
    return new(traces, spans, points, logs, documents, queries);
}

static OpenTelemetryDiagnosticsOptions OptionsFor(TraceCorpus corpus) => new()
{
    TraceCapacity = corpus.TraceCount,
    SpanCapacity = corpus.TraceCount,
    MetricPointCapacity = corpus.TraceCount,
    LogRecordCapacity = corpus.TraceCount,
    ResourceCapacity = TraceCorpus.ServiceCount,
    MetricInstrumentCapacity = TraceCorpus.ServiceCount,
    MaxQuerySize = corpus.TraceCount,
    SubscriberChannelCapacity = corpus.Batches.Count + 10,
    ShutdownDrainTimeout = TimeSpan.FromMinutes(1)
};

static string Validate(OpenTelemetryTraceResult result, int expected, string phase, string? expectedResultDigest = null)
{
    if (result.Items.Count != expected)
        throw new InvalidOperationException($"Groundwork v1 {phase} returned {result.Items.Count} traces; expected {expected}.");

    var resultDigest = BenchmarkFingerprint.OrderedTraceIds(result);
    if (expectedResultDigest is not null && !string.Equals(resultDigest, expectedResultDigest, StringComparison.Ordinal))
        throw new InvalidOperationException($"Groundwork v1 {phase} returned a different ordered trace-ID set ({resultDigest}); expected {expectedResultDigest}.");
    return resultDigest;
}

static void DeleteDatabase(string path)
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        var candidate = path + suffix;
        if (File.Exists(candidate))
            File.Delete(candidate);
    }
}

internal sealed class NullCollectorConfigurationProvider : ICollectorConfigurationProvider
{
    public ValueTask<CollectorConfiguration> GetAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CollectorConfiguration(
            new CollectorEndpointInfo("http", null, false, "benchmark"),
            new CollectorEndpointInfo("grpc", null, false, "benchmark"),
            "OTEL_SERVICE_NAME",
            "OTEL_EXPORTER_OTLP_ENDPOINT",
            "OTEL_EXPORTER_OTLP_PROTOCOL",
            new Dictionary<string, string>()));
}

internal sealed class RoutedBoundedDocumentStore(IEnumerable<KeyValuePair<string, IBoundedDocumentStore>> stores) : IBoundedDocumentStore
{
    private readonly IReadOnlyDictionary<string, IBoundedDocumentStore> routes = stores.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) => Route(query).QueryAsync(query, cancellationToken);
    public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) => Route(query).CountAsync(query, cancellationToken);
    public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) => Route(query).FirstOrDefaultAsync(query, cancellationToken);
    public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) => Route(query).AnyAsync(query, cancellationToken);
    private IBoundedDocumentStore Route(DocumentQuery query) => routes.TryGetValue(query.DocumentKind, out var route) ? route : throw new InvalidOperationException($"No v1 document route exists for '{query.DocumentKind}'.");
}

internal sealed record BenchmarkOptions(int Warmups, int Samples, int Seed, int TraceCount)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var warmups = Integer(args, "--warmups", 5);
        var samples = Integer(args, "--samples", 30);
        var seed = Integer(args, "--seed", 2682026);
        var traceCount = Integer(args, "--traces", 1_000);
        if (warmups < 0 || samples < 5 || traceCount < TraceCorpus.ServiceCount || traceCount > 5_000)
            throw new ArgumentException("Use --warmups >= 0, --samples >= 5, and --traces between 8 and 5000.");
        return new(warmups, samples, seed, traceCount);
    }

    private static int Integer(string[] args, string option, int fallback)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
    }
}

internal sealed class TraceCorpus
{
    public const int ServiceCount = 8;
    public required IReadOnlyList<OpenTelemetryBatch> Batches { get; init; }
    public required OpenTelemetryTraceFilter Filter { get; init; }
    public required string Fingerprint { get; init; }
    public required int TraceCount { get; init; }
    public required int ExpectedMatches { get; init; }

    public static TraceCorpus Create(int seed, int traceCount)
    {
        var baseTime = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero).AddSeconds(seed % 60);
        var resources = Enumerable.Range(0, ServiceCount)
            .Select(service => new TelemetryResource(
                $"resource-{service:D2}",
                $"benchmark-service-{service:D2}",
                $"instance-{service:D2}",
                "dotnet",
                new Dictionary<string, string?> { ["benchmark.seed"] = seed.ToString() },
                baseTime,
                TelemetryResourceStatus.Active))
            .ToArray();
        var traces = new List<TelemetryTrace>(traceCount);
        var spans = new List<TelemetrySpan>(traceCount);
        for (var index = 0; index < traceCount; index++)
        {
            var service = index % ServiceCount;
            var start = baseTime.AddMilliseconds(index);
            var traceId = $"trace-{index:D6}";
            var spanId = $"span-{index:D6}";
            traces.Add(new(traceId, spanId, $"checkout-trace-{index:D6}", start, start.AddMilliseconds(4), TimeSpan.FromMilliseconds(4), SpanStatus.Ok, [resources[service].Id], [], 1));
            spans.Add(new($"span-record-{index:D6}", traceId, spanId, null, resources[service].Id, $"checkout-span-{index:D6}", "server", start, start.AddMilliseconds(4), SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []));
        }

        const int batchSize = 100;
        var batches = Enumerable.Range(0, (traceCount + batchSize - 1) / batchSize)
            .Select(batchIndex =>
            {
                var offset = batchIndex * batchSize;
                var count = Math.Min(batchSize, traceCount - offset);
                return new OpenTelemetryBatch(batchIndex == 0 ? resources : [], traces.GetRange(offset, count), spans.GetRange(offset, count), [], [], []);
            })
            .ToArray();
        var expected = Enumerable.Range(3, traceCount - 3).Count(index => index % ServiceCount == 3);
        var filter = new OpenTelemetryTraceFilter
        {
            ServiceName = "benchmark-service-03",
            Search = "checkout-trace",
            From = baseTime,
            To = baseTime.AddMilliseconds(traceCount),
            Take = expected
        };
        return new()
        {
            Batches = batches,
            Filter = filter,
            Fingerprint = BenchmarkFingerprint.Input(seed, traceCount, batches, filter),
            TraceCount = traceCount,
            ExpectedMatches = expected
        };
    }
}

internal sealed record Statistics(int Count, double MeanMilliseconds, double P50Milliseconds, double P95Milliseconds, double P99Milliseconds)
{
    public static Statistics Create(IReadOnlyList<double> samples)
    {
        var sorted = samples.OrderBy(value => value).ToArray();
        static double Percentile(double[] values, double percentile)
        {
            var index = (values.Length - 1) * percentile;
            var lower = (int)Math.Floor(index);
            var upper = (int)Math.Ceiling(index);
            return lower == upper ? values[lower] : values[lower] + (values[upper] - values[lower]) * (index - lower);
        }
        return new(samples.Count, samples.Average(), Percentile(sorted, 0.50), Percentile(sorted, 0.95), Percentile(sorted, 0.99));
    }
}

internal sealed record Measurement(
    string Name,
    Statistics Statistics,
    int ExpectedMatches,
    string? InputSha256 = null,
    string? ResultTraceIdsSha256 = null,
    double[]? Samples = null);
