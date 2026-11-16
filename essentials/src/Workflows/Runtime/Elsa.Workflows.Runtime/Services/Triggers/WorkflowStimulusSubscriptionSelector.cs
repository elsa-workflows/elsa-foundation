using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Prepares independent associations for one actual workflow broadcast; performs no recipient effects or source acceptance.</summary>
internal sealed class WorkflowStimulusSubscriptionSelector(IStandaloneTriggerStore resources, TriggerTargetResolver targets,
    ITriggerInputMapper mapper, IEnumerable<IStandaloneTriggerRuntimeTarget> runtimeTargets,
    IEnumerable<IStimulusDeliveryIntentAdapter> adapters, IWorkflowExecutionStateStore executions, TimeProvider clock,
    IWorkflowExecutableStartPolicy policy)
{
    private readonly StimulusDeliveryRequestProjector projector = new(adapters);
    private readonly IReadOnlyDictionary<string, IStandaloneTriggerRuntimeTarget> runtimeTargets = runtimeTargets.ToDictionary(target => target.ModelKey, StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal async ValueTask<RuntimeWorkflowSubscriptionCandidate> SelectAsync(PostCommitDeliveryContext source, CancellationToken token)
    {
        var request = projector.Read(source);
        var publisher = await executions.FindAsync(source.Intent.WorkflowExecutionId, token) ?? throw new InvalidDataException("The physical broadcast publisher is missing.");
        var scope = new TriggerScope(source.Scope, publisher.TenantId);
        targets.RequireScope(scope);
        if (request.Mode == StimulusRoutingMode.ResumeOnly)
            return new(scope, null, Empty(source), []);
        var sourceActor = publisher.Authority;
        var input = projector.SubscriptionSource(source);
        var observations = new List<RuntimeIngressTriggerObservation>();
        var accepted = new List<RuntimeSubscriptionRecipient>();
        var refused = new List<RuntimeSubscriptionRefusal>();
        var captures = new List<RuntimeSubscriptionResourceCapture>();
        foreach (var resource in await ReadMatchingAsync(resources, scope, input, token))
        {
            var trigger = resource.Trigger;
            var subscriptions = resource.Subscriptions.Where(subscription => subscription.Enabled).OrderBy(subscription => subscription.Id, StringComparer.Ordinal).ToArray();
            if (subscriptions.Length != 0 && sourceActor is null)
                throw new NotSupportedException("The actual broadcast publisher has no captured execution authority.");
            var envelope = JsonSerializer.SerializeToElement(new
            {
                payload = input.Payload, metadata = input.Metadata,
                source = new { family = input.SourceFamily, key = source.SourceOutboxItemId, recordedAt = source.Intent.RecordedAt, actor = sourceActor },
                trigger = TriggerEnvelope(trigger)
            }, Json);
            var targetsObserved = new List<RuntimeIngressTargetObservation>();
            foreach (var subscription in subscriptions)
            {
                var target = await targets.ResolveAsync(scope, subscription.DefinitionId, subscription.SlotName, token);
                targetsObserved.Add(new(subscription.Id, subscription.DefinitionId, subscription.SlotName, target.Slot, target.Source));
                string? reason = subscription.Grant is not { Status: TriggerGrantStatus.Granted } grant || grant.SourceTypeKey != trigger.TypeKey
                    ? "target-grant-revoked" : !target.Available ? target.Findings[0].Code : null;
                if (reason is null && (!runtimeTargets.TryGetValue(target.Model!.ModelKey, out var adapter) || !adapter.Supports(target.Native, target.Material?.Model)))
                    reason = "target-runtime-unavailable";
                var authority = new WorkflowExecutionAuthoritySnapshot("standalone-trigger:" + trigger.Id, sourceActor!.RootInitiator,
                    new Dictionary<string, string>
                    {
                        ["triggerId"] = trigger.Id, ["subscriptionId"] = subscription.Id, ["grantId"] = subscription.Grant?.Id ?? "ungranted",
                        ["sourceFamily"] = input.SourceFamily
                    });
                if (reason is null && (publisher.Partition is null || publisher.Partition.Value != source.Scope.Value))
                    reason = "target-partition-unsupported";
                if (reason is null && !(await policy.EvaluateAsync(new(target.Identity, WorkflowExecutableStartAuthorityKind.StandaloneTrigger,
                        authority.SystemIdentity, authority, WorkflowRunKind.PublishedRun, scope.TenantId, publisher.Partition), token)).IsAllowed)
                    reason = "target-start-policy-denied";
                WorkflowExecutableInputValidation? inputs = null;
                if (reason is null)
                {
                    inputs = await mapper.MapAsync(target.InputContract!, subscription.Mappings, envelope, token);
                    if (!inputs.IsValid) reason = inputs.Findings.First().ReasonCode;
                }
                if (reason is not null) { refused.Add(new(subscription.Id, subscription.Revision, "Admission", reason)); continue; }
                var identity = new RuntimeSubscriptionStartIdentity(1, source.Scope, source.SourceOutboxItemId, subscription.Id);
                accepted.Add(new(identity.Digest, subscription.Id, subscription.Revision, subscription.Grant!.Revision, subscription.DefinitionId,
                    target.Model!.ModelKey, target.Slot!.SlotId, target.Slot.Revision, target.Slot.ActiveActivationId!, target.Identity,
                    WorkflowExecutableSourceProvenance.From(target.Source!), RuntimeIngressAdmission.ComputeInputContractFingerprint(target.InputContract!),
                    subscription.MappingRevision, 1, inputs!.NormalizedInputs, authority, scope.TenantId,
                    publisher.Partition ?? throw new NotSupportedException("The actual broadcast publisher has no explicit execution partition."), identity,
                    target.Material?.Model?.Protocol, target.Material?.Model?.Profile, target.Material?.Model is null ? null : target.Material.IncarnationId));
            }
            observations.Add(new(resource, Array.AsReadOnly(targetsObserved.ToArray())));
            captures.Add(new(trigger.Id, trigger.Revision, trigger.ConfigurationRevision, trigger.AssociationRevision,
                subscriptions.Select(subscription => subscription.Id).ToArray(), TriggerEnvelope(trigger)));
        }
        return new(scope, input, new(1, source.SourceOutboxItemId, source.IntentFingerprint, source.Scope, accepted, refused,
            accepted.Select(recipient => new RuntimeRecipientProgress(recipient.RecipientId, RuntimeRecipientPhase.Pending)).ToArray(), resourceCaptures: captures),
            Array.AsReadOnly(observations.ToArray()));
    }

    internal static RuntimeSubscriptionDelivery Empty(PostCommitDeliveryContext source) =>
        new(1, source.SourceOutboxItemId, source.IntentFingerprint, source.Scope, [], [], [], resourceCaptures: []);

    internal static JsonElement TriggerEnvelope(StandaloneTrigger trigger) => JsonSerializer.SerializeToElement(new
    {
        trigger.Id, trigger.Name, trigger.TypeKey, trigger.SchemaVersion, trigger.Revision, trigger.ConfigurationRevision,
        trigger.AssociationRevision, trigger.Configuration
    }, Json);

    internal static bool Matches(StandaloneTrigger trigger, RuntimeWorkflowSubscriptionSource source)
    {
        if (trigger.TypeKey != source.TriggerTypeKey || trigger.DesiredState != TriggerDesiredState.Enabled) return false;
        if (trigger.SchemaVersion != source.TriggerSchemaVersion) throw new NotSupportedException("An eligible source resource has an unsupported schema.");
        if (!trigger.Configuration.TryGetProperty(source.ConfigurationField, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("A source resource has invalid canonical matching configuration.");
        return value.GetString() == source.ConfigurationValue;
    }

    internal static async ValueTask<IReadOnlyList<TriggerAggregate>> ReadMatchingAsync(IStandaloneTriggerStore resources, TriggerScope scope,
        RuntimeWorkflowSubscriptionSource source, CancellationToken token)
    {
        var result = new List<TriggerAggregate>();
        string? after = null; var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await resources.ListAsync(scope, new(100, after, TypeKey: source.TriggerTypeKey, DesiredState: TriggerDesiredState.Enabled), token);
            foreach (var trigger in page.Items)
            {
                if (!Matches(trigger, source)) continue;
                var aggregate = await resources.FindAsync(scope, trigger.Id, token) ?? throw new TriggerOperationException("conflict", "A matching resource changed during discovery.");
                if (aggregate.Trigger.Revision != trigger.Revision || !Matches(aggregate.Trigger, source))
                    throw new TriggerOperationException("conflict", "A matching resource changed during discovery.");
                result.Add(aggregate);
            }
            if (page.Next is null) break;
            if (!seen.Add(page.Next)) throw new InvalidDataException("The resource discovery cursor did not advance.");
            after = page.Next;
        }
        return Array.AsReadOnly(result.OrderBy(resource => resource.Trigger.Id, StringComparer.Ordinal).ToArray());
    }
}
