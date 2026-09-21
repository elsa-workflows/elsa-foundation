using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ProviderFailures;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowActivationAuthorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowActivationSource Importer = WorkflowActivationSource.ArtifactReconciliation("drop-1");

    [Fact]
    public void Registration_is_opt_in_and_uses_the_shared_operational_context()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
        services.AddRuntimeWorkflowActivationAuthorityEntityFrameworkCore();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfWorkflowActivationAuthority>(scope.ServiceProvider.GetRequiredService<IWorkflowActivationAuthority>());
        Assert.IsType<WorkflowActivationAuthorityBackend>(provider.GetRequiredService<WorkflowActivationAuthorityBackend>());
        Assert.IsType<RuntimeSqliteDbContext>(scope.ServiceProvider.GetRequiredService<RuntimeDbContext>());
    }

    [Fact]
    public void Registration_refuses_an_unowned_activation_authority_without_mutating_services()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
        services.AddScoped<IWorkflowActivationAuthority>(_ => throw new InvalidOperationException("Foreign authority."));
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeWorkflowActivationAuthorityEntityFrameworkCore());
        Assert.Equal(before, services);
    }

    [Fact]
    public async Task SQLite_proves_scope_isolation_listing_and_owner_takeover()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = NewContext(connection);
        await context.Database.EnsureCreatedAsync();
        var authority = new EfWorkflowActivationAuthority(context, new Accessor("tenant-a"));

        var first = await authority.TryActivateAsync(new("definition-1", "default", "activation-a", WorkflowActivationSource.Publishing, 0, Now));
        Assert.True(first.Succeeded);
        Assert.Equal(1, first.Slot.Revision);
        var foreign = await authority.TryActivateAsync(new("definition-1", "default", "activation-b", Importer, 1, Now));
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
        var takeover = await authority.TryActivateAsync(new("definition-1", "default", "activation-b", Importer, 1, Now, WorkflowActivationOwnershipIntent.TakeOver));
        Assert.True(takeover.Succeeded);
        Assert.Equal(WorkflowActivationSource.Publishing, takeover.ReplacedSource);
        var cleared = await authority.TryDeactivateAsync("definition-1", "default", Importer, 2, Now);
        Assert.True(cleared.Succeeded);
        Assert.Null(cleared.Slot.ActiveActivationId);

        for (var index = 0; index < 101; index++)
            Assert.True((await authority.TryActivateAsync(new("definition-paged", $"slot-{index:D3}", $"activation-{index:D3}", Importer, 0, Now))).Succeeded);
        var listed = await authority.ListByDefinitionAsync("definition-paged");
        Assert.Equal(101, listed.Count);
        Assert.Equal(Enumerable.Range(0, 101).Select(x => $"slot-{x:D3}"), listed.Select(x => x.SlotName));

        var other = new EfWorkflowActivationAuthority(context, new Accessor("tenant-b"));
        Assert.True((await other.TryActivateAsync(new("definition-1", "default", "activation-a", WorkflowActivationSource.Publishing, 0, Now))).Succeeded);
        Assert.Null(await authority.FindAsync("definition-1", "missing"));
        Assert.NotNull(await other.FindAsync("definition-1", "default"));
    }

    [Fact]
    public async Task SQLite_proves_activation_identity_uniqueness_revision_cas_and_restart_recovery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var firstContext = NewContext(connection);
        await firstContext.Database.EnsureCreatedAsync();
        var first = new EfWorkflowActivationAuthority(firstContext, new Accessor("tenant-a"));
        var claimed = await first.TryActivateAsync(new("definition-1", "default", "activation-a", WorkflowActivationSource.Publishing, 0, Now));
        Assert.True(claimed.Succeeded);

        var duplicate = await first.TryActivateAsync(new("definition-1", "canary", "activation-a", WorkflowActivationSource.Publishing, 0, Now));
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, duplicate.Conflict);
        var stale = await first.TryDeactivateAsync("definition-1", "default", WorkflowActivationSource.Publishing, 0, Now);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);

        await using var restartedContext = NewContext(connection);
        var restarted = new EfWorkflowActivationAuthority(restartedContext, new Accessor("tenant-a"));
        var restored = await restarted.FindAsync("definition-1", "default");
        Assert.Equal(claimed.Slot, restored);
        var released = await restarted.TryDeactivateAsync("definition-1", "default", WorkflowActivationSource.Publishing, 1, Now);
        Assert.True(released.Succeeded);
        Assert.Null((await restarted.FindAsync("definition-1", "default"))!.ActiveActivationId);
    }

    [Fact]
    public async Task SQLite_accepts_maximum_definition_and_slot_name_identities()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = NewContext(connection);
        await context.Database.EnsureCreatedAsync();
        var authority = new EfWorkflowActivationAuthority(context, new Accessor("tenant-a"));
        var definitionId = new string('d', RuntimeOperationalStateEfModule.IdentityMaximumLength);
        var slotName = new string('s', RuntimeOperationalStateEfModule.IdentityMaximumLength);
        var request = new WorkflowActivationSlotRequest(definitionId, slotName, "activation-boundary", WorkflowActivationSource.Publishing, 0, Now);

        var activated = await authority.TryActivateAsync(request);
        Assert.True(activated.Succeeded);
        Assert.Equal(activated.Slot, await authority.FindAsync(definitionId, slotName));
        Assert.Equal(slotName, (await authority.ListByDefinitionAsync(definitionId)).Single().SlotName);

        var deactivated = await authority.TryDeactivateAsync(definitionId, slotName, WorkflowActivationSource.Publishing, activated.Slot.Revision, Now);
        Assert.True(deactivated.Succeeded);
        Assert.Null((await authority.FindAsync(definitionId, slotName))!.ActiveActivationId);
    }

    [Fact]
    public async Task SQLite_concurrent_first_claims_have_one_winner_and_one_cas_conflict()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var schema = NewContext(connection);
        await schema.Database.EnsureCreatedAsync();
        await using var contextA = NewContext(connection);
        await using var contextB = NewContext(connection);
        var first = new EfWorkflowActivationAuthority(contextA, new Accessor("tenant-a"));
        var second = new EfWorkflowActivationAuthority(contextB, new Accessor("tenant-a"));
        var results = await Task.WhenAll(
            first.TryActivateAsync(new("definition-race", "default", "a", WorkflowActivationSource.Publishing, 0, Now)).AsTask(),
            second.TryActivateAsync(new("definition-race", "default", "b", WorkflowActivationSource.Publishing, 0, Now)).AsTask());
        Assert.Single(results, x => x.Succeeded);
        Assert.Single(results, x => !x.Succeeded && x.Conflict == WorkflowActivationConflict.RevisionMismatch);
    }

    [Fact]
    public async Task A_corrupt_activation_lookup_fails_closed_before_claiming_another_slot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = NewContext(connection);
        await context.Database.EnsureCreatedAsync();
        var authority = new EfWorkflowActivationAuthority(context, new Accessor("tenant-a"));
        Assert.True((await authority.TryActivateAsync(new("definition-a", "slot-a", "activation-a", Importer, 0, Now))).Succeeded);
        var row = await context.WorkflowActivationSlots.SingleAsync();
        row.ContentJson = "corrupt";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => authority.TryActivateAsync(
            new("definition-b", "slot-b", "activation-a", Importer, 0, Now)).AsTask());
        Assert.Single(await context.WorkflowActivationSlots.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task A_transient_conflict_the_provider_execution_strategy_wrapped_around_a_save_is_retried()
    {
        var saves = FailingSaveInterceptor.WrappedDeadlock(failures: 1);
        await using var database = await ActivationDatabase.CreateAsync(saves);

        var activated = await database.Authority.TryActivateAsync(new("definition-1", "default", "activation-a", Importer, 0, Now));

        Assert.True(activated.Succeeded);
        Assert.Equal(activated.Slot, await database.Authority.FindAsync("definition-1", "default"));
        Assert.Equal(2, saves.Attempts);
    }

    [Fact]
    public async Task A_wrapped_provider_failure_that_is_not_a_transient_conflict_fails_without_a_retry()
    {
        var saves = FailingSaveInterceptor.WrappedProviderFailure();
        await using var database = await ActivationDatabase.CreateAsync(saves);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Authority.TryActivateAsync(
            new("definition-1", "default", "activation-a", Importer, 0, Now)).AsTask());

        Assert.Equal(1, saves.Attempts);
    }

    [Fact]
    public async Task A_transient_conflict_the_provider_execution_strategy_wrapped_around_a_read_is_not_retried()
    {
        var reads = new FailingReadInterceptor(() => WrappedByExecutionStrategy(new SqlException(Deadlock)));
        await using var database = await ActivationDatabase.CreateAsync(reads);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Authority.TryActivateAsync(
            new("definition-1", "default", "activation-a", Importer, 0, Now)).AsTask());

        Assert.Equal(1, reads.Attempts);
    }

    private static RuntimeSqliteDbContext NewContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class ActivationDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly RuntimeSqliteDbContext context;

        private ActivationDatabase(SqliteConnection connection, RuntimeSqliteDbContext context)
        {
            this.connection = connection;
            this.context = context;
            Authority = new EfWorkflowActivationAuthority(context, new Accessor("tenant-a"));
        }

        public EfWorkflowActivationAuthority Authority { get; }

        /// <summary>Creates the schema, then opens the authority through a context that runs <paramref name="interceptor"/>.</summary>
        public static async Task<ActivationDatabase> CreateAsync(IInterceptor interceptor)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using (var schema = NewContext(connection))
                await schema.Database.EnsureCreatedAsync();
            return new ActivationDatabase(connection, new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
                .UseSqlite(connection).AddInterceptors(interceptor).Options));
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
