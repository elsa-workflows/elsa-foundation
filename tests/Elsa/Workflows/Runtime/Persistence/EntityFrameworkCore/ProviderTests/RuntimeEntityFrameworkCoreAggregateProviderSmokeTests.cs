using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
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
        services.AddGroundworkV2RuntimeStores();

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

        var invalidServices = new ServiceCollection().AddWorkflowRuntime();
        invalidServices.AddGroundworkV2RuntimeStores();
        invalidServices.AddScoped<IWorkflowDispatchStore>(_ => throw new InvalidOperationException("foreign dispatch store"));
        var beforeServices = invalidServices.ToArray();
        var invalidRegistry = Assert.IsType<GroundworkStorageUnitRegistry>(invalidServices.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        var beforeUnits = invalidRegistry.Registrations;

        Assert.Throws<InvalidOperationException>(() => invalidServices.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = providerName,
            ConnectionString = fixture.ConnectionString,
            HierarchyCursorSigningKey = HierarchySigningKey,
            RecoveryContinuationSigningKey = RecoverySigningKey
        }));
        Assert.Equal(beforeServices, invalidServices);
        Assert.Equal(beforeUnits, invalidRegistry.Registrations);
        Assert.Equal(RuntimeCheckpointCommitStoreBackend.Groundwork, RuntimeCheckpointCommitStoreBackend.Find(invalidServices)!.Name);
        Assert.DoesNotContain(invalidServices, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
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
