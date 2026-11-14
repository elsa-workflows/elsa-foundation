using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Checks trusted authoring observations inside the selected provider's protected resource transition.</summary>
internal sealed class TriggerManagementTargets(TriggerTargetResolver resolver, ITriggerInputMapper mapper)
{
    internal IWorkflowActivationAuthority Slots => resolver.Slots;
    internal IWorkflowExecutableSourceReferenceStore Sources => resolver.Sources;
    internal IWorkflowExecutableStore Executables => resolver.Executables;

    internal async ValueTask<IReadOnlyList<ResolvedTriggerTarget>> CheckAsync(TriggerScope scope, TriggerAggregate? previous,
        TriggerStoreWrite write, CancellationToken cancellationToken)
    {
        var selectors = Selectors(previous, write);
        var observed = write.ObservedTargets ?? throw new NotSupportedException("Protected association changes require server-observed targets.");
        if (observed.Any(item => item is null) || observed.Select(item => (item.DefinitionId, item.SlotName)).Distinct().Count() != observed.Count ||
            !selectors.ToHashSet().SetEquals(observed.Select(item => (item.DefinitionId, item.SlotName))))
            throw new InvalidDataException("The protected change requires exactly one observation for every previous and proposed target.");
        var actual = new Dictionary<(string DefinitionId, string SlotName), ResolvedTriggerTarget>();
        foreach (var expected in observed)
        {
            var target = await resolver.ResolveAsync(scope, expected.DefinitionId, expected.SlotName, cancellationToken);
            if (Observation(expected.DefinitionId, expected.SlotName, target) != expected)
                throw new TriggerOperationException("conflict", "A reviewed target publication or input contract changed before the resource commit.");
            actual.Add((expected.DefinitionId, expected.SlotName), target);
        }
        bool Eligible(TriggerSubscription row)
        {
            var target = actual[(row.DefinitionId, row.SlotName)];
            return target.Available && target.Model!.ModelKey == row.ModelKey &&
                row.Grant is { Status: TriggerGrantStatus.Granted } grant && grant.SourceTypeKey == write.Trigger.TypeKey &&
                mapper.ValidateMappings(target.InputContract!, row.Mappings).IsValid;
        }
        foreach (var row in write.Subscriptions.Where(row => row.Enabled))
        {
            var old = previous?.Subscriptions.SingleOrDefault(item => item.Id == row.Id);
            if ((old is null || !old.Enabled || old.DefinitionId != row.DefinitionId || old.SlotName != row.SlotName ||
                 old.ModelKey != row.ModelKey || old.MappingRevision != row.MappingRevision ||
                 row.Grant?.Status == TriggerGrantStatus.Granted && old.Grant?.Revision != row.Grant.Revision) && !Eligible(row))
                throw new TriggerOperationException("validation", "An enabled association requires its current serving contract, valid mappings and a resource invocation grant.");
        }
        // Pausing, revocation and edits to an already blocked resource remain possible.
        if (write.Trigger.DesiredState == TriggerDesiredState.Enabled && previous?.Trigger.DesiredState != TriggerDesiredState.Enabled &&
            !write.Subscriptions.Any(row => row.Enabled && Eligible(row)))
            throw new TriggerOperationException("validation", "Enable at least one currently eligible association first.");
        return actual.Values.ToArray();
    }

    internal static IReadOnlyList<(string DefinitionId, string SlotName)> Selectors(TriggerAggregate? previous, TriggerStoreWrite write) =>
        (previous?.Subscriptions ?? []).Concat(write.Subscriptions).Select(row => (row.DefinitionId, row.SlotName)).Distinct().ToArray();

    internal static TriggerTargetObservation Observation(string definitionId, string slotName, ResolvedTriggerTarget target) =>
        new(definitionId, slotName, target.Slot?.Revision, target.Source?.ActivationId,
            target.InputContract is null ? null : RuntimeIngressAdmission.ComputeInputContractFingerprint(target.InputContract),
            target.Model?.ModelKey ?? target.Material?.Model?.Protocol.Key ?? "native",
            target.Available ? TriggerTargetState.Available : target.Slot?.ActiveActivationId is null ? TriggerTargetState.Unpublished : TriggerTargetState.Unavailable);

    internal static TriggerTargetObservation Observation(string definitionId, string slotName, TriggerTargetDescription? target) =>
        new(definitionId, slotName, target?.SlotRevision, target?.PublicationId, target?.ContractRevision, target?.ModelKey,
            target?.State ?? TriggerTargetState.Unavailable);
}
