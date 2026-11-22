using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowHistoryClaimRaceTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Expiry_or_transfer_during_the_last_native_edge_read_refuses_the_recipient_handoff(bool parentResume, bool transfer)
    {
        var race = new Race();
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(configureServices: services =>
        {
            services.AddWorkflowRuntimeHistory(); services.AddWorkflowStimulusRouter();
            services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Access()));
            services.Replace(ServiceDescriptor.Singleton<IWorkflowDispatchStore>(provider => new ExpiringDispatchStore(
                new InMemoryWorkflowDispatchStore(provider.GetRequiredService<InMemoryRuntimeCheckpointStoreState>()),
                race)));
        });
        var run = await fixture.StartParentAsync("history-lease-" + parentResume, "history-lease-parent-" + parentResume, "correlation",
            waitForCompletion: true, parentPartition: new("tenant-42"));
        await using var scope = fixture.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        if (parentResume)
            Assert.Equal(1, (await processor.ProcessAsync(new(1, intentKind: WorkflowDispatchLifecycle.StartChildIntentKind))).DeliveredCount);
        race.Kind = parentResume ? WorkflowDispatchLifecycle.ResumeParentIntentKind : WorkflowDispatchLifecycle.StartChildIntentKind;
        race.OnExpiry = async () =>
        {
            fixture.AdvanceTime(TimeSpan.FromHours(1));
            if (transfer)
            {
                var store = (IRuntimePostCommitOutboxClaimStore)scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
                Assert.Single(await store.ClaimAsync(new("racing-peer", scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(),
                    TimeSpan.FromMinutes(5), 1, workflowExecutionId: run.Start.WorkflowExecutionId, intentKind: race.Kind)));
            }
        };
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

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task A_claim_lost_during_actor_activation_cannot_enqueue_a_child_start_or_parent_resume(bool parentResume, bool transfer)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(configureServices: services =>
        {
            services.AddWorkflowRuntimeHistory(); services.AddWorkflowStimulusRouter();
            services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Access()));
        });
        var run = await fixture.StartParentAsync("history-activation-lease-" + parentResume + transfer,
            "history-activation-parent-" + parentResume + transfer, "correlation", waitForCompletion: true, parentPartition: new("tenant-42"));
        await using var scope = fixture.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        if (parentResume)
            Assert.Equal(1, (await processor.ProcessAsync(new(1, intentKind: WorkflowDispatchLifecycle.StartChildIntentKind))).DeliveredCount);
        var kind = parentResume ? WorkflowDispatchLifecycle.ResumeParentIntentKind : WorkflowDispatchLifecycle.StartChildIntentKind;
        var target = parentResume ? run.Start.WorkflowExecutionId : run.Identity.ChildWorkflowExecutionId;
        var raced = false;
        fixture.Actors.BeforeAgentReturned = async request =>
        {
            if (request.WorkflowExecutionId != target) return;
            fixture.Actors.BeforeAgentReturned = null;
            raced = true; fixture.AdvanceTime(TimeSpan.FromHours(1));
            if (transfer)
            {
                var claims = (IRuntimePostCommitOutboxClaimStore)scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
                Assert.Single(await claims.ClaimAsync(new("activation-peer", scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(),
                    TimeSpan.FromMinutes(5), 1, workflowExecutionId: run.Start.WorkflowExecutionId, intentKind: kind)));
            }
        };
        await processor.ProcessAsync(new(1, intentKind: kind));
        Assert.True(raced);
        if (parentResume)
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().FindAsync(run.Start.WorkflowExecutionId, run.Identity.WaitBookmarkId));
        else Assert.Empty(fixture.ChildProbe.Observations);
        Assert.DoesNotContain(fixture.Actors.Envelopes, envelope => envelope.WorkflowExecutionId == target &&
            envelope.Command.Kind == (parentResume ? WorkflowExecutionCommandKind.ResumeBookmark : WorkflowExecutionCommandKind.Start));
    }

    private sealed class Race { internal string? Kind; internal int NativeEdgeReads; internal bool Expired; internal Func<Task>? OnExpiry; }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("tenant-42")); }

    private sealed class ExpiringDispatchStore(InMemoryWorkflowDispatchStore inner, Race race) :
        IWorkflowDispatchStore, IWorkflowDispatchQueryStore, IWorkflowDispatchAdmissionStore, IWorkflowDispatchCancellationStore,
        IWorkflowDispatchDeleteStore, IWorkflowDispatchRetentionRootStore
    {
        public ValueTask<WorkflowDispatchRecord> SaveAsync(WorkflowDispatchRecord record, CancellationToken token = default) => inner.SaveAsync(record, token);
        public async ValueTask<WorkflowDispatchRecord?> FindAsync(string id, CancellationToken token = default)
        {
            var record = await inner.FindAsync(id, token);
            if (race.Kind is not null && ++race.NativeEdgeReads == (race.Kind == WorkflowDispatchLifecycle.ResumeParentIntentKind ? 2 : 3))
            {
                race.Expired = true;
                await race.OnExpiry!();
            }
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
