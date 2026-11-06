using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Dormant trusted authored publisher; source/material/slot authority stays in the selected Runtime owners.</summary>
internal sealed class BpmnMessageServingPublicationBridge(IWorkflowExecutableStore materials,
    IWorkflowExecutableSourceReferenceStore sources, IPersistenceAccessContextAccessor access, TimeProvider clock,
    MessageModelPublicationCoordinator publications)
{
    // Same canonical authored-version kind used by Workflow publishing. Old direct BPMN catalog/start profiles
    // do not recognize it and therefore cannot accidentally admit this still-dormant profile through formats1–6.
    internal const string SourceKind = BpmnMessageServingSourceReference.SourceKind;
    internal sealed record Publication(BpmnPublishedModel Material, WorkflowActivationResult Activation);
    internal async ValueTask<Publication> PublishAsync(RuntimeModelMaterial requested, BpmnModelPublicationProvenance authored,
        string requestId, BpmnModelCaller caller, string slotName, long expectedRevision, CancellationToken ct = default)
    {
        publications.RequireDomainTuple(materials, sources, access, clock);
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(authored); ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller.ActorId); ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        var scope = access.Current.RequireScope(); access.Current.EnsureTenantScope(caller.TenantId);
        void RequireCallScope()
        { publications.RequireDomainTuple(materials, sources, access, clock); if (access.Current.RequireScope() != scope) throw new NotSupportedException("The publication scope changed."); access.Current.EnsureTenantScope(caller.TenantId); }
        if (authored.DefinitionId is null) throw new NotSupportedException("Message model publication requires canonical authored WorkflowDefinition provenance.");
        _ = BpmnMessageMaterial.ReadChecked(requested);
        if (!materials.SupportsModelMaterial || !sources.CanWriteRootReferences(materials))
            throw new NotSupportedException("The selected material/source owners cannot publish this model safely.");
        var activationId = "bpmn-message:" + BpmnModelIdentity.Create(scope.Value, caller.TenantId ?? "", caller.ActorId, requestId);
        var at = clock.GetUtcNow();
        var reference = BpmnMessageServingSourceReference.Create(authored, requested.Identity.ArtifactId,
            activationId, slotName, caller.TenantId, at);
        var prior = await sources.FindAsync(reference.SourceReferenceId, ct); RequireCallScope();
        if (prior is not null)
        {
            reference = reference with { CreatedAt = prior.CreatedAt, PublishedAt = prior.PublishedAt };
            if ((!prior.IsLive(clock.GetUtcNow()) && (prior.DeletedAt is null || prior.DeletedReason != Elsa.Workflows.Runtime.Services.Executables.WorkflowActivationCoordinator.FailedRetireReason)) ||
                !WorkflowExecutableSourceReferenceComparer.SameIdentity(prior, reference))
                throw new RuntimeCheckpointReplayConflictException("publish-message:" + requestId);
        }
        await materials.SaveModelAsync(requested, ct); RequireCallScope();
        var snapshot = await materials.FindMaterialAsync(requested.Identity.ArtifactId, ct) ?? throw new NotSupportedException("The canonical model is unavailable."); RequireCallScope();
        var canonicalModel = snapshot.Model ?? throw new NotSupportedException("The canonical published model material is unavailable.");
        if (snapshot.Identity.ArtifactHash != requested.Identity.ArtifactHash) throw new NotSupportedException("The canonical published model material differs.");
        var activation = await publications.ActivateAsync(new(canonicalModel, reference, slotName, expectedRevision, WorkflowActivationSource.Publishing), ct);
        RequireCallScope();
        return new(new(snapshot.Identity, snapshot.IncarnationId, reference.SourceReferenceId, canonicalModel.Profile), activation);
    }
    internal async ValueTask<WorkflowActivationResult> DeactivateAsync(string sourceReferenceId, string slotName,
        long expectedRevision, BpmnModelCaller caller, CancellationToken ct = default)
    {
        publications.RequireDomainTuple(materials, sources, access, clock);
        var scope = access.Current.RequireScope(); access.Current.EnsureTenantScope(caller.TenantId);
        void RequireCallScope()
        { publications.RequireDomainTuple(materials, sources, access, clock); if (access.Current.RequireScope() != scope) throw new NotSupportedException("The publication scope changed."); access.Current.EnsureTenantScope(caller.TenantId); }
        var reference = await sources.FindAsync(sourceReferenceId, ct); RequireCallScope();
        if (reference is null || reference.SourceKind != SourceKind || reference.TenantId != caller.TenantId ||
            reference.Scope != WorkflowExecutableReferenceScope.Published || reference.ActivationId is null ||
            reference.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(reference.ActivationId) ||
            reference.SlotId != WorkflowActivationSlotIdentity.Create(reference.DefinitionId, slotName)) throw new NotSupportedException("The canonical Message publication is unavailable.");
        var snapshot = await materials.FindMaterialAsync(reference.ArtifactId, ct); RequireCallScope();
        if (snapshot?.Model is null) throw new NotSupportedException("The canonical Message model is unavailable.");
        _ = BpmnMessageMaterial.ReadChecked(snapshot.Model);
        var result = await publications.DeactivateAsync(reference, slotName, expectedRevision, WorkflowActivationSource.Publishing, ct);
        RequireCallScope(); return result;
    }
}
