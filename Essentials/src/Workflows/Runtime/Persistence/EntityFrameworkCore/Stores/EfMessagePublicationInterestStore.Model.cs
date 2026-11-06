using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfMessagePublicationInterestStore
{
    internal MessageModelPublicationCoordinator CreateModelCoordinator(EfWorkflowActivationSwitch activationSwitch,
        IWorkflowExecutableRootWriteLeaseManager leases, IWorkflowTriggerBindingStore bindings,
        IRecurringTriggerScheduleStore? schedules = null) => new(this, authority, activationSwitch, material, references, leases, bindings, access, clock, schedules);
    void IMessagePublicationInterestProjection.RequireModelComposition(IWorkflowExecutableStore selected,
        IWorkflowExecutableRootWriteLeaseManager leases, TimeProvider selectedClock, IWorkflowActivationSwitch selectedSwitch,
        IWorkflowTriggerBindingStore bindings, IRecurringTriggerScheduleStore? schedules, IPersistenceAccessContextAccessor selectedAccess)
    {
        RequireScope();
        if (!ReferenceEquals(selected.GetMaterialStore(), material) || !ReferenceEquals(selectedClock, clock) || !ReferenceEquals(selectedAccess, access) ||
            !references.UsesModelPublicationClock(clock) ||
            leases is not WorkflowExecutableRootWriteLeaseManager actual || !actual.UsesMessageInterestTuple(material, clock) ||
            selectedSwitch is not EfWorkflowActivationSwitch physical) throw Unsupported();
        physical.RequireModelPublicationTuple(this, bindings, schedules, clock);
    }
    ValueTask IMessagePublicationInterestProjection.PrepareModelAsync(RuntimeModelMaterial requested,
        WorkflowExecutableSourceReference expected, string slotName, CancellationToken ct) => PrepareModelAsync(requested, expected, slotName, ct);
    internal async ValueTask PrepareModelAsync(RuntimeModelMaterial requested, WorkflowExecutableSourceReference expected, string slotName, CancellationToken ct)
    {
        var scope = RequireScope(); var capturedAccess = access.Current; RequireCleanContext(); _ = MessageInterestWriteVersion.For(context);
        void RequireCapturedAccess()
        {
            if (access.Current != capturedAccess || RequireScope() != scope) throw Unsupported();
            access.Current.EnsureTenantScope(expected.TenantId);
        }
        RequireCapturedAccess();
        var activation = expected.ActivationId ?? throw Unsupported();
        ArgumentException.ThrowIfNullOrWhiteSpace(activation);
        await Writes.RunUntilSettledAsync<bool>(context, async () =>
        {
            RequireCapturedAccess(); context.ChangeTracker.Clear(); await using var transaction = await context.Database.BeginTransactionAsync(ct);
            RequireCapturedAccess();
            try
            {
                var source = await references.ReadPhysicalMaintenanceSourceAsync(scope, WorkflowActivationReferenceIdentity.Create(activation), ct)
                    ?? throw new InvalidOperationException("The canonical model activation reference is missing.");
                RequireCapturedAccess();
                var current = await ((IWorkflowExecutableStore)material).FindMaterialAsync(source.Value.ArtifactId, ct) ?? throw Unsupported();
                RequireCapturedAccess();
                if (current.Model is null || current.Model.Dependencies.Count != 0 ||
                    current.Identity.ArtifactId != requested.Identity.ArtifactId || current.Identity.ArtifactHash != requested.Identity.ArtifactHash ||
                    requested.ComputeContentHash() != current.Identity.ArtifactHash ||
                    MessagePublicationSourceFacts.From(source.Value) != MessagePublicationSourceFacts.From(expected) ||
                    expected.Scope != WorkflowExecutableReferenceScope.Published || expected.ExpiresAt is not null ||
                    expected.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(activation) ||
                    expected.SlotId != WorkflowActivationSlotIdentity.Create(expected.DefinitionId, slotName)) throw Unsupported();
                access.Current.EnsureTenantScope(source.Value.TenantId);
                var declarations = extractor.Extract(current).Declarations.Select(item =>
                    new MessagePublicationDeclaration(item.ReceiverAddress, item.MessageNames.ToArray(), item.CanStartWorkflow)).ToArray();
                var descriptor = new MessagePublicationDescriptor(new(new(scope), source.Value.TenantId), MessageDefinitionIdentity.WorkflowDefinitionNamespace,
                    activation, expected.SlotId!, slotName, MessagePublicationSourceFacts.From(source.Value), source.Row.IncarnationId,
                    current.Identity.ArtifactHash, current.IncarnationId, declarations, IsModel: true);
                MessagePublicationInterestDocument.Validate(descriptor);
                var existing = await RootAsync(scope, activation, ct); RequireCapturedAccess();
                var old = existing is null ? null : ReadRoot(existing, scope, activation);
                if (old is not null && existing!.DescriptorFingerprint != MessagePublicationInterestDocument.Fingerprint(descriptor))
                    throw new InvalidOperationException("The immutable model publication preparation differs.");
                if (old?.State is (int)WorkflowActivationProjectionState.Active or (int)WorkflowActivationProjectionState.Replaced) throw Unsupported();
                var id = MessagePublicationInterestDocument.ScopeId(descriptor.Scope);
                var control = await context.MessageInterestScopeControls.SingleOrDefaultAsync(row => row.Id == id, ct); RequireCapturedAccess();
                if (control is null) context.MessageInterestScopeControls.Add(NewControl(descriptor.Scope));
                else _ = MessagePublicationInterestDocument.Read(control, descriptor.Scope);
                if (existing is null) context.MessagePublicationInterests.Add(NewRoot(descriptor));
                else if (old!.State == MessagePublicationInterestDocument.Closed) SetState(existing, old, (int)WorkflowActivationProjectionState.Prepared, old.ClosedAtEpoch);
                RequireCapturedAccess();
                try
                {
                    await context.SaveChangesAsync(ct); RequireCapturedAccess();
                    await transaction.CommitAsync(ct); RequireCapturedAccess();
                    context.ChangeTracker.Clear(); return true;
                }
                catch (Exception error) when (EfRelationalExceptionClassifier.IsProviderFailure(error))
                {
                    await context.RollbackAndClearAsync(transaction); await transaction.DisposeAsync(); RequireCapturedAccess();
                    return Writes.ShouldRetry(context, error) ? EfWriteAttempt<bool>.Retry(error) :
                        throw EfRuntimeOperationalStoreSupport.CommitFailure("preparing Message model publication interest", error);
                }
            }
            catch { await context.RollbackAndClearAsync(transaction); throw; }
        }, conflict => throw new InvalidOperationException("The model publication preparation changed concurrently.", conflict), ct);
    }
}
