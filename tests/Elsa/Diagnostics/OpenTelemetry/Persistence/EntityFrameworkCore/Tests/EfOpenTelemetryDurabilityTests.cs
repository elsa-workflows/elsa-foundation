using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Entities;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Stores;
using Elsa.Diagnostics.Persistence.Draining;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests;

public sealed class EfOpenTelemetryDurabilityTests
{
    [Fact]
    public void Binding_scope_identity_is_portable_and_rejects_malformed_utf16()
    {
        Assert.Equal(
            "9aba52fe1e4f3bc8c4bd47dd67fe1fa459749d9df36c3dde0dc2f9784106a6c8",
            EfOpenTelemetryBinding.Default.ScopeKey);
        _ = new EfOpenTelemetryBinding("\ufffd", "scope", "source");
        _ = new EfOpenTelemetryBinding("\ud83d\ude80", "scope", "source");
        Assert.Throws<ArgumentException>(() => new EfOpenTelemetryBinding("\ud800", "scope", "source"));
        Assert.Throws<ArgumentException>(() => new EfOpenTelemetryBinding("\udc00", "scope", "source"));
        Assert.Throws<ArgumentException>(() => new EfOpenTelemetryBinding("\ud800A", "scope", "source"));
    }

    [Fact]
    public async Task Synchronous_disposal_does_not_race_an_in_flight_query()
    {
        var directory = Path.Join(Path.GetTempPath(), "elsa-otel-dispose-" + Guid.NewGuid().ToString("N"));
        var path = Path.Join(directory, "opentelemetry.db");
        Directory.CreateDirectory(directory);
        try
        {
            var interceptor = new BlockingReaderInterceptor();
            await using var provider = OpenTelemetryEntityFrameworkCoreFixture.BuildInterceptingProvider(path, interceptor);
            await OpenTelemetryEntityFrameworkCoreFixture.EnsureCreatedAsync(provider);
            var store = provider.GetRequiredService<EfOpenTelemetryStore>();
            interceptor.Arm();

            var query = store.QueryResourcesAsync(new() { Take = 1 }).AsTask();
            await interceptor.WaitForReaderAsync().WaitAsync(TimeSpan.FromSeconds(5));
            store.Dispose();
            interceptor.Release();

            Assert.Empty((await query).Items);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => store.QueryResourcesAsync(new() { Take = 1 }).AsTask());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

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

    [Theory]
    [InlineData("{not-json}")]
    [InlineData("[\"z\",\"a\"]")]
    public async Task Corrupt_durable_summary_is_reported_as_data_corruption_without_a_partial_append(string persistedMemberships)
    {
        await using var fixture = await CreateFixtureAsync();
        var original = TelemetryTestData.Batch("corrupt-summary");
        await fixture.Store.WriteAsync(original);

        await fixture.WithDbAsync(db => CorruptTraceSummaryAsync(db, persistedMemberships));

        var readFailure = await Assert.ThrowsAsync<OpenTelemetryPersistenceDataException>(() => fixture.Store.GetTraceAsync(original.Traces.Single().TraceId).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.CorruptData, readFailure.Reason);

        var second = original with
        {
            Traces = [original.Traces.Single() with
            {
                StartTime = original.Traces.Single().StartTime.AddSeconds(1),
                EndTime = original.Traces.Single().EndTime.AddSeconds(1)
            }]
        };
        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceDataException>(() => fixture.Store.WriteAsync(second).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.CorruptData, failure.Reason);
        var rawTraceCount = await fixture.WithDbAsync(db => db.Traces.CountAsync());
        Assert.Equal(1, rawTraceCount);
    }

    [Theory]
    [InlineData("payload-identity")]
    [InlineData("scalar-projection")]
    [InlineData("search-projection")]
    [InlineData("invalid-shape")]
    public async Task Corrupt_trace_payload_or_scalar_projection_fails_the_data_boundary_atomically(string corruption)
    {
        await using var fixture = await CreateFixtureAsync();
        var original = TelemetryTestData.Batch("corrupt-trace-projection");
        await fixture.Store.WriteAsync(original);
        await fixture.WithDbAsync(async db =>
        {
            var summary = await db.TraceSummaries.SingleAsync();
            switch (corruption)
            {
                case "payload-identity":
                    var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                    serializerOptions.Converters.Add(new JsonStringEnumConverter());
                    var payload = JsonSerializer.Deserialize<TelemetryTrace>(summary.PayloadJson, serializerOptions)!;
                    summary.PayloadJson = JsonSerializer.Serialize(payload with { TraceId = "different-trace" }, serializerOptions);
                    break;
                case "scalar-projection":
                    summary.SpanCount++;
                    break;
                case "search-projection":
                    summary.TraceIdSearchKey = "corrupt";
                    break;
                case "invalid-shape":
                    summary.PayloadJson = "{}";
                    break;
            }
            await db.SaveChangesAsync();
        });

        var readFailure = await Assert.ThrowsAsync<OpenTelemetryPersistenceDataException>(
            () => fixture.Store.GetTraceAsync(original.Traces.Single().TraceId).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.CorruptData, readFailure.Reason);

        var appendFailure = await Assert.ThrowsAsync<OpenTelemetryPersistenceDataException>(
            () => fixture.Store.WriteAsync(original with { Traces = [original.Traces.Single() with { SpanCount = 2 }] }).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.CorruptData, appendFailure.Reason);
        Assert.Equal(1, await fixture.WithDbAsync(db => db.Traces.CountAsync()));
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
        services.AddOpenTelemetryDiagnosticsServices();
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

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOpenTelemetryStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfOpenTelemetryStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(OpenTelemetryEntityFrameworkCoreOptions));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDiagnosticsPersistenceDrain));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.IsType<EfOpenTelemetryStore>(provider.GetRequiredService<IOpenTelemetryStore>());
        Assert.Same(provider.GetRequiredService<IOpenTelemetryStore>(), provider.GetRequiredService<EfOpenTelemetryStore>());
        Assert.NotNull(provider.GetRequiredService<object>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetrySourceRegistry>());
    }

    [Fact]
    public void Registration_rejects_conflicting_repeated_EF_options()
    {
        var services = new ServiceCollection();
        services.AddOpenTelemetryEntityFrameworkCore(new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=first.db"
        });
        var descriptorCount = services.Count;

        Assert.Throws<InvalidOperationException>(() => services.AddOpenTelemetryEntityFrameworkCore(new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=second.db"
        }));
        Assert.Equal(descriptorCount, services.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registration_rejects_custom_store_regardless_of_default_feature_order(bool customBeforeDefault)
    {
        var services = new ServiceCollection();
        if (customBeforeDefault)
            services.AddSingleton<IOpenTelemetryStore>(_ => null!);
        services.AddOpenTelemetryDiagnosticsServices();
        if (!customBeforeDefault)
            services.AddSingleton<IOpenTelemetryStore>(_ => null!);
        var originalStores = services.Where(descriptor => descriptor.ServiceType == typeof(IOpenTelemetryStore)).ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddOpenTelemetryEntityFrameworkCore(new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }));
        Assert.Equal(originalStores, services.Where(descriptor => descriptor.ServiceType == typeof(IOpenTelemetryStore)));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfOpenTelemetryStore));
    }

    [Fact]
    public async Task Transient_transaction_begin_failure_is_retried_within_the_durable_write()
    {
        var directory = Path.Join(Path.GetTempPath(), "elsa-otel-transaction-retry-" + Guid.NewGuid().ToString("N"));
        var path = Path.Join(directory, "opentelemetry.db");
        Directory.CreateDirectory(directory);
        try
        {
            var interceptor = new TransientTransactionStartInterceptor();
            await using var provider = OpenTelemetryEntityFrameworkCoreFixture.BuildInterceptingProvider(path, interceptor);
            await OpenTelemetryEntityFrameworkCoreFixture.EnsureCreatedAsync(provider);
            var beginAttemptsBeforeWrite = interceptor.BeginAttempts;
            interceptor.FailNextBegin();
            var store = provider.GetRequiredService<EfOpenTelemetryStore>();

            await store.WriteAsync(DiagnosticsDrainBatchId.New(), TelemetryTestData.Batch("transaction-begin-retry"));

            Assert.Equal(beginAttemptsBeforeWrite + 2, interceptor.BeginAttempts);
            Assert.NotNull(await store.GetTraceAsync("transaction-begin-retry"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
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
    public void Ef_feature_configures_the_opt_in_store_and_provider_context()
    {
        var services = new ServiceCollection();
        new EfOpenTelemetryFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:",
            TenantId = "feature-tenant",
            ScopeId = "feature-scope",
            SourceId = "feature-source"
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.IsType<EfOpenTelemetryStore>(provider.GetRequiredService<IOpenTelemetryStore>());
        using var scope = provider.CreateScope();
        Assert.IsType<OpenTelemetrySqliteDbContext>(scope.ServiceProvider.GetRequiredService<OpenTelemetryDbContext>());
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

    [Fact]
    public async Task Rejected_capture_is_counted_once_for_each_lost_signal()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.EfStore.StopAsync();
        var batch = TelemetryTestData.Batch("rejected-capture");

        await Assert.ThrowsAsync<DiagnosticsDrainException>(() => fixture.Store.WriteAsync(batch).AsTask());

        var diagnostics = await fixture.Store.GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.DroppedTraceCount);
        Assert.Equal(1, diagnostics.DroppedSpanCount);
        Assert.Equal(1, diagnostics.DroppedMetricPointCount);
        Assert.Equal(1, diagnostics.DroppedLogRecordCount);
    }

    private static async Task CorruptTraceSummaryAsync(OpenTelemetryDbContext db, string persistedMemberships)
    {
        var summary = await db.TraceSummaries.SingleAsync();
        summary.ServiceMembershipJson = persistedMemberships;
        await db.SaveChangesAsync();
    }

    private static async Task<OpenTelemetryEntityFrameworkCoreFixture> CreateFixtureAsync()
    {
        var fixture = new OpenTelemetryEntityFrameworkCoreFixture();
        await fixture.InitializeAsync(new() { MaxQuerySize = 100 });
        return fixture;
    }
}
