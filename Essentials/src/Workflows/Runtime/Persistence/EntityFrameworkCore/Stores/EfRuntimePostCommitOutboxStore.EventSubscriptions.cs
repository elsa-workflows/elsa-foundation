using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfRuntimePostCommitOutboxStore
{
    public ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot candidate,
        CancellationToken cancellationToken = default) => InstallCohortAsync(source, candidate, null, null, cancellationToken);

    internal ValueTask<StimulusDeliverySnapshot> InstallCombinedAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot ordinary,
        RuntimeWorkflowSubscriptionCandidate subscriptions, IStandaloneTriggerStore resources, CancellationToken token) =>
        InstallCohortAsync(source, ordinary, subscriptions, resources, token);

    private async ValueTask ValidateWorkflowSubscriptionsAsync(RuntimeWorkflowSubscriptionCandidate candidate, IStandaloneTriggerStore resources,
        WorkflowExecutionState publisher, CancellationToken token)
    {
        // SERIALIZABLE discovery fences the complete enabled/type range, including insertion/activation of new matching resources.
        IReadOnlyList<TriggerAggregate> current = candidate.Source is null ? [] : await WorkflowStimulusSubscriptionSelector.ReadMatchingAsync(resources, candidate.Scope, candidate.Source, token);
        RuntimeWorkflowSubscriptionValidation.CheckResources(candidate, current, publisher);
        foreach (var resource in current.OrderBy(resource => resource.Trigger.Id, StringComparer.Ordinal))
            if (!await EfStandaloneTriggerStore.LockResourceAsync(context, candidate.Scope, resource.Trigger.Id, resource.Trigger.Revision, token))
                throw new TriggerOperationException("conflict", "A matching source resource changed before cohort acceptance.");
        foreach (var observation in candidate.Triggers.SelectMany(trigger => trigger.Targets)
                     .OrderBy(target => target.DefinitionId, StringComparer.Ordinal).ThenBy(target => target.SlotName, StringComparer.Ordinal))
        {
            if (!await EfAdmissionTargetFence.LockSlotAsync(context, candidate.Scope.Persistence.Value, observation.DefinitionId,
                    observation.SlotName, observation.Slot, token))
                throw new TriggerOperationException("conflict", "A captured independent target slot changed before cohort acceptance.");
            WorkflowExecutableSourceReference? reference = null;
            if (observation.Slot?.ActiveActivationId is { } activation)
            {
                reference = (await retentionReferences!.ReadPhysicalMaintenanceSourceAsync(candidate.Scope.Persistence.Value,
                    WorkflowActivationReferenceIdentity.Create(activation), token))?.Value;
                if (reference is not null && !await EfAdmissionTargetFence.LockSourceAsync(retentionReferences,
                        candidate.Scope.Persistence.Value, reference, token))
                    throw new TriggerOperationException("conflict", "A captured independent source changed before cohort acceptance.");
            }
            RuntimeIngressAdmissionValidation.CheckTarget(candidate.Delivery, candidate.Scope, observation, observation.Slot, reference, timeProvider.GetUtcNow());
        }
        var materials = (Core.Contracts.IWorkflowExecutableStore)new EfWorkflowExecutableStore(context, accessContextAccessor);
        foreach (var recipient in candidate.Delivery.Recipients)
        {
            var material = await materials.FindMaterialAsync(recipient.Material.ArtifactId, token) ?? throw new InvalidDataException("Selected independent material is missing.");
            RuntimeIngressAdmissionValidation.CheckMaterial(recipient, material);
            var modelId = EfRuntimeOperationalStoreSupport.CompositeId(candidate.Scope.Persistence.Value, recipient.StartIdentity.WorkflowExecutionId);
            if (await context.WorkflowExecutionStates.AnyAsync(row => row.Id == EfWorkflowExecutionStateStore.CreateIdForAtomicParticipant(candidate.Scope.Persistence.Value,
                    recipient.StartIdentity.WorkflowExecutionId), token) || await context.Set<Entities.RuntimeModelExecutionEntity>().AnyAsync(row => row.Id == modelId, token))
                throw new InvalidDataException("An independent recipient execution identity was already used before cohort acceptance.");
        }
    }
}
