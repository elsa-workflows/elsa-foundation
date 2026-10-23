using System.Net;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Runtime.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeApiHttpTests
{
    [Fact]
    public async Task Connector_runtime_only_api_pages_scoped_progress_and_exposes_retry_metadata_without_request_identity()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync(maximumPageSize: 2);
        Assert.Null(host.Host.Services.GetService<IEtlArtifactCompiler>());
        Assert.Null(host.Host.Services.GetService<EtlRuntimeWorker>());
        var scope = new EtlScope("tenant-a", "partition-a");
        var admission = await SeedConnectorAsync(host, "http-progress", scope);
        await SeedConnectorAsync(host, "http-other", scope);
        await SeedConnectorAsync(host, "http-foreign", new("tenant-b", "partition-a"));
        await using (var services = host.Host.Services.CreateAsyncScope())
        {
            var store = services.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
            var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(scope,
                admission.Invocation.RunId, "private-worker-identity"));
            var begin = await store.BeginConnectorHttpSourceAsync(lease, Guid.NewGuid().ToString("N"));
            Assert.Equal(EtlStoreStatus.Applied, begin.Status);
            var waiting = await store.CompleteConnectorHttpSourceAsync(lease, begin.Permit!, new(429, null, false));
            Assert.Equal(EtlStoreStatus.Applied, waiting.Status);
            Assert.Equal(EtlConnectorHttpSourceFetchPhase.Waiting, waiting.Fetch!.Phase);
        }

        using var detailResponse = await host.SendAsync(HttpMethod.Get, "/etl/runs/http-progress", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = await ReadJsonAsync(detailResponse);
        var connector = detail.RootElement.GetProperty("connector");
        Assert.Equal("http", RequiredString(connector, "sourceKind"));
        Assert.Equal("http", RequiredString(connector, "mainKind"));
        Assert.Equal(0, connector.GetProperty("acceptedMainEffects").GetInt64());
        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("sourceRecordCount").ValueKind);
        Assert.Null(OptionalString(detail.RootElement.GetProperty("bindingAliases"), "lookup"));
        AssertConnectorProjection(detail.RootElement);

        using var firstResponse = await host.SendAsync(HttpMethod.Get,
            "/etl/runs/http-progress/connector-partitions?limit=1", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var first = await ReadJsonAsync(firstResponse);
        var item = Assert.Single(first.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, item.GetProperty("partitionOrdinal").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("file").ValueKind);
        var progress = item.GetProperty("http");
        Assert.Equal(0, progress.GetProperty("pageSequence").GetInt64());
        Assert.Equal(0, progress.GetProperty("nextItem").GetInt32());
        Assert.False(progress.GetProperty("exhausted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, progress.GetProperty("pageRecordCount").ValueKind);
        var retry = progress.GetProperty("fetch");
        Assert.Equal("waiting", RequiredString(retry, "phase"));
        Assert.Equal(1, retry.GetProperty("attemptCount").GetInt32());
        Assert.Equal(429, retry.GetProperty("lastStatusCode").GetInt32());
        Assert.True(retry.GetProperty("notBefore").GetDateTimeOffset() <= retry.GetProperty("deadline").GetDateTimeOffset());
        AssertConnectorProjection(item);
        var cursor = RequiredString(first.RootElement, "nextCursor");

        var ordinals = new List<int> { 0 };
        var pages = 0;
        string? next = cursor;
        while (next is not null)
        {
            Assert.True(++pages <= 3, "Connector partition pagination did not terminate.");
            using var response = await host.SendAsync(HttpMethod.Get,
                $"/etl/runs/http-progress/connector-partitions?limit=1&cursor={Uri.EscapeDataString(next)}", permissions: ReadPermissions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var page = await ReadJsonAsync(response);
            var row = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
            AssertConnectorProjection(row);
            ordinals.Add(row.GetProperty("partitionOrdinal").GetInt32());
            next = OptionalString(page.RootElement, "nextCursor");
        }
        Assert.Equal(new[] { 0, 1, 2 }, ordinals);
        using var largeResponse = await host.SendAsync(HttpMethod.Get,
            "/etl/runs/http-progress/connector-partitions?limit=200", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, largeResponse.StatusCode);
        using var large = await ReadJsonAsync(largeResponse);
        Assert.Equal(new[] { 0, 1, 2 }, large.RootElement.GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("partitionOrdinal").GetInt32()));
        Assert.Null(OptionalString(large.RootElement, "nextCursor"));

        foreach (var (run, limit, tenant, partition) in new[]
        {
            ("http-other", 1, "tenant-a", "partition-a"),
            ("http-progress", 2, "tenant-a", "partition-a"),
            ("http-foreign", 1, "tenant-b", "partition-a")
        })
        {
            using var response = await host.SendAsync(HttpMethod.Get,
                $"/etl/runs/{run}/connector-partitions?limit={limit}&cursor={Uri.EscapeDataString(cursor)}",
                tenant: tenant, partition: partition, permissions: ReadPermissions);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "etl.cursor-invalid");
        }
        using var tampered = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs/http-progress/connector-partitions?limit=1&cursor={Uri.EscapeDataString(Tamper(cursor))}", permissions: ReadPermissions);
        await AssertProblemAsync(tampered, HttpStatusCode.BadRequest, "etl.cursor-invalid");
        using var cursorKind = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs?limit=1&cursor={Uri.EscapeDataString(cursor)}", permissions: ReadPermissions);
        await AssertProblemAsync(cursorKind, HttpStatusCode.BadRequest, "etl.cursor-invalid");
        using var foreign = await host.SendAsync(HttpMethod.Get,
            "/etl/runs/http-progress/connector-partitions", tenant: "tenant-b", permissions: ReadPermissions);
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "etl.run-not-found");
        using var denied = await host.SendAsync(HttpMethod.Get, "/etl/runs/http-progress/connector-partitions");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Connector_inspection_absence_is_explicit_while_legacy_file_detail_remains_available()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync(configureServices:
            services => services.RemoveAll<IEtlConnectorRuntimeInspector>());
        await SeedConnectorAsync(host, "no-inspector", new("tenant-a", "partition-a"));
        await SeedRunningAsync(host, "legacy-file", new("tenant-a", "partition-a"));
        foreach (var suffix in new[] { "", "/connector-partitions" })
        {
            using var response = await host.SendAsync(HttpMethod.Get, $"/etl/runs/no-inspector{suffix}", permissions: ReadPermissions);
            await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "etl.provider-unavailable");
        }
        using var wrongEndpoint = await host.SendAsync(HttpMethod.Get, "/etl/runs/no-inspector/partitions", permissions: ReadPermissions);
        await AssertProblemAsync(wrongEndpoint, HttpStatusCode.Conflict, "etl.connector-inspection-required");
        using var wrongFormat = await host.SendAsync(HttpMethod.Get, "/etl/runs/legacy-file/connector-partitions", permissions: ReadPermissions);
        await AssertProblemAsync(wrongFormat, HttpStatusCode.Conflict, "etl.connector-run-required");
        using var legacy = await host.SendAsync(HttpMethod.Get, "/etl/runs/legacy-file", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
    }

    [Fact]
    public async Task Connector_csv_inspection_retains_real_file_ranges_and_optional_lookup_absence()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync(maximumPageSize: 1);
        var pipeline = EtlV2TestPipelines.Create();
        pipeline = pipeline with
        {
            Source = new EtlCsvSource("source", [new EtlColumn("id", EtlColumnType.Int64, Required: true)]),
            Mapping = [new EtlOutputField("id", EtlFieldOrigin.Source, "id")],
            Routes = new(new EtlHttpDestination("output"), new EtlJsonlDestination("errors")),
            Limits = pipeline.Limits with { FilePartitionCount = 2 }
        };
        var input = EtlRuntimeFixtures.Prepared(4);
        await SeedConnectorAsync(host, "csv-connector", new("tenant-a", "partition-a"), pipeline,
            new(new(input.Source, input.SourceIndex, input.SourceRecordCount), null));
        using var response = await host.SendAsync(HttpMethod.Get, "/etl/runs/csv-connector/connector-partitions?limit=20", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var page = await ReadJsonAsync(response);
        var rows = page.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(1, rows[0].GetProperty("file").GetProperty("startOrdinal").GetInt64());
        Assert.Equal(3, rows[0].GetProperty("file").GetProperty("endOrdinalExclusive").GetInt64());
        Assert.Equal(3, rows[1].GetProperty("file").GetProperty("nextOrdinal").GetInt64());
        Assert.Equal(5, rows[1].GetProperty("file").GetProperty("endOrdinalExclusive").GetInt64());
        Assert.All(rows, row => Assert.Equal(JsonValueKind.Null, row.GetProperty("http").ValueKind));
        using var detailResponse = await host.SendAsync(HttpMethod.Get, "/etl/runs/csv-connector", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = await ReadJsonAsync(detailResponse);
        Assert.Equal(4, detail.RootElement.GetProperty("sourceRecordCount").GetInt64());
        Assert.Equal("csv", RequiredString(detail.RootElement.GetProperty("connector"), "sourceKind"));
        Assert.Equal("jsonl", RequiredString(detail.RootElement.GetProperty("connector"), "errorKind"));
        AssertConnectorProjection(detail.RootElement);
    }

    [Fact]
    public async Task Connector_api_distinguishes_captured_input_and_acknowledged_outputs_from_accepted_progress()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync();
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Routes = new(new EtlHttpDestination("output"), new EtlJsonlDestination("errors"))
        };
        var scope = new EtlScope("tenant-a", "partition-a");
        var admission = await SeedConnectorAsync(host, "owed-progress", scope, pipeline);
        await using var services = host.Host.Services.CreateAsyncScope();
        var store = services.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
        var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(scope,
            admission.Invocation.RunId, "private-worker-identity"));
        var position = Assert.IsType<EtlConnectorHttpPosition>(lease.Position);
        var page = new EtlRetainedHttpPage(0, EtlRuntimeIdentity.HttpRequestHash(position.Request),
            new("private-response-body", EtlRuntimeFixtures.Hash, 23), 3, "private-next-cursor");
        var attached = await store.AttachTestHttpPageAsync(lease, page);
        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        lease = attached.Lease!;
        var prepared = (await store.FindConnectorRunAsync(scope, admission.Invocation.RunId))!.Prepared!;
        var range = new EtlConnectorHttpBatchRange(0, page.RequestHash, page.Body.ContentHash, 0, 2);
        var batchId = EtlRuntimeIdentity.ConnectorBatchId(admission, prepared, lease.PartitionOrdinal, range, 7, EtlRuntimeFixtures.Hash);
        var batch = new EtlConnectorBatchDescriptor(batchId, range, 7, EtlRuntimeFixtures.Hash);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        EtlConnectorRouteOutput Output(EtlOutputRoute route, EtlConnectorKind kind) => new(kind,
            new($"private-{route}-body", EtlRuntimeFixtures.Hash, 19), 1,
            EtlRuntimeIdentity.ConnectorEffectId(admission, batchId, route, EtlRuntimeFixtures.Hash, 19, 1));
        var main = Output(EtlOutputRoute.Main, EtlConnectorKind.Http);
        var errors = Output(EtlOutputRoute.Error, EtlConnectorKind.Jsonl);
        Assert.Equal(EtlStoreStatus.Applied,
            (await store.RetainConnectorBatchOutputAsync(lease, batchId, new(1, 1, 0, main, errors))).Status);

        using (var retained = await ReadConnectorPartitionAsync(host, "owed-progress"))
        {
            var row = retained.RootElement.GetProperty("items")[0];
            Assert.Equal(3, row.GetProperty("http").GetProperty("pageRecordCount").GetInt32());
            Assert.Equal("captured", RequiredString(row.GetProperty("http").GetProperty("fetch"), "phase"));
            Assert.Equal("retained", RequiredString(row.GetProperty("owedBatch").GetProperty("main"), "state"));
            Assert.Equal("retained", RequiredString(row.GetProperty("owedBatch").GetProperty("errors"), "state"));
            AssertConnectorProjection(row);
        }
        var begin = await store.BeginConnectorHttpDeliveryAsync(lease, batchId, EtlOutputRoute.Main, Guid.NewGuid().ToString("N"));
        Assert.Equal(EtlStoreStatus.Applied, begin.Status);
        Assert.Equal(EtlStoreStatus.Applied, (await store.CompleteConnectorHttpDeliveryAsync(lease, batchId,
            EtlOutputRoute.Main, begin.Permit!, new(204, null))).Status);
        Assert.Equal(EtlStoreStatus.Applied, await store.RecordConnectorFilePublicationAsync(lease, batchId,
            EtlOutputRoute.Error, errors.Body with { Key = EtlRuntimeIdentity.ConnectorFileOutputKey(errors.EffectId) }));
        using (var owed = await ReadConnectorPartitionAsync(host, "owed-progress"))
        {
            var row = owed.RootElement.GetProperty("items")[0];
            Assert.Equal(0, row.GetProperty("http").GetProperty("nextItem").GetInt32());
            Assert.Equal(0, row.GetProperty("acceptedMainEffects").GetInt64());
            Assert.Equal(0, row.GetProperty("acceptedErrorEffects").GetInt64());
            var output = row.GetProperty("owedBatch");
            Assert.True(output.GetProperty("outputRetained").GetBoolean());
            Assert.Equal("acknowledged", RequiredString(output.GetProperty("main"), "state"));
            Assert.Equal(204, output.GetProperty("main").GetProperty("retry").GetProperty("lastStatusCode").GetInt32());
            Assert.Equal("published", RequiredString(output.GetProperty("errors"), "state"));
            AssertConnectorProjection(row);
        }
        await AssertConnectorEffectCountsAsync(host, "owed-progress", 0);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AcceptConnectorBatchAsync(lease, batchId)).Status);
        Assert.Equal(EtlStoreStatus.Replay, (await store.AcceptConnectorBatchAsync(lease, batchId)).Status);
        using (var accepted = await ReadConnectorPartitionAsync(host, "owed-progress"))
        {
            var row = accepted.RootElement.GetProperty("items")[0];
            Assert.Equal(2, row.GetProperty("http").GetProperty("nextItem").GetInt32());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("owedBatch").ValueKind);
            Assert.Equal(1, row.GetProperty("acceptedMainEffects").GetInt64());
            Assert.Equal(1, row.GetProperty("acceptedErrorEffects").GetInt64());
            AssertConnectorProjection(row);
        }
        await AssertConnectorEffectCountsAsync(host, "owed-progress", 1);
    }

    [Fact]
    public async Task Connector_rejection_before_admission_keeps_terminal_diagnostics_visible_without_invented_progress()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync();
        var admission = EtlRuntimeFixtures.ConnectorAdmission("not-admitted", new("tenant-a", "partition-a"));
        await using (var services = host.Host.Services.CreateAsyncScope())
        {
            var store = services.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            Assert.Equal(EtlStoreStatus.Applied, (await store.RejectAdmissionAsync(admission.Invocation,
                new("etl.binding-unavailable"))).Status);
        }
        using var response = await host.SendAsync(HttpMethod.Get, "/etl/runs/not-admitted", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var detail = await ReadJsonAsync(response);
        Assert.Equal(2, detail.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("etl.binding-unavailable", RequiredString(detail.RootElement.GetProperty("diagnostic"), "code"));
        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("connector").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("bindingAliases").ValueKind);
        using var page = await ReadConnectorPartitionAsync(host, "not-admitted");
        Assert.Empty(page.RootElement.GetProperty("items").EnumerateArray());
    }

    private static async Task<JsonDocument> ReadConnectorPartitionAsync(EtlDesignApiTestHost host, string runId)
    {
        using var response = await host.SendAsync(HttpMethod.Get, $"/etl/runs/{runId}/connector-partitions", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task AssertConnectorEffectCountsAsync(EtlDesignApiTestHost host, string runId, long expected)
    {
        using var response = await host.SendAsync(HttpMethod.Get, $"/etl/runs/{runId}", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var detail = await ReadJsonAsync(response);
        var connector = detail.RootElement.GetProperty("connector");
        Assert.Equal(expected, connector.GetProperty("acceptedMainEffects").GetInt64());
        Assert.Equal(expected, connector.GetProperty("acceptedErrorEffects").GetInt64());
        AssertConnectorProjection(detail.RootElement);
    }

    private static async Task<EtlConnectorRunAdmission> SeedConnectorAsync(EtlDesignApiTestHost host,
        string runId, EtlScope scope, EtlPipelineV2? pipeline = null, EtlConnectorPreparedRun? prepared = null)
    {
        var admission = EtlRuntimeFixtures.ConnectorAdmission(runId, scope, pipeline,
            pipeline?.Source is EtlCsvSource ? [] : ["private-shard-one", "private-shard-two", "private-shard-three"]);
        await using var services = host.Host.Services.CreateAsyncScope();
        var store = services.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        var lease = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(scope, "api-test-preparer"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(lease, prepared ?? new(null, null)));
        return admission;
    }

    private static void AssertConnectorProjection(JsonElement element)
    {
        AssertSafeRunView(element);
        AssertNoForbiddenProperties(element, new(StringComparer.OrdinalIgnoreCase)
        {
            "request", "requestId", "requestHash", "shardId", "continuation", "nextContinuation", "body",
            "uri", "url", "headers", "credentials", "owner", "bindings", "descriptor", "delivery"
        });
        var json = element.GetRawText();
        foreach (var marker in new[] { "private-shard", "private-worker", "source-handle", "output-handle", "errors-handle", "private-response", "private-next-cursor", "private-Main", "private-Error" })
            Assert.DoesNotContain(marker, json, StringComparison.Ordinal);
    }
}
