using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class ProtectedTriggerManagementCommitter(IStandaloneTriggerStore resources, ITriggerSourceResourceCommitStore physical,
    IEnumerable<StandaloneTriggerSourceCapability> installedSources, IEnumerable<ITriggerReservationPlanner> reservationPlanners,
    IStandaloneTriggerTargetDiscovery? targets = null, IEnumerable<IStandaloneSchedulePlanner>? schedulePlanners = null,
    IRuntimeIngressDeliveryStore? ingress = null) : ITriggerManagementCommitter
{
    // Several independently bound producers can supply the same trigger family.
    // Their source identities remain distinct; activation needs an installed type/schema pair.
    private readonly IReadOnlyList<StandaloneTriggerSourceCapability> installed = installedSources
        .Where(item => item.SupportsResourceAuthorization).ToArray();
    private readonly IReadOnlyDictionary<string, ITriggerReservationPlanner> planners = reservationPlanners.ToDictionary(item => item.TypeKey, StringComparer.Ordinal);
    public bool SupportsActivatedResources => physical.SupportsActivatedResources;
    public bool CanActivateType(string typeKey, int schemaVersion) => SupportsActivatedResources &&
        installed.Any(item => item.Descriptor.TypeKey == typeKey && item.Descriptor.SchemaVersion == schemaVersion &&
            (item.WorkflowIntentKind is null ? ingress?.SupportsAdmission == true : ingress?.SupportsWorkflowCohort(item.WorkflowIntentKind) == true));
    public bool CanStopType(string typeKey, int schemaVersion) => SupportsActivatedResources && (CanActivateType(typeKey, schemaVersion) ||
        planners.ContainsKey(typeKey) || schedulePlanners?.Any(planner => planner.Supports(typeKey)) == true);

    public async ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write, CancellationToken cancellationToken = default)
    {
        var replay = await resources.FindMutationAsync(scope, write.ActorId, write.RequestId, cancellationToken);
        if (replay is not null)
        {
            if (replay.RequestFingerprint != write.RequestFingerprint || write.ExpectedRevision is not null && replay.TriggerId != write.Trigger.Id)
                throw new TriggerOperationException("conflict", "The mutation identity already names different content.");
            return replay;
        }
        var previous = await resources.FindAsync(scope, write.Trigger.Id, cancellationToken);
        if (write.ObservedTargets is null)
        {
            var observations = new List<TriggerTargetObservation>();
            foreach (var (definitionId, slotName) in TriggerManagementTargets.Selectors(previous, write))
            {
                if (targets is null) throw new NotSupportedException("Protected association changes require server target discovery.");
                observations.Add(TriggerManagementTargets.Observation(definitionId, slotName,
                    await targets.DescribeAsync(scope, definitionId, slotName, cancellationToken)));
            }
            write = write with { ObservedTargets = observations.AsReadOnly() };
        }
        var live = write.Trigger.HasActivated || previous?.Trigger.HasActivated == true || write.Trigger.DesiredState == TriggerDesiredState.Enabled;
        var stopOnly = previous is not null && CanStopType(write.Trigger.TypeKey, write.Trigger.SchemaVersion) &&
            TriggerStopOnlyChange.IsSafe(previous, new(write.Trigger, write.Subscriptions));
        if (live && !CanActivateType(write.Trigger.TypeKey, write.Trigger.SchemaVersion) && !stopOnly)
            throw new TriggerOperationException("unavailable", "The protected source for this trigger type is unavailable.");
        if (live && !write.Trigger.HasActivated)
        {
            var row = write.Trigger;
            write = write with { Trigger = new(row.Id, row.Name, row.TypeKey, row.SchemaVersion, row.Configuration, row.Revision,
                row.ConfigurationRevision, row.AssociationRevision, row.DesiredState, row.Origin, row.CreatedAt, row.UpdatedAt,
                row.CreatedBy, row.Description, true, row.TenantId) };
        }
        var reservations = planners.TryGetValue(write.Trigger.TypeKey, out var planner)
            ? await planner.PlanAsync(scope, previous, write, cancellationToken) : [];
        return await physical.CommitAsync(scope, write, reservations, cancellationToken);
    }
}
