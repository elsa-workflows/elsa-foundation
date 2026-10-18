using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Proves a fault plus its ordered parent notifications survives EF persistence as one outcome and that a lost outbox
/// acknowledgement cannot invoke the parent callback twice after a provider restart.
/// </summary>
public sealed class CombinedChildOutcomeEfTests : IDisposable
{
    private const string RecoverySigningKey = "ef-combined-outcome-recovery-signing-key-32";
    private const string HierarchySigningKey = "ef-combined-outcome-hierarchy-signing-key-32";

    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-runtime-ef-combined-outcome-{Guid.NewGuid():N}.db");
    private readonly CombinedOutcomeClock _clock = new(new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero));
    private readonly CombinedOutcomeObservation _observation = new();
    private readonly CombinedOutcomeOutboxProbe _outboxProbe = new();

    [Fact]
    public async Task Fault_and_ordered_notifications_survive_reload_and_ack_loss_without_splitting_or_replaying_the_parent_callback()
    {
        RuntimePostCommitOutboxItem failedOutcome;
        RuntimeSchedulerWorkItem parentOutcomeWorkItem;

        await using (var first = await StartGenerationAsync(failFirstCombinedOutcome: true))
        {
            var run = await first.RunAsync(
                CombinedChildOutcomeEfFixture.CreateExecutable(),
                allowPendingWorkOnTerminalCompletion: false,
                allowAcceptedButFaulted: true);

            Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
            Assert.Empty(_observation.Outcomes);
            var composite = run.State(CombinedChildOutcomeEfFixture.CompositeNodeId);
            Assert.Equal(ActivityExecutionStatus.Faulted, composite.Status);
            Assert.Equal(
                $"refused {CombinedChildOutcomeEfFixture.SecretMarker}",
                composite.Fault?.Message);
            Assert.DoesNotContain(CombinedChildOutcomeEfFixture.Secret, composite.Fault?.Message, StringComparison.Ordinal);
            Assert.Equal(ActivityExecutionStatus.Suspended, run.State(CombinedChildOutcomeEfFixture.WaitingNodeId).Status);

            var incidents = await ListIncidentsAsync(first);
            var pendingOutcomeIncident = Assert.Single(incidents, incident => incident.ExecutableNodeId == CombinedChildOutcomeEfFixture.CompositeNodeId);
            Assert.Equal(IncidentStatus.Blocking, pendingOutcomeIncident.Status);
            Assert.Null(pendingOutcomeIncident.ResolutionOutcome);

            // The real scheduler drain and both real drain observers have run. The failed combined enqueue leaves
            // the workflow recoverable instead of terminalizing it ahead of the durable parent outcome.
            Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(first));

            failedOutcome = await FindFailedParentOutcomeAsync(first);
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, failedOutcome.Status);
            Assert.Equal(1, failedOutcome.DeliveryAttemptCount);
            parentOutcomeWorkItem = ReadParentOutcomeWorkItem(failedOutcome);
            var payload = Assert.IsType<RuntimeCompleteActivityCommandPayload>(parentOutcomeWorkItem.Payload.Deserialize<RuntimeCompleteActivityCommandPayload>());
            Assert.Equal(SchedulerCompletionKind.ParentOutcomeEvaluation, payload.CompletionKind);
            Assert.Equal(CombinedChildOutcomeEfFixture.ParentNodeId, payload.ExecutableNodeId);
            Assert.Equal(CombinedChildOutcomeEfFixture.ParentActivityExecutionId, payload.ActivityExecutionId);
            Assert.Equal(CombinedChildOutcomeEfFixture.CompositeActivityExecutionId, payload.CompletedChildActivityExecutionId);
            Assert.Equal(pendingOutcomeIncident.IncidentId, payload.ParentOutcome?.IncidentId);
            Assert.Equal(parentOutcomeWorkItem.WorkItemId,
                pendingOutcomeIncident.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentOutcomeWorkItemId));
            Assert.Equal(CombinedChildOutcomeEfFixture.FaultCode, payload.ParentOutcome?.Fault.Code);
            Assert.Equal($"refused {CombinedChildOutcomeEfFixture.SecretMarker}", payload.ParentOutcome?.Fault.Message);
            Assert.Equal(
                [CombinedChildOutcomeEfFixture.FirstNotificationCode, CombinedChildOutcomeEfFixture.SecondNotificationCode],
                payload.ParentOutcome?.Notifications.Select(notification => notification.Code).ToArray());

            var firstOutcomeDispatch = Assert.Single(
                _outboxProbe.Attempts,
                attempt => attempt.CompletionKind == SchedulerCompletionKind.ParentOutcomeEvaluation);
            Assert.Equal(parentOutcomeWorkItem.WorkItemId, firstOutcomeDispatch.WorkItemId);
            Assert.DoesNotContain(_outboxProbe.Attempts, attempt => attempt.CommandKind == WorkflowExecutionCommandKind.NotifyParentActivity);
        }

        // A new provider reloads the failed durable row and enqueues its persisted intent directly. A real resumption
        // sweep re-drives it under normal execution ownership. The fixed clock keeps the source row before its retry time,
        // so the callback checkpoint can commit while the original outbox delivery remains unacknowledged.
        await using (var second = await StartGenerationAsync())
        {
            var persistedBeforeReplay = await FindOutboxItemAsync(second, failedOutcome.OutboxItemId);
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, persistedBeforeReplay?.Status);
            Assert.NotNull(persistedBeforeReplay);

            await using (var scope = second.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>()
                    .DispatchAsync(persistedBeforeReplay!.Intent);

            var replayWindowSweep = await second.SweepAsync();
            Assert.Single(replayWindowSweep.Dispatches);

            var stillUnacknowledged = await FindOutboxItemAsync(second, failedOutcome.OutboxItemId);
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, stillUnacknowledged?.Status);
            Assert.Equal(2, Assert.Single(_observation.Outcomes).Notifications.Count);
            Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(second));

            var parentOutcome = Assert.Single(_observation.Outcomes);
            Assert.Equal(CombinedChildOutcomeEfFixture.CompositeActivityExecutionId, parentOutcome.ChildActivityExecutionId);
            Assert.Equal(CombinedChildOutcomeEfFixture.CompositeNodeId, parentOutcome.ChildExecutableNodeId);
            Assert.Null(parentOutcome.ChildIterationId);
            Assert.Equal(CombinedChildOutcomeEfFixture.FaultCode, parentOutcome.FaultCode);
            Assert.Equal($"refused {CombinedChildOutcomeEfFixture.SecretMarker}", parentOutcome.FaultMessage);
            Assert.Equal(failedOutcome.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!
                .Payload.Deserialize<RuntimeCompleteActivityCommandPayload>()!.ParentOutcome!.IncidentId, parentOutcome.IncidentId);
            Assert.Equal(
                [CombinedChildOutcomeEfFixture.FirstNotificationCode, CombinedChildOutcomeEfFixture.SecondNotificationCode],
                parentOutcome.Notifications.Select(notification => notification.Code).ToArray());
            Assert.Equal(
                ["{\"ordinal\":1,\"value\":\"first\"}", "{\"ordinal\":2,\"value\":\"second\"}"],
                parentOutcome.Notifications.Select(notification => notification.Payload).ToArray());

            var afterParentCommit = await second.ReadRunAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId);
            var committedFaultedChild = afterParentCommit.State(CombinedChildOutcomeEfFixture.CompositeNodeId);
            Assert.Equal(ActivityExecutionStatus.Faulted, committedFaultedChild.Status);
            Assert.Equal(parentOutcomeWorkItem.WorkItemId,
                committedFaultedChild.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
            Assert.Equal(ActivityExecutionStatus.Cancelled, afterParentCommit.State(CombinedChildOutcomeEfFixture.WaitingNodeId).Status);
            var afterBookmark = Assert.Single(await BookmarksAsync(second));
            Assert.Equal(CombinedChildOutcomeEfFixture.AfterActivityExecutionId, afterBookmark.ActivityExecutionId);
            var absorbedIncident = Assert.Single(
                await ListIncidentsAsync(second),
                incident => incident.ExecutableNodeId == CombinedChildOutcomeEfFixture.CompositeNodeId);
            Assert.Equal(IncidentStatus.Resolved, absorbedIncident.Status);
            Assert.Equal(IncidentResolutionActionKinds.AbsorbFault, absorbedIncident.ResolutionOutcome?.ActionKind);
            Assert.Equal(IncidentResolutionSystemSources.StructuralFaultAbsorption, absorbedIncident.ResolutionOutcome?.SystemSource);
            Assert.Equal("ef-combined-outcome-accepted",
                absorbedIncident.Metadata.GetValueOrDefault(RuntimeMetadataKeys.FaultAbsorptionReason));
            Assert.Equal(ActivityExecutionStatus.Suspended, afterParentCommit.State(CombinedChildOutcomeEfFixture.AfterNodeId).Status);
        }

        _clock.Advance(RuntimeSchedulerPostCommitIntentDispatcher.RetryPolicy.Delay!.Value);
        var claimBarrier = new CombinedOutcomeRetryClaimBarrier();
        await using (var third = await StartGenerationAsync(claimBarrier: claimBarrier))
        {
            // Two fresh scopes race the same failed source row. A barrier at ClaimAsync makes both durable claims
            // contend, while EF's claim transition grants the combined row to only one delivery pass.
            var concurrentSweeps = await Task.WhenAll(third.SweepAsync(), third.SweepAsync());
            Assert.Equal(2, claimBarrier.Arrivals);
            Assert.Equal(1, concurrentSweeps.Count(result => result.OutboxAttemptedCount > 0));
            Assert.Equal(1, concurrentSweeps.Sum(result => result.OutboxDeliveredCount));
            Assert.Equal(0, concurrentSweeps.Sum(result => result.OutboxFailedCount));
            Assert.Contains(concurrentSweeps.SelectMany(result => result.Dispatches), dispatch =>
                dispatch.WorkflowExecutionId == CombinedChildOutcomeEfFixture.WorkflowExecutionId &&
                dispatch.Outcome is RuntimeResumptionDispatchOutcome.Accepted or RuntimeResumptionDispatchOutcome.Duplicate);
            await using (var scope = third.Services.CreateAsyncScope())
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAllAsync(new RuntimeSchedulerWorkQuery(CombinedChildOutcomeEfFixture.WorkflowExecutionId)));

            Assert.Single(_observation.Outcomes);
            var replayedRun = await third.ReadRunAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Running, replayedRun.WorkflowState?.Status);
            Assert.Equal(ActivityExecutionStatus.Faulted, replayedRun.State(CombinedChildOutcomeEfFixture.CompositeNodeId).Status);
            Assert.Equal(ActivityExecutionStatus.Cancelled, replayedRun.State(CombinedChildOutcomeEfFixture.WaitingNodeId).Status);
            Assert.Equal(ActivityExecutionStatus.Suspended, replayedRun.State(CombinedChildOutcomeEfFixture.AfterNodeId).Status);
            Assert.Single(await BookmarksAsync(third));

            var retriedOutcome = await FindOutboxItemAsync(third, failedOutcome.OutboxItemId);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, retriedOutcome?.Status);
            Assert.Equal(2, retriedOutcome?.DeliveryAttemptCount);
            var retriedWorkItem = ReadParentOutcomeWorkItem(retriedOutcome!);
            Assert.Equal(parentOutcomeWorkItem.WorkItemId, retriedWorkItem.WorkItemId);
            var retriedPayload = retriedWorkItem.Payload.Deserialize<RuntimeCompleteActivityCommandPayload>();
            Assert.Equal(SchedulerCompletionKind.ParentOutcomeEvaluation, retriedPayload?.CompletionKind);
            Assert.Equal(CombinedChildOutcomeEfFixture.ParentActivityExecutionId, retriedPayload?.ActivityExecutionId);
            Assert.Equal(CombinedChildOutcomeEfFixture.CompositeActivityExecutionId, retriedPayload?.CompletedChildActivityExecutionId);
            Assert.DoesNotContain(_outboxProbe.Attempts, attempt => attempt.CommandKind == WorkflowExecutionCommandKind.NotifyParentActivity);

            var incidents = await ListIncidentsAsync(third);
            Assert.Equal(IncidentStatus.Resolved,
                Assert.Single(incidents, incident => incident.ExecutableNodeId == CombinedChildOutcomeEfFixture.CompositeNodeId).Status);
            Assert.Equal(IncidentStatus.Suppressed,
                Assert.Single(incidents, incident => incident.ExecutableNodeId == CombinedChildOutcomeEfFixture.FaultingNodeId).Status);

            var afterBookmark = Assert.Single(await BookmarksAsync(third));
            Assert.Equal(CombinedChildOutcomeEfFixture.AfterActivityExecutionId, afterBookmark.ActivityExecutionId);
            var completed = await third.ResumeAsync(
                WorkflowExecutionHarness.Identity,
                afterBookmark.BookmarkId,
                afterBookmark.ActivityExecutionId,
                afterBookmark.ExecutableNodeId,
                afterBookmark.ResumeTargetId,
                afterBookmark.StimulusType,
                afterBookmark.StimulusHash,
                JsonSerializer.SerializeToElement(new EventReceived(CombinedChildOutcomeEfFixture.AfterEventName)));
            Assert.Equal(WorkflowExecutionStatus.Completed, completed.WorkflowState?.Status);
            Assert.Equal(ActivityExecutionStatus.Completed, completed.State(CombinedChildOutcomeEfFixture.AfterNodeId).Status);
            Assert.Empty(await BookmarksAsync(third));
        }
    }

    public void Dispose()
    {
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }

    private async Task<WorkflowExecutionHarness> StartGenerationAsync(
        bool failFirstCombinedOutcome = false,
        CombinedOutcomeRetryClaimBarrier? claimBarrier = null)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<TimeProvider>(_clock);
                services.AddSingleton(_observation);
                services.AddSingleton(_outboxProbe);
                services.Replace(ServiceDescriptor.Scoped<IRuntimePostCommitIntentDispatcher>(provider =>
                    new FailFirstCombinedOutcomeDispatcher(
                        ActivatorUtilities.CreateInstance<RuntimePostCommitIntentDispatcher>(provider),
                        provider.GetRequiredService<CombinedOutcomeOutboxProbe>())));
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={_databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = RecoverySigningKey,
                    HierarchyCursorSigningKey = HierarchySigningKey
                })
                .AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                if (claimBarrier is not null)
                {
                    services.AddSingleton(claimBarrier);
                    var processorDescriptor = services.Last(descriptor =>
                        descriptor.ServiceType == typeof(IRuntimePostCommitOutboxProcessor));
                    var processorFactory = processorDescriptor.ImplementationFactory
                        ?? throw new InvalidOperationException("The runtime outbox processor registration must be a factory for this concurrency contract.");
                    services.Replace(ServiceDescriptor.Scoped<IRuntimePostCommitOutboxProcessor>(provider =>
                        new CombinedOutcomeClaimBarrierOutboxProcessor(
                            (IRuntimePostCommitOutboxProcessor)processorFactory(provider),
                            provider.GetRequiredService<CombinedOutcomeRetryClaimBarrier>())));
                }
            })
            .Build(
                WorkflowExecutionHarness.Identity,
                CombinedChildOutcomeEfFixture.WorkflowExecutionId,
                CombinedChildOutcomeEfFixture.ActivityExecutionIds);

        foreach (var initializer in harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        if (failFirstCombinedOutcome)
            _outboxProbe.FailNextParentOutcome();
        return harness;
    }

    private sealed class CombinedOutcomeClock(DateTimeOffset now) : TimeProvider
    {
        private long _offsetTicks;

        public void Advance(TimeSpan amount) => Interlocked.Add(ref _offsetTicks, amount.Ticks);

        public override DateTimeOffset GetUtcNow() => now.AddTicks(Interlocked.Read(ref _offsetTicks));
    }

    private static async Task<RuntimePostCommitOutboxItem> FindFailedParentOutcomeAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var due = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(1),
            limit: 100,
            workflowExecutionId: CombinedChildOutcomeEfFixture.WorkflowExecutionId,
            intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        return Assert.Single(due, item => IsParentOutcome(item.Intent));
    }

    private static async Task<RuntimePostCommitOutboxItem?> FindOutboxItemAsync(WorkflowExecutionHarness harness, string outboxItemId)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(outboxItemId);
    }

    private static async Task<IReadOnlyCollection<IncidentState>> ListIncidentsAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId);
    }

    private static async Task<IReadOnlyCollection<BookmarkState>> BookmarksAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId);
    }

    private static async Task<WorkflowExecutionStatus?> WorkflowStatusAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<EfWorkflowExecutionStateStore>()
            .FindAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId))?.Status;
    }

    private static RuntimeSchedulerWorkItem ReadParentOutcomeWorkItem(RuntimePostCommitOutboxItem item)
    {
        Assert.True(IsParentOutcome(item.Intent));
        return Assert.IsType<RuntimeSchedulerWorkItem>(item.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>());
    }

    private static bool IsParentOutcome(RuntimePostCommitIntent intent) =>
        intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork &&
        intent.Payload is { } payload &&
        payload.Deserialize<RuntimeSchedulerWorkItem>() is { CommandKind: WorkflowExecutionCommandKind.CompleteActivity } workItem &&
        workItem.Payload is { } completionPayload &&
        completionPayload.Deserialize<RuntimeCompleteActivityCommandPayload>()?.CompletionKind == SchedulerCompletionKind.ParentOutcomeEvaluation;
}
