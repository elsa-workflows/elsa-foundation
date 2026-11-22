using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowHistoryClaimRaceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Expiry_during_the_last_native_edge_read_refuses_the_recipient_handoff(bool parentResume)
    {
        var race = new Race();
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(configureServices: services =>
        {
            services.AddWorkflowRuntimeHistory(); services.AddWorkflowStimulusRouter();
            services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Access()));
            services.Replace(ServiceDescriptor.Singleton<IWorkflowDispatchStore>(provider => new ExpiringDispatchStore(
                new InMemoryWorkflowDispatchStore(provider.GetRequiredService<InMemoryRuntimeCheckpointStoreState>()),
                (FakeTimeProvider)provider.GetRequiredService<TimeProvider>(), race)));
            var descriptor = services.Single(item => item.ServiceType == typeof(IPostCommitOutboxLookupStore));
            services.Remove(descriptor);
            services.AddScoped<IPostCommitOutboxLookupStore>(provider => new CountingLookup(
                (IPostCommitOutboxLookupStore)(descriptor.ImplementationFactory?.Invoke(provider) ??
                    descriptor.ImplementationInstance ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), race));
        });
        var run = await fixture.StartParentAsync("history-lease-" + parentResume, "history-lease-parent-" + parentResume, "correlation",
            waitForCompletion: true, parentPartition: new("tenant-42"));
        await using var scope = fixture.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        if (parentResume)
            Assert.Equal(1, (await processor.ProcessAsync(new(1, intentKind: WorkflowDispatchLifecycle.StartChildIntentKind))).DeliveredCount);
        race.Kind = parentResume ? WorkflowDispatchLifecycle.ResumeParentIntentKind : WorkflowDispatchLifecycle.StartChildIntentKind;
        await processor.ProcessAsync(new(1, intentKind: race.Kind));
        Assert.True(race.Expired);
        if (parentResume)
        {
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().FindAsync(run.Start.WorkflowExecutionId, run.Identity.WaitBookmarkId));
            Assert.Equal(ActivityExecutionStatus.Suspended,
                (await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().FindAsync(run.Start.WorkflowExecutionId, run.Activity.InvocationId))!.Status);
        }
        else Assert.Empty(fixture.ChildProbe.Observations);
    }

    private sealed class Race { internal string? Kind; internal int Reads; internal bool Expired; }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("tenant-42")); }

    private sealed class CountingLookup(IPostCommitOutboxLookupStore inner, Race race) : IPostCommitOutboxLookupStore
    {
        public bool CanInspectHistorySources(IRuntimeHistoryStore reader) => inner.CanInspectHistorySources(reader);
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string id, CancellationToken token = default) => inner.FindAsync(id, token);
        public async ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string id, CancellationToken token = default)
        {
            var item = await inner.FindDurableAsync(id, token);
            if (race.Kind is not null && item?.Intent.Kind == race.Kind) race.Reads++;
            return item;
        }
    }

    private sealed class ExpiringDispatchStore(InMemoryWorkflowDispatchStore inner, FakeTimeProvider clock, Race race) :
        IWorkflowDispatchStore, IWorkflowDispatchQueryStore, IWorkflowDispatchAdmissionStore, IWorkflowDispatchCancellationStore,
        IWorkflowDispatchDeleteStore, IWorkflowDispatchRetentionRootStore
    {
        public ValueTask<WorkflowDispatchRecord> SaveAsync(WorkflowDispatchRecord record, CancellationToken token = default) => inner.SaveAsync(record, token);
        public async ValueTask<WorkflowDispatchRecord?> FindAsync(string id, CancellationToken token = default)
        {
            var record = await inner.FindAsync(id, token);
            if (race.Reads >= 2 && !race.Expired) { race.Expired = true; clock.Advance(TimeSpan.FromHours(1)); }
            return record;
        }
        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(string id, CancellationToken token = default) => inner.ListAsync(id, token);
        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(WorkflowDispatchQuery query, CancellationToken token = default) => inner.QueryAsync(query, token);
        public ValueTask<WorkflowDispatchAdmissionResult> TryAdmitAsync(string id, DateTimeOffset at, CancellationToken token = default) => inner.TryAdmitAsync(id, at, token);
        public ValueTask<WorkflowDispatchCancellationResult> ApplyCancellationAsync(WorkflowDispatchCancellationRequest request, CancellationToken token = default) => inner.ApplyCancellationAsync(request, token);
        public ValueTask<bool> TryDeleteAsync(WorkflowDispatchRecord expected, CancellationToken token = default) => inner.TryDeleteAsync(expected, token);
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken token = default) => inner.ListPinnedExecutableArtifactIdsAsync(token);
    }
}
