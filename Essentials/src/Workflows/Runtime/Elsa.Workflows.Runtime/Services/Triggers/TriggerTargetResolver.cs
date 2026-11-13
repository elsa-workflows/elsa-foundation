using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed record ResolvedTriggerTarget(WorkflowActivationSlot? Slot, WorkflowExecutableSourceReference? Source,
    WorkflowExecutable? Native, RuntimeExecutionMaterial? Material, TriggerTargetModelDescriptor? Model,
    WorkflowExecutableInputContract? InputContract, IReadOnlyList<TriggerFinding> Findings)
{
    internal bool Available => Slot?.ActiveActivationId is not null && Source is not null && Model is not null && InputContract is not null && Findings.Count == 0;
    internal WorkflowExecutableIdentity Identity => Native?.Identity ?? Material?.Identity ?? throw new InvalidOperationException("Target material is unavailable.");
}

/// <summary>Reads current logical slots. This performs no policy admission, mapping, queue writes or runtime adapter resolution.</summary>
internal sealed class TriggerTargetResolver(IWorkflowActivationAuthority slots, IWorkflowExecutableSourceReferenceStore sources,
    IWorkflowExecutableStore executables, IPersistenceAccessContextAccessor access, IInlineEventPublisher publisher,
    TimeProvider clock)
{

    internal IWorkflowActivationAuthority Slots => slots;
    internal IWorkflowExecutableSourceReferenceStore Sources => sources;
    internal IWorkflowExecutableStore Executables => executables.GetMaterialStore();

    internal async ValueTask<ResolvedTriggerTarget> ResolveAsync(TriggerScope scope, string definitionId, string slotName, CancellationToken ct)
    {
        RequireScope(scope);
        var slot = await slots.FindAsync(definitionId, slotName, ct);
        if (slot?.ActiveActivationId is null)
            return new(slot, null, null, null, null, null, [new("target-unpublished", "The selected publication slot is not serving a version.")]);
        var source = await sources.FindAsync(WorkflowActivationReferenceIdentity.Create(slot.ActiveActivationId), ct);
        if (source is null || source.ActivationId != slot.ActiveActivationId || source.SlotId != slot.SlotId ||
            source.DefinitionId != definitionId || source.Scope != WorkflowExecutableReferenceScope.Published ||
            source.TenantId != scope.TenantId || !source.IsLive(clock.GetUtcNow()))
            throw new InvalidDataException("The serving publication slot does not have its exact live source in this tenant.");

        RuntimeExecutionMaterial? material = null;
        WorkflowExecutable? native;
        if (executables.SupportsModelMaterial)
        {
            material = await executables.FindMaterialAsync(source.ArtifactId, ct)
                ?? throw new InvalidDataException("The serving publication material is missing.");
            native = material.NativeWorkflow;
        }
        else native = await executables.FindAsync(source.ArtifactId, ct)
            ?? throw new InvalidDataException("The serving publication material is missing.");
        var discovery = new TriggerTargetModelsDiscovering();
        await publisher.Publish(discovery, ct);
        var models = ValidateModels(discovery.Models);
        var matching = models.Where(model => native is not null
            ? model.ProtocolKey is null
            : model.ProtocolKey == material!.Model!.Protocol.Key && model.ProtocolVersion == material.Model.Protocol.Version &&
              model.Profiles.Contains(material.Model.Profile, StringComparer.Ordinal)).ToArray();
        if (matching.Length > 1) throw new InvalidOperationException("Several target descriptors claim the same execution material.");
        var descriptor = matching.SingleOrDefault();
        var contract = native?.InputContract ?? material?.Model?.InputContract;
        IReadOnlyList<TriggerFinding> findings = descriptor is null
            ? [new("target-model-unsupported", "This execution model or profile is unavailable for standalone triggers.")]
            : !descriptor.AcceptsNewStarts ? [new("target-starts-disabled", "This execution model is not accepting new workflow starts.")]
            : contract is null || contract.Version != WorkflowExecutableInputContract.CurrentVersion
                ? [new("target-input-contract-unavailable", "Publish this workflow with a supported declared input contract.")]
                : [];
        RequireScope(scope);
        return new(slot, source, native, material, descriptor, contract, findings);
    }

    internal void RequireScope(TriggerScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (access.Current.RequireScope() != scope.Persistence)
            throw new InvalidOperationException("Trigger target discovery requires its actual persistence scope.");
    }

    internal static TriggerTargetModelDescriptor[] ValidateModels(IEnumerable<TriggerTargetModelDescriptor> models)
    {
        var result = models.ToArray();
        if (result.Any(item => item is null || string.IsNullOrWhiteSpace(item.ModelKey) || item.Profiles is null) ||
            result.GroupBy(item => item.ModelKey, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            result.Count(item => item.ProtocolKey is null) > 1)
            throw new InvalidOperationException("Target model descriptors must have distinct owned model keys and one native descriptor.");
        foreach (var item in result)
            if ((item.ProtocolKey is null) != (item.ProtocolVersion is null) || item.ProtocolVersion <= 0 ||
                item.ProtocolKey is not null && item.Profiles.Count == 0 || item.Profiles.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("A model target descriptor requires an exact protocol and supported profiles.");
        return result;
    }
}

internal sealed class NativeTriggerTargetModelSource : ITriggerTargetModelSource
{
    public IEnumerable<TriggerTargetModelDescriptor> GetModels() => [new("native", null, null, [], true)];
}

internal sealed class CollectTriggerTargetModels(IEnumerable<ITriggerTargetModelSource> sources)
    : IEventHandler<TriggerTargetModelsDiscovering>
{
    public Task Handle(TriggerTargetModelsDiscovering domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
            foreach (var model in source.GetModels())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Detach before the producer iterator can clear or reuse its profile collection.
                domainEvent.Models.Add(model is null || model.Profiles is null ? model! : model with
                {
                    Profiles = Array.AsReadOnly(model.Profiles.ToArray())
                });
            }
        return Task.CompletedTask;
    }
}
