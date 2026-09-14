using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowExecutionStateStoreTests
{
    [Fact]
    public void Registration_is_idempotent_and_rejects_foreign_ownership()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        var options = new RuntimeWorkflowExecutionEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = "01234567890123456789012345678901" };
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);
        var count = services.Count;
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);
        Assert.Equal(count, services.Count);

        var foreign = new ServiceCollection();
        foreign.AddSingleton<IWorkflowExecutionStateStore>(new InMemoryWorkflowExecutionStateStore());
        Assert.Throws<InvalidOperationException>(() => foreign.AddRuntimeWorkflowExecutionEntityFrameworkCore(options));
    }

    [Fact]
    public void Registration_can_follow_or_precede_runtime_defaults_without_duplicate_contracts()
    {
        var options = new RuntimeWorkflowExecutionEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = "01234567890123456789012345678901" };
        var afterDefaults = new ServiceCollection();
        afterDefaults.AddWorkflowRuntime();
        afterDefaults.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);
        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(afterDefaults)!.Name);
        Assert.Single(afterDefaults.Where(x => x.ServiceType == typeof(IWorkflowExecutionStateStore)));

        var beforeDefaults = new ServiceCollection();
        beforeDefaults.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);
        beforeDefaults.AddWorkflowRuntime();
        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(beforeDefaults)!.Name);
        Assert.Single(beforeDefaults.Where(x => x.ServiceType == typeof(IWorkflowExecutionStateStore)));
    }

    [Fact]
    public async Task Workflow_execution_feature_registers_store_options_and_manifest_contract()
    {
        const string signingKey = "ef-runtime-workflow-execution-feature-signing-key";
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new RuntimeWorkflowExecutionEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = signingKey
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfWorkflowExecutionStateStore>(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>());
        var options = provider.GetRequiredService<RuntimeWorkflowExecutionEntityFrameworkCoreOptions>();
        Assert.Equal("Sqlite", options.Provider);
        Assert.Equal("Data Source=:memory:", options.ConnectionString);
        Assert.Null(options.ConnectionName);
        Assert.Equal(signingKey, options.RecoveryContinuationSigningKey);
        Assert.Equal(signingKey, provider.GetRequiredService<IOptions<RuntimeRecoveryContinuationOptions>>().Value.SigningKey);
        Assert.False(provider.GetRequiredService<IOptions<RuntimeRecoveryContinuationOptions>>().Value.AllowEphemeralDevelopmentKey);

        var featureType = typeof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature);
        Assert.Contains(featureType.CustomAttributes, attribute => attribute.AttributeType.Name == "ShellFeatureAttribute");
        Assert.Contains(featureType.CustomAttributes, attribute => attribute.AttributeType.Name == "ManifestRuntimeKindAttribute");
        Assert.Equal(3, featureType.CustomAttributes.Count(attribute => attribute.AttributeType.Name == "ManifestFeatureCategoryAttribute"));
        foreach (var property in new[]
                 {
                     nameof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature.Provider),
                     nameof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature.ConnectionString),
                     nameof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature.ConnectionName),
                     nameof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature.RecoveryContinuationSigningKey)
                 }.Select(featureType.GetProperty))
        {
            Assert.Contains(property!.CustomAttributes, attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        }

        Assert.False(featureType.IsSealed);
        Assert.True(featureType.GetMethod(nameof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature.ConfigureServices))!.IsVirtual);
        Assert.IsAssignableFrom<RuntimeWorkflowExecutionEntityFrameworkCoreFeature>(new DerivedFeature());
    }

    [Fact]
    public async Task Crud_restart_and_scope_isolation_round_trip_lossless_state()
    {
        await using var database = await Database.CreateAsync();
        var state = State("execution-1", "tenant-a", DateTimeOffset.UtcNow) with { CorrelationId = "correlation", Authority = new WorkflowExecutionAuthoritySnapshot("system", "root", new Dictionary<string, string> { ["region"] = "eu" }) };
        await using (var first = database.Open("tenant-a"))
        {
            Assert.Same(state, await first.Store.SaveAsync(state));
            var roundTrip = await first.Store.FindAsync(state.WorkflowExecutionId);
            Assert.Equal(state.WorkflowExecutionId, roundTrip!.WorkflowExecutionId);
            Assert.Equal(state.PinnedExecutable, roundTrip.PinnedExecutable);
            Assert.Equal(state.Authority!.SystemIdentity, roundTrip.Authority!.SystemIdentity);
            Assert.Equal(state.Authority.RootInitiator, roundTrip.Authority.RootInitiator);
            Assert.True(await first.Store.DeleteAsync(state.WorkflowExecutionId));
            Assert.False(await first.Store.DeleteAsync(state.WorkflowExecutionId));
            await first.Store.SaveAsync(state);
        }
        await using var restarted = database.Open("tenant-a");
        Assert.Equal(state.WorkflowExecutionId, (await restarted.Store.FindAsync(state.WorkflowExecutionId))!.WorkflowExecutionId);
        await using var other = database.Open("tenant-b");
        Assert.Null(await other.Store.FindAsync(state.WorkflowExecutionId));
    }

    [Fact]
    public async Task History_pages_are_bounded_stable_and_cursor_bound_to_filters()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var timestamp = DateTimeOffset.UtcNow;
        foreach (var id in new[] { "d", "b", "a", "c" }) await fixture.Store.SaveAsync(State(id, "tenant-a", timestamp));
        var first = await fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(2));
        await fixture.Store.SaveAsync(State("new", "tenant-a", timestamp.AddMinutes(1)));
        var second = await fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(2, Cursor: first.NextCursor));
        Assert.Equal(["a", "b"], first.Items.Select(x => x.WorkflowExecutionId));
        Assert.Equal(["c", "d"], second.Items.Select(x => x.WorkflowExecutionId));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(2, TenantId: "other", Cursor: first.NextCursor)).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(2, TenantId: "other")).AsTask());
    }

    [Fact]
    public async Task Workflow_execution_ids_with_distinct_lone_surrogates_do_not_collide()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var first = State("\uD800", "tenant-a", DateTimeOffset.UtcNow);
        var second = State("\uD801", "tenant-a", DateTimeOffset.UtcNow.AddMinutes(1));

        await fixture.Store.SaveAsync(first);
        await fixture.Store.SaveAsync(second);

        Assert.Equal(first.WorkflowExecutionId, (await fixture.Store.FindAsync(first.WorkflowExecutionId))!.WorkflowExecutionId);
        Assert.Equal(second.WorkflowExecutionId, (await fixture.Store.FindAsync(second.WorkflowExecutionId))!.WorkflowExecutionId);
        Assert.Equal(2, await fixture.Context.WorkflowExecutionStates.CountAsync());
    }

    [Fact]
    public async Task History_cursor_round_trips_lone_surrogate_execution_ids()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var timestamp = DateTimeOffset.UtcNow;
        await fixture.Store.SaveAsync(State("\uD800", "tenant-a", timestamp));
        await fixture.Store.SaveAsync(State("\uD801", "tenant-a", timestamp));

        var first = await fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(1));
        var second = await fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(1, Cursor: first.NextCursor));

        Assert.Equal("\uD800", first.Items[0].WorkflowExecutionId);
        Assert.Equal("\uD801", second.Items[0].WorkflowExecutionId);
    }

    [Fact]
    public async Task History_cursor_rejects_distinct_lone_surrogate_definition_filters()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        const string firstDefinition = "\uD800";
        const string secondDefinition = "\uD801";
        var timestamp = DateTimeOffset.UtcNow;
        var first = State("history-a", "tenant-a", timestamp) with { PinnedExecutable = new("artifact-a", firstDefinition, "version", "1", "hash") };
        var second = State("history-b", "tenant-a", timestamp) with { PinnedExecutable = new("artifact-b", firstDefinition, "version", "1", "hash") };
        await fixture.Store.SaveAsync(first);
        await fixture.Store.SaveAsync(second);

        var firstQuery = new WorkflowExecutionStatePageQuery(1, DefinitionId: firstDefinition);
        var secondQuery = new WorkflowExecutionStatePageQuery(1, DefinitionId: secondDefinition);
        var page = await fixture.Store.QueryPageAsync(firstQuery);

        Assert.NotNull(page.NextCursor);
        Assert.NotEqual(WorkflowExecutionStateHistory.Scope(firstQuery), WorkflowExecutionStateHistory.Scope(secondQuery));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryPageAsync(secondQuery with { Cursor = page.NextCursor }).AsTask());
    }

    [Fact]
    public async Task History_and_alteration_capture_pages_reject_sizes_above_the_provider_bound()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(WorkflowExecutionStateStorePagingExtensions.MaximumPageSize + 1)).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryAlterationCapturePageAsync(new WorkflowExecutionAlterationCaptureQuery(
            "tenant-a", "system", "root", new Dictionary<string, string>(), new WorkflowAlterationQuerySelector(matchAllAuthorized: true), WorkflowExecutionStateStorePagingExtensions.MaximumPageSize + 1)).AsTask());
    }

    [Fact]
    public async Task Tenant_and_scope_values_support_the_groundwork_256_character_contract()
    {
        var tenant = new string('t', RuntimeWorkflowExecutionEfModule.TenantMaximumLength);
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open(tenant);
        var state = State("long-tenant", tenant, DateTimeOffset.UtcNow);

        await fixture.Store.SaveAsync(state);

        Assert.Equal(state.WorkflowExecutionId, (await fixture.Store.FindAsync(state.WorkflowExecutionId))!.WorkflowExecutionId);
        Assert.Single((await fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(1, TenantId: tenant))).Items);
    }

    [Fact]
    public async Task Tenant_and_scope_values_above_the_groundwork_contract_are_rejected()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var tooLong = new string('t', RuntimeWorkflowExecutionEfModule.TenantMaximumLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveAsync(State("long-tenant", tooLong, DateTimeOffset.UtcNow)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(1, TenantId: tooLong)).AsTask());
    }

    [Fact]
    public async Task Pinned_artifacts_are_projected_without_returning_state_documents()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var artifacts = new[] { "😀😀", "😀a", "😀", "\uE000", "😀" };
        for (var index = 0; index < artifacts.Length; index++)
            await fixture.Store.SaveAsync(State($"execution-{index}", "tenant-a", DateTimeOffset.UtcNow) with { PinnedExecutable = new(artifacts[index], "definition", "version", "1", "hash") });
        Assert.Equal(["😀", "😀a", "😀😀", "\uE000"], await fixture.Store.ListPinnedExecutableArtifactIdsAsync());
    }

    [Fact]
    public async Task Alteration_capture_is_immutable_and_cursor_bound()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var authorityMetadata = new Dictionary<string, string> { ["region"] = "eu\uD800" };
        var authority = new WorkflowExecutionAuthoritySnapshot("system", "root", authorityMetadata);
        await fixture.Store.SaveAsync(State("a", "tenant-a", DateTimeOffset.UtcNow) with { Authority = authority });
        await fixture.Store.SaveAsync(State("b", "tenant-a", DateTimeOffset.UtcNow) with { Authority = authority });
        var query = new WorkflowExecutionAlterationCaptureQuery("tenant-a", "system", "root", authorityMetadata, new WorkflowAlterationQuerySelector(matchAllAuthorized: true), 1);
        var first = await fixture.Store.QueryAlterationCapturePageAsync(query);
        await fixture.Store.SaveAsync(State("a", "tenant-a", DateTimeOffset.UtcNow.AddMinutes(2)) with { Authority = authority });
        var second = await fixture.Store.QueryAlterationCapturePageAsync(new WorkflowExecutionAlterationCaptureQuery("tenant-a", "system", "root", authorityMetadata, new WorkflowAlterationQuerySelector(matchAllAuthorized: true), 1, first.NextCursor));
        Assert.Equal("a", first.Items[0].WorkflowExecutionId);
        Assert.Equal("b", second.Items[0].WorkflowExecutionId);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryAlterationCapturePageAsync(new WorkflowExecutionAlterationCaptureQuery("tenant-a", "system", "root", new Dictionary<string, string> { ["region"] = "us\uD800" }, new WorkflowAlterationQuerySelector(matchAllAuthorized: true), 1, first.NextCursor)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryAlterationCapturePageAsync(new WorkflowExecutionAlterationCaptureQuery("tenant-a", "system", "root", authorityMetadata, new WorkflowAlterationQuerySelector(definitionId: "definition", matchAllAuthorized: true), 1, first.NextCursor)).AsTask());
    }

    [Fact]
    public async Task Corrupt_projection_is_rejected_fail_closed()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(State("execution", "tenant-a", DateTimeOffset.UtcNow));
        var row = await fixture.Context.WorkflowExecutionStates.SingleAsync();
        row.Status = (int)WorkflowExecutionStatus.Faulted;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync("execution").AsTask());
    }

    [Fact]
    public async Task Save_rejects_state_from_another_tenant_scope()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.SaveAsync(State("execution", "tenant-b", DateTimeOffset.UtcNow)).AsTask());
    }

    [Fact]
    public async Task Invalid_revision_and_json_fail_closed_without_poisoning_the_tracker()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var state = State("execution", "tenant-a", DateTimeOffset.UtcNow);
        await fixture.Store.SaveAsync(state);
        var row = await fixture.Context.WorkflowExecutionStates.SingleAsync();
        row.Revision = 0;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.SaveAsync(state with { Status = WorkflowExecutionStatus.Running }).AsTask());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        row = await fixture.Context.WorkflowExecutionStates.SingleAsync();
        row.Revision = 1;
        await fixture.Context.SaveChangesAsync();
        row.ContentJson = "not-json";
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync(state.WorkflowExecutionId).AsTask());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Malformed_encoded_projection_is_reported_as_invalid_data_without_poisoning_the_tracker()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var state = State("execution", "tenant-a", DateTimeOffset.UtcNow);
        await fixture.Store.SaveAsync(state);
        var row = await fixture.Context.WorkflowExecutionStates.SingleAsync();
        row.ArtifactId = "not-base64";
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ListPinnedExecutableArtifactIdsAsync().AsTask());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Concurrent_create_race_leaves_one_authoritative_row_and_recoverable_contexts()
    {
        await using var database = await Database.CreateAsync();
        var state = State("race", "tenant-a", DateTimeOffset.UtcNow);
        await using var left = database.Open("tenant-a");
        await using var right = database.Open("tenant-a");
        var outcomes = await Task.WhenAll(
            Capture(left.Store.SaveAsync(state)),
            Capture(right.Store.SaveAsync(state)));
        Assert.All(outcomes, outcome => Assert.True(outcome is null or InvalidOperationException));
        await using var verification = database.Open("tenant-a");
        Assert.NotNull(await verification.Store.FindAsync(state.WorkflowExecutionId));
        Assert.Single(await verification.Context.WorkflowExecutionStates.ToArrayAsync());
        left.Context.ChangeTracker.Clear();
        right.Context.ChangeTracker.Clear();
        Assert.Empty(left.Context.ChangeTracker.Entries());
        Assert.Empty(right.Context.ChangeTracker.Entries());
    }

    private static async Task<Exception?> Capture(ValueTask<WorkflowExecutionState> operation)
    {
        try { await operation; return null; }
        catch (Exception exception) when (exception is InvalidOperationException) { return exception; }
    }

    private static WorkflowExecutionState State(string id, string tenant, DateTimeOffset timestamp) => new(id, new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Completed, null, timestamp.AddMinutes(-1), timestamp.AddMinutes(-1), timestamp, timestamp, null, null, tenant, new Dictionary<string, string>());

    private sealed class DerivedFeature : RuntimeWorkflowExecutionEntityFrameworkCoreFeature
    {
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _connectionString;
        private Database(SqliteConnection connection, string connectionString) { _connection = connection; _connectionString = connectionString; }
        public static async Task<Database> CreateAsync()
        {
            var connectionString = $"Data Source=file:elsa-runtime-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Database(connection, connectionString);
        }
        public Fixture Open(string scope) => new(_connectionString, scope);
        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public readonly BookmarkStateSqliteDbContext Context;
        public readonly EfWorkflowExecutionStateStore Store;
        public Fixture(string connectionString, string scope)
        {
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
            Context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(_connection).Options);
            Store = new EfWorkflowExecutionStateStore(Context, new Accessor(scope), new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "01234567890123456789012345678901" })));
        }
        public async ValueTask DisposeAsync()
        {
            try { await Context.DisposeAsync(); }
            finally { await _connection.DisposeAsync(); }
        }
    }
    private sealed class Accessor(string value) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(value)); }
}
