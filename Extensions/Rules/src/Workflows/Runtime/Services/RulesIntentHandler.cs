using System.Text.Json;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Rules.Workflows.Runtime.Services;

/// <summary>Delivers the checkpoint-owned admission, cancellation, and invalid-input responsibilities for native Rules activities.</summary>
internal sealed class RulesIntentHandler : IRuntimePostCommitIntentHandler
{
    private readonly IRuleRunStore _runs;
    private readonly IRuleRunReader _reader;
    private readonly IWorkflowExecutionStateStore _workflows;
    private readonly IActivityExecutionStateStore _activities;
    private readonly IBookmarkStateStore _bookmarks;
    private readonly IPersistenceAccessContextAccessor _access;
    private readonly IBookmarkResumeDispatcher _resumeDispatcher;

    public RulesIntentHandler(
        IRuleRunStore runs,
        IRuleRunReader reader,
        IWorkflowExecutionStateStore workflows,
        IActivityExecutionStateStore activities,
        IBookmarkStateStore bookmarks,
        IPersistenceAccessContextAccessor access,
        IBookmarkResumeDispatcher resumeDispatcher)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(bookmarks);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(resumeDispatcher);

        _runs = runs;
        _reader = reader;
        _workflows = workflows;
        _activities = activities;
        _bookmarks = bookmarks;
        _access = access;
        _resumeDispatcher = resumeDispatcher;
    }

    public async ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var (request, address, cancel) = ReadIntent(intent);
            EnsureScope(request, cancellationToken);

            var invocationIdentity = new RuleRunIdentity(request.Scope, request.RunId);
            var retained = await _reader.FindAsync(invocationIdentity, cancellationToken);
            if (retained.Disposition == RuleRunReadDisposition.Unavailable)
                throw Transient("rules.intent-read-unavailable");
            if (retained.Disposition is not RuleRunReadDisposition.Found and not RuleRunReadDisposition.NotFound)
                throw Permanent("rules.intent-read-unknown-disposition");

            var invalidBudget = RulesNativeBoundary.HasInvalidRoundLimit(request);
            if (retained.Disposition == RuleRunReadDisposition.Found)
            {
                if (invalidBudget)
                    throw Permanent("rules.intent-invalid-budget-has-run");

                var invocation = CreateInvocation(request, address);
                if (!Matches(retained.View!, invocation))
                    throw Permanent("rules.intent-fingerprint-conflict");

                // A matching admission is itself durable proof for Start, even after the native wait has
                // been consumed and its bookmark or trigger registration has been cleaned up.
                if (!cancel)
                    return;

                await CancelAsync(invocation, invocationIdentity, cancellationToken);
                return;
            }

            if (invalidBudget)
            {
                if (cancel)
                {
                    var workflow = await ReadWorkflowAsync(request, cancellationToken);
                    if (workflow.Status.IsTerminal())
                    {
                        await EnsureNoRuleRunAsync(invocationIdentity, cancellationToken);
                        return;
                    }

                    var cancelledActivity = await ReadActivityAsync(request, address, cancellationToken);
                    if (cancelledActivity.Status == ActivityExecutionStatus.Cancelled)
                    {
                        await EnsureNoRuleRunAsync(invocationIdentity, cancellationToken);
                        return;
                    }

                    throw Transient("rules.intent-cancellation-not-visible");
                }

                await DispatchInvalidRoundLimitAsync(request, address, cancellationToken);
                return;
            }

            var parent = await ReadWorkflowAsync(request, cancellationToken);
            if (parent.Status.IsTerminal())
            {
                await CancelAsync(CreateInvocation(request, address), invocationIdentity, cancellationToken);
                return;
            }

            var activity = await ReadActivityAsync(request, address, cancellationToken);
            var nativeTerminal = activity.Status is
                ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or
                ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Superseded;

            if (cancel)
            {
                // This intent is emitted by the exact checkpoint-owned route already validated above.
                // Cancellation can race activity execution, so the Rules tombstone must not wait for
                // the native activity to become terminal before it can win against a later Start.
                await CancelAsync(CreateInvocation(request, address), invocationIdentity, cancellationToken);
                return;
            }

            if (nativeTerminal)
            {
                // If native execution already ended, admission can no longer own a continuation. A
                // tombstone prevents a concurrently retried Start from creating executable work.
                await CancelAsync(CreateInvocation(request, address), invocationIdentity, cancellationToken);
                return;
            }

            if (activity.Status != ActivityExecutionStatus.Suspended)
                throw Transient("rules.intent-wait-not-ready");

            await RequireBookmarkAsync(request, address, activity, cancellationToken);
            await AdmitAsync(CreateInvocation(request, address), invocationIdentity, cancellationToken);
        }
        catch (RuntimePostCommitDeliveryException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Store and runtime providers can fail after the commit. Keep the outbox responsibility
            // retryable and never expose provider diagnostics through its safe failure summary.
            throw Transient("rules.intent-delivery-unavailable", exception);
        }
    }

    private static (RunRulesRequest Request, RuleNativeResultAddress Address, bool Cancel) ReadIntent(RuntimePostCommitIntent intent)
    {
        try
        {
            if (intent.Payload is not { } payload)
                throw RulesNativeBoundary.Invalid();

            var cancel = intent.Kind switch
            {
                RulesNativeBoundary.StartKind => false,
                RulesNativeBoundary.CancelKind => true,
                _ => throw RulesNativeBoundary.Invalid()
            };
            var native = RulesNativeBoundary.Read<RulesNativeIntent>(payload);
            if (native.FormatVersion != RulesNativeBoundary.FormatVersion)
                throw RulesNativeBoundary.Invalid();

            var validatedRequest = RulesNativeBoundary.ReadState(new RunRulesState(
                RulesNativeBoundary.FormatVersion,
                RulesNativeBoundary.Write(native.Request)));
            if (!JsonElement.DeepEquals(
                    RulesNativeBoundary.Write(validatedRequest),
                    RulesNativeBoundary.Write(native.Request)))
                throw RulesNativeBoundary.Invalid();

            RulesNativeBoundary.EnsureAddress(native.Request, native.Destination, native.Destination.RegistrationId);
            var canonical = RulesNativeBoundary.Intent(native.Request, native.Destination, cancel, intent.RecordedAt);
            if (!intent.IsEquivalentTo(canonical))
                throw RulesNativeBoundary.Invalid();

            return (native.Request, native.Destination, cancel);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Permanent("rules.intent-invalid", exception);
        }
    }

    private void EnsureScope(RunRulesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var scope = _access.Current.RequireScope();
            if (!StringComparer.Ordinal.Equals(scope.Value, request.Scope.PersistencePartition))
                throw new InvalidDataException("The native Rules intent does not match the selected persistence scope.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Permanent("rules.intent-scope-mismatch", exception);
        }
    }

    private async ValueTask DispatchInvalidRoundLimitAsync(
        RunRulesRequest request,
        RuleNativeResultAddress address,
        CancellationToken cancellationToken)
    {
        var parent = await ReadWorkflowAsync(request, cancellationToken);
        if (parent.Status.IsTerminal())
        {
            await EnsureNoRuleRunAsync(new RuleRunIdentity(request.Scope, request.RunId), cancellationToken);
            return;
        }

        var activity = await ReadActivityAsync(request, address, cancellationToken);
        if (HasInvalidRoundLimitProof(activity, request, address))
        {
            await EnsureNoRuleRunAsync(new RuleRunIdentity(request.Scope, request.RunId), cancellationToken);
            return;
        }

        if (activity.Status != ActivityExecutionStatus.Suspended)
            throw Transient("rules.intent-invalid-budget-wait-not-ready");

        var bookmark = await RequireBookmarkAsync(request, address, activity, cancellationToken);
        var completion = new RunRulesCompletion(
            RulesNativeBoundary.FormatVersion,
            address,
            RulesNativeBoundary.InvalidDeliveryId(request, address),
            Result: null,
            InvalidCode: RulesNativeBoundary.InvalidRoundLimit);
        var dispatch = new BookmarkResumeDispatchRequest(
            request.WorkflowExecutionId,
            RulesNativeBoundary.StimulusType,
            RulesNativeBoundary.StimulusHash(request),
            // The typed activity state deserializer uses the default System.Text.Json contract.
            input: JsonSerializer.SerializeToElement(completion),
            idempotencyKey: completion.DeliveryId,
            requestedBy: "rules.native-intent",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["rules.intentId"] = $"rules-start:{request.RunId}"
            },
            payloadType: RulesNativeBoundary.CompletionType,
            providerId: RulesNativeBoundary.ProviderId,
            bookmarkId: bookmark.BookmarkId,
            testRunDelivery: null);

        // Duplicate/Dispatched describe mailbox handling, not durable activity consumption. Always read
        // the committed native outcome again before retiring this intent.
        _ = await _resumeDispatcher.DispatchAsync(dispatch, cancellationToken: cancellationToken);

        var afterParent = await ReadWorkflowAsync(request, cancellationToken);
        if (afterParent.Status.IsTerminal())
        {
            await EnsureNoRuleRunAsync(new RuleRunIdentity(request.Scope, request.RunId), cancellationToken);
            return;
        }

        var afterActivity = await ReadActivityAsync(request, address, cancellationToken);
        if (HasInvalidRoundLimitProof(afterActivity, request, address))
        {
            await EnsureNoRuleRunAsync(new RuleRunIdentity(request.Scope, request.RunId), cancellationToken);
            return;
        }

        var runAfterDispatch = await _reader.FindAsync(new RuleRunIdentity(request.Scope, request.RunId), cancellationToken);
        if (runAfterDispatch.Disposition == RuleRunReadDisposition.Unavailable)
            throw Transient("rules.intent-read-unavailable");
        if (runAfterDispatch.Disposition == RuleRunReadDisposition.Found)
            throw Permanent("rules.intent-invalid-budget-has-run");

        throw Transient("rules.intent-invalid-budget-outcome-unproven");
    }

    private async ValueTask<ActivityExecutionState> ReadActivityAsync(
        RunRulesRequest request,
        RuleNativeResultAddress address,
        CancellationToken cancellationToken)
    {
        var activity = await _activities.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        if (activity is null)
            throw Transient("rules.intent-activity-not-found");

        try
        {
            var retainedRequest = RulesNativeBoundary.ReadState(activity);
            if (!JsonElement.DeepEquals(RulesNativeBoundary.Write(retainedRequest), RulesNativeBoundary.Write(request)) ||
                RulesNativeBoundary.ReadAddress(activity) != address)
                throw RulesNativeBoundary.Invalid();
        }
        catch (Exception exception)
        {
            throw Permanent("rules.intent-native-route-mismatch", exception);
        }

        return activity;
    }

    private async ValueTask<WorkflowExecutionState> ReadWorkflowAsync(
        RunRulesRequest request,
        CancellationToken cancellationToken)
    {
        var workflow = await _workflows.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (workflow is null)
            throw Transient("rules.intent-workflow-not-found");

        if (workflow.WorkflowExecutionId != request.WorkflowExecutionId ||
            workflow.TenantId != request.Scope.TenantId ||
            workflow.Partition?.Value != request.Scope.PersistencePartition ||
            workflow.PinnedExecutable.ArtifactId != request.ExecutableId ||
            workflow.PinnedExecutable.ArtifactHash != request.ExecutableHash)
            throw Permanent("rules.intent-parent-identity-mismatch");

        return workflow;
    }

    private async ValueTask EnsureNoRuleRunAsync(RuleRunIdentity identity, CancellationToken cancellationToken)
    {
        var read = await _reader.FindAsync(identity, cancellationToken);
        if (read.Disposition == RuleRunReadDisposition.Unavailable)
            throw Transient("rules.intent-read-unavailable");
        if (read.Disposition == RuleRunReadDisposition.Found)
            throw Permanent("rules.intent-invalid-budget-has-run");
        if (read.Disposition != RuleRunReadDisposition.NotFound)
            throw Permanent("rules.intent-read-unknown-disposition");
    }

    private async ValueTask<BookmarkState> RequireBookmarkAsync(
        RunRulesRequest request,
        RuleNativeResultAddress address,
        ActivityExecutionState activity,
        CancellationToken cancellationToken)
    {
        try
        {
            if (activity.BookmarkIds.Count != 1 || !activity.BookmarkIds.Contains(address.BookmarkId, StringComparer.Ordinal) ||
                activity.TriggerRegistrations is not { Count: 1 } registrations)
                throw RulesNativeBoundary.Invalid();

            var registration = registrations.Single();
            if (registration.RegistrationId != address.RegistrationId ||
                registration.InvocationId != request.ActivityExecutionId ||
                registration.ResumeTargetKey != address.ResumeTargetId ||
                registration.StimulusType != address.StimulusType ||
                registration.StimulusHash != address.StimulusHash ||
                registration.PayloadType != RulesNativeBoundary.CompletionType ||
                registration.DeduplicationPolicy != ActivityTriggerDeduplicationPolicy.Once)
                throw RulesNativeBoundary.Invalid();
        }
        catch (Exception exception)
        {
            throw Permanent("rules.intent-bookmark-registration-mismatch", exception);
        }

        var bookmark = await _bookmarks.FindAsync(request.WorkflowExecutionId, address.BookmarkId, cancellationToken);
        if (bookmark is null)
            throw Transient("rules.intent-bookmark-not-found");

        try
        {
            RulesNativeBoundary.EnsureBookmark(address, bookmark);
        }
        catch (Exception exception)
        {
            throw Permanent("rules.intent-bookmark-mismatch", exception);
        }

        return bookmark;
    }

    private static RuleInvocationEnvelope CreateInvocation(RunRulesRequest request, RuleNativeResultAddress address)
    {
        try
        {
            return RulesNativeBoundary.Invocation(request, address);
        }
        catch (Exception exception)
        {
            throw Permanent("rules.intent-invocation-invalid", exception);
        }
    }

    private static bool Matches(RuleRunView view, RuleInvocationEnvelope invocation) =>
        view.Identity == invocation.Identity &&
        view.Fingerprint == RuleRecordCodec.Fingerprint(invocation) &&
        view.ArtifactId == invocation.Artifact.ArtifactId &&
        view.ArtifactSha256 == invocation.Artifact.Sha256 &&
        view.ImplementationPinHash == RuleRecordCodec.PinHash(invocation.ExpectedImplementation);

    private async ValueTask AdmitAsync(
        RuleInvocationEnvelope invocation,
        RuleRunIdentity identity,
        CancellationToken cancellationToken)
    {
        var result = await _runs.AdmitAsync(new(invocation), cancellationToken);
        if (result.Identity != identity)
            throw Permanent("rules.intent-admission-identity-mismatch");

        switch (result.Disposition)
        {
            case RuleAdmissionDisposition.Admitted:
            case RuleAdmissionDisposition.Replay:
            case RuleAdmissionDisposition.AlreadyCancelled:
            case RuleAdmissionDisposition.AlreadyRefused:
            case RuleAdmissionDisposition.Refused:
                return;
            case RuleAdmissionDisposition.Unavailable:
                throw Transient("rules.intent-admission-unavailable");
            case RuleAdmissionDisposition.Conflict:
            case RuleAdmissionDisposition.Invalid:
                throw Permanent("rules.intent-admission-rejected");
            default:
                throw Permanent("rules.intent-admission-unknown-disposition");
        }
    }

    private async ValueTask CancelAsync(
        RuleInvocationEnvelope invocation,
        RuleRunIdentity identity,
        CancellationToken cancellationToken)
    {
        var result = await _runs.CancelAsync(new(invocation), cancellationToken);
        if (result.Identity != identity)
            throw Permanent("rules.intent-cancellation-identity-mismatch");

        switch (result.Disposition)
        {
            case RuleCancellationDisposition.Cancelled:
            case RuleCancellationDisposition.Replay:
            case RuleCancellationDisposition.TerminalUnchanged:
                return;
            case RuleCancellationDisposition.Unavailable:
                throw Transient("rules.intent-cancellation-unavailable");
            case RuleCancellationDisposition.Conflict:
            case RuleCancellationDisposition.Invalid:
                throw Permanent("rules.intent-cancellation-rejected");
            default:
                throw Permanent("rules.intent-cancellation-unknown-disposition");
        }
    }

    private static bool HasInvalidRoundLimitProof(
        ActivityExecutionState activity,
        RunRulesRequest request,
        RuleNativeResultAddress address)
    {
        try
        {
            if (activity.Status != ActivityExecutionStatus.Faulted ||
                activity.Fault?.Code != RulesNativeBoundary.InvalidRoundLimit ||
                activity.TriggerDeliveries is not { } deliveries || activity.Attempts is not { } attempts)
                return false;

            var expectedKey = RulesNativeBoundary.DeliveryDeduplicationKey(
                address, RulesNativeBoundary.InvalidDeliveryId(request, address));
            var matches = deliveries.Where(delivery =>
                delivery.RegistrationId == address.RegistrationId &&
                delivery.PayloadType == RulesNativeBoundary.CompletionType &&
                delivery.ProviderId == RulesNativeBoundary.ProviderId &&
                delivery.DeduplicationKey == expectedKey &&
                delivery.Status == ActivityTriggerDeliveryStatus.Consumed).ToArray();
            if (matches.Length != 1)
                return false;

            var receipt = matches[0];
            if (attempts.Count == 0 || attempts.Select(attempt => attempt.Ordinal).Distinct().Count() != attempts.Count)
                return false;

            var resumeAttempts = attempts
                .Where(attempt => attempt.TriggerDeliveryId == receipt.DeliveryId)
                .OrderBy(attempt => attempt.Ordinal)
                .ToArray();
            if (resumeAttempts.Length == 0 || resumeAttempts[^1].Ordinal != attempts.Max(attempt => attempt.Ordinal))
                return false;

            for (var index = 0; index < resumeAttempts.Length; index++)
            {
                var attempt = resumeAttempts[index];
                if (attempt.InvocationId != request.ActivityExecutionId ||
                    attempt.Reason != ActivityAttemptReason.Resume ||
                    attempt.TransitionKind != ActivityTransitionKind.Fault || attempt.EndedAt is null)
                    return false;

                if (index > 0 &&
                    (resumeAttempts[index - 1].EndedAt is not { } previousEnd || previousEnd > attempt.StartedAt))
                    return false;
            }

            if (receipt.Payload.Presence == ValuePresence.Absent)
                return true;

            if (receipt.Payload.InlineValue is not { } json)
                return false;

            var completion = RulesNativeBoundary.Read<RunRulesCompletion>(json);
            return completion.FormatVersion == RulesNativeBoundary.FormatVersion &&
                   completion.Destination == address &&
                   completion.DeliveryId == RulesNativeBoundary.InvalidDeliveryId(request, address) &&
                   completion.Result is null &&
                   completion.InvalidCode == RulesNativeBoundary.InvalidRoundLimit;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static RuntimePostCommitDeliveryException Transient(string code, Exception? inner = null) =>
        new(PostCommitFailureKind.Transient, code,
            "The Rules native intent is retained for retry because durable completion is not yet proven.", inner);

    private static RuntimePostCommitDeliveryException Permanent(string code, Exception? inner = null) =>
        new(PostCommitFailureKind.Permanent, code,
            "The Rules native intent does not match its committed route or invocation.", inner);
}
