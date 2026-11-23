using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class BpmnCalledWorkflowControlEfTests
{
    [Theory]
    [InlineData(false, PersistenceMode.Durable)]
    [InlineData(false, PersistenceMode.Balanced)]
    [InlineData(true, PersistenceMode.Durable)]
    [InlineData(true, PersistenceMode.Balanced)]
    public async Task Expired_called_control_claim_takeover_converges_before_or_after_caller_acceptance(
        bool afterEffect, PersistenceMode mode)
    {
        var unavailable = new CalledResumeCut { HoldNotifications = true };
        RuntimeChildControlTransport control;
        RuntimePostCommitOutboxItem original;
        WorkflowExecutableIdentity pin;
        string childId;
        await using (var first = await StartAsync(mode, unavailable, "claim-first"))
        {
            var child = await EscalatingChildAsync(false);
            pin = child.Identity;
            var reference = await first.PublishAsync(child, "source-claim-child");
            var parent = await first.PublishAsync(await EscalationParentAsync(reference, pin, false), "source-claim-caller");
            await first.StartPublishedAsync(parent, ParentExecutionId,
                partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));
            await DrainCalledAsync(first);
            control = ReadControl(Assert.IsType<RuntimePostCommitIntent>(unavailable.LastHeldNotification));
            childId = control.Batch.ChildWorkflowExecutionId;
            original = await SourceAsync(first, control.SourceOutboxItemId);
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, original.Status);
            Assert.True(original.DeliveryAttemptCount > 0);
            Assert.False(control.Batch.IsTerminal);
            Assert.Equal(0, control.Batch.Ordinal);
            Assert.Equal("bpmn.escalation", Assert.Single(control.Batch.Notifications).Code);
            Assert.Empty(unavailable.ControlCalls);
        }

        _clock.Advance(TimeSpan.FromMinutes(1));
        var ownerCut = new CalledClaimCut(afterEffect);
        var peerCut = new CalledClaimCut(false);
        var ownerObservation = new CalledResumeCut();
        var peerObservation = new CalledResumeCut();
        string stableFacts;
        string deliveredRow;
        await using (var owner = await StartAsync(mode, ownerObservation, "claim-owner", ownerCut))
        await using (var peer = await StartAsync(mode, peerObservation, "claim-peer", peerCut))
        {
            Assert.NotSame(owner.Services, peer.Services);
            await using var ownerScope = owner.Services.CreateAsyncScope();
            await using var peerScope = peer.Services.CreateAsyncScope();
            var request = new RuntimePostCommitOutboxProcessRequest(1, childId, NotifyControlIntentKind);
            using var deliveryCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var ownerDelivery = ownerScope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                .ProcessAsync(request, deliveryCancellation.Token).AsTask();
            Task<RuntimePostCommitOutboxProcessResult>? peerDelivery = null;
            var assertionFailed = false;
            try
            {
                var ownerIntent = await ownerCut.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(original.Intent.IsEquivalentTo(ownerIntent));
                var owned = await SourceAsync(peer, control.SourceOutboxItemId);
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, owned.Status);
                Assert.False(string.IsNullOrWhiteSpace(owned.DeliveringOwnerId));
                Assert.Equal(original.DeliveryFencingToken + 1, owned.DeliveryFencingToken);
                Assert.Equal(original.DeliveryAttemptCount, owned.DeliveryAttemptCount);
                Assert.True(owned.DeliveryVisibleAfter > _clock.GetUtcNow());
                Assert.Equal(afterEffect ? 1 : 0, ownerObservation.ControlCalls.Count);
                var caller = await peer.ReadRunAsync(ParentExecutionId);
                Assert.Equal(WorkflowExecutionStatus.Running, caller.WorkflowState!.Status);
                if (afterEffect)
                {
                    var inbox = Assert.IsType<RuntimeChildControlInbox>(caller.State(DispatchNodeId).ChildControlInbox);
                    Assert.True(inbox.HasAccepted(control));
                    Assert.Equal(1, inbox.NextOrdinal);
                    Assert.False(inbox.IsTerminalConsumed);
                    Assert.Equal(ActivityExecutionStatus.Suspended, caller.State(WarningNodeId).Status);
                }
                else
                {
                    Assert.Null(caller.State(DispatchNodeId).ChildControlInbox);
                    caller.AssertDidNotRun(WarningNodeId);
                }
                var beforeExpiry = await peerScope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                    .ProcessAsync(request);
                Assert.Equal(0, beforeExpiry.AttemptedCount);
                Assert.False(peerCut.Entered.Task.IsCompleted);
                Assert.Equal(JsonSerializer.Serialize(owned, Json), JsonSerializer.Serialize(await SourceAsync(peer, control.SourceOutboxItemId), Json));

                _clock.Advance(owned.DeliveryVisibleAfter!.Value - _clock.GetUtcNow() + TimeSpan.FromSeconds(1));
                peerDelivery = peerScope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                    .ProcessAsync(request, deliveryCancellation.Token).AsTask();
                var peerIntent = await peerCut.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(ownerIntent.IsEquivalentTo(peerIntent));
                var taken = await SourceAsync(owner, control.SourceOutboxItemId);
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, taken.Status);
                Assert.NotEqual(owned.DeliveringOwnerId, taken.DeliveringOwnerId);
                Assert.False(string.IsNullOrWhiteSpace(taken.DeliveringOwnerId));
                Assert.Equal(owned.DeliveryFencingToken + 1, taken.DeliveryFencingToken);
                Assert.Equal(original.DeliveryAttemptCount, taken.DeliveryAttemptCount);
                Assert.True(taken.DeliveryVisibleAfter > _clock.GetUtcNow());
                Assert.False(ownerDelivery.IsCompleted);
                peerCut.Release.TrySetResult();
                var peerResult = await peerDelivery.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(1, peerResult.AttemptedCount);
                Assert.Equal(1, peerResult.DeliveredCount);
                Assert.Equal(0, peerResult.FailedCount);
                Assert.Equal(0, peerResult.SupersededCount);
                var winner = await SourceAsync(peer, control.SourceOutboxItemId);
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, winner.Status);
                Assert.Equal(original.DeliveryAttemptCount + 1, winner.DeliveryAttemptCount);
                Assert.Equal(taken.DeliveryFencingToken, winner.DeliveryFencingToken);
                Assert.True(original.Intent.IsEquivalentTo(winner.Intent));
                var accepted = await peer.ReadRunAsync(ParentExecutionId);
                Assert.Equal(WorkflowExecutionStatus.Running, accepted.WorkflowState!.Status);
                var acceptedInbox = Assert.IsType<RuntimeChildControlInbox>(accepted.State(DispatchNodeId).ChildControlInbox);
                Assert.True(acceptedInbox.HasAccepted(control));
                Assert.Equal(1, acceptedInbox.NextOrdinal);
                Assert.False(acceptedInbox.IsTerminalConsumed);
                Assert.Empty(acceptedInbox.Pending);
                Assert.Equal(ActivityExecutionStatus.Suspended, accepted.State(WarningNodeId).Status);
                stableFacts = await EscalationFactsAsync(peer, childId, pin, mode);
                deliveredRow = JsonSerializer.Serialize(winner, Json);
                Assert.False(ownerDelivery.IsCompleted);
                ownerCut.Release.TrySetResult();
                var oldResult = await ownerDelivery.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(1, oldResult.AttemptedCount);
                Assert.Equal(1, oldResult.SupersededCount);
                Assert.Equal(0, oldResult.DeliveredCount);
                Assert.Equal(0, oldResult.FailedCount);
                Assert.True(Assert.Single(oldResult.Items).IsSuperseded);
                var actual = Assert.Single(ownerObservation.ControlCalls.Concat(peerObservation.ControlCalls));
                Assert.Equal(control.CallingActivityExecutionId, actual.CallingActivityExecutionId);
                Assert.Equal([0L], actual.Ordinals);
                Assert.Equal(afterEffect ? 1 : 0, ownerObservation.ControlCalls.Count);
                Assert.Equal(afterEffect ? 0 : 1, peerObservation.ControlCalls.Count);
                Assert.Equal(stableFacts, await EscalationFactsAsync(peer, childId, pin, mode));
                Assert.Equal(deliveredRow, JsonSerializer.Serialize(await SourceAsync(peer, control.SourceOutboxItemId), Json));
            }
            catch
            {
                assertionFailed = true;
                throw;
            }
            finally
            {
                ownerCut.Release.TrySetResult();
                peerCut.Release.TrySetResult();
                try
                {
                    await Task.WhenAll(peerDelivery is null ? [ownerDelivery] : new[] { ownerDelivery, peerDelivery });
                }
                catch when (assertionFailed) { /* Observe both delivery outcomes while preserving the original assertion. */ }
            }
        }

        var resumed = new CalledResumeCut();
        await using (var final = await StartAsync(mode, resumed, "claim-final"))
        {
            Assert.Equal(stableFacts, await EscalationFactsAsync(final, childId, pin, mode));
            Assert.Equal(deliveredRow, JsonSerializer.Serialize(await SourceAsync(final, control.SourceOutboxItemId), Json));
            await ResumeCalledWaitAsync(final, ParentExecutionId, WarningNodeId, "warning-handler-continue");
            await ResumeCalledWaitAsync(final, childId, ChildWaitNodeId, "called-continue");
            await DrainCalledAsync(final);
            var terminal = Assert.Single(resumed.ControlCalls);
            Assert.Equal(control.CallingActivityExecutionId, terminal.CallingActivityExecutionId);
            Assert.Equal([1L], terminal.Ordinals);
            var run = await final.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState!.Status);
            Assert.Equal(WorkflowExecutionHarness.Identity, run.WorkflowState.PinnedExecutable);
            Assert.Equal(mode.ToString(), run.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            var inbox = Assert.IsType<RuntimeChildControlInbox>(run.State(DispatchNodeId).ChildControlInbox);
            Assert.Equal(2, inbox.NextOrdinal);
            Assert.True(inbox.IsTerminalConsumed);
            Assert.Empty(inbox.Pending);
            var completedChild = (await final.ReadRunAsync(childId)).WorkflowState!;
            Assert.Equal(WorkflowExecutionStatus.Completed, completedChild.Status);
            Assert.Equal(pin, completedChild.PinnedExecutable);
            Assert.Equal(mode.ToString(), completedChild.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            Assert.Equal(deliveredRow, JsonSerializer.Serialize(await SourceAsync(final, control.SourceOutboxItemId), Json));
            await using var scope = final.Services.CreateAsyncScope();
            foreach (var id in new[] { ParentExecutionId, childId })
            {
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(id));
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(id));
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(id));
            }
        }
    }

    [Theory]
    [InlineData(false, PersistenceMode.Durable)]
    [InlineData(false, PersistenceMode.Balanced)]
    [InlineData(true, PersistenceMode.Durable)]
    [InlineData(true, PersistenceMode.Balanced)]
    public async Task Separate_live_claims_deliver_terminal_before_earlier_notification_without_advancing_across_gap(
        bool ordinaryFault, PersistenceMode mode)
    {
        var unavailable = new CalledResumeCut { HoldNotifications = true };
        RuntimeChildControlTransport earlier;
        WorkflowExecutableIdentity pin;
        string childId;
        await using (var first = await StartAsync(mode, unavailable, "split-first"))
        {
            var child = await GapChildAsync(ordinaryFault);
            pin = child.Identity;
            var reference = await first.PublishAsync(child, "source-split-child");
            var parent = await first.PublishAsync(await EscalationParentAsync(reference, pin, false), "source-split-caller");
            await first.StartPublishedAsync(parent, ParentExecutionId,
                partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));
            await DrainCalledAsync(first);
            earlier = ReadControl(Assert.IsType<RuntimePostCommitIntent>(unavailable.LastHeldNotification));
            childId = earlier.Batch.ChildWorkflowExecutionId;
            Assert.Equal(0, earlier.Batch.Ordinal);
            Assert.False(earlier.Batch.IsTerminal);
            Assert.Empty(unavailable.ControlCalls);
        }

        _clock.Advance(TimeSpan.FromMinutes(1));
        var earlierCut = new CalledClaimCut(false);
        var terminalCut = new CalledClaimCut(false, DispatchWorkflowConstants.ResumeParentIntentKind);
        var earlierObservation = new CalledResumeCut();
        var terminalObservation = new CalledResumeCut();
        string stable;
        string childFacts;
        string terminalRow;
        string earlierRow;
        RuntimeChildControlTransport terminal;
        await using (var owner = await StartAsync(mode, earlierObservation, "split-owner", earlierCut))
        await using (var peer = await StartAsync(mode, terminalObservation, "split-peer", terminalCut))
        {
            await using var earlierScope = owner.Services.CreateAsyncScope();
            await using var terminalScope = peer.Services.CreateAsyncScope();
            using var deliveryCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var earlierDelivery = earlierScope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, childId, NotifyControlIntentKind), deliveryCancellation.Token).AsTask();
            Task<RuntimePostCommitOutboxProcessResult>? terminalDelivery = null;
            var assertionFailed = false;
            try
            {
                Assert.Equal(earlier.SourceIntentId, (await earlierCut.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30))).IntentId);
                var heldEarlier = await SourceAsync(peer, earlier.SourceOutboxItemId);
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, heldEarlier.Status);
                Assert.False(string.IsNullOrWhiteSpace(heldEarlier.DeliveringOwnerId));
                Assert.True(heldEarlier.DeliveryFencingToken > 0);
                Assert.True(heldEarlier.DeliveryVisibleAfter > _clock.GetUtcNow());
                var semanticBeforeTerminal = JsonSerializer.Serialize((await peer.ReadRunAsync(ParentExecutionId)).State(F.ProcessNodeId).PrivateState, Json);
                var producerObservation = new CalledResumeCut { RefuseDelivery = true };
                await using (var producer = await StartAsync(mode, producerObservation, "split-terminal-producer"))
                {
                    await ResumeCalledWaitAsync(producer, childId, ChildWaitNodeId, "called-continue");
                    for (var attempt = 0; attempt < 8 && producerObservation.LastIntent is null; attempt++)
                        await producer.SweepAsync();
                    var intent = Assert.IsType<RuntimePostCommitIntent>(producerObservation.LastIntent);
                    terminal = ReadControl(intent);
                    Assert.Equal(1, terminal.Batch.Ordinal);
                    Assert.True(terminal.Batch.IsTerminal);
                    Assert.Empty(terminal.Batch.Notifications);
                    Assert.Null(terminal.Batch.AuthoredFailureCode);
                    Assert.Equal(earlier.DispatchId, terminal.DispatchId);
                    Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, (await SourceAsync(producer, terminal.SourceOutboxItemId)).Status);
                    childFacts = await GapChildFactsAsync(producer, childId, pin, mode, ordinaryFault);
                    Assert.Empty(producerObservation.ControlCalls);
                }
                _clock.Advance(TimeSpan.FromSeconds(2));
                terminalDelivery = terminalScope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                    .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, terminal.ParentWorkflowExecutionId, DispatchWorkflowConstants.ResumeParentIntentKind), deliveryCancellation.Token).AsTask();
                Assert.Equal(terminal.SourceIntentId, (await terminalCut.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30))).IntentId);
                var heldTerminal = await SourceAsync(owner, terminal.SourceOutboxItemId);
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, heldTerminal.Status);
                Assert.NotEqual(heldEarlier.DeliveringOwnerId, heldTerminal.DeliveringOwnerId);
                Assert.False(string.IsNullOrWhiteSpace(heldTerminal.DeliveringOwnerId));
                Assert.True(heldTerminal.DeliveryFencingToken > 0);
                Assert.True(heldTerminal.DeliveryVisibleAfter > _clock.GetUtcNow());
                Assert.Equal(JsonSerializer.Serialize(heldEarlier, Json), JsonSerializer.Serialize(await SourceAsync(peer, earlier.SourceOutboxItemId), Json));
                Assert.True(heldEarlier.DeliveryVisibleAfter > _clock.GetUtcNow());
                terminalCut.Release.TrySetResult();
                Assert.Equal(1, (await terminalDelivery.WaitAsync(TimeSpan.FromSeconds(30))).DeliveredCount);
                var buffered = await peer.ReadRunAsync(ParentExecutionId);
                AssertBufferedTerminal(buffered, terminal);
                Assert.Equal(ActivityExecutionStatus.Completed, buffered.State(DispatchNodeId).Status);
                Assert.Null(buffered.State(DispatchNodeId).Fault);
                buffered.AssertOutcomes(DispatchNodeId, ordinaryFault ? "Faulted" : "Completed");
                Assert.Equal(WorkflowExecutionStatus.Running, buffered.WorkflowState!.Status);
                Assert.Equal(semanticBeforeTerminal, JsonSerializer.Serialize(buffered.State(F.ProcessNodeId).PrivateState, Json));
                var receipt = buffered.State(DispatchNodeId).Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId];
                Assert.False(string.IsNullOrWhiteSpace(receipt));
                Assert.Empty(earlierObservation.ControlCalls);
                Assert.Empty(terminalObservation.ControlCalls);
                Assert.False(earlierDelivery.IsCompleted);
                terminalRow = JsonSerializer.Serialize(await SourceAsync(peer, terminal.SourceOutboxItemId), Json);
                Assert.Equal(JsonSerializer.Serialize(heldEarlier, Json), JsonSerializer.Serialize(await SourceAsync(peer, earlier.SourceOutboxItemId), Json));
                earlierCut.Release.TrySetResult();
                var released = await earlierDelivery.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(1, released.DeliveredCount);
                Assert.Equal(0, released.FailedCount);
                Assert.Equal(0, released.SupersededCount);
                var actual = Assert.Single(earlierObservation.ControlCalls.Concat(terminalObservation.ControlCalls));
                Assert.Equal(earlier.CallingActivityExecutionId, actual.CallingActivityExecutionId);
                Assert.Equal([0L, 1L], actual.Ordinals);
                var consumed = await peer.ReadRunAsync(ParentExecutionId);
                var inbox = Assert.IsType<RuntimeChildControlInbox>(consumed.State(DispatchNodeId).ChildControlInbox);
                Assert.True(inbox.IsTerminalConsumed);
                Assert.Equal(2, inbox.NextOrdinal);
                Assert.Empty(inbox.Pending);
                Assert.True(inbox.HasAccepted(earlier));
                Assert.True(inbox.HasAccepted(terminal));
                Assert.Equal(receipt, consumed.State(DispatchNodeId).Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId]);
                Assert.Equal(ordinaryFault ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Running, consumed.WorkflowState!.Status);
                Assert.Equal(childFacts, await GapChildFactsAsync(peer, childId, pin, mode, ordinaryFault));
                stable = await EscalationFactsAsync(peer, childId, pin, mode);
                earlierRow = JsonSerializer.Serialize(await SourceAsync(peer, earlier.SourceOutboxItemId), Json);
                Assert.Equal(terminalRow, JsonSerializer.Serialize(await SourceAsync(peer, terminal.SourceOutboxItemId), Json));
            }
            catch { assertionFailed = true; throw; }
            finally
            {
                earlierCut.Release.TrySetResult();
                terminalCut.Release.TrySetResult();
                try { await Task.WhenAll(terminalDelivery is null ? [earlierDelivery] : new[] { earlierDelivery, terminalDelivery }); }
                catch when (assertionFailed) { /* Observe both tasks without replacing the assertion. */ }
            }
        }
        await using (var final = await StartAsync(mode, new CalledResumeCut(), "split-final"))
        {
            Assert.Equal(stable, await EscalationFactsAsync(final, childId, pin, mode));
            Assert.Equal(earlierRow, JsonSerializer.Serialize(await SourceAsync(final, earlier.SourceOutboxItemId), Json));
            Assert.Equal(terminalRow, JsonSerializer.Serialize(await SourceAsync(final, terminal.SourceOutboxItemId), Json));
            if (!ordinaryFault)
            {
                await ResumeCalledWaitAsync(final, ParentExecutionId, WarningNodeId, "warning-handler-continue");
                await DrainCalledAsync(final);
            }
            var run = await final.ReadRunAsync(ParentExecutionId);
            Assert.Equal(ordinaryFault ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed, run.WorkflowState!.Status);
            Assert.Equal(WorkflowExecutionHarness.Identity, run.WorkflowState.PinnedExecutable);
            Assert.Equal(mode.ToString(), run.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            Assert.Equal(childFacts, await GapChildFactsAsync(final, childId, pin, mode, ordinaryFault));
        }
    }

    private sealed class CalledClaimCut(bool afterEffect, string intentKind = NotifyControlIntentKind)
    {
        public bool AfterEffect { get; } = afterEffect;
        public string IntentKind { get; } = intentKind;
        public TaskCompletionSource<RuntimePostCommitIntent> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CalledClaimCutDispatcher(IRuntimePostCommitIntentDispatcher inner, CalledClaimCut cut)
        : IRuntimePostCommitIntentDispatcher
    {
        public bool RequiresDurableSource(string kind) => inner.RequiresDurableSource(kind);
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            DispatchAsync(intent, () => inner.DispatchAsync(intent, cancellationToken), cancellationToken);
        public ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default) =>
            DispatchAsync(source.Intent, () => inner.DispatchAsync(source, cancellationToken), cancellationToken);
        private async ValueTask DispatchAsync(RuntimePostCommitIntent intent, Func<ValueTask> dispatch, CancellationToken cancellationToken)
        {
            if (intent.Kind != cut.IntentKind) { await dispatch(); return; }
            if (cut.AfterEffect) await dispatch();
            cut.Entered.TrySetResult(intent);
            await cut.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            if (!cut.AfterEffect) await dispatch();
        }
    }
}
