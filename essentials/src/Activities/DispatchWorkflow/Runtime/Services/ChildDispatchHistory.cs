using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Diagnostic transport from an exact physically committed native child source.</summary>
internal static class ChildDispatchHistory
{
    internal static bool CanRecord(IServiceProvider? services, TimeProvider clock,
        IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader)
    {
        if (services is null) return false;
        _ = services.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope();
        return ReferenceEquals(clock, services.GetService<TimeProvider>()) &&
            ReferenceEquals(writer, services.GetService<IRuntimeHistoryObservationWriter>()) &&
            ReferenceEquals(reader, services.GetService<IRuntimeHistoryStore>()) && writer.CanRecordHistory(reader) &&
            services.GetService<IPostCommitOutboxLookupStore>()?.CanInspectHistorySources(reader) == true &&
            services.GetService<IWorkflowDispatchStore>() is not null && services.GetService<IWorkflowExecutionStateStore>() is not null;
    }

    internal static async ValueTask<RuntimeHistoryCapturePolicy> VerifyAsync(IServiceProvider? services,
        TimeProvider clock, PostCommitDeliveryContext supplied, CancellationToken token)
    {
        try
        {
            if (services is null) throw new NotSupportedException("The native child history owner is unavailable.");
            var access = services.GetRequiredService<IPersistenceAccessContextAccessor>();
            var context = access.Current;
            if (context.RequireScope() != supplied.Scope) throw new InvalidDataException("Child source scope changed.");
            var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
            var current = await lookup.FindDurableAsync(supplied.SourceOutboxItemId, token)
                ?? throw new InvalidDataException("The committed child source is missing.");
            var cause = current.Intent.HistoryCausation ?? throw new InvalidDataException("The native child source has no retained origin.");
            if (current.SourceKind != RuntimeDeliverySourceKind.WorkflowCheckpoint || current.OutboxItemId != supplied.SourceOutboxItemId ||
                RuntimeCheckpointCommitFingerprint.ComputeIntent(current.Intent) != supplied.IntentFingerprint || current.StimulusDeliverySourceScope != supplied.Scope)
                throw new InvalidDataException("The retained child source differs from its supplied identity.");
            if (current.Status != RuntimePostCommitOutboxStatus.Delivering || current.DeliveringOwnerId != supplied.Claim.OwnerId ||
                current.DeliveryFencingToken != supplied.Claim.FencingToken || current.DeliveryStartedAt != supplied.Claim.ClaimedAt ||
                current.DeliveryVisibleAfter is not { } deadline || deadline <= clock.GetUtcNow())
                throw new RuntimePostCommitOutboxStaleClaimException(current.OutboxItemId, supplied.Claim.OwnerId, supplied.Claim.FencingToken,
                    current.DeliveringOwnerId, current.DeliveryFencingToken, current.Status);
            if (cause.DeliveryId != current.OutboxItemId || string.IsNullOrWhiteSpace(cause.HistoryEventId) || string.IsNullOrWhiteSpace(cause.WorkflowExecutionId))
                throw new InvalidDataException("Child source causation differs from its committed responsibility.");
            var executions = services.GetRequiredService<IWorkflowExecutionStateStore>();
            var publisher = await executions.FindAsync(current.Intent.WorkflowExecutionId, token)
                ?? throw new InvalidDataException("The native child source target is missing.");
            if (!publisher.HasRetainedSourceScope(supplied.Scope)) context.EnsureTenantScope(publisher.TenantId);
            if (publisher.Partition is { } partition && partition.Value != supplied.Scope.Value)
                throw new InvalidDataException("The native child source target belongs to a different scope.");
            var identity = new WorkflowDispatchIdentity(current.Intent.WorkflowExecutionId,
                current.Intent.ActivityExecutionId ?? throw new InvalidDataException("Child source has no calling occurrence."));
            var dispatch = await services.GetRequiredService<IWorkflowDispatchStore>().FindAsync(identity.DispatchId, token)
                ?? throw new InvalidDataException("The native child dispatch edge is missing.");
            var start = current.Intent.Kind == WorkflowDispatchLifecycle.StartChildIntentKind && current.Intent.IntentId == identity.StartIntentId;
            var settlement = current.Intent.Kind == WorkflowDispatchLifecycle.ResumeParentIntentKind && current.Intent.IntentId == identity.ParentResumeIntentId &&
                dispatch.Mode == WorkflowDispatchMode.WaitForCompletion && WorkflowDispatchLifecycle.IsTerminal(dispatch.Status);
            if ((!start && !settlement) || dispatch.ParentWorkflowExecutionId != current.Intent.WorkflowExecutionId ||
                dispatch.ParentActivityExecutionId != current.Intent.ActivityExecutionId ||
                cause.WorkflowExecutionId != (start ? dispatch.ParentWorkflowExecutionId : dispatch.ChildWorkflowExecutionId))
                throw new InvalidDataException("Child history source does not match its admitted native edge.");
            var origin = start ? publisher : await executions.FindAsync(dispatch.ChildWorkflowExecutionId, token);
            if (origin?.HistoryCapturePolicy is not { PolicyId: "runtime-history-v5", Version: 5 } policy ||
                settlement && !origin.Status.IsTerminal()) throw new InvalidDataException("The native child origin has no qualified retained policy or terminal state.");
            if (access.Current != context || context.RequireScope() != supplied.Scope)
                throw new InvalidDataException("Persistence access changed during child source verification.");
            var reader = services.GetRequiredService<IRuntimeHistoryStore>();
            var writer = services.GetRequiredService<IRuntimeHistoryObservationWriter>();
            if (!CanRecord(services, clock, writer, reader)) throw new NotSupportedException("The native child source/history pair is unavailable.");
            var recorded = await reader.FindEventAsync(cause.WorkflowExecutionId, cause.HistoryEventId, token);
            if (recorded is null || recorded.Authority != RuntimeHistoryAuthority.CommittedTransition ||
                recorded.Name != (start ? "ChildWorkflowDispatchRequested" : "ChildWorkflowSettled") ||
                recorded.Metadata.GetValueOrDefault("history.sourceDeliveryId") != current.OutboxItemId ||
                recorded.Metadata.GetValueOrDefault("history.relatedWorkflowExecutionId") != (start ? dispatch.ChildWorkflowExecutionId : dispatch.ParentWorkflowExecutionId))
                throw new InvalidDataException("The physical child source disagrees with its committed history origin.");
            // Native edge/origin reads can outlive or lose the source claim. Reopen the physical
            // row after every other awaited read, then check trusted time before recipient handoff.
            var final = await lookup.FindDurableAsync(supplied.SourceOutboxItemId, token)
                ?? throw new InvalidDataException("The child source disappeared during verification.");
            if (final.SourceKind != RuntimeDeliverySourceKind.WorkflowCheckpoint || final.OutboxItemId != supplied.SourceOutboxItemId ||
                RuntimeCheckpointCommitFingerprint.ComputeIntent(final.Intent) != supplied.IntentFingerprint ||
                final.StimulusDeliverySourceScope != supplied.Scope)
                throw new InvalidDataException("The native child source changed during verification.");
            if (final.Status != RuntimePostCommitOutboxStatus.Delivering || final.DeliveringOwnerId != supplied.Claim.OwnerId ||
                final.DeliveryFencingToken != supplied.Claim.FencingToken || final.DeliveryStartedAt != supplied.Claim.ClaimedAt ||
                final.DeliveryVisibleAfter is not { } finalDeadline || finalDeadline <= clock.GetUtcNow())
                throw new RuntimePostCommitOutboxStaleClaimException(final.OutboxItemId, supplied.Claim.OwnerId, supplied.Claim.FencingToken,
                    final.DeliveringOwnerId, final.DeliveryFencingToken, final.Status);
            if (access.Current != context || context.RequireScope() != supplied.Scope)
                throw new InvalidDataException("Persistence access changed during child origin verification.");
            return policy;
        }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception failure) { throw Boundary(supplied, failure); }
    }

    internal static async ValueTask ObserveAsync(IServiceProvider services, TimeProvider clock, PostCommitDeliveryContext source,
        RuntimeHistoryCapturePolicy policy, string receiptId, bool disposition, string reason, CancellationToken token)
    {
        try
        {
            var access = services.GetRequiredService<IPersistenceAccessContextAccessor>();
            var context = access.Current;
            if (context.RequireScope() != source.Scope) throw new InvalidDataException("Child observation scope changed.");
            var name = disposition ? "ChildStartDispositionObserved" : "ChildStartAttemptObserved";
            string Id(string kind) => "child-source-history:" + RuntimeHistoryIdentity.CreateBinding([source.Intent.WorkflowExecutionId, receiptId, kind]);
            var cause = source.Intent.HistoryCausation!;
            var e = new RuntimeHistoryEvent(Id(name), source.Intent.WorkflowExecutionId, 0, RuntimeHistoryEventCategory.DeliveryObservation,
                name, clock.GetUtcNow(), source.Intent.ActivityExecutionId, metadata: new Dictionary<string, string> { ["history.reasonCode"] = reason })
            {
                Authority = RuntimeHistoryAuthority.DeliveryObservation,
                Causation = disposition ? cause with { HistoryEventId = Id("ChildStartAttemptObserved") } : cause,
                AttemptId = "outbox-attempt:" + ((long)source.Claim.Item.DeliveryAttemptCount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ":fence:" + source.Claim.FencingToken.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            var observation = new RuntimeHistoryObservation(policy, e);
            RuntimeHistorySafety.ValidateObservation(observation);
            if (!(await services.GetRequiredService<IRuntimeHistoryObservationWriter>().RecordAsync(observation, token)).Recorded)
                throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind,
                    new NotSupportedException("The original child history head is unavailable."), true);
            if (access.Current != context) throw new InvalidDataException("Child observation access changed during persistence.");
        }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception failure) { throw Boundary(source, failure); }
    }
    private static RuntimePostCommitSourceBoundaryException Boundary(PostCommitDeliveryContext source, Exception failure) =>
        new(source.SourceOutboxItemId, source.Intent.Kind, failure, IsUnavailable(failure));

    private static bool IsUnavailable(Exception failure)
    {
        if (failure is RuntimePostCommitSourceBoundaryException boundary) return boundary.IsUnavailable;
        if (failure is AggregateException aggregate) return aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsUnavailable);
        if (RuntimeCheckpointCommitValidationException.IsCauseOf(failure)) return false;
        var unavailable = false;
        for (Exception? cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is InvalidDataException or JsonException or ArgumentException or RuntimeHistoryIntegrityException) return false;
            unavailable |= cause is IOException or DbException or TimeoutException or NotSupportedException or OperationCanceledException or
                RuntimeHistoryUnavailableException or RuntimeSchedulerWorkUnavailableException;
        }
        return unavailable;
    }

}
