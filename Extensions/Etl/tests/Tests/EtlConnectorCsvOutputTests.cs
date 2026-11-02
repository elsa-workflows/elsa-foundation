using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Transformation;
using Elsa.Etl.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1191: CSV route bodies through the real output processor and store.</summary>
public sealed partial class EtlRuntimeStoreTests
{
    private static readonly string[] CsvOutputFields = ["id", "name"];

    [Fact]
    public async Task Csv_routes_retain_header_and_rows_then_publish_verified_csv_files()
    {
        await using var fixture = await CsvOutputFixture.StartAsync(this, long.MaxValue / 4,
            Accepted(1, 1, "Ada, \"the first\""), Error(2, "etl.invalid-value", "name"), Accepted(3, 3, "Bram"));

        var prepared = await fixture.TransformAsync();

        Assert.True(prepared.Succeeded, prepared.FailureCode);
        var output = prepared.Output!;
        Assert.Equal((EtlConnectorKind.Csv, EtlConnectorKind.Csv), (output.Main!.Kind, output.Errors!.Kind));
        Assert.Equal("id,name\r\n1,\"Ada, \"\"the first\"\"\"\r\n3,Bram\r\n", await fixture.RetainedAsync(output.Main));
        Assert.Equal("recordOrdinal,code,field\r\n2,etl.invalid-value,name\r\n", await fixture.RetainedAsync(output.Errors));
        var row = await fixture.Context.ConnectorBatches.AsNoTracking().SingleAsync();
        // The effect columns hold the delivery mechanism: a CSV file publication shares JSONL's value 2.
        Assert.Equal((2, 2), (row.MainEffectKind, row.ErrorEffectKind));

        var dispatch = await fixture.DispatchAsync(output);

        Assert.True(dispatch.ReadyToAccept, dispatch.FailureCode);
        Assert.Equal(await fixture.RetainedAsync(output.Main), await File.ReadAllTextAsync(fixture.Published(EtlOutputRoute.Main, output.Main)));
        Assert.Equal(await fixture.RetainedAsync(output.Errors), await File.ReadAllTextAsync(fixture.Published(EtlOutputRoute.Error, output.Errors)));
        Assert.EndsWith(".csv", fixture.Published(EtlOutputRoute.Main, output.Main));
        var published = await fixture.Context.ConnectorBatches.AsNoTracking().SingleAsync();
        Assert.True(published.MainFilePublished && published.ErrorFilePublished);
        Assert.True((await fixture.DispatchAsync(output)).ReadyToAccept); // Republishing the same effect is idempotent.
        Assert.Single(Directory.EnumerateFiles(fixture.MainRoot));
        Assert.Equal(EtlStoreStatus.Applied, (await fixture.AcceptAsync()).Status);
    }

    [Theory]
    [InlineData(0, 2, 0)]
    [InlineData(1, 1, 1)]
    public async Task Csv_row_bound_counts_the_header_and_rejects_the_row_that_does_not_fit(int overBy, long accepted, long rejected)
    {
        var wide = new string('b', 120);
        var header = EtlCsvRouteWriter.Header(CsvOutputFields).Length;
        var row1 = CsvRow(1, "Ada").Length;
        var row2 = CsvRow(2, wide).Length;
        var rejection = EtlCsvRouteWriter.ErrorHeader().Length + EtlCsvRouteWriter.ErrorRow(new(2, null, "etl.output-too-large")).Length;
        Assert.True(rejection < row2);
        // At the limit both rows fit exactly; one byte less leaves row 2 out, with room for its size rejection.
        var limit = header + row1 + row2 - overBy;
        await using var fixture = await CsvOutputFixture.StartAsync(this, limit, Accepted(1, 1, "Ada"), Accepted(2, 2, wide));

        var prepared = await fixture.TransformAsync();

        Assert.True(prepared.Succeeded, prepared.FailureCode);
        Assert.Equal((accepted, rejected), (prepared.Output!.AcceptedRecords, prepared.Output.RejectedRecords));
        if (rejected == 1)
            Assert.Equal("recordOrdinal,code,field\r\n2,etl.output-too-large,\r\n", await fixture.RetainedAsync(prepared.Output.Errors!));
    }

    [Fact]
    public async Task A_rejected_first_row_writes_no_header_and_the_next_fitting_row_carries_it()
    {
        var wide = new string('w', 200);
        var rejection = EtlCsvRouteWriter.ErrorHeader().Length + EtlCsvRouteWriter.ErrorRow(new(1, null, "etl.output-too-large")).Length;
        var limit = rejection + EtlCsvRouteWriter.Header(CsvOutputFields).Length + CsvRow(2, "Bo").Length;
        await using var fixture = await CsvOutputFixture.StartAsync(this, limit, Accepted(1, 1, wide), Accepted(2, 2, "Bo"));

        var prepared = await fixture.TransformAsync();

        Assert.True(prepared.Succeeded, prepared.FailureCode);
        Assert.Equal("id,name\r\n2,Bo\r\n", await fixture.RetainedAsync(prepared.Output!.Main!));
        Assert.Equal(1, prepared.Output.RejectedRecords);
    }

    [Fact]
    public async Task A_size_rejection_whose_error_row_cannot_fit_fails_the_batch()
    {
        await using var fixture = await CsvOutputFixture.StartAsync(this, 8, Accepted(1, 1, "Ada Lovelace"));

        var prepared = await fixture.TransformAsync();

        Assert.False(prepared.Succeeded);
        Assert.Equal("etl.output-too-large", prepared.FailureCode);
    }

    [Fact]
    public async Task A_row_whose_json_exceeds_the_allowance_is_accepted_when_its_csv_fits()
    {
        var accented = new string('é', 60); // 120 UTF-8 bytes in CSV; the JSON writer escapes each é as \u00e9 (6 bytes, 360 in all).
        var limit = EtlCsvRouteWriter.Header(CsvOutputFields).Length + CsvRow(1, accented).Length;
        Assert.True(limit < 200);
        await using var fixture = await CsvOutputFixture.StartAsync(this, limit, Accepted(1, 1, accented));

        var prepared = await fixture.TransformAsync();

        Assert.True(prepared.Succeeded, prepared.FailureCode);
        Assert.Equal((1L, 0L), (prepared.Output!.AcceptedRecords, prepared.Output.RejectedRecords));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_http_error_array_reserves_its_closing_bracket_when_a_file_route_fills_the_limit(bool jsonlMain)
    {
        const string errorJson = "{\"recordOrdinal\":1,\"code\":\"etl.invalid-value\"}";
        const string jsonlRow = "{\"id\":2,\"name\":\"Ada\"}\n";
        var mainBytes = jsonlMain ? jsonlRow.Length : EtlCsvRouteWriter.Header(CsvOutputFields).Length + CsvRow(2, "Ada").Length;
        var limit = 1 + errorJson.Length + 1 + mainBytes;
        await using var fixture = await CsvOutputFixture.StartAsync(this, limit, httpErrors: true,
            mainKind: jsonlMain ? EtlConnectorKind.Jsonl : EtlConnectorKind.Csv, Error(1, "etl.invalid-value", null), Accepted(2, 2, "Ada"));

        var prepared = await fixture.TransformAsync();

        Assert.True(prepared.Succeeded, prepared.FailureCode);
        Assert.Equal("[" + errorJson + "]", await fixture.RetainedAsync(prepared.Output!.Errors!));
        Assert.Equal(jsonlMain ? jsonlRow : "id,name\r\n2,Ada\r\n", await fixture.RetainedAsync(prepared.Output.Main!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_file_row_that_would_take_the_http_error_arrays_closing_bracket_is_rejected_to_that_array(bool jsonlMain)
    {
        // Without the reservation the file row would fit, and the array's ']' would land one byte past the limit.
        const string errorJson = "{\"recordOrdinal\":1,\"code\":\"etl.invalid-value\"}";
        var wide = new string('w', 100);
        var jsonlRow = "{\"id\":2,\"name\":\"" + wide + "\"}\n";
        var mainBytes = jsonlMain ? jsonlRow.Length : EtlCsvRouteWriter.Header(CsvOutputFields).Length + CsvRow(2, wide).Length;
        var limit = 1 + errorJson.Length + mainBytes;
        await using var fixture = await CsvOutputFixture.StartAsync(this, limit, httpErrors: true,
            mainKind: jsonlMain ? EtlConnectorKind.Jsonl : EtlConnectorKind.Csv, Error(1, "etl.invalid-value", null), Accepted(2, 2, wide));

        var prepared = await fixture.TransformAsync();

        Assert.True(prepared.Succeeded, prepared.FailureCode);
        Assert.Equal(0, prepared.Output!.AcceptedRecords);
        Assert.Null(prepared.Output.Main);
        var errors = await fixture.RetainedAsync(prepared.Output.Errors!);
        Assert.StartsWith("[" + errorJson + ",{\"recordOrdinal\":2,", errors);
        Assert.Contains("\"etl.output-too-large\"", errors);
        Assert.EndsWith("]", errors);
        Assert.True(Encoding.UTF8.GetByteCount(errors) <= limit);
    }

    [Fact]
    public async Task An_http_main_first_row_that_fills_the_allowance_is_rejected_not_the_batch()
    {
        // The first JSON array element carries its reserved closing bracket, so a row whose JSON exactly fills the
        // allowance is rejected to the (CSV) error route instead of failing the whole batch on append.
        var name = new string('a', 80);
        var rowJson = "{\"id\":1,\"name\":\"" + name + "\"}";
        var rejection = EtlCsvRouteWriter.ErrorHeader().Length + EtlCsvRouteWriter.ErrorRow(new(1, "name", "etl.output-too-large")).Length;
        Assert.True(rowJson.Length + 1 > rejection);
        // The transformer's JSONL line is the object plus LF, so this limit is exactly what the line occupies.
        await using var fixture = await CsvOutputFixture.StartAsync(this, rowJson.Length + 1, httpErrors: false,
            mainKind: EtlConnectorKind.Http, Accepted(1, 1, name));

        var prepared = await fixture.TransformAsync();

        Assert.True(prepared.Succeeded, prepared.FailureCode);
        Assert.Equal((0L, 1L), (prepared.Output!.AcceptedRecords, prepared.Output.RejectedRecords));
        Assert.Contains("etl.output-too-large", await fixture.RetainedAsync(prepared.Output.Errors!));
    }

    [Theory]
    [InlineData("tampered-body", "etl.output-read-conflict")]
    [InlineData("conflicting-file", "etl.effect-conflict")]
    public async Task Csv_publication_refuses_a_changed_body_or_a_different_existing_file(string variant, string code)
    {
        await using var fixture = await CsvOutputFixture.StartAsync(this, long.MaxValue / 4, Accepted(1, 1, "Ada"));
        var output = (await fixture.TransformAsync()).Output!;
        if (variant == "tampered-body")
        {
            var path = Path.Combine(fixture.SnapshotRoot, output.Main!.Body.Key);
            File.SetAttributes(path, FileAttributes.Normal);
            await File.WriteAllTextAsync(path, "id,name\r\n1,Eve\r\n");
        }
        else
        {
            Directory.CreateDirectory(fixture.MainRoot);
            await File.WriteAllTextAsync(fixture.Published(EtlOutputRoute.Main, output.Main!), "id,name\r\n1,Eve\r\n");
        }

        var dispatch = await fixture.DispatchAsync(output);

        Assert.False(dispatch.ReadyToAccept);
        Assert.Equal(code, dispatch.FailureCode);
        Assert.False((await fixture.Context.ConnectorBatches.AsNoTracking().SingleAsync()).MainFilePublished);
        if (variant == "tampered-body")
            Assert.False(Directory.Exists(fixture.MainRoot) && Directory.EnumerateFiles(fixture.MainRoot).Any());
    }

    [Fact]
    public async Task A_csv_route_stored_with_the_http_mechanism_is_refused_on_read()
    {
        await using var fixture = await CsvOutputFixture.StartAsync(this, long.MaxValue / 4, Accepted(1, 1, "Ada"));
        Assert.True((await fixture.TransformAsync()).Succeeded);
        await fixture.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_etl_connector_batches SET MainEffectKind = 1");

        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Inspector.ListConnectorPartitionsAsync(
            fixture.Admission.Invocation.Scope, fixture.Admission.Invocation.RunId, -1, 1));
    }

    private static byte[] CsvRow(long id, string name) =>
        EtlCsvRouteWriter.RecordRow(Encoding.UTF8.GetBytes($"{{\"id\":{id},\"name\":{System.Text.Json.JsonSerializer.Serialize(name)}}}\n"), 2);

    private static EtlConnectorInputRecord Accepted(long index, long id, string name) => new(index, 5,
        new EtlTypedRow((int)index, new Dictionary<string, EtlTypedValue>
        {
            ["id"] = new(EtlColumnType.Int64, id), ["name"] = new(EtlColumnType.String, name)
        }), null);

    private static EtlConnectorInputRecord Error(long index, string code, string? field) =>
        new(index, 5, null, new EtlRowError((int)index, field, code));

    /// <summary>A prepared one-partition run whose routes are CSV files (or an HTTP error array) under one temp root.</summary>
    private sealed class CsvOutputFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "elsa-etl-csv-output-" + Guid.NewGuid().ToString("N"));
        private TestDatabase? _database;
        private EtlHttpLoopbackServer? _endpoint;
        private EtlHttpTransport? _transport;
        private EtlConnectorOutputProcessor _processor = null!;
        private EtlPipelineV2 _pipeline = null!;
        private EtlConnectorPartitionLease _lease = null!;
        private EtlConnectorInputBatch _input = null!;
        private IEtlConnectorRuntimeStore _store = null!;

        public string SnapshotRoot => Path.Combine(_root, "snapshots");
        public string MainRoot => Path.Combine(_root, "main");
        public string ErrorRoot => Path.Combine(_root, "errors");
        public EtlRuntimeSqliteDbContext Context { get; private set; } = null!;
        public IEtlConnectorRuntimeInspector Inspector => (IEtlConnectorRuntimeInspector)_store;
        public EtlConnectorRunAdmission Admission { get; private set; } = null!;

        public static Task<CsvOutputFixture> StartAsync(EtlRuntimeStoreTests _, long outputLimit,
            params EtlConnectorInputRecord[] records) => StartAsync(_, outputLimit, httpErrors: false, records);

        public static Task<CsvOutputFixture> StartAsync(EtlRuntimeStoreTests _, long outputLimit, bool httpErrors,
            params EtlConnectorInputRecord[] records) => StartAsync(_, outputLimit, httpErrors, EtlConnectorKind.Csv, records);

        public static async Task<CsvOutputFixture> StartAsync(EtlRuntimeStoreTests _, long outputLimit, bool httpErrors,
            EtlConnectorKind mainKind, params EtlConnectorInputRecord[] records)
        {
            var fixture = new CsvOutputFixture();
            await fixture.InitializeAsync(outputLimit, httpErrors, mainKind, records);
            return fixture;
        }

        private async Task InitializeAsync(long outputLimit, bool httpErrors, EtlConnectorKind mainKind, EtlConnectorInputRecord[] records)
        {
            Directory.CreateDirectory(_root);
            var scope = new EtlScope("tenant-csv", "csv-output");
            var fileBindings = new HostFileBindings([
                new HostFileBinding(scope, "output", EtlFileRole.MainOutput, "destination-revision-1", MainRoot, IsCurrent: true, AtomicPublication: true),
                new HostFileBinding(scope, "errors", EtlFileRole.ErrorOutput, "destination-revision-1", ErrorRoot, IsCurrent: true, AtomicPublication: true)
            ]);
            _endpoint = await EtlHttpLoopbackServer.StartAsync((_, _) => ValueTask.FromResult(new EtlHttpLoopbackResponse(204, "")));
            var httpBindings = new HostHttpBindings([new EtlHostHttpBindingOptions
            {
                TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "errors", Role = "ErrorOutput",
                Revision = "destination-revision-1", Endpoint = _endpoint.CreateUri("/errors").AbsoluteUri, AllowedPaths = ["/errors"], AllowHttp = true
            }, new EtlHostHttpBindingOptions
            {
                TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "output", Role = "MainOutput",
                Revision = "destination-revision-1", Endpoint = _endpoint.CreateUri("/output").AbsoluteUri, AllowedPaths = ["/output"], AllowHttp = true
            }]);
            var baseline = CsvMixedRoutePipeline();
            var maxOutput = Math.Min(outputLimit, baseline.Limits.MaxRetainedOutputBytes);
            _pipeline = baseline with
            {
                Source = new EtlCsvSource("source", [new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("name", EtlColumnType.String, true)]),
                Routes = new(mainKind switch
                    {
                        EtlConnectorKind.Http => new EtlHttpDestination("output"),
                        EtlConnectorKind.Jsonl => new EtlJsonlDestination("output"),
                        _ => new EtlCsvDestination("output")
                    },
                    httpErrors ? new EtlHttpDestination("errors") : new EtlCsvDestination("errors")),
                Mapping = [new EtlOutputField("id", EtlFieldOrigin.Source, "id"), new EtlOutputField("name", EtlFieldOrigin.Source, "name")],
                Limits = baseline.Limits with
                {
                    FilePartitionCount = 1, MaxPartitions = 1, MaxBatchRecords = records.Length,
                    MaxOutputBatchBytes = maxOutput
                }
            };
            var mainPin = fileBindings.Resolve(scope, "output", EtlFileRole.MainOutput);
            var errorPin = fileBindings.Resolve(scope, "errors", EtlFileRole.ErrorOutput);
            Admission = ConnectorAdmission("connector-csv-output", scope, _pipeline);
            await using var httpPin = await httpBindings.ResolveCurrentAsync(scope, "errors", "ErrorOutput");
            await using var httpMainPin = await httpBindings.ResolveCurrentAsync(scope, "output", "MainOutput");
            Admission = Admission with
            {
                Bindings = Admission.Bindings with
                {
                    Main = mainKind == EtlConnectorKind.Http
                        ? new(EtlConnectorKind.Http, new("output", httpMainPin.Revision, httpMainPin.Handle, null))
                        : new(mainKind, new("output", mainPin.Revision, mainPin.Handle, null)),
                    Errors = httpErrors
                        ? new(EtlConnectorKind.Http, new("errors", httpPin.Revision, httpPin.Handle, null))
                        : new(EtlConnectorKind.Csv, new("errors", errorPin.Revision, errorPin.Handle, null))
                }
            };

            _database = await TestDatabase.CreateAsync();
            Context = _database.CreateContext();
            _store = (IEtlConnectorRuntimeStore)_database.CreateStore(Context);
            _lease = Assert.Single(await StartOutputCsvRunAsync(_store, Admission, recordCount: records.Length));
            var run = Assert.IsType<EtlConnectorRunView>(await _store.FindConnectorRunAsync(scope, Admission.Invocation.RunId));
            var batch = OutputTestBatch(Admission, run.Prepared!, _lease, recordCount: records.Length);
            var reserved = await _store.ReserveConnectorBatchAsync(_lease, batch);
            Assert.Equal(EtlStoreStatus.Applied, reserved.Status);
            _lease = _lease with { OwedBatch = reserved.Reservation };
            _input = new EtlConnectorInputBatch(_lease.PartitionOrdinal, batch, records);
            _transport = new EtlHttpTransport(TimeProvider.System);
            _processor = new EtlConnectorOutputProcessor(_store, fileBindings, httpBindings, new TestChunkPublisherFactory(),
                _transport, SnapshotRoot);
        }

        public async Task<EtlConnectorOutputPreparationResult> TransformAsync()
        {
            var prepared = await _processor.TransformAndRetainAsync(_lease, Admission, _pipeline, _input);
            if (prepared.Output is { } output)
                _lease = _lease with { OwedBatch = _lease.OwedBatch! with { Output = output } };
            return prepared;
        }

        public ValueTask<EtlConnectorRouteDispatchResult> DispatchAsync(EtlConnectorBatchOutput output) =>
            _processor.DispatchAsync(_lease, Admission, _pipeline, output);

        public async Task<EtlConnectorPositionResult> AcceptAsync() =>
            await _store.AcceptConnectorBatchAsync(_lease, _lease.OwedBatch!.Batch.BatchId);

        public async Task<string> RetainedAsync(EtlConnectorRouteOutput route)
        {
            var (extension, prefix) = route.Kind switch
            {
                EtlConnectorKind.Csv => (".csv", "etl-connector-csv-route-body-v1:"),
                EtlConnectorKind.Jsonl => (".jsonl", "etl-connector-jsonl-route-body-v1:"),
                _ => (".json", "etl-connector-http-route-body-v1:")
            };
            var bytes = await new FileChunkStore(extension).ReadAsync(SnapshotRoot, prefix + route.EffectId,
                _pipeline.Limits.MaxOutputBatchBytes, route.Body.ByteLength);
            return Encoding.UTF8.GetString(Assert.IsType<byte[]>(bytes));
        }

        public string Published(EtlOutputRoute route, EtlConnectorRouteOutput output) => Path.Combine(
            route == EtlOutputRoute.Main ? MainRoot : ErrorRoot, EtlRuntimeIdentity.ConnectorFileOutputKey(output.EffectId, output.Kind));

        public async ValueTask DisposeAsync()
        {
            _transport?.Dispose();
            if (_endpoint is not null) await _endpoint.DisposeAsync();
            if (Context is not null) await Context.DisposeAsync();
            if (_database is not null) await _database.DisposeAsync();
            if (!Directory.Exists(_root)) return;
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
    }
}
