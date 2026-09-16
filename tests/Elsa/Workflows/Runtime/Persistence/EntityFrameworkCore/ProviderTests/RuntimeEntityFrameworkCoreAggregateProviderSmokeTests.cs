using System.Data.Common;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeEntityFrameworkCoreAggregatePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_full_runtime_aggregate_model_checkpoint_and_rollback() =>
        RuntimeEntityFrameworkCoreAggregateProviderSmoke.RunAsync(
            fixture,
            "PostgreSql",
            connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task PostgreSql_fresh_runtime_aggregate_migrates_and_commits_on_an_empty_database() =>
        RuntimeEntityFrameworkCoreAggregateProviderSmoke.RunFreshAsync(
            fixture,
            "PostgreSql",
            connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeEntityFrameworkCoreAggregateSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_full_runtime_aggregate_model_checkpoint_and_rollback() =>
        RuntimeEntityFrameworkCoreAggregateProviderSmoke.RunAsync(
            fixture,
            "SqlServer",
            connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task SqlServer_fresh_runtime_aggregate_migrates_and_commits_on_an_empty_database() =>
        RuntimeEntityFrameworkCoreAggregateProviderSmoke.RunFreshAsync(
            fixture,
            "SqlServer",
            connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeEntityFrameworkCoreAggregateMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_full_runtime_aggregate_model_checkpoint_and_rollback() =>
        RuntimeEntityFrameworkCoreAggregateProviderSmoke.RunAsync(
            fixture,
            "MySql",
            connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task MySql_fresh_runtime_aggregate_migrates_and_commits_on_an_empty_database() =>
        RuntimeEntityFrameworkCoreAggregateProviderSmoke.RunFreshAsync(
            fixture,
            "MySql",
            connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeEntityFrameworkCoreAggregateProviderSmoke
{
    private const string RecoverySigningKey = "ef-runtime-aggregate-native-recovery-key-32-bytes";
    private const string HierarchySigningKey = "ef-runtime-aggregate-native-hierarchy-key-32-bytes";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var scope = $"native-r19-aggregate-{Guid.NewGuid():N}";
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = providerName,
            ConnectionString = fixture.ConnectionString,
            HierarchyCursorSigningKey = HierarchySigningKey,
            RecoveryContinuationSigningKey = RecoverySigningKey
        });

        Assert.Equal(RuntimeCheckpointCommitStoreBackend.EntityFramework, RuntimeCheckpointCommitStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeWorkflowDispatchStoreBackend.EntityFramework, RuntimeWorkflowDispatchStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimePostCommitOutboxStoreBackend.EntityFramework, RuntimePostCommitOutboxStoreBackend.Find(services)!.Name);
        Assert.Equal(SchedulerWorkQueueStoreBackend.EntityFramework, SchedulerWorkQueueStoreBackend.Find(services)!.Name);
        Assert.Equal(DurableTimerStoreBackend.EntityFramework, DurableTimerStoreBackend.Find(services)!.Name);
        services.AddSingleton<IWorkflowDispatchDurabilityEvidence>(
            new WorkflowDispatchDurabilityEvidence(WorkflowDispatchDurabilityComponents.Resumption, WorkflowDispatchDurabilityLevel.Durable));

        // This smoke owns persistence composition and model behavior. The runtime host's
        // unrelated pumps require application-level serializer/logging registrations, so
        // resolve only the persistence context instead of validating the entire host graph.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
        await using var serviceScope = provider.CreateAsyncScope();
        await using var context = serviceScope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
        Assert.Equal(expectedProviderName, context.Database.ProviderName);
        var readiness = await serviceScope.ServiceProvider.GetRequiredService<IWorkflowDispatchReadinessAssessor>().AssessAsync();
        Assert.Equal(WorkflowDispatchReadinessGuarantee.DurableReady, readiness.Guarantee);
        Assert.True(readiness.Ready);
        await context.Database.EnsureCreatedAsync();

        var checkpointStore = new EfRuntimeCheckpointCommitStore(
            context,
            new FixedAccessor(scope),
            rootWriteLeaseManager: new PassThroughRootWriteLeaseManager());
        var commit = EmptyCheckpointCommit($"aggregate-{Guid.NewGuid():N}");
        var first = await checkpointStore.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        var replay = await checkpointStore.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Empty(first.PendingPostCommitWorkIds);
        Assert.Empty(replay.PendingPostCommitWorkIds);
        Assert.Single(await context.RuntimeCheckpointCommits.AsNoTracking().Where(row =>
            row.CommitId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(commit.CommitId)).ToArrayAsync());

        // A foreign dispatch store the EF aggregate does not own is refused before anything is mutated.
        var invalidServices = new ServiceCollection().AddWorkflowRuntime();
        invalidServices.AddScoped<IWorkflowDispatchStore>(_ => throw new InvalidOperationException("foreign dispatch store"));
        var beforeServices = invalidServices.ToArray();

        Assert.Throws<InvalidOperationException>(() => invalidServices.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = providerName,
            ConnectionString = fixture.ConnectionString,
            HierarchyCursorSigningKey = HierarchySigningKey,
            RecoveryContinuationSigningKey = RecoverySigningKey
        }));
        Assert.Equal(beforeServices, invalidServices);
        Assert.Null(RuntimeCheckpointCommitStoreBackend.Find(invalidServices));
        Assert.DoesNotContain(invalidServices, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
    }

    /// <summary>
    /// The aggregate on a fresh collection: its registered module migrator installs the Runtime
    /// schema into an empty database of its own, then checkpoints commit through the resolved EF writer, including
    /// one execution committing from two scopes in turn.
    /// </summary>
    public static async Task RunFreshAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var database = $"elsa_runtime_fresh_{Guid.NewGuid():N}";
        await using (var admin = createContext(fixture.ConnectionString))
            await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database}");
        var connection = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        connection.Remove("Initial Catalog");
        connection["Database"] = database;

        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = providerName,
            ConnectionString = connection.ConnectionString,
            HierarchyCursorSigningKey = HierarchySigningKey,
            RecoveryContinuationSigningKey = RecoverySigningKey
        });
        services.AddEfModuleMigrations<BookmarkStateDbContext>(providerName);
        services.AddSingleton<IWorkflowDispatchDurabilityEvidence>(
            new WorkflowDispatchDurabilityEvidence(WorkflowDispatchDurabilityComponents.Resumption, WorkflowDispatchDurabilityLevel.Durable));
        Assert.Equal(RuntimeCheckpointCommitStoreBackend.EntityFramework, RuntimeCheckpointCommitStoreBackend.Find(services)!.Name);
        Assert.Equal(WorkflowActivationAuthorityBackend.EntityFramework, WorkflowActivationAuthorityBackend.Find(services)!.Name);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            var readiness = await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchReadinessAssessor>().AssessAsync();
            Assert.Equal(WorkflowDispatchReadinessGuarantee.DurableReady, readiness.Guarantee);
        }

        await using (var outer = provider.CreateAsyncScope())
        await using (var nested = provider.CreateAsyncScope())
        {
            var outerStore = outer.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>();
            var nestedStore = nested.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>();
            Assert.IsType<EfRuntimeCheckpointCommitStore>(outerStore);
            await outerStore.CommitAsync(InspectionCommit("fresh-scheduled", ActivityExecutionStatus.Scheduled), new(RuntimeCheckpointPersistenceMode.Immediate));
            await outerStore.CommitAsync(InspectionCommit("fresh-running", ActivityExecutionStatus.Running), new(RuntimeCheckpointPersistenceMode.Immediate));
            await nestedStore.CommitAsync(InspectionCommit("fresh-suspended", ActivityExecutionStatus.Suspended), new(RuntimeCheckpointPersistenceMode.Immediate));
            await outerStore.CommitAsync(InspectionCommit("fresh-completed", ActivityExecutionStatus.Completed), new(RuntimeCheckpointPersistenceMode.Immediate));
        }

        await using (var verification = provider.CreateAsyncScope())
        {
            var context = verification.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
            var inspection = await context.ActivityExecutionInspections.AsNoTracking().SingleAsync();
            Assert.Equal(nameof(ActivityExecutionStatus.Completed), inspection.Status);
            Assert.Equal(4, await context.RuntimeCheckpointCommits.AsNoTracking().CountAsync());
        }

        // Running the migrator again on an installed schema is a no-op, as a shell reload or a second node is.
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
    }

    private static RuntimeCheckpointCommit InspectionCommit(string commitId, ActivityExecutionStatus status)
    {
        var occurredAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var inspection = new ActivityExecutionInspectionProjection(
            "activity-fresh", "workflow-fresh", "node-fresh", "authored-fresh", "Test.Activity", "1",
            status, null, 1, occurredAt, occurredAt, occurredAt, "checkpoint-1", "checkpoint-1", occurredAt,
            ActivitySchedulingProvenance.From("workflow-fresh", null, null, null, null, null, "scope-fresh", "checkpoint"),
            ["Done"], [], [], [], new Dictionary<string, string>(), "scope-fresh");
        return EmptyCheckpointCommit(commitId) with
        {
            Checkpoint = new RuntimeCheckpoint($"checkpoint-{commitId}", "NativeFreshCheckpoint", "workflow-fresh", occurredAt, [], new Dictionary<string, string>()),
            StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], null,
                [new RuntimeStateChange<ActivityExecutionInspectionProjection>(inspection.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert, inspection, new Dictionary<string, string>())],
                null, null, null)
        };
    }

    private static RuntimeCheckpointCommit EmptyCheckpointCommit(string commitId) => new(
        commitId,
        new RuntimeCheckpoint(
            $"checkpoint-{commitId}",
            "NativeAggregateCheckpoint",
            "workflow-a",
            DateTimeOffset.UtcNow,
            [],
            new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        [],
        new Dictionary<string, string>());

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => write(cancellationToken);
    }
}
