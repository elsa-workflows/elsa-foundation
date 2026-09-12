using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Entities;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Stores;
using Elsa.Diagnostics.Persistence.Draining;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests;

public sealed class EfOpenTelemetryDurabilityTests
{
    [Fact]
    public async Task Durable_group_commits_each_capture_once_and_replays_as_a_unit()
    {
        await using var fixture = await CreateFixtureAsync();
        var first = (DiagnosticsDrainBatchId.New(), TelemetryTestData.Batch("group-trace-a"));
        var second = (DiagnosticsDrainBatchId.New(), TelemetryTestData.Batch("group-trace-b"));
        var group = new[] { first, second };

        await fixture.EfStore.WriteGroupAsync(group);
        await fixture.EfStore.WriteGroupAsync(group);

        var diagnostics = await fixture.Store.GetDiagnosticsAsync();
        Assert.Equal(2, diagnostics.TraceCount);
        Assert.Equal(2, diagnostics.SpanCount);
        Assert.Equal(2, diagnostics.MetricPointCount);
        Assert.Equal(2, diagnostics.LogRecordCount);
        Assert.Equal(2, await fixture.WithDbAsync(db => db.CaptureLedger.CountAsync()));
    }

    [Fact]
    public async Task Reusing_a_capture_guid_with_a_different_issuance_time_conflicts()
    {
        await using var fixture = await CreateFixtureAsync();
        var batch = TelemetryTestData.Batch("issued-at-conflict");
        var first = DiagnosticsDrainBatchId.New();
        var changed = new DiagnosticsDrainBatchId(first.Value, first.IssuedAt.AddTicks(1));

        await fixture.EfStore.WriteAsync(first, batch);

        await Assert.ThrowsAsync<OpenTelemetryPersistenceConflictException>(() => fixture.EfStore.WriteAsync(changed, batch).AsTask());
        Assert.Equal(1, await fixture.WithDbAsync(db => db.Traces.CountAsync()));
    }

    [Fact]
    public async Task Conflicting_identity_rolls_back_every_pending_capture_in_the_group()
    {
        await using var fixture = await CreateFixtureAsync();
        var existingId = DiagnosticsDrainBatchId.New();
        var existing = TelemetryTestData.Batch("group-existing");
        await fixture.EfStore.WriteAsync(existingId, existing);
        var pending = TelemetryTestData.Batch("group-must-rollback");
        var changedExisting = existing with
        {
            Traces = [existing.Traces.Single() with { Name = "conflicting-content" }]
        };

        await Assert.ThrowsAsync<OpenTelemetryPersistenceConflictException>(() => fixture.EfStore.WriteGroupAsync(
            [
                (DiagnosticsDrainBatchId.New(), pending),
                (existingId, changedExisting)
            ]).AsTask());

        Assert.Null(await fixture.Store.GetTraceAsync("group-must-rollback"));
        Assert.NotNull(await fixture.Store.GetTraceAsync("group-existing"));
        Assert.Equal(1, await fixture.WithDbAsync(db => db.CaptureLedger.CountAsync()));
    }

    [Fact]
    public async Task Identical_writers_converge_to_one_durable_capture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-otel-concurrent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "concurrent.db");

        try
        {
            await using var first = OpenTelemetryEntityFrameworkCoreFixture.BuildProvider(path, new() { MaxQuerySize = 100 });
            await OpenTelemetryEntityFrameworkCoreFixture.EnsureCreatedAsync(first);
            await using var second = OpenTelemetryEntityFrameworkCoreFixture.BuildProvider(path, new() { MaxQuerySize = 100 });
            await OpenTelemetryEntityFrameworkCoreFixture.EnsureCreatedAsync(second);
            var firstStore = first.GetRequiredService<EfOpenTelemetryStore>();
            var secondStore = second.GetRequiredService<EfOpenTelemetryStore>();
            firstStore.Start();
            secondStore.Start();

            var batch = TelemetryTestData.Batch("concurrent-trace");
            var batchId = DiagnosticsDrainBatchId.New();
            await Task.WhenAll(
                firstStore.WriteAsync(batchId, batch).AsTask(),
                secondStore.WriteAsync(batchId, batch).AsTask());

            Assert.Single((await firstStore.QueryTracesAsync(new() { Take = 10 })).Items);
            Assert.Single((await firstStore.QueryResourcesAsync(new() { Take = 10 })).Items);
            Assert.Single((await firstStore.QueryMetricsAsync(new() { Take = 10 })).Points);
            Assert.Single((await firstStore.QueryLogsAsync(new() { Take = 10 })).Items);
            await firstStore.StopAsync();
            await secondStore.StopAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Durable_capture_survives_provider_restart_but_isolated_database_does_not_leak()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-otel-restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "restart.db");
        var foreignPath = Path.Combine(directory, "foreign.db");

        try
        {
            await using (var first = OpenTelemetryEntityFrameworkCoreFixture.BuildProvider(path))
            {
                await OpenTelemetryEntityFrameworkCoreFixture.EnsureCreatedAsync(first);
                var store = first.GetRequiredService<EfOpenTelemetryStore>();
                store.Start();
                await store.WriteAsync(TelemetryTestData.Batch("restart-trace"));
                await store.StopAsync();
            }

            await using (var restarted = OpenTelemetryEntityFrameworkCoreFixture.BuildProvider(path))
            {
                await OpenTelemetryEntityFrameworkCoreFixture.EnsureCreatedAsync(restarted);
                var store = restarted.GetRequiredService<EfOpenTelemetryStore>();
                store.Start();
                Assert.Equal(["restart-trace"], (await store.QueryTracesAsync(new() { Take = 10 })).Items.Select(item => item.TraceId));
                await store.StopAsync();
            }

            await using var foreign = OpenTelemetryEntityFrameworkCoreFixture.BuildProvider(foreignPath);
            await OpenTelemetryEntityFrameworkCoreFixture.EnsureCreatedAsync(foreign);
            var foreignStore = foreign.GetRequiredService<EfOpenTelemetryStore>();
            foreignStore.Start();
            Assert.Empty((await foreignStore.QueryTracesAsync(new() { Take = 10 })).Items);
            await foreignStore.StopAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Corrupt_durable_summary_is_reported_without_a_partial_append()
    {
        await using var fixture = await CreateFixtureAsync();
        var original = TelemetryTestData.Batch("corrupt-summary");
        await fixture.Store.WriteAsync(original);

        await fixture.WithDbAsync(CorruptTraceSummaryAsync);

        var second = original with
        {
            Traces = [original.Traces.Single() with { StartTime = TelemetryTestData.Now.AddSeconds(1) }]
        };
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Store.WriteAsync(second).AsTask());
        var rawTraceCount = await fixture.WithDbAsync(db => db.Traces.CountAsync());
        Assert.Equal(1, rawTraceCount);
    }

    [Fact]
    public async Task The_model_has_signal_keys_query_indexes_and_a_concurrency_token()
    {
        await using var fixture = await CreateFixtureAsync();
        var metadata = await fixture.WithDbAsync(db => Task.FromResult(db.Model.GetEntityTypes().ToArray()));
        Assert.True(metadata.Length >= 6, "The EF model should contain resources, traces, spans, instruments, points and logs.");

        foreach (var suffix in new[] { "Resource", "Trace", "Span", "Instrument", "Point", "Log" })
        {
            var entity = Assert.Single(metadata, item =>
                item.ClrType.Name.Contains(suffix, StringComparison.OrdinalIgnoreCase) &&
                (suffix != "Trace" || !item.ClrType.Name.Contains("Summary", StringComparison.OrdinalIgnoreCase)));
            Assert.NotNull(entity.FindPrimaryKey());
            Assert.NotNull(entity.GetTableName());
        }

        var summary = Assert.Single(metadata, item => item.ClrType == typeof(OpenTelemetryTraceSummaryEntity));
        var version = summary.FindProperty(nameof(OpenTelemetryTraceSummaryEntity.Version));
        Assert.NotNull(version);
        Assert.True(version!.IsConcurrencyToken);

        var trace = Assert.Single(metadata, item =>
            item.ClrType.Name.Contains("Trace", StringComparison.OrdinalIgnoreCase) &&
            !item.ClrType.Name.Contains("Summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trace.GetIndexes(), index => index.Properties.Any(property =>
            property.Name.Contains("Trace", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Start", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Registration_is_opt_in_idempotent_and_replaces_only_the_telemetry_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        services.AddOpenTelemetryEntityFrameworkCore(new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.AddOpenTelemetryEntityFrameworkCore(new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.IsType<EfOpenTelemetryStore>(provider.GetRequiredService<IOpenTelemetryStore>());
        Assert.Same(provider.GetRequiredService<IOpenTelemetryStore>(), provider.GetRequiredService<EfOpenTelemetryStore>());
        Assert.NotNull(provider.GetRequiredService<object>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetrySourceRegistry>());
    }

    [Fact]
    public void Registration_rejects_an_unknown_provider_at_the_feature_boundary()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddOpenTelemetryEntityFrameworkCore(new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = "unknown",
            ConnectionString = "Data Source=:memory:"
        }));
    }

    [Fact]
    public void Registration_rejects_a_second_explicit_store_instead_of_silently_overriding_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOpenTelemetryStore>(_ => null!);

        Assert.Throws<InvalidOperationException>(() => services.AddOpenTelemetryEntityFrameworkCore(new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }));
    }

    [Fact]
    public async Task Infrastructure_failures_do_not_escape_as_successful_captures()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-otel-infrastructure-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "missing", "otel.db");
        try
        {
            await using var provider = OpenTelemetryEntityFrameworkCoreFixture.BuildProvider(path);
            var store = provider.GetRequiredService<EfOpenTelemetryStore>();
            store.Start();
            var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceUnavailableException>(() =>
                store.WriteAsync(TelemetryTestData.Batch("unavailable")).AsTask());
            Assert.Equal(OpenTelemetryPersistenceFailureReason.ProviderFailure, failure.Reason);
            Assert.NotNull(failure.InnerException);
            await store.StopAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CorruptTraceSummaryAsync(OpenTelemetryDbContext db)
    {
        var summary = await db.TraceSummaries.SingleAsync();
        summary.ServiceMembershipJson = "{not-json}";
        await db.SaveChangesAsync();
    }

    private static async Task<OpenTelemetryEntityFrameworkCoreFixture> CreateFixtureAsync()
    {
        var fixture = new OpenTelemetryEntityFrameworkCoreFixture();
        await fixture.InitializeAsync(new() { MaxQuerySize = 100 });
        return fixture;
    }
}
