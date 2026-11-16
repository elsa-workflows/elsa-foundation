using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static class RuntimeWorkflowSubscriptionValidation
{
    internal static void CheckCandidate(PostCommitDeliveryContext source, StimulusDeliverySnapshot ordinary,
        RuntimeWorkflowSubscriptionCandidate candidate, RuntimeWorkflowSubscriptionSource? expected)
    {
        ArgumentNullException.ThrowIfNull(candidate); ArgumentNullException.ThrowIfNull(candidate.Delivery); ArgumentNullException.ThrowIfNull(candidate.Triggers);
        var delivery = candidate.Delivery;
        if (source.Claim.Item.StandaloneIngress is not null || source.Claim.Item.SubscriptionDeliveryProtocolVersion != 1 || !ordinary.MatchesSource(source) ||
            candidate.Scope.Persistence != source.Scope || candidate.Scope.TenantId != ordinary.Scope.TenantId ||
            delivery.SourceOutboxItemId != source.SourceOutboxItemId || delivery.SourceFingerprint != source.IntentFingerprint || delivery.Scope != source.Scope ||
            delivery.ResourceCaptures is null || delivery.ControlFacts is not null || delivery.WaitDelivery is not null ||
            delivery.Progress.Any(progress => progress.Phase != RuntimeRecipientPhase.Pending || progress.AttemptCount != 0 ||
                progress.RecoveryState != RuntimeRecipientRecoveryState.None) || !RuntimeIngressAdmissionValidation.Same(expected, candidate.Source) ||
            candidate.Triggers.Any(trigger => trigger is null) || candidate.Triggers.Select(trigger => trigger.Resource.Trigger.Id).Distinct(StringComparer.Ordinal).Count() != candidate.Triggers.Count)
            throw new InvalidDataException("The proposed combined workflow cohort differs from its canonical source or complete new selection.");
        if (expected is null && (candidate.Triggers.Count != 0 || delivery.ResourceCaptures.Count != 0 || delivery.Recipients.Count != 0 || delivery.Refusals.Count != 0))
            throw new InvalidDataException("A local stimulus cannot capture independently managed starts.");
    }

    internal static RuntimeSubscriptionDelivery? Existing(RuntimePostCommitOutboxItem item, RuntimeWorkflowSubscriptionCandidate? candidate)
    {
        if (candidate is null)
        {
            if (item.SubscriptionDeliveryProtocolVersion is not null)
                throw new NotSupportedException("This source requires one atomic ordinary and independent recipient selection.");
            return null;
        }
        if (item.SubscriptionDeliveryProtocolVersion != 1 || (item.RecipientSnapshot is null) != (item.SubscriptionDelivery is null))
            throw new InvalidDataException("The workflow source has an unsupported or partial combined cohort.");
        if (item.SubscriptionDelivery is not { } delivery) return null;
        RuntimeIngressAdmissionValidation.CheckSource(item, candidate.Scope);
        return delivery;
    }

    internal static void CheckResources(RuntimeWorkflowSubscriptionCandidate candidate, IReadOnlyList<TriggerAggregate> current,
        WorkflowExecutionState publisher)
    {
        var delivery = candidate.Delivery;
        if (candidate.Scope.TenantId != publisher.TenantId || !current.Select(resource => resource.Trigger.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(candidate.Triggers.Select(trigger => trigger.Resource.Trigger.Id)) ||
            !current.Select(resource => resource.Trigger.Id).ToHashSet(StringComparer.Ordinal).SetEquals(delivery.ResourceCaptures!.Select(capture => capture.TriggerId)))
            throw new TriggerOperationException("conflict", "The complete matching resource range changed before source selection.");
        if (delivery.Recipients.Count != 0 && publisher.Authority is null)
            throw new NotSupportedException("The actual workflow publisher has no retained execution authority.");
        foreach (var actual in current)
        {
            var observed = candidate.Triggers.Single(trigger => trigger.Resource.Trigger.Id == actual.Trigger.Id);
            var capture = delivery.ResourceCaptures!.Single(capture => capture.TriggerId == actual.Trigger.Id);
            var enabled = actual.Subscriptions.Where(subscription => subscription.Enabled).ToDictionary(subscription => subscription.Id, StringComparer.Ordinal);
            if (!RuntimeIngressAdmissionValidation.Same(actual, observed.Resource) || actual.Trigger.TenantId != candidate.Scope.TenantId ||
                candidate.Source is null || !WorkflowStimulusSubscriptionSelector.Matches(actual.Trigger, candidate.Source))
                throw new TriggerOperationException("conflict", "A resource changed before source selection.");
            if (observed.Targets.Any(target => target is null) || observed.Targets.Select(target => target.SubscriptionId).Distinct(StringComparer.Ordinal).Count() != observed.Targets.Count ||
                !enabled.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(observed.Targets.Select(target => target.SubscriptionId)) ||
                !RuntimeIngressAdmissionValidation.Same(capture, new RuntimeSubscriptionResourceCapture(actual.Trigger.Id, actual.Trigger.Revision,
                    actual.Trigger.ConfigurationRevision, actual.Trigger.AssociationRevision, enabled.Keys.ToArray(), WorkflowStimulusSubscriptionSelector.TriggerEnvelope(actual.Trigger))))
                throw new InvalidDataException("The complete resource attribution or target observations differ from the enabled association set.");
            foreach (var target in observed.Targets)
                if (target.DefinitionId != enabled[target.SubscriptionId].DefinitionId || target.SlotName != enabled[target.SubscriptionId].SlotName)
                    throw new InvalidDataException("A target observation differs from its actual association.");
            foreach (var refusal in delivery.Refusals.Where(refusal => capture.SubscriptionIds.Contains(refusal.SubscriptionId, StringComparer.Ordinal)))
                if (refusal.SubscriptionRevision != enabled[refusal.SubscriptionId].Revision)
                    throw new InvalidDataException("A refused association changed before source acceptance.");
            foreach (var recipient in delivery.Recipients.Where(recipient => capture.SubscriptionIds.Contains(recipient.SubscriptionId, StringComparer.Ordinal)))
            {
                var subscription = enabled[recipient.SubscriptionId];
                if (subscription.Revision != recipient.SubscriptionRevision || subscription.MappingRevision != recipient.MappingRevision || recipient.MappingVersion != 1 ||
                    subscription.DefinitionId != recipient.LogicalDefinitionId || subscription.Grant is not { Status: TriggerGrantStatus.Granted } grant ||
                    grant.Revision != recipient.GrantRevision || grant.SourceTypeKey != actual.Trigger.TypeKey || recipient.TenantId != candidate.Scope.TenantId ||
                    recipient.Partition != publisher.Partition || recipient.RecipientId != recipient.StartIdentity.Digest)
                    throw new TriggerOperationException("conflict", "An accepted association or invocation grant changed before source selection.");
                var authority = new WorkflowExecutionAuthoritySnapshot("standalone-trigger:" + actual.Trigger.Id, publisher.Authority!.RootInitiator,
                    new Dictionary<string, string>
                    {
                        ["triggerId"] = actual.Trigger.Id, ["subscriptionId"] = subscription.Id, ["grantId"] = subscription.Grant.Id,
                        ["sourceFamily"] = candidate.Source.SourceFamily
                    });
                if (!RuntimeIngressAdmissionValidation.Same(authority, recipient.Authority))
                    throw new InvalidDataException("The accepted workflow subscription authority differs from its actual grant.");
            }
        }
    }
}
