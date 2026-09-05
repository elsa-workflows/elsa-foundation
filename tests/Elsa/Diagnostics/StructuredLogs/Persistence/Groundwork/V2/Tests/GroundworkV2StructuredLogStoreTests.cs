using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkV2StructuredLogStoreTests
{
    [Fact]
    public async Task Provider_connection_registration_is_explicit_and_resolvable_by_the_feature_seam()
    {
        await using var fixture = await Fixture.CreateAsync();
        var services = new ServiceCollection();

        services.AddGroundworkStorageProviderConnection(fixture.Connection);

        var descriptor = Assert.Single(services, service =>
            service.ServiceType == typeof(IStorageProviderConnection) && !service.IsKeyedService);
        Assert.Same(fixture.Connection, descriptor.ImplementationInstance);
        var keyedDescriptor = Assert.Single(services, service =>
            service.ServiceType == typeof(IStorageProviderConnection) && service.IsKeyedService);
        Assert.Same(fixture.Connection, keyedDescriptor.KeyedImplementationInstance);
    }

    [Fact]
    public async Task Groundwork_feature_resolves_one_connection_backed_store_for_both_contracts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var services = new ServiceCollection();
        services.AddGroundworkStorageProviderConnection(fixture.Connection);
        services.AddOptions<StructuredLogsOptions>();
        new GroundworkStructuredLogsPersistenceFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        var contract = provider.GetRequiredService<IStructuredLogStore>();
        var concrete = provider.GetRequiredService<GroundworkStructuredLogStore>();

        Assert.Same(concrete, contract);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStructuredLogStore));
    }

    [Fact]
    public void Schema_declares_provider_sequence_scope_indexes_and_distinct_exact_ledgers()
    {
        var unit = StructuredLogsGroundworkStorageSchema.CreateUnit(3);

        Assert.Equal(StructuredLogsGroundworkStorageSchema.UnitId, unit.Id.Value);
        Assert.Equal(StructuredLogsGroundworkStorageSchema.UnitName, unit.Name);
        var sequence = Assert.Single(unit.Columns, column => column.Name == StructuredLogsGroundworkStorageSchema.SequenceField);
        Assert.Equal(ColumnGeneration.ProviderSequence, sequence.Generation);
        var payload = Assert.Single(unit.Columns, column => column.Name == StructuredLogsGroundworkStorageSchema.PayloadField);
        Assert.Equal(PortableType.Json, payload.Type);
        Assert.True(payload.IsNullable is false);
        Assert.Equal([StructuredLogsGroundworkStorageSchema.SequenceField], unit.Key.Columns);
        Assert.Equal(ScopePolicy.Scoped, unit.Scope);
        Assert.Equal(3, unit.Retention!.KeepNewest);
        Assert.Equal(
            StructuredLogsGroundworkStorageSchema.SequenceField,
            unit.Retention.OrderColumn);
        Assert.Equal("elsa_structured_logs_append", unit.AppendIdempotency!.LedgerName);
        Assert.Equal("elsa_structured_logs_retention", unit.RetentionIdempotency!.LedgerName);
        Assert.NotEqual(unit.AppendIdempotency.LedgerName, unit.RetentionIdempotency.LedgerName);
        Assert.Equal(
            ["elsa_structured_logs_category", "elsa_structured_logs_level", "elsa_structured_logs_replay", "elsa_structured_logs_sequence_order", "elsa_structured_logs_source"],
            unit.Indexes.Select(index => index.Name).Order(StringComparer.Ordinal));
        var order = Assert.Single(unit.Indexes, index => index.Name == "elsa_structured_logs_sequence_order");
        Assert.Equal([new IndexColumn(StructuredLogsGroundworkStorageSchema.SequenceField, SortDirection.Descending)], order.Columns);
    }

    [Fact]
    public async Task Provider_sequence_is_the_public_sequence_and_replays_authoritative_cursor()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = fixture.CreateStore();

        var first = await store.AppendAsync(Entry(999, "first"));
        var second = await store.AppendAsync(Entry(999, "second"));

        Assert.True(first.Sequence > 0);
        Assert.NotEqual(999, first.Sequence);
        Assert.True(second.Sequence > first.Sequence);
        Assert.NotEqual(first.ReplayCursor, second.ReplayCursor);
        Assert.Equal(second.Sequence, await store.GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task Scope_isolation_and_zero_retention_preserve_lifetime_high_water_without_reserved_data_rows()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var first = fixture.CreateStore(new("tenant-a", "scope-a", "structured-logs"));
        await using var second = fixture.CreateStore(new("tenant-a", "scope-b", "structured-logs"));

        var committed = await first.AppendAsync(Entry(0, "first"));
        await second.AppendAsync(Entry(0, "second"));

        Assert.Single(await first.GetRecentAsync(StructuredLogFilter.None));
        Assert.Single(await second.GetRecentAsync(StructuredLogFilter.None));
        Assert.Single(fixture.OpenSession(new("tenant-a", "scope-a", "structured-logs"))
            .Query(AllRows(StructuredLogsGroundworkStorageSchema.CreateUnit())).Rows);

        await first.TrimAsync(0);
        Assert.Empty(await first.GetRecentAsync(StructuredLogFilter.None));
        Assert.Empty(fixture.OpenSession(new("tenant-a", "scope-a", "structured-logs"))
            .Query(AllRows(StructuredLogsGroundworkStorageSchema.CreateUnit())).Rows);
        Assert.Equal(committed.Sequence, await first.GetHighWaterMarkAsync());
        Assert.Single(await second.GetRecentAsync(StructuredLogFilter.None));
    }

    [Fact]
    public async Task Stream_identity_isolated_in_scope_and_retention()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstBinding = new StructuredLogStoreBinding("tenant-a", "scope-a", "stream-a");
        var secondBinding = new StructuredLogStoreBinding("tenant-a", "scope-a", "stream-b");
        Assert.NotEqual(
            StructuredLogsGroundworkStorageSchema.ScopeFor(firstBinding),
            StructuredLogsGroundworkStorageSchema.ScopeFor(secondBinding));

        await using var first = fixture.CreateStore(firstBinding);
        await using var second = fixture.CreateStore(secondBinding);
        var firstCommitted = await first.AppendAsync(Entry(0, "first"));
        var secondCommitted = await second.AppendAsync(Entry(0, "second"));

        await first.TrimAsync(0);

        Assert.Empty(await first.GetRecentAsync(StructuredLogFilter.None));
        Assert.Equal(firstCommitted.Sequence, await first.GetHighWaterMarkAsync());
        Assert.Single(await second.GetRecentAsync(StructuredLogFilter.None));
        Assert.Equal(secondCommitted.Sequence, await second.GetHighWaterMarkAsync());
    }

    [Fact]
    public void Scope_identity_is_injective_when_binding_values_contain_delimiters()
    {
        var first = new StructuredLogStoreBinding("tenant/a", "scope", "stream");
        var second = new StructuredLogStoreBinding("tenant", "a/scope", "stream");

        Assert.NotEqual(
            StructuredLogsGroundworkStorageSchema.ScopeFor(first),
            StructuredLogsGroundworkStorageSchema.ScopeFor(second));
    }

    [Fact]
    public async Task Read_after_scans_filtered_positions_and_restart_continues_provider_sequence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = fixture.CreateStore();
        await store.AppendAsync(Entry(0, "ignored") with { SourceId = "other" });
        var selected = await store.AppendAsync(Entry(0, "selected") with { SourceId = "selected" });

        var empty = await store.ReadAfterAsync(null, new StructuredLogFilter { SourceId = "missing" }, 1);
        Assert.Empty(empty.Entries);
        Assert.NotNull(empty.NextCursor);
        Assert.True(empty.HasMore);

        var page = await store.ReadAfterAsync(empty.NextCursor, new StructuredLogFilter { SourceId = "selected" }, 1);
        Assert.Equal([selected.Sequence], page.Entries.Select(entry => entry.Sequence));

        var highWater = await store.GetHighWaterMarkAsync();
        await store.DisposeAsync();
        await fixture.ReopenAsync();
        await using var restarted = fixture.CreateStore();
        var afterRestart = await restarted.AppendAsync(Entry(0, "after restart"));
        Assert.True(afterRestart.Sequence > highWater);
    }

    [Fact]
    public async Task Canceled_append_is_refused_before_provider_work()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = fixture.CreateStore();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.AppendAsync(Entry(0, "canceled"), canceled.Token).AsTask());
        Assert.Empty(await store.GetRecentAsync(StructuredLogFilter.None));
    }

    [Fact]
    public async Task Failed_append_rolls_back_durable_row_and_lifetime_high_water_before_commit()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreateAppendFailureTriggerAsync();
        var faulting = new AcknowledgementLosingSession(fixture.OpenSession());
        var observer = new DiagnosticsPersistenceCounters();
        await using var store = fixture.CreateStore(sessionOverride: faulting, observer: observer);

        await Assert.ThrowsAsync<StructuredLogsException>(() =>
            store.AppendAsync(Entry(99, "doomed")).AsTask());

        Assert.Equal(1, observer.Snapshot().Accepted);
        Assert.True(faulting.Calls > 0, "Expected the accepted append to reach the Groundwork provider.");
        // Groundwork translates the trigger rejection to its atomic append failure,
        // rather than exposing the SQLite trigger message through the drain.
        var providerFailure = Assert.IsType<InvalidOperationException>(faulting.FirstProviderFailure);
        Assert.Contains("payload row was not accepted", providerFailure.Message, StringComparison.Ordinal);
        Assert.Equal(0, await store.GetHighWaterMarkAsync());
        var verification = fixture.OpenSession();
        Assert.Empty(verification.Query(AllRows(StructuredLogsGroundworkStorageSchema.CreateUnit())).Rows);
    }

    [Fact]
    public async Task Acknowledgement_loss_reuses_one_exact_operation_and_publishes_one_row()
    {
        await using var fixture = await Fixture.CreateAsync();
        var faulting = new AcknowledgementLosingSession(fixture.OpenSession());
        await using var store = fixture.CreateStore(sessionOverride: faulting);

        var committed = await store.AppendAsync(Entry(123, "ack-loss"));

        Assert.Equal(2, faulting.Calls);
        Assert.Single(faulting.Operations.Distinct());
        Assert.Equal(committed.Sequence, Assert.Single((await store.GetRecentAsync(StructuredLogFilter.None))).Sequence);
    }

    [Fact]
    public async Task Lifecycle_refuses_prestart_and_stops_without_accepting_new_work()
    {
        await using var fixture = await Fixture.CreateAsync();
        var unstarted = new GroundworkStructuredLogStore(
            fixture.OpenSession(),
            Options.Create(new StructuredLogsOptions()),
            StructuredLogStoreBinding.Default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unstarted.AppendAsync(Entry(0, "before-start")).AsTask());
        await unstarted.StopAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            unstarted.AppendAsync(Entry(0, "after-stop")).AsTask());
    }

    [Fact]
    public async Task Disposal_before_start_is_terminal_without_provider_work()
    {
        await using var fixture = await Fixture.CreateAsync();
        var unstarted = new GroundworkStructuredLogStore(
            fixture.OpenSession(),
            Options.Create(new StructuredLogsOptions()),
            StructuredLogStoreBinding.Default);

        await unstarted.DisposeAsync();

        Assert.Throws<InvalidOperationException>(unstarted.Start);
    }

    [Theory]
    [InlineData("stréam")]
    [InlineData(" stream")]
    [InlineData("stream ")]
    public async Task Binding_rejects_identifiers_outside_the_portable_domain(string streamId)
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Throws<ArgumentException>(() => fixture.CreateStore(new("tenant", "scope", streamId)));
    }

    [Fact]
    public async Task Binding_rejects_identifiers_over_sixty_four_code_units()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Throws<ArgumentException>(() => fixture.CreateStore(new("tenant", "scope", new string('s', 65))));
    }

    [Fact]
    public async Task Recent_query_lowers_filters_so_older_matching_rows_are_not_lost()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = fixture.CreateStore();
        await store.AppendAsync(Entry(0, "selected-1") with { SourceId = "selected" });
        await store.AppendAsync(Entry(0, "selected-2") with { SourceId = "selected" });
        for (var index = 0; index < 8; index++)
            await store.AppendAsync(Entry(0, $"other-{index}") with { SourceId = "other" });

        var recent = await store.GetRecentAsync(new StructuredLogFilter { SourceId = "selected", MaxCount = 2 });

        Assert.Equal(["selected-1", "selected-2"], recent.Select(entry => entry.Message));
    }

    [Fact]
    public async Task Recent_query_clamps_to_requested_count_after_filtering()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = fixture.CreateStore();
        await store.AppendAsync(Entry(0, "selected-1") with { SourceId = "selected" });
        await store.AppendAsync(Entry(0, "selected-2") with { SourceId = "selected" });
        await store.AppendAsync(Entry(0, "selected-3") with { SourceId = "selected" });

        var recent = await store.GetRecentAsync(new StructuredLogFilter { SourceId = "selected", MaxCount = 2 });

        Assert.Equal(["selected-2", "selected-3"], recent.Select(entry => entry.Message));
    }

    [Fact]
    public async Task Recent_query_clamps_a_large_requested_count_to_configured_maximum()
    {
        await using var fixture = await Fixture.CreateAsync();
        var options = Options.Create(new StructuredLogsOptions { MaxRecentQuerySize = 2 });
        await using var store = fixture.CreateStore(options: options);
        await store.AppendAsync(Entry(0, "one"));
        await store.AppendAsync(Entry(0, "two"));
        await store.AppendAsync(Entry(0, "three"));

        var recent = await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 100 });

        Assert.Equal(["two", "three"], recent.Select(entry => entry.Message));
    }

    [Fact]
    public async Task Recent_query_applies_the_minimum_level_to_durable_rows()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = fixture.CreateStore();
        await store.AppendAsync(Entry(0, "debug") with { Level = LogLevel.Debug });
        await store.AppendAsync(Entry(0, "warning") with { Level = LogLevel.Warning });
        await store.AppendAsync(Entry(0, "error") with { Level = LogLevel.Error });

        var recent = await store.GetRecentAsync(new StructuredLogFilter { MinimumLevel = LogLevel.Warning });

        Assert.Equal(["warning", "error"], recent.Select(entry => entry.Message));
    }

    [Fact]
    public async Task Wrong_scope_tampered_and_trimmed_cursors_are_non_disclosing()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var source = fixture.CreateStore(new("tenant-a", "scope-a", "structured-logs"));
        await using var wrongScope = fixture.CreateStore(new("tenant-a", "scope-b", "structured-logs"));
        var committed = await source.AppendAsync(Entry(0, "source"));

        var tampered = new StructuredLogReplayCursor(committed.ReplayCursor!.Value.Value + "x");
        await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            source.ReadAfterAsync(tampered, StructuredLogFilter.None, 10));
        await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            wrongScope.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 10));

        await source.TrimAsync(0);
        await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            source.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 10));
    }

    [Fact]
    public async Task Positive_trim_and_tail_cursor_follow_provider_sequence_order()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = fixture.CreateStore();
        var entries = new[]
        {
            await store.AppendAsync(Entry(0, "one")),
            await store.AppendAsync(Entry(0, "two")),
            await store.AppendAsync(Entry(0, "three"))
        };

        Assert.Equal(entries[^1].ReplayCursor, await store.GetTailCursorAsync());
        await store.TrimAsync(1);
        Assert.Equal(["three"], (await store.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
        Assert.Equal(entries[^1].Sequence, await store.GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task Exact_append_conflict_rejects_same_nonce_with_a_different_payload()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = fixture.OpenSession();
        var operation = new OperationId(DateTimeOffset.UtcNow, "structured-conflict");

        session.AppendWithOutcomes(operation, Values("first", "token-1"));

        var conflict = Assert.Throws<AppendIdempotencyConflictException>(() =>
            session.AppendWithOutcomes(operation, Values("different", "token-2")));

        Assert.Equal(operation.Nonce, conflict.Nonce);
        Assert.Contains("GW-APPEND-001", conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_retention_acknowledgement_loss_retries_one_operation_and_keeps_newest_rows()
    {
        await using var fixture = await Fixture.CreateAsync();
        var lossy = new RetentionAcknowledgementLosingSession(fixture.OpenSession());
        await using var store = fixture.CreateStore(
            maxRetainedEntries: 2,
            retentionInterval: 1,
            sessionOverride: lossy);

        await store.AppendAsync(Entry(0, "one"));
        await store.AppendAsync(Entry(0, "two"));
        await store.AppendAsync(Entry(0, "three"));
        await WaitUntilAsync(() => lossy.Calls >= 2);

        Assert.True(lossy.Calls >= 2);
        Assert.Equal(lossy.Operations[0], lossy.Operations[1]);
        Assert.Equal(["two", "three"], (await store.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
    }

    [Fact]
    public async Task Periodic_retention_retries_a_transient_failure_and_keeps_the_counter_healthy()
    {
        await using var fixture = await Fixture.CreateAsync();
        var faulting = new RetentionFailureSession(fixture.OpenSession(), failuresBeforeSuccess: 1);
        await using var store = fixture.CreateStore(
            maxRetainedEntries: 2,
            retentionInterval: 3,
            sessionOverride: faulting);

        await store.AppendAsync(Entry(0, "one"));
        await store.AppendAsync(Entry(0, "two"));
        await store.AppendAsync(Entry(0, "three"));
        await WaitUntilAsync(() => faulting.Calls >= 2);

        Assert.Equal(2, faulting.Calls);
        Assert.Equal(faulting.Operations[0], faulting.Operations[1]);
        Assert.Equal(["two", "three"], (await store.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
    }

    [Fact]
    public async Task Exhausted_periodic_retention_retries_remain_armed_for_the_next_batch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var faulting = new RetentionFailureSession(fixture.OpenSession(), failuresBeforeSuccess: 3);
        await using var store = fixture.CreateStore(
            maxRetainedEntries: 2,
            retentionInterval: 2,
            sessionOverride: faulting);

        await store.AppendAsync(Entry(0, "one"));
        await store.AppendAsync(Entry(0, "two"));
        await WaitUntilAsync(() => faulting.Calls >= 3);

        await store.AppendAsync(Entry(0, "three"));
        await WaitUntilAsync(() => faulting.Calls >= 4);

        Assert.Equal(faulting.Operations[0], faulting.Operations[1]);
        Assert.Equal(faulting.Operations[1], faulting.Operations[2]);
        Assert.Equal(faulting.Operations[2], faulting.Operations[3]);
        Assert.Equal(["two", "three"], (await store.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
    }

    [Fact]
    public async Task Final_retention_prunes_rows_below_the_periodic_interval()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = fixture.CreateStore(maxRetainedEntries: 2, retentionInterval: 100);

        await store.AppendAsync(Entry(0, "one"));
        await store.AppendAsync(Entry(0, "two"));
        await store.AppendAsync(Entry(0, "three"));

        await store.StopAsync();

        Assert.Equal(["two", "three"], (await store.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
    }

    [Fact]
    public async Task Connection_constructor_applies_schema_and_publishes_actual_provider_capabilities()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = new GroundworkStructuredLogStore(
            fixture.Connection,
            Options.Create(new StructuredLogsOptions()),
            StructuredLogStoreBinding.Default);
        var resource = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
        store.Start();

        var committed = await store.AppendAsync(Entry(0, "startup"));

        Assert.True(committed.Sequence > 0);
        Assert.Equal(committed.Sequence, await store.GetHighWaterMarkAsync());
        await resource.DisposeAsync();
    }

    [Fact]
    public async Task Complex_structured_log_fields_round_trip_through_the_v2_payload()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var store = fixture.CreateStore();
        var entry = Entry(0, "boom") with
        {
            Level = LogLevel.Error,
            Properties = [new LogProperty("user", "alice")],
            Scopes = [new LogScope([new LogProperty("operation", "checkout")], "checkout scope")],
            Exception = new LogExceptionDetails("System.InvalidOperationException", "bad state", "at Checkout")
        };

        await store.AppendAsync(entry);

        var persisted = Assert.Single(await store.GetRecentAsync(StructuredLogFilter.None));
        Assert.Equal(entry.Level, persisted.Level);
        Assert.Equal(entry.Properties, persisted.Properties);
        Assert.Equal(entry.Exception, persisted.Exception);
        var scope = Assert.Single(persisted.Scopes);
        Assert.Equal("checkout scope", scope.Text);
        Assert.Equal([new LogProperty("operation", "checkout")], scope.Items);
    }

    [Fact]
    public async Task Tied_writers_replay_in_provider_sequence_order()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var first = fixture.CreateStore();
        await using var second = fixture.CreateStore();

        var commits = await Task.WhenAll(
            first.AppendAsync(Entry(0, "first")).AsTask(),
            second.AppendAsync(Entry(0, "second")).AsTask());
        var ordered = (await first.GetRecentAsync(StructuredLogFilter.None)).ToArray();
        var replayed = (await first.ReadAfterAsync(ordered[0].ReplayCursor, StructuredLogFilter.None, 100)).Entries;

        Assert.Equal(2, ordered.Length);
        Assert.Single(replayed);
        Assert.Equal(ordered[1].Message, replayed[0].Message);
        Assert.NotEqual(commits[0].ReplayCursor, commits[1].ReplayCursor);
        Assert.Equal(commits.Max(commit => commit.Sequence), await first.GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task Malformed_or_foreign_cursors_have_one_non_disclosing_outcome()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var source = fixture.CreateStore(new("tenant-a", "scope-a", "structured-logs"));
        await using var wrongTenant = fixture.CreateStore(new("tenant-b", "scope-a", "structured-logs"));
        await using var wrongScope = fixture.CreateStore(new("tenant-a", "scope-b", "structured-logs"));
        var committed = await source.AppendAsync(Entry(0, "source"));

        var errors = new[]
        {
            await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
                source.ReadAfterAsync(new StructuredLogReplayCursor("not-a-cursor"), StructuredLogFilter.None, 100)),
            await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
                wrongTenant.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100)),
            await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
                wrongScope.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100))
        };

        Assert.All(errors, error => Assert.Equal("The structured log replay cursor is unavailable.", error.Message));
    }

    [Fact]
    public async Task Operational_query_failure_is_not_reported_as_cursor_unavailable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var failure = new StructuredLogsException("The diagnostics database is unavailable.");
        var failing = new QueryFailingSession(fixture.OpenSession(), failure);
        await using var store = fixture.CreateStore(sessionOverride: failing);
        var committed = await store.AppendAsync(Entry(0, "source"));

        failing.FailQueries = true;

        var actual = await Assert.ThrowsAsync<StructuredLogsException>(() =>
            store.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task High_water_mark_propagates_a_direct_inspection_failure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var failure = new StructuredLogsException("The diagnostics inspection is unavailable.");
        var failing = new QueryFailingSession(fixture.OpenSession(), failure) { FailInspection = true };
        await using var store = fixture.CreateStore(sessionOverride: failing);

        var actual = await Assert.ThrowsAsync<StructuredLogsException>(() => store.GetHighWaterMarkAsync());

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Negative_shutdown_timeout_is_clamped_and_disposal_settles_an_accepted_append()
    {
        await using var fixture = await Fixture.CreateAsync();
        var hanging = new HangingAppendSession(fixture.OpenSession());
        var options = Options.Create(new StructuredLogsOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(-1) });
        await using var store = fixture.CreateStore(sessionOverride: hanging, options: options);
        var append = store.AppendAsync(Entry(0, "pending")).AsTask();
        var entered = false;
        try
        {
            await hanging.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            entered = true;
            await store.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAsync<StructuredLogsException>(() => append.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            hanging.Release();
            if (entered || !append.IsCompleted)
                await hanging.Exited.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Hard_stop_settles_accepted_append_when_provider_ignores_cancellation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var hanging = new HangingAppendSession(fixture.OpenSession());
        var options = Options.Create(new StructuredLogsOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(20) });
        await using var store = fixture.CreateStore(sessionOverride: hanging, options: options);
        var append = store.AppendAsync(Entry(0, "pending")).AsTask();
        var entered = false;
        try
        {
            await hanging.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            entered = true;
            await store.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAsync<StructuredLogsException>(() => append.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            hanging.Release();
            if (entered || !append.IsCompleted)
                await hanging.Exited.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Session_gate_does_not_claim_optional_capabilities()
    {
        await using var fixture = await Fixture.CreateAsync();
        var gate = new GroundworkStorageSessionGate();
        gate.Publish(fixture.OpenSession());

        Assert.False(typeof(IExactAppendStorageSession).IsAssignableFrom(gate.GetType()));
        Assert.True(gate.Current is IExactAppendStorageSession);
        gate.Release();
    }

    private static StructuredLogEntry Entry(long sequence, string message) => new()
    {
        Sequence = sequence,
        Timestamp = DateTimeOffset.UnixEpoch,
        Level = LogLevel.Information,
        Category = "E2.StructuredLogs",
        Message = message,
        SourceId = "writer"
    };

    private static QueryRequest AllRows(StorageUnit unit) => new(
        new TableId(unit.Name),
        new Predicate.AlwaysTrue(),
        [],
        Projection.All,
        Paging.None);

    private static StorageValues Values(string message, string token) => new(new Dictionary<string, object?>
    {
        [StructuredLogsGroundworkStorageSchema.TimestampField] = DateTimeOffset.UnixEpoch,
        [StructuredLogsGroundworkStorageSchema.LevelField] = (long)LogLevel.Information,
        [StructuredLogsGroundworkStorageSchema.CategoryKeyField] = "category",
        [StructuredLogsGroundworkStorageSchema.SourceKeyField] = "source",
        [StructuredLogsGroundworkStorageSchema.ReplayTokenField] = token,
        [StructuredLogsGroundworkStorageSchema.PayloadField] = JsonSerializer.Serialize(message)
    });

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string databasePath;
        private SqliteProviderConnection? connection;

        private Fixture(string databasePath, SqliteProviderConnection connection)
        {
            this.databasePath = databasePath;
            this.connection = connection;
        }

        public static Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-v2-structured-{Guid.NewGuid():N}.db");
            var connection = (SqliteProviderConnection)new SqliteProviderFactory().Create($"Data Source={path}");
            var unit = StructuredLogsGroundworkStorageSchema.CreateUnit();
            connection.Schema.Apply(unit);
            return Task.FromResult(new Fixture(path, connection));
        }

        public SqliteProviderConnection Connection => connection!;

        public async Task CreateAppendFailureTriggerAsync()
        {
            await using var sqlite = new SqliteConnection($"Data Source={databasePath}");
            await sqlite.OpenAsync();
            await using var command = sqlite.CreateCommand();
            var tableName = StructuredLogsGroundworkStorageSchema.UnitName.Replace("\"", "\"\"");
            command.CommandText = $"CREATE TRIGGER fail_structured_log BEFORE INSERT ON \"{tableName}\" BEGIN SELECT RAISE(ABORT, 'structured log append rejected'); END;";
            await command.ExecuteNonQueryAsync();
        }

        public GroundworkStructuredLogStore CreateStore(
            StructuredLogStoreBinding? binding = null,
            IStorageSession? sessionOverride = null,
            int maxRetainedEntries = 100_000,
            int retentionInterval = 5_000,
            IOptions<StructuredLogsOptions>? options = null,
            IDiagnosticsPersistenceObserver? observer = null)
        {
            var actualBinding = binding ?? StructuredLogStoreBinding.Default;
            var unit = StructuredLogsGroundworkStorageSchema.CreateUnit();
            var session = sessionOverride ?? connection!.OpenSession(unit, StorageAccess.Scoped(StructuredLogsGroundworkStorageSchema.ScopeFor(actualBinding)));
            var store = new GroundworkStructuredLogStore(
                session,
                options ?? Options.Create(new StructuredLogsOptions()),
                actualBinding,
                maxRetainedEntries,
                retentionInterval,
                observer);
            store.Start();
            return store;
        }

        public IStorageSession OpenSession(StructuredLogStoreBinding? binding = null)
        {
            var actualBinding = binding ?? StructuredLogStoreBinding.Default;
            return connection!.OpenSession(
                StructuredLogsGroundworkStorageSchema.CreateUnit(),
                StorageAccess.Scoped(StructuredLogsGroundworkStorageSchema.ScopeFor(actualBinding)));
        }

        public Task ReopenAsync()
        {
            connection?.Dispose();
            connection = (SqliteProviderConnection)new SqliteProviderFactory().Create($"Data Source={databasePath}");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            connection?.Dispose();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
            var lockPath = databasePath + ".schema.lock";
            if (File.Exists(lockPath))
                File.Delete(lockPath);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AcknowledgementLosingSession(IStorageSession inner) : DelegatingStorageSession(inner), IExactAppendStorageSession, IStorageInspectionSession
    {
        private int loseAcknowledgement = 1;

        public int Calls { get; private set; }
        public Exception? FirstProviderFailure { get; private set; }
        public List<OperationId> Operations { get; } = [];
        public StorageInspection Inspect() =>
            Assert.IsAssignableFrom<IStorageInspectionSession>(Inner).Inspect();

        public AppendOutcomeReport AppendWithOutcomes(OperationId operationId, IReadOnlyList<StorageValues> values)
        {
            Calls++;
            Operations.Add(operationId);
            AppendOutcomeReport result;
            try
            {
                result = Inner.AppendWithOutcomes(operationId, values);
            }
            catch (Exception exception)
            {
                FirstProviderFailure ??= exception;
                throw;
            }
            if (Interlocked.Exchange(ref loseAcknowledgement, 0) == 1)
                throw new IOException("The provider committed but the acknowledgement was lost.");
            return result;
        }
    }

    private sealed class RetentionAcknowledgementLosingSession(IStorageSession inner) : DelegatingStorageSession(inner), IExactAppendStorageSession, IExactRetentionStorageSession
    {
        private readonly IExactRetentionStorageSession exact = Assert.IsAssignableFrom<IExactRetentionStorageSession>(inner);
        private int loseAcknowledgement = 1;

        public int Calls { get; private set; }
        public List<OperationId> Operations { get; } = [];
        public AppendOutcomeReport AppendWithOutcomes(OperationId operationId, IReadOnlyList<StorageValues> values) =>
            Assert.IsAssignableFrom<IExactAppendStorageSession>(Inner).AppendWithOutcomes(operationId, values);

        public RetentionOperationResult ApplyRetention(OperationId operationId, RetentionExecutionOptions? options = null)
        {
            Calls++;
            Operations.Add(operationId);
            var result = exact.ApplyRetention(operationId, options);
            if (Interlocked.Exchange(ref loseAcknowledgement, 0) == 1)
                throw new IOException("The provider committed but the retention acknowledgement was lost.");
            return result;
        }
    }

    private sealed class RetentionFailureSession(IStorageSession inner, int failuresBeforeSuccess) : DelegatingStorageSession(inner), IExactAppendStorageSession, IExactRetentionStorageSession
    {
        private readonly IExactRetentionStorageSession exact = Assert.IsAssignableFrom<IExactRetentionStorageSession>(inner);
        private int failuresRemaining = failuresBeforeSuccess;
        private int calls;

        public int Calls => Volatile.Read(ref calls);
        public List<OperationId> Operations { get; } = [];

        public AppendOutcomeReport AppendWithOutcomes(OperationId operationId, IReadOnlyList<StorageValues> values) =>
            Assert.IsAssignableFrom<IExactAppendStorageSession>(Inner).AppendWithOutcomes(operationId, values);

        public RetentionOperationResult ApplyRetention(OperationId operationId, RetentionExecutionOptions? options = null)
        {
            Operations.Add(operationId);
            try
            {
                if (Interlocked.Decrement(ref failuresRemaining) >= 0)
                    throw new IOException("The provider temporarily rejected retention.");
                return exact.ApplyRetention(operationId, options);
            }
            finally
            {
                Interlocked.Increment(ref calls);
            }
        }
    }

    private sealed class HangingAppendSession(IStorageSession inner) : DelegatingStorageSession(inner), IExactAppendStorageSession
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;
        public Task Exited => exited.Task;
        public AppendOutcomeReport AppendWithOutcomes(OperationId operationId, IReadOnlyList<StorageValues> values)
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            try
            {
                return Assert.IsAssignableFrom<IExactAppendStorageSession>(Inner).AppendWithOutcomes(operationId, values);
            }
            finally
            {
                exited.TrySetResult();
            }
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class QueryFailingSession(IStorageSession inner, Exception failure) : DelegatingStorageSession(inner), IExactAppendStorageSession, IStorageInspectionSession
    {
        public bool FailQueries { get; set; }
        public bool FailInspection { get; set; }
        public StorageInspection Inspect() => FailInspection
            ? throw failure
            : Assert.IsAssignableFrom<IStorageInspectionSession>(Inner).Inspect();

        public override QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
            FailQueries ? throw failure : Inner.Query(request, options);

        public AppendOutcomeReport AppendWithOutcomes(OperationId operationId, IReadOnlyList<StorageValues> values) =>
            Assert.IsAssignableFrom<IExactAppendStorageSession>(Inner).AppendWithOutcomes(operationId, values);
    }
}
