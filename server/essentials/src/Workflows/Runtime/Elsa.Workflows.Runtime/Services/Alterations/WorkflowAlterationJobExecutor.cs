using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Executes one claimed target job inside its workflow actor. It performs every handler preflight against one staged
/// projection and makes exactly one checkpoint attempt for both successful and rejected jobs.
/// </summary>
public sealed class WorkflowAlterationJobExecutor
{
    private readonly IWorkflowAlterationHandlerResolver _handlerResolver;
    private readonly IWorkflowAlterationCheckpointWriter _checkpointWriter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkflowAlterationJobExecutor>? _logger;

    public WorkflowAlterationJobExecutor(
        IWorkflowAlterationHandlerResolver handlerResolver,
        IWorkflowAlterationCheckpointWriter checkpointWriter,
        TimeProvider? timeProvider = null,
        ILogger<WorkflowAlterationJobExecutor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(handlerResolver);
        ArgumentNullException.ThrowIfNull(checkpointWriter);

        _handlerResolver = handlerResolver;
        _checkpointWriter = checkpointWriter;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async ValueTask<WorkflowAlterationJobExecutionResult> ExecuteAsync(
        WorkflowAlterationJobExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var staging = new WorkflowAlterationStagingWorkspace();
        var preflight = await PreflightAsync(request, staging, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var terminalChange = CreateTerminalChange(request.Job, request.Envelopes, preflight, now);
        var writeRequest = new WorkflowAlterationCheckpointWriteRequest(
            request.Job,
            preflight.IsSuccessful ? staging.StagedChanges : [],
            terminalChange,
            request.ProjectedState);

        var writeResult = await CommitOrReconcileAsync(writeRequest, cancellationToken);
        var terminalJob = CreateTerminalJob(request.Job, writeResult.Result.TerminalJobChange);
        return new WorkflowAlterationJobExecutionResult(terminalJob, writeResult.ReconciledAfterAcknowledgementLoss);
    }

    private async ValueTask<PreflightRun> PreflightAsync(
        WorkflowAlterationJobExecutionRequest request,
        WorkflowAlterationStagingWorkspace staging,
        CancellationToken cancellationToken)
    {
        if (request.ProjectedState.TryGetOriginal<WorkflowExecutionState>(out var workflow) &&
            WorkflowAlterationCapturedConcurrencyValidator.ValidateAuthority(request.Job, workflow!) is { } authorityConflict)
        {
            return PreflightRun.Failed(0, authorityConflict);
        }

        for (var ordinal = 0; ordinal < request.Envelopes.Count; ordinal++)
        {
            var envelope = request.Envelopes[ordinal];
            IWorkflowAlterationPreflightHandler handler;
            try
            {
                handler = _handlerResolver.Resolve(envelope)
                    ?? throw new InvalidOperationException("The alteration handler resolver returned no handler.");
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
            {
                _logger?.LogError(
                    exception,
                    "Workflow alteration handler resolution failed for job {JobId}, kind {Kind}/{SchemaVersion}, ordinal {Ordinal}.",
                    request.Job.JobId,
                    envelope.Kind,
                    envelope.SchemaVersion,
                    ordinal);
                return PreflightRun.Failed(ordinal, new WorkflowAlterationSafeFailure("HandlerResolutionFailed", "The requested alteration handler is unavailable."));
            }

            WorkflowAlterationPreflightResult decision;
            try
            {
                decision = await handler.PreflightAsync(
                    new WorkflowAlterationPreflightContext(request.Job, envelope, ordinal, request.ProjectedState, staging),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.LogError(
                    exception,
                    "Workflow alteration handler preflight failed for job {JobId}, kind {Kind}/{SchemaVersion}, ordinal {Ordinal}.",
                    request.Job.JobId,
                    envelope.Kind,
                    envelope.SchemaVersion,
                    ordinal);
                return PreflightRun.Failed(ordinal, new WorkflowAlterationSafeFailure("HandlerPreflightFailed", "The requested alteration could not be validated."));
            }

            if (!decision.IsAccepted)
            {
                return PreflightRun.Failed(
                    ordinal,
                    decision.Failure ?? new WorkflowAlterationSafeFailure("AlterationPreflightRejected", "The requested alteration is not valid for the current execution."));
            }
        }

        return PreflightRun.Succeeded();
    }

    private WorkflowAlterationJobTerminalChange CreateTerminalChange(
        WorkflowAlterationJobState job,
        IReadOnlyList<WorkflowAlterationEnvelope> envelopes,
        PreflightRun preflight,
        DateTimeOffset recordedAt)
    {
        var status = preflight.IsSuccessful ? WorkflowAlterationJobStatus.Succeeded : WorkflowAlterationJobStatus.Failed;
        var outcomes = CreateOutcomes(envelopes, preflight, recordedAt);
        var failure = preflight.IsSuccessful
            ? null
            : new WorkflowAlterationSafeFailure("AlterationPreflightFailed", "The requested alteration is not valid for the current execution.");
        return new WorkflowAlterationJobTerminalChange(
            job.JobId,
            job.Claim!.Token,
            status,
            outcomes.ToArray(),
            WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, job.AttemptCount),
            recordedAt,
            failure);
    }

    private static IReadOnlyCollection<WorkflowAlterationOutcome> CreateOutcomes(
        IReadOnlyList<WorkflowAlterationEnvelope> envelopes,
        PreflightRun preflight,
        DateTimeOffset recordedAt)
    {
        return preflight.IsSuccessful
            ? CreateSuccessOutcomes()
            : CreateFailureOutcomes();

        IReadOnlyCollection<WorkflowAlterationOutcome> CreateSuccessOutcomes() =>
            Enumerable.Range(0, envelopes.Count)
                .Select(ordinal => NewOutcome(ordinal, WorkflowAlterationOutcomeStatus.Succeeded, "Applied", null))
                .ToArray();

        IReadOnlyCollection<WorkflowAlterationOutcome> CreateFailureOutcomes()
        {
            var failedOrdinal = preflight.FailedOrdinal!.Value;
            return Enumerable.Range(0, envelopes.Count)
                .Select(ordinal =>
                {
                    if (ordinal < failedOrdinal)
                        return NewOutcome(ordinal, WorkflowAlterationOutcomeStatus.Skipped, "NotAppliedDueToPreflightFailure", "Not applied because complete preflight did not succeed.");
                    if (ordinal == failedOrdinal)
                        return NewOutcome(ordinal, WorkflowAlterationOutcomeStatus.Failed, preflight.Failure!.Code, preflight.Failure.Message);
                    return NewOutcome(ordinal, WorkflowAlterationOutcomeStatus.Skipped, "SkippedAfterPreflightFailure", "Not applied because an earlier alteration failed preflight.");
                })
                .ToArray();
        }

        WorkflowAlterationOutcome NewOutcome(int ordinal, WorkflowAlterationOutcomeStatus status, string code, string? message)
        {
            var envelope = envelopes[ordinal];
            return new WorkflowAlterationOutcome(ordinal, envelope.Kind, envelope.SchemaVersion, status, code, message, recordedAt);
        }
    }

    private async ValueTask<CommittedWrite> CommitOrReconcileAsync(
        WorkflowAlterationCheckpointWriteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _checkpointWriter.CommitAsync(request, cancellationToken);
            ValidateWriteResult(request, result);
            return new CommittedWrite(result, false);
        }
        catch (WorkflowAlterationCheckpointAcknowledgementLostException)
        {
            var reconciled = await _checkpointWriter.FindCommittedAsync(request.TerminalJobChange.CheckpointCommitId, cancellationToken);
            if (reconciled is null)
                throw;
            ValidateWriteResult(request, reconciled);
            return new CommittedWrite(reconciled, true);
        }
    }

    private static WorkflowAlterationJobState CreateTerminalJob(
        WorkflowAlterationJobState original,
        WorkflowAlterationJobTerminalChange terminalChange) =>
        new(
            original.JobId,
            original.PlanId,
            original.WorkflowExecutionId,
            original.TenantPartition,
            original.CaptureOrdinal,
            terminalChange.Status,
            null,
            original.AttemptCount,
            terminalChange.Outcomes.ToArray(),
            terminalChange.CheckpointCommitId,
            terminalChange.SafeFailure,
            original.CreatedAt,
            original.StartedAt,
            terminalChange.CompletedAt,
            original.Revision + 1,
            original.CapturedConcurrency);

    private static void ValidateWriteResult(
        WorkflowAlterationCheckpointWriteRequest request,
        WorkflowAlterationCheckpointWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(result.CheckpointCommitId, request.TerminalJobChange.CheckpointCommitId, StringComparison.Ordinal) ||
            !string.Equals(result.TerminalJobChange.JobId, request.Job.JobId, StringComparison.Ordinal) ||
            !string.Equals(result.TerminalJobChange.ClaimToken, request.Job.Claim!.Token, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The alteration checkpoint writer returned evidence for a different job or claim.");
        }
    }

    private sealed record PreflightRun(bool IsSuccessful, int? FailedOrdinal, WorkflowAlterationSafeFailure? Failure)
    {
        public static PreflightRun Succeeded() => new(true, null, null);
        public static PreflightRun Failed(int ordinal, WorkflowAlterationSafeFailure failure) => new(false, ordinal, failure);
    }

    private sealed record CommittedWrite(WorkflowAlterationCheckpointWriteResult Result, bool ReconciledAfterAcknowledgementLoss)
    {
        public static implicit operator WorkflowAlterationCheckpointWriteResult(CommittedWrite write) => write.Result;
    }

    private sealed class WorkflowAlterationStagingWorkspace : IWorkflowAlterationStagingWorkspace
    {
        private readonly List<IWorkflowAlterationStagedChange> _changes = [];
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public IReadOnlyList<IWorkflowAlterationStagedChange> StagedChanges => _changes;

        public void Stage(IWorkflowAlterationStagedChange change)
        {
            ArgumentNullException.ThrowIfNull(change);
            ArgumentException.ThrowIfNullOrWhiteSpace(change.ChangeKey);
            if (!_keys.Add(change.ChangeKey))
                throw new InvalidOperationException($"The alteration job staged duplicate change '{change.ChangeKey}'.");
            _changes.Add(change);
        }
    }
}
