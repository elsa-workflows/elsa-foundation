using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1191: CSV routes through the real worker, store and child worker processes.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    private const string CsvJourneySource = "id,name\n1,Ada\n2,skip\n99,unknown\n3,Bram\n";

    [Theory]
    [InlineData("csv", "csv", "csv")]
    [InlineData("jsonl", "csv", "csv")]
    [InlineData("http", "csv", "csv")]
    [InlineData("csv", "csv", "jsonl")]
    [InlineData("csv", "jsonl", "csv")]
    public async Task Shared_worker_publishes_csv_files_with_a_header_per_batch(string sourceKind, string mainKind, string errorKind)
    {
        await using var files = new RuntimeFiles(sourceKind == "jsonl"
            ? "{\"id\":1,\"name\":\"Ada\"}\n{\"id\":2,\"name\":\"skip\"}\n{\"id\":99,\"name\":\"unknown\"}\n{\"id\":3,\"name\":\"Bram\"}\n"
            : CsvJourneySource);
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, request.RawTarget switch
            {
                "/a" => "{\"records\":[{\"id\":1,\"name\":\"Ada\"},{\"id\":2,\"name\":\"skip\"}],\"next\":null}",
                "/b" => "{\"records\":[{\"id\":99,\"name\":\"unknown\"},{\"id\":3,\"name\":\"Bram\"}],\"next\":null}",
                _ => throw new InvalidOperationException("Unexpected fixture request.")
            })));
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: sourceKind != "http", httpMain: false, httpErrors: false));
        var pipeline = CsvRoutePipeline(sourceKind, errorKind == "csv", mainKind == "csv");
        var invocation = ConnectorInvocation(files.Scope, $"csv-routes-{sourceKind}-{mainKind}-{errorKind}", pipeline);

        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal((2, 1, 1, 0, 0), (run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords, run.OutstandingRecords, run.OutstandingBytes));
        if (mainKind == "csv")
        {
            var main = ReadCsvRoute(files.OutputRoot, "name,category");
            // One record per batch: each accepted record is its own file, and every file carries the header.
            Assert.Equal(2, main.Files);
            Assert.Equal(["Ada,one", "Bram,three"], main.Rows.Order().ToArray());
            Assert.Empty(Directory.EnumerateFiles(files.OutputRoot, "*.jsonl"));
        }
        else
            Assert.Equal(2, Directory.EnumerateFiles(files.OutputRoot, "*.jsonl").Count());
        if (errorKind == "csv")
        {
            var errors = ReadCsvRoute(files.ErrorRoot, "recordOrdinal,code,field");
            Assert.Equal(1, errors.Files);
            Assert.Matches(@"^\d+,etl\.lookup-key-not-found,id$", Assert.Single(errors.Rows));
        }
        else
            Assert.Single(Directory.EnumerateFiles(files.ErrorRoot, "*.jsonl"));
        await using var scope = host.Services.CreateAsyncScope();
        var inspection = Assert.IsType<EtlConnectorRunInspection>(await scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeInspector>()
            .FindConnectorInspectionAsync(files.Scope, invocation.RunId));
        Assert.Equal((2, 1), (inspection.AcceptedMainEffects, inspection.AcceptedErrorEffects));
        Assert.Equal(mainKind == "csv" ? EtlConnectorKind.Csv : EtlConnectorKind.Jsonl, inspection.Run.Bindings.Main.Kind);
    }

    [Fact]
    public async Task One_output_approval_serves_a_jsonl_and_a_csv_pipeline_without_collisions()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var csv = CsvRoutePipeline("csv", csvErrors: true);
        var jsonl = csv with { Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")) };

        foreach (var (pipeline, runId) in new[] { (jsonl, "shared-jsonl"), (csv, "shared-csv") })
        {
            var invocation = ConnectorInvocation(files.Scope, runId, pipeline);
            await files.AdmitAsync(host, invocation);
            Assert.Equal(EtlRunStatus.BusinessError, (await files.WaitForTerminalAsync(host, invocation.RunId)).Status);
        }

        Assert.Equal(2, Directory.EnumerateFiles(files.OutputRoot, "*.jsonl").Count());
        Assert.Equal(2, Directory.EnumerateFiles(files.OutputRoot, "*.csv").Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Csv_worker_processes_replay_a_published_effect_or_refuse_a_conflicting_file(bool conflict)
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(deadline.Token);
        var routes = CsvRoutePipeline("csv", csvErrors: true);
        var invocation = ConnectorInvocation(files.Scope, $"csv-process-{conflict}", routes with { Limits = routes.Limits with { MaxBatchRecords = 1_000 } });
        await files.AdmitAsync(host, invocation);

        EtlWorkerProcess predecessor;
        EtlConnectorWorkerCheckpoint? stopped = null;
        Dictionary<string, string> before;
        if (conflict)
        {
            // Pause the first CSV publication mid-write, then put a different file at the effect's create-only key.
            predecessor = await files.StartWorkerAsync("csv-predecessor", deadline.Token, EtlWorkerWritePause.OutputChunk);
            var write = await predecessor.WaitForWriteAsync(deadline.Token);
            Assert.Equal(".csv", write.Extension);
            var target = Path.Combine(write.OutputRoot, EtlRuntimeIdentity.ConnectorFileOutputKey(write.EffectIdentity, EtlConnectorKind.Csv));
            Assert.False(File.Exists(target));
            await File.WriteAllTextAsync(target, "name,category\r\nEve,forged\r\n");
            before = new() { [target] = File.ReadAllText(target) };
        }
        else
        {
            predecessor = await files.StartWorkerAsync("csv-predecessor", deadline.Token, connectorGate: EtlConnectorWorkerGate.BeforeAcceptance);
            stopped = await predecessor.WaitForConnectorCheckpointAsync(deadline.Token);
            var published = new[] { files.OutputRoot, files.ErrorRoot }.Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csv")).ToArray();
            Assert.NotEmpty(published);
            Assert.All(published, path => Assert.StartsWith(Path.GetDirectoryName(path) == files.OutputRoot ? "name,category\r\n" : "recordOrdinal,code,field\r\n",
                File.ReadAllText(path)));
            before = published.ToDictionary(path => path, File.ReadAllText);
        }

        await predecessor.CrashAsync();
        await WaitForAllPartitionLeasesExpiredAsync(host.Services, deadline.Token);
        var survivor = await files.StartWorkerAsync("csv-survivor", deadline.Token,
            connectorGate: conflict ? EtlConnectorWorkerGate.None : EtlConnectorWorkerGate.BeforeAcceptance);
        if (!conflict)
        {
            var recovered = await survivor.WaitForConnectorCheckpointAsync(deadline.Token);
            AssertReplacementWorker(stopped!, recovered, survivor);
            Assert.Equal(stopped!.BatchId, recovered.BatchId);
            await survivor.ReleaseAsync(deadline.Token);
        }

        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.All(before, pair => Assert.Equal(pair.Value, File.ReadAllText(pair.Key)));
        if (conflict)
        {
            // The conflict settles through the shared diagnostic path, which classifies it as a business error, as for JSONL.
            Assert.Equal("etl.effect-conflict", run.TerminalResult!.Diagnostic!.Code);
            Assert.Equal(EtlRunStatus.BusinessError, run.Status);
            return;
        }

        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal((2, 1, 1, 0, 0), (run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords, run.OutstandingRecords, run.OutstandingBytes));
        // The replayed effect reused its create-only file: one main file per accepted batch, never a second copy.
        Assert.Equal(["Ada,one", "Bram,three"], ReadCsvRoute(files.OutputRoot, "name,category").Rows.Order().ToArray());
    }

    private static async Task WaitForAllPartitionLeasesExpiredAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var operation = services.CreateAsyncScope();
            var leases = await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorPartitions.AsNoTracking()
                .Select(row => row.ExpiresAtUtcTicks).ToListAsync(cancellationToken);
            if (leases.All(expires => expires is null || expires < DateTimeOffset.UtcNow.UtcTicks - TimeSpan.TicksPerSecond / 2))
                return;
            await Task.Delay(100, cancellationToken);
        }
    }

    private static EtlPipelineV2 CsvRoutePipeline(string sourceKind, bool csvErrors, bool csvMain = true)
    {
        var baseline = EtlV2TestPipelines.Create();
        EtlSource source = sourceKind switch
        {
            "csv" => new EtlCsvSource("source", EtlTestPipelines.Create().SourceSchema),
            "jsonl" => new EtlJsonlSource("source", [new EtlJsonField("id", "/id", EtlColumnType.Int64, Required: true),
                new EtlJsonField("name", "/name", EtlColumnType.String, Required: true)]),
            _ => baseline.Source
        };
        return baseline with
        {
            Source = source,
            Lookup = new("lookup", EtlTestPipelines.Create().LookupSchema, "id", "id"),
            Routes = new(csvMain ? new EtlCsvDestination("output") : new EtlJsonlDestination("output"),
                csvErrors ? new EtlCsvDestination("errors") : new EtlJsonlDestination("errors")),
            Mapping = [new("name", EtlFieldOrigin.Source, "name"), new("category", EtlFieldOrigin.Lookup, "category")],
            Filters = [new("name", EtlComparison.NotEqual, JsonSerializer.SerializeToElement("skip"))],
            Limits = baseline.Limits with { FilePartitionCount = 2, MaxBatchRecords = 1 }
        };
    }

    private static (int Files, IReadOnlyList<string> Rows) ReadCsvRoute(string directory, string header)
    {
        var rows = new List<string>();
        var paths = Directory.EnumerateFiles(directory, "*.csv").ToArray();
        foreach (var path in paths)
        {
            var text = File.ReadAllText(path);
            Assert.EndsWith("\r\n", text);
            var lines = text[..^2].Split("\r\n");
            Assert.Equal(header, lines[0]);
            rows.AddRange(lines.Skip(1));
        }
        return (paths.Length, rows);
    }
}
