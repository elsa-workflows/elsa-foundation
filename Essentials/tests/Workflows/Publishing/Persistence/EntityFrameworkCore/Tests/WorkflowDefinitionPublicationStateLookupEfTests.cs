using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class WorkflowDefinitionPublicationStateLookupEfTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SQLite_empty_slot_after_unpublish_is_retracted_not_published()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Authority.TryActivateAsync(new("orders", "default", "publication-orders", WorkflowActivationSource.Publishing, 0, Now))).Succeeded);
        Assert.True((await fixture.Authority.TryDeactivateAsync("orders", "default", WorkflowActivationSource.Publishing, 1, Now)).Succeeded);

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Retracted, states["orders"]);
    }

    [Fact]
    public async Task SQLite_live_slot_plus_an_empty_slot_on_the_same_definition_is_published()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Authority.TryActivateAsync(new("orders", "default", "publication-live", WorkflowActivationSource.Publishing, 0, Now))).Succeeded);
        Assert.True((await fixture.Authority.TryActivateAsync(new("orders", "canary", "publication-empty", WorkflowActivationSource.Publishing, 0, Now))).Succeeded);
        Assert.True((await fixture.Authority.TryDeactivateAsync("orders", "canary", WorkflowActivationSource.Publishing, 1, Now)).Succeeded);

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["orders"]);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly RuntimeSqliteDbContext context;

        private Fixture(SqliteConnection connection, RuntimeSqliteDbContext context, EfWorkflowActivationAuthority authority)
        {
            this.connection = connection;
            this.context = context;
            Authority = authority;
            Lookup = new WorkflowDefinitionPublicationStateLookup(authority, new InMemoryPublicationRecordStore());
        }

        public EfWorkflowActivationAuthority Authority { get; }
        public IWorkflowDefinitionPublicationStateLookup Lookup { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, context, new EfWorkflowActivationAuthority(context, TestAccess.Scoped("tenant-a")));
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
