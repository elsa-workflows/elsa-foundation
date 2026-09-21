using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
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
