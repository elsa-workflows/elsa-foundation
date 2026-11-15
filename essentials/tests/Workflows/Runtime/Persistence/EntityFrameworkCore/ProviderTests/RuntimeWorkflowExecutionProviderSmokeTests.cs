using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeWorkflowExecutionPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_workflow_execution_model_crud_query_transaction_and_concurrency() =>
        RuntimeWorkflowExecutionProviderSmoke.RunAsync(fixture, "PostgreSql", connection => new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options),
            RuntimePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeWorkflowExecutionSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_workflow_execution_model_crud_query_transaction_and_concurrency() =>
        RuntimeWorkflowExecutionProviderSmoke.RunAsync(fixture, "SqlServer", connection => new RuntimeSqlServerDbContext(
            new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
            RuntimeSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeWorkflowExecutionMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_workflow_execution_model_crud_query_transaction_and_concurrency() =>
        RuntimeWorkflowExecutionProviderSmoke.RunAsync(fixture, "MySql", connection => new RuntimeMySqlDbContext(
            new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
            RuntimeMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeWorkflowExecutionProviderSmoke
{
    private const string SigningKey = "ef-runtime-workflow-execution-provider-smoke-key";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, RuntimeDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var scope = $"provider-workflow-executions-{Guid.NewGuid():N}";
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        {
            SigningKey = SigningKey,
            AllowEphemeralDevelopmentKey = false
        }));
        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = new EfWorkflowExecutionStateStore(context, new FixedAccessor(scope), codec);
            var state = State("workflow-execution-a", scope, DateTimeOffset.UtcNow);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await store.SaveAsync(state);
            await transaction.CommitAsync();
            Assert.Equal(state.WorkflowExecutionId, (await store.FindAsync(state.WorkflowExecutionId))!.WorkflowExecutionId);
            Assert.Single((await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(10))).Items);
            Assert.Contains("artifact-a", await store.ListPinnedExecutableArtifactIdsAsync());

            var updated = state with { Status = WorkflowExecutionStatus.Running, SubStatus = "provider-update", UpdatedAt = state.UpdatedAt!.Value.AddMinutes(1) };
            await store.SaveAsync(updated);
            var updatedRoundTrip = await store.FindAsync(state.WorkflowExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Running, updatedRoundTrip!.Status);
            Assert.Equal("provider-update", updatedRoundTrip.SubStatus);
            Assert.Equal(updated.UpdatedAt, updatedRoundTrip.UpdatedAt);
            Assert.True(await store.DeleteAsync(state.WorkflowExecutionId));
            Assert.False(await store.DeleteAsync(state.WorkflowExecutionId));
        }

        var concurrent = State("workflow-execution-concurrent", scope, DateTimeOffset.UtcNow);
        await using var leftContext = createContext(fixture.ConnectionString);
        await using var rightContext = createContext(fixture.ConnectionString);
        var outcomes = await Task.WhenAll(
            Capture(new EfWorkflowExecutionStateStore(leftContext, new FixedAccessor(scope), codec).SaveAsync(concurrent)),
            Capture(new EfWorkflowExecutionStateStore(rightContext, new FixedAccessor(scope), codec).SaveAsync(concurrent)));
        Assert.Contains(outcomes, outcome => outcome is null);
        Assert.All(outcomes, outcome => Assert.True(outcome is null or InvalidOperationException));
        await using var verificationContext = createContext(fixture.ConnectionString);
        var verification = new EfWorkflowExecutionStateStore(verificationContext, new FixedAccessor(scope), codec);
        Assert.NotNull(await verification.FindAsync(concurrent.WorkflowExecutionId));
    }

    private static WorkflowExecutionState State(string id, string tenant, DateTimeOffset timestamp) => new(
        id,
        new WorkflowExecutableIdentity("artifact-a", "definition-a", "version-a", "1", "hash-a"),
        WorkflowExecutionStatus.Completed,
        null,
        timestamp.AddMinutes(-1), timestamp.AddMinutes(-1), timestamp, timestamp,
        null, null, tenant, new Dictionary<string, string>());

    private static async Task<Exception?> Capture(ValueTask operation)
    {
        try { await operation; return null; }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { return exception; }
    }

    private static async Task<Exception?> Capture(ValueTask<WorkflowExecutionState> operation)
    {
        try { await operation; return null; }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { return exception; }
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
