using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class StandaloneTriggerAdmission(IStandaloneTriggerStore resources, IRuntimeIngressDeliveryStore delivery,
    TriggerTargetResolver targets, ITriggerInputMapper mapper, IEnumerable<IStandaloneTriggerRuntimeTarget> runtimeTargets, TimeProvider clock,
    IWorkflowExecutableStartPolicy startPolicy)
{
    private readonly IReadOnlyDictionary<string, IStandaloneTriggerRuntimeTarget> runtimeTargets = runtimeTargets.ToDictionary(x => x.ModelKey, StringComparer.Ordinal);

    internal async ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(StandaloneTriggerSourceDescriptor producer,
        StandaloneTriggerInvocationContext context, StandaloneTriggerSignal signal, CancellationToken ct, TriggerAggregate? authorizedResource = null)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(signal);
        targets.RequireScope(context.Scope);
        if (!delivery.SupportsAdmission) throw new NotSupportedException("The selected Runtime provider cannot accept standalone trigger work.");
        if (!SchemasMatch(producer.PayloadSchema, signal.PayloadSchema))
            throw new TriggerOperationException("source-schema-mismatch", "The submitted payload schema does not belong to this source.");
        var sourceId = RuntimeStandaloneIngressSource.BuildOutboxItemId(context.Scope.Persistence, context.Scope.TenantId,
            signal.TriggerId, producer.SourceFamily, signal.SourceKey);
        var submittedIntent = context.SubmittedIntent?.Clone() ?? RuntimeIngressAdmission.ComputeSubmittedIntent(signal.Payload, signal.Metadata);
        var requestFingerprint = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(context.Scope.Persistence,
            context.Scope.TenantId, signal.TriggerId, producer.SourceFamily, signal.SourceKey, signal.PayloadSchema,
            submittedIntent, context.Actor);
        // Saved acceptance wins before today's enabled state, targets, mappings, policy and publication are consulted.
        if (await delivery.FindAcceptedAsync(context.Scope, sourceId, requestFingerprint, ct) is { } prior) return prior;
        var aggregate = authorizedResource ?? await resources.FindAsync(context.Scope, signal.TriggerId, ct)
            ?? throw new TriggerOperationException("trigger-not-found", "The trigger is unavailable in this scope.");
        var trigger = aggregate.Trigger;
        if (trigger.TypeKey != producer.TypeKey || trigger.SchemaVersion != producer.SchemaVersion)
            throw new TriggerOperationException("source-type-mismatch", "This source does not own the trigger type and schema.");
        if (trigger.DesiredState != TriggerDesiredState.Enabled)
            throw new StandaloneTriggerAdmissionRefusedException("trigger-not-enabled", []);
        if (context.Reservation is { } reservation && (reservation.Scope != context.Scope ||
            reservation.OwnerKind != RuntimeIngressReservation.StandaloneTriggerOwner || reservation.OwnerId != signal.TriggerId ||
            reservation.OwnerRevision != trigger.Revision || reservation.ConfigurationRevision != trigger.ConfigurationRevision))
            throw new TriggerOperationException("conflict", "The matched source route changed before admission.");
        var subscriptions = aggregate.Subscriptions.Where(x => x.Enabled).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var policy = Policy(trigger);
        var responseMode = IsWorkflowResponse(trigger);
        if (responseMode && subscriptions.Length != 1)
            throw new StandaloneTriggerAdmissionRefusedException("http-response-owner-required", []);
        var now = clock.GetUtcNow();
        var envelope = JsonSerializer.SerializeToElement(new
        {
            payload = signal.Payload,
            metadata = signal.Metadata,
            submittedIntent,
            waitCandidate = context.WaitCandidate,
            source = new { family = producer.SourceFamily, key = signal.SourceKey, recordedAt = now, actor = context.Actor },
            trigger = new { trigger.Id, trigger.Name, trigger.TypeKey, trigger.SchemaVersion, trigger.Revision,
                trigger.ConfigurationRevision, trigger.AssociationRevision, trigger.Configuration }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var source = new RuntimeStandaloneIngressSource(1, sourceId, sourceId, now, context.Scope.Persistence,
            context.Scope.TenantId, context.Partition, trigger.Id, trigger.Revision, trigger.AssociationRevision,
            producer.SourceFamily, signal.SourceKey, requestFingerprint, signal.PayloadSchema, envelope, policy, context.Actor);
        var accepted = new List<RuntimeSubscriptionRecipient>();
        var refused = new List<RuntimeSubscriptionRefusal>();
        var observations = new List<RuntimeIngressTargetObservation>();
        foreach (var subscription in subscriptions)
        {
            // Every enabled target is observed, including refused targets. An unreadable cohort aborts the entire intake.
            var target = await targets.ResolveAsync(context.Scope, subscription.DefinitionId, subscription.SlotName, ct);
            observations.Add(new(subscription.Id, subscription.DefinitionId, subscription.SlotName, target.Slot, target.Source));
            WorkflowExecutionAuthoritySnapshot? authority = subscription.Grant is null ? null : new("standalone-trigger:" + trigger.Id, context.Actor.RootInitiator,
                new Dictionary<string, string> { ["triggerId"] = trigger.Id, ["subscriptionId"] = subscription.Id,
                    ["grantId"] = subscription.Grant.Id, ["sourceFamily"] = producer.SourceFamily });
            string? reason = subscription.Grant is not { Status: TriggerGrantStatus.Granted } grant || grant.SourceTypeKey != trigger.TypeKey
                ? "target-grant-revoked" : !target.Available ? target.Findings[0].Code : null;
            if (reason is null && (!runtimeTargets.TryGetValue(target.Model!.ModelKey, out var adapter) || !adapter.Supports(target.Native, target.Material?.Model)))
                reason = "target-runtime-unavailable";
            if (reason is null && responseMode && !target.Model!.SupportsWorkflowOwnedHttpResponse)
                reason = "target-http-response-unsupported";
            if (reason is null && context.Partition.Value != context.Scope.Persistence.Value)
                reason = "target-partition-unsupported";
            WorkflowExecutableInputValidation? inputs = null;
            if (reason is null)
            {
                inputs = await mapper.MapAsync(target.InputContract!, subscription.Mappings, envelope, ct);
                if (!inputs.IsValid) reason = inputs.Findings.First().ReasonCode;
            }
            if (reason is null)
            {
                var decision = await startPolicy.EvaluateAsync(new(target.Identity, WorkflowExecutableStartAuthorityKind.StandaloneTrigger,
                    authority!.SystemIdentity, authority, WorkflowRunKind.PublishedRun, context.Scope.TenantId, context.Partition), ct);
                if (!decision.IsAllowed) reason = "target-start-policy-denied";
            }
            if (reason is not null)
            { refused.Add(new(subscription.Id, subscription.Revision, inputs is { IsValid: false } ? "Input" : "Admission", reason)); continue; }
            var identity = new RuntimeSubscriptionStartIdentity(1, context.Scope.Persistence, sourceId, subscription.Id);
            accepted.Add(new(identity.Digest, subscription.Id, subscription.Revision, subscription.Grant!.Revision,
                subscription.DefinitionId, target.Model!.ModelKey, target.Slot!.SlotId, target.Slot.Revision,
                target.Slot.ActiveActivationId!, target.Identity, WorkflowExecutableSourceProvenance.From(target.Source!),
                RuntimeIngressAdmission.ComputeInputContractFingerprint(target.InputContract!), subscription.MappingRevision, 1,
                inputs!.NormalizedInputs, authority!, context.Scope.TenantId, context.Partition, identity,
                target.Material?.Model?.Protocol, target.Material?.Model?.Profile,
                target.Material?.Model is null ? null : target.Material.IncarnationId));
        }
        if (accepted.Count == 0 || policy == RuntimeIngressAdmissionPolicy.RequireAll && refused.Count != 0)
            throw new StandaloneTriggerAdmissionRefusedException(accepted.Count == 0 ? "no-eligible-subscribers" : "all-subscribers-required", refused,
                accepted.Select(recipient => recipient.SubscriptionId).ToArray());
        var selection = new RuntimeSubscriptionDelivery(1, sourceId, source.Fingerprint, context.Scope.Persistence,
            accepted, refused, accepted.Select(x => new RuntimeRecipientProgress(x.RecipientId, RuntimeRecipientPhase.Pending)).ToArray());
        var candidate = RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, selection,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)));
        return await delivery.AdmitAsync(new(context.Scope, trigger.Revision, candidate, observations)
            { Reservation = context.Reservation, ScheduleClaim = context.ScheduleClaim, WaitCandidate = context.WaitCandidate }, ct);
    }

    internal static bool SchemasMatch(Elsa.Primitives.Models.ValueTypeDescriptor left, Elsa.Primitives.Models.ValueTypeDescriptor right) =>
        left.Alias == right.Alias && left.CollectionKind == right.CollectionKind && left.SchemaVersion == right.SchemaVersion &&
        (left.Schema is null && right.Schema is null || left.Schema is { } a && right.Schema is { } b && JsonElement.DeepEquals(a, b));

    private static bool IsWorkflowResponse(StandaloneTrigger trigger) => trigger.TypeKey == "http" &&
        trigger.Configuration.TryGetProperty("http", out var http) && http.TryGetProperty("mode", out var mode) && mode.GetString() == "WorkflowResponse";

    internal static RuntimeIngressAdmissionPolicy Policy(StandaloneTrigger trigger)
    {
        if (trigger.TypeKey != "http") return RuntimeIngressAdmissionPolicy.Independent;
        if (!trigger.Configuration.TryGetProperty("http", out var http) || !http.TryGetProperty("mode", out var mode))
            throw new InvalidDataException("An HTTP trigger requires its canonical response mode.");
        if (mode.GetString() == "WorkflowResponse") return RuntimeIngressAdmissionPolicy.RequireAll;
        if (mode.GetString() != "AsyncFanOut") throw new NotSupportedException("The HTTP response mode is unsupported.");
        if (!http.TryGetProperty("admissionPolicy", out var policy) || policy.GetString() == "RequireAll") return RuntimeIngressAdmissionPolicy.RequireAll;
        if (policy.GetString() == "AcceptAvailable") return RuntimeIngressAdmissionPolicy.AcceptAvailable;
        throw new NotSupportedException("The HTTP admission policy is unsupported.");
    }
}
