using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Dormant model-material lifecycle over the existing source, slot and physical switch owners.</summary>
internal sealed class MessageModelPublicationCoordinator
{
    private readonly IMessagePublicationInterestProjection interests;
    private readonly IWorkflowActivationAuthority authority;
    private readonly IWorkflowActivationSwitch activationSwitch;
    private readonly IWorkflowExecutableStore material;
    private readonly IWorkflowExecutableSourceReferenceStore references;
    private readonly IWorkflowExecutableRootWriteLeaseManager leases;
    private readonly IWorkflowTriggerBindingStore bindings;
    private readonly IRecurringTriggerScheduleStore? schedules;
    private readonly TimeProvider clock;
    private readonly IPersistenceAccessContextAccessor access;
    private readonly ActivationChannelOptions channels;
    internal MessageModelPublicationCoordinator(IMessagePublicationInterestProjection interests,
        IWorkflowActivationAuthority authority, IWorkflowActivationSwitch activationSwitch,
        IWorkflowExecutableStore material, IWorkflowExecutableSourceReferenceStore references,
        IWorkflowExecutableRootWriteLeaseManager leases, IWorkflowTriggerBindingStore bindings,
        IPersistenceAccessContextAccessor access, TimeProvider clock, IRecurringTriggerScheduleStore? schedules = null, ActivationChannelOptions? channels = null)
    {
        this.interests = interests; this.authority = authority; this.activationSwitch = activationSwitch; this.material = material;
        this.references = references; this.leases = leases; this.bindings = bindings; this.schedules = schedules;
        this.clock = clock; this.access = access; this.channels = channels ?? new(); RequireComposition();
    }
    private void RequireComposition()
    {
        interests.RequireActualComposition(authority, activationSwitch, references);
        interests.RequireModelComposition(material, leases, clock, activationSwitch, bindings, schedules, access);
        if (!material.SupportsModelMaterial || !references.CanWriteRootReferences(material)) throw Unsupported();
    }
    internal void RequireDomainTuple(IWorkflowExecutableStore selectedMaterial, IWorkflowExecutableSourceReferenceStore selectedReferences,
        IPersistenceAccessContextAccessor selectedAccess, TimeProvider selectedClock)
    {
        RequireComposition();
        if (!ReferenceEquals(material.GetMaterialStore(), selectedMaterial.GetMaterialStore()) || !ReferenceEquals(references, selectedReferences) ||
            !ReferenceEquals(access, selectedAccess) || !ReferenceEquals(clock, selectedClock)) throw Unsupported();
    }
    internal sealed record Command(RuntimeModelMaterial Material, WorkflowExecutableSourceReference Reference,
        string SlotName, long ExpectedRevision, WorkflowActivationSource Source);
    internal async ValueTask<WorkflowActivationResult> ActivateAsync(Command command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(command.Source); RequireComposition();
        var reference = command.Reference; var activation = reference.ActivationId;
        var callScope = access.Current.RequireScope();
        void RequireCallScope()
        { RequireComposition(); if (access.Current.RequireScope() != callScope) throw Unsupported(); access.Current.EnsureTenantScope(reference.TenantId); }
        RequireCallScope();
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SlotName); ArgumentOutOfRangeException.ThrowIfNegative(command.ExpectedRevision);
        if (string.IsNullOrWhiteSpace(activation) || reference.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(activation) ||
            reference.Scope != WorkflowExecutableReferenceScope.Published || reference.ExpiresAt is not null ||
            reference.SlotId != WorkflowActivationSlotIdentity.Create(reference.DefinitionId, command.SlotName) ||
            reference.ArtifactId != command.Material.Identity.ArtifactId || command.Material.Dependencies.Count != 0 ||
            command.Material.Identity.ArtifactHash != command.Material.ComputeContentHash()) throw Unsupported();
        var physical = await material.FindMaterialAsync(command.Material.Identity.ArtifactId, ct) ?? throw Unsupported();
        RequireCallScope();
        var canonicalModel = physical.Model ?? throw Unsupported();
        if (physical.Identity.ArtifactHash != command.Material.Identity.ArtifactHash || canonicalModel.ComputeContentHash() != command.Material.ComputeContentHash()) throw Unsupported();
        var current = await authority.FindAsync(reference.DefinitionId, command.SlotName, ct) ?? Empty(reference.DefinitionId, command.SlotName);
        RequireCallScope();
        if (!channels.Allows(command.SlotName)) return new(false, WorkflowActivationOutcome.ChannelNotAllowed, current);
        if (current.Source is { } owner && !owner.IsSameOwnerAs(command.Source))
            return new(false, WorkflowActivationOutcome.Conflict, current, Conflict: WorkflowActivationConflict.ForeignSource);
        // Selection always comes from the one canonical slot. No catalog/time/Design selection or material-carrier owner.
        if (current.ActiveActivationId is { } serving) { await RequireServingAsync(serving, ct); RequireCallScope(); }
        if (current.ActiveActivationId == activation)
        {
            var actual = await references.FindAsync(reference.SourceReferenceId, ct) ?? throw Unsupported();
            RequireCallScope();
            if (!WorkflowExecutableSourceReferenceComparer.SameIdentity(actual, reference)) throw Unsupported();
            return new(true, WorkflowActivationOutcome.AlreadyActive, current, actual);
        }
        if (current.Revision != command.ExpectedRevision)
            return new(false, WorkflowActivationOutcome.Conflict, current, Conflict: WorkflowActivationConflict.RevisionMismatch);
        WorkflowActivationResult? result = null;
        await leases.ExecuteFencedAsync([physical.Identity], "model-publication:" + activation + ":" + Guid.NewGuid().ToString("N"), async (grants, token) =>
        {
            RequireCallScope();
            // The source owner returns the actual winning snapshot; preparation consumes that value, not caller proof strings.
            var prior = await references.FindAsync(reference.SourceReferenceId, token); RequireCallScope();
            if (prior is not null)
            {
                var original = reference with { CreatedAt = prior.CreatedAt, PublishedAt = prior.PublishedAt };
                if (!WorkflowExecutableSourceReferenceComparer.SameIdentity(prior, original)) throw Unsupported();
                if (prior.DeletedAt is not null)
                {
                    if (prior.DeletedReason != WorkflowActivationCoordinator.FailedRetireReason ||
                        !await references.TryRestoreAsync(prior, prior with { DeletedAt = null, DeletedReason = null }, token))
                        throw Unsupported();
                    RequireCallScope();
                }
                reference = original;
            }
            var saved = await references.SaveWithRootWriteLeasesAsync(reference, physical.Identity, grants, token);
            RequireCallScope();
            try
            {
                await bindings.PrepareActivationAsync(activation, [], token); RequireCallScope();
                if (schedules is not null) await schedules.PrepareActivationAsync(activation, [], token);
                RequireCallScope();
                await interests.PrepareModelAsync(canonicalModel, saved, command.SlotName, token); RequireCallScope();
                var transition = await activationSwitch.TryActivateAsync(new(saved.DefinitionId, command.SlotName, activation,
                    command.Source, command.ExpectedRevision, clock.GetUtcNow()), token);
                RequireCallScope();
                if (!transition.Succeeded)
                {
                    await activationSwitch.TryDiscardAsync(saved, CancellationToken.None);
                    result = new(false, WorkflowActivationOutcome.Conflict, transition.Slot, saved,
                        Conflict: transition.Conflict, Diagnostic: transition.Diagnostic); return;
                }
                result = new(true, WorkflowActivationOutcome.Activated, transition.Slot, saved, transition.ReplacedActivationId);
            }
            catch
            {
                // Never reconcile or compensate through a different current scope after an awaited boundary.
                RequireCallScope();
                // A committed switch cannot be recalled by caller cancellation/acknowledgement loss. Reconcile only
                // actual slot + all selected projection states. A replaced root is retained for its successor's revert.
                var observed = await authority.FindAsync(saved.DefinitionId, command.SlotName, CancellationToken.None);
                RequireCallScope();
                if (observed?.ActiveActivationId == activation)
                {
                    await RequireServingAsync(activation, CancellationToken.None);
                    result = new(true, WorkflowActivationOutcome.AlreadyActive, observed,
                        await references.FindAsync(saved.SourceReferenceId, CancellationToken.None)); return;
                }
                var state = await interests.FindStateAsync(activation, CancellationToken.None);
                RequireCallScope();
                if (state != WorkflowActivationProjectionState.Replaced)
                    await activationSwitch.TryDiscardAsync(saved, CancellationToken.None);
                throw;
            }
        }, ct);
        RequireCallScope(); return result ?? throw Unsupported();
    }
    internal async ValueTask<WorkflowActivationResult> DeactivateAsync(WorkflowExecutableSourceReference requested,
        string slotName, long expectedRevision, WorkflowActivationSource source, CancellationToken ct = default)
    {
        RequireComposition(); ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        var callScope = access.Current.RequireScope(); access.Current.EnsureTenantScope(requested.TenantId);
        void RequireCallScope()
        { RequireComposition(); if (access.Current.RequireScope() != callScope) throw Unsupported(); access.Current.EnsureTenantScope(requested.TenantId); }
        var slot = await authority.FindAsync(requested.DefinitionId, slotName, ct) ?? Empty(requested.DefinitionId, slotName);
        RequireCallScope();
        if (slot.ActiveActivationId is null) return new(true, WorkflowActivationOutcome.AlreadyInactive, slot);
        if (slot.ActiveActivationId != requested.ActivationId || slot.Revision != expectedRevision)
            return new(false, WorkflowActivationOutcome.Conflict, slot, Conflict: WorkflowActivationConflict.RevisionMismatch);
        await RequireServingAsync(slot.ActiveActivationId, ct); RequireCallScope();
        var transition = await activationSwitch.TryDeactivateAsync(new(requested.DefinitionId, slotName, source, expectedRevision, clock.GetUtcNow()), [], ct);
        RequireCallScope();
        return new(transition.Succeeded, transition.Succeeded ? WorkflowActivationOutcome.Deactivated : WorkflowActivationOutcome.Conflict,
            transition.Slot, ReplacedActivationId: transition.ReplacedActivationId, Conflict: transition.Conflict, Diagnostic: transition.Diagnostic);
    }
    private async ValueTask RequireServingAsync(string activation, CancellationToken ct)
    {
        if (await interests.FindStateAsync(activation, ct) != WorkflowActivationProjectionState.Active ||
            await bindings.FindActivationStateAsync(activation, ct) != WorkflowActivationProjectionState.Active ||
            schedules is not null && await schedules.FindActivationStateAsync(activation, ct) != WorkflowActivationProjectionState.Active) throw Unsupported();
    }
    private static WorkflowActivationSlot Empty(string definition, string slot) => new(WorkflowActivationSlotIdentity.Create(definition, slot), definition, slot, null, null, 0, DateTimeOffset.MinValue);
    private static NotSupportedException Unsupported() => new("The selected model publication lifecycle or exact owning tuple is unavailable.");
}
