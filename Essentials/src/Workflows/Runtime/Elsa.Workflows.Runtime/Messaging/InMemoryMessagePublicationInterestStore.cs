using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Unregistered volatile publication projection, driven only by the actual activation switch.</summary>
/// <remarks>Complete active/checkpoint/BPMN/EF coverage and low-level owner-write fencing are incomplete. It cannot freeze
/// accepted cohorts or advertise Message capability. Existing in-memory authorities are unpartitioned, so this
/// composition is bound to one actual scope and refuses scope drift rather than claiming multi-scope isolation.</remarks>
internal sealed class InMemoryMessagePublicationInterestStore : IInMemoryActivationProjection, IMessagePublicationInterestProjection
{
    private readonly InMemoryRuntimeCheckpointStoreState checkpointState;
    private readonly InMemoryWorkflowActivationAuthority authority;
    private readonly InMemoryWorkflowExecutableSourceReferenceStore references;
    private readonly InMemoryWorkflowExecutableStore material;
    private readonly IPersistenceAccessContextAccessor access;
    private readonly MessageMaterialInterestExtractor extractor;
    private readonly TimeProvider clock;
    private readonly PersistenceScope boundScope;
    private readonly SharedState shared;
    private object gate => shared.Gate;
    private InMemoryActivationProjectionStates states => shared.States;
    private Dictionary<string, Root> roots => shared.Roots;
    private Dictionary<MessageScope, long> epochs => shared.Epochs;

    internal InMemoryMessagePublicationInterestStore(InMemoryRuntimeCheckpointCommitStore checkpoints,
        InMemoryWorkflowActivationAuthority authority, InMemoryWorkflowExecutableSourceReferenceStore references,
        InMemoryWorkflowExecutableStore material, IPersistenceAccessContextAccessor access,
        MessageMaterialInterestExtractor extractor, TimeProvider clock)
    {
        checkpointState = checkpoints.State; this.authority = authority; this.references = references; this.material = material;
        this.access = access; this.extractor = extractor; this.clock = clock; boundScope = access.Current.RequireScope();
        if (!ReferenceEquals(references.FencedArtifactStore, material))
            throw new NotSupportedException("Publication interest requires the actual shared artifact/reference boundary.");
        lock (checkpointState.SyncRoot)
        {
            shared = checkpointState.MessagePublicationInterests ??= new(boundScope, authority, references, material);
            if (shared.Scope != boundScope || !ReferenceEquals(shared.Authority, authority) ||
                !ReferenceEquals(shared.References, references) || !ReferenceEquals(shared.Material, material))
                throw new NotSupportedException("The checkpoint state already belongs to a different publication interest tuple.");
        }
    }

    internal void RequireComposition(IWorkflowActivationAuthority actualAuthority, IWorkflowExecutableSourceReferenceStore actualReferences)
    {
        RequireScope();
        if (!ReferenceEquals(authority, actualAuthority) || !ReferenceEquals(references, actualReferences))
            throw new NotSupportedException("Publication interest requires its actual activation/reference tuple.");
    }

    void IMessagePublicationInterestProjection.RequireActualComposition(IWorkflowActivationAuthority actualAuthority,
        IWorkflowActivationSwitch activationSwitch, IWorkflowExecutableSourceReferenceStore actualReferences)
    {
        RequireComposition(actualAuthority, actualReferences);
        if (activationSwitch is not InMemoryWorkflowActivationSwitch physical || !physical.UsesMessageInterests(this))
            throw new NotSupportedException("Publication preparation requires the actual matching activation switch.");
    }
    ValueTask IMessagePublicationInterestProjection.PrepareAsync(WorkflowExecutable requested, string activationId, string slotId,
        string slotName, CancellationToken ct) => PrepareAsync(requested, activationId, slotId, slotName, ct);
    ValueTask<WorkflowActivationProjectionState> IMessagePublicationInterestProjection.FindStateAsync(string activationId, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(State(activationId)); }
    ValueTask<IReadOnlyCollection<string>> IMessagePublicationInterestProjection.ListServingActivationIdsAsync(string slotId, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(ServingForSlot(slotId)); }

    internal ValueTask<T> UnderWriteGateAsync<T>(Func<ValueTask<T>> write, CancellationToken ct) =>
        checkpointState.ExecuteWriteGateAsync(_ =>
        {
            RequireScope();
            // One switch plus closing every retained root is a conservative bound for any of the five
            // activation operations (including legacy repair/alsoServing batches). Check BEFORE authority,
            // reference or projection mutation; repeated checked increments cannot overflow mid-operation.
            lock (gate)
            {
                var maximumBumps = (long)roots.Count + 1;
                if (epochs.Values.Any(value => value > long.MaxValue - maximumBumps))
                    throw new InvalidOperationException("The interest epoch cannot advance for the complete operation.");
            }
            return write();
        }, ct);

    internal async ValueTask PrepareAsync(WorkflowExecutable requested, string activationId, string slotId, string slotName, CancellationToken ct)
    {
        await UnderWriteGateAsync(async () =>
        {
            var reference = await references.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId), ct)
                ?? throw new InvalidOperationException("The canonical activation reference is missing.");
            var current = material.ReadNativeMaterialForMessageInterest(reference.ArtifactId)
                ?? throw new InvalidOperationException("The canonical native material is missing.");
            if (reference.Scope != WorkflowExecutableReferenceScope.Published || reference.ExpiresAt is not null ||
                reference.ActivationId != activationId || reference.SlotId != slotId ||
                WorkflowActivationSlotIdentity.Create(reference.DefinitionId, slotName) != slotId ||
                reference.DefinitionId != requested.Identity.DefinitionId || reference.DefinitionVersionId != requested.Identity.DefinitionVersionId ||
                reference.ArtifactVersion != requested.Identity.ArtifactVersion || current.Identity.ArtifactId != requested.Identity.ArtifactId ||
                current.Identity.ArtifactHash != requested.Identity.ArtifactHash || current.NativeWorkflow!.Dependencies.Count != 0)
                throw new NotSupportedException("The native publication interest profile or owner is unsupported.");
            access.Current.EnsureTenantScope(reference.TenantId);
            ArgumentException.ThrowIfNullOrWhiteSpace(current.Identity.ArtifactHash);
            var declarations = extractor.Extract(current).Declarations;
            var scope = new MessageScope(boundScope, reference.TenantId);
            var definition = MessageDefinitionIdentity.ForWorkflowDefinition(scope, reference.DefinitionId);
            var fingerprint = Fingerprint(scope, reference, current, declarations);
            lock (gate)
            {
                if (roots.TryGetValue(activationId, out var prior) && prior.Fingerprint != fingerprint)
                    throw new InvalidOperationException("The immutable prepared publication interest differs.");
                states.Prepare(activationId, "Message-interest");
                roots[activationId] = new(scope, definition, reference, slotName, current.Identity.ArtifactHash, current.IncarnationId, declarations, fingerprint, false)
                    { ClosedAtEpoch = prior?.ClosedAtEpoch };
            }
            return true;
        }, ct);
    }

    object IInMemoryActivationProjection.SyncRoot => gate;
    WorkflowActivationProjectionState IInMemoryActivationProjection.State(string activationId) => State(activationId);
    internal WorkflowActivationProjectionState State(string activationId)
    {
        RequireScope();
        lock (gate)
        {
            var state = states.Find(activationId);
            if (state == WorkflowActivationProjectionState.Active) AssertCurrentRoot(roots[activationId]);
            return state;
        }
    }
    internal IReadOnlyCollection<string> ServingForSlot(string slotId)
    {
        RequireScope();
        lock (gate) return roots.Where(item => item.Value.Reference.SlotId == slotId && states.Find(item.Key) == WorkflowActivationProjectionState.Active)
            .Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
    }

    void IInMemoryActivationProjection.CheckSwitch(string activationId, string? replacedActivationId)
    {
        RequireScope();
        _ = states.NeedsSwitch(activationId, replacedActivationId, "Message-interest");
        AssertCurrentRoot(roots[activationId]);
    }

    private void AssertCurrentRoot(Root root)
    {
        var current = material.ReadNativeMaterialForMessageInterest(root.Reference.ArtifactId);
        var valid = references.ExecuteUnderReferenceGate(all => all.TryGetValue(root.Reference.SourceReferenceId, out var reference) &&
            reference.IsLive(clock.GetUtcNow()) && SameOwner(root.Reference, reference));
        if (!valid || current is null || current.IncarnationId != root.MaterialIncarnation || current.Identity.ArtifactHash != root.ReferenceArtifactHash)
            throw new InvalidOperationException("The canonical publication root moved before activation.");
    }

    void IInMemoryActivationProjection.Switch(string activationId, string? replacedActivationId)
    {
        if (!states.Activate(activationId, replacedActivationId, "Message-interest")) return;
        var affected = new HashSet<MessageScope> { roots[activationId].Scope };
        if (replacedActivationId is not null && replacedActivationId != activationId) affected.Add(roots[replacedActivationId].Scope);
        foreach (var scope in affected) epochs[scope] = checked(epochs.GetValueOrDefault(scope) + 1);
    }

    void IInMemoryActivationProjection.Delete(string activationId)
    {
        if (!roots.TryGetValue(activationId, out var root)) return;
        if (states.Find(activationId) == WorkflowActivationProjectionState.Active)
            epochs[root.Scope] = checked(epochs.GetValueOrDefault(root.Scope) + 1);
        states.Remove(activationId); roots[activationId] = root with { Closed = true, ClosedAtEpoch = epochs.GetValueOrDefault(root.Scope) };
    }

    // Inspection of this publication-only slice, NOT a cohort/selection API. Join the real slot/ref before reporting names.
    internal ValueTask<(long Epoch, IReadOnlyList<string> Names)> ObservePublicationAsync(MessageScope scope, CancellationToken ct = default) =>
        UnderWriteGateAsync(() =>
        {
            checkpointState.MessageActiveInterests?.RequireHealthy();
            if (scope.PersistenceScope != boundScope) throw new InvalidOperationException("The publication scope differs.");
            lock (authority.Gate)
            lock (gate)
            {
                var names = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var pair in roots.Where(item => item.Value.Scope == scope && states.Find(item.Key) == WorkflowActivationProjectionState.Active))
                {
                    var root = pair.Value;
                    AssertCurrentRoot(root);
                    var currentSlot = authority.Current(root.Reference.DefinitionId, root.SlotName);
                    if (currentSlot?.ActiveActivationId != pair.Key) throw new InvalidOperationException("The serving root and actual slot differ.");
                    var live = references.ExecuteUnderReferenceGate(all => all.TryGetValue(root.Reference.SourceReferenceId, out var current) &&
                        current.IsLive(clock.GetUtcNow()) && SameOwner(root.Reference, current));
                    if (!live) throw new InvalidOperationException("The serving canonical reference is unavailable.");
                    foreach (var name in root.Declarations.SelectMany(item => item.MessageNames)) names.Add(name);
                }
                return ValueTask.FromResult<(long, IReadOnlyList<string>)>((epochs.GetValueOrDefault(scope), names.ToArray()));
            }
        }, ct);

    internal void RequireCompleteAcceptanceCoverage() =>
        throw new NotSupportedException("Active/checkpoint and model interest coverage is incomplete; cohort acceptance is unavailable.");

    private void RequireScope()
    {
        if (access.Current.RequireScope() != boundScope) throw new InvalidOperationException("The selected volatile publication scope changed.");
    }
    private static bool SameOwner(WorkflowExecutableSourceReference left, WorkflowExecutableSourceReference right) =>
        left.SourceReferenceId == right.SourceReferenceId && left.ArtifactId == right.ArtifactId && left.SourceKind == right.SourceKind &&
        left.SourceId == right.SourceId && left.SourceVersion == right.SourceVersion && left.DefinitionId == right.DefinitionId &&
        left.DefinitionVersionId == right.DefinitionVersionId && left.ArtifactVersion == right.ArtifactVersion &&
        left.Scope == right.Scope && left.ExpiresAt == right.ExpiresAt && left.ActivationId == right.ActivationId &&
        left.SlotId == right.SlotId && left.TenantId == right.TenantId;
    private static string Fingerprint(MessageScope scope, WorkflowExecutableSourceReference reference, RuntimeExecutionMaterial material,
        IReadOnlyList<MessageMaterialInterestDeclaration> declarations)
    {
        var text = new StringBuilder("elsa.message-publication-interest/v1");
        void Add(string? value) { if (value is null) text.Append("-:"); else text.Append(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(value); }
        Add(scope.PersistenceScope.Value); Add(scope.TenantId); Add(reference.SourceReferenceId); Add(reference.ActivationId); Add(reference.SlotId);
        Add(reference.DefinitionId); Add(reference.DefinitionVersionId); Add(reference.SourceKind); Add(reference.SourceId); Add(reference.SourceVersion);
        Add(reference.ArtifactVersion); Add(material.Identity.ArtifactId); Add(material.Identity.ArtifactHash); Add(material.IncarnationId);
        foreach (var declaration in declarations)
        { Add(declaration.ReceiverAddress); Add(declaration.CanStartWorkflow ? "1" : "0"); Add(declaration.MessageNames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)); foreach (var name in declaration.MessageNames) Add(name); }
        var value = text.ToString(); var bytes = new byte[value.Length * 2];
        for (var index = 0; index < value.Length; index++) { bytes[index * 2] = (byte)value[index]; bytes[index * 2 + 1] = (byte)(value[index] >> 8); }
        return "elsa.message-publication-interest/v1:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
    internal sealed record Root(MessageScope Scope, MessageDefinitionIdentity Definition, WorkflowExecutableSourceReference Reference, string SlotName, string ReferenceArtifactHash,
        string MaterialIncarnation, IReadOnlyList<MessageMaterialInterestDeclaration> Declarations, string Fingerprint, bool Closed)
    {
        internal long? ClosedAtEpoch { get; init; }
        public override string ToString() => nameof(Root);
    }

    // Application-wide with the existing checkpoint state, not scoped adapter state or a second message authority.
    internal sealed class SharedState(PersistenceScope scope, InMemoryWorkflowActivationAuthority authority,
        InMemoryWorkflowExecutableSourceReferenceStore references, InMemoryWorkflowExecutableStore material)
    {
        internal readonly PersistenceScope Scope = scope;
        internal readonly InMemoryWorkflowActivationAuthority Authority = authority;
        internal readonly InMemoryWorkflowExecutableSourceReferenceStore References = references;
        internal readonly InMemoryWorkflowExecutableStore Material = material;
        internal readonly object Gate = new();
        internal readonly InMemoryActivationProjectionStates States = new();
        internal readonly Dictionary<string, Root> Roots = new(StringComparer.Ordinal);
        internal readonly Dictionary<MessageScope, long> Epochs = [];
    }
}
