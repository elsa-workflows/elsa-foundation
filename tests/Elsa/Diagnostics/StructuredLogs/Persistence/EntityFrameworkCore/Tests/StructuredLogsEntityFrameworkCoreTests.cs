using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests;

public sealed class StructuredLogsEntityFrameworkCoreTests
{
    [Fact]
    public async Task Append_round_trips_complex_payload_and_durable_cursor()
    {
        await using var fixture = await CreateFixtureAsync();
        var entry = Entry("first", LogLevel.Warning, "source-a") with
        {
            Timestamp = new DateTimeOffset(2026, 9, 12, 13, 14, 15, TimeSpan.FromHours(5)).AddTicks(7),
            EventId = 42,
            EventName = "Complex",
            MessageTemplate = "{Name}",
            Properties = [new("Name", "value")],
            Scopes = [new([new("Tenant", "tenant-a")], "scope text")],
            Exception = new("InvalidOperationException", "bad", "stack", new("Inner", "inner"))
        };

        var committed = await fixture.Store.AppendAsync(entry);

        Assert.Equal(1, committed.Sequence);
        Assert.True(committed.ReplayCursor?.IsValid);
        Assert.Equal(entry.Timestamp, committed.Timestamp);
        Assert.Equal(entry.Properties, committed.Properties);
        Assert.Equal(entry.Scopes.Select(scope => (scope.Items.Select(item => (item.Name, item.Value)).ToArray(), scope.Text)),
            committed.Scopes.Select(scope => (scope.Items.Select(item => (item.Name, item.Value)).ToArray(), scope.Text)));
        Assert.Equal(entry.Exception, committed.Exception);
        Assert.Equal(1, await fixture.Store.GetHighWaterMarkAsync());
        var recent = (await fixture.Store.GetRecentAsync(StructuredLogFilter.None)).Single();
        Assert.Equal(committed.Sequence, recent.Sequence);
        Assert.Equal(committed.ReplayCursor, recent.ReplayCursor);
        Assert.Equal(committed.Message, recent.Message);
        Assert.Equal(committed.Exception, recent.Exception);
    }

    [Fact]
    public async Task Recent_and_read_after_are_bounded_stable_and_advance_over_filtered_rows()
    {
        await using var fixture = await CreateFixtureAsync();
        var first = await fixture.Store.AppendAsync(Entry("first", LogLevel.Information, "source-a"));
        await fixture.Store.AppendAsync(Entry("hidden", LogLevel.Debug, "source-a"));
        var third = await fixture.Store.AppendAsync(Entry("third", LogLevel.Warning, "source-a"));

        var recent = await fixture.Store.GetRecentAsync(new StructuredLogFilter { MinimumLevel = LogLevel.Information, MaxCount = 2 });
        Assert.Equal([first.Message, third.Message], recent.Select(value => value.Message));

        var page = await fixture.Store.ReadAfterAsync(first.ReplayCursor, new StructuredLogFilter { MinimumLevel = LogLevel.Warning }, 10);
        Assert.Equal([third.Message], page.Entries.Select(value => value.Message));
        Assert.Equal(third.ReplayCursor, page.NextCursor);
        Assert.False(page.HasMore);
    }

    [Theory]
    [InlineData("tenant-b", "scope-a", "stream-a")]
    [InlineData("tenant-a", "scope-b", "stream-a")]
    [InlineData("tenant-a", "scope-a", "stream-b")]
    public async Task Tenant_scope_and_stream_binding_isolation_and_cursor_fail_closed(
        string tenantId,
        string scopeId,
        string streamId)
    {
        await using var fixture = await CreateFixtureAsync();
        var committed = await fixture.Store.AppendAsync(Entry("private", LogLevel.Information, "source-a"));
        await using var otherProvider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(
            fixture.DatabasePath,
            new(tenantId, scopeId, streamId));
        await using var otherScope = otherProvider.CreateAsyncScope();
        var other = otherScope.ServiceProvider.GetRequiredService<EfStructuredLogStore>();
        other.Start();

        Assert.Empty(await other.GetRecentAsync(StructuredLogFilter.None));
        await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            other.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 10));
        await other.StopAsync();
    }

    [Fact]
    public async Task Binding_identity_remains_injective_when_values_contain_delimiters()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Store.StopAsync();
        await using var firstProvider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(
            fixture.DatabasePath,
            new("tenant:a", "scope", "stream"));
        await using var secondProvider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(
            fixture.DatabasePath,
            new("tenant", "a:scope", "stream"));
        var first = firstProvider.GetRequiredService<EfStructuredLogStore>();
        var second = secondProvider.GetRequiredService<EfStructuredLogStore>();
        first.Start();
        second.Start();

        await first.AppendAsync(Entry("first", LogLevel.Information, "source-a"));
        await second.AppendAsync(Entry("second", LogLevel.Information, "source-a"));

        Assert.Equal(["first"], (await first.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
        Assert.Equal(["second"], (await second.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
        await first.StopAsync();
        await second.StopAsync();
    }

    [Fact]
    public async Task Trim_zero_preserves_lifetime_high_water_and_restart_position()
    {
        await using var fixture = await CreateFixtureAsync();
        var committed = await fixture.Store.AppendAsync(Entry("retained", LogLevel.Information, "source-a"));
        await fixture.Store.TrimAsync(0);

        Assert.Equal(1, await fixture.Store.GetHighWaterMarkAsync());
        Assert.Null(await fixture.Store.GetTailCursorAsync());
        await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            fixture.Store.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 10));

        await fixture.Store.StopAsync();
        var restarted = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(fixture.DatabasePath, fixture.Binding);
        await StructuredLogsEntityFrameworkCoreFixture.EnsureCreatedAsync(restarted);
        var restartedStore = restarted.GetRequiredService<EfStructuredLogStore>();
        restartedStore.Start();
        var next = await restartedStore.AppendAsync(Entry("after-restart", LogLevel.Information, "source-a"));
        Assert.Equal(2, next.Sequence);
        await restarted.DisposeAsync();
    }

    [Fact]
    public async Task Positive_trim_keeps_newest_rows_and_tail()
    {
        await using var fixture = await CreateFixtureAsync();
        var entries = new[]
        {
            await fixture.Store.AppendAsync(Entry("one", LogLevel.Information, "source-a")),
            await fixture.Store.AppendAsync(Entry("two", LogLevel.Information, "source-a")),
            await fixture.Store.AppendAsync(Entry("three", LogLevel.Information, "source-a")),
            await fixture.Store.AppendAsync(Entry("four", LogLevel.Information, "source-a"))
        };

        await fixture.Store.TrimAsync(2);

        Assert.Equal(["three", "four"], (await fixture.Store.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
        Assert.Equal(entries[3].ReplayCursor, await fixture.Store.GetTailCursorAsync());
        await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            fixture.Store.ReadAfterAsync(entries[1].ReplayCursor, StructuredLogFilter.None, 10));
        Assert.Equal(4, await fixture.Store.GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task Append_idempotency_outcomes_are_pruned_but_the_expiry_cutoff_remains_durable()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Store.AppendAsync(Entry("sensitive", LogLevel.Information, "source-a"));

        await using (var provider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(fixture.DatabasePath, fixture.Binding))
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
            var operation = await db.AppendOperations.SingleAsync();
            operation.IssuedAtTicks = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(2)).UtcTicks;
            await db.SaveChangesAsync();
        }

        var cutoffFloor = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(1)).UtcTicks;
        await fixture.Store.TrimAsync(1);
        var cutoffCeiling = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(1)).UtcTicks;

        await using (var provider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(fixture.DatabasePath, fixture.Binding))
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
            Assert.Empty(await db.AppendOperations.AsNoTracking().ToListAsync());
            var cutoff = await db.StreamStates.Select(state => state.AppendOperationCutoffTicks).SingleAsync();
            Assert.InRange(cutoff, cutoffFloor, cutoffCeiling);
        }

        var fresh = await fixture.Store.AppendAsync(Entry("fresh-operation", LogLevel.Information, "source-a"));
        Assert.Equal(2, fresh.Sequence);
        Assert.Equal(["sensitive", "fresh-operation"],
            (await fixture.Store.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
    }

    [Fact]
    public async Task Invalid_default_foreign_and_malformed_cursors_have_one_public_failure_shape()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Store.AppendAsync(Entry("one", LogLevel.Information, "source-a"));
        StructuredLogReplayCursor invalidWireValue = default;
        var invalid = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            fixture.Store.ReadAfterAsync(invalidWireValue, StructuredLogFilter.None, 10));
        var malformed = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            fixture.Store.ReadAfterAsync(new StructuredLogReplayCursor("malformed"), StructuredLogFilter.None, 10));

        await using var foreignProvider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(
            fixture.DatabasePath,
            new StructuredLogStoreBinding("tenant-b", "scope-a", "stream-a"));
        await using var foreignScope = foreignProvider.CreateAsyncScope();
        await foreignScope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>().Database.EnsureCreatedAsync();
        var foreignStore = foreignScope.ServiceProvider.GetRequiredService<EfStructuredLogStore>();
        foreignStore.Start();
        var foreignCursor = (await foreignStore.AppendAsync(Entry("foreign", LogLevel.Information, "source-a"))).ReplayCursor;
        var foreign = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            fixture.Store.ReadAfterAsync(foreignCursor, StructuredLogFilter.None, 10));

        Assert.Equal(invalid.Message, malformed.Message);
        Assert.Equal(invalid.Message, foreign.Message);
        await foreignStore.StopAsync();
    }

    [Fact]
    public async Task Filtered_cursor_advancement_reports_more_and_can_continue()
    {
        await using var fixture = await CreateFixtureAsync();
        var first = await fixture.Store.AppendAsync(Entry("first", LogLevel.Information, "source-a"));
        await fixture.Store.AppendAsync(Entry("filtered", LogLevel.Debug, "source-a"));
        var last = await fixture.Store.AppendAsync(Entry("last", LogLevel.Error, "source-a"));

        var page = await fixture.Store.ReadAfterAsync(first.ReplayCursor, new StructuredLogFilter { MinimumLevel = LogLevel.Error }, 1);
        Assert.Empty(page.Entries);
        Assert.NotEqual(first.ReplayCursor, page.NextCursor);
        Assert.True(page.HasMore);

        var continuation = await fixture.Store.ReadAfterAsync(page.NextCursor, StructuredLogFilter.None, 1);
        Assert.Equal(last.Message, continuation.Entries.Single().Message);
        Assert.False(continuation.HasMore);
    }

    [Fact]
    public async Task Independent_file_backed_stores_allocate_deterministic_unique_positions()
    {
        await using var fixture = await CreateFixtureAsync();
        await using var secondProvider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(fixture.DatabasePath, fixture.Binding);
        await StructuredLogsEntityFrameworkCoreFixture.EnsureCreatedAsync(secondProvider);
        var secondStore = secondProvider.GetRequiredService<EfStructuredLogStore>();
        secondStore.Start();

        var commits = await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
            (index % 2 == 0 ? fixture.Store : secondStore).AppendAsync(Entry($"concurrent-{index}", LogLevel.Information, "source-a")).AsTask()));

        Assert.Equal(10, commits.Select(value => value.Sequence).Distinct().Count());
        await secondStore.StopAsync();
        await using var verificationProvider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(fixture.DatabasePath, fixture.Binding);
        await StructuredLogsEntityFrameworkCoreFixture.EnsureCreatedAsync(verificationProvider);
        await using var verificationScope = verificationProvider.CreateAsyncScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
        Assert.Equal(10, await db.Records.CountAsync());
        Assert.Equal(10, await db.StreamStates.Select(state => state.HighWater).SingleAsync());
    }

    [Fact]
    public async Task Acknowledgement_loss_replays_the_same_committed_batch_once()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Store.StopAsync();
        var interceptor = new CommitAcknowledgementLossInterceptor();
        await using var provider = BuildInterceptingProvider(fixture.DatabasePath, fixture.Binding, interceptor);
        await StructuredLogsEntityFrameworkCoreFixture.EnsureCreatedAsync(provider);
        var store = provider.GetRequiredService<EfStructuredLogStore>();
        store.Start();
        interceptor.Arm();

        var committed = await store.AppendAsync(Entry("ack-loss", LogLevel.Information, "source-a"));

        Assert.Equal(1, committed.Sequence);
        Assert.Single(await ReadRecordsAsync(provider));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
            Assert.Single(await db.AppendOperations.ToListAsync());
            Assert.Equal(1, await db.StreamStates.Select(state => state.HighWater).SingleAsync());
        }
        await store.StopAsync();
    }

    [Fact]
    public async Task Failed_batch_is_atomic_after_all_bounded_retries()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Store.StopAsync();
        var interceptor = new FailingInsertInterceptor();
        await using var provider = BuildInterceptingProvider(fixture.DatabasePath, fixture.Binding, interceptor);
        await StructuredLogsEntityFrameworkCoreFixture.EnsureCreatedAsync(provider);
        var store = provider.GetRequiredService<EfStructuredLogStore>();
        store.Start();
        interceptor.FailInserts();

        await Assert.ThrowsAsync<StructuredLogsException>(() => store.AppendAsync(Entry("rollback", LogLevel.Information, "source-a")).AsTask());

        Assert.True(interceptor.AllowedInsertCount > 0);
        Assert.True(interceptor.FailedCommandCount > 0);
        Assert.Empty(await ReadRecordsAsync(provider));
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
        Assert.Empty(await db.StreamStates.ToListAsync());
        Assert.Empty(await db.AppendOperations.ToListAsync());
        await store.StopAsync();
    }

    [Fact]
    public async Task Feature_registration_is_opt_in_and_replaces_only_the_structured_log_store()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddOptions<StructuredLogsOptions>();
        new StructuredLogsFeature().ConfigureServices(services);
        services.AddSingleton(new object());
        new StructuredLogsEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }.ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        Assert.IsType<EfStructuredLogStore>(provider.GetRequiredService<IStructuredLogStore>());
        Assert.Same(provider.GetRequiredService<IStructuredLogStore>(), provider.GetRequiredService<EfStructuredLogStore>());
        await using var scope = provider.CreateAsyncScope();
        Assert.IsType<StructuredLogsSqliteDbContext>(scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>());
        Assert.Null(provider.GetService<InMemoryStructuredLogStore>());
        Assert.NotNull(provider.GetRequiredService<object>());
    }

    [Fact]
    public void Store_constructor_rejects_invalid_dependencies_bindings_and_bounds()
    {
        var scopes = new ThrowingScopeFactory(new IOException("unused"));
        var options = Options.Create(new StructuredLogsOptions());
        var binding = new StructuredLogStoreBinding("tenant", "scope", "stream");

        Assert.Throws<ArgumentNullException>(() => new EfStructuredLogStore(null!, options, binding));
        Assert.Throws<ArgumentNullException>(() => new EfStructuredLogStore(scopes, null!, binding));
        Assert.Throws<ArgumentNullException>(() => new EfStructuredLogStore(scopes, options, null!));
        Assert.Throws<ArgumentException>(() => new EfStructuredLogStore(scopes, options, new("", "scope", "stream")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EfStructuredLogStore(scopes, options, binding, maxRetainedEntries: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EfStructuredLogStore(scopes, options, binding, retentionInterval: 0));
    }

    [Fact]
    public async Task Store_wraps_read_and_write_infrastructure_failures_at_the_feature_boundary()
    {
        var cause = new IOException("database unavailable");
        await using var store = new EfStructuredLogStore(
            new ThrowingScopeFactory(cause),
            Options.Create(new StructuredLogsOptions()),
            new("tenant", "scope", "stream"));

        var read = await Assert.ThrowsAsync<StructuredLogsException>(() => store.GetHighWaterMarkAsync());
        var write = await Assert.ThrowsAsync<StructuredLogsException>(() => store.TrimAsync(0));

        Assert.Same(cause, read.InnerException);
        Assert.Same(cause, write.InnerException);
    }

    [Fact]
    public async Task Query_validation_and_corrupt_payload_fail_with_stable_public_exceptions()
    {
        await using var fixture = await CreateFixtureAsync();
        Assert.Empty(await fixture.Store.GetRecentAsync(new StructuredLogFilter { MaxCount = 0 }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Store.GetRecentAsync(null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Store.ReadAfterAsync(null, StructuredLogFilter.None, 0));
        var committed = await fixture.Store.AppendAsync(Entry("corrupt", LogLevel.Information, "source-a"));

        await using (var provider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(fixture.DatabasePath, fixture.Binding))
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
            var record = await db.Records.SingleAsync();
            record.PayloadJson = "not-json";
            await db.SaveChangesAsync();
        }

        var failure = await Assert.ThrowsAsync<StructuredLogsException>(() =>
            fixture.Store.GetRecentAsync(StructuredLogFilter.None));
        Assert.IsType<System.Text.Json.JsonException>(failure.InnerException);

        await using (var provider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(fixture.DatabasePath, fixture.Binding))
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
            var record = await db.Records.SingleAsync();
            record.PayloadJson = "null";
            await db.SaveChangesAsync();
        }

        var anchorFailure = await Assert.ThrowsAsync<StructuredLogsException>(() =>
            fixture.Store.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 10));
        Assert.IsNotType<StructuredLogReplayCursorUnavailableException>(anchorFailure);
        Assert.Equal("The EF structured log payload is invalid.", anchorFailure.Message);
    }

    [Fact]
    public void Feature_rejects_a_second_explicit_structured_log_backend()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.ReplaceDiagnosticsStore<IStructuredLogStore, InMemoryStructuredLogStore>(ServiceLifetime.Singleton);

        Assert.Throws<InvalidOperationException>(() =>
            services.AddStructuredLogsEntityFrameworkCore(new StructuredLogsEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            }));
    }

    [Fact]
    public async Task Model_has_scope_keys_concurrency_token_and_query_indexes()
    {
        await using var fixture = await CreateFixtureAsync();
        // The fixture's provider owns the scoped context; inspect the model through a fresh provider scope.
        await using var provider = StructuredLogsEntityFrameworkCoreFixture.BuildProvider(fixture.DatabasePath, fixture.Binding);
        await using var contextScope = provider.CreateAsyncScope();
        var model = contextScope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>().Model;
        var record = model.FindEntityType(typeof(StructuredLogRecord));
        var state = model.FindEntityType(typeof(StructuredLogStreamState));
        Assert.NotNull(record?.FindPrimaryKey());
        Assert.True(state!.FindProperty(nameof(StructuredLogStreamState.Version))!.IsConcurrencyToken);
        Assert.NotNull(state.FindProperty(nameof(StructuredLogStreamState.AppendOperationCutoffTicks)));
        Assert.Contains(record!.GetIndexes(), index => index.IsUnique && index.Properties.Any(property => property.Name == nameof(StructuredLogRecord.ReplayToken)));
    }

    private static StructuredLogEntry Entry(string message, LogLevel level, string source) => new()
    {
        Message = message,
        Category = "category",
        Level = level,
        SourceId = source,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static async Task<StructuredLogsEntityFrameworkCoreFixture> CreateFixtureAsync()
    {
        var fixture = new StructuredLogsEntityFrameworkCoreFixture();
        await fixture.InitializeAsync();
        return fixture;
    }

    private static ServiceProvider BuildInterceptingProvider(
        string path,
        StructuredLogStoreBinding binding,
        IInterceptor interceptor)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddOptions<StructuredLogsOptions>();
        services.AddSingleton(binding);
        services.AddDbContext<InterceptingStructuredLogsDbContext>((_, builder) =>
        {
            builder.UseSqlite($"Data Source={path}");
            builder.AddInterceptors(interceptor);
        });
        services.AddScoped<StructuredLogsDbContext>(provider => provider.GetRequiredService<InterceptingStructuredLogsDbContext>());
        services.AddSingleton<EfStructuredLogStore>();
        services.AddDiagnosticsPersistenceLifecycle<EfStructuredLogStore>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<IReadOnlyList<StructuredLogRecord>> ReadRecordsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>().Records.AsNoTracking().ToListAsync();
    }
}

internal sealed class InterceptingStructuredLogsDbContext(DbContextOptions<InterceptingStructuredLogsDbContext> options)
    : StructuredLogsDbContext(options)
{
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StructuredLogRecord>().Property(record => record.PayloadJson).HasColumnType("TEXT");
        modelBuilder.Entity<StructuredLogAppendOperation>().Property(operation => operation.OutcomeJson).HasColumnType("TEXT");
    }
}

internal sealed class CommitAcknowledgementLossInterceptor : DbTransactionInterceptor
{
    private int armed;

    public void Arm() => Interlocked.Exchange(ref armed, 1);

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref armed, 0) == 1)
            throw new InvalidOperationException("Simulated acknowledgement loss after commit.");
        return Task.CompletedTask;
    }
}

internal sealed class FailingInsertInterceptor : DbCommandInterceptor
{
    private int armed;
    private int allowedInsertCount;
    private int failedCommandCount;

    public int AllowedInsertCount => Volatile.Read(ref allowedInsertCount);
    public int FailedCommandCount => Volatile.Read(ref failedCommandCount);

    public void FailInserts() => Interlocked.Exchange(ref armed, 1);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (ShouldFail(command))
            return ValueTask.FromException<InterceptionResult<int>>(new InvalidOperationException("Simulated atomic write failure after partial work."));
        return new(result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (ShouldFail(command))
            return ValueTask.FromException<InterceptionResult<DbDataReader>>(new InvalidOperationException("Simulated atomic write failure after partial work."));
        return new(result);
    }

    private bool ShouldFail(DbCommand command)
    {
        if (Volatile.Read(ref armed) == 0 ||
            !command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!command.CommandText.Contains("stream_states", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref allowedInsertCount);
            return false;
        }
        Interlocked.Increment(ref failedCommandCount);
        return true;
    }
}

internal sealed class ThrowingScopeFactory(Exception exception) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => throw exception;
}
