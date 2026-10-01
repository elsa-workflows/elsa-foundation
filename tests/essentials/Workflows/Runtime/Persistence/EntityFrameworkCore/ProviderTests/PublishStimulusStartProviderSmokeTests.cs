using System.Data.Common;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// <see cref="PublishStimulusStartContract"/> (#2195) on PostgreSQL. Each case runs whole runtime nodes, whose module
/// migrator installs the schema, so each gets an empty database of its own.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class PublishStimulusStartPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task PostgreSql_publish_stimulus_delivered_again_on_another_node_starts_its_workflow_once() =>
        await PublishStimulusStartContract.ARedeliveryOnAnotherNodeStartsTheWorkflowOnceAsync("PostgreSql", await CreateDatabaseAsync());

    [SkippableFact]
    public async Task PostgreSql_publish_stimulus_delivery_racing_on_another_node_converges_on_the_first_start() =>
        await PublishStimulusStartContract.ARacingDeliveryOnAnotherNodeConvergesOnTheFirstStartAsync("PostgreSql", await CreateDatabaseAsync());

    private async Task<string> CreateDatabaseAsync()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var database = $"elsa_runtime_keyed_start_{Guid.NewGuid():N}";
        await using (var admin = new RuntimePostgreSqlDbContext(
                         new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).Options))
            await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database}");
        var connection = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        connection["Database"] = database;
        return connection.ConnectionString;
    }
}
