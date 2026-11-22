using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Testing;
using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Extensions;
using System.Reflection;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.ActivityUpgradeFixtures;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class EfActivityDependencyProjectionRebuildCoordinatorTests : IAsyncLifetime
{
    private ActivityUpgradeDatabase database = null!;
    private readonly IPayloadSerializer payloads = Serializer();

    public async Task InitializeAsync()
    {
        database = await ActivityUpgradeDatabase.CreateAsync();
        await ActivityUpgradeSeed.BaseGraphAsync(database.Contexts, payloads);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    [Fact]
    public async Task A_rebuild_converges_an_ordinary_workflow_edit_at_a_new_watermark()
    {
        // An ordinary Workflows Design edit is not a Publishing mutation, so it deliberately leaves the
        // derived view behind. Converging it is exactly what the coordinator is for.
        await EditWorkflowDraftOutsidePublishingAsync(NewVersionId);
        Assert.All((await ReadProjectionAsync()).Items, item => Assert.Equal(OldVersionId, item.Dependency.VersionId));
        await using var scope = Scope();

        var rebuild = await scope.Coordinator.RebuildAsync();

        var projection = await ReadProjectionAsync();
        Assert.Equal(2, projection.Sequence);
        Assert.Equal(rebuild.RebuildId, projection.RebuildId);
        Assert.StartsWith("rebuild-", projection.RebuildId, StringComparison.Ordinal);
        Assert.Contains(projection.Items, item => item.Owner.Kind == "WorkflowDraft" && item.Dependency.VersionId == NewVersionId);
        Assert.Contains(projection.Items, item => item.Owner.Kind == "ActivityDraft" && item.Dependency.VersionId == OldVersionId);
        Assert.Equal(
            projection.Items.Select(SortKey),
            projection.Items.Select(SortKey).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_rebuild_refuses_an_ordinary_tenant_scope_before_reading_anything()
    {
        await using var scope = Scope();
        var coordinator = scope.Rebuilder(TestAccess.Scoped(Tenant));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RebuildAsync());

        Assert.Contains("privileged-across-scopes", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, (await ReadProjectionAsync()).Sequence);
    }

    [Fact]
    public async Task An_interrupted_rebuild_leaves_the_previous_projection_and_a_rerun_converges_once()
    {
        await EditWorkflowDraftOutsidePublishingAsync(NewVersionId);
        var crash = new RefuseSaveInterceptor(context =>
            context.ChangeTracker.Entries<ActivityDependencyProjectionState>().Any(entry => entry.State == EntityState.Modified));
        await using (var interrupted = Scope(activitiesInterceptors: [crash]))
            await Assert.ThrowsAsync<InjectedCrashException>(() => interrupted.Coordinator.RebuildAsync());

        Assert.Equal(1, crash.Refused);
        var untouched = await ReadProjectionAsync();
        Assert.Equal(1, untouched.Sequence);
        Assert.Equal("seed", untouched.RebuildId);
        Assert.All(untouched.Items, item => Assert.Equal(OldVersionId, item.Dependency.VersionId));

        await using var retry = Scope();
        await retry.Coordinator.RebuildAsync();

        var converged = await ReadProjectionAsync();
        Assert.Equal(2, converged.Sequence);
        Assert.Contains(converged.Items, item => item.Owner.Kind == "WorkflowDraft" && item.Dependency.VersionId == NewVersionId);
    }

    [Fact]
    public async Task A_rebuild_fails_closed_on_a_workflow_row_with_no_serialized_state()
    {
        await using (var workflows = database.Workflows())
        {
            var draft = await workflows.Drafts.SingleAsync(x => x.Id == WorkflowDraftId);
            draft.StateSource = null;
            await workflows.SaveChangesAsync();
        }

        await using var scope = Scope();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Coordinator.RebuildAsync());

        Assert.Contains(WorkflowDraftId, failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, (await ReadProjectionAsync()).Sequence);
    }

    // Kept beside the composition test: forwarding, one EF store behind the upgrade-bridge contracts, owned by the EF backend.
    [Fact]
    public void The_EF_publishing_registration_owns_the_upgrade_bridge_and_resolves_it()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(TestAccess.Scoped(Tenant));
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "publishing-ef-test-recovery-signing-key-32-bytes");
        services.AddActivitiesDesignEntityFrameworkCore(new() { ConnectionString = database.ConnectionString });
        services.AddWorkflowsDesignEntityFrameworkCore(new() { ConnectionString = database.ConnectionString });
        services.AddRuntimeArtifactsEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        new WorkflowsPublishingFeature().ConfigureServices(services);
        services.AddSingleton(payloads);
        services.AddSingleton<Elsa.Primitives.Contracts.ISystemClock>(new FrozenClock(Now));
        services.AddSingleton<IActivityProviderRegistry>(new ActivityProviderRegistry([new TestActivityProvider()]));
        services.AddSingleton(new Elsa.Activities.Design.Core.Services.ActivityContractAuthoringValidator(new EmptyCapabilityCatalog()));
        services.AddSingleton<Elsa.Locking.Core.IDistributedLockProvider>(new ImmediateLockProvider());
        services.AddPublishingEntityFrameworkCore(new() { ConnectionString = database.ConnectionString });

        Assert.Equal(
            PublishingPersistenceFamilyBackend.EntityFramework,
            PublishingPersistenceFamilyBackend.Find(services, PublishingPersistenceFamilyBackend.ActivityUpgradeMutation)!.Name);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<EfActivityUpgradePlanStore>();
        Assert.Same(store, scope.ServiceProvider.GetRequiredService<IActivityUpgradeDiscoverySource>());
        Assert.Same(store, scope.ServiceProvider.GetRequiredService<IActivityUpgradePlanMutationStore>());
        Assert.Same(store, scope.ServiceProvider.GetRequiredService<IActivityUpgradePublishedDraftResolver>());
        Assert.IsType<EfActivityDependencyProjectionRebuildCoordinator>(
            scope.ServiceProvider.GetRequiredService<IActivityDependencyProjectionRebuildCoordinator>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shell_start_rebuilds_missing_and_existing_usage_snapshots(bool removeSnapshot)
    {
        await EditWorkflowDraftOutsidePublishingAsync(NewVersionId);
        if (removeSnapshot)
        {
            await using var activities = database.Activities();
            await activities.ActivityDependencyProjections.ExecuteDeleteAsync();
        }
        await using var provider = StartupServices(database).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var task = Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>().OfType<RebuildActivityUsageStartupTask>());
        Assert.True(task.GetType().GetCustomAttribute<OrderAttribute>()!.Order > 2);
        Assert.Null(task.GetType().GetCustomAttribute<SingleNodeTaskAttribute>());

        await scope.ServiceProvider.GetRequiredService<ITaskExecutor>().ExecuteTaskAsync(task, default);

        var projection = await ReadProjectionAsync();
        Assert.Equal(removeSnapshot ? 1 : 2, projection.Sequence);
        Assert.Contains(projection.Items, item => item.Owner.Kind == "WorkflowDraft" && item.Dependency.VersionId == NewVersionId);
        Assert.Contains(projection.Items, item => item.Owner.Kind == "ActivityDraft" && item.Dependency.VersionId == OldVersionId);
        Assert.Equal(PersistenceAccessContext.Scoped(new(Tenant)), scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current);
    }

    [Fact]
    public async Task Shell_start_initializes_a_confirmed_empty_snapshot_on_a_fresh_database()
    {
        await using var fresh = await ActivityUpgradeDatabase.CreateAsync();
        await using var provider = StartupServices(fresh).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var task = Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>().OfType<RebuildActivityUsageStartupTask>());

        await scope.ServiceProvider.GetRequiredService<ITaskExecutor>().ExecuteTaskAsync(task, default);

        await using var activities = fresh.Activities();
        var projection = await activities.ActivityDependencyProjections.SingleAsync();
        Assert.Equal(1, projection.Sequence);
        Assert.Empty(projection.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_startup_rebuild_preserves_existing_evidence_or_leaves_it_unavailable(bool removeSnapshot)
    {
        await using (var activities = database.Activities())
            if (removeSnapshot) await activities.ActivityDependencyProjections.ExecuteDeleteAsync();
        await using (var workflows = database.Workflows())
        {
            var draft = await workflows.Drafts.SingleAsync(x => x.Id == WorkflowDraftId);
            draft.StateSource = null;
            await workflows.SaveChangesAsync();
        }
        await using var provider = StartupServices(database).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var task = Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>().OfType<RebuildActivityUsageStartupTask>());

        await scope.ServiceProvider.GetRequiredService<ITaskExecutor>().ExecuteTaskAsync(task, default);

        await using var read = database.Activities();
        var projection = await read.ActivityDependencyProjections.SingleOrDefaultAsync();
        if (removeSnapshot) Assert.Null(projection);
        else Assert.Equal("seed", Assert.IsType<ActivityDependencyProjectionState>(projection).RebuildId);
    }

    [Fact]
    public async Task Shutdown_cancellation_is_not_swallowed_by_the_usage_startup_task()
    {
        await using var provider = StartupServices(database).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var task = Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>().OfType<RebuildActivityUsageStartupTask>());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<ITaskExecutor>().ExecuteTaskAsync(task, cancellation.Token));

        Assert.Equal("seed", (await ReadProjectionAsync()).RebuildId);
    }

    [Fact]
    public async Task The_EF_projection_reader_reports_a_missing_snapshot_as_unavailable()
    {
        await using (var activities = database.Activities())
            await activities.ActivityDependencyProjections.ExecuteDeleteAsync();
        await using var scope = Scope();

        await Assert.ThrowsAsync<Elsa.Activities.Design.Persistence.Core.Stores.ActivityDependencyProjectionUnavailableException>(() =>
            scope.Design.ReadAsync(new(OldVersionId,
                new(Elsa.Activities.Design.Core.Models.ActivityDependencyDirection.Inbound, false, new HashSet<string> { "Versions", "Drafts" }),
                Tenant, null, 0, 100)));
    }

    [Fact]
    public async Task Composing_startup_contributors_does_not_require_optional_usage_maintenance_services()
    {
        var services = StartupServices(database);
        services.RemoveAll<IDistributedLockProvider>();
        services.RemoveAll<CShells.ShellSettings>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        var task = Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>().OfType<RebuildActivityUsageStartupTask>());
        await task.ExecuteAsync(default);

        Assert.Equal("seed", (await ReadProjectionAsync()).RebuildId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Maintenance_lock_failure_does_not_prevent_shell_start_or_replace_evidence(bool loseHandle)
    {
        await using var provider = StartupServices(database, new UnavailableUsageLock(loseHandle))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var task = Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>().OfType<RebuildActivityUsageStartupTask>());

        await scope.ServiceProvider.GetRequiredService<ITaskExecutor>().ExecuteTaskAsync(task, default);

        Assert.Equal("seed", (await ReadProjectionAsync()).RebuildId);
    }

    [Fact]
    public async Task The_composed_task_manager_rebuilds_usage_after_source_reconciliation()
    {
        await using var fresh = await ActivityUpgradeDatabase.CreateAsync();
        var services = StartupServices(fresh);
        services.AddScoped<IStartupTask>(_ => new ReconcileUsageSourcesStartupTask(async cancellation =>
        {
            await ActivityUpgradeSeed.BaseGraphAsync(fresh.Contexts, payloads);
            await using var activities = fresh.Activities();
            await activities.ActivityDependencyProjections.ExecuteDeleteAsync(cancellation);
        }));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await provider.GetRequiredService<ITaskManager>().StartExecutingRegisteredTasks();

        await using var read = fresh.Activities();
        var snapshot = await read.ActivityDependencyProjections.SingleAsync();
        Assert.Equal(1, snapshot.Sequence);
        Assert.Contains(snapshot.Items, item => item.Owner.Kind == "WorkflowDraft" && item.Dependency.VersionId == OldVersionId);
    }

    [Order(2)]
    private sealed class ReconcileUsageSourcesStartupTask(Func<CancellationToken, Task> reconcile) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => reconcile(cancellationToken);
    }

    private sealed class UnavailableUsageLock(bool loseHandle) : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Usage maintenance lock is unavailable.");
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Usage maintenance lock is unavailable.");
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            loseHandle ? ValueTask.FromResult<IDistributedSynchronizationHandle>(new LostHandle()) :
                throw new TimeoutException("Usage maintenance lock is unavailable.");

        private sealed class LostHandle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => new(true);
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private ServiceCollection StartupServices(ActivityUpgradeDatabase source, IDistributedLockProvider? locking = null)
    {
        var services = TaskServices.Compose(locking ?? new ImmediateLockProvider());
        services.AddLogging();
        services.AddPersistenceCore(Tenant);
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "publishing-ef-test-recovery-signing-key-32-bytes");
        services.AddActivitiesDesignEntityFrameworkCore(new() { ConnectionString = source.ConnectionString });
        services.AddWorkflowsDesignEntityFrameworkCore(new() { ConnectionString = source.ConnectionString });
        services.AddRuntimeArtifactsEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        new WorkflowsPublishingFeature().ConfigureServices(services);
        services.AddSingleton(payloads);
        services.AddSingleton<Elsa.Primitives.Contracts.ISystemClock>(new FrozenClock(Now));
        services.AddSingleton<IActivityProviderRegistry>(new ActivityProviderRegistry([new TestActivityProvider()]));
        services.AddSingleton(new ActivityContractAuthoringValidator(new EmptyCapabilityCatalog()));
        services.AddSingleton<IActivityTemplateDependencyDiscoverer>(new TestDependencyDiscoverer());
        services.AddSingleton<IActivityStructureService>(new LeafStructureService());
        services.AddPublishingEntityFrameworkCore(new() { ConnectionString = source.ConnectionString });
        // This fixture owns only the Design catalogs; slot-journal recovery has its own Runtime tests.
        services.Remove(services.Single(descriptor => descriptor.ServiceType == typeof(IStartupTask) &&
            descriptor.ImplementationType == typeof(CompleteInterruptedPublicationsStartupTask)));
        return services;
    }

    private ActivityUpgradeScope Scope(Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[]? activitiesInterceptors = null) =>
        new(
            database.Activities(activitiesInterceptors ?? []),
            database.Workflows(),
            TestAccess.Scoped(Tenant),
            new SequentialIdentities(),
            new FrozenTimeProvider(Now));

    private async Task EditWorkflowDraftOutsidePublishingAsync(string activityVersionId)
    {
        await using var workflows = database.Workflows();
        var draft = await workflows.Drafts.SingleAsync(x => x.Id == WorkflowDraftId);
        draft.StateSource = payloads.Serialize(WorkflowState(activityVersionId));
        draft.LastModifiedAt = Now.AddMinutes(5);
        await workflows.SaveChangesAsync();
    }

    private async Task<ActivityDependencyProjectionState> ReadProjectionAsync()
    {
        await using var activities = database.Activities();
        return await activities.ActivityDependencyProjections.AsNoTracking()
            .SingleAsync(x => x.Id == ActivityDependencyProjectionState.CurrentId);
    }

    private static string SortKey(Elsa.Activities.Design.Core.Models.ActivityDependencyItem item) =>
        string.Join('', item.Depth, item.Owner.Kind, item.Owner.DraftId ?? item.Owner.VersionId,
            item.Occurrence.OccurrenceId, item.Dependency.VersionId);
}
