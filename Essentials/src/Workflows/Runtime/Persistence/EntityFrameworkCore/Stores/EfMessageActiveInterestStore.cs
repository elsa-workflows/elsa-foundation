using Elsa.Persistence.EntityFramework;
using System.Transactions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Unregistered physical native participant; persistent selection is not complete-cohort readiness.</summary>
internal sealed class EfMessageActiveInterestStore
{
    private readonly RuntimeDbContext context;
    private readonly EfRuntimeCheckpointCommitStore checkpoints;
    private readonly EfWorkflowExecutionStateStore executions;
    private readonly EfExecutionLivenessStateStore liveness;
    private readonly EfWorkflowExecutableSourceReferenceStore references;
    internal EfWorkflowExecutableStore Material { get; }
    private readonly EfMessagePublicationInterestStore publication;
    private readonly IPersistenceAccessContextAccessor access;
    private readonly TimeProvider clock;
    private readonly string boundScope;
    private EfMessageActiveInterestStore(RuntimeDbContext context, EfRuntimeCheckpointCommitStore checkpoints,
        EfWorkflowExecutionStateStore executions, EfExecutionLivenessStateStore liveness,
        EfWorkflowExecutableSourceReferenceStore references, EfWorkflowExecutableStore material,
        EfMessagePublicationInterestStore publication, IPersistenceAccessContextAccessor access, TimeProvider clock)
    {
        this.context = context; this.checkpoints = checkpoints; this.executions = executions; this.liveness = liveness;
        this.references = references; Material = material; this.publication = publication; this.access = access; this.clock = clock;
        boundScope = access.Current.RequireScope().Value; RequireTuple(checkpoints);
    }
    internal static async ValueTask<EfMessageActiveInterestStore> BindAsync(RuntimeDbContext context,
        EfRuntimeCheckpointCommitStore checkpoints, EfWorkflowExecutionStateStore executions, EfExecutionLivenessStateStore liveness,
        EfWorkflowExecutableSourceReferenceStore references, EfWorkflowExecutableStore material,
        EfMessagePublicationInterestStore publication, IPersistenceAccessContextAccessor access, TimeProvider clock, CancellationToken ct = default)
    {
        RequireClean(context); var owner = new EfMessageActiveInterestStore(context, checkpoints, executions, liveness, references, material, publication, access, clock);
        if (context.MessageActiveInterests is { } old)
        { old.RequireTuple(checkpoints); if (!ReferenceEquals(old.publication, publication)) throw Unsupported(); return old; }
        _ = MessageInterestWriteVersion.For(context);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var row = await context.MessageActiveInterestOwners.SingleOrDefaultAsync(item => item.Id == MessageActiveInterestOwnerDocument.Id(owner.boundScope), ct);
            if (row is null) context.MessageActiveInterestOwners.Add(MessageActiveInterestOwnerDocument.New(owner.boundScope));
            else _ = MessageActiveInterestOwnerDocument.Read(row, owner.boundScope);
            await context.SaveChangesAsync(ct); await transaction.CommitAsync(ct); context.ChangeTracker.Clear();
            context.MessageActiveInterests = owner; return owner;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); context.ChangeTracker.Clear(); throw; }
    }
    private void RequireTuple(EfRuntimeCheckpointCommitStore writer)
    {
        if (!ReferenceEquals(writer, checkpoints) || access.Current.RequireScope().Value != boundScope ||
            !ReferenceEquals(context, publication.Context) || !ReferenceEquals(context, references.Context) || !ReferenceEquals(context, Material.Context) ||
            executions.AccessContext.RequireScope().Value != boundScope || liveness.CurrentAccess.RequireScope().Value != boundScope) throw Unsupported();
        publication.RequireActiveTuple(references, Material, access, clock);
        writer.RequireMessageActiveTuple(executions, liveness, Material, access, clock);
    }
    internal static async ValueTask<EfMessageActiveInterestStore?> RequireNativeParticipantAsync(RuntimeDbContext context,
        EfRuntimeCheckpointCommitStore writer, string scope, RuntimeCheckpointCommit commit, CancellationToken ct)
    {
        if (commit.StateChanges.WorkflowExecution is null) return null;
        var selected = await ReadSelectionAsync(context, scope, ct);
        if (!selected) return null;
        var owner = context.MessageActiveInterests ?? throw Unsupported(); owner.RequireTuple(writer); RequireClean(context);
        if (commit.ModelState is not null || commit.ExpectedModelExecution is not null || commit.ExpectedModelParentWait is not null) throw Unsupported();
        return owner;
    }
    internal static async ValueTask RecheckSelectionAsync(RuntimeDbContext context, string scope, EfMessageActiveInterestStore? expected, CancellationToken ct)
    {
        if (await ReadSelectionAsync(context, scope, ct) != (expected is not null))
            throw new NotSupportedException("The native interest profile changed before its physical transaction.");
    }
    internal static async ValueTask RefuseOrdinarySaveAsync(RuntimeDbContext context, string scope, CancellationToken ct)
    { if (await ReadSelectionAsync(context, scope, ct)) throw new NotSupportedException("Managed native history must use its qualified physical checkpoint writer."); }
    private static async ValueTask<bool> ReadSelectionAsync(RuntimeDbContext context, string scope, CancellationToken ct)
    {
        var row = await context.MessageActiveInterestOwners.AsNoTracking().SingleOrDefaultAsync(item => item.Id == MessageActiveInterestOwnerDocument.Id(scope), ct);
        if (row is null) return false;
        _ = MessageActiveInterestOwnerDocument.Read(row, scope); _ = MessageInterestWriteVersion.For(context); return true;
    }
    // Runs before ownership/execution staging in the caller's owning transaction. SQL fences roll back with it.
    internal async ValueTask StageAsync(RuntimeCheckpointCommit commit, CancellationToken ct)
    {
        RequireTuple(checkpoints); if (context.Database.CurrentTransaction is null) throw Unsupported();
        var change = commit.StateChanges.WorkflowExecution ?? throw Unsupported(); var state = change.State;
        ValidateShape(commit, state);
        var scope = new MessageScope(new(boundScope), state.TenantId);
        if (!await publication.StageLockScopesAsync([scope], true, ct)) throw new DbUpdateConcurrencyException("The native interest scope changed.");
        // Scope precedes even ownership reads: Serializable providers may retain their shared locks.
        // The owning writer later rereads and CAS-touches this exact ownership row.
        var ownership = await liveness.FindAsync(state.WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(state.WorkflowExecutionId), ct);
        RuntimeExecutionFenceValidator.EnsureCurrent(state.WorkflowExecutionId, commit.ExpectedFence!, ownership, clock.GetUtcNow());
        var row = await context.MessageActiveInterestsRows.SingleOrDefaultAsync(item => item.Id == MessageActiveInterestDocument.Id(boundScope, state.WorkflowExecutionId), ct);
        var previous = row is null ? null : MessageActiveInterestDocument.Read(row, boundScope, state.WorkflowExecutionId);
        var actualRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(item => item.Id == EfWorkflowExecutionStateStore.CreateId(boundScope, state.WorkflowExecutionId), ct);
        var actual = actualRow is null ? null : EfWorkflowExecutionStateStore.ReadChecked(actualRow, boundScope, state.WorkflowExecutionId);
        if (actual is not null && (!MessageActiveInterestDocument.SameOwner(actual, state) || actual.Status.IsTerminal() && actual.Status != state.Status)) throw Invalid();
        MessageActiveInterestDocument.Document next;
        if (previous is null)
        {
            var source = await references.ReadPhysicalMaintenanceSourceAsync(boundScope, state.PinnedSource!.SourceReferenceId, ct) ?? throw Invalid();
            if (source.Value.Scope != WorkflowExecutableReferenceScope.Published || source.Value.SourceKind != "WorkflowDefinitionVersion" ||
                string.IsNullOrWhiteSpace(source.Value.ActivationId) || string.IsNullOrWhiteSpace(source.Value.SlotId) ||
                source.Value.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(source.Value.ActivationId) ||
                source.Value.ArtifactId != state.PinnedExecutable.ArtifactId || source.Value.TenantId != state.TenantId ||
                WorkflowExecutableSourceProvenance.From(source.Value) != state.PinnedSource) throw Unsupported();
            var material = await ReadMaterialAsync(state.PinnedExecutable, ct);
            next = MessageActiveInterestDocument.Create(scope, state, source.Value, source.Row.IncarnationId,
                material.IncarnationId, publication.ExtractActiveDeclarations(material), Guid.NewGuid().ToString("N"),
                state.Status.IsTerminal() ? publication.LockedScopeEpoch(scope) : null);
            if (!await Material.StageMessageInterestMaterialFenceAsync(boundScope, state.PinnedExecutable.ArtifactId, material.IncarnationId, ct) ||
                !await references.StageMessageInterestFenceAsync(boundScope, source.Value.SourceReferenceId, source.Row.IncarnationId, ct))
                throw new DbUpdateConcurrencyException("The first native interest owner changed.");
        }
        else
        {
            if (actual is null || !MessageActiveInterestDocument.SameOwner(previous.Owner, actual) || !MessageActiveInterestDocument.SameOwner(previous.Owner, state) ||
                previous.ClosedAtEpoch.HasValue != actual.Status.IsTerminal() || previous.ClosedAtEpoch.HasValue && (state.Status != actual.Status || previous.TerminalStatus != actual.Status)) throw Invalid();
            var material = await ReadMaterialAsync(previous.Owner.PinnedExecutable, ct);
            if (material.IncarnationId != previous.MaterialIncarnation) throw Invalid();
            if (!await Material.StageMessageInterestMaterialFenceAsync(boundScope, previous.Owner.PinnedExecutable.ArtifactId, previous.MaterialIncarnation, ct))
                throw new DbUpdateConcurrencyException("The pinned native material changed.");
            next = previous with { TerminalStatus = state.Status.IsTerminal() ? state.Status : null };
        }
        ct.ThrowIfCancellationRequested();
        var changesEligibility = previous is null ? !state.Status.IsTerminal() : previous.ClosedAtEpoch.HasValue != state.Status.IsTerminal();
        var epoch = changesEligibility ? publication.StageAdvanceScopeEpoch(scope) : publication.LockedScopeEpoch(scope);
        next = next with { ClosedAtEpoch = state.Status.IsTerminal() ? previous?.ClosedAtEpoch ?? epoch : null };
        // Validate the complete canonical value before staging any ordinary checkpoint participant.
        if (row is null) context.MessageActiveInterestsRows.Add(MessageActiveInterestDocument.ToEntity(next));
        else MessageActiveInterestDocument.Update(row, next);
    }
    private async ValueTask<RuntimeExecutionMaterial> ReadMaterialAsync(WorkflowExecutableIdentity pin, CancellationToken ct)
    {
        var current = await ((IWorkflowExecutableStore)Material).FindMaterialAsync(pin.ArtifactId, ct) ?? throw Invalid();
        if (current.NativeWorkflow is null || current.Identity.ArtifactHash != pin.ArtifactHash || current.NativeWorkflow.Dependencies.Count != 0) throw Unsupported();
        return current;
    }
    private void ValidateShape(RuntimeCheckpointCommit commit, WorkflowExecutionState state)
    {
        if (commit.ExpectedFence is null || changeInvalid(commit, state) || !Enum.IsDefined(state.Status) || state.RunKind != WorkflowRunKind.PublishedRun ||
            state.TestScope is not null || state.ModelWorkOrigin is not null || state.ParentWorkflowExecutionId is not null ||
            state.PinnedSource is null || state.Partition is null || state.Authority is null) throw Unsupported();
        access.Current.EnsureTenantScope(state.TenantId);
        static bool changeInvalid(RuntimeCheckpointCommit c, WorkflowExecutionState s) => c.StateChanges.WorkflowExecution!.Operation != RuntimeStateChangeOperation.Upsert ||
            c.StateChanges.WorkflowExecution.StateId != c.WorkflowExecutionId || s.WorkflowExecutionId != c.WorkflowExecutionId;
    }
    internal sealed record DeletionScopeHint(bool Selected, MessageScope? Scope, string? Incarnation, string? Fingerprint);
    // This is lock discovery only, outside any transaction. It cannot authorize deletion; the canonical
    // control and the real execution are revalidated after the discovered scope is locked.
    internal static async ValueTask<DeletionScopeHint> ReadDeletionScopeHintAsync(RuntimeDbContext context,
        string scope, string executionId, CancellationToken ct)
    {
        RequireClean(context);
        if (!await ReadSelectionAsync(context, scope, ct)) return new(false, null, null, null);
        var owner = context.MessageActiveInterests ?? throw Unsupported(); owner.RequireTuple(owner.checkpoints);
        var row = await context.MessageActiveInterestsRows.AsNoTracking().SingleOrDefaultAsync(item => item.Id == MessageActiveInterestDocument.Id(scope, executionId), ct);
        if (row is null)
        {
            // No later scope wait is permitted on this branch. Keep ordinary absent-ID semantics, but refuse
            // deletion of pre-profile history whose control has never been authoritatively captured/closed.
            if (await context.WorkflowExecutionStates.AsNoTracking().AnyAsync(item => item.Id == EfWorkflowExecutionStateStore.CreateId(scope, executionId), ct)) throw Unsupported();
            return new(true, null, null, null);
        }
        var root = MessageActiveInterestDocument.Read(row, scope, executionId); owner.access.Current.EnsureTenantScope(root.Scope.TenantId);
        return new(true, root.Scope, root.Incarnation, row.DescriptorFingerprint);
    }
    internal static async ValueTask StageDeletionScopeAsync(RuntimeDbContext context, string scope, string executionId,
        DeletionScopeHint hint, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw Unsupported();
        if (await ReadSelectionAsync(context, scope, ct) != hint.Selected) throw Unsupported();
        if (!hint.Selected) return;
        var owner = context.MessageActiveInterests ?? throw Unsupported(); owner.RequireTuple(owner.checkpoints);
        var messageScope = hint.Scope ?? throw Unsupported();
        if (!await owner.publication.StageLockScopesAsync([messageScope], false, ct)) throw new DbUpdateConcurrencyException("The deletion scope changed.");
        var row = await context.MessageActiveInterestsRows.AsNoTracking().SingleOrDefaultAsync(item => item.Id == MessageActiveInterestDocument.Id(scope, executionId), ct) ?? throw Unsupported();
        var root = MessageActiveInterestDocument.Read(row, scope, executionId);
        if (root.Scope != hint.Scope || root.Incarnation != hint.Incarnation || row.DescriptorFingerprint != hint.Fingerprint || root.ClosedAtEpoch is null) throw Unsupported();
    }
    internal static async ValueTask ValidateDeletionOwnerAsync(RuntimeDbContext context, string scope,
        WorkflowExecutionState state, DeletionScopeHint hint, CancellationToken ct)
    {
        if (!hint.Selected) return;
        var owner = context.MessageActiveInterests ?? throw Unsupported(); owner.RequireTuple(owner.checkpoints);
        var messageScope = hint.Scope ?? throw Unsupported(); _ = owner.publication.LockedScopeEpoch(messageScope);
        var row = await context.MessageActiveInterestsRows.AsNoTracking().SingleOrDefaultAsync(item => item.Id == MessageActiveInterestDocument.Id(scope, state.WorkflowExecutionId), ct) ?? throw Unsupported();
        var root = MessageActiveInterestDocument.Read(row, scope, state.WorkflowExecutionId); owner.access.Current.EnsureTenantScope(state.TenantId);
        if (root.Scope != messageScope || root.Incarnation != hint.Incarnation || row.DescriptorFingerprint != hint.Fingerprint ||
            state.TenantId != messageScope.TenantId || !state.Status.IsTerminal() || root.ClosedAtEpoch is null ||
            root.TerminalStatus != state.Status || !MessageActiveInterestDocument.SameOwner(root.Owner, state)) throw Unsupported();
    }
    internal static async ValueTask RefuseOrdinaryDeletionTombstoneAsync(RuntimeDbContext context, string scope,
        ExecutionLivenessState state, CancellationToken ct)
    {
        if (state.ExecutionLivenessStateId == RuntimeExecutionOwnershipStateId.For(state.WorkflowExecutionId) &&
            state.Metadata.ContainsKey(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone) && await ReadSelectionAsync(context, scope, ct))
            throw new NotSupportedException("Managed deletion tombstones require the qualified closed-control deletion transaction.");
    }
    internal async ValueTask<(long Epoch, IReadOnlyList<MessageActiveInterestDocument.Document> Roots)> ObserveAsync(MessageScope scope, CancellationToken ct = default)
    {
        RequireTuple(checkpoints); RequireClean(context);
        if (scope.PersistenceScope.Value != boundScope || scope.TestScopeId is not null) throw Unsupported();
        access.Current.EnsureTenantScope(scope.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var control = await context.MessageInterestScopeControls.AsNoTracking().SingleOrDefaultAsync(row => row.Id == MessagePublicationInterestDocument.ScopeId(scope), ct);
        var epoch = control is null ? 0 : MessagePublicationInterestDocument.Read(control, scope).Epoch;
        var key = EfRelationalIdentity.Encode(boundScope); var hash = EfRelationalIdentity.Hash(boundScope);
        var tenant = MessagePublicationInterestDocument.Encode(scope.TenantId); var tenantHash = MessagePublicationInterestDocument.Hash(scope.TenantId);
        var rows = await context.MessageActiveInterestsRows.AsNoTracking().Where(row => row.ScopeKey == key && row.ScopeKeyHash == hash &&
            row.TenantId == tenant && row.TenantIdHash == tenantHash && !row.Closed).OrderBy(row => row.Id).ToArrayAsync(ct);
        var roots = new List<MessageActiveInterestDocument.Document>();
        foreach (var row in rows)
        {
            var root = MessageActiveInterestDocument.Read(row, boundScope, EfRelationalIdentity.Decode(row.WorkflowExecutionId));
            if (root.Scope != scope) throw Invalid();
            var physical = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(item => item.Id == EfWorkflowExecutionStateStore.CreateId(boundScope, root.Owner.WorkflowExecutionId), ct) ?? throw Invalid();
            var state = EfWorkflowExecutionStateStore.ReadChecked(physical, boundScope, root.Owner.WorkflowExecutionId);
            if (state.Status.IsTerminal() || !MessageActiveInterestDocument.SameOwner(root.Owner, state) ||
                (await ReadMaterialAsync(root.Owner.PinnedExecutable, ct)).IncarnationId != root.MaterialIncarnation) throw Invalid();
            roots.Add(root);
        }
        RequireTuple(checkpoints); access.Current.EnsureTenantScope(scope.TenantId);
        await transaction.CommitAsync(ct);
        RequireTuple(checkpoints); access.Current.EnsureTenantScope(scope.TenantId);
        return (epoch, roots);
    }
    internal void RequireCompleteAcceptanceCoverage() => throw new NotSupportedException("Native active backfill/fleet/all-owner coverage is not qualified.");
    internal async ValueTask<(int Inspected, bool Truncated, int MissingControls, bool CompleteCoverage)> AssessCoverageAsync(int maximumRows, CancellationToken ct = default)
    {
        if (maximumRows is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(maximumRows)); RequireTuple(checkpoints); RequireClean(context);
        await using var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var hash = EfRelationalIdentity.Hash(boundScope); var key = EfRelationalIdentity.Encode(boundScope);
        // Serializable inspection reads the scope-control range before workflow/root rows, matching writers' lock order.
        // If that range itself exceeds the foreground bound, refuse to describe the uninspected workflow range.
        var controls = await context.MessageInterestScopeControls.AsNoTracking().Where(row => row.ScopeKeyHash == hash && row.ScopeKey == key)
            .OrderBy(row => row.Id).Take(maximumRows + 1).ToArrayAsync(ct);
        var admittedTenants = new HashSet<string?>();
        foreach (var control in controls)
        {
            var tenant = control.TenantId is null ? null : EfRelationalIdentity.Decode(control.TenantId);
            access.Current.EnsureTenantScope(tenant); _ = MessagePublicationInterestDocument.Read(control, new(new(boundScope), tenant)); admittedTenants.Add(tenant);
        }
        if (controls.Length > maximumRows)
        { await tx.CommitAsync(ct); RequireCurrentCoverageAccess(); return (0, true, 0, false); }
        var rows = await context.WorkflowExecutionStates.AsNoTracking().Where(row => row.ScopeKeyHash == hash && row.ScopeKey == key).OrderBy(row => row.Id).Take(maximumRows + 1).ToArrayAsync(ct);
        var missing = 0;
        foreach (var row in rows.Take(maximumRows))
        {
            var state = EfWorkflowExecutionStateStore.ReadChecked(row, boundScope, EfRelationalIdentity.Decode(row.WorkflowExecutionId)); access.Current.EnsureTenantScope(state.TenantId); admittedTenants.Add(state.TenantId);
            if (!state.Status.IsTerminal() && !await context.MessageActiveInterestsRows.AsNoTracking().AnyAsync(item => item.Id == MessageActiveInterestDocument.Id(boundScope, state.WorkflowExecutionId), ct)) missing++;
        }
        RequireCurrentCoverageAccess(); await tx.CommitAsync(ct); RequireCurrentCoverageAccess();
        return (Math.Min(rows.Length, maximumRows), rows.Length > maximumRows, missing, false);
        void RequireCurrentCoverageAccess()
        { RequireTuple(checkpoints); foreach (var tenant in admittedTenants) access.Current.EnsureTenantScope(tenant); }
    }
    private static void RequireClean(RuntimeDbContext context)
    { if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null || context.Database.GetEnlistedTransaction() is not null || Transaction.Current is not null) throw Unsupported(); }
    private static NotSupportedException Unsupported() => new("The qualified native active-interest owner boundary is unavailable.");
    private static InvalidDataException Invalid() => new("The native active-interest owner or material changed.");
}
