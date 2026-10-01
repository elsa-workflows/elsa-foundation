using Elsa.Primitives.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Options;
using Elsa.Workflows.Design.Reconciliation.Services;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
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
        CreateContext,
        WorkflowsDesignPostgreSqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task PostgreSql_permanently_deleted_definition_is_imported_again() =>
        WorkflowsDesignNativeProviderSmoke.RunPermanentDeleteReimportAsync(fixture, CreateContext);

    [SkippableTheory]
    [InlineData("Renamed")] // Two passes apply the same change.
    [InlineData("Renamed elsewhere")] // The other pass applies a different one, so the paused pass has to write again.
    public Task PostgreSql_metadata_write_that_loses_a_race_converges(string otherWritersName) =>
        WorkflowsDesignNativeProviderSmoke.RunLostMetadataRaceAsync(fixture, CreateContext, otherWritersName);

    private static WorkflowsDesignDbContext CreateContext(string connection) =>
        new WorkflowsDesignPostgreSqlDbContext(new DbContextOptionsBuilder<WorkflowsDesignPostgreSqlDbContext>().UseNpgsql(connection).Options);
}

[Collection(WorkflowsDesignSqlServerFixture.CollectionName)]
public sealed class WorkflowsDesignSqlServerSmokeTests(WorkflowsDesignSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_live_workflows_design_w01_w05_smoke() => WorkflowsDesignNativeProviderSmoke.RunAsync(
        fixture,
        CreateContext,
        WorkflowsDesignSqlServerDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task SqlServer_permanently_deleted_definition_is_imported_again() =>
        WorkflowsDesignNativeProviderSmoke.RunPermanentDeleteReimportAsync(fixture, CreateContext);

    private static WorkflowsDesignDbContext CreateContext(string connection) =>
        new WorkflowsDesignSqlServerDbContext(new DbContextOptionsBuilder<WorkflowsDesignSqlServerDbContext>().UseSqlServer(connection).Options);
}

[Collection(WorkflowsDesignMySqlFixture.CollectionName)]
public sealed class WorkflowsDesignMySqlSmokeTests(WorkflowsDesignMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_live_workflows_design_w01_w05_smoke() => WorkflowsDesignNativeProviderSmoke.RunAsync(
        fixture,
        CreateContext,
        WorkflowsDesignMySqlDbContext.ExpectedProviderName);

    [Fact(Skip = "Blocked by elsa-workflows/elsa-foundation#2204: the MySQL provider reads LastModifiedAt without its fractional seconds, so updating or permanently deleting a workflow definition fails its concurrency check.")]
    public Task MySql_permanently_deleted_definition_is_imported_again() =>
        WorkflowsDesignNativeProviderSmoke.RunPermanentDeleteReimportAsync(fixture, CreateContext);

    private static WorkflowsDesignDbContext CreateContext(string connection) =>
        new WorkflowsDesignMySqlDbContext(new DbContextOptionsBuilder<WorkflowsDesignMySqlDbContext>().UseMySQL(connection).Options);
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
        var globalDefinitionId = $"provider-global-definition-{Guid.NewGuid():N}";
        var globalVersionId = $"provider-global-version-{Guid.NewGuid():N}";
        var globalDraftId = $"provider-global-draft-{Guid.NewGuid():N}";
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
                },
                new WorkflowDefinition
                {
                    Id = globalDefinitionId, TenantId = null, Name = "Native global definition",
                    Description = "Native global description"
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
                },
                new WorkflowDefinitionVersion(globalDefinitionId, "1.0.0", "{}")
                {
                    Id = globalVersionId, TenantId = null, CreatedAt = Now, LastModifiedAt = Now
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
                },
                new WorkflowDefinitionDraft
                {
                    Id = globalDraftId, TenantId = null, WorkflowDefinitionId = globalDefinitionId, StateSource = "{}",
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
                },
                new WorkflowDefinitionDraftLayout
                {
                    Id = $"provider-global-draft-layout-{Guid.NewGuid():N}", TenantId = null, WorkflowDefinitionDraftId = globalDraftId,
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
                },
                new WorkflowDefinitionVersionLayout
                {
                    Id = $"provider-global-layout-{Guid.NewGuid():N}", TenantId = null, WorkflowDefinitionVersionId = globalVersionId,
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

            // Global rows use the same physical scope envelope but remain visible only through
            // explicit privileged cross-scope access. This also proves nullable TenantId rows
            // can be tracked and related without nullable relational keys.
            var acrossScopes = new FixedAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("provider-global-smoke")));
            Assert.Equal(globalDefinitionId, (await new EfWorkflowDefinitionStore(context, acrossScopes).FindByIdAsync(globalDefinitionId))!.Id);
            Assert.Equal(globalVersionId, (await new EfWorkflowDefinitionVersionStore(context, new NativeProviderSerializer(), new EfWorkflowDefinitionStore(context, acrossScopes), acrossScopes).FindByIdAsync(globalVersionId))!.Id);
            Assert.Equal(globalDraftId, (await new EfWorkflowDefinitionDraftStore(context, new NativeProviderSerializer(), acrossScopes).FindWithLayoutByIdAsync(globalDraftId))!.Draft.Id);
            Assert.NotNull(await new EfWorkflowDefinitionVersionLayoutStore(context, acrossScopes).FindByVersionIdAsync(globalVersionId));
            var globalOnly = new FixedAccess(PersistenceAccessContext.Global);
            var globalDefinitions = new EfWorkflowDefinitionStore(context, globalOnly);
            Assert.Equal(globalDefinitionId, (await globalDefinitions.FindByIdAsync(globalDefinitionId))!.Id);
            Assert.Equal(globalVersionId, (await new EfWorkflowDefinitionVersionStore(context, new NativeProviderSerializer(), globalDefinitions, globalOnly).FindByIdAsync(globalVersionId))!.Id);
            Assert.Equal(globalDraftId, (await new EfWorkflowDefinitionDraftStore(context, new NativeProviderSerializer(), globalOnly).FindWithLayoutByIdAsync(globalDraftId))!.Draft.Id);
            Assert.NotNull(await new EfWorkflowDefinitionVersionLayoutStore(context, globalOnly).FindByVersionIdAsync(globalVersionId));
            var privilegedGlobal = new FixedAccess(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("provider-global-reader")));
            var privilegedDefinitions = new EfWorkflowDefinitionStore(context, privilegedGlobal);
            Assert.Equal(globalDefinitionId, (await privilegedDefinitions.FindByIdAsync(globalDefinitionId))!.Id);
            Assert.Equal(globalVersionId, (await new EfWorkflowDefinitionVersionStore(context, new NativeProviderSerializer(), privilegedDefinitions, privilegedGlobal).FindByIdAsync(globalVersionId))!.Id);
            Assert.Equal(globalDraftId, (await new EfWorkflowDefinitionDraftStore(context, new NativeProviderSerializer(), privilegedGlobal).FindWithLayoutByIdAsync(globalDraftId))!.Draft.Id);
            Assert.NotNull(await new EfWorkflowDefinitionVersionLayoutStore(context, privilegedGlobal).FindByVersionIdAsync(globalVersionId));

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
            transactionContext.Definitions.Add(new WorkflowDefinition { Id = rollbackId + "-global", TenantId = null, Name = "Global rolled back" });
            await transactionContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var reopened = createContext(fixture.ConnectionString))
        {
            Assert.Null(await reopened.Definitions.SingleOrDefaultAsync(x => x.TenantId == tenant && x.Id == rollbackId));
            Assert.Null(await reopened.Definitions.SingleOrDefaultAsync(x => x.TenantId == null && x.Id == rollbackId + "-global"));
        }

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

    /// <summary>
    /// A permanent delete retires the reconciler's materialization markers with the definition, so a source that still
    /// lists the definition imports it again. The re-import matches the first one exactly, so a surviving marker would
    /// replay without writing a row (#2187). Each step gets its own context, as each pass and request gets its own scope.
    /// </summary>
    public static async Task RunPermanentDeleteReimportAsync(
        WorkflowsDesignProviderFixture fixture,
        Func<string, WorkflowsDesignDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/native provider is unavailable.");
        var tenant = $"provider-tenant-{Guid.NewGuid():N}";
        var definitionId = $"provider-reimported-{Guid.NewGuid():N}";
        var versionId = $"provider-reimported-version-{Guid.NewGuid():N}";
        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        await InNewContextAsync((context, _) => context.Database.EnsureCreatedAsync());

        await ImportAsync();
        await InNewContextAsync((context, writer) => new EfSaveWorkflowDefinitionCommand(context, access, writer).Execute(
            WorkflowReconciliationOperationKeys.DefinitionMetadataWrite(definitionId),
            new WorkflowDefinition { Id = definitionId, Name = "Reimported", DeletedAt = Now, IsSourceOwned = true }));
        await InNewContextAsync((context, writer) => new EfDeleteWorkflowDefinitionPermanentlyCommand(context, access, writer, [new NeverPublishedGuard()])
            .Execute(new DesignOperationKey($"provider-permanent-delete-{Guid.NewGuid():N}"), definitionId));
        // The tenant is this test's own, so the delete's marker is the only one its reconciliation markers leave.
        await InNewContextAsync(async (context, _) => Assert.Equal(
            EfDeleteWorkflowDefinitionPermanentlyCommand.OperationKind,
            Assert.Single(await context.Operations.Where(marker => marker.TenantId == tenant).Select(marker => marker.OperationKind).ToListAsync())));
        await ImportAsync();

        await InNewContextAsync(async (context, _) =>
        {
            Assert.Equal(definitionId, (await new EfWorkflowDefinitionStore(context, access).FindByIdAsync(definitionId))?.Id);
            Assert.True(await context.Versions.AnyAsync(version => version.TenantId == tenant && version.Id == versionId));
        });

        Task ImportAsync() => InNewContextAsync(async (context, writer) =>
        {
            await new EfMaterializeWorkflowDefinitionCommand(context, access, writer).Execute(
                WorkflowReconciliationOperationKeys.Definition(definitionId),
                new WorkflowDefinition { Id = definitionId, Name = "Reimported", IsSourceOwned = true });
            await new EfMaterializeWorkflowDefinitionVersionCommand(context, access, writer, new NativeProviderSerializer()).Execute(
                WorkflowReconciliationOperationKeys.Version(definitionId, SemVer.ToSortKey("1.0.0")),
                new WorkflowDefinitionVersion(definitionId, "1.0.0", "{}") { Id = versionId });
        });

        async Task InNewContextAsync(Func<WorkflowsDesignDbContext, EfDesignAtomicWriter, Task> step)
        {
            await using var context = createContext(fixture.ConnectionString);
            await step(context, new EfDesignAtomicWriter(context, access));
        }
    }

    /// <summary>
    /// Two reconciliation passes race on one definition's metadata. The paused pass reads the definition, another pass
    /// commits, and the paused pass's write then fails the row's LastModifiedAt check on the provider. It must read again
    /// and converge on the name it wants (#2187).
    /// </summary>
    public static async Task RunLostMetadataRaceAsync(
        WorkflowsDesignProviderFixture fixture,
        Func<string, WorkflowsDesignDbContext> createContext,
        string otherWritersName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/native provider is unavailable.");
        var tenant = $"provider-tenant-{Guid.NewGuid():N}";
        var definitionId = $"provider-raced-{Guid.NewGuid():N}";
        var versionId = $"provider-raced-version-{Guid.NewGuid():N}";
        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        await using (var context = createContext(fixture.ConnectionString))
            await context.Database.EnsureCreatedAsync();

        await ReconcileAsync("Original");
        var read = new Pause();
        var paused = ReconcileAsync("Renamed", store => new PausingDefinitionStore(store, read));
        await read.ReachedBy(paused);
        await ReconcileAsync(otherWritersName);
        read.Resume();
        await paused;

        await using (var context = createContext(fixture.ConnectionString))
            Assert.Equal("Renamed", (await new EfWorkflowDefinitionStore(context, access).FindByIdAsync(definitionId))?.Name);

        // One pass in its own context, as each reconciliation pass gets its own scope.
        async Task ReconcileAsync(string name, Func<IWorkflowDefinitionStore, IWorkflowDefinitionStore>? definitions = null)
        {
            await using var context = createContext(fixture.ConnectionString);
            var writer = new EfDesignAtomicWriter(context, access);
            var serializer = new NativeProviderSerializer();
            var definitionStore = new EfWorkflowDefinitionStore(context, access);
            var version = new WorkflowDefinitionVersion(definitionId, "1.0.0")
            {
                Id = versionId,
                State = new WorkflowDefinitionState([], null, [], [], null),
                Definition = new WorkflowDefinition { Id = definitionId, Name = name }
            };
            await new WorkflowsVersionReconciler(
                NullLogger<WorkflowsVersionReconciler>.Instance,
                new ContributingPublisher(version),
                Microsoft.Extensions.Options.Options.Create(new WorkflowVersionReconcilerOptions()),
                definitions?.Invoke(definitionStore) ?? definitionStore,
                new EfWorkflowDefinitionVersionStore(context, serializer, definitionStore, access),
                new EfMaterializeWorkflowDefinitionCommand(context, access, writer),
                new EfMaterializeWorkflowDefinitionVersionCommand(context, access, writer, serializer),
                new EfSaveWorkflowDefinitionCommand(context, access, writer),
                serializer).Reconcile(CancellationToken.None);
        }
    }

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class NeverPublishedGuard : IWorkflowDefinitionPublicationDeletionGuard
    {
        public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
