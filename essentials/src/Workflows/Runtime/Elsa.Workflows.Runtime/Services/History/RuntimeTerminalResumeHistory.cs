using System.Data.Common;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Observes an ineligible exact saved resume before the existing terminal-work purge, never executing it.</summary>
internal static class RuntimeTerminalResumeHistory
{
    internal static async ValueTask RecordAsync(RuntimeSchedulerWorkItem work, WorkflowExecutionState execution,
        IRuntimeHistoryObservationWriter? writer, IRuntimeHistoryStore? reader, IPersistenceAccessContextAccessor? access,
        DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (work.CommandKind != WorkflowExecutionCommandKind.ResumeBookmark || execution.HistoryCapturePolicy is not { } policy ||
            !policy.SupportedKinds.Contains("DeliveryUnmatched", StringComparer.Ordinal)) return;
        try
        {
            var payload = work.Payload?.Deserialize<RuntimeResumeBookmarkCommandPayload>()
                ?? throw new InvalidDataException("The terminal resume has no payload.");
            if (payload.ExpectedBookmarkResume is not { } expected) return; // Other legacy receiver paths remain separate.
            var context = access?.Current ?? throw Unavailable();
            void EnsureCurrent()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (access!.Current != context) throw Unavailable();
                context.EnsureTenantScope(execution.TenantId);
                if (!execution.Status.IsTerminal() || expected.RegistrationCapture is not { } capture ||
                    !capture.Scope.Matches(execution, context.RequireScope()) ||
                    !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(capture.Executable, execution.PinnedExecutable) ||
                    !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(payload.PinnedExecutable, execution.PinnedExecutable) ||
                    work.WorkflowExecutionId != execution.WorkflowExecutionId || expected.WorkflowExecutionId != work.WorkflowExecutionId ||
                    expected.ActivityExecutionId != payload.ActivityExecutionId || expected.BookmarkId != payload.BookmarkId ||
                    expected.ExecutableNodeId != payload.ExecutableNodeId || expected.ResumeTargetId != payload.ResumeTargetId ||
                    expected.StimulusType != payload.StimulusType || expected.StimulusHash != payload.StimulusHash)
                    throw new RuntimeHistoryIntegrityException(work.WorkflowExecutionId, "terminal saved-resume attribution disagrees with native scope or its queued recipient");
            }
            EnsureCurrent();
            if (writer is null || reader is null || !writer.CanRecordHistory(reader)) throw Unavailable();
            EnsureCurrent();
            var cause = payload.HistoryCausation ?? new RuntimeHistoryCausation();
            var observation = RuntimeHistoryDeliveryObservations.Create(policy, work.WorkflowExecutionId,
                Guid.NewGuid().ToString("N"), "DeliveryUnmatched", observedAt, payload.StimulusType, payload.BookmarkId,
                payload.ActivityExecutionId, payload.ExecutableNodeId,
                cause with { CommandId = work.CommandId, SchedulerWorkItemId = work.WorkItemId }, "terminal-resume-ineligible");
            var result = await writer.RecordAsync(observation, cancellationToken);
            EnsureCurrent();
            if (!result.Recorded) throw Unavailable();
        }
        catch (Exception failure) when (cancellationToken.IsCancellationRequested && IsUnavailable(failure) &&
            RequestedCancellation(failure, cancellationToken) is { } cancellation)
        { ExceptionDispatchInfo.Capture(cancellation).Throw(); throw; }
        catch (RuntimeSchedulerWorkUnavailableException) { throw; }
        catch (RuntimeHistoryIntegrityException) { throw; }
        catch (Exception failure) when (IsUnavailable(failure))
        { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Terminal saved-resume history is unavailable; its source work remains owed.", failure); }
        catch (Exception failure) when (failure is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { throw new RuntimeHistoryIntegrityException(work.WorkflowExecutionId, "terminal saved-resume history could not retain its classified observation", failure); }
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable() => new("runtime.history.capture-unavailable",
        "Terminal saved-resume work cannot be purged before its retained observation is acknowledged.");
    private static OperationCanceledException? RequestedCancellation(Exception failure, CancellationToken token)
    {
        for (Exception? cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is AggregateException { InnerExceptions.Count: > 1 }) return null;
            if (cause is OperationCanceledException cancellation && cancellation.CancellationToken == token) return cancellation;
        }
        return null;
    }
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
