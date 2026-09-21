using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class EfWorkflowRunHealthDataSourcePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_run_health_model_and_query_smoke() =>
        WorkflowRunHealthProviderSmoke.RunAsync(fixture, connection => new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options),
            RuntimePostgreSqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task PostgreSql_portfolio_design_and_runtime_query_smoke() =>
        WorkflowPortfolioProviderSmoke.RunAsync(fixture,
            connection => new WorkflowsDesignPostgreSqlDbContext(
                new DbContextOptionsBuilder<WorkflowsDesignPostgreSqlDbContext>().UseNpgsql(connection).Options),
            connection => new RuntimePostgreSqlDbContext(
                new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options),
            RuntimePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class EfWorkflowRunHealthDataSourceSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_run_health_model_and_query_smoke() =>
        WorkflowRunHealthProviderSmoke.RunAsync(fixture, connection => new RuntimeSqlServerDbContext(
            new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
            RuntimeSqlServerDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task SqlServer_portfolio_design_and_runtime_query_smoke() =>
        WorkflowPortfolioProviderSmoke.RunAsync(fixture,
            connection => new WorkflowsDesignSqlServerDbContext(
                new DbContextOptionsBuilder<WorkflowsDesignSqlServerDbContext>().UseSqlServer(connection).Options),
            connection => new RuntimeSqlServerDbContext(
                new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
            RuntimeSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class EfWorkflowRunHealthDataSourceMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_run_health_model_and_query_smoke() =>
        WorkflowRunHealthProviderSmoke.RunAsync(fixture, connection => new RuntimeMySqlDbContext(
            new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
            RuntimeMySqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task MySql_portfolio_design_and_runtime_query_smoke() =>
        WorkflowPortfolioProviderSmoke.RunAsync(fixture,
            connection => new WorkflowsDesignMySqlDbContext(
                new DbContextOptionsBuilder<WorkflowsDesignMySqlDbContext>().UseMySQL(connection).Options),
            connection => new RuntimeMySqlDbContext(
                new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
            RuntimeMySqlDbContext.ExpectedProviderName);
}

internal static class WorkflowRunHealthProviderSmoke
{
    private static readonly DateTimeOffset From = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, RuntimeDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r25-run-health-{Guid.NewGuid():N}";
        var row = Row(scope, "workflow-native", "definition-native", From.AddMinutes(5));

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            context.WorkflowRunHealthStates.Add(row);
            await context.SaveChangesAsync();
        }

        await using var verification = createContext(fixture.ConnectionString);
        var source = new EfWorkflowRunHealthDataSource(verification, new FixedAccessor(scope));
        var query = new WorkflowRunHealthQuery(
            From,
            From.AddHours(1),
            "Etc/UTC",
            WorkflowRunHealthBucketSize.Hour,
            scope);
        var result = await source.QueryAsync(new WorkflowRunHealthDataQuery(
            query,
            [new WorkflowRunHealthBucketRange(0, query.From, query.To)]));

        Assert.Equal(1, result.StartedCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Empty(result.HighestFailureDefinitions);
    }

    private static WorkflowRunHealthStateEntity Row(string scope, string workflowId, string definitionId, DateTimeOffset startedAt)
    {
        var row = new WorkflowRunHealthStateEntity
        {
            Id = CompositeId(scope, workflowId),
            ScopeKey = EfRelationalIdentity.Encode(scope),
            ScopeKeyHash = EfRelationalIdentity.Hash(scope),
            WorkflowExecutionId = EfRelationalIdentity.Encode(workflowId),
            WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowId),
            WorkflowExecutionIdOrderKey = Order(workflowId),
            DefinitionId = EfRelationalIdentity.Encode(definitionId),
            DefinitionIdHash = EfRelationalIdentity.Hash(definitionId),
            DefinitionIdOrderKey = Order(definitionId),
            RunKind = (int)WorkflowRunKind.PublishedRun,
            StartedAtUtcTicks = startedAt.UtcTicks,
            StartedAtOffsetMinutes = (int)startedAt.Offset.TotalMinutes,
            Status = (int)WorkflowExecutionStatus.Completed,
            IncidentCount = 0,
            IncidentBearingCount = 0,
            SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
            Revision = 1
        };
        row.ContentJson = JsonSerializer.Serialize(new RawProjection(
            row.WorkflowExecutionId,
            row.DefinitionId,
            WorkflowRunKind.PublishedRun,
            startedAt,
            WorkflowExecutionStatus.Completed,
            0,
            0), JsonOptions);
        return row;
    }

    private static string CompositeId(string scope, string workflowId) =>
        EfRelationalIdentity.Hash($"{scope.Length}:{scope}{workflowId.Length}:{workflowId}");

    private static string Order(string value) => Convert.ToHexString(
        EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.IdentityMaximumLength));

    private sealed record RawProjection(
        string WorkflowExecutionId,
        string DefinitionId,
        WorkflowRunKind RunKind,
        DateTimeOffset? StartedAt,
        WorkflowExecutionStatus Status,
        long IncidentCount,
        long IncidentBearingCount);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
