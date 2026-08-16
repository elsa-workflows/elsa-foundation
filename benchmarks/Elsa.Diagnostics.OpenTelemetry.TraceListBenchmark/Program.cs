using System.Diagnostics;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

if (args.Contains("--v1-v2", StringComparer.Ordinal))
{
    await CompareGroundworkVersionsAsync(args);
    return;
}

var options = BenchmarkOptions.Parse(args);
var corpus = TraceCorpus.Create(options.Seed, options.TraceCount);

Console.WriteLine("Trace-list latency benchmark");
Console.WriteLine($"scope=provider-route (the Elsa trace provider call; HTTP transport/JSON are excluded)");
Console.WriteLine($"seed={options.Seed} traces={corpus.TraceCount} services={TraceCorpus.ServiceCount} expected={corpus.ExpectedMatches} warmups={options.Warmups} samples={options.Samples}");
Console.WriteLine($"input-sha256={corpus.Fingerprint}");
Console.WriteLine("oracle=EF Core OpenTelemetry store on file-backed SQLite; target=Groundwork v2 ordinary units on file-backed SQLite");

var oracle = await RunAsync("ef-core-v1-oracle", CreateEfStoreAsync, corpus, options);
var target = await RunAsync("groundwork-v2-target", CreateGroundworkStoreAsync, corpus, options);
var ratio = target.Statistics.P95Milliseconds / oracle.Statistics.P95Milliseconds;
var report = new BenchmarkReport(
    "otel-trace-list-provider-route-v1",
    DateTimeOffset.UtcNow,
    options,
    corpus.Fingerprint,
    oracle,
    target,
    ratio);

Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

static async Task<Measurement> RunAsync(
    string name,
    Func<TraceCorpus, Task<StoreHandle>> createStore,
    TraceCorpus corpus,
    BenchmarkOptions options)
{
    await using var handle = await createStore(corpus);
    var provider = new DefaultOpenTelemetryProvider(handle.Store, new NullCollectorConfigurationProvider());
    var filter = corpus.Filter;

    var initial = await provider.GetTracesAsync(filter);
    var expectedResultDigest = Validate(initial, corpus.ExpectedMatches, name, "initial");
    for (var i = 0; i < options.Warmups; i++)
        Validate(await provider.GetTracesAsync(filter), corpus.ExpectedMatches, name, $"warmup-{i}", expectedResultDigest);

    var samples = new double[options.Samples];
    for (var i = 0; i < samples.Length; i++)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await provider.GetTracesAsync(filter);
        stopwatch.Stop();
        Validate(result, corpus.ExpectedMatches, name, $"sample-{i}", expectedResultDigest);
        samples[i] = stopwatch.Elapsed.TotalMilliseconds;
    }

    var statistics = Statistics.Create(samples);
    Console.WriteLine($"{name}: n={statistics.Count} mean={statistics.MeanMilliseconds:F3}ms p50={statistics.P50Milliseconds:F3}ms p95={statistics.P95Milliseconds:F3}ms p99={statistics.P99Milliseconds:F3}ms");
    return new(name, statistics, corpus.ExpectedMatches, corpus.Fingerprint, expectedResultDigest, samples);
}

static async Task CompareGroundworkVersionsAsync(string[] args)
{
    var options = BenchmarkOptions.Parse(args);
    var childPath = Option(args, "--v1-child") ?? throw new ArgumentException("--v1-v2 requires --v1-child <path-to-v1-child.dll>.");
    if (!File.Exists(childPath))
        throw new FileNotFoundException("The isolated Groundwork v1 child was not found.", childPath);

    var child = new ProcessStartInfo
    {
        FileName = "dotnet",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    child.ArgumentList.Add(childPath);
    child.ArgumentList.Add("--warmups");
    child.ArgumentList.Add(options.Warmups.ToString());
    child.ArgumentList.Add("--samples");
    child.ArgumentList.Add(options.Samples.ToString());
    child.ArgumentList.Add("--traces");
    child.ArgumentList.Add(options.TraceCount.ToString());
    child.ArgumentList.Add("--seed");
    child.ArgumentList.Add(options.Seed.ToString());

    using var process = Process.Start(child) ?? throw new InvalidOperationException("Unable to start the isolated Groundwork v1 child.");
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var stdout = await stdoutTask;
    var stderr = await stderrTask;
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"The Groundwork v1 child failed with exit code {process.ExitCode}: {stderr}");

    var before = JsonSerializer.Deserialize<Measurement>(stdout.Trim()) ??
                 throw new InvalidDataException("The Groundwork v1 child did not emit a measurement JSON object.");
    var corpus = TraceCorpus.Create(options.Seed, options.TraceCount);
    if (!string.Equals(before.InputSha256, corpus.Fingerprint, StringComparison.Ordinal))
        throw new InvalidDataException($"The Groundwork v1 child used a different input (reported '{before.InputSha256}', expected '{corpus.Fingerprint}').");
    var after = await RunAsync("groundwork-v2-target", CreateGroundworkStoreAsync, corpus, options);
    if (!string.Equals(before.ResultTraceIdsSha256, after.ResultTraceIdsSha256, StringComparison.Ordinal))
        throw new InvalidDataException($"Groundwork v1 and v2 returned different ordered trace IDs ('{before.ResultTraceIdsSha256}' versus '{after.ResultTraceIdsSha256}').");
    var ratio = after.Statistics.P95Milliseconds / before.Statistics.P95Milliseconds;

    Console.WriteLine(JsonSerializer.Serialize(
        new GroundworkVersionComparison(
            "otel-trace-list-provider-route-groundwork-v1-v2",
            DateTimeOffset.UtcNow,
            options,
            corpus.Fingerprint,
            before,
            after,
            ratio,
            BenchmarkProvenance.Current),
        new JsonSerializerOptions { WriteIndented = true }));
}

static string? Option(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
        if (string.Equals(args[index], name, StringComparison.Ordinal))
            return args[index + 1];
    return null;
}

static string Validate(
    OpenTelemetryTraceResult result,
    int expected,
    string store,
    string phase,
    string? expectedResultDigest = null)
{
    if (result.Items.Count != expected)
        throw new InvalidOperationException($"{store} {phase} returned {result.Items.Count} traces; expected {expected}.");

    var resultDigest = BenchmarkFingerprint.OrderedTraceIds(result);
    if (expectedResultDigest is not null && !string.Equals(resultDigest, expectedResultDigest, StringComparison.Ordinal))
        throw new InvalidOperationException($"{store} {phase} returned a different ordered trace-ID set ({resultDigest}); expected {expectedResultDigest}.");
    return resultDigest;
}

static async Task<StoreHandle> CreateEfStoreAsync(TraceCorpus corpus)
{
    var databasePath = NewDatabasePath("ef");
    var host = SqliteEfContextFactory.Create(databasePath);
    var options = OptionsFor(corpus);
    var store = new EfCoreOpenTelemetryStore(host, options, new OpenTelemetrySourceRegistry(options));
    store.StartDraining();
    foreach (var batch in corpus.Batches)
        await store.WriteAsync(batch);
    await store.CompleteDrainingAsync();

    return new StoreHandle(store, async () =>
    {
        await store.DisposeAsync();
        host.Dispose();
        DeleteDatabase(databasePath);
    });
}

static async Task<StoreHandle> CreateGroundworkStoreAsync(TraceCorpus corpus)
{
    var databasePath = NewDatabasePath("groundwork");
    var connection = new SqliteProviderFactory().Create($"Data Source={databasePath}");
    var options = OptionsFor(corpus);
    var store = new GroundworkOpenTelemetryStore(
        connection,
        options,
        new V2OpenTelemetryBinding("benchmark", "otel", "trace-list"));
    var lease = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
    foreach (var batch in corpus.Batches)
        await store.WriteAsync(DiagnosticsDrainBatchId.New(), batch);

    return new StoreHandle(store, async () =>
    {
        await store.DisposeAsync();
        await lease.DisposeAsync();
        if (connection is IAsyncDisposable asyncConnection)
            await asyncConnection.DisposeAsync();
        else if (connection is IDisposable disposableConnection)
            disposableConnection.Dispose();
        DeleteDatabase(databasePath);
    });
}

static IOptions<OpenTelemetryDiagnosticsOptions> OptionsFor(TraceCorpus corpus) =>
    Options.Create(new OpenTelemetryDiagnosticsOptions
    {
        TraceCapacity = corpus.TraceCount,
        SpanCapacity = corpus.TraceCount,
        ResourceCapacity = TraceCorpus.ServiceCount,
        MaxQuerySize = corpus.TraceCount,
        SubscriberChannelCapacity = corpus.Batches.Count + 10,
        ShutdownDrainTimeout = TimeSpan.FromMinutes(1)
    });

static string NewDatabasePath(string name) => Path.Combine(Path.GetTempPath(), $"elsa-otel-trace-list-{name}-{Guid.NewGuid():N}.db");

static void DeleteDatabase(string path)
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        var candidate = path + suffix;
        if (File.Exists(candidate))
            File.Delete(candidate);
    }
}

internal sealed record StoreHandle(IOpenTelemetryStore Store, Func<ValueTask> Cleanup) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Cleanup();
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

internal sealed class SqliteEfContextFactory : IDbContextFactory<OpenTelemetryDbContext>, IDisposable
{
    private readonly SqliteConnection rootConnection;
    private readonly string connectionString;
    private readonly ServiceProvider services;

    private SqliteEfContextFactory(SqliteConnection rootConnection, string connectionString, ServiceProvider services)
    {
        this.rootConnection = rootConnection;
        this.connectionString = connectionString;
        this.services = services;
    }

    public static SqliteEfContextFactory Create(string databasePath)
    {
        var connectionString = $"Data Source={databasePath}";
        var rootConnection = new SqliteConnection(connectionString);
        rootConnection.Open();
        var services = new ServiceCollection()
            .AddSingleton<ISystemClock, SystemClock>()
            .AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>()
            .BuildServiceProvider();
        var factory = new SqliteEfContextFactory(rootConnection, connectionString, services);
        using var context = factory.CreateDbContext();
        context.Database.EnsureCreated();
        return factory;
    }

    public OpenTelemetryDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OpenTelemetryDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpenTelemetryDbContext(options, services);
    }

    public Task<OpenTelemetryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose()
    {
        services.Dispose();
        rootConnection.Dispose();
    }
}

internal sealed record BenchmarkOptions(int Warmups, int Samples, int Seed, int TraceCount)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var warmups = Integer(args, "--warmups", 5);
        var samples = Integer(args, "--samples", 30);
        var seed = Integer(args, "--seed", 2682026);
        var traceCount = Integer(args, "--traces", 1_000);
        if (warmups < 0 || samples < 5 || traceCount < TraceCorpus.ServiceCount)
            throw new ArgumentException("Use --warmups >= 0, --samples >= 5, and --traces >= 8.");
        if (traceCount > 1_000)
            throw new ArgumentException("--traces must be <= 1000 because the v2 trace profile is bounded to 1000 groups.");
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
                return new OpenTelemetryBatch(
                    batchIndex == 0 ? resources : [],
                    traces.GetRange(offset, count),
                    spans.GetRange(offset, count),
                    [], [], []);
            })
            .ToArray();
        var selectedService = "benchmark-service-03";
        var firstSelected = 3;
        var expected = Enumerable.Range(firstSelected, traceCount - firstSelected)
            .Count(index => index % ServiceCount == 3);
        var filter = new OpenTelemetryTraceFilter
        {
            ServiceName = selectedService,
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
        return new(
            sorted.Length,
            samples.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99));
    }
}

internal sealed record Measurement(
    string Name,
    Statistics Statistics,
    int ExpectedMatches,
    string? InputSha256 = null,
    string? ResultTraceIdsSha256 = null,
    double[]? Samples = null);

internal sealed record BenchmarkReport(
    string Benchmark,
    DateTimeOffset GeneratedAtUtc,
    BenchmarkOptions Options,
    string InputSha256,
    Measurement Oracle,
    Measurement Target,
    double TargetToOracleP95Ratio);

internal sealed record GroundworkVersionComparison(
    string Benchmark,
    DateTimeOffset GeneratedAtUtc,
    BenchmarkOptions Options,
    string InputSha256,
    Measurement Before,
    Measurement After,
    double AfterToBeforeP95Ratio,
    BenchmarkProvenance Provenance);

internal sealed record BenchmarkProvenance(
    string V1SourceCommit,
    string V1GroundworkPackage,
    string V2SourceCommit,
    string V2GroundworkPackage,
    string Os,
    string Runtime,
    string Architecture,
    int ProcessorCount)
{
    public static BenchmarkProvenance Current { get; } = new(
        "e30c2d291a34d3c5e986a9339af9722748572cac",
        "0.0.1-preview.114",
        "fc29bd5065cdeaced2b16dbd9ce5ffc1ff874806",
        "0.1.0-preview.1",
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount);
}
