using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ProviderFailures;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowTriggerBindingStoreTests
{
    [Fact]
    public void Registration_is_explicit_and_uses_the_shared_operational_context()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new RuntimeOperationalStateEntityFrameworkCoreOptions
        {
            Provider = "Sqlite", ConnectionString = "Data Source=:memory:"
        });
        services.AddRuntimeWorkflowTriggerBindingEntityFrameworkCore();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfWorkflowTriggerBindingStore>(scope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>());
        Assert.IsType<WorkflowTriggerBindingStoreBackend>(provider.GetRequiredService<WorkflowTriggerBindingStoreBackend>());
        Assert.IsType<BookmarkStateSqliteDbContext>(scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>());
    }

    [Fact]
    public void Registration_rejects_and_rolls_back_an_unowned_trigger_binding_descriptor()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new RuntimeOperationalStateEntityFrameworkCoreOptions
        {
            Provider = "Sqlite", ConnectionString = "Data Source=:memory:"
        });
        services.AddScoped<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeWorkflowTriggerBindingEntityFrameworkCore());
        Assert.Equal(before, services);
    }

    [Fact]
    public void Registration_rolls_back_provider_owned_state_when_a_selected_backend_withdrawal_fails()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new RuntimeOperationalStateEntityFrameworkCoreOptions
        {
            Provider = "Sqlite", ConnectionString = "Data Source=:memory:"
        });
        var state = new MutableRegistrationState();
        services.AddSingleton<IRuntimePersistenceRegistrationState>(state);
        services.AddScoped<InMemoryWorkflowTriggerBindingStore>();
        var concrete = services.Last();
        var contract = ServiceDescriptor.Scoped<IWorkflowTriggerBindingStore>(provider =>
            provider.GetRequiredService<InMemoryWorkflowTriggerBindingStore>());
        ((IServiceCollection)services).Add(contract);
        WorkflowTriggerBindingStoreBackend.Register(services, new(
            WorkflowTriggerBindingStoreBackend.InMemory, contract, concrete,
            _ => { state.Version++; throw new InvalidOperationException("Late backend withdrawal failed."); }));
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeWorkflowTriggerBindingEntityFrameworkCore());
        Assert.Equal(before, services);
        Assert.Equal(0, state.Version);
        WorkflowTriggerBindingStoreBackend.Find(services)!.EnsureOwnsRegisteredContract(services);
    }

    [Fact]
    public async Task Revisions_are_positive_monotonic_and_optimistic_concurrency_tokens()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var property = context.Model.FindEntityType(typeof(WorkflowTriggerBindingEntity))!.FindProperty(nameof(WorkflowTriggerBindingEntity.Revision))!;
        Assert.True(property.IsConcurrencyToken);

        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        var binding = Binding("revision", null, "revision-a");
        await store.SaveAsync(binding);
        Assert.Equal(1, await context.WorkflowTriggerBindings.Select(x => x.Revision).SingleAsync());
        await store.SaveAsync(binding with { StimulusHash = "revision-b" });
        Assert.Equal(2, await context.WorkflowTriggerBindings.Select(x => x.Revision).SingleAsync());
    }

    [Fact]
    public async Task Stores_are_scoped_bounded_and_activation_transitions_are_atomic()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        var first = Binding("a", "activation-a", "hash-a");
        var second = Binding("b", "activation-a", "hash-b");
        await store.PrepareActivationAsync("activation-a", [first, second]);
        Assert.Equal(new[] { first.TriggerBindingId, second.TriggerBindingId }.Order(StringComparer.Ordinal),
            (await store.ListByActivationAsync(new WorkflowTriggerBindingActivationPageQuery("activation-a"))).Items.Select(x => x.TriggerBindingId));
        Assert.Empty((await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "hash-a"))).Items);
        await store.ActivateAsync("activation-a", null);
        Assert.Single((await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "hash-a"))).Items);
        Assert.Equal(2, await store.DeleteByArtifactAsync("artifact-a"));
        Assert.Empty((await store.ListByActivationAsync(new WorkflowTriggerBindingActivationPageQuery("activation-a"))).Items);

        var other = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-b"));
        await other.SaveAsync(Binding("a", null, "hash-a"));
        Assert.Empty((await store.ListByStimulusTypeAsync(new WorkflowTriggerBindingTypePageQuery("Event"))).Items);
        Assert.Single((await other.ListByStimulusTypeAsync(new WorkflowTriggerBindingTypePageQuery("Event"))).Items);
    }

    [Fact]
    public async Task Preparation_reconciles_activation_rows_saved_before_projection_state()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        var binding = Binding("saved-first", "activation-saved-first", "saved-first-hash");

        await store.SaveAsync(binding);
        await store.PrepareActivationAsync(binding.ActivationId!, [binding]);

        var row = await context.WorkflowTriggerBindings.SingleAsync();
        Assert.Equal(2, row.Revision);
        Assert.Equal(binding.TriggerBindingId, (await store.ListByActivationAsync(new WorkflowTriggerBindingActivationPageQuery(binding.ActivationId!))).Items.Single().TriggerBindingId);
    }

    [Fact]
    public void Registration_replaces_the_stock_trigger_default_when_the_feature_has_run()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new RuntimeOperationalStateEntityFrameworkCoreOptions
        {
            Provider = "Sqlite", ConnectionString = "Data Source=:memory:"
        });
        services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();

        services.AddRuntimeWorkflowTriggerBindingEntityFrameworkCore();

        Assert.IsType<EfWorkflowTriggerBindingStore>(services.BuildServiceProvider().GetRequiredService<IWorkflowTriggerBindingStore>());
    }

    [Fact]
    public async Task Continuation_is_query_bound_and_projection_corruption_fails_closed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        await store.SaveAsync(Binding("a", null, "hash-a"));
        await store.SaveAsync(Binding("b", null, "hash-b"));
        var page = await store.ListByStimulusTypeAsync(new WorkflowTriggerBindingTypePageQuery("Event", 1));
        Assert.NotNull(page.NextContinuationToken);
        var next = await store.ListByStimulusTypeAsync(new WorkflowTriggerBindingTypePageQuery("Event", 1, page.NextContinuationToken));
        Assert.Single(next.Items);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListByStimulusTypeAsync(new WorkflowTriggerBindingTypePageQuery("Other", 1, page.NextContinuationToken)).AsTask());
        var row = await context.WorkflowTriggerBindings.FirstAsync();
        row.StimulusTypeLookupKey = "corrupt";
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ListByStimulusTypeAsync(new WorkflowTriggerBindingTypePageQuery("Event")).AsTask());
    }

    [Fact]
    public async Task Preparation_is_idempotent_but_conflicting_replay_and_missing_rows_fail_closed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        var binding = Binding("a", "activation-a", "hash-a");
        await store.PrepareActivationAsync("activation-a", [binding]);
        await store.PrepareActivationAsync("activation-a", [binding]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PrepareActivationAsync("activation-a", [Binding("b", "activation-a", "hash-b")]).AsTask());

        context.WorkflowTriggerBindings.RemoveRange(context.WorkflowTriggerBindings);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ActivateAsync("activation-a", null).AsTask());
    }

    [Fact]
    public async Task Activation_delete_fails_closed_when_a_binding_row_is_corrupt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        await store.PrepareActivationAsync("activation-corrupt-row", [Binding("corrupt-row", "activation-corrupt-row", "corrupt-row-hash")]);
        var row = await context.WorkflowTriggerBindings.SingleAsync();
        row.ContentJson = "corrupt";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.DeleteByActivationAsync("activation-corrupt-row").AsTask());
        Assert.Single(await context.WorkflowTriggerBindings.AsNoTracking().ToArrayAsync());
        Assert.Single(await context.WorkflowTriggerBindingProjectionStates.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Activation_delete_fails_closed_when_projection_state_is_corrupt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        await store.PrepareActivationAsync("activation-corrupt-state", [Binding("corrupt-state", "activation-corrupt-state", "corrupt-state-hash")]);
        var state = await context.WorkflowTriggerBindingProjectionStates.SingleAsync();
        state.ContentJson = "corrupt";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.DeleteByActivationAsync("activation-corrupt-state").AsTask());
        Assert.Single(await context.WorkflowTriggerBindings.AsNoTracking().ToArrayAsync());
        Assert.Single(await context.WorkflowTriggerBindingProjectionStates.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Activation_replaces_only_the_prior_active_projection_and_split_artifacts_roll_back()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        await store.PrepareActivationAsync("old", [Binding("old", "old", "old-hash", "artifact-old")]);
        await store.ActivateAsync("old", null);
        await store.PrepareActivationAsync("new", [Binding("new", "new", "new-hash", "artifact-new")]);
        await store.ActivateAsync("new", "old");
        Assert.Empty((await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "old-hash"))).Items);
        Assert.Single((await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "new-hash"))).Items);

        await store.PrepareActivationAsync("split", [Binding("split-a", "split", "split-a", "artifact-a"), Binding("split-b", "split", "split-b", "artifact-b")]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteByArtifactAsync("artifact-a").AsTask());
        Assert.Equal(2, (await store.ListByActivationAsync(new WorkflowTriggerBindingActivationPageQuery("split"))).Items.Count);
    }

    [Fact]
    public async Task Competing_activate_using_stale_context_is_mapped_to_a_semantic_conflict_and_rolled_back()
    {
        const string connectionString = "Data Source=file:trigger-binding-cas;Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using var contextA = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connectionString).Options);
        await using var contextB = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connectionString).Options);
        await contextA.Database.EnsureCreatedAsync();
        var binding = Binding("cas", "activation-cas", "cas-hash");
        var storeA = new EfWorkflowTriggerBindingStore(contextA, new Accessor("tenant-a"));
        var storeB = new EfWorkflowTriggerBindingStore(contextB, new Accessor("tenant-a"));
        await storeA.PrepareActivationAsync("activation-cas", [binding]);

        // Keep a stale state and row tracked in B. A then advances both revision tokens before B
        // attempts the same activation, forcing EF's optimistic-concurrency predicate to fail.
        _ = await contextB.WorkflowTriggerBindingProjectionStates.SingleAsync();
        _ = await contextB.WorkflowTriggerBindings.ToArrayAsync();
        await storeA.ActivateAsync("activation-cas", null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => storeB.ActivateAsync("activation-cas", null).AsTask());
        Assert.Contains("changed concurrently", exception.Message, StringComparison.Ordinal);
        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);
        Assert.Empty(contextB.ChangeTracker.Entries());
        Assert.True((await storeA.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "cas-hash"))).Items.Single().IsActive);
    }

    [Fact]
    public async Task Artifact_delete_validates_activationless_authoritative_rows_before_removing_them()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        await store.SaveAsync(Binding("corrupt-delete", null, "corrupt-delete-hash", "corrupt-delete-artifact"));
        var row = await context.WorkflowTriggerBindings.SingleAsync();
        row.StimulusTypeLookupKey = "corrupt";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.DeleteByArtifactAsync("corrupt-delete-artifact").AsTask());
        Assert.Equal(1, await context.WorkflowTriggerBindings.CountAsync());
    }

    [Theory]
    [InlineData("envelope")]
    [InlineData("active-state")]
    public async Task Activation_rejects_corrupt_projection_state_without_changing_binding_rows(string corruption)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        await store.PrepareActivationAsync("activation-a", [Binding("state", "activation-a", "state-hash")]);
        var state = await context.WorkflowTriggerBindingProjectionStates.SingleAsync();
        if (corruption == "envelope") state.ContentJson = "corrupt";
        else state.IsActive = true;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ActivateAsync("activation-a", null).AsTask());
        Assert.False((await context.WorkflowTriggerBindings.AsNoTracking().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Activation_reports_a_transient_conflict_the_provider_execution_strategy_wrapped_and_rolls_back()
    {
        await using var activation = await PreparedActivation.CreateAsync(FailingSaveInterceptor.WrappedDeadlock());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => activation.Store.ActivateAsync("activation-a", null).AsTask());

        Assert.EndsWith("encountered a transient write conflict; retry the operation.", failure.Message, StringComparison.Ordinal);
        Assert.Empty(activation.Context.ChangeTracker.Entries());
        Assert.Empty((await activation.Store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "hash-a"))).Items);
    }

    [Fact]
    public async Task Activation_does_not_report_a_wrapped_provider_failure_that_is_not_a_transient_conflict_as_one()
    {
        var saves = FailingSaveInterceptor.WrappedProviderFailure();
        await using var activation = await PreparedActivation.CreateAsync(saves);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => activation.Store.ActivateAsync("activation-a", null).AsTask());

        Assert.DoesNotContain("transient write conflict", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, saves.Attempts);
    }

    private static WorkflowTriggerBinding Binding(string id, string? activationId, string stimulusHash, string artifactId = "artifact-a") =>
        new(WorkflowTriggerBinding.BuildId(activationId is null ? artifactId : activationId, artifactId, "node-" + id, stimulusHash), artifactId, "definition-a", "1", "artifact-hash", "node-" + id, "Event", stimulusHash, null, new Dictionary<string, string> { ["k"] = id }, DateTimeOffset.UnixEpoch, activationId, activationId is null ? null : "slot-a");

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class PreparedActivation : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private PreparedActivation(SqliteConnection connection, BookmarkStateSqliteDbContext context)
        {
            this.connection = connection;
            Context = context;
            Store = new EfWorkflowTriggerBindingStore(context, new Accessor("tenant-a"));
        }

        public BookmarkStateSqliteDbContext Context { get; }

        public EfWorkflowTriggerBindingStore Store { get; }

        /// <summary>Prepares activation-a with one binding, then opens the store through a context whose saves run <paramref name="saves"/>.</summary>
        public static async Task<PreparedActivation> CreateAsync(IInterceptor saves)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using (var seed = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options))
            {
                await seed.Database.EnsureCreatedAsync();
                await new EfWorkflowTriggerBindingStore(seed, new Accessor("tenant-a")).PrepareActivationAsync("activation-a", [Binding("a", "activation-a", "hash-a")]);
            }
            return new PreparedActivation(connection, new(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>()
                .UseSqlite(connection).AddInterceptors(saves).Options));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class MutableRegistrationState : IRuntimePersistenceRegistrationState
    {
        public int Version { get; set; }
        public IRuntimePersistenceRegistrationSnapshot CaptureSnapshot() => new Snapshot(this, Version);

        private sealed class Snapshot(MutableRegistrationState state, int version) : IRuntimePersistenceRegistrationSnapshot
        {
            public void Rollback() => state.Version = version;
        }
    }
}
