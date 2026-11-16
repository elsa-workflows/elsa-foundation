using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static class RuntimeIngressAdmissionValidation
{
    internal static void CheckScope(IPersistenceAccessContextAccessor access, TriggerScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        access.Current.RequireScope(); access.Current.EnsureScope(scope.Persistence);
    }

    internal static RuntimeIngressAdmissionReceipt Replay(RuntimePostCommitOutboxItem item, TriggerScope scope, string id, string fingerprint)
    {
        CheckSource(item, scope);
        if (item.StandaloneIngress is not { } source)
            throw new InvalidDataException("The source key does not identify a standalone acceptance.");
        if (item.OutboxItemId != id || source.RequestFingerprint != fingerprint)
            throw new TriggerOperationException("conflict", "The source key already names different submitted content.");
        return new(item, true);
    }

    internal static void CheckSource(RuntimePostCommitOutboxItem item, TriggerScope scope)
    {
        if (item.SourceScope != scope.Persistence || item.SubscriptionDelivery is null)
            throw new InvalidDataException("The accepted ingress source differs from its exact trusted scope.");
        if (item.StandaloneIngress is { } source)
        {
            if (item.Intent is not null || source.Scope != scope.Persistence || source.TenantId != scope.TenantId)
                throw new InvalidDataException("The accepted standalone source differs from its exact trusted scope.");
        }
        else if (item.Intent is null || item.RecipientSnapshot is not { } legacy || legacy.Scope.PersistenceScope != scope.Persistence ||
            legacy.Scope.TenantId != scope.TenantId || legacy.RetentionAcceptanceVersion != StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion ||
            item.SubscriptionDelivery.ResourceCaptures is null)
            throw new InvalidDataException("The workflow source has no complete physically accepted combined cohort.");
    }

    internal static string? Tenant(RuntimePostCommitOutboxItem item) => item.StandaloneIngress is { } source ? source.TenantId :
        item.RecipientSnapshot?.Scope.TenantId;

    internal static void CheckPublisher(RuntimePostCommitOutboxItem item, WorkflowExecutionState publisher, PersistenceScope scope)
    {
        if (item.Intent?.WorkflowExecutionId != publisher.WorkflowExecutionId || item.RecipientSnapshot?.Scope.Matches(publisher, scope) != true)
            throw new InvalidDataException("The physical workflow publisher differs from its combined accepted source scope.");
        CheckSource(item, new(scope, publisher.TenantId));
    }

    internal static void CheckCandidate(RuntimeIngressAdmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Candidate);
        ArgumentNullException.ThrowIfNull(request.Targets);
        CheckSource(request.Candidate, request.Scope);
        var candidate = request.Candidate;
        if (candidate.StandaloneIngress is null) throw new InvalidDataException("Standalone admission requires its actual standalone source arm.");
        if (candidate.SubscriptionDelivery!.WaitDelivery is not null)
            throw new InvalidDataException("Only the selected physical provider can capture a new ordinary wait cohort.");
        if (request.WaitCandidate is { } waits)
        {
            if (!candidate.StandaloneIngress.CanonicalPayload.TryGetProperty("waitCandidate", out var waitEnvelope) ||
                !Same(waitEnvelope, System.Text.Json.JsonSerializer.SerializeToElement(waits, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))))
                throw new InvalidDataException("The ordinary wait candidate differs from its trusted captured source context.");
            if (request.Reservation is { } reservation && (reservation.StimulusType != waits.StimulusType || reservation.StimulusHash != waits.StimulusHash))
                throw new InvalidDataException("The ordinary wait candidate differs from the exact protected source reservation.");
        }
        if (request.ExpectedTriggerRevision <= 0 || candidate.StandaloneIngress!.TriggerRevision != request.ExpectedTriggerRevision ||
            candidate.Status != RuntimePostCommitOutboxStatus.Pending || candidate.DeliveryFencingToken != 0 ||
            candidate.DeliveryAttemptCount != 0 || candidate.DeliveringOwnerId is not null || candidate.DeliveredAt is not null ||
            candidate.SubscriptionDelivery!.Recipients.Count == 0 || candidate.SubscriptionDelivery.Progress.Any(progress =>
                progress.Phase != RuntimeRecipientPhase.Pending || progress.AttemptCount != 0 || progress.RecoveryState != RuntimeRecipientRecoveryState.None) ||
            candidate.SubscriptionDelivery.ControlFacts is not null ||
            request.Targets.Any(target => target is null) || request.Targets.Select(target => target.SubscriptionId).Distinct(StringComparer.Ordinal).Count() != request.Targets.Count)
            throw new InvalidDataException("Ingress admission requires one new complete pending candidate.");
        var source = candidate.StandaloneIngress!;
        if (source.CanonicalPayload.ValueKind != JsonValueKind.Object || !source.CanonicalPayload.TryGetProperty("payload", out var payload) ||
            !source.CanonicalPayload.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object ||
            !source.CanonicalPayload.TryGetProperty("submittedIntent", out var submittedIntent) || submittedIntent.ValueKind == JsonValueKind.Undefined ||
            !source.CanonicalPayload.TryGetProperty("source", out var sourceEnvelope) ||
            RuntimeStandaloneIngressSource.ComputeRequestFingerprint(source.Scope, source.TenantId, source.TriggerId, source.SourceFamily,
                source.SourceKey, source.PayloadSchema, submittedIntent, source.SourceActorAttribution) != source.RequestFingerprint ||
            !JsonElement.DeepEquals(sourceEnvelope, JsonSerializer.SerializeToElement(new
            {
                family = source.SourceFamily, key = source.SourceKey, recordedAt = source.RecordedAt, actor = source.SourceActorAttribution
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            throw new InvalidDataException("The prepared source envelope differs from its exact submitted intent.");
    }

    internal static void CheckAggregate(RuntimeIngressAdmissionRequest request, TriggerAggregate aggregate)
    {
        var source = request.Candidate.StandaloneIngress!;
        var delivery = request.Candidate.SubscriptionDelivery!;
        if (aggregate.Trigger.Id != source.TriggerId || aggregate.Trigger.TenantId != request.Scope.TenantId ||
            aggregate.Trigger.Revision != request.ExpectedTriggerRevision || aggregate.Trigger.AssociationRevision != source.AssociationGeneration ||
            aggregate.Trigger.DesiredState != TriggerDesiredState.Enabled || StandaloneTriggerAdmission.Policy(aggregate.Trigger) != source.AdmissionPolicy)
            throw new TriggerOperationException("conflict", "The trigger changed before admission; prepare its current associations again.");
        if (aggregate.Trigger.TypeKey is "timer" or "cron" && request.ScheduleClaim is null)
            throw new NotSupportedException("Resource schedules require their actual server-owned occurrence claim before admission.");
        if (aggregate.Trigger.TypeKey == "http" && (request.Reservation is null || request.WaitCandidate?.ObservedWaits is null))
            throw new NotSupportedException("HTTP admission requires its protected route and complete authorized ordinary-wait observation.");
        if (!source.CanonicalPayload.TryGetProperty("trigger", out var triggerEnvelope) || !JsonElement.DeepEquals(triggerEnvelope,
            JsonSerializer.SerializeToElement(new
            {
                aggregate.Trigger.Id, aggregate.Trigger.Name, aggregate.Trigger.TypeKey, aggregate.Trigger.SchemaVersion,
                aggregate.Trigger.Revision, aggregate.Trigger.ConfigurationRevision, aggregate.Trigger.AssociationRevision, aggregate.Trigger.Configuration
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            throw new InvalidDataException("The prepared trigger envelope differs from its actual resource revision.");
        var enabled = aggregate.Subscriptions.Where(subscription => subscription.Enabled).ToDictionary(subscription => subscription.Id, StringComparer.Ordinal);
        if (!enabled.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(request.Targets.Select(target => target.SubscriptionId)) ||
            !enabled.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(delivery.Recipients.Select(recipient => recipient.SubscriptionId)
                .Concat(delivery.Refusals.Select(refusal => refusal.SubscriptionId))))
            throw new InvalidDataException("Every enabled association requires one frozen accepted or refused outcome and a target observation.");
        foreach (var target in request.Targets)
        {
            var subscription = enabled[target.SubscriptionId];
            if (target.DefinitionId != subscription.DefinitionId || target.SlotName != subscription.SlotName ||
                target.Slot is { } slot && (slot.WorkflowDefinitionId != target.DefinitionId || slot.SlotName != target.SlotName))
                throw new InvalidDataException("A target observation differs from its resource-owned association.");
        }
        if (delivery.ResourceCaptures is { } resourceCaptures && (resourceCaptures.Count != 1 || !Same(resourceCaptures[0],
            new RuntimeSubscriptionResourceCapture(aggregate.Trigger.Id, aggregate.Trigger.Revision, aggregate.Trigger.ConfigurationRevision,
                aggregate.Trigger.AssociationRevision, enabled.Keys.ToArray(), triggerEnvelope))))
            throw new InvalidDataException("The resource attribution differs from the actual accepted trigger.");
        foreach (var refusal in delivery.Refusals)
            if (refusal.SubscriptionRevision != enabled[refusal.SubscriptionId].Revision)
                throw new InvalidDataException("A refused association changed before admission.");
        foreach (var recipient in delivery.Recipients)
        {
            var subscription = enabled[recipient.SubscriptionId];
            if (subscription.Revision != recipient.SubscriptionRevision || subscription.MappingRevision != recipient.MappingRevision ||
                subscription.DefinitionId != recipient.LogicalDefinitionId || recipient.MappingVersion != 1 ||
                subscription.Grant is not { Status: TriggerGrantStatus.Granted } grant || grant.Revision != recipient.GrantRevision ||
                grant.SourceTypeKey != aggregate.Trigger.TypeKey || recipient.TenantId != request.Scope.TenantId)
                throw new TriggerOperationException("conflict", "A selected association or invocation grant changed before admission.");
            var expectedAuthority = new WorkflowExecutionAuthoritySnapshot("standalone-trigger:" + aggregate.Trigger.Id,
                source.SourceActorAttribution.RootInitiator, new Dictionary<string, string>
                {
                    ["triggerId"] = aggregate.Trigger.Id, ["subscriptionId"] = subscription.Id,
                    ["grantId"] = subscription.Grant!.Id, ["sourceFamily"] = source.SourceFamily
                });
            if (!Same(expectedAuthority, recipient.Authority) || recipient.RecipientId != recipient.StartIdentity.Digest)
                throw new InvalidDataException("The accepted authority differs from its resource-owned invocation grant.");
        }
    }

    internal static void CheckTarget(RuntimeIngressAdmissionRequest request, RuntimeIngressTargetObservation target,
        WorkflowActivationSlot? actualSlot, WorkflowExecutableSourceReference? actualReference, DateTimeOffset now)
        => CheckTarget(request.Candidate.SubscriptionDelivery!, request.Scope, target, actualSlot, actualReference, now);

    internal static void CheckTarget(RuntimeSubscriptionDelivery delivery, TriggerScope scope, RuntimeIngressTargetObservation target,
        WorkflowActivationSlot? actualSlot, WorkflowExecutableSourceReference? actualReference, DateTimeOffset now)
    {
        if (!Same(target.Slot, actualSlot) || !Same(target.SourceReference, actualReference))
            throw new TriggerOperationException("conflict", "A target publication changed before admission.");
        var recipient = delivery.Recipients.SingleOrDefault(item => item.SubscriptionId == target.SubscriptionId);
        if (recipient is null) return;
        if (actualSlot is not { ActiveActivationId: not null } || actualReference is null ||
            actualSlot.SlotId != recipient.SlotId || actualSlot.Revision != recipient.SlotRevision || actualSlot.ActiveActivationId != recipient.ActivationId ||
            actualReference.Scope != WorkflowExecutableReferenceScope.Published || !actualReference.IsLive(now) ||
            actualReference.TenantId != scope.TenantId || actualReference.ActivationId != actualSlot.ActiveActivationId ||
            actualReference.SlotId != actualSlot.SlotId || actualReference.DefinitionId != target.DefinitionId ||
            actualReference.ArtifactId != recipient.Material.ArtifactId || WorkflowExecutableSourceProvenance.From(actualReference) != recipient.SourceProvenance)
            throw new TriggerOperationException("conflict", "The accepted target is no longer the exact live publication in this tenant.");
    }

    internal static void CheckMaterial(RuntimeSubscriptionRecipient recipient, RuntimeExecutionMaterial material)
    {
        if (material.Identity != recipient.Material || (material.Model is null) != (recipient.ModelProtocol is null) ||
            material.Model is { } model && (model.Protocol != recipient.ModelProtocol || model.Profile != recipient.ModelProfile || material.IncarnationId != recipient.MaterialIncarnation))
            throw new InvalidDataException("Accepted target material differs from its complete exact capture.");
        var contract = material.Model?.InputContract ?? material.NativeWorkflow?.InputContract ??
            throw new NotSupportedException("Standalone ingress requires a declared target input contract.");
        if (RuntimeIngressAdmission.ComputeInputContractFingerprint(contract) != recipient.InputContractFingerprint)
            throw new InvalidDataException("The accepted input contract differs from actual target material.");
    }

    internal static RuntimePostCommitOutboxItem CheckClaim(RuntimePostCommitOutboxItem item, RuntimeDeliverySourceContext source, DateTimeOffset now)
    {
        if (item.OutboxItemId != source.SourceOutboxItemId || item.SourceScope != source.Scope || item.SourceFingerprint != source.SourceFingerprint ||
            item.Status != RuntimePostCommitOutboxStatus.Delivering || item.DeliveringOwnerId != source.Claim.OwnerId ||
            item.DeliveryFencingToken != source.Claim.FencingToken || item.DeliveryVisibleAfter is not { } deadline || deadline <= now)
            throw new RuntimePostCommitOutboxStaleClaimException(item.OutboxItemId, source.Claim.OwnerId, source.Claim.FencingToken,
                item.DeliveringOwnerId, item.DeliveryFencingToken, item.Status);
        CheckSource(item, new(source.Scope, Tenant(item)));
        return item;
    }

    internal static RuntimeCommittedRecipient Committed(RuntimePostCommitOutboxItem item, RuntimeCommittedRecipientAuthority authority)
    {
        CheckSource(item, new(authority.Scope, Tenant(item)));
        var delivery = item.SubscriptionDelivery ?? throw new InvalidDataException("The accepted source has no recipient selection.");
        if (item.OutboxItemId != authority.SourceOutboxItemId || item.SourceScope != authority.Scope || item.SourceFingerprint != authority.SourceFingerprint || delivery.SelectionFingerprint != authority.SelectionFingerprint)
            throw new InvalidDataException("The committed authority differs from the physical accepted source.");
        var recipient = delivery.Recipients.SingleOrDefault(candidate => candidate.RecipientId == authority.RecipientId) ??
            throw new InvalidDataException("The accepted recipient does not exist.");
        var progress = delivery.Progress.Single(candidate => candidate.RecipientId == recipient.RecipientId);
        if (progress.Seal is not { } seal || seal.Fingerprint != authority.SealFingerprint)
            throw new InvalidDataException("The immutable dispatch commitment does not exist or differs.");
        return new(item, recipient, seal);
    }

    internal static RuntimeCommittedRecipient Committed(RuntimePostCommitOutboxItem item, RuntimeIngressCommandOrigin origin)
    {
        var progress = item.SubscriptionDelivery?.Progress.SingleOrDefault(progress => progress.RecipientId == origin.RecipientId);
        if (progress?.Seal is not { } seal) throw new InvalidDataException("The physical dispatch commitment does not exist.");
        return Committed(item, new RuntimeCommittedRecipientAuthority(origin.SourceOutboxItemId, origin.SourceFingerprint,
            origin.SelectionFingerprint, origin.Scope, origin.RecipientId, seal.Fingerprint));
    }

    internal static RuntimeCommittedIngressWait CommittedWait(RuntimePostCommitOutboxItem item, RuntimeIngressWaitOrigin origin)
    {
        CheckSource(item, new(origin.Scope, Tenant(item)));
        var delivery = item.SubscriptionDelivery!.WaitDelivery ?? throw new InvalidDataException("The source has no accepted ordinary wait cohort.");
        if (item.OutboxItemId != origin.SourceOutboxItemId || item.SourceFingerprint != origin.SourceFingerprint ||
            delivery.Scope != origin.Scope || delivery.SelectionFingerprint != origin.SelectionFingerprint)
            throw new InvalidDataException("The wait origin differs from the actual physical source selection.");
        var recipient = delivery.Recipients.SingleOrDefault(recipient => recipient.WaitId == origin.WaitId) ??
            throw new InvalidDataException("The wait was not accepted by this source.");
        var seal = delivery.Progress.Single(progress => progress.WaitId == origin.WaitId).Seal ??
            throw new InvalidDataException("The immutable physical wait dispatch commitment is missing.");
        if (seal.Origin != origin || !seal.Identity.IsEquivalentTo(recipient.Identity))
            throw new InvalidDataException("The physical wait dispatch commitment differs from its immutable origin.");
        return new(item, recipient, seal);
    }

    internal static RuntimePostCommitOutboxItem RecordFailure(RuntimePostCommitOutboxItem item, string id,
        RuntimeRecipientRecoveryState recoveryState, string code, DateTimeOffset? retryAt)
    {
        if (!Enum.IsDefined(recoveryState) || recoveryState == RuntimeRecipientRecoveryState.None || string.IsNullOrWhiteSpace(code) ||
            code.Length > 128 || code.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
            throw new ArgumentException("Recipient failures require a typed recovery state and sanitized diagnostic code.");
        var delivery = item.SubscriptionDelivery!;
        var current = delivery.Progress.SingleOrDefault(progress => progress.RecipientId == id) ?? throw new InvalidDataException("The recipient was not accepted.");
        if (current.Phase is RuntimeRecipientPhase.TargetAccepted or RuntimeRecipientPhase.CancelledBeforeDispatch) return item;
        return item.WithSubscriptionDelivery(delivery.WithProgress(new RuntimeRecipientProgress(id, current.Phase, current.Seal, current.Receipt,
            checked(current.AttemptCount + 1), retryAt, code, recoveryState)));
    }

    internal static RuntimeCommittedIngressWait ProposedWait(RuntimePostCommitOutboxItem item, RuntimeIngressWaitDispatchSeal seal)
    {
        ArgumentNullException.ThrowIfNull(seal);
        CheckSource(item, new(seal.Origin.Scope, Tenant(item)));
        var waits = item.SubscriptionDelivery!.WaitDelivery ?? throw new InvalidDataException("The source has no accepted ordinary wait cohort.");
        var recipient = waits.Recipients.SingleOrDefault(recipient => recipient.WaitId == seal.Identity.WaitId) ??
            throw new InvalidDataException("The wait was not accepted by this source.");
        if (seal.Origin != waits.Origin(recipient) || !seal.Identity.IsEquivalentTo(recipient.Identity) || seal.CommittedAt < item.RecordedAt)
            throw new InvalidDataException("The proposed wait seal differs from the immutable accepted selection.");
        return new(item, recipient, seal);
    }

    internal static RuntimeCommittedIngressWait WaitReceiptTarget(RuntimePostCommitOutboxItem item, RuntimeIngressWaitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var waits = item.SubscriptionDelivery!.WaitDelivery ?? throw new InvalidDataException("The source has no accepted ordinary wait cohort.");
        var recipient = waits.Recipients.SingleOrDefault(recipient => recipient.WaitId == receipt.WaitId) ??
            throw new InvalidDataException("The wait receipt was not accepted by this source.");
        var committed = CommittedWait(item, waits.Origin(recipient));
        if (receipt.SealFingerprint != committed.Seal.Fingerprint || !receipt.Identity.IsEquivalentTo(recipient.Identity) ||
            receipt.Material != recipient.Expected.RegistrationCapture!.Executable)
            throw new InvalidDataException("The wait receipt differs from the exact physical dispatch commitment.");
        return committed;
    }

    internal static RuntimePostCommitOutboxItem AcceptObservedWait(RuntimePostCommitOutboxItem item, RuntimeIngressWaitReceipt receipt)
    {
        _ = WaitReceiptTarget(item, receipt);
        var waits = item.SubscriptionDelivery!.WaitDelivery!;
        var progress = waits.Progress.Single(progress => progress.WaitId == receipt.WaitId);
        return progress.Receipt is not null ? item : item.WithSubscriptionDelivery(item.SubscriptionDelivery.WithWaitDelivery(
            waits.WithProgress(progress.AcceptTarget(receipt))));
    }

    internal static RuntimePostCommitOutboxItem RecordWaitFailure(RuntimePostCommitOutboxItem item, string waitId,
        RuntimeRecipientRecoveryState recoveryState, string code, DateTimeOffset? retryAt)
    {
        if (!Enum.IsDefined(recoveryState) || recoveryState == RuntimeRecipientRecoveryState.None || string.IsNullOrWhiteSpace(code) ||
            code.Length > 128 || code.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
            throw new ArgumentException("Wait failures require a typed recovery state and sanitized diagnostic code.");
        var delivery = item.SubscriptionDelivery!;
        var waits = delivery.WaitDelivery ?? throw new InvalidDataException("The source has no accepted ordinary wait cohort.");
        var progress = waits.Progress.SingleOrDefault(progress => progress.WaitId == waitId) ?? throw new InvalidDataException("The wait was not accepted.");
        if (progress.Phase == RuntimeIngressWaitPhase.TargetAccepted) return item;
        return item.WithSubscriptionDelivery(delivery.WithWaitDelivery(waits.WithProgress(new(waitId, progress.Phase, progress.Seal,
            progress.Receipt, checked(progress.AttemptCount + 1), retryAt, code, recoveryState))));
    }

    internal static RuntimeCommittedRecipient ReceiptTarget(RuntimePostCommitOutboxItem item, RuntimeTargetAcceptanceReceipt receipt)
    {
        var recipient = item.SubscriptionDelivery!.Recipients.SingleOrDefault(recipient => recipient.RecipientId == receipt.RecipientId) ??
            throw new InvalidDataException("The receipt recipient was not accepted.");
        var seal = item.SubscriptionDelivery.Progress.Single(progress => progress.RecipientId == receipt.RecipientId).Seal ??
            throw new InvalidDataException("Target acceptance requires a prior physical dispatch commitment.");
        if (receipt.SealFingerprint != seal.Fingerprint || !receipt.StartIdentity.IsEquivalentTo(recipient.StartIdentity) || receipt.Material != recipient.Material)
            throw new InvalidDataException("The target receipt differs from its exact physical dispatch commitment.");
        return new(item, recipient, seal);
    }

    internal static RuntimePostCommitOutboxItem AcceptObserved(RuntimePostCommitOutboxItem item, RuntimeTargetAcceptanceReceipt receipt)
    {
        _ = ReceiptTarget(item, receipt);
        var delivery = item.SubscriptionDelivery!;
        var progress = delivery.Progress.Single(progress => progress.RecipientId == receipt.RecipientId);
        return progress.Receipt is not null ? item : item.WithSubscriptionDelivery(delivery.WithProgress(progress.AcceptTarget(receipt)));
    }

    internal static RuntimeIngressCancellationReceipt Cancel(RuntimePostCommitOutboxItem item, RuntimeIngressCancellationRequest request)
    {
        CheckSource(item, request.Scope);
        var delivery = item.SubscriptionDelivery!;
        if (item.SourceFingerprint != request.SourceFingerprint || delivery.SelectionFingerprint != request.SelectionFingerprint ||
            request.RecipientIds.Count != request.RecipientIds.Distinct(StringComparer.Ordinal).Count() ||
            request.RecipientIds.Any(id => delivery.Progress.All(progress => progress.RecipientId != id)))
            throw new TriggerOperationException("conflict", "Cancellation requires the exact accepted source and recipient set.");
        foreach (var id in request.RecipientIds)
            delivery = delivery.WithProgress(delivery.Progress.Single(progress => progress.RecipientId == id).CancelPending());
        var replacement = item.WithSubscriptionDelivery(delivery);
        if (item.StandaloneIngress is not null && delivery.Progress.All(progress => progress.Phase == RuntimeRecipientPhase.CancelledBeforeDispatch) &&
            (delivery.WaitDelivery?.Recipients.Count ?? 0) == 0 && item.Status != RuntimePostCommitOutboxStatus.Cancelled)
            replacement = RuntimeIngressControlRules.CancelSource(replacement);
        return new(replacement, Array.AsReadOnly(request.RecipientIds.Where(id => delivery.Progress.Single(progress => progress.RecipientId == id)
            .Phase == RuntimeRecipientPhase.CancelledBeforeDispatch).ToArray()), Array.AsReadOnly(request.RecipientIds.Where(id =>
                delivery.Progress.Single(progress => progress.RecipientId == id).Seal is not null).ToArray()));
    }

    internal static bool Same<T>(T? left, T? right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));
}
