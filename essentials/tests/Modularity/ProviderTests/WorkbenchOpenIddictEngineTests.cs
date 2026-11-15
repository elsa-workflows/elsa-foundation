using Elsa.Modularity.Tests;
using Elsa.Workbench;
using Elsa.Workbench.OpenIddict;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using OpenIddict.Abstractions;
using Xunit;

namespace Elsa.Modularity.ProviderTests;

/// <summary>
/// Workbench's OpenIddict store on a native database engine, run once per engine below. The scenario that makes the engine matter:
/// access-token validation reads the token's row, so a token one node issued is only valid on another when the nodes share a store.
/// </summary>
public abstract class WorkbenchOpenIddictEngineTests(OpenIddictEngineDatabase engine) : IAsyncLifetime
{
    private const string Section = "CShells:Shells:default:Features:FoundationIdentityOpenIddict";

    private readonly List<ServiceProvider> _nodes = [];
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        if (engine.SkipReason is null)
            _connectionString = await engine.CreateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var node in _nodes)
            await node.DisposeAsync();
    }

    [SkippableFact]
    public async Task Migrates_the_store_and_a_token_one_node_issued_is_found_by_another()
    {
        Skip.If(engine.SkipReason is not null, engine.SkipReason);
        var issuer = await StartNodeAsync();
        var validator = await StartNodeAsync();

        string id;
        await using (var scope = issuer.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database;
            Assert.Equal(1, (await database.GetAppliedMigrationsAsync()).Count());
            Assert.Empty(await database.GetPendingMigrationsAsync());
            id = await WorkbenchOpenIddictTestHost.CreateTokenAsync(scope.ServiceProvider, "issued-on-one-node");
        }

        await using var other = validator.CreateAsyncScope();
        Assert.NotNull(await other.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().FindByIdAsync(id));
    }

    /// <summary>Nodes that start together all migrate the store, and the engine's own migration lock lets each of them through.</summary>
    [SkippableFact]
    public async Task Nodes_that_start_together_migrate_the_store_once()
    {
        Skip.If(engine.SkipReason is not null, engine.SkipReason);
        var nodes = new[] { Node(), Node(), Node() };

        await Task.WhenAll(nodes.Select(WorkbenchOpenIddictTestHost.StartAsync));

        await using var scope = nodes[0].CreateAsyncScope();
        Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.GetAppliedMigrationsAsync()).Count());
    }

    [SkippableFact]
    public async Task A_prune_removes_expired_and_redeemed_entries_and_keeps_the_rest()
    {
        Skip.If(engine.SkipReason is not null, engine.SkipReason);
        var node = await StartNodeAsync();
        var scenario = new OpenIddictPruneScenario(node, _time);
        var entries = await scenario.SeedAsync(extraExpired: 20);

        await node.GetServices<IHostedService>().OfType<WorkbenchOpenIddictPruningService>().Single().PruneAsync(CancellationToken.None);

        Assert.Equal(entries.Kept, await scenario.RemainingAsync(entries.All));
    }

    private async Task<ServiceProvider> StartNodeAsync()
    {
        var node = Node();
        await WorkbenchOpenIddictTestHost.StartAsync(node);
        return node;
    }

    /// <summary>A node of the deployment: the store on the engine's database, as Workbench's settings name it, and the prune beside it.</summary>
    private ServiceProvider Node()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{Section}:IsDevelopmentOrDemo"] = "false",
                [$"{Section}:Provider"] = engine.Provider,
                [$"{Section}:ConnectionString"] = _connectionString
            })
            .Build();
        var node = WorkbenchOpenIddictTestHost.CreateProvider(configuration, withMigrationPolicy: true, services =>
        {
            services.AddSingleton<TimeProvider>(_time);
            services.AddWorkbenchOpenIddictPruning(configuration);
        });
        _nodes.Add(node);
        return node;
    }
}

[Collection(PostgreSqlOpenIddictDatabase.Collection)]
public sealed class PostgreSqlWorkbenchOpenIddictEngineTests(PostgreSqlOpenIddictDatabase engine) : WorkbenchOpenIddictEngineTests(engine);

[Collection(SqlServerOpenIddictDatabase.Collection)]
public sealed class SqlServerWorkbenchOpenIddictEngineTests(SqlServerOpenIddictDatabase engine) : WorkbenchOpenIddictEngineTests(engine);
