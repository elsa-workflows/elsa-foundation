using System.Data.Common;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class StandaloneTriggerResourcePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_migrates_and_retains_scoped_resources_paging_bindings_and_mutation_receipts() =>
        StandaloneTriggerResourceProviderSmoke.RunAsync(fixture, "PostgreSql", connection =>
            new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options));
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class StandaloneTriggerResourceSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_migrates_and_retains_scoped_resources_paging_bindings_and_mutation_receipts() =>
        StandaloneTriggerResourceProviderSmoke.RunAsync(fixture, "SqlServer", connection =>
            new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options));
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class StandaloneTriggerResourceMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_migrates_and_retains_scoped_resources_paging_bindings_and_mutation_receipts() =>
        StandaloneTriggerResourceProviderSmoke.RunAsync(fixture, "MySql", connection =>
            new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options));
}

/// <summary>Real native engines exercise the new tables and translated paging, beyond the deep SQLite protocol cases.</summary>
internal static class StandaloneTriggerResourceProviderSmoke
{
    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, string providerName, Func<string, RuntimeDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The required native provider is unavailable.");
        var database = $"standalone_{Guid.NewGuid():N}";
        var connection = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        connection.Remove("Initial Catalog"); connection["Database"] = database;
        try
        {
            await using (var admin = createContext(fixture.ConnectionString))
                await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database}");
            var services = new ServiceCollection(); services.AddLogging(); services.AddWorkflowRuntime();
            services.AddRuntimeEntityFrameworkCore(new()
            {
                Provider = providerName, ConnectionString = connection.ConnectionString,
                HierarchyCursorSigningKey = "standalone-native-provider-hierarchy-key-32bytes",
                RecoveryContinuationSigningKey = "standalone-native-provider-recovery-key-32bytes"
            });
            services.AddEfModuleMigrations<RuntimeDbContext>(providerName);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            foreach (var initializer in provider.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
            var scope = new TriggerScope(new($"native-{Guid.NewGuid():N}"), "resource-tenant");
            var now = DateTimeOffset.UtcNow;
            var exactValue = JsonSerializer.SerializeToElement(new { value = ulong.MaxValue, text = "123" });
            StandaloneTrigger Resource(string id, long revision = 1) => new(id, "Reconcile Accounts " + id, "event", 1,
                JsonSerializer.SerializeToElement(new { eventName = "provider.broadcast" }), revision, revision, 1,
                TriggerDesiredState.Paused, new(TriggerManagementKind.Studio), now, now.AddSeconds(revision), "departed-creator", tenantId: scope.TenantId);
            TriggerSubscription Subscription(string id) => new("subscription-" + id, id, "workflow-" + id, "default", "native", 1, 1, false,
                [new("payload", new TriggerLiteralBinding(exactValue))], new(TriggerManagementKind.Studio));
            var create = new TriggerStoreWrite("create-A", "fingerprint-A", "manager", null, Resource("A"), [Subscription("A")]);
            TriggerMutationReceipt receipt;
            await using (var first = Open())
            {
                var context = first.ServiceProvider.GetRequiredService<RuntimeDbContext>();
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
                var store = first.ServiceProvider.GetRequiredService<IStandaloneTriggerStore>();
                receipt = await store.WriteAsync(scope, create);
                await store.WriteAsync(scope, new("create-B", "fingerprint-B", "manager", null, Resource("B"), [Subscription("B")]));
            }
            await using (var restarted = Open())
            {
                var store = restarted.ServiceProvider.GetRequiredService<IStandaloneTriggerStore>();
                var first = await store.ListAsync(scope, new(Limit: 1, Search: "reconcile"));
                Assert.Equal("A", Assert.Single(first.Items).Id); Assert.NotNull(first.Next);
                var second = await store.ListAsync(scope, new(Limit: 1, After: first.Next, Search: "reconcile"));
                Assert.Equal("B", Assert.Single(second.Items).Id); Assert.Null(second.Next);
                var row = Assert.Single((await store.ListSubscriptionsAsync(scope, new(DefinitionId: "workflow-A", SlotName: "default"))).Items);
                Assert.Equal(JsonSerializer.Serialize(Subscription("A")), JsonSerializer.Serialize(row));
                Assert.Contains("18446744073709551615", JsonSerializer.Serialize(row));
                Assert.Null(await store.FindAsync(scope with { TenantId = "other-tenant" }, "A"));
                await store.WriteAsync(scope, new("update-A", "fingerprint-update", "manager", 1, Resource("A", 2), [row]));
            }
            await using (var replayed = Open())
            {
                var store = replayed.ServiceProvider.GetRequiredService<IStandaloneTriggerStore>();
                Assert.Equal(receipt, await store.WriteAsync(scope, create));
                var conflict = await Assert.ThrowsAsync<TriggerOperationException>(() => store.WriteAsync(scope,
                    create with { RequestFingerprint = "changed" }).AsTask());
                Assert.Equal("conflict", conflict.Code);
                Assert.Equal(2, (await store.FindAsync(scope, "A"))!.Trigger.Revision);
                Assert.Equal(receipt, await store.FindMutationAsync(scope, "manager", "create-A"));
                var stale = await Assert.ThrowsAsync<TriggerOperationException>(() => store.WriteAsync(scope,
                    new("stale-A", "stale", "manager", 1, Resource("A", 3), [Subscription("A")])).AsTask());
                Assert.Equal("conflict", stale.Code);
                Assert.Null(await store.FindMutationAsync(scope, "manager", "stale-A"));
            }
            AsyncServiceScope Open()
            {
                var opened = provider.CreateAsyncScope();
                opened.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(scope.Persistence));
                return opened;
            }
        }
        finally
        {
            // The provider and all scopes above are disposed before deleting only this owned database.
            await using var cleanup = createContext(connection.ConnectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
