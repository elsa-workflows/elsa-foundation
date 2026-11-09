using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Rules.Workflows.Runtime.Services;

internal sealed record RulesNativeDeliveryInspection(RuleDeliveryProof? Proof, BookmarkState? Bookmark);

/// <summary>Reads the persisted native parent and child records which can prove a Rules result was consumed.</summary>
internal sealed class RulesNativeDeliveryInspector(
    IWorkflowExecutionStateStore workflows,
    IActivityExecutionStateStore activities,
    IBookmarkStateStore bookmarks,
    IPersistenceAccessContextAccessor access,
    IExternalPayloadStore? externalPayloadStore = null)
{
    // Keep these checks aligned with ActivityAttemptActivationClaimer's persisted history bounds. That runtime
    // compacts attempts and retains receipts referenced by those attempts before committing state. A larger
    // array is therefore malformed/unsupported persisted state, not a valid retry chain.
    private const int MaxRetainedAttempts = 32;
    private const int MaxRetainedTriggerDeliveries = 32;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24
    };

    public async ValueTask<RulesNativeDeliveryInspection> InspectAsync(
        RuleRunIdentity identity,
        RuleInvocationFingerprint expectedFingerprint,
        RuleTerminalEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ValidEnvelope(identity, expectedFingerprint, envelope))
            return Empty;

        try
        {
            if (access.Current.RequireScope().Value != identity.Scope.PersistencePartition)
                return Empty;
        }
        catch (InvalidOperationException)
        {
            return Empty;
        }

        var address = envelope.Destination;
        var parent = await workflows.FindAsync(address.WorkflowExecutionId, cancellationToken);
        if (parent is null || !MatchesParent(parent, identity, address))
            return Empty;

        if (parent.Status.IsTerminal())
        {
            if (parent.CompletedAt is not { } completedAt)
                return Empty;
            return new(Proof(envelope, RuleDeliveryProofKind.ParentProvenTerminal,
                ParentTerminalIdentity(parent, identity.Scope.PersistencePartition, completedAt)), null);
        }

        var activity = await activities.FindAsync(address.WorkflowExecutionId, address.ActivityExecutionId, cancellationToken);
        if (activity is null || !MatchesActivity(activity, address) ||
            !TryReadAddress(activity, out var retainedAddress) || retainedAddress != address)
            return Empty;

        if (activity.PrivateState is not null && !MatchesPrivateInvocation(activity, identity, expectedFingerprint, address))
            return Empty;

        if (activity.Status == ActivityExecutionStatus.Suspended)
        {
            var bookmark = await FindLiveBookmarkAsync(activity, identity, expectedFingerprint, address, cancellationToken);
            return new(null, bookmark);
        }

        if (activity.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted))
            return Empty;

        if (!TryFindFinalConsumedReceipt(activity, address, envelope, out var receipt, out var finalAttempt))
            return Empty;

        if (receipt.Payload.Presence == ValuePresence.Present)
        {
            var received = await ReadEnvelopeJsonAsync(receipt.Payload, cancellationToken);
            if (received is null || !MatchesDeliveredCompletion(received.Value, address, envelope))
                return Empty;
        }
        else if (receipt.Payload.Presence != ValuePresence.Absent)
        {
            return Empty;
        }

        if (envelope.Result.Kind is RuleTerminalKind.Succeeded or RuleTerminalKind.Refused)
        {
            if (!await MatchesSuccessfulCompletionAsync(activity, finalAttempt, envelope, cancellationToken))
                return Empty;
        }
        else if (envelope.Result.Kind == RuleTerminalKind.Faulted)
        {
            if (!MatchesFault(activity, finalAttempt, envelope))
                return Empty;
        }
        else
        {
            // A child cancellation is not durable proof that the parent observed the result. The parent-terminal
            // proof above is the only cancellation proof.
            return Empty;
        }

        return new(Proof(envelope, RuleDeliveryProofKind.AlreadyConsumed, finalAttempt.AttemptId), null);
    }

    private async ValueTask<BookmarkState?> FindLiveBookmarkAsync(
        ActivityExecutionState activity,
        RuleRunIdentity identity,
        RuleInvocationFingerprint expectedFingerprint,
        RuleNativeResultAddress address,
        CancellationToken cancellationToken)
    {
        if (activity.PrivateState is null || activity.BookmarkIds.Count != 1 ||
            !activity.BookmarkIds.Contains(address.BookmarkId, StringComparer.Ordinal) ||
            activity.TriggerRegistrations is not { Count: 1 } registrations)
            return null;

        if (activity.TriggerDeliveries is { } deliveries)
        {
            if (deliveries.Count > MaxRetainedTriggerDeliveries)
                return null;
            // Any consumed record for this exact native bookmark means the suspended view is stale and must not
            // be dispatched again, including the separate invalid-budget refusal delivery.
            if (deliveries.Any(delivery => delivery.RegistrationId == address.RegistrationId &&
                                           delivery.Status == ActivityTriggerDeliveryStatus.Consumed &&
                                           delivery.DeduplicationKey.StartsWith(
                                               $"{address.WorkflowExecutionId}:bookmark-resume:{address.BookmarkId}:{address.StimulusType}:{address.StimulusHash}:",
                                               StringComparison.Ordinal)))
                return null;
        }

        var registration = registrations.Single();
        if (registration.RegistrationId != address.RegistrationId ||
            registration.InvocationId != address.ActivityExecutionId ||
            registration.ResumeTargetKey != address.ResumeTargetId ||
            registration.StimulusType != address.StimulusType ||
            registration.StimulusHash != address.StimulusHash ||
            registration.PayloadType != RulesNativeBoundary.CompletionType ||
            registration.DeduplicationPolicy != ActivityTriggerDeduplicationPolicy.Once ||
            !MatchesPrivateInvocation(activity, identity, expectedFingerprint, address))
            return null;

        var bookmark = await bookmarks.FindAsync(address.WorkflowExecutionId, address.BookmarkId, cancellationToken);
        if (bookmark is null)
            return null;

        try
        {
            RulesNativeBoundary.EnsureBookmark(address, bookmark);
            return bookmark;
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            return null;
        }
    }

    private static bool ValidEnvelope(
        RuleRunIdentity identity,
        RuleInvocationFingerprint expectedFingerprint,
        RuleTerminalEnvelope envelope)
    {
        try
        {
            var address = envelope.Destination;
            if (address.FormatVersion != RulesNativeBoundary.FormatVersion ||
                address.ProviderId != RulesNativeBoundary.ProviderId ||
                address.ResultContractId != RulesNativeBoundary.CompletionType.Alias ||
                address.ResultSchemaVersion != RulesNativeBoundary.FormatVersion ||
                address.DeduplicationPolicy != "once" ||
                string.IsNullOrWhiteSpace(address.ResumeTargetId) ||
                address.StimulusType != RulesNativeBoundary.StimulusType ||
                address.StimulusHash != RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(identity)) ||
                address.WorkflowExecutionId.Length == 0 || address.ActivityExecutionId.Length == 0 ||
                identity.InvocationId != RulesNativeBoundary.RunId(identity.Scope, address.WorkflowExecutionId, address.ActivityExecutionId))
                return false;

            var delivery = RuleRecordCodec.DeliveryBytes(envelope.DeliveryId, address, envelope.Result);
            return RuleRecordCodec.Hash(delivery) == envelope.CanonicalPayloadSha256 &&
                   expectedFingerprint.Sha256.Length == 64;
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            return false;
        }
    }

    private static bool MatchesParent(WorkflowExecutionState parent, RuleRunIdentity identity, RuleNativeResultAddress address) =>
        parent.WorkflowExecutionId == address.WorkflowExecutionId &&
        parent.TenantId == identity.Scope.TenantId &&
        parent.Partition?.Value == identity.Scope.PersistencePartition &&
        parent.PinnedExecutable is not null &&
        parent.PinnedExecutable.ArtifactId == address.ExecutableId &&
        parent.PinnedExecutable.ArtifactHash == address.ExecutableHash;

    private static bool MatchesActivity(ActivityExecutionState activity, RuleNativeResultAddress address) =>
        activity.Execution is not null &&
        activity.Execution.ActivityType == RunRules.ActivityType &&
        activity.Execution.WorkflowExecutionId == address.WorkflowExecutionId &&
        activity.InvocationId == address.ActivityExecutionId &&
        activity.Execution.ExecutableNodeId == address.NodeId;

    private static bool TryReadAddress(ActivityExecutionState activity, out RuleNativeResultAddress? address)
    {
        address = null;
        try
        {
            if (!activity.Metadata.TryGetValue(RulesNativeBoundary.AddressMetadataKey, out var json) ||
                Encoding.UTF8.GetByteCount(json) > RuleLimits.MaxStateMetadataBytes)
                return false;
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            address = RulesNativeBoundary.Read<RuleNativeResultAddress>(document.RootElement);
            return true;
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            address = null;
            return false;
        }
    }

    private static bool MatchesPrivateInvocation(
        ActivityExecutionState activity,
        RuleRunIdentity identity,
        RuleInvocationFingerprint expectedFingerprint,
        RuleNativeResultAddress address)
    {
        try
        {
            var request = RulesNativeBoundary.ReadState(activity);
            if (request.Scope != identity.Scope || request.RunId != identity.InvocationId ||
                request.WorkflowExecutionId != address.WorkflowExecutionId ||
                request.ActivityExecutionId != address.ActivityExecutionId ||
                request.ExecutableId != address.ExecutableId || request.ExecutableHash != address.ExecutableHash ||
                request.NodeId != address.NodeId || request.ResumeTargetId != address.ResumeTargetId)
                return false;
            var invocation = RulesNativeBoundary.Invocation(request, address);
            return invocation.Identity == identity && RuleRecordCodec.Fingerprint(invocation) == expectedFingerprint;
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            return false;
        }
    }

    private static bool TryFindFinalConsumedReceipt(
        ActivityExecutionState activity,
        RuleNativeResultAddress address,
        RuleTerminalEnvelope envelope,
        out ActivityTriggerDelivery receipt,
        out ActivityAttempt finalAttempt)
    {
        receipt = null!;
        finalAttempt = null!;
        if (activity.TriggerDeliveries is not { } deliveries || deliveries.Count is 0 or > MaxRetainedTriggerDeliveries ||
            activity.Attempts is not { } attempts || attempts.Count is 0 or > MaxRetainedAttempts)
            return false;

        var expectedKey = RulesNativeBoundary.DeliveryDeduplicationKey(address, envelope.DeliveryId);
        var matches = deliveries.Where(delivery => delivery.DeduplicationKey == expectedKey).ToArray();
        if (matches.Length != 1 || matches[0].Status != ActivityTriggerDeliveryStatus.Consumed)
            return false;

        var candidate = matches[0];
        if (candidate.RegistrationId != address.RegistrationId || candidate.ProviderId != RulesNativeBoundary.ProviderId ||
            candidate.PayloadType != RulesNativeBoundary.CompletionType || candidate.Payload.Type != RulesNativeBoundary.CompletionType)
            return false;

        var ordered = attempts.OrderBy(attempt => attempt.Ordinal).ToArray();
        if (ordered.Select(attempt => attempt.Ordinal).Distinct().Count() != ordered.Length ||
            ordered.Select(attempt => attempt.AttemptId).Distinct(StringComparer.Ordinal).Count() != ordered.Length ||
            ordered.Any(attempt => attempt.InvocationId != address.ActivityExecutionId || attempt.EndedAt is null ||
                                   attempt.TransitionKind is null))
            return false;

        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].EndedAt!.Value > ordered[index].StartedAt)
                return false;
        }

        var matching = ordered.Where(attempt => attempt.TriggerDeliveryId == candidate.DeliveryId).ToArray();
        if (matching.Length == 0 || matching[^1] != ordered[^1] ||
            matching.Any(attempt => attempt.Reason != ActivityAttemptReason.Resume) ||
            matching.Take(matching.Length - 1).Any(attempt => attempt.TransitionKind != ActivityTransitionKind.Fault) ||
            matching[^1].StartedAt < candidate.ReceivedAt)
            return false;

        receipt = candidate;
        finalAttempt = matching[^1];
        return true;
    }

    private async ValueTask<JsonElement?> ReadEnvelopeJsonAsync(ValueEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Presence != ValuePresence.Present)
            return null;
        if (envelope.InlineValue is { } inline)
            return IsBounded(inline) ? inline.Clone() : null;
        if (envelope.ExternalReference is not { } reference)
            return null;
        if (externalPayloadStore is null)
            throw new InvalidOperationException("The native Rules payload store is required to verify an external result.");
        var external = await externalPayloadStore.ReadAsync(reference, cancellationToken);
        return IsBounded(external) ? external.Clone() : null;
    }

    private async ValueTask<bool> MatchesSuccessfulCompletionAsync(
        ActivityExecutionState activity,
        ActivityAttempt finalAttempt,
        RuleTerminalEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (activity.Status != ActivityExecutionStatus.Completed || activity.Completion is not { } completion ||
            activity.Fault is not null || completion.InvocationId != activity.InvocationId ||
            completion.AttemptId != finalAttempt.AttemptId || activity.CompletedAt is not { } activityCompletedAt ||
            finalAttempt.EndedAt != activityCompletedAt || finalAttempt.StartedAt > completion.CompletedAt ||
            completion.CompletedAt > activityCompletedAt || finalAttempt.TransitionKind != ActivityTransitionKind.Complete ||
            completion.Result.Type != new ValueTypeDescriptor(typeof(RuleTerminalResult).FullName!) ||
            activity.ContractIdentity is not { } contract || contract.ActivityTypeKey != RunRules.ActivityType ||
            completion.ContractFingerprint != contract.SchemaFingerprint)
            return false;

        var expectedOutcome = envelope.Result.Kind == RuleTerminalKind.Succeeded ? "Done" : "Refused";
        if (completion.OutcomeKey != expectedOutcome)
            return false;

        var json = await ReadEnvelopeJsonAsync(completion.Result, cancellationToken);
        if (json is null)
            return false;

        try
        {
            if (!IsBounded(json.Value))
                return false;
            var bytes = JsonMarshal.GetRawUtf8Value(json.Value);
            var observed = JsonSerializer.Deserialize<RuleTerminalResult>(bytes, WebJson);
            return observed is not null &&
                   RuleRecordCodec.TerminalBytes(observed).AsSpan().SequenceEqual(
                       RuleRecordCodec.TerminalBytes(envelope.Result));
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            return false;
        }
    }

    private static bool MatchesFault(ActivityExecutionState activity, ActivityAttempt finalAttempt, RuleTerminalEnvelope envelope)
    {
        if (activity.Status != ActivityExecutionStatus.Faulted || activity.Completion is not null ||
            activity.Fault is not { } fault || activity.CompletedAt != finalAttempt.EndedAt ||
            finalAttempt.TransitionKind != ActivityTransitionKind.Fault ||
            fault.Code != (envelope.Result.Diagnostic?.Code ?? "rules.run-faulted") ||
            fault.ExceptionType != typeof(ActivityFault).FullName)
            return false;

        using var terminalDocument = JsonDocument.Parse(RuleRecordCodec.TerminalBytes(envelope.Result));
        var completion = new RunRulesCompletion(RulesNativeBoundary.FormatVersion, envelope.Destination,
            envelope.DeliveryId, terminalDocument.RootElement.Clone(), InvalidCode: null);
        var expectedMarker = RulesNativeBoundary.TerminalMarker(completion, envelope.Result);
        return fault.Metadata.TryGetValue(ActivityFault.FaultTypeMetadataKey, out var actualMarker) &&
               actualMarker == expectedMarker;
    }

    private static bool MatchesDeliveredCompletion(JsonElement json, RuleNativeResultAddress address, RuleTerminalEnvelope envelope)
    {
        try
        {
            if (!IsBounded(json))
                return false;
            var completion = RulesNativeBoundary.Read<RunRulesCompletion>(json);
            if (completion.FormatVersion != RulesNativeBoundary.FormatVersion || completion.Destination != address ||
                completion.DeliveryId != envelope.DeliveryId || completion.InvalidCode is not null ||
                completion.Result is not { } resultJson)
                return false;
            var bytes = JsonMarshal.GetRawUtf8Value(resultJson);
            if (bytes.Length > RuleLimits.MaxEnvelopeBytes)
                return false;
            var result = RuleRecordCodec.ReadTerminal(bytes.ToArray());
            return RuleRecordCodec.TerminalBytes(result).AsSpan().SequenceEqual(RuleRecordCodec.TerminalBytes(envelope.Result));
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            return false;
        }
    }

    private static bool IsBounded(JsonElement json) =>
        JsonMarshal.GetRawUtf8Value(json).Length <= RuleLimits.MaxEnvelopeBytes;

    private static RuleDeliveryProof Proof(RuleTerminalEnvelope envelope, RuleDeliveryProofKind kind, string evidenceId) =>
        new(kind, RuleRecordCodec.DestinationIdentity(envelope.Destination), envelope.Result.TerminalId,
            envelope.CanonicalPayloadSha256, evidenceId);

    private static string ParentTerminalIdentity(WorkflowExecutionState parent, string partition, DateTimeOffset completedAt)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            parent.WorkflowExecutionId,
            Partition = partition,
            parent.TenantId,
            parent.PinnedExecutable.ArtifactId,
            parent.PinnedExecutable.DefinitionId,
            parent.PinnedExecutable.DefinitionVersionId,
            parent.PinnedExecutable.ArtifactVersion,
            parent.PinnedExecutable.ArtifactHash,
            Status = parent.Status.ToString(),
            CompletedAtUtcTicks = completedAt.UtcDateTime.Ticks
        });
        return "rules-parent-terminal-v1:" + RuleRecordCodec.Hash(bytes);
    }

    private static bool IsMalformed(Exception exception) =>
        exception is ArgumentException or InvalidDataException or JsonException or NotSupportedException or FormatException or
            OverflowException or InvalidOperationException or KeyNotFoundException;

    private static RulesNativeDeliveryInspection Empty { get; } = new(null, null);
}
