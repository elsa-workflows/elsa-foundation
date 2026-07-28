using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class GroundworkOpenTelemetryStoreTests : IAsyncLifetime
{
    private readonly OpenTelemetryGroundworkSqliteFixture _fixture = new();

    [Fact]
    public void Tenant_scope_and_source_are_all_part_of_non_aliasable_stream_identity()
    {
        var bindings = new[]
        {
            GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-a"),
            GroundworkOpenTelemetryBinding.Create("tenant-b", "shell-a", "collector-a"),
            GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-b", "collector-a"),
            GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-b")
        };

        Assert.Empty(typeof(GroundworkOpenTelemetryBinding).GetConstructors());
        var streams = bindings.SelectMany(binding =>
            new[] { binding.TraceStreamId, binding.SpanStreamId, binding.MetricPointStreamId, binding.LogStreamId }).ToArray();
        Assert.Equal(streams.Length, streams.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("ténant", "scope")]
    [InlineData("tenant", " scope")]
    [InlineData("tenant", "scope ")]
    public void Binding_rejects_identifiers_outside_the_portable_diagnostic_domain(
        string tenantId,
        string scopeId)
    {
        Assert.Throws<ArgumentException>(() =>
            GroundworkOpenTelemetryBinding.Create(tenantId, scopeId, "collector"));
    }

    [Fact]
    public void Binding_rejects_diagnostic_identifiers_over_sixty_four_bytes()
    {
        Assert.Throws<ArgumentException>(() =>
            GroundworkOpenTelemetryBinding.Create(new string('t', 65), "scope", "collector"));
        Assert.Throws<ArgumentException>(() =>
            GroundworkOpenTelemetryBinding.Create("tenant", new string('s', 65), "collector"));
    }

    [Fact]
    public async Task Trace_capacity_above_the_grouped_union_contract_fails_before_provider_work()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var options = Options.Create(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = OpenTelemetryRecordStreamDefinitions.MaxTraceRecordCapacity + 1
        });

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GroundworkOpenTelemetryStore(providers, options, _fixture.Binding));

        Assert.Equal("options", exception.ParamName);
        Assert.Contains(
            OpenTelemetryRecordStreamDefinitions.MaxTraceRecordCapacity.ToString(),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Document_session_must_match_the_explicit_tenant_scope_and_source_binding()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var wrongSource = GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-b");

        Assert.Throws<ArgumentException>(() => new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            wrongSource));
    }

    [Fact]
    public async Task Capture_before_explicit_lifecycle_start_is_rejected()
    {
        var providers = await _fixture.CreateProvidersAsync();
        await using var store = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            _fixture.Binding);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.WriteAsync(CreateBatch(includeCatalogs: false)).AsTask());

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((0, 0, 0, 0),
            (diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Lifecycle_stop_before_start_is_terminal_without_retention_io()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var traces = new RecordingTrimStore(providers.Traces);
        var store = new GroundworkOpenTelemetryStore(
            providers with { Traces = traces },
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            _fixture.Binding);

        await ((IDiagnosticsPersistenceDrain)store).StopAsync();

        Assert.Empty(traces.Requests);
        Assert.Throws<InvalidOperationException>(store.Start);
        await store.DisposeAsync();
    }

    [Fact]
    public async Task Disposal_before_start_is_terminal_without_retention_io()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var traces = new RecordingTrimStore(providers.Traces);
        var store = new GroundworkOpenTelemetryStore(
            providers with { Traces = traces },
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            _fixture.Binding);

        await store.DisposeAsync();

        Assert.Empty(traces.Requests);
        Assert.Throws<InvalidOperationException>(store.Start);
    }

    [Fact]
    public async Task Partial_multi_stream_retry_after_restart_reuses_batch_identity_and_does_not_duplicate_records()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var traces = new ObservingRecordStore(providers.Traces);
        var points = new ObservingRecordStore(providers.MetricPoints, failFirstAppend: true);
        var store = _fixture.CreateStore(providers with
        {
            Traces = traces,
            MetricPoints = points
        });
        var batchId = DiagnosticsDrainBatchId.New();
        var batch = CreateBatch(includeCatalogs: false);

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceUnavailableException>(() =>
            store.WriteAsync(batchId, batch).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.ProviderFailure, failure.Reason);
        Assert.Equal("write", failure.Operation);
        Assert.Equal(batchId.ToString(), failure.Context["batchId"]);
        Assert.IsType<IOException>(failure.InnerException);
        var partial = await store.GetDiagnosticsAsync();
        Assert.Equal(1, partial.TraceCount);
        Assert.Equal(1, partial.SpanCount);
        Assert.Equal(0, partial.MetricPointCount);
        Assert.Equal(0, partial.LogRecordCount);

        var restartedProviders = await _fixture.CreateProvidersAsync();
        var restartedTraces = new ObservingRecordStore(restartedProviders.Traces);
        var restartedPoints = new ObservingRecordStore(restartedProviders.MetricPoints);
        var restarted = _fixture.CreateStore(restartedProviders with
        {
            Traces = restartedTraces,
            MetricPoints = restartedPoints
        });
        await restarted.WriteAsync(batchId, batch);

        var completed = await restarted.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (completed.TraceCount, completed.SpanCount, completed.MetricPointCount, completed.LogRecordCount));
        Assert.Single(traces.Requests);
        Assert.Empty(restartedTraces.Requests);
        Assert.Equal([DiagnosticAppendStatus.Committed], traces.Outcomes);
        Assert.Empty(restartedTraces.Outcomes);
        Assert.Single(points.Requests);
        Assert.Single(restartedPoints.Requests);
        Assert.Equal(points.Requests[0].OperationId, restartedPoints.Requests[0].OperationId);
        Assert.Equal(points.Requests[0].RequestFingerprint, restartedPoints.Requests[0].RequestFingerprint);
        Assert.Empty(points.Outcomes);
        Assert.Equal([DiagnosticAppendStatus.Committed], restartedPoints.Outcomes);

        Assert.True((await CaptureOperationsAsync(restartedProviders)).Single().Version > 1);
    }

    [Fact]
    public async Task Identical_independent_captures_do_not_collapse()
    {
        var store = await _fixture.CreateStoreAsync();
        var batch = CreateBatch(includeCatalogs: false);

        await store.WriteAsync(DiagnosticsDrainBatchId.New(), batch);
        await store.WriteAsync(DiagnosticsDrainBatchId.New(), batch);

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((2, 2, 2, 2),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Capture_write_returns_before_provider_io_completes()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var traces = new BlockingRecordStore(providers.Traces);
        var store = _fixture.CreateStore(providers with { Traces = traces });

        var write = store.WriteAsync(CreateBatch(includeCatalogs: false));
        await traces.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(write.IsCompletedSuccessfully);

        traces.Release();
        await store.CompleteDrainingAsync();
        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Graceful_stop_applies_final_signal_retention_and_reports_durable_deletions()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var counters = new DiagnosticsPersistenceCounters();
        var store = _fixture.CreateStore(
            providers,
            options: new()
            {
                TraceCapacity = 1,
                SpanCapacity = 1,
                MetricPointCapacity = 1,
                LogRecordCapacity = 1
            },
            observer: counters);

        await store.WriteAsync(CreateBatch(includeCatalogs: false));
        await store.WriteAsync(CreateBatch(includeCatalogs: false));

        await store.CompleteDrainingAsync();

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
        var snapshot = counters.Snapshot();
        Assert.Equal(DiagnosticsDrainState.Stopped, snapshot.State);
        Assert.Equal(4, snapshot.Losses[DiagnosticsPersistenceLossReason.DurableRetentionDeletion]);
    }

    [Fact]
    public async Task Final_retention_replays_the_same_operation_after_acknowledgement_loss()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var traces = new TrimAcknowledgementLossRecordStore(providers.Traces);
        var counters = new DiagnosticsPersistenceCounters();
        var store = _fixture.CreateStore(
            providers with { Traces = traces },
            options: new()
            {
                TraceCapacity = 1,
                SpanCapacity = 1,
                MetricPointCapacity = 1,
                LogRecordCapacity = 1
            },
            observer: counters);

        await store.WriteAsync(CreateBatch(includeCatalogs: false));
        await store.WriteAsync(CreateBatch(includeCatalogs: false));

        await store.CompleteDrainingAsync();

        Assert.Equal(2, traces.TrimRequests.Count);
        Assert.Equal(traces.TrimRequests[0].OperationId, traces.TrimRequests[1].OperationId);
        Assert.Equal(1, counters.Snapshot().RetentionRetries);
        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Capture_marks_sources_only_when_the_drain_accepts_the_batch()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var registry = new RecordingSourceRegistry();
        var counters = new DiagnosticsPersistenceCounters();
        var store = _fixture.CreateStore(providers, sourceRegistry: registry, observer: counters);
        var batch = CreateBatch();

        await store.WriteAsync(batch);

        Assert.Equal(batch.Resources, registry.Seen);
        await store.CompleteDrainingAsync();

        await store.WriteAsync(batch);

        Assert.Equal(batch.Resources, registry.Seen);
        Assert.Equal(1, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.WriteAfterClosure]);
        Assert.Equal(1, (await store.QueryTracesAsync(new())).DroppedCount);
        Assert.Equal(1, (await store.QueryMetricsAsync(new())).DroppedCount);
        Assert.Equal(1, (await store.QueryLogsAsync(new())).DroppedCount);
        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((1L, 1L, 1L, 1L),
            (diagnostics.DroppedTraceCount, diagnostics.DroppedSpanCount,
                diagnostics.DroppedMetricPointCount, diagnostics.DroppedLogRecordCount));
    }

    [Fact]
    public async Task Reusing_batch_identity_for_different_canonical_input_fails_without_mutation()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batchId = DiagnosticsDrainBatchId.New();
        var first = CreateBatch(includeCatalogs: false);
        await store.WriteAsync(batchId, first);
        var changed = first with
        {
            Logs = first.Logs.Select(x => x with { Body = $"{x.Body}-changed" }).ToArray()
        };

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceConflictException>(() =>
            store.WriteAsync(batchId, changed).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.ConflictingOperation, failure.Reason);
        Assert.Equal(batchId.ToString(), failure.Context["batchId"]);
        Assert.IsType<DiagnosticOperationConflictException>(failure.InnerException);

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Catalogs_are_part_of_the_batch_fingerprint_and_instrument_time_is_retry_stable()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var store = _fixture.CreateStore(providers, clock);
        var batchId = new DiagnosticsDrainBatchId(Guid.NewGuid(), clock.GetUtcNow());
        var batch = CreateBatch();

        await store.WriteAsync(batchId, batch);
        clock.Advance(TimeSpan.FromMinutes(5));
        await store.WriteAsync(batchId, batch);

        var changed = batch with
        {
            Resources = batch.Resources
                .Select(resource => resource with { ServiceName = $"{resource.ServiceName}-changed" })
                .ToArray()
        };
        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceConflictException>(() =>
            store.WriteAsync(batchId, changed).AsTask());

        Assert.IsType<DiagnosticOperationConflictException>(failure.InnerException);
        var resource = Assert.Single((await store.QueryResourcesAsync(new())).Items);
        Assert.DoesNotContain("-changed", resource.ServiceName, StringComparison.Ordinal);
        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1, 1, 1),
            (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Expired_pending_stream_attempt_fails_explicitly_after_restart_without_duplicate_or_late_progress()
    {
        var clock = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers with
        {
            MetricPoints = new ObservingRecordStore(providers.MetricPoints, failFirstAppend: true)
        }, clock);
        var batchId = DiagnosticsDrainBatchId.New();
        var batch = CreateBatch(includeCatalogs: false);
        var appendFailure = await Assert.ThrowsAsync<OpenTelemetryPersistenceUnavailableException>(() =>
            store.WriteAsync(batchId, batch).AsTask());
        Assert.IsType<IOException>(appendFailure.InnerException);
        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(1));

        var restartedProviders = await _fixture.CreateProvidersAsync();
        var restarted = _fixture.CreateStore(restartedProviders, clock);
        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceExpiredException>(() =>
            restarted.WriteAsync(batchId, batch).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.ExpiredOperation, failure.Reason);
        Assert.Equal(batchId.ToString(), failure.Context["batchId"]);
        Assert.IsType<DiagnosticOperationExpiredException>(failure.InnerException);

        var diagnostics = await restarted.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 0, 0),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Catalog_writes_use_physical_entities_and_unfiltered_declared_routes()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);

        await store.WriteDurablyAsync(CreateBatch());

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((1, 1), (diagnostics.ResourceCount, diagnostics.MetricInstrumentCount));
        Assert.Single((await store.QueryResourcesAsync(new())).Items);
        Assert.Single((await store.QueryResourcesAsync(new() { Status = TelemetryResourceStatus.Active })).Items);
        Assert.Single((await store.QueryMetricsAsync(new())).Instruments);
    }

    [Fact]
    public async Task Supported_case_insensitive_record_reads_reach_the_bounded_provider_route()
    {
        var store = await _fixture.CreateStoreAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.QueryResourcesAsync(
            new() { ServiceName = "API" }, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.QueryTracesAsync(
            new(), cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetTraceAsync(
            "TRACE-1", cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.QueryMetricsAsync(
            new() { ResourceId = "resource-1" }, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.QueryLogsAsync(
            new() { TraceId = "TRACE-1" }, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Oversized_normalized_stream_batch_is_rejected_before_ledger_or_record_mutation()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batch = CreateBatch(includeCatalogs: false);
        var template = Assert.Single(batch.Logs);
        batch = batch with
        {
            Traces = [],
            Spans = [],
            MetricPoints = [],
            Logs = Enumerable.Range(0, 1_001)
                .Select(index => template with { Id = $"log-{index:D4}" })
                .ToArray()
        };

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceValidationException>(() =>
            store.WriteDurablyAsync(batch).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.InvalidRecord, failure.Reason);
        Assert.IsType<DiagnosticRecordValidationException>(failure.InnerException);

        await AssertNoMutationAsync(store, providers);
    }

    [Fact]
    public async Task Provider_invalid_normalized_record_is_rejected_before_ledger_or_record_mutation()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batch = CreateBatch(includeCatalogs: false);
        batch = batch with
        {
            Traces = [],
            Spans = [],
            MetricPoints = [],
            Logs = batch.Logs.Select(x => x with { SeverityText = new string('s', 4_097) }).ToArray()
        };

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceValidationException>(() =>
            store.WriteDurablyAsync(batch).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.InvalidRecord, failure.Reason);
        Assert.IsType<DiagnosticRecordValidationException>(failure.InnerException);

        await AssertNoMutationAsync(store, providers);
    }

    [Fact]
    public async Task Canonical_payload_failures_are_translated_but_caller_argument_failures_remain_argument_exceptions()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batch = CreateBatch(includeCatalogs: false);
        var log = Assert.Single(batch.Logs);
        var invalidPayload = batch with
        {
            Logs = [log with { Attributes = null! }]
        };

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceValidationException>(() =>
            store.WriteDurablyAsync(invalidPayload).AsTask());
        Assert.IsType<RecordPayloadException>(failure.InnerException);

        var conflictingInput = batch with
        {
            Logs = [log, log with { Body = "different" }]
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteDurablyAsync(conflictingInput).AsTask());

        await AssertNoMutationAsync(store, providers);
    }

    [Fact]
    public async Task Concurrent_same_batch_writers_converge_through_capture_ledger_cas()
    {
        var first = _fixture.CreateStore(await _fixture.CreateProvidersAsync());
        var second = _fixture.CreateStore(await _fixture.CreateProvidersAsync());
        var batchId = DiagnosticsDrainBatchId.New();
        var batch = CreateBatch(includeCatalogs: false);

        await Task.WhenAll(
            first.WriteAsync(batchId, batch).AsTask(),
            second.WriteAsync(batchId, batch).AsTask());

        var diagnostics = await first.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Cancellation_after_one_committed_stream_records_partial_progress_and_retry_resumes()
    {
        using var cancellation = new CancellationTokenSource();
        var providers = await _fixture.CreateProvidersAsync();
        var traces = new ObservingRecordStore(providers.Traces, afterSuccessfulAppend: cancellation.Cancel);
        var store = _fixture.CreateStore(providers with { Traces = traces });
        var batchId = DiagnosticsDrainBatchId.New();
        var batch = CreateBatch(includeCatalogs: false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.WriteAsync(batchId, batch, cancellation.Token).AsTask());

        var restarted = _fixture.CreateStore(await _fixture.CreateProvidersAsync());
        await restarted.WriteAsync(batchId, batch);
        var diagnostics = await restarted.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Malformed_capture_ledger_is_reported_as_provider_neutral_corrupt_data()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batchId = DiagnosticsDrainBatchId.New();
        var result = await providers.Documents.SaveAsync(new(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
            batchId.ToString(),
            OpenTelemetryGroundworkStorageSchema.SchemaVersion,
            JsonSerializer.Serialize(new { createdAt = DateTimeOffset.UtcNow }),
            0));
        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceDataException>(() =>
            store.WriteAsync(batchId, CreateBatch(includeCatalogs: false)).AsTask());

        Assert.Equal(OpenTelemetryPersistenceFailureReason.CorruptData, failure.Reason);
        Assert.Equal(batchId.ToString(), failure.Context["batchId"]);
        Assert.IsType<DocumentSchemaVersionException>(failure.InnerException);
    }

    [Fact]
    public async Task Malformed_catalog_payload_is_reported_as_provider_neutral_corrupt_data()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var resource = CreateBatch().Resources.Single() with { Id = "resource-corrupt" };
        var request = new CatalogDocumentSerializer().ToSaveRequest(resource);
        var content = JsonNode.Parse(request.ContentJson)!.AsObject();
        content.Remove("attributes");
        var result = await providers.Documents.SaveAsync(request with
        {
            ContentJson = content.ToJsonString()
        });
        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceDataException>(() =>
            store.QueryResourcesAsync(new() { Take = 10 }).AsTask());

        Assert.Equal(OpenTelemetryPersistenceFailureReason.CorruptData, failure.Reason);
        Assert.Equal("query-resources", failure.Operation);
        Assert.IsType<CatalogPayloadException>(failure.InnerException);
    }

    [Fact]
    public async Task Null_capture_stream_attempt_is_reported_as_provider_neutral_corrupt_data()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batchId = DiagnosticsDrainBatchId.New();
        var content = JsonSerializer.Serialize(new
        {
            ledgerSchemaVersion = 2,
            batchId = batchId.ToString(),
            fingerprint = "persisted-fingerprint",
            createdAt = DateTimeOffset.UtcNow,
            tenantId = _fixture.Binding.TenantId,
            scopeId = _fixture.Binding.ScopeId,
            sourceId = _fixture.Binding.SourceId,
            streams = new Dictionary<string, object?> { ["traces"] = null }
        });
        var result = await providers.Documents.SaveAsync(new(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
            batchId.ToString(),
            OpenTelemetryGroundworkStorageSchema.SchemaVersion,
            content,
            0));
        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceDataException>(() =>
            store.WriteAsync(batchId, CreateBatch(includeCatalogs: false)).AsTask());

        Assert.Equal(OpenTelemetryPersistenceFailureReason.CorruptData, failure.Reason);
        Assert.Equal(batchId.ToString(), failure.Context["batchId"]);
    }

    [Fact]
    public async Task Query_and_diagnostics_provider_failures_are_translated_at_the_public_boundary()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var queryFailure = new IOException("Injected query failure.");
        var inspectFailure = new IOException("Injected inspect failure.");
        var store = _fixture.CreateStore(providers with
        {
            Logs = new ObservingRecordStore(providers.Logs, queryFailure: queryFailure),
            Traces = new ObservingRecordStore(providers.Traces, inspectFailure: inspectFailure)
        });

        var queryError = await Assert.ThrowsAsync<OpenTelemetryPersistenceUnavailableException>(() =>
            store.QueryLogsAsync(new()).AsTask());
        Assert.Equal("query-logs", queryError.Operation);
        Assert.Same(queryFailure, queryError.InnerException);

        var diagnosticsError = await Assert.ThrowsAsync<OpenTelemetryPersistenceUnavailableException>(() =>
            store.GetDiagnosticsAsync().AsTask());
        Assert.Equal("get-diagnostics", diagnosticsError.Operation);
        Assert.Same(inspectFailure, diagnosticsError.InnerException);
    }

    private static async Task<IReadOnlyList<DocumentEnvelope>> CaptureOperationsAsync(GroundworkOpenTelemetryStores providers) =>
        (await providers.DocumentQueries.QueryAsync(new DocumentQuery(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
            OpenTelemetryGroundworkStorageSchema.CaptureOperationsByCreatedAtQuery,
            take: 100))).Documents;

    private static async Task AssertNoMutationAsync(
        GroundworkOpenTelemetryStore store,
        GroundworkOpenTelemetryStores providers)
    {
        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((0, 0, 0, 0, 0, 0),
            (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
        Assert.Empty(await CaptureOperationsAsync(providers));
    }

    private static OpenTelemetryBatch CreateBatch(bool includeCatalogs = true)
    {
        var timestamp = new DateTimeOffset(2026, 7, 14, 1, 0, 0, TimeSpan.Zero);
        var resource = new TelemetryResource(
            "resource-1", "api", "api-1", "dotnet", new Dictionary<string, string?>(), timestamp,
            TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace(
            "trace-1", "span-root", "request", timestamp, timestamp.AddMilliseconds(10),
            TimeSpan.FromMilliseconds(10), SpanStatus.Ok, [resource.Id], [], 1);
        var span = new TelemetrySpan(
            "span-record-1", trace.TraceId, "span-1", null, resource.Id, "request", "internal",
            timestamp, timestamp.AddMilliseconds(10), SpanStatus.Ok, null,
            new Dictionary<string, string?>(), [], []);
        var instrument = new MetricInstrument(
            "instrument-1", resource.Id, "request.duration", "ms", null, MetricKind.Gauge,
            new Dictionary<string, string?>());
        var point = new MetricPoint(
            "point-1", instrument.Id, instrument.Name, resource.Id, timestamp, 10, null, null,
            new Dictionary<string, string?>(), trace.TraceId, span.SpanId);
        var log = new OtlpLogRecord(
            "log-1", resource.Id, timestamp, "Information", null, "request completed", trace.TraceId,
            span.SpanId, new Dictionary<string, string?>());
        return new(
            includeCatalogs ? [resource] : [],
            [trace],
            [span],
            includeCatalogs ? [instrument] : [],
            [point],
            [log]);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private sealed class ObservingRecordStore(
        IDiagnosticRecordStore inner,
        bool failFirstAppend = false,
        Action? afterSuccessfulAppend = null,
        Exception? queryFailure = null,
        Exception? inspectFailure = null) : IDiagnosticRecordStore
    {
        private int _failNext = failFirstAppend ? 1 : 0;

        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;
        public List<DiagnosticRecordBatch> Requests { get; } = [];
        public List<DiagnosticAppendStatus> Outcomes { get; } = [];

        public async ValueTask<DiagnosticAppendResult> AppendAsync(
            DiagnosticRecordBatch batch,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(batch);
            if (Interlocked.Exchange(ref _failNext, 0) != 0)
                throw new IOException("Injected metric append failure after earlier streams committed.");

            var result = await inner.AppendAsync(batch, cancellationToken);
            Outcomes.Add(result.Status);
            afterSuccessfulAppend?.Invoke();
            return result;
        }

        public ValueTask<DiagnosticRecordPage> QueryAsync(
            DiagnosticRecordQuery query,
            CancellationToken cancellationToken = default) =>
            queryFailure is null
                ? inner.QueryAsync(query, cancellationToken)
                : ValueTask.FromException<DiagnosticRecordPage>(queryFailure);

        public ValueTask<DiagnosticStreamStatistics> InspectAsync(
            DiagnosticStreamInspectionRequest request,
            CancellationToken cancellationToken = default) =>
            inspectFailure is null
                ? inner.InspectAsync(request, cancellationToken)
                : ValueTask.FromException<DiagnosticStreamStatistics>(inspectFailure);
    }

    private sealed class BlockingRecordStore(IDiagnosticRecordStore inner) : IDiagnosticRecordStore
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<DiagnosticAppendResult> AppendAsync(
            DiagnosticRecordBatch batch,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return await inner.AppendAsync(batch, cancellationToken);
        }

        public ValueTask<DiagnosticRecordPage> QueryAsync(
            DiagnosticRecordQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public ValueTask<DiagnosticStreamStatistics> InspectAsync(
            DiagnosticStreamInspectionRequest request,
            CancellationToken cancellationToken = default) =>
            inner.InspectAsync(request, cancellationToken);

        public void Release() => _release.TrySetResult();
    }

    private sealed class TrimAcknowledgementLossRecordStore(IDiagnosticRecordStore inner) : IDiagnosticRecordStore
    {
        private int _loseNextAcknowledgement = 1;

        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;
        public List<DiagnosticTrimRequest> TrimRequests { get; } = [];

        public async ValueTask<DiagnosticTrimResult> TrimAsync(
            DiagnosticTrimRequest request,
            CancellationToken cancellationToken = default)
        {
            TrimRequests.Add(request);
            var result = await inner.TrimAsync(request, cancellationToken);
            if (Interlocked.Exchange(ref _loseNextAcknowledgement, 0) != 0)
                throw new IOException("Injected acknowledgement loss after the trace trim committed.");
            return result;
        }
    }

    private sealed class RecordingTrimStore(IDiagnosticRecordStore inner) : IDiagnosticRecordStore
    {
        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;
        public List<DiagnosticTrimRequest> Requests { get; } = [];

        public async ValueTask<DiagnosticTrimResult> TrimAsync(
            DiagnosticTrimRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return await inner.TrimAsync(request, cancellationToken);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed class RecordingSourceRegistry : IOpenTelemetrySourceRegistry
    {
        public List<TelemetryResource> Seen { get; } = [];
        public long DroppedCount => 0;

        public void MarkSeen(TelemetryResource resource) => Seen.Add(resource);

        public IReadOnlyCollection<TelemetryResource> List() => Seen;
    }
}
