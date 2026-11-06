using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Elsa.Workflows.Runtime.Services.Executables.WorkflowActivationSwitchRules;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Unregistered dependency-free native publication projection, not cohort/start/acceptance authority.</summary>
internal sealed partial class EfMessagePublicationInterestStore : IMessagePublicationInterestProjection
{
    private static readonly EfWriteRetry Writes = new(MaximumAttempts, error =>
        EfRelationalExceptionClassifier.IsSaveConflict(error, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient));
    private readonly RuntimeDbContext context;
    private readonly EfWorkflowActivationAuthority authority;
    private readonly EfWorkflowExecutableSourceReferenceStore references;
    private readonly EfWorkflowExecutableStore material;
    private readonly IPersistenceAccessContextAccessor access;
    private readonly MessageMaterialInterestExtractor extractor;
    private readonly TimeProvider clock;
    private IDbContextTransaction? lockedTransaction;
    private readonly Dictionary<string, MessageInterestScopeControlEntity> lockedScopes = new(StringComparer.Ordinal);
    private readonly HashSet<string> bumpedScopes = new(StringComparer.Ordinal);

    internal EfMessagePublicationInterestStore(RuntimeDbContext context, IWorkflowActivationAuthority authority,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutableStore material,
        IPersistenceAccessContextAccessor access, IEnumerable<IMessageMaterialInterestProvider> providers, TimeProvider clock)
    {
        this.context = context; this.access = access; this.extractor = new(providers); this.clock = clock;
        this.authority = authority as EfWorkflowActivationAuthority ?? throw Unsupported();
        this.references = references as EfWorkflowExecutableSourceReferenceStore ?? throw Unsupported();
        this.material = material as EfWorkflowExecutableStore ?? throw Unsupported();
        if (!ReferenceEquals(this.authority.Context, context) || !ReferenceEquals(this.references.Context, context) ||
            !ReferenceEquals(this.material.Context, context)) throw Unsupported();
        RequireScope();
    }
    // The real internal coordinator path; no registration or Message capability is enabled here.
    internal WorkflowActivationCoordinator CreateCoordinator(EfWorkflowActivationSwitch activationSwitch,
        IWorkflowExecutableRootWriteLeaseManager leases, IWorkflowTriggerIndexer triggerIndexer,
        IWorkflowTriggerBindingStore bindings, IRecurringTriggerScheduleStore? schedules = null,
        IEnumerable<IWorkflowTriggerIndexObserver>? observers = null,
        Microsoft.Extensions.Logging.ILogger<WorkflowActivationCoordinator>? logger = null,
        Microsoft.Extensions.Options.IOptions<Elsa.Workflows.Runtime.Core.Options.ActivationChannelOptions>? channels = null) =>
        new(this, authority, activationSwitch, references, leases, clock, triggerIndexer, bindings, schedules, observers, logger, channels);

    internal RuntimeDbContext Context => context;
    internal void RequireComposition(IWorkflowActivationAuthority actualAuthority, IWorkflowExecutableSourceReferenceStore actualReferences)
    {
        RequireScope();
        if (!ReferenceEquals(authority, actualAuthority) || !ReferenceEquals(references, actualReferences)) throw Unsupported();
    }
    void IMessagePublicationInterestProjection.RequireActualComposition(IWorkflowActivationAuthority actualAuthority,
        IWorkflowActivationSwitch activationSwitch, IWorkflowExecutableSourceReferenceStore actualReferences)
    {
        RequireComposition(actualAuthority, actualReferences);
        if (activationSwitch is not EfWorkflowActivationSwitch physical || !physical.UsesMessageInterests(this)) throw Unsupported();
        physical.RequireMessageInterestTuple();
    }

    public async ValueTask PrepareAsync(WorkflowExecutable requested, string activationId, string slotId, string slotName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requested);
        var scope = RequireScope(); RequireCleanContext(); _ = MessageInterestWriteVersion.For(context);
        await Writes.RunUntilSettledAsync<bool>(context, async () =>
        {
            context.ChangeTracker.Clear(); await using var transaction = await context.Database.BeginTransactionAsync(ct);
            try
            {
                var source = await references.ReadPhysicalMaintenanceSourceAsync(scope, WorkflowActivationReferenceIdentity.Create(activationId), ct)
                    ?? throw new InvalidOperationException("The canonical activation reference is missing.");
                var current = await ((IWorkflowExecutableStore)material).FindMaterialAsync(source.Value.ArtifactId, ct)
                    ?? throw new InvalidOperationException("The canonical material is missing.");
                var reference = source.Value;
                if (current.NativeWorkflow is null || current.NativeWorkflow.Dependencies.Count != 0 ||
                    reference.Scope != WorkflowExecutableReferenceScope.Published || reference.ExpiresAt is not null ||
                    reference.ActivationId != activationId || reference.SlotId != slotId ||
                    slotId != WorkflowActivationSlotIdentity.Create(reference.DefinitionId, slotName) ||
                    reference.DefinitionId != requested.Identity.DefinitionId || reference.DefinitionVersionId != requested.Identity.DefinitionVersionId ||
                    reference.ArtifactVersion != requested.Identity.ArtifactVersion || current.Identity.ArtifactId != requested.Identity.ArtifactId ||
                    current.Identity.ArtifactHash != requested.Identity.ArtifactHash) throw Unsupported();
                access.Current.EnsureTenantScope(reference.TenantId);
                var declarations = extractor.Extract(current).Declarations.Select(item =>
                    new MessagePublicationDeclaration(item.ReceiverAddress, item.MessageNames.ToArray(), item.CanStartWorkflow)).ToArray();
                var descriptor = new MessagePublicationDescriptor(new(new(scope), reference.TenantId), MessageDefinitionIdentity.WorkflowDefinitionNamespace,
                    activationId, slotId, slotName, MessagePublicationSourceFacts.From(reference), source.Row.IncarnationId,
                    current.Identity.ArtifactHash, current.IncarnationId, declarations);
                MessagePublicationInterestDocument.Validate(descriptor);
                var existing = await RootAsync(scope, activationId, ct);
                var old = existing is null ? null : ReadRoot(existing, scope, activationId);
                if (old is not null && existing!.DescriptorFingerprint != MessagePublicationInterestDocument.Fingerprint(descriptor))
                    throw new InvalidOperationException("The immutable prepared publication interest differs.");
                if (old?.State is (int)WorkflowActivationProjectionState.Active or (int)WorkflowActivationProjectionState.Replaced)
                    throw new InvalidOperationException("The publication interest already serves or was replaced.");
                var controlId = MessagePublicationInterestDocument.ScopeId(descriptor.Scope);
                var control = await context.MessageInterestScopeControls.SingleOrDefaultAsync(row => row.Id == controlId, ct);
                if (control is null) context.MessageInterestScopeControls.Add(NewControl(descriptor.Scope));
                else _ = MessagePublicationInterestDocument.Read(control, descriptor.Scope);
                if (existing is null) context.MessagePublicationInterests.Add(NewRoot(descriptor));
                else if (old!.State == MessagePublicationInterestDocument.Closed) SetState(existing, old, (int)WorkflowActivationProjectionState.Prepared, old.ClosedAtEpoch);
                var conflict = await context.TryCommitAndClearAsync(transaction, Writes, "preparing Message publication interest", ct);
                return conflict is null ? true : EfWriteAttempt<bool>.Retry(conflict);
            }
            catch { await context.RollbackAndClearAsync(transaction); throw; }
        }, conflict => throw new InvalidOperationException("Publication interest preparation changed concurrently.", conflict), ct);
    }

    public async ValueTask<WorkflowActivationProjectionState> FindStateAsync(string activationId, CancellationToken ct)
    {
        var scope = RequireScope(); var row = await RootAsync(scope, activationId, ct, tracking: false);
        if (row is null) return WorkflowActivationProjectionState.Missing;
        var document = ReadRoot(row, scope, activationId);
        if (document.State == (int)WorkflowActivationProjectionState.Active) await ValidateCurrentAsync(document.Descriptor, false, false, ct);
        return MessagePublicationInterestDocument.State(document);
    }
    public async ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string slotId, CancellationToken ct)
    {
        var scope = RequireScope(); var encoded = EfRelationalIdentity.Encode(scope); var hash = EfRelationalIdentity.Hash(scope);
        var slot = EfRelationalIdentity.Encode(slotId); var slotHash = EfRelationalIdentity.Hash(slotId);
        var rows = await context.MessagePublicationInterests.AsNoTracking().Where(row => row.ScopeKey == encoded && row.ScopeKeyHash == hash &&
            row.SlotId == slot && row.SlotIdHash == slotHash && row.State == (int)WorkflowActivationProjectionState.Active)
            .OrderBy(row => row.ActivationIdHash).ToArrayAsync(ct);
        var result = new List<string>();
        foreach (var row in rows)
        {
            var activation = EfRelationalIdentity.Decode(row.ActivationId); var document = ReadRoot(row, scope, activation);
            if (document.Descriptor.SlotId != slotId) throw MessagePublicationInterestDocument.Invalid();
            // Cleanup discovery names serving projection state even when the old reference/material was retired.
            result.Add(activation);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    // Called before ANY authority/reference/projection staging in this switch attempt. Locks belong to its transaction.
    internal async ValueTask<bool> StageLockAsync(string scope, IReadOnlyCollection<string> activations, bool allowMissing, string? admittedActivation, CancellationToken ct)
    {
        if (RequireScope() != scope || context.Database.CurrentTransaction is null) throw Unsupported();
        _ = MessageInterestWriteVersion.For(context);
        var roots = new List<MessagePublicationDocument>();
        foreach (var activation in activations.Distinct(StringComparer.Ordinal))
        {
            var row = await RootAsync(scope, activation, ct, tracking: false);
            if (row is null)
            { if (allowMissing) continue; throw new InvalidOperationException("Message publication preparation is missing."); }
            if (row.Revision == long.MaxValue) throw new InvalidOperationException("The root revision cannot advance.");
            roots.Add(ReadRoot(row, scope, activation));
        }
        if (!await StageLockScopesAsync(roots.Select(root => root.Descriptor.Scope).Distinct().ToArray(), false, ct)) return false;
        // Only a root being admitted needs live source/material fences. Closing stale projection rows must remain
        // possible after source/material retirement; their retained canonical descriptor and locked scope suffice.
        var admitted = roots.Where(root => root.Descriptor.ActivationId == admittedActivation).ToArray();
        foreach (var root in admitted.GroupBy(root => root.Descriptor.Source.ArtifactId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var values = root.ToArray();
            if (values.Any(value => value.Descriptor.MaterialIncarnation != values[0].Descriptor.MaterialIncarnation))
                throw MessagePublicationInterestDocument.Invalid();
            if (!await material.StageMessageInterestMaterialFenceAsync(scope, root.Key, values[0].Descriptor.MaterialIncarnation, ct)) return false;
        }
        foreach (var root in admitted.OrderBy(root => root.Descriptor.Source.SourceReferenceId, StringComparer.Ordinal))
            if (!await references.StageMessageInterestFenceAsync(scope, root.Descriptor.Source.SourceReferenceId, root.Descriptor.SourceIncarnation, ct)) return false;
        return true;
    }

    internal async ValueTask<WorkflowActivationProjectionState> StageStateAsync(string scope, string activationId, bool repairResumeFailed, CancellationToken ct)
    {
        var row = await RootAsync(scope, activationId, ct); if (row is null) return WorkflowActivationProjectionState.Missing;
        var root = ReadRoot(row, scope, activationId); EnsureLocked(root.Descriptor.Scope);
        if (root.State == (int)WorkflowActivationProjectionState.Active) await ValidateCurrentAsync(root.Descriptor, repairResumeFailed, false, ct);
        return MessagePublicationInterestDocument.State(root);
    }
    internal async ValueTask<ProjectionStaging> StageSwitchAsync(string scope, string activationId, string? replacedId, bool closeReplaced, CancellationToken ct)
    {
        var row = await RootAsync(scope, activationId, ct) ?? throw new InvalidOperationException("Message preparation is missing.");
        var root = ReadRoot(row, scope, activationId); EnsureLocked(root.Descriptor.Scope);
        MessagePublicationInterestEntity? oldRow = null; MessagePublicationDocument? old = null;
        if (replacedId is not null && replacedId != activationId)
        {
            oldRow = await RootAsync(scope, replacedId, ct); if (oldRow is not null) old = ReadRoot(oldRow, scope, replacedId);
        }
        if (root.State == MessagePublicationInterestDocument.Closed) throw new InvalidOperationException("Message preparation was closed.");
        await ValidateCurrentAsync(root.Descriptor, !closeReplaced, closeReplaced, ct);
        if (root.State == (int)WorkflowActivationProjectionState.Active)
        { if (old?.State == (int)WorkflowActivationProjectionState.Active) throw MessagePublicationInterestDocument.Invalid(); return ProjectionStaging.Unchanged; }
        if (replacedId is not null && replacedId != activationId && old?.State != (int)WorkflowActivationProjectionState.Active)
            throw new InvalidOperationException("The replaced publication no longer serves.");
        // Preview EXACTLY the owning switch's later reference transition while its physical row is locked.
        if (old is not null) EnsureLocked(old.Descriptor.Scope);
        _ = Bump(root.Descriptor.Scope); SetState(row, root, (int)WorkflowActivationProjectionState.Active, root.ClosedAtEpoch);
        if (old is not null)
        {
            var closedAt = Bump(old.Descriptor.Scope);
            SetState(oldRow!, old, closeReplaced ? MessagePublicationInterestDocument.Closed : (int)WorkflowActivationProjectionState.Replaced,
                closeReplaced ? closedAt : old.ClosedAtEpoch);
        }
        return ProjectionStaging.Staged;
    }
    internal async ValueTask<ProjectionStaging> StageCloseAsync(string scope, string activationId, bool unlessServing, CancellationToken ct)
    {
        var row = await RootAsync(scope, activationId, ct); if (row is null) return ProjectionStaging.Unchanged;
        var root = ReadRoot(row, scope, activationId); EnsureLocked(root.Descriptor.Scope);
        if (unlessServing && root.State == (int)WorkflowActivationProjectionState.Active) return ProjectionStaging.Serves;
        if (root.State == MessagePublicationInterestDocument.Closed) return ProjectionStaging.Unchanged;
        var epoch = root.State == (int)WorkflowActivationProjectionState.Active ? Bump(root.Descriptor.Scope) : lockedScopes[MessagePublicationInterestDocument.ScopeId(root.Descriptor.Scope)].Epoch;
        SetState(row, root, MessagePublicationInterestDocument.Closed, epoch); return ProjectionStaging.Staged;
    }

    internal async ValueTask<(long Epoch, IReadOnlyList<string> Names)> ObservePublicationAsync(MessageScope requested, CancellationToken ct = default)
    {
        RequireCleanContext();
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (requested.PersistenceScope.Value != RequireScope() || requested.TestScopeId is not null) throw Unsupported();
        access.Current.EnsureTenantScope(requested.TenantId);
        var control = await context.MessageInterestScopeControls.AsNoTracking().SingleOrDefaultAsync(row => row.Id == MessagePublicationInterestDocument.ScopeId(requested), ct);
        var epoch = control is null ? 0 : MessagePublicationInterestDocument.Read(control, requested).Epoch;
        var scope = EfRelationalIdentity.Encode(RequireScope()); var hash = EfRelationalIdentity.Hash(RequireScope());
        var tenant = MessagePublicationInterestDocument.Encode(requested.TenantId); var tenantHash = MessagePublicationInterestDocument.Hash(requested.TenantId);
        var rows = await context.MessagePublicationInterests.AsNoTracking().Where(row => row.ScopeKey == scope && row.ScopeKeyHash == hash &&
            row.TenantId == tenant && row.TenantIdHash == tenantHash && row.State == (int)WorkflowActivationProjectionState.Active).OrderBy(row => row.Id).ToArrayAsync(ct);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var root = ReadRoot(row, RequireScope(), EfRelationalIdentity.Decode(row.ActivationId));
            if (root.Descriptor.Scope != requested) throw MessagePublicationInterestDocument.Invalid();
            var slot = await authority.FindAsync(root.Descriptor.Source.DefinitionId, root.Descriptor.SlotName, ct);
            if (slot?.ActiveActivationId != root.Descriptor.ActivationId) throw MessagePublicationInterestDocument.Invalid();
            await ValidateCurrentAsync(root.Descriptor, false, false, ct);
            foreach (var name in root.Descriptor.Declarations.SelectMany(item => item.Names)) names.Add(name);
        }
        await transaction.CommitAsync(ct); return (epoch, names.ToArray());
    }
    internal void RequireCompleteAcceptanceCoverage() => throw new NotSupportedException("Active/model coverage and complete accepted cohorts are unavailable.");

    private MessagePublicationDocument ReadRoot(MessagePublicationInterestEntity row, string scope, string activation)
    {
        var root = MessagePublicationInterestDocument.Read(row, scope, activation);
        access.Current.EnsureTenantScope(root.Descriptor.Scope.TenantId);
        return root;
    }
    private string RequireScope()
    {
        var scope = access.Current.RequireScope().Value;
        if (authority.AccessContext.RequireScope().Value != scope || references.AccessContext.RequireScope().Value != scope ||
            material.AccessContext.RequireScope().Value != scope) throw Unsupported();
        return scope;
    }
    private void RequireCleanContext()
    {
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null || context.Database.GetEnlistedTransaction() is not null ||
            Transaction.Current is not null) throw new NotSupportedException("Interest preparation requires its own clean transaction.");
    }
    private void EnsureLocked(MessageScope scope)
    {
        if (RequireScope() != scope.PersistenceScope.Value || context.Database.CurrentTransaction is null ||
            !ReferenceEquals(lockedTransaction, context.Database.CurrentTransaction) || !lockedScopes.ContainsKey(MessagePublicationInterestDocument.ScopeId(scope)))
            throw new DbUpdateConcurrencyException("The actual interest scope fence changed.");
    }
    private long Bump(MessageScope scope)
    {
        EnsureLocked(scope); var id = MessagePublicationInterestDocument.ScopeId(scope); var row = lockedScopes[id];
        if (bumpedScopes.Add(id))
        { row.Epoch = checked(row.Epoch + 1); row.Revision = checked(row.Revision + 1); row.ContentJson = RuntimeArtifactJson.Serialize(new MessageInterestControlDocument(1, scope, row.Epoch)); }
        return row.Epoch;
    }
    private async ValueTask ValidateCurrentAsync(MessagePublicationDescriptor root, bool resumeFailed, bool restoreReplaced, CancellationToken ct)
    {
        var source = await references.ReadPhysicalMaintenanceSourceAsync(RequireScope(), root.Source.SourceReferenceId, ct)
            ?? throw MessagePublicationInterestDocument.Invalid();
        if (source.Row.IncarnationId != root.SourceIncarnation || MessagePublicationSourceFacts.From(source.Value) != root.Source) throw MessagePublicationInterestDocument.Invalid();
        var effective = restoreReplaced ? RestoreReplaced(source.Value, root.ActivationId) ?? source.Value :
            resumeFailed ? ResumeFailed(source.Value, root.ActivationId) ?? source.Value : source.Value;
        if (!effective.IsLive(clock.GetUtcNow())) throw new InvalidOperationException("The canonical publication reference is unavailable.");
        var current = await ((IWorkflowExecutableStore)material).FindMaterialAsync(root.Source.ArtifactId, ct);
        if (current is null || (root.IsModel ? current.Model is null : current.NativeWorkflow is null) || current.IncarnationId != root.MaterialIncarnation || current.Identity.ArtifactHash != root.ArtifactHash)
            throw new InvalidOperationException("The canonical publication material changed.");
    }
    private Task<MessagePublicationInterestEntity?> RootAsync(string scope, string activation, CancellationToken ct, bool tracking = true)
    {
        IQueryable<MessagePublicationInterestEntity> query = context.MessagePublicationInterests;
        if (!tracking) query = query.AsNoTracking();
        var id = MessagePublicationInterestDocument.Id(scope, activation); return query.SingleOrDefaultAsync(row => row.Id == id, ct);
    }
    private MessagePublicationInterestEntity NewRoot(MessagePublicationDescriptor value) => new()
    {
        Id = MessagePublicationInterestDocument.Id(value.Scope.PersistenceScope.Value, value.ActivationId), ScopeKey = EfRelationalIdentity.Encode(value.Scope.PersistenceScope.Value),
        ScopeKeyHash = EfRelationalIdentity.Hash(value.Scope.PersistenceScope.Value), TenantId = MessagePublicationInterestDocument.Encode(value.Scope.TenantId),
        TenantIdHash = MessagePublicationInterestDocument.Hash(value.Scope.TenantId), ActivationId = EfRelationalIdentity.Encode(value.ActivationId), ActivationIdHash = EfRelationalIdentity.Hash(value.ActivationId),
        SlotId = EfRelationalIdentity.Encode(value.SlotId), SlotIdHash = EfRelationalIdentity.Hash(value.SlotId), DescriptorFingerprint = MessagePublicationInterestDocument.Fingerprint(value),
        State = (int)WorkflowActivationProjectionState.Prepared, ContentJson = RuntimeArtifactJson.Serialize(new MessagePublicationDocument(1, value, (int)WorkflowActivationProjectionState.Prepared, null)),
        SchemaVersion = MessageInterestWriteVersion.For(context), Revision = 1
    };
    private MessageInterestScopeControlEntity NewControl(MessageScope scope) => new()
    {
        Id = MessagePublicationInterestDocument.ScopeId(scope), ScopeKey = EfRelationalIdentity.Encode(scope.PersistenceScope.Value), ScopeKeyHash = EfRelationalIdentity.Hash(scope.PersistenceScope.Value),
        TenantId = MessagePublicationInterestDocument.Encode(scope.TenantId), TenantIdHash = MessagePublicationInterestDocument.Hash(scope.TenantId), FullIdentity = MessagePublicationInterestDocument.ScopeIdentity(scope),
        Epoch = 0, ContentJson = RuntimeArtifactJson.Serialize(new MessageInterestControlDocument(1, scope, 0)), SchemaVersion = MessageInterestWriteVersion.For(context), Revision = 1
    };
    private void SetState(MessagePublicationInterestEntity row, MessagePublicationDocument value, int state, long? closedAt)
    {
        row.State = state; row.Revision = checked(row.Revision + 1); row.SchemaVersion = MessageInterestWriteVersion.For(context);
        row.ContentJson = RuntimeArtifactJson.Serialize(value with { State = state, ClosedAtEpoch = closedAt });
    }
    private static NotSupportedException Unsupported() => new("The native Message publication owner composition/profile is unsupported.");
}
