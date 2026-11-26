using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Services.Executables;

public sealed partial class WorkflowActivationCoordinator
{
    private readonly IWorkflowExecutableStore? _executableStore;

    /// <summary>Adds verified material reads while preserving the original native-only constructor.</summary>
    public WorkflowActivationCoordinator(
        IWorkflowExecutableStore executableStore,
        IWorkflowActivationAuthority authority,
        IWorkflowActivationSwitch activationSwitch,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
        TimeProvider timeProvider,
        IWorkflowTriggerIndexer? triggerIndexer = null,
        IWorkflowTriggerBindingStore? triggerBindingStore = null,
        IRecurringTriggerScheduleStore? recurringScheduleStore = null,
        IEnumerable<IWorkflowTriggerIndexObserver>? triggerObservers = null,
        ILogger<WorkflowActivationCoordinator>? logger = null,
        IOptions<ActivationChannelOptions>? activationChannelOptions = null)
        : this(authority, activationSwitch, sourceReferenceStore, rootWriteLeaseManager, timeProvider, triggerIndexer,
            triggerBindingStore, recurringScheduleStore, triggerObservers, logger, activationChannelOptions)
    {
        _executableStore = executableStore ?? throw new ArgumentNullException(nameof(executableStore));
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowActivationResult> ActivateMaterialAsync(
        WorkflowMaterialActivationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.WorkflowDefinitionId);
        ArgumentNullException.ThrowIfNull(command.Material);
        ArgumentNullException.ThrowIfNull(command.Reference);
        ArgumentNullException.ThrowIfNull(command.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ActivationId);
        ArgumentOutOfRangeException.ThrowIfNegative(command.ExpectedRevision);
        if (command.Reference.ArtifactId != command.Material.Identity.ArtifactId ||
            command.Reference.DefinitionId != command.WorkflowDefinitionId ||
            command.Reference.Scope != WorkflowExecutableReferenceScope.Published ||
            !command.Reference.IsLive(timeProvider.GetUtcNow()))
            throw new ArgumentException("Material activation requires live Published provenance for its logical definition and artifact.", nameof(command));

        var material = await ReadStoredMaterialAsync(command.Material, cancellationToken);
        if (!sourceReferenceStore.CanWriteRootReferences(_executableStore!))
            throw new NotSupportedException("Material activation requires the selected physical material and fenced source-reference stores.");
        return await ActivateCoreAsync(new(command.WorkflowDefinitionId, material.Identity, material.NativeWorkflow,
            material, command.Reference, command.SlotName, command.ActivationId, command.Source,
            command.ExpectedRevision, command.OwnershipIntent), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowActivationResult> DeactivateMaterialAsync(
        WorkflowMaterialDeactivationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.WorkflowDefinitionId);
        ArgumentNullException.ThrowIfNull(command.Material);
        ArgumentNullException.ThrowIfNull(command.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SlotName);
        ArgumentOutOfRangeException.ThrowIfNegative(command.ExpectedRevision);
        var material = await ReadStoredMaterialAsync(command.Material, cancellationToken);
        var slot = await authority.FindAsync(command.WorkflowDefinitionId, command.SlotName, cancellationToken);
        if (slot?.Revision == command.ExpectedRevision && slot.ActiveActivationId is not null && await FindLiveReferenceAsync(slot, cancellationToken) is { } source &&
            (source.ArtifactId != material.Identity.ArtifactId || source.DefinitionId != command.WorkflowDefinitionId))
            throw new ArgumentException("The supplied material does not match the logical slot's selected source.", nameof(command));
        return await DeactivateCoreAsync(new(command.WorkflowDefinitionId, material.Identity, material.NativeWorkflow,
            command.SlotName, command.Source, command.ExpectedRevision), cancellationToken);
    }

    private async ValueTask<RuntimeExecutionMaterial> ReadStoredMaterialAsync(
        RuntimeExecutionMaterial expected,
        CancellationToken cancellationToken)
    {
        if (_executableStore is null)
            throw new NotSupportedException("Material activation requires the coordinator's selected executable store.");
        var stored = await _executableStore.FindMaterialAsync(expected.Identity.ArtifactId, cancellationToken)
            ?? throw new WorkflowExecutableNotFoundException(expected.Identity.ArtifactId);
        if (stored.IncarnationId != expected.IncarnationId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(stored.Identity, expected.Identity) ||
            (stored.Model is null) != (expected.Model is null))
            throw new ArgumentException("The requested material pin, incarnation or execution model differs from the selected store.", nameof(expected));
        return stored;
    }

    private sealed record ActivationCandidate(
        string WorkflowDefinitionId,
        WorkflowExecutableIdentity Identity,
        WorkflowExecutable? NativeWorkflow,
        RuntimeExecutionMaterial? Material,
        WorkflowExecutableSourceReference Reference,
        string SlotName,
        string ActivationId,
        WorkflowActivationSource Source,
        long ExpectedRevision,
        WorkflowActivationOwnershipIntent OwnershipIntent);

    private sealed record DeactivationCandidate(
        string WorkflowDefinitionId,
        WorkflowExecutableIdentity Identity,
        WorkflowExecutable? NativeWorkflow,
        string SlotName,
        WorkflowActivationSource Source,
        long ExpectedRevision);
}
