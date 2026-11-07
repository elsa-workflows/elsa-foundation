using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Unregistered native checkpoint participant, never a cohort or start-authority API.</summary>
/// <remarks>Only the actual selected volatile tuple may install this participant, before any executions exist.
/// Initial source attribution comes from the retained canonical reference selected by the runtime, not the
/// content store's historical source carrier. Model workers/embedded ownership and backfill remain unsupported.
/// Existing generic in-memory participants do not provide rollback: any failure after mutation starts permanently
/// makes this projection unavailable. No Message acceptance capability is enabled by this component.</remarks>
internal sealed class InMemoryMessageActiveInterestStore
{
    private readonly InMemoryRuntimeCheckpointStoreState checkpointState;
    private readonly InMemoryWorkflowExecutionStateStore executions;
    private readonly InMemoryExecutionLivenessStateStore liveness;
    private readonly InMemoryWorkflowExecutableSourceReferenceStore references;
    private readonly InMemoryWorkflowExecutableStore material;
    private readonly IPersistenceAccessContextAccessor access;
    private readonly MessageMaterialInterestExtractor extractor;
    private readonly TimeProvider clock;
    private readonly InMemoryMessagePublicationInterestStore.SharedState shared;
    private readonly Dictionary<string, ActiveRoot> controls = new(StringComparer.Ordinal);
    private bool uncertain;

    private InMemoryMessageActiveInterestStore(InMemoryRuntimeCheckpointCommitStore checkpoints,
        InMemoryWorkflowExecutionStateStore executions, InMemoryExecutionLivenessStateStore liveness,
        InMemoryWorkflowExecutableSourceReferenceStore references, InMemoryWorkflowExecutableStore material,
        IPersistenceAccessContextAccessor access, MessageMaterialInterestExtractor extractor, TimeProvider clock,
        InMemoryMessagePublicationInterestStore.SharedState shared)
    {
        checkpointState = checkpoints.State; this.executions = executions;
        this.liveness = liveness; this.references = references; this.material = material; this.access = access;
        this.extractor = extractor; this.clock = clock; this.shared = shared;
    }

    internal static ValueTask<InMemoryMessageActiveInterestStore> CreateAsync(
        InMemoryRuntimeCheckpointCommitStore checkpoints, InMemoryWorkflowExecutionStateStore executions,
        InMemoryExecutionLivenessStateStore liveness, InMemoryWorkflowExecutableSourceReferenceStore references,
        InMemoryWorkflowExecutableStore material, IPersistenceAccessContextAccessor access,
        MessageMaterialInterestExtractor extractor, TimeProvider clock, CancellationToken ct = default) =>
        checkpoints.State.ExecuteWriteGateAsync(_ =>
        {
            checkpoints.RequireMessageActiveComposition(executions, liveness, references, material, access, clock);
            var shared = checkpoints.State.MessagePublicationInterests
                ?? throw new NotSupportedException("The actual publication interest participant is missing.");
            if (shared.Scope != access.Current.RequireScope() || !ReferenceEquals(shared.References, references) ||
                !ReferenceEquals(shared.Material, material))
                throw new NotSupportedException("The native interest tuple differs from the publication owner.");
            if (checkpoints.State.MessageActiveInterests is { } existing)
            {
                existing.RequireTuple(checkpoints, executions, liveness, references, material, access, extractor, clock);
                existing.RequireHealthy();
                return ValueTask.FromResult(existing);
            }
            // No scan can manufacture an incarnation or prove that an existing execution was enrolled atomically.
            if (executions.HasMessageInterestHistory)
                throw new NotSupportedException("Existing native history requires an explicit interest backfill protocol.");
            var owner = new InMemoryMessageActiveInterestStore(checkpoints, executions, liveness, references,
                material, access, extractor, clock, shared);
            checkpoints.State.MessageActiveInterests = owner;
            return ValueTask.FromResult(owner);
        }, ct);

    private void RequireTuple(InMemoryRuntimeCheckpointCommitStore selected,
        InMemoryWorkflowExecutionStateStore selectedExecutions, InMemoryExecutionLivenessStateStore selectedLiveness,
        InMemoryWorkflowExecutableSourceReferenceStore selectedReferences, InMemoryWorkflowExecutableStore selectedMaterial,
        IPersistenceAccessContextAccessor selectedAccess, MessageMaterialInterestExtractor selectedExtractor, TimeProvider selectedClock)
    {
        if (!ReferenceEquals(checkpointState, selected.State) || !ReferenceEquals(executions, selectedExecutions) ||
            !ReferenceEquals(liveness, selectedLiveness) || !ReferenceEquals(references, selectedReferences) ||
            !ReferenceEquals(material, selectedMaterial) || !ReferenceEquals(access, selectedAccess) ||
            !ReferenceEquals(extractor, selectedExtractor) || !ReferenceEquals(clock, selectedClock))
            throw new NotSupportedException("The selected native interest tuple changed.");
    }

    internal void RequireHealthy()
    {
        if (access.Current.RequireScope() != shared.Scope)
            throw new InvalidOperationException("The native interest persistence scope changed.");
        lock (shared.Gate)
            if (uncertain) throw new NotSupportedException("Native interest consistency is unavailable after an incomplete physical commit.");
    }

    // Invoked only by the real checkpoint writer, under its write and actual ownership gates, before any effects.
    internal async ValueTask<PreparedWrite?> PrepareAsync(RuntimeCheckpointCommit commit, CancellationToken ct)
    {
        RequireHealthy();
        if (commit.StateChanges.WorkflowExecution is not { } change) return null;
        if (!checkpointState.IsWriteGateHeld || commit.ExpectedFence is null ||
            change.Operation != RuntimeStateChangeOperation.Upsert || change.StateId != commit.WorkflowExecutionId ||
            change.State.WorkflowExecutionId != commit.WorkflowExecutionId)
            throw new NotSupportedException("Managed native interests require an actual fenced workflow upsert.");
        var state = change.State;
        if (!Enum.IsDefined(state.Status) || state.RunKind != WorkflowRunKind.PublishedRun || state.TestScope is not null ||
            state.ModelWorkOrigin is not null || state.ParentWorkflowExecutionId is not null ||
            state.PinnedSource is null || state.Partition is null || state.Authority is null)
            throw new NotSupportedException("This native interest profile requires a top-level authored published execution.");
        access.Current.EnsureTenantScope(state.TenantId);
        var ownerState = await liveness.FindAsync(state.WorkflowExecutionId,
            RuntimeExecutionOwnershipStateId.For(state.WorkflowExecutionId), ct);
        if (ownerState?.Metadata.ContainsKey(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone) == true)
            throw new InvalidOperationException("The native execution is deletion-fenced.");
        RuntimeExecutionFenceValidator.EnsureCurrent(state.WorkflowExecutionId, commit.ExpectedFence, ownerState, clock.GetUtcNow());

        ActiveRoot? previous;
        lock (shared.Gate) controls.TryGetValue(state.WorkflowExecutionId, out previous);
        var actual = executions.ReadForMessageInterest(state.WorkflowExecutionId);
        ActiveRoot next;
        if (previous is null)
        {
            if (actual is not null) throw new NotSupportedException("The actual native history has no create-only interest control.");
            var source = await references.FindAsync(state.PinnedSource.SourceReferenceId, ct)
                ?? throw new InvalidOperationException("The retained canonical native source is unavailable.");
            if (source.Scope != WorkflowExecutableReferenceScope.Published || source.SourceKind != "WorkflowDefinitionVersion" ||
                string.IsNullOrWhiteSpace(source.ActivationId) || string.IsNullOrWhiteSpace(source.SlotId) ||
                source.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(source.ActivationId) ||
                source.ArtifactId != state.PinnedExecutable.ArtifactId || source.TenantId != state.TenantId ||
                WorkflowExecutableSourceProvenance.From(source) != state.PinnedSource)
                throw new NotSupportedException("The pinned native source does not match its canonical retained owner.");
            var currentMaterial = ReadMaterial(state.PinnedExecutable);
            // Retired/replaced references are deliberately permitted: the physical start checkpoint may be coalesced.
            var declarations = extractor.Extract(currentMaterial).Declarations;
            var scope = new MessageScope(shared.Scope, state.TenantId);
            next = new(Guid.NewGuid().ToString("N"), scope,
                MessageDefinitionIdentity.ForWorkflowDefinition(scope, source.DefinitionId), CaptureOwner(state),
                currentMaterial.IncarnationId, declarations, state.Status.IsTerminal());
        }
        else
        {
            if (actual is null || !SameOwner(previous.Owner, actual) || !SameOwner(previous.Owner, state) ||
                previous.Closed != actual.Status.IsTerminal() ||
                previous.Closed && (!state.Status.IsTerminal() || state.Status != actual.Status))
                throw new InvalidOperationException("The native interest incarnation or immutable execution owner differs.");
            var currentMaterial = ReadMaterial(previous.Owner.PinnedExecutable);
            if (currentMaterial.IncarnationId != previous.MaterialIncarnation)
                throw new InvalidOperationException("The pinned native material incarnation changed.");
            next = previous with { Closed = state.Status.IsTerminal() };
        }
        // Everything that may parse/extract/validate or overflow happens before scheduler consumption/state mutation.
        ct.ThrowIfCancellationRequested(); RequireHealthy();
        RuntimeExecutionFenceValidator.EnsureCurrent(state.WorkflowExecutionId, commit.ExpectedFence, ownerState, clock.GetUtcNow());
        lock (shared.Gate)
        {
            var changesEligibility = previous is null ? !next.Closed : previous.Closed != next.Closed;
            var nextEpoch = changesEligibility ? checked(shared.Epochs.GetValueOrDefault(next.Scope) + 1) : (long?)null;
            return new(this, state, next, nextEpoch);
        }
    }

    private RuntimeExecutionMaterial ReadMaterial(WorkflowExecutableIdentity pin)
    {
        var current = material.ReadNativeMaterialForMessageInterest(pin.ArtifactId)
            ?? throw new InvalidOperationException("The pinned native material is unavailable.");
        // The material's historical DefinitionId/version carrier is not its current logical owner.
        if (current.Identity.ArtifactHash != pin.ArtifactHash || current.NativeWorkflow!.Dependencies.Count != 0)
            throw new NotSupportedException("The native material or dependency ownership is unsupported.");
        return current;
    }

    internal void Apply(PreparedWrite prepared)
    {
        // No external provider, cancellation, extraction or epoch calculation is allowed in this physical stage.
        if (!ReferenceEquals(prepared.Owner, this))
            throw new InvalidOperationException("The prepared native write belongs to another provider owner.");
        lock (shared.Gate)
        {
            executions.SaveForMessageInterest(prepared.State, this);
            controls[prepared.State.WorkflowExecutionId] = prepared.Root;
            if (prepared.NextEpoch is { } epoch) shared.Epochs[prepared.Root.Scope] = epoch;
        }
    }

    internal void MarkUncertain()
    {
        lock (shared.Gate) uncertain = true;
    }

    internal void EnsureDeletionCanProceed(WorkflowExecutionState actual)
    {
        RequireHealthy();
        lock (shared.Gate)
            if (!actual.Status.IsTerminal() || !controls.TryGetValue(actual.WorkflowExecutionId, out var control) ||
                !control.Closed || !SameOwner(control.Owner, actual))
                throw new NotSupportedException("The native interest control has not durably closed for this execution.");
        // Retain the closed create-only control after history deletion; an ID cannot reopen a prior incarnation.
    }

    internal ValueTask<(long Epoch, IReadOnlyList<ActiveRoot> Roots)> ObserveAsync(MessageScope scope, CancellationToken ct = default) =>
        checkpointState.ExecuteWriteGateAsync(_ =>
        {
            RequireHealthy();
            if (scope.PersistenceScope != shared.Scope) throw new InvalidOperationException("The native interest scope differs.");
            access.Current.EnsureTenantScope(scope.TenantId);
            lock (shared.Gate)
            {
                var active = controls.Values.Where(root => root.Scope == scope && !root.Closed).ToArray();
                try
                {
                    foreach (var root in active)
                    {
                        var actual = executions.ReadForMessageInterest(root.Owner.WorkflowExecutionId);
                        if (actual is null || actual.Status.IsTerminal() || !SameOwner(root.Owner, actual) ||
                            ReadMaterial(root.Owner.PinnedExecutable).IncarnationId != root.MaterialIncarnation)
                            throw new InvalidOperationException("The native interest root and physical execution/material differ.");
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
                {
                    uncertain = true;
                    throw new NotSupportedException("Native interest consistency is unavailable for the actual execution/material.");
                }
                return ValueTask.FromResult<(long, IReadOnlyList<ActiveRoot>)>((shared.Epochs.GetValueOrDefault(scope), active));
            }
        }, ct);

    // Retain only owner facts, never a variable-frame/payload snapshot or mutable diagnostic metadata.
    private static WorkflowExecutionState CaptureOwner(WorkflowExecutionState state) => state with
    { CorrelationId = null, SystemMetadata = RuntimeModelMetadata.Snapshot(null), RootVariableFrame = null };

    private static bool SameOwner(WorkflowExecutionState left, WorkflowExecutionState right) =>
        left.WorkflowExecutionId == right.WorkflowExecutionId && left.PinnedExecutable == right.PinnedExecutable &&
        left.PinnedSource == right.PinnedSource && left.TenantId == right.TenantId && left.RunKind == right.RunKind &&
        left.Partition == right.Partition && left.TestScope == right.TestScope && left.ModelWorkOrigin == right.ModelWorkOrigin &&
        left.ParentWorkflowExecutionId == right.ParentWorkflowExecutionId && left.Authority is { } authority &&
        right.Authority is { } other && WorkflowExecutionAuthoritySnapshot.Matches(authority, other.SystemIdentity, other.RootInitiator, other.Metadata);

    internal sealed class PreparedWrite(InMemoryMessageActiveInterestStore owner, WorkflowExecutionState state, ActiveRoot root, long? nextEpoch)
    {
        internal InMemoryMessageActiveInterestStore Owner { get; } = owner;
        internal WorkflowExecutionState State { get; } = state;
        internal ActiveRoot Root { get; } = root;
        internal long? NextEpoch { get; } = nextEpoch;
        public override string ToString() => nameof(PreparedWrite);
    }
    internal sealed record ActiveRoot(string Incarnation, MessageScope Scope, MessageDefinitionIdentity Definition,
        WorkflowExecutionState Owner, string MaterialIncarnation, IReadOnlyList<MessageMaterialInterestDeclaration> Declarations, bool Closed)
    { public override string ToString() => nameof(ActiveRoot); }
}
