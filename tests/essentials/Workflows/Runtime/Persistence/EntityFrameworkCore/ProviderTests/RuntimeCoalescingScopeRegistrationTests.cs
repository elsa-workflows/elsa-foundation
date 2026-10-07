using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class RuntimeCoalescingScopeRegistrationTests
{
    private const string RecoverySigningKey = "coalescing-scope-recovery-signing-key-32";
    private const string HierarchySigningKey = "coalescing-scope-hierarchy-signing-key-32";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Default_factory_and_ef_collaborators_are_scoped_to_each_command(bool validateScopes)
    {
        var services = ComposeCoalescedRuntime();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validateScopes });
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        var first = ResolveGraph(firstScope.ServiceProvider);
        var firstAgain = ResolveGraph(firstScope.ServiceProvider);
        var second = ResolveGraph(secondScope.ServiceProvider);

        Assert.IsType<RuntimeCoalescingDrainScopeFactory>(first.Factory);
        Assert.IsType<EfRuntimeCheckpointCommitStore>(first.InnerCheckpointStore.Value);
        Assert.IsType<EfSchedulerWorkQueueStore>(first.InnerQueue.Value);
        Assert.IsType<EfRuntimePostCommitOutboxStore>(first.InnerOutboxStore.Value);
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", first.DbContext.Database.ProviderName);

        AssertSameGraph(first, firstAgain);
        AssertDifferentGraphs(first, second);
    }

    private static ServiceCollection ComposeCoalescedRuntime()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = RecoverySigningKey,
            HierarchyCursorSigningKey = HierarchySigningKey
        });
        services.AddCoalescingRuntimeCheckpointPersistence();
        return services;
    }

    private static ScopeGraph ResolveGraph(IServiceProvider services) => new(
        services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>(),
        services.GetRequiredService<RuntimeCheckpointCommitter>(),
        services.GetRequiredService<IRuntimeCheckpointCommitStore>(),
        services.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>(),
        services.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>(),
        services.GetRequiredService<CoalescingInner<IRuntimePostCommitOutboxStore>>(),
        services.GetRequiredService<RuntimeDbContext>());

    private static void AssertSameGraph(ScopeGraph expected, ScopeGraph actual)
    {
        Assert.Same(expected.Factory, actual.Factory);
        Assert.Same(expected.Committer, actual.Committer);
        Assert.Same(expected.CheckpointStore, actual.CheckpointStore);
        Assert.Same(expected.InnerCheckpointStore, actual.InnerCheckpointStore);
        Assert.Same(expected.InnerCheckpointStore.Value, actual.InnerCheckpointStore.Value);
        Assert.Same(expected.InnerQueue, actual.InnerQueue);
        Assert.Same(expected.InnerQueue.Value, actual.InnerQueue.Value);
        Assert.Same(expected.InnerOutboxStore, actual.InnerOutboxStore);
        Assert.Same(expected.InnerOutboxStore.Value, actual.InnerOutboxStore.Value);
        Assert.Same(expected.DbContext, actual.DbContext);
    }

    private static void AssertDifferentGraphs(ScopeGraph first, ScopeGraph second)
    {
        Assert.NotSame(first.Factory, second.Factory);
        Assert.NotSame(first.Committer, second.Committer);
        Assert.NotSame(first.CheckpointStore, second.CheckpointStore);
        Assert.NotSame(first.InnerCheckpointStore, second.InnerCheckpointStore);
        Assert.NotSame(first.InnerCheckpointStore.Value, second.InnerCheckpointStore.Value);
        Assert.NotSame(first.InnerQueue, second.InnerQueue);
        Assert.NotSame(first.InnerQueue.Value, second.InnerQueue.Value);
        Assert.NotSame(first.InnerOutboxStore, second.InnerOutboxStore);
        Assert.NotSame(first.InnerOutboxStore.Value, second.InnerOutboxStore.Value);
        Assert.NotSame(first.DbContext, second.DbContext);
    }

    private sealed record ScopeGraph(
        IRuntimeCoalescingDrainScopeFactory Factory,
        RuntimeCheckpointCommitter Committer,
        IRuntimeCheckpointCommitStore CheckpointStore,
        CoalescingInner<IRuntimeCheckpointCommitStore> InnerCheckpointStore,
        CoalescingInner<IWorkflowSchedulerWorkQueue> InnerQueue,
        CoalescingInner<IRuntimePostCommitOutboxStore> InnerOutboxStore,
        RuntimeDbContext DbContext);
}
