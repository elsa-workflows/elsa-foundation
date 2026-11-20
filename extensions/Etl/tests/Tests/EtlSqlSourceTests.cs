using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;
using Elsa.Etl.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1198: authoring rules, snapshot preparation, recovery and processing of the SQL source, on SQLite.</summary>
public sealed class EtlSqlSourceTests : IDisposable
{
    private static readonly EtlScope Scope = new("tenant", "sql-source");
    private static readonly EtlColumn[] Columns =
        [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, true), new("score", EtlColumnType.Decimal, false),
            new("active", EtlColumnType.Boolean, false)];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "etl-sql-source-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteEtlSqlEngine _engine = new();

    public EtlSqlSourceTests()
    {
        Directory.CreateDirectory(_root);
        Execute("CREATE TABLE people (id INTEGER NOT NULL PRIMARY KEY, name TEXT NULL, score DECIMAL_TEXT NULL, active BOOLEAN NULL, ratio REAL NULL)");
    }

    private string ConnectionString => $"Data Source={Path.Combine(_root, "source.db")};Pooling=False";
    private string SnapshotRoot => Path.Combine(_root, "snapshots");

    // Existing artifact bytes and identities are pinned by the golden hashes in EtlV2ArtifactIdentityTests.
    [Fact]
    public void A_sql_source_compiles_and_round_trips()
    {
        var artifact = Assert.IsType<EtlArtifact>(Compile(Pipeline()).Artifact);

        var read = Assert.IsType<EtlSqlSource>(EtlArtifactV2Json.Read(artifact.Pipeline).Source);
        Assert.Equal(("people-db", "main.people"), (read.Binding, read.Table));
        Assert.Equal(["id"], read.Key);
        Assert.Equal(Columns, read.Columns);
        Assert.Contains("\"kind\":\"sql\"", artifact.Pipeline.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("people;drop", "source.sql-identifier-invalid")]
    [InlineData("a.b.c", "source.sql-identifier-invalid")]
    [InlineData("1people", "source.sql-identifier-invalid")]
    [InlineData("p234567890123456789012345678901234567890123456789012345678901234", "source.sql-identifier-invalid")]
    public void A_table_that_is_not_an_identifier_is_refused(string table, string code) =>
        AssertRejected(Pipeline(Source(table: table)), code);

    [Fact]
    public void Columns_must_be_identifiers_distinct_ignoring_case_and_the_key_required_declared_columns()
    {
        AssertRejected(Pipeline(Source(columns: [new("id", EtlColumnType.Int64, true), new("full name", EtlColumnType.String, true)])),
            "source.sql-identifier-invalid");
        AssertRejected(Pipeline(Source(columns: [new("id", EtlColumnType.Int64, true), new("ID", EtlColumnType.String, true)])),
            "source.sql-identifier-invalid");
        AssertRejected(Pipeline(Source(key: [])), "source.sql-key-invalid");
        AssertRejected(Pipeline(Source(key: ["id", "id"])), "source.sql-key-invalid");
        AssertRejected(Pipeline(Source(key: ["missing"])), "source.sql-key-invalid");
        AssertRejected(Pipeline(Source(key: ["score"])), "source.sql-key-invalid");
        var longest = "c" + new string('x', 62);
        Assert.NotNull(Compile(Pipeline(Source(table: "p" + new string('x', 62),
            columns: [new("id", EtlColumnType.Int64, true), new(longest, EtlColumnType.String, false)]))).Artifact);
        AssertRejected(Pipeline(Source(columns: [new("id", EtlColumnType.Int64, true), new(longest + "x", EtlColumnType.String, false)])),
            "source.sql-identifier-invalid");
        var nine = Enumerable.Range(0, 9).Select(index => new EtlColumn($"k{index}", EtlColumnType.Int64, true)).ToArray();
        AssertRejected(Pipeline(Source(columns: nine, key: nine.Select(column => column.Name).ToArray())), "source.sql-key-invalid");
        Assert.NotNull(Compile(Pipeline(Source(columns: nine, key: nine.Take(8).Select(column => column.Name).ToArray()))).Artifact);
        Assert.NotNull(Compile(Pipeline(Source(key: ["id", "name"]))).Artifact);
    }

    [Fact]
    public void More_columns_than_the_field_limit_are_refused()
    {
        var pipeline = Pipeline();
        pipeline = pipeline with { Limits = pipeline.Limits with { MaxFields = Columns.Length - 1 } };

        var result = Compile(pipeline);

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, item => item.Code == "schema.field-count.exceeded" && item.Path == "source.columns");
    }

    [Fact]
    public void A_watermark_must_be_a_declared_required_int64_column_and_is_omitted_when_absent()
    {
        AssertRejected(Pipeline(Source() with { Watermark = "missing" }), "source.sql-watermark-invalid");
        AssertRejected(Pipeline(Source() with { Watermark = "name" }), "source.sql-watermark-invalid");
        AssertRejected(Pipeline(Source(columns: [new("id", EtlColumnType.Int64, true), new("version", EtlColumnType.Int64, false)]) with
            { Watermark = "version" }), "source.sql-watermark-invalid");

        var watermarked = Assert.IsType<EtlArtifact>(Compile(Pipeline(Source() with { Watermark = "id" })).Artifact);
        Assert.Equal("id", Assert.IsType<EtlSqlSource>(EtlArtifactV2Json.Read(watermarked.Pipeline).Source).Watermark);
        // Without a watermark the artifact keeps spec 1198's bytes.
        Assert.DoesNotContain("watermark", Compile(Pipeline()).Artifact!.Pipeline.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_watermark_reads_only_rows_above_the_floor_and_records_the_snapshot_high_watermark()
    {
        Execute("CREATE TABLE versioned (id INTEGER NOT NULL PRIMARY KEY, name TEXT NOT NULL, version INTEGER NULL)");
        // A NULL watermark is read whatever the floor, so the run rejects it as a record error rather than skipping it.
        Execute("INSERT INTO versioned VALUES (1, 'Ada', 5), (2, 'Bram', 9), (3, 'Cleo', 7), (4, 'Dov', NULL)");
        var source = new EtlSqlSource("people-db", "main.versioned",
            [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, true), new("version", EtlColumnType.Int64, true)], ["id"])
            { Watermark = "version" };
        var preparer = new EtlConnectorPreparer(new HostFileBindings([]), SnapshotRoot) { SqlBindings = Sql("main.versioned") };

        var all = await preparer.PrepareAsync(Admission(Pipeline(source), "main.versioned"));
        var above = await preparer.PrepareAsync(Admission(Pipeline(source), "main.versioned"), default, watermarkFloor: 6);
        var none = await preparer.PrepareAsync(Admission(Pipeline(source), "main.versioned"), default, watermarkFloor: 9);

        Assert.Equal((4L, 9L), (all.Prepared!.File!.RecordCount, all.Prepared.SourceWatermark!.Value));
        Assert.Equal((3L, 9L), (above.Prepared!.File!.RecordCount, above.Prepared.SourceWatermark!.Value));
        Assert.Equal(1L, none.Prepared!.File!.RecordCount);
        Assert.Null(none.Prepared.SourceWatermark);
        Assert.Equal("{\"id\":2,\"name\":\"Bram\",\"version\":9}\n{\"id\":3,\"name\":\"Cleo\",\"version\":7}\n{\"id\":4,\"name\":\"Dov\",\"version\":null}\n",
            await File.ReadAllTextAsync(Path.Combine(SnapshotRoot, above.Prepared.File.Source.Key)));
    }

    [Theory]
    [InlineData("{\"id\":2,\"vers")]
    [InlineData("12")]
    public async Task A_snapshot_that_no_longer_parses_reports_an_integrity_failure_when_its_high_watermark_is_read(string corruptLine)
    {
        Directory.CreateDirectory(SnapshotRoot);
        var path = Path.Combine(SnapshotRoot, "corrupt.jsonl");
        await File.WriteAllTextAsync(path, "{\"id\":1,\"version\":4}\n" + corruptLine);

        await Assert.ThrowsAsync<InvalidDataException>(() => EtlSqlSnapshotReader.ReadHighWatermarkAsync(path, "version", default).AsTask());
    }

    [Fact]
    public void A_prepared_run_without_a_watermark_keeps_its_persisted_bytes()
    {
        var prepared = new EtlConnectorPreparedRun(null, null);

        Assert.Equal("{\"file\":null,\"lookup\":null}", EtlRuntimeJson.Write(prepared));
        Assert.Contains("\"sourceWatermark\":9", EtlRuntimeJson.Write(prepared with { SourceWatermark = 9 }), StringComparison.Ordinal);
    }

    [Fact]
    public void A_continuous_sql_source_is_refused() =>
        Assert.Equal("etl.continuous-source-unsupported",
            EtlContinuousRunPolicy.FindViolation(Pipeline() with { Continuous = new EtlContinuousRunPolicy(5) }));

    [Fact]
    public void A_source_binding_resolves_a_digest_free_pin_and_refuses_a_table_it_does_not_allow()
    {
        var bindings = Connector();
        var resolution = bindings.ResolveAsync(Scope, Pipeline()).AsTask().GetAwaiter().GetResult();

        Assert.Null(resolution.FailureCode);
        Assert.Equal(EtlConnectorKind.Sql, resolution.Bindings!.Source.Kind);
        Assert.Null(resolution.Bindings.Source.Revision.ExpectedSha256);
        Assert.Equal("etl.sql-table-not-allowed",
            bindings.ResolveAsync(Scope, Pipeline(Source(table: "main.other"))).AsTask().GetAwaiter().GetResult().FailureCode);
    }

    [Fact]
    public async Task Preparation_reads_the_table_in_key_order_as_one_json_lines_object_per_row()
    {
        Execute("INSERT INTO people VALUES (3, 'Cleo', '2.25', 1, NULL), (1, 'Ada', '1.5', 0, NULL), (2, NULL, NULL, NULL, NULL)");

        var result = await Preparer().PrepareAsync(Admission(Pipeline()));

        Assert.True(result.Succeeded, result.Diagnostic?.Code);
        var file = result.Prepared!.File!;
        Assert.Equal(3, file.RecordCount);
        Assert.EndsWith(".jsonl", file.Source.Key);
        Assert.Equal(
            "{\"id\":1,\"name\":\"Ada\",\"score\":1.5,\"active\":false}\n" +
            "{\"id\":2,\"name\":null,\"score\":null,\"active\":null}\n" +
            "{\"id\":3,\"name\":\"Cleo\",\"score\":2.25,\"active\":true}\n",
            await File.ReadAllTextAsync(Path.Combine(SnapshotRoot, file.Source.Key)));
    }

    [Fact]
    public async Task An_empty_table_prepares_zero_records()
    {
        var result = await Preparer().PrepareAsync(Admission(Pipeline()));

        Assert.True(result.Succeeded, result.Diagnostic?.Code);
        Assert.Equal(0, result.Prepared!.File!.RecordCount);
    }

    [Theory]
    [InlineData("ratio", EtlColumnType.Decimal, "etl.sql-column-type-unsupported")]
    [InlineData("name", EtlColumnType.Int64, "etl.sql-column-type-unsupported")]
    [InlineData("missing", EtlColumnType.String, "etl.sql-read-rejected")]
    public async Task A_column_the_declaration_cannot_hold_or_a_missing_column_fails_preparation(string column, EtlColumnType type, string code)
    {
        Execute("INSERT INTO people VALUES (1, 'Ada', '1.5', 1, 0.25)");
        var pipeline = Pipeline(Source(columns: [new("id", EtlColumnType.Int64, true), new(column, type, false)]));

        var result = await Preparer().PrepareAsync(Admission(pipeline));

        Assert.Equal(code, result.Diagnostic?.Code);
        Assert.Null(result.Prepared);
        if (code == "etl.sql-column-type-unsupported")
            Assert.Equal(column, result.Diagnostic!.Field);
    }

    [Fact]
    public async Task A_published_snapshot_is_reused_after_the_table_changed_so_a_replacement_reads_the_same_point_in_time()
    {
        Execute("INSERT INTO people VALUES (1, 'Ada', '1.5', 1, NULL)");
        var admission = Admission(Pipeline());
        var first = await Preparer().PrepareAsync(admission);

        Execute("UPDATE people SET name = 'changed'; INSERT INTO people VALUES (2, 'Bram', NULL, NULL, NULL)");
        var replacement = await Preparer().PrepareAsync(admission);

        Assert.True(replacement.Succeeded, replacement.Diagnostic?.Code);
        Assert.Equal(1, _engine.SnapshotReads);
        Assert.Equal(first.Prepared!.File!.Source, replacement.Prepared!.File!.Source);
        Assert.Equal(1, replacement.Prepared.File.RecordCount);
    }

    [Fact]
    public async Task A_snapshot_published_concurrently_by_a_lapsed_predecessor_is_adopted_rather_than_failing_the_run()
    {
        Execute("INSERT INTO people VALUES (1, 'Ada', '1.5', 1, NULL)");
        var admission = Admission(Pipeline());
        var first = await Preparer().PrepareAsync(admission);
        Execute("UPDATE people SET name = 'changed'");

        // The replacement looks before the predecessor publishes, reads a later point in time, and loses the publication.
        var replacement = new EtlConnectorPreparer(new HostFileBindings([]), SnapshotRoot, new MissFirstLookupFactory()) { SqlBindings = Sql() };
        var adopted = await replacement.PrepareAsync(admission);

        Assert.True(adopted.Succeeded, adopted.Diagnostic?.Code);
        Assert.Equal(first.Prepared!.File!.Source, adopted.Prepared!.File!.Source);
        Assert.Equal(2, _engine.SnapshotReads);
    }

    [Fact]
    public async Task A_sql_pin_that_carries_a_digest_is_refused_at_preparation()
    {
        var admission = Admission(Pipeline());
        var digested = admission with
        {
            Bindings = admission.Bindings with
            {
                Source = admission.Bindings.Source with { Revision = admission.Bindings.Source.Revision with { ExpectedSha256 = new string('a', 64) } }
            }
        };

        Assert.Equal("etl.binding-unavailable", (await Preparer().PrepareAsync(digested)).Diagnostic?.Code);
        Assert.Equal(0, _engine.SnapshotReads);
    }

    [Fact]
    public async Task Non_ascii_text_is_retained_as_raw_utf8_not_escaped()
    {
        Execute("INSERT INTO people VALUES (1, 'Привет ☃', NULL, NULL, NULL)");

        var file = (await Preparer().PrepareAsync(Admission(Pipeline()))).Prepared!.File!;

        Assert.Equal("{\"id\":1,\"name\":\"Привет ☃\",\"score\":null,\"active\":null}\n",
            await File.ReadAllTextAsync(Path.Combine(SnapshotRoot, file.Source.Key)));
    }

    [Fact]
    public async Task A_transient_read_failure_records_nothing_and_the_next_attempt_prepares()
    {
        Execute("INSERT INTO people VALUES (1, 'Ada', '1.5', 1, NULL)");
        var admission = Admission(Pipeline());
        _engine.FailNextSnapshot(EtlSqlErrorKind.Transient);

        var failed = await Preparer().PrepareAsync(admission);
        Assert.True(failed.Transient);
        Assert.IsType<SqliteEtlSqlEngine.InjectedDbException>(failed.TransientCause);
        Assert.Empty(Directory.Exists(SnapshotRoot) ? Directory.GetFiles(SnapshotRoot, "*.jsonl") : []);

        var retried = await Preparer().PrepareAsync(admission);
        Assert.True(retried.Succeeded, retried.Diagnostic?.Code);
        Assert.Equal(1, retried.Prepared!.File!.RecordCount);
    }

    [Fact]
    public async Task A_rejected_read_or_a_missing_table_fails_preparation()
    {
        var admission = Admission(Pipeline());
        _engine.FailNextSnapshot(EtlSqlErrorKind.Rejected);
        Assert.Equal("etl.sql-read-rejected", (await Preparer().PrepareAsync(admission)).Diagnostic?.Code);

        Execute("DROP TABLE people");
        Assert.Equal("etl.sql-read-rejected", (await Preparer().PrepareAsync(admission)).Diagnostic?.Code);
    }

    [Fact]
    public async Task A_provider_failure_outside_the_db_exception_family_is_a_rejection_not_an_endless_retry()
    {
        _engine.FailNextSnapshot(new NotSupportedException("An unmapped provider type."));

        var result = await Preparer().PrepareAsync(Admission(Pipeline()));

        Assert.Equal("etl.sql-read-rejected", result.Diagnostic?.Code);
    }

    [Fact]
    public void A_sql_source_with_a_null_column_or_key_entry_is_not_a_readable_artifact()
    {
        var document = Compile(Pipeline()).Artifact!.Pipeline.GetRawText();

        Assert.Throws<System.Text.Json.JsonException>(() => EtlArtifactV2Json.Read(
            System.Text.Json.JsonDocument.Parse(document.Replace("\"columns\":[", "\"columns\":[null,", StringComparison.Ordinal)).RootElement));
        Assert.Throws<System.Text.Json.JsonException>(() => EtlArtifactV2Json.Read(
            System.Text.Json.JsonDocument.Parse(document.Replace("\"key\":[", "\"key\":[null,", StringComparison.Ordinal)).RootElement));
    }

    [Fact]
    public async Task A_snapshot_beyond_the_snapshot_limit_fails_preparation()
    {
        Execute("INSERT INTO people VALUES (1, 'Ada', '1.5', 1, NULL), (2, 'Bram', '1.5', 1, NULL)");
        var pipeline = Pipeline();
        pipeline = pipeline with { Limits = pipeline.Limits with { MaxSnapshotBytes = 64, MaxLookupBytes = 64 } };

        Assert.Equal("etl.snapshot-too-large", (await Preparer().PrepareAsync(Admission(pipeline))).Diagnostic?.Code);
    }

    [Fact]
    public async Task A_prepared_snapshot_is_processed_as_json_lines_and_a_required_null_is_a_record_error()
    {
        Execute("INSERT INTO people VALUES (1, 'Ada', '1.5', 1, NULL), (2, NULL, NULL, NULL, NULL), (3, 'Cleo', NULL, 0, NULL)");
        var admission = Admission(Pipeline());
        var prepared = (await Preparer().PrepareAsync(admission)).Prepared!;

        await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(SnapshotRoot, admission, prepared, 0);
        var batch = (await builder.BuildNextAsync(new EtlConnectorFilePosition(1, 4, 1))).Batch!;

        Assert.Equal([1L, 2L, 3L], batch.Records.Select(record => record.Index));
        Assert.Equal([null, "etl.required-value-missing", null], batch.Records.Select(record => record.Error?.Code));
        Assert.Equal("Ada", batch.Records[0].Row!.Values["name"].Value);
        Assert.Equal(1.5m, batch.Records[0].Row!.Values["score"].Value);
        Assert.Equal(false, batch.Records[2].Row!.Values["active"].Value);
    }

    [Fact]
    public async Task A_change_committed_after_preparation_does_not_reach_the_batches()
    {
        Execute("INSERT INTO people VALUES (1, 'Ada', NULL, NULL, NULL), (2, 'Bram', NULL, NULL, NULL)");
        var admission = Admission(Pipeline());
        var prepared = (await Preparer().PrepareAsync(admission)).Prepared!;

        // After the preparation and before any partition runs.
        Execute("UPDATE people SET name = 'changed'; DELETE FROM people WHERE id = 2; INSERT INTO people VALUES (3, 'Cleo', NULL, NULL, NULL)");
        await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(SnapshotRoot, admission, prepared, 0);
        var batch = (await builder.BuildNextAsync(new EtlConnectorFilePosition(1, 3, 1))).Batch!;

        Assert.Equal(["Ada", "Bram"], batch.Records.Select(record => (string)record.Row!.Values["name"].Value!));
    }

    [Fact]
    public async Task A_row_beyond_the_record_limit_is_a_record_error_not_a_failed_preparation()
    {
        Execute($"INSERT INTO people VALUES (1, 'Ada', NULL, NULL, NULL), (2, '{new string('x', 200)}', NULL, NULL, NULL)");
        var pipeline = Pipeline();
        pipeline = pipeline with { Limits = pipeline.Limits with { MaxRecordBytes = 128 } };
        var admission = Admission(pipeline);
        var prepared = (await Preparer().PrepareAsync(admission)).Prepared!;

        await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(SnapshotRoot, admission, prepared, 0);
        var batch = (await builder.BuildNextAsync(new EtlConnectorFilePosition(1, 3, 1))).Batch!;

        Assert.Equal([null, "etl.record-too-large"], batch.Records.Select(record => record.Error?.Code));
    }

    [Fact]
    public async Task A_snapshot_that_no_longer_matches_its_recorded_hash_is_refused()
    {
        Execute("INSERT INTO people VALUES (1, 'Ada', '1.5', 1, NULL)");
        var admission = Admission(Pipeline());
        var prepared = (await Preparer().PrepareAsync(admission)).Prepared!;
        var tampered = prepared with { File = prepared.File! with { Source = prepared.File.Source with { ContentHash = new string('0', 64) } } };

        Assert.Equal("etl.snapshot-integrity-failed", (await Assert.ThrowsAsync<EtlConnectorInputBatchBuilderException>(() =>
            EtlConnectorInputBatchBuilder.OpenAsync(SnapshotRoot, admission, tampered, 0).AsTask())).Diagnostic.Code);
        // A SQL pin never carries a digest; one that does is not a SQL admission.
        var digested = admission with
        {
            Bindings = admission.Bindings with
            {
                Source = admission.Bindings.Source with { Revision = admission.Bindings.Source.Revision with { ExpectedSha256 = prepared.File!.Source.ContentHash } }
            }
        };
        Assert.Equal("etl.binding-unavailable", (await Assert.ThrowsAsync<EtlConnectorInputBatchBuilderException>(() =>
            EtlConnectorInputBatchBuilder.OpenAsync(SnapshotRoot, digested, prepared, 0).AsTask())).Diagnostic.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private EtlConnectorPreparer Preparer() => new(new HostFileBindings([]), SnapshotRoot) { SqlBindings = Sql() };

    private HostSqlBindings Sql(string table = "main.people") => new([new EtlHostSqlBindingOptions
    {
        TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "people-db", Role = "Source", Revision = "v1",
        Provider = "sqlite", ConnectionString = ConnectionString, AllowedTables = [table]
    }], [_engine]);

    private HostConnectorBindings Connector() => new(new HostFileBindings([
        new HostFileBinding(Scope, "output", EtlFileRole.MainOutput, "v1", Path.Combine(_root, "output"), true, AtomicPublication: true),
        new HostFileBinding(Scope, "errors", EtlFileRole.ErrorOutput, "v1", Path.Combine(_root, "errors"), true, AtomicPublication: true)
    ]), new HostHttpBindings([]), 8, Sql());

    private EtlConnectorRunAdmission Admission(EtlPipelineV2 pipeline, string table = "main.people")
    {
        var artifact = Compile(pipeline).Artifact ?? EtlArtifactFor(pipeline);
        var invocation = EtlRuntimeFixtures.Admission("sql-source-" + Guid.NewGuid().ToString("N"), Scope).Invocation with { Artifact = artifact };
        var resolved = Sql(table).ResolveCurrent(Scope, "people-db", "Source", table);
        return new EtlConnectorRunAdmission(invocation, new EtlConnectorRunBindings(
            new EtlConnectorBindingRevision(EtlConnectorKind.Sql, new EtlBindingRevision(resolved.Alias, resolved.Revision, resolved.Handle, null)),
            null,
            new EtlConnectorBindingRevision(EtlConnectorKind.Jsonl, new EtlBindingRevision("output", "v1", "output-handle", null)),
            new EtlConnectorBindingRevision(EtlConnectorKind.Jsonl, new EtlBindingRevision("errors", "v1", "errors-handle", null)), []));
    }

    // Some refusal tests prepare pipelines the compiler would refuse; the preparer must still check them itself.
    private static EtlArtifact EtlArtifactFor(EtlPipelineV2 pipeline)
    {
        var document = EtlArtifactV2Json.Write(pipeline);
        var hash = EtlArtifactJson.Hash(document);
        return new EtlArtifact(EtlArtifactV2Json.FormatVersion,
            EtlArtifactJson.CreateArtifactId(EtlArtifactV2Json.FormatVersion, "orders", 1, hash), hash, "orders", 1, document);
    }

    private static EtlSqlSource Source(string table = "main.people", EtlColumn[]? columns = null, string[]? key = null) =>
        new("people-db", table, columns ?? Columns, key ?? ["id"]);

    private static EtlPipelineV2 Pipeline(EtlSqlSource? source = null)
    {
        var baseline = EtlV2TestPipelines.Create();
        var declared = source ?? Source();
        return baseline with
        {
            Source = declared,
            Lookup = null,
            Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")),
            Mapping = declared.Columns.Where(column => column is not null)
                .Select(column => new EtlOutputField(column.Name, EtlFieldOrigin.Source, column.Name)).ToArray(),
            Filters = [],
            Limits = baseline.Limits with { FilePartitionCount = 1, MaxBatchRecords = 10 }
        };
    }

    private static EtlCompilationResult Compile(EtlPipelineV2 pipeline) => new EtlArtifactCompiler().Compile("orders", 1, pipeline);

    private static void AssertRejected(EtlPipelineV2 pipeline, string code)
    {
        var result = Compile(pipeline);
        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, item => item.Code == code);
    }

    /// <summary>Publishes normally, but the first lookup misses, as when a predecessor publishes after the replacement looked.</summary>
    private sealed class MissFirstLookupFactory : IFileChunkPublisherFactory
    {
        private readonly FileChunkPublisherFactory _inner = new();

        public IFileChunkPublisher Create(EtlFileChunkFormat format) =>
            format == EtlFileChunkFormat.JsonlSnapshot ? new MissFirstLookup(_inner.Create(format)) : _inner.Create(format);
    }

    private sealed class MissFirstLookup(IFileChunkPublisher inner) : IFileChunkPublisher
    {
        private int _lookups;

        public ValueTask<EtlChunk> PublishAsync(string outputRoot, string effectIdentity, long maxBytes,
            Func<Stream, CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            inner.PublishAsync(outputRoot, effectIdentity, maxBytes, write, cancellationToken);

        public ValueTask<EtlChunk?> FindAsync(string outputRoot, string effectIdentity, long maxBytes, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _lookups) == 1 ? ValueTask.FromResult<EtlChunk?>(null) : inner.FindAsync(outputRoot, effectIdentity, maxBytes, cancellationToken);
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
