using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

[Collection(WorkflowsDesignPostgreSqlFixture.CollectionName)]
public sealed class WorkflowsDesignPostgreSqlSmokeTests(WorkflowsDesignPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_live_workflows_design_w01_w05_smoke() => WorkflowsDesignNativeProviderSmoke.RunAsync(
        fixture,
        connection => new WorkflowsDesignPostgreSqlDbContext(new DbContextOptionsBuilder<WorkflowsDesignPostgreSqlDbContext>().UseNpgsql(connection).Options),
        WorkflowsDesignPostgreSqlDbContext.ExpectedProviderName);
}

[Collection(WorkflowsDesignSqlServerFixture.CollectionName)]
public sealed class WorkflowsDesignSqlServerSmokeTests(WorkflowsDesignSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_live_workflows_design_w01_w05_smoke() => WorkflowsDesignNativeProviderSmoke.RunAsync(
        fixture,
        connection => new WorkflowsDesignSqlServerDbContext(new DbContextOptionsBuilder<WorkflowsDesignSqlServerDbContext>().UseSqlServer(connection).Options),
        WorkflowsDesignSqlServerDbContext.ExpectedProviderName);
}

[Collection(WorkflowsDesignMySqlFixture.CollectionName)]
public sealed class WorkflowsDesignMySqlSmokeTests(WorkflowsDesignMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_live_workflows_design_w01_w05_smoke() => WorkflowsDesignNativeProviderSmoke.RunAsync(
        fixture,
        connection => new WorkflowsDesignMySqlDbContext(new DbContextOptionsBuilder<WorkflowsDesignMySqlDbContext>().UseMySQL(connection).Options),
        WorkflowsDesignMySqlDbContext.ExpectedProviderName);
}

internal static class WorkflowsDesignNativeProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(
        WorkflowsDesignProviderFixture fixture,
        Func<string, WorkflowsDesignDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/native provider is unavailable.");
        var tenant = $"provider-tenant-{Guid.NewGuid():N}";
        var definitionId = $"provider-definition-{Guid.NewGuid():N}";
        var trailingDefinitionId = definitionId + " ";
        var versionId = $"provider-version-{Guid.NewGuid():N}";
        var trailingVersionId = versionId + " ";
        var draftId = $"provider-draft-{Guid.NewGuid():N}";
        var trailingDraftId = draftId + " ";
        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            Assert.Equal(WorkflowsDesignEfModule.DefinitionTable, context.Model.FindEntityType(typeof(WorkflowDefinition))!.GetTableName());
            Assert.Equal(WorkflowsDesignEfModule.VersionTable, context.Model.FindEntityType(typeof(WorkflowDefinitionVersion))!.GetTableName());
            Assert.Equal(WorkflowsDesignEfModule.DraftTable, context.Model.FindEntityType(typeof(WorkflowDefinitionDraft))!.GetTableName());

            // W01: definition CRUD/query through the provider store.
            context.Definitions.AddRange(
                new WorkflowDefinition
                {
                    Id = definitionId, TenantId = tenant, Name = "Native provider definition",
                    Description = "Native provider description"
                },
                new WorkflowDefinition
                {
                    Id = trailingDefinitionId, TenantId = tenant, Name = "Native provider definition ",
                    Description = "Native provider description "
                });
            await context.SaveChangesAsync();
            var definitions = new EfWorkflowDefinitionStore(context, access);
            Assert.Equal(definitionId, (await definitions.FindByIdAsync(definitionId))!.Id);
            Assert.Equal(trailingDefinitionId, (await definitions.FindByIdAsync(trailingDefinitionId))!.Id);
            Assert.Equal(definitionId, Assert.Single(await definitions.ListAsync(new() { Name = "Native provider definition" })).Id);
            Assert.Equal(trailingDefinitionId, Assert.Single(await definitions.ListAsync(new() { Names = ["Native provider definition "] })).Id);
            Assert.Equal(definitionId, Assert.Single(await definitions.ListAsync(new() { Description = "Native provider description" })).Id);

            // W02: immutable version and ordered latest-version query.
            context.Versions.AddRange(
                new WorkflowDefinitionVersion(definitionId, "1.0.0", "{}")
                {
                    Id = versionId, TenantId = tenant, CreatedAt = Now, LastModifiedAt = Now
                },
                new WorkflowDefinitionVersion(trailingDefinitionId, "1.0.0", "{}")
                {
                    Id = trailingVersionId, TenantId = tenant, CreatedAt = Now, LastModifiedAt = Now
                });
            await context.SaveChangesAsync();
            var versions = new EfWorkflowDefinitionVersionStore(context, new NativeProviderSerializer(), definitions, access);
            Assert.Equal(versionId, (await versions.FindByIdAsync(versionId))!.Id);
            Assert.Equal(trailingVersionId, (await versions.FindByIdAsync(trailingVersionId))!.Id);
            Assert.Equal(versionId, (await versions.FindLatestVersionAsync(definitionId))!.Id);
            Assert.Equal(trailingVersionId, (await versions.FindLatestVersionAsync(trailingDefinitionId))!.Id);

            // W03: mutable draft read and W04: version-layout read.
            context.Drafts.AddRange(
                new WorkflowDefinitionDraft
                {
                    Id = draftId, TenantId = tenant, WorkflowDefinitionId = definitionId, StateSource = "{}",
                    CreatedAt = Now, LastModifiedAt = Now
                },
                new WorkflowDefinitionDraft
                {
                    Id = trailingDraftId, TenantId = tenant, WorkflowDefinitionId = trailingDefinitionId, StateSource = "{}",
                    CreatedAt = Now, LastModifiedAt = Now
                });
            context.DraftLayouts.AddRange(
                new WorkflowDefinitionDraftLayout
                {
                    Id = $"provider-draft-layout-{Guid.NewGuid():N}", TenantId = tenant, WorkflowDefinitionDraftId = draftId,
                    CreatedAt = Now, LastModifiedAt = Now
                },
                new WorkflowDefinitionDraftLayout
                {
                    Id = $"provider-draft-layout-{Guid.NewGuid():N} ", TenantId = tenant, WorkflowDefinitionDraftId = trailingDraftId,
                    CreatedAt = Now, LastModifiedAt = Now
                });
            context.VersionLayouts.AddRange(
                new WorkflowDefinitionVersionLayout
                {
                    Id = $"provider-layout-{Guid.NewGuid():N}", TenantId = tenant, WorkflowDefinitionVersionId = versionId,
                    CreatedAt = Now, LastModifiedAt = Now
                },
                new WorkflowDefinitionVersionLayout
                {
                    Id = $"provider-layout-{Guid.NewGuid():N} ", TenantId = tenant, WorkflowDefinitionVersionId = trailingVersionId,
                    CreatedAt = Now, LastModifiedAt = Now
                });
            await context.SaveChangesAsync();
            var drafts = new EfWorkflowDefinitionDraftStore(context, new NativeProviderSerializer(), access);
            Assert.Equal(draftId, (await drafts.FindByIdAsync(draftId))!.Id);
            Assert.Equal(trailingDraftId, (await drafts.FindByIdAsync(trailingDraftId))!.Id);
            Assert.Equal(draftId, (await drafts.FindByWorkflowDefinitionIdAsync(definitionId))!.Id);
            Assert.Equal(trailingDraftId, (await drafts.FindByWorkflowDefinitionIdAsync(trailingDefinitionId))!.Id);
            Assert.Equal(draftId, (await drafts.FindWithLayoutByIdAsync(draftId))!.Draft.Id);
            Assert.Equal(trailingDraftId, (await drafts.FindWithLayoutByIdAsync(trailingDraftId))!.Draft.Id);
            Assert.NotNull(await new EfWorkflowDefinitionVersionLayoutStore(context, access).FindByVersionIdAsync(versionId));
            Assert.NotNull(await new EfWorkflowDefinitionVersionLayoutStore(context, access).FindByVersionIdAsync(trailingVersionId));

            // W05: the operation ledger commits atomically with its staged mutation and replays.
            var writer = new EfDesignAtomicWriter(context, access);
            var operationKey = new DesignOperationKey($"provider-operation-{Guid.NewGuid():N}");
            var operationId = await writer.ExecuteAsync(operationKey, "provider.design.smoke.v1", new { definitionId }, ["definitions"], async token =>
            {
                var committed = new WorkflowDefinition { Id = $"{definitionId}-operation", TenantId = tenant, Name = "Operation definition" };
                context.Definitions.Add(committed);
                await context.SaveChangesAsync(token);
                return committed.Id;
            });
            Assert.Equal($"{definitionId}-operation", operationId);
            Assert.Equal(operationId, await writer.ExecuteAsync(operationKey, "provider.design.smoke.v1", new { definitionId }, ["definitions"], _ => Task.FromResult(operationId)));

            // The operation identity must remain exact on SQL Server, whose string equality and
            // unique indexes ignore trailing spaces even under a binary collation.
            var exactKind = "provider.trailing-kind";
            var exactKey = new DesignOperationKey("provider-trailing-key");
            Assert.Equal("kind", await writer.ExecuteAsync(exactKey, exactKind, new { Value = "kind" }, ["operations"], _ => Task.FromResult("kind")));
            Assert.Equal("kind-trailing", await writer.ExecuteAsync(exactKey, exactKind + " ", new { Value = "kind-trailing" }, ["operations"], _ => Task.FromResult("kind-trailing")));
            Assert.Equal("key-trailing", await writer.ExecuteAsync(new DesignOperationKey(exactKey.Value + " "), exactKind, new { Value = "key-trailing" }, ["operations"], _ => Task.FromResult("key-trailing")));
            var operationMarkers = await context.Operations.AsNoTracking().ToListAsync();
            Assert.Contains(operationMarkers, marker => StringComparer.Ordinal.Equals(marker.OperationKind, exactKind) && StringComparer.Ordinal.Equals(marker.OperationKey, exactKey.Value));
            Assert.Contains(operationMarkers, marker => StringComparer.Ordinal.Equals(marker.OperationKind, exactKind + " ") && StringComparer.Ordinal.Equals(marker.OperationKey, exactKey.Value));
            Assert.Contains(operationMarkers, marker => StringComparer.Ordinal.Equals(marker.OperationKind, exactKind) && StringComparer.Ordinal.Equals(marker.OperationKey, exactKey.Value + " "));
        }

        var rollbackId = $"provider-rollback-{Guid.NewGuid():N}";
        await using (var transactionContext = createContext(fixture.ConnectionString))
        {
            await using var transaction = await transactionContext.Database.BeginTransactionAsync();
            transactionContext.Definitions.Add(new WorkflowDefinition { Id = rollbackId, TenantId = tenant, Name = "Rolled back" });
            await transactionContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var reopened = createContext(fixture.ConnectionString))
            Assert.Null(await reopened.Definitions.SingleOrDefaultAsync(x => x.TenantId == tenant && x.Id == rollbackId));

        var concurrentId = $"provider-concurrent-{Guid.NewGuid():N}";
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var leftReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rightReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var leftCommit = InsertConcurrentDefinitionAsync("left", leftReady);
        var rightCommit = InsertConcurrentDefinitionAsync("right", rightReady);
        await Task.WhenAll(leftReady.Task, rightReady.Task);
        startGate.SetResult(true);

        var commitResults = await Task.WhenAll(leftCommit, rightCommit);
        Assert.Single(commitResults, exception => exception is null);
        Assert.Single(commitResults, exception => exception is DbUpdateException);

        await using var persisted = createContext(fixture.ConnectionString);
        Assert.Equal(1, await persisted.Definitions.CountAsync(definition => definition.TenantId == tenant && definition.Id == concurrentId));

        async Task<Exception?> InsertConcurrentDefinitionAsync(string side, TaskCompletionSource<bool> ready)
        {
            await using var context = createContext(fixture.ConnectionString);
            context.Definitions.Add(new WorkflowDefinition { Id = concurrentId, TenantId = tenant, Name = $"Concurrent {side}" });
            ready.SetResult(true);
            await startGate.Task;
            try
            {
                await context.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateException exception)
            {
                return exception;
            }
        }
    }

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}

internal sealed class NativeProviderSerializer : IPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
    public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
    public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;
    public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;
    public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;
    public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;
    public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;
    public JsonSerializerOptions GetOptions() => Options;
}

public abstract class WorkflowsDesignProviderFixture : IAsyncLifetime
{
    protected string? ConnectionStringValue;
    public bool IsAvailable { get; private protected set; }
    public string? SkipReason { get; private protected set; }
    public string ConnectionString => ConnectionStringValue ?? throw new InvalidOperationException("Provider is unavailable.");
    protected abstract string EnvironmentVariable { get; }
    protected abstract Task StartContainerAsync();
    protected abstract Task DisposeContainerAsync();

    public async Task InitializeAsync()
    {
        ConnectionStringValue = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(ConnectionStringValue))
        {
            IsAvailable = true;
            return;
        }
        try
        {
            await StartContainerAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) || exception.InnerException?.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) == true)
        {
            SkipReason = $"Docker container unavailable for {EnvironmentVariable}: {exception.Message}";
        }
    }

    public Task DisposeAsync() => DisposeContainerAsync();
}

public sealed class WorkflowsDesignPostgreSqlFixture : WorkflowsDesignProviderFixture
{
    private PostgreSqlContainer? container;
    public const string CollectionName = "workflows-design-ef-postgresql";
    protected override string EnvironmentVariable => "ELSA_WORKFLOWS_DESIGN_EF_POSTGRESQL_TEST_CONNECTION_STRING";
    protected override async Task StartContainerAsync()
    {
        container = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("elsa_workflows_design").WithUsername("postgres").WithPassword("postgres").Build();
        await container.StartAsync();
        ConnectionStringValue = container.GetConnectionString();
    }
    protected override async Task DisposeContainerAsync() { if (container is not null) await container.DisposeAsync(); }
}

public sealed class WorkflowsDesignSqlServerFixture : WorkflowsDesignProviderFixture
{
    private MsSqlContainer? container;
    public const string CollectionName = "workflows-design-ef-sqlserver";
    protected override string EnvironmentVariable => "ELSA_WORKFLOWS_DESIGN_EF_SQLSERVER_TEST_CONNECTION_STRING";
    protected override async Task StartContainerAsync()
    {
        container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        ConnectionStringValue = container.GetConnectionString();
    }
    protected override async Task DisposeContainerAsync() { if (container is not null) await container.DisposeAsync(); }
}

public sealed class WorkflowsDesignMySqlFixture : WorkflowsDesignProviderFixture
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;
    public const string CollectionName = "workflows-design-ef-mysql";
    protected override string EnvironmentVariable => "ELSA_WORKFLOWS_DESIGN_EF_MYSQL_TEST_CONNECTION_STRING";
    protected override async Task StartContainerAsync()
    {
        container = new MySqlBuilder(Image).WithDatabase("elsa_workflows_design").WithUsername("root").WithPassword("root").Build();
        await container.StartAsync();
        ConnectionStringValue = container.GetConnectionString();
    }
    protected override async Task DisposeContainerAsync() { if (container is not null) await container.DisposeAsync(); }
}

[CollectionDefinition(WorkflowsDesignPostgreSqlFixture.CollectionName)]
public sealed class WorkflowsDesignPostgreSqlCollection : ICollectionFixture<WorkflowsDesignPostgreSqlFixture>;

[CollectionDefinition(WorkflowsDesignSqlServerFixture.CollectionName)]
public sealed class WorkflowsDesignSqlServerCollection : ICollectionFixture<WorkflowsDesignSqlServerFixture>;

[CollectionDefinition(WorkflowsDesignMySqlFixture.CollectionName)]
public sealed class WorkflowsDesignMySqlCollection : ICollectionFixture<WorkflowsDesignMySqlFixture>;
