using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Records an actual receiver decision before acknowledging an ineligible saved resume; never consumes a wait.</summary>
internal static class ActivityResumeHistoryObservations
{
    internal static async ValueTask UnmatchedAsync(IServiceProvider services, RuntimeSchedulerWorkItem work,
        RuntimeResumeBookmarkCommandPayload payload, WorkflowExecutionState execution, PersistenceAccessContext context,
        DateTimeOffset observedAt, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (execution.HistoryCapturePolicy is not { } original || !original.SupportedKinds.Contains("DeliveryUnmatched", StringComparer.Ordinal)) return;
        try
        {
            if (reason is not ("receiver-bookmark-missing" or "receiver-bookmark-expired" or "receiver-wait-no-longer-eligible"))
                throw new ArgumentOutOfRangeException(nameof(reason));
            var access = services.GetService<IPersistenceAccessContextAccessor>() ?? throw Unavailable();
            void EnsureCurrent()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (access.Current != context) throw Unavailable();
                _ = context.RequireScope(); context.EnsureTenantScope(execution.TenantId);
            }
            EnsureCurrent();
            var policy = await ActivityHistoryPreflight.ReadPolicyAndValidateAsync(services, work.WorkflowExecutionId, cancellationToken);
            EnsureCurrent();
            if (policy is null)
            {
                if (original.Required) throw Unavailable();
                return;
            }
            if (!original.IsEquivalentTo(policy))
                throw new RuntimeHistoryIntegrityException(work.WorkflowExecutionId, "the receiver's original capture policy changed before its observation");
            var writer = services.GetService<IRuntimeHistoryObservationWriter>();
            var reader = services.GetService<IRuntimeHistoryStore>();
            if (writer is null || reader is null || !writer.CanRecordHistory(reader)) throw Unavailable();
            EnsureCurrent();
            var cause = payload.HistoryCausation ?? new RuntimeHistoryCausation();
            // One identity per actual handler invocation. A deferred re-entry is another receiver observation,
            // not another business occurrence. An uncertain write never authorizes acknowledgement or consumption.
            var e = new RuntimeHistoryEvent("delivery-history:" + RuntimeHistoryIdentity.CreateBinding(
                    [work.WorkflowExecutionId, Guid.NewGuid().ToString("N"), "DeliveryUnmatched"]),
                work.WorkflowExecutionId, 0, RuntimeHistoryEventCategory.DeliveryObservation, "DeliveryUnmatched", observedAt,
                payload.ActivityExecutionId, payload.ExecutableNodeId, metadata: new Dictionary<string, string>
                { ["history.stimulusType"] = payload.StimulusType, ["history.reasonCode"] = reason })
            {
                Authority = RuntimeHistoryAuthority.DeliveryObservation, BookmarkId = payload.BookmarkId,
                Causation = cause with { CommandId = work.CommandId, SchedulerWorkItemId = work.WorkItemId }
            };
            var observation = new RuntimeHistoryObservation(policy, e);
            RuntimeHistorySafety.ValidateObservation(observation);
            var recorded = await writer.RecordAsync(observation, cancellationToken);
            EnsureCurrent();
            if (!recorded.Recorded) throw Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RuntimeSchedulerWorkUnavailableException) { throw; }
        catch (RuntimeHistoryIntegrityException) { throw; }
        catch (Exception failure) when (IsUnavailable(failure))
        { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The saved resume's receiver observation is unavailable.", failure); }
        catch (Exception failure) when (failure is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { throw new RuntimeHistoryIntegrityException(work.WorkflowExecutionId, "the receiver could not retain its classified saved-resume observation", failure); }
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable() => new("runtime.history.capture-unavailable",
        "The saved resume cannot acknowledge its retained receiver history.");
    private static bool IsUnavailable(Exception failure)
    {
        if (IsHard(failure)) return false;
        if (failure is AggregateException aggregate) return aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsUnavailable);
        if (failure is RuntimePostCommitSourceBoundaryException source) return source.IsUnavailable;
        if (failure is StimulusDeliveryBoundaryException delivery) return delivery.IsUnavailable;
        return failure is IOException or DbException or TimeoutException or NotSupportedException or OperationCanceledException or RuntimeHistoryUnavailableException ||
            failure.InnerException is { } cause && IsUnavailable(cause);
    }
    private static bool IsHard(Exception failure) => failure is ArgumentException or InvalidDataException or JsonException or RuntimeHistoryIntegrityException or
        RuntimePostCommitSourceBoundaryException { IsUnavailable: false } or StimulusDeliveryBoundaryException { IsUnavailable: false } ||
        RuntimeCheckpointCommitValidationException.IsCauseOf(failure) ||
        failure is AggregateException aggregate && aggregate.InnerExceptions.Any(IsHard) || failure.InnerException is { } cause && IsHard(cause);
}
