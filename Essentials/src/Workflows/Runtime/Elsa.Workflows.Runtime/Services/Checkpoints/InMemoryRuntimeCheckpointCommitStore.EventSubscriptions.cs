using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

public sealed partial class InMemoryRuntimeCheckpointCommitStore
{
    public ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot candidate,
        CancellationToken cancellationToken = default) => InstallCohortAsync(source, candidate, null, null, cancellationToken);

    internal ValueTask<StimulusDeliverySnapshot> InstallCombinedAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot ordinary,
        RuntimeWorkflowSubscriptionCandidate subscriptions, InMemoryWorkflowActivationAuthority authority, CancellationToken token) =>
        InstallCohortAsync(source, ordinary, subscriptions, authority, token);

    private static T WithSubscriptionSlots<T>(InMemoryWorkflowActivationAuthority? authority, Func<T> action)
    {
        if (authority is null) return action();
        lock (authority.Gate) return action();
    }

    private void ValidateWorkflowSubscriptions(RuntimeWorkflowSubscriptionCandidate candidate, InMemoryWorkflowActivationAuthority authority,
        WorkflowExecutionState publisher, IReadOnlyDictionary<string, WorkflowExecutableSourceReference> references, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IReadOnlyList<TriggerAggregate> current = candidate.Source is null ? [] : _state.StandaloneTriggers
            .Where(pair => pair.Key.Scope == candidate.Scope.Persistence.Value && pair.Key.Tenant == candidate.Scope.TenantId)
            .Select(pair => pair.Value).Where(resource => WorkflowStimulusSubscriptionSelector.Matches(resource.Trigger, candidate.Source)).ToArray();
        RuntimeWorkflowSubscriptionValidation.CheckResources(candidate, current, publisher);
        foreach (var observation in candidate.Triggers.SelectMany(trigger => trigger.Targets))
        {
            var slot = authority.Current(observation.DefinitionId, observation.SlotName);
            var reference = slot?.ActiveActivationId is { } activation
                ? references.GetValueOrDefault(WorkflowActivationReferenceIdentity.Create(activation)) : null;
            RuntimeIngressAdmissionValidation.CheckTarget(candidate.Delivery, candidate.Scope, observation, slot, reference, _timeProvider.GetUtcNow());
        }
        _retentionMaterial!.ExecuteUnderMaterialGate(material =>
        {
            foreach (var recipient in candidate.Delivery.Recipients)
            {
                if (recipient.ModelProtocol is not null) throw new NotSupportedException("The selected native store cannot retain model material.");
                var actual = material.SingleOrDefault(executable => executable.Identity.ArtifactId == recipient.Material.ArtifactId) ??
                    throw new InvalidDataException("An independently selected executable is missing.");
                RuntimeIngressAdmissionValidation.CheckMaterial(recipient, new RuntimeExecutionMaterial(actual, "native-memory"));
            }
            return true;
        });
    }
}
