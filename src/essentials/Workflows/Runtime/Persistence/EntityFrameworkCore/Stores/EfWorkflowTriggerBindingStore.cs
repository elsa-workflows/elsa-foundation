using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Concrete opt-in EF adapter for the durable workflow trigger-binding index.</summary>
/// <remarks>
/// An activation projection is its state row and its binding rows, read in separate statements. Every write that changes
/// whether a row serves, or the projection's immutable content, moves the state in the transaction that writes the rows:
/// preparing, switching and deleting a projection create, move or delete its state. So rows read while the state stood
/// still belong to it, and a state that moved while they were read is read again, never taken for a corrupt projection:
/// two calls can make the same switch at once (#2265). <c>EfWorkflowActivationSwitch</c> stages the switch and the
/// deletion through <see cref="StageSwitchAsync"/> and <see cref="StageDeletionAsync"/> in the transaction that moves the
/// slot (#2230).
/// <see cref="SaveAsync"/> writes a binding row and moves no state. That is safe: it serves the artifact-scoped index,
/// whose rows belong to no projection; one that rewrote an activation's row would leave it disagreeing with a state that
/// stood still, which is reported as the corruption it is; and the row's own revision moves, so a switch that read the row
/// loses its write and reads again.
/// </remarks>
public sealed class EfWorkflowTriggerBindingStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowTriggerBindingStore
{
    private const string ProjectionKind = "triggerBindings";
    private const int MaterializationBatchSize = 256;

    public async ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default)
    {
        Validate(binding);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = Id(scope, binding.TriggerBindingId);
        context.ChangeTracker.Clear();
        var existing = await context.WorkflowTriggerBindings.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is not null)
        {
            var current = Read(existing, scope, binding.TriggerBindingId);
            if (StringComparer.Ordinal.Equals(RuntimeArtifactJson.Serialize(current), RuntimeArtifactJson.Serialize(binding)))
                return binding;
            Copy(existing, binding, scope, revision: checked(existing.Revision + 1));
        }
        else
        {
            context.WorkflowTriggerBindings.Add(ToEntity(binding, scope, id, 1));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return binding;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException($"Workflow trigger binding '{binding.TriggerBindingId}' changed concurrently.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            context.ChangeTracker.Clear();
            var winner = await context.WorkflowTriggerBindings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (winner is not null && StringComparer.Ordinal.Equals(RuntimeArtifactJson.Serialize(Read(winner, scope, binding.TriggerBindingId)), RuntimeArtifactJson.Serialize(binding)))
                return binding;
            throw new InvalidOperationException($"Workflow trigger binding '{binding.TriggerBindingId}' changed concurrently.", exception);
        }
    }

    public async ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<WorkflowTriggerBinding> bindings, CancellationToken cancellationToken = default)
    {
        ValidateActivation(activationId, bindings);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var state = await context.WorkflowTriggerBindingProjectionStates.SingleOrDefaultAsync(x =>
            x.Id == ProjectionId(scope, activationId), cancellationToken);
        var existing = await RowsForActivation(scope, activationId, cancellationToken);
        var prepared = bindings.Select(x => x with { IsActive = false }).ToArray();
        if (state is not null)
        {
            ActivationProjectionStateLifecycle.EnsurePreparable(state.IsActive, state.Revision, "trigger-binding", activationId);
            if (ProjectionMatches(state, existing, scope, activationId) && ProjectionsEqual(existing, prepared, scope))
            {
                await context.CommitAndClearAsync(transaction, cancellationToken, noChanges: true);
                return;
            }
            throw new InvalidOperationException($"Trigger-binding activation projection '{activationId}' is already prepared with different state.");
        }

        var byId = existing.ToDictionary(x => Decode(x.TriggerBindingId), StringComparer.Ordinal);
        var desired = prepared.ToDictionary(x => x.TriggerBindingId, StringComparer.Ordinal);
        foreach (var row in existing)
        {
            if (desired.TryGetValue(Decode(row.TriggerBindingId), out var replacement))
                Copy(row, replacement, scope, checked(row.Revision + 1));
            else
                context.WorkflowTriggerBindings.Remove(row);
        }
        foreach (var binding in prepared.Where(x => !byId.ContainsKey(x.TriggerBindingId)))
            context.WorkflowTriggerBindings.Add(ToEntity(binding, scope, Id(scope, binding.TriggerBindingId), 1));
        context.WorkflowTriggerBindingProjectionStates.Add(new WorkflowTriggerBindingProjectionStateEntity
        {
            Id = ProjectionId(scope, activationId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
            ActivationId = Encode(activationId), ActivationIdHash = Hash(activationId), ActivationIdOrderKey = Order(activationId),
            IsActive = false, BindingCount = prepared.Length, ProjectionFingerprint = Fingerprint(prepared),
            ContentJson = Fingerprint(prepared),
            SchemaVersion = RuntimeTriggerBindingEfModule.SchemaVersion, Revision = ActivationProjectionStateLifecycle.CreationRevision
        });
        try
        {
            await context.CommitAndClearAsync(transaction, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await context.RollbackAndClearAsync(transaction);
            throw new InvalidOperationException($"Trigger-binding activation projection '{activationId}' changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            await context.RollbackAndClearAsync(transaction);
            var winner = await context.WorkflowTriggerBindingProjectionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, activationId), cancellationToken);
            var winnerRows = await RowsForActivation(scope, activationId, cancellationToken);
            if (winner is not null &&
                ActivationProjectionStateLifecycle.Read(winner.IsActive, winner.Revision) == WorkflowActivationProjectionState.Prepared &&
                ProjectionMatches(winner, winnerRows, scope, activationId) && ProjectionsEqual(winnerRows, prepared, scope))
                return;
            throw new InvalidOperationException($"Trigger-binding activation projection '{activationId}' changed concurrently with different state.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            await context.RollbackAndClearAsync(transaction);
            throw new InvalidOperationException($"Trigger-binding activation projection '{activationId}' could not be committed.", exception);
        }
    }

    public async ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(WorkflowTriggerBindingActivationPageQuery query, CancellationToken cancellationToken = default) =>
        await QueryPageAsync(query, query.ActivationId, x => x.ActivationId == Encode(query.ActivationId), cancellationToken);

    public async ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        if (replacedActivationId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(replacedActivationId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var operation = $"Trigger-binding activation projection '{activationId}'";
        await EfRuntimeOperationalStoreSupport.ProjectionSwitches.RunUntilSettledAsync<bool>(
            context,
            () => TrySwitchAsync(scope, activationId, replacedActivationId, operation, cancellationToken),
            conflict => throw EfRuntimeOperationalStoreSupport.CommitFailure(operation, conflict),
            cancellationToken);
    }

    /// <summary>
    /// One attempt at <see cref="ActivateAsync"/>. It reads both projections first and decides only once neither state moved
    /// while they were read, so a switch, or a deletion and a new preparation, another call committed in between is read
    /// again, never taken for corruption or for a replaced activation that no longer serves. A switch committed after the
    /// reads loses this attempt's write to the revisions it read, and the next attempt finds it made; so does a deadlock that
    /// chose this attempt's write as its victim (#2265).
    /// </summary>
    private async ValueTask<EfWriteAttempt<bool>> TrySwitchAsync(string scope, string activationId, string? replacedActivationId, string operation, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        switch (await StageSwitchAsync(scope, activationId, replacedActivationId, deleteReplaced: false, cancellationToken))
        {
            case ProjectionStaging.Moved:
                await context.RollbackAndClearAsync(transaction);
                return EfWriteAttempt<bool>.Retry();
            case ProjectionStaging.Unchanged:
                await context.CommitAndClearAsync(transaction, cancellationToken, noChanges: true);
                return true;
        }
        return await context.TryCommitAndClearAsync(transaction, EfRuntimeOperationalStoreSupport.ProjectionSwitches, operation, cancellationToken) is { } conflict
            ? EfWriteAttempt<bool>.Retry(conflict)
            : true;
    }

    /// <summary>
    /// Stages <see cref="ActivateAsync"/>'s switch in the caller's transaction, which <c>EfWorkflowActivationSwitch</c>
    /// shares with the slot transition (#2230). It reads both projections and decides only once neither state moved while
    /// they were read. The caller saves, commits, and reads again on <see cref="ProjectionStaging.Moved"/> or a lost write.
    /// With <paramref name="deleteReplaced"/> the replaced projection is deleted rather than switched off, as a revert does.
    /// </summary>
    internal async ValueTask<ProjectionStaging> StageSwitchAsync(string scope, string activationId, string? replacedActivationId, bool deleteReplaced, CancellationToken cancellationToken)
    {
        var candidate = await context.WorkflowTriggerBindingProjectionStates.SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, activationId), cancellationToken);
        var candidateRows = candidate is null ? [] : await RowsForActivation(scope, activationId, cancellationToken);
        var distinct = replacedActivationId is not null && !StringComparer.Ordinal.Equals(activationId, replacedActivationId);
        var replaced = distinct ? await context.WorkflowTriggerBindingProjectionStates.SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, replacedActivationId!), cancellationToken) : null;
        var replacedRows = replaced is null ? [] : await RowsForActivation(scope, replacedActivationId!, cancellationToken);
        if (await StateMovedAsync(scope, activationId, candidate, cancellationToken) ||
            distinct && await StateMovedAsync(scope, replacedActivationId!, replaced, cancellationToken))
            return ProjectionStaging.Moved;

        if (candidate is null)
            throw new InvalidOperationException($"Activation '{activationId}' has no prepared trigger-binding projection.");
        EnsureProjection(candidate, candidateRows, scope, activationId);
        if (replaced is not null)
            EnsureProjection(replaced, replacedRows, scope, replacedActivationId!);
        // The candidate is checked first, so a switch that already happened is a no-op whoever made it.
        if (candidate.IsActive)
        {
            if (replaced is not { IsActive: true })
                return ProjectionStaging.Unchanged;
            throw new InvalidOperationException($"Activation '{activationId}' is active while replaced activation '{replacedActivationId}' is still active.");
        }
        // Switching a candidate on is refused once its replaced activation no longer serves: two activations never serve
        // one slot through a switch.
        if (distinct && replaced is not { IsActive: true })
            throw new InvalidOperationException($"Activation '{activationId}' cannot replace a projection that is missing or no longer active.");
        foreach (var row in candidateRows) SetActive(row, true);
        candidate.IsActive = true; candidate.Revision = checked(candidate.Revision + 1);
        if (replaced is not null && deleteReplaced)
        {
            context.WorkflowTriggerBindings.RemoveRange(replacedRows);
            context.WorkflowTriggerBindingProjectionStates.Remove(replaced);
        }
        else if (replaced is not null)
        {
            foreach (var row in replacedRows) SetActive(row, false);
            replaced.IsActive = false; replaced.Revision = checked(replaced.Revision + 1);
        }
        return ProjectionStaging.Staged;
    }

    /// <summary>
    /// Stages the deletion of an activation's projection in the caller's transaction (#2230). Unlike
    /// <see cref="DeleteByActivationAsync"/>, it reads the state before the rows and again after them, so a switch committed
    /// meanwhile is read again rather than taken for a corrupt projection; with <paramref name="unlessServing"/> a projection
    /// that serves is left alone. Every row and the state are deleted at the revisions read, so a switch that commits before
    /// the caller does makes its commit lose, and the caller reads again.
    /// </summary>
    internal async ValueTask<ProjectionStaging> StageDeletionAsync(string scope, string activationId, bool unlessServing, CancellationToken cancellationToken)
    {
        var state = await context.WorkflowTriggerBindingProjectionStates.SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, activationId), cancellationToken);
        var rows = await RowsForActivation(scope, activationId, cancellationToken);
        if (await StateMovedAsync(scope, activationId, state, cancellationToken))
            return ProjectionStaging.Moved;
        if (unlessServing && state is { IsActive: true })
            return ProjectionStaging.Serves;

        foreach (var row in rows)
            _ = Read(row, scope, Decode(row.TriggerBindingId));
        if (state is not null)
        {
            EnsureProjection(state, rows, scope, activationId);
            context.WorkflowTriggerBindingProjectionStates.Remove(state);
        }
        context.WorkflowTriggerBindings.RemoveRange(rows);
        return state is null && rows.Length == 0 ? ProjectionStaging.Unchanged : ProjectionStaging.Staged;
    }

    /// <summary>The context every staged change writes through; a switch commits only stores that share it.</summary>
    internal RuntimeDbContext Context => context;

    public async ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(string activationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        // The answer is the state's alone, so rows that match it confirm it whenever they were read. Rows that do not match
        // it are corrupt only if the state stood still while they were read; otherwise a switch, or a deletion and a new
        // preparation, committed in between, and the state is read again (#2265).
        return await EfRuntimeOperationalStoreSupport.ProjectionSwitches.RunUntilSettledAsync<WorkflowActivationProjectionState>(
            context,
            async () =>
            {
                context.ChangeTracker.Clear();
                var state = await context.WorkflowTriggerBindingProjectionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, activationId), cancellationToken);
                if (state is null)
                    return WorkflowActivationProjectionState.Missing;
                var rows = await RowsForActivation(scope, activationId, cancellationToken);
                context.ChangeTracker.Clear();
                if (ProjectionMatches(state, rows, scope, activationId))
                    return ActivationProjectionStateLifecycle.Read(state.IsActive, state.Revision);
                return await StateMovedAsync(scope, activationId, state, cancellationToken)
                    ? EfWriteAttempt<WorkflowActivationProjectionState>.Retry()
                    : throw ProjectionMismatch(activationId);
            },
            _ => throw EfRuntimeOperationalStoreSupport.ChangedConcurrently($"Trigger-binding activation projection '{activationId}'", null),
            cancellationToken);
    }

    /// <summary>
    /// No index covers the slot, so this reads the scope's active bindings once; only deactivation calls it. The rows'
    /// activation ids are deduplicated here rather than with a database <c>DISTINCT</c> over a text column.
    /// </summary>
    public async ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string slotId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var activations = await context.WorkflowTriggerBindings.AsNoTracking()
            .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.IsActive && x.SlotId == Encode(slotId) && x.ActivationId != null)
            .Select(x => x.ActivationId!)
            .ToArrayAsync(cancellationToken);
        return activations.Select(Decode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public async ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Rows, then state, without ActivateAsync's re-read: a switch committed between them fails this loudly as corrupt (#2265).
        var rows = await RowsForActivation(scope, activationId, cancellationToken);
        foreach (var row in rows)
            _ = Read(row, scope, Decode(row.TriggerBindingId));
        context.WorkflowTriggerBindings.RemoveRange(rows);
        var state = await context.WorkflowTriggerBindingProjectionStates.SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, activationId), cancellationToken);
        if (state is not null)
        {
            EnsureProjection(state, rows, scope, activationId);
            context.WorkflowTriggerBindingProjectionStates.Remove(state);
        }
        await context.CommitMutationAndClearAsync(transaction, $"Trigger-binding activation projection '{activationId}' deletion", cancellationToken);
    }

    public async ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var rows = await RowsForArtifact(scope, artifactId, cancellationToken);
        // Artifact lookup is intentionally projection-backed for bounded reads. Validate every
        // matched row, including activation-less rows, before deleting anything so a corrupt
        // authoritative payload cannot be silently removed by the lookup path.
        foreach (var row in rows)
            _ = Read(row, scope);
        foreach (var activation in rows.Select(x => x.ActivationId).Where(x => x is not null).Distinct(StringComparer.Ordinal))
        {
            // Rows, then state, without ActivateAsync's re-read: a switch committed between them fails this loudly as corrupt (#2265).
            var all = await RowsForActivation(scope, Decode(activation!), cancellationToken);
            if (all.Any(x => x.ArtifactId != Encode(artifactId))) throw new InvalidOperationException($"Cannot delete artifact '{artifactId}' because its activation contains another artifact.");
            var state = await context.WorkflowTriggerBindingProjectionStates.SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, Decode(activation!)), cancellationToken);
            if (state is not null) { EnsureProjection(state, all, scope, Decode(activation!)); context.WorkflowTriggerBindingProjectionStates.Remove(state); }
        }
        context.WorkflowTriggerBindings.RemoveRange(rows);
        await context.CommitMutationAndClearAsync(transaction, $"Trigger-binding artifact '{artifactId}' deletion", cancellationToken);
        return rows.Length;
    }

    public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) =>
        QueryPageAsync(query, $"exact\0{query.StimulusType}\0{query.StimulusHash}", x => x.StimulusType == Encode(query.StimulusType) && x.StimulusHash == Encode(query.StimulusHash) && x.IsActive, cancellationToken);

    public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) =>
        QueryPageAsync(query, query.ArtifactId, x => x.ArtifactId == Encode(query.ArtifactId), cancellationToken);

    public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) =>
        QueryPageAsync(query, $"type\0{query.StimulusType}", x => x.StimulusType == Encode(query.StimulusType) && x.IsActive, cancellationToken);

    /// <summary>
    /// The population <see cref="ListByStimulusTypeAsync"/> pages, projected to its stimulus identities in the database
    /// under the two rules <see cref="EfBookmarkStateStore.ListWaitingStimulusHashesByTypeAsync"/> explains, over
    /// <see cref="RuntimeTriggerBindingEfModule.RouteConvergenceIndexName"/>.
    /// </summary>
    public async ValueTask<IReadOnlyCollection<string>> ListActiveStimulusHashesAsync(string stimulusType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        // Hash-only matching, where the page scan also compares the encoded scope and type: two scopes or types share a
        // hash only through a SHA-256 collision. A colliding or corrupt row can at worst make this fingerprint differ from
        // the last refresh's and so cause a rebuild, never a wrong route: the rebuild reads through ListByStimulusTypeAsync,
        // which leaves a colliding row out by its encoded columns and validates every row it reads (Read).
        var identities = await context.WorkflowTriggerBindings.AsNoTracking()
            .Where(x => x.ScopeKeyHash == Hash(scope) && x.StimulusTypeLookupKey == Lookup(stimulusType) && x.IsActive)
            .Select(x => new { x.StimulusLookupKey, x.StimulusHash })
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return StimulusHashes.DistinctOrdinal(identities.Select(x => Decode(x.StimulusHash)));
    }

    private async ValueTask<WorkflowTriggerBindingPage> QueryPageAsync(WorkflowTriggerBindingPageRequest query, string binding, System.Linq.Expressions.Expression<Func<WorkflowTriggerBindingEntity, bool>> predicate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query); cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope(); var source = context.WorkflowTriggerBindings.AsNoTracking().Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope)).Where(predicate);
        var total = await source.LongCountAsync(cancellationToken);
        var cursor = DecodeCursor(query.ContinuationToken, query, scope, binding);
        if (cursor is not null) source = source.Where(x => x.TriggerBindingIdOrderKey.CompareTo(cursor) > 0);
        var rows = await source.OrderBy(x => x.TriggerBindingIdOrderKey).Take(checked(query.Limit + 1)).ToArrayAsync(cancellationToken);
        var more = rows.Length > query.Limit; if (more) rows = rows[..query.Limit];
        var items = rows.Select(x => Read(x, scope, Decode(x.TriggerBindingId))).ToArray();
        var next = more ? EncodeCursor(query, scope, binding, items[^1].TriggerBindingId) : null;
        return new WorkflowTriggerBindingPage(query, items, total, next);
    }

    private async Task<WorkflowTriggerBindingEntity[]> RowsForActivation(string scope, string activationId, CancellationToken ct)
    {
        var rows = new List<WorkflowTriggerBindingEntity>();
        string? after = null;
        while (true)
        {
            var page = await context.WorkflowTriggerBindings
                .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ActivationIdHash == Hash(activationId) && x.ActivationId == Encode(activationId) && (after == null || x.TriggerBindingIdOrderKey.CompareTo(after) > 0))
                .OrderBy(x => x.TriggerBindingIdOrderKey)
                .Take(MaterializationBatchSize)
                .ToArrayAsync(ct);
            rows.AddRange(page);
            if (page.Length < MaterializationBatchSize) return rows.ToArray();
            after = page[^1].TriggerBindingIdOrderKey;
        }
    }

    private async Task<WorkflowTriggerBindingEntity[]> RowsForArtifact(string scope, string artifactId, CancellationToken ct)
    {
        var rows = new List<WorkflowTriggerBindingEntity>();
        string? after = null;
        while (true)
        {
            var page = await context.WorkflowTriggerBindings
                .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ArtifactIdHash == Hash(artifactId) && x.ArtifactId == Encode(artifactId) && (after == null || x.TriggerBindingIdOrderKey.CompareTo(after) > 0))
                .OrderBy(x => x.TriggerBindingIdOrderKey)
                .Take(MaterializationBatchSize)
                .ToArrayAsync(ct);
            rows.AddRange(page);
            if (page.Length < MaterializationBatchSize) return rows.ToArray();
            after = page[^1].TriggerBindingIdOrderKey;
        }
    }

    private Task<bool> StateMovedAsync(string scope, string activationId, WorkflowTriggerBindingProjectionStateEntity? read, CancellationToken ct) =>
        EfRuntimeOperationalStoreSupport.StateMovedAsync(context.WorkflowTriggerBindingProjectionStates.Where(x => x.Id == ProjectionId(scope, activationId)), read, ct);

    private string RequireScope() => EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
    private static string Encode(string value) => EfRuntimeOperationalStoreSupport.Encode(value);
    private static string Decode(string value) => EfRuntimeOperationalStoreSupport.Decode(value);
    private static string Hash(string value) => EfRuntimeOperationalStoreSupport.Hash(value);
    private static string Order(string value) => EfRuntimeOperationalStoreSupport.Order(value);
    private static string Id(string scope, string bindingId) => EfRuntimeOperationalStoreSupport.CompositeId(scope, bindingId);
    private static string ProjectionId(string scope, string activationId) => EfRuntimeOperationalStoreSupport.CompositeId(scope, ProjectionKind, activationId);
    private static string Lookup(string type, string hash) => Hash($"{type.Length}:{type}{hash}");
    private static string Lookup(string type) => Hash(type);

    private static WorkflowTriggerBindingEntity ToEntity(WorkflowTriggerBinding x, string scope, string id, long revision) { var e = new WorkflowTriggerBindingEntity(); Copy(e, x, scope, revision); e.Id = id; return e; }
    private static void Copy(WorkflowTriggerBindingEntity e, WorkflowTriggerBinding x, string scope, long revision)
    { Validate(x); e.ScopeKey = Encode(scope); e.ScopeKeyHash = Hash(scope); e.TriggerBindingId = Encode(x.TriggerBindingId); e.TriggerBindingIdHash = Hash(x.TriggerBindingId); e.TriggerBindingIdOrderKey = Order(x.TriggerBindingId); e.ArtifactId = Encode(x.ArtifactId); e.ArtifactIdHash = Hash(x.ArtifactId); e.ArtifactIdOrderKey = Order(x.ArtifactId); e.DefinitionId = Encode(x.DefinitionId); e.ArtifactVersion = Encode(x.ArtifactVersion); e.ArtifactHash = Encode(x.ArtifactHash); e.ExecutableNodeId = Encode(x.ExecutableNodeId); e.StimulusType = Encode(x.StimulusType); e.StimulusHash = Encode(x.StimulusHash); e.StimulusLookupKey = Lookup(x.StimulusType, x.StimulusHash); e.StimulusTypeLookupKey = Lookup(x.StimulusType); e.CorrelationScope = x.CorrelationScope is null ? null : Encode(x.CorrelationScope); e.ActivationId = x.ActivationId is null ? null : Encode(x.ActivationId); e.ActivationIdHash = x.ActivationId is null ? null : Hash(x.ActivationId); e.ActivationIdOrderKey = x.ActivationId is null ? null : Order(x.ActivationId); e.SlotId = x.SlotId is null ? null : Encode(x.SlotId); e.Cardinality = (int)x.Cardinality; e.IsActive = x.IsActive; e.CreatedAtUtcTicks = x.CreatedAt.UtcTicks; e.CreatedAtOffsetMinutes = (int)x.CreatedAt.Offset.TotalMinutes; e.ContentJson = RuntimeArtifactJson.Serialize(x); e.SchemaVersion = RuntimeTriggerBindingEfModule.SchemaVersion; e.Revision = revision; }
    private static WorkflowTriggerBinding Read(WorkflowTriggerBindingEntity e, string scope, string? expectedId = null)
    {
        if (EfSchemaVersion.NotReadable(RuntimeTriggerBindingEfModule.Chain, e.SchemaVersion) || e.Id != Id(scope, Decode(e.TriggerBindingId)) || e.ScopeKey != Encode(scope) || e.ScopeKeyHash != Hash(scope) || expectedId is not null && e.TriggerBindingId != Encode(expectedId) || e.Revision <= 0)
            throw new InvalidDataException("The persisted EF trigger-binding row does not match its identity envelope.");
        WorkflowTriggerBinding x;
        try
        {
            x = RuntimeArtifactJson.Deserialize<WorkflowTriggerBinding>(RuntimeTriggerBindingEfModule.Chain.Upcast<WorkflowTriggerBindingEntity>(e.SchemaVersion, (nameof(e.ContentJson), e.ContentJson))[nameof(e.ContentJson)]!);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The persisted EF trigger-binding content is not valid current JSON.", exception);
        }
        Validate(x);
        if (x.TriggerBindingId != Decode(e.TriggerBindingId) || x.ArtifactId != Decode(e.ArtifactId) || x.DefinitionId != Decode(e.DefinitionId) || x.ArtifactVersion != Decode(e.ArtifactVersion) || x.ArtifactHash != Decode(e.ArtifactHash) || x.ExecutableNodeId != Decode(e.ExecutableNodeId) || x.StimulusType != Decode(e.StimulusType) || x.StimulusHash != Decode(e.StimulusHash) || x.CorrelationScope != DecodeOptional(e.CorrelationScope) || x.ActivationId != DecodeOptional(e.ActivationId) || x.SlotId != DecodeOptional(e.SlotId) || x.Cardinality != (TriggerCardinality)e.Cardinality || x.IsActive != e.IsActive || x.CreatedAt.UtcTicks != e.CreatedAtUtcTicks || (int)x.CreatedAt.Offset.TotalMinutes != e.CreatedAtOffsetMinutes || e.TriggerBindingIdHash != Hash(x.TriggerBindingId) || e.TriggerBindingIdOrderKey != Order(x.TriggerBindingId) || e.ArtifactIdHash != Hash(x.ArtifactId) || e.ArtifactIdOrderKey != Order(x.ArtifactId) || e.StimulusLookupKey != Lookup(x.StimulusType, x.StimulusHash) || e.StimulusTypeLookupKey != Lookup(x.StimulusType) || x.ActivationId is not null && (e.ActivationIdHash != Hash(x.ActivationId) || e.ActivationIdOrderKey != Order(x.ActivationId)) || x.ActivationId is null && (e.ActivationIdHash is not null || e.ActivationIdOrderKey is not null))
            throw new InvalidDataException("The persisted EF trigger-binding content does not match its authoritative projections.");
        return x;
    }
    private static string? DecodeOptional(string? value) => value is null ? null : Decode(value);
    private static void Validate(WorkflowTriggerBinding x) { ArgumentNullException.ThrowIfNull(x); WorkflowTriggerBinding.ValidateId(x.TriggerBindingId); foreach (var value in new[] { x.ArtifactId, x.DefinitionId, x.ArtifactVersion, x.ArtifactHash, x.ExecutableNodeId, x.StimulusType, x.StimulusHash }) ArgumentException.ThrowIfNullOrWhiteSpace(value); if (x.StimulusType.Length > RuntimeTriggerBindingEfModule.StimulusTypeMaximumLength) throw new ArgumentException($"Stimulus type cannot exceed {RuntimeTriggerBindingEfModule.StimulusTypeMaximumLength} characters.", nameof(x)); if (x.StimulusHash.Length > RuntimeTriggerBindingEfModule.IdentityMaximumLength) throw new ArgumentException($"Stimulus hash cannot exceed {RuntimeTriggerBindingEfModule.IdentityMaximumLength} characters.", nameof(x)); if (!Enum.IsDefined(x.Cardinality)) throw new ArgumentOutOfRangeException(nameof(x.Cardinality)); ArgumentNullException.ThrowIfNull(x.Metadata); if (x.ActivationId is null && x.SlotId is not null) throw new ArgumentException("A trigger binding slot requires an activation.", nameof(x)); }
    // Invariant: every caller reaches SetActive only after ProjectionMatches has already called Read
    // (eagerly, via .ToArray()) on this same row, so the version check has already passed and this
    // Deserialize call cannot observe a skewed row's changed shape as corruption. The content is rewritten at the
    // current version, so the stamp moves with it: a row read at an older version is upgraded here.
    private static void SetActive(WorkflowTriggerBindingEntity row, bool active) { var binding = RuntimeArtifactJson.Deserialize<WorkflowTriggerBinding>(RuntimeTriggerBindingEfModule.Chain.Upcast<WorkflowTriggerBindingEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!); row.IsActive = active; row.ContentJson = RuntimeArtifactJson.Serialize(binding with { IsActive = active }); row.SchemaVersion = RuntimeTriggerBindingEfModule.SchemaVersion; row.Revision = checked(row.Revision + 1); }
    private static void ValidateActivation(string id, IReadOnlyCollection<WorkflowTriggerBinding> xs) { ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentNullException.ThrowIfNull(xs); var ids = new HashSet<string>(StringComparer.Ordinal); foreach (var x in xs) { Validate(x); if (x.ActivationId != id || string.IsNullOrWhiteSpace(x.SlotId) || !ids.Add(x.TriggerBindingId)) throw new ArgumentException("Activation bindings must have matching activation, slot, and unique binding ids.", nameof(xs)); } }
    private static bool ProjectionsEqual(IEnumerable<WorkflowTriggerBindingEntity> rows, IEnumerable<WorkflowTriggerBinding> xs, string scope) => Fingerprint(rows.Select(x => Read(x, scope))) == Fingerprint(xs);
    private static bool ProjectionMatches(WorkflowTriggerBindingProjectionStateEntity state, IEnumerable<WorkflowTriggerBindingEntity> rows, string scope, string activation)
    {
        var bindings = rows.Select(row => Read(row, scope)).ToArray();
        var fingerprint = Fingerprint(bindings);
        return EfSchemaVersion.Readable(RuntimeTriggerBindingEfModule.Chain, state.SchemaVersion) &&
               state.Id == ProjectionId(scope, activation) &&
               state.ScopeKey == Encode(scope) && state.ScopeKeyHash == Hash(scope) &&
               state.ActivationId == Encode(activation) && state.ActivationIdHash == Hash(activation) &&
               state.ActivationIdOrderKey == Order(activation) &&
               state.Revision > 0 &&
               state.BindingCount == bindings.Length &&
               state.ProjectionFingerprint == fingerprint &&
               RuntimeTriggerBindingEfModule.Chain.Upcast<WorkflowTriggerBindingProjectionStateEntity>(state.SchemaVersion, (nameof(state.ContentJson), state.ContentJson))[nameof(state.ContentJson)] == fingerprint &&
               bindings.All(binding => binding.ActivationId == activation && binding.IsActive == state.IsActive);
    }
    private static void EnsureProjection(WorkflowTriggerBindingProjectionStateEntity state, IEnumerable<WorkflowTriggerBindingEntity> rows, string scope, string activation) { if (!ProjectionMatches(state, rows, scope, activation)) throw ProjectionMismatch(activation); }
    private static InvalidDataException ProjectionMismatch(string activation) => new($"Trigger-binding activation projection '{activation}' does not match its rows.");
    private static string Fingerprint(IEnumerable<WorkflowTriggerBinding> xs) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(xs.Select(x => x with { IsActive = false }).OrderBy(x => x.TriggerBindingId, StringComparer.Ordinal).ToArray()))));

    private static string EncodeCursor(WorkflowTriggerBindingPageRequest q, string scope, string binding, string id) { var payload = Encoding.UTF8.GetBytes($"{Hash(QueryBinding(q))}\0{Hash(scope)}\0{id}"); var sum = SHA256.HashData(payload); return $"tbq1.{B64(payload)}.{B64(sum)}"; }
    private static string? DecodeCursor(string? token, WorkflowTriggerBindingPageRequest q, string scope, string binding) { if (token is null) return null; try { var p = token.Split('.'); if (p is not ["tbq1", var a, var b]) throw new FormatException(); var payload = B64D(a); var sum = B64D(b); if (!CryptographicOperations.FixedTimeEquals(sum, SHA256.HashData(payload))) throw new FormatException(); var parts = Encoding.UTF8.GetString(payload).Split('\0'); if (parts.Length != 3 || parts[0] != Hash(QueryBinding(q)) || parts[1] != Hash(scope) || string.IsNullOrWhiteSpace(parts[2])) throw new FormatException(); return Order(parts[2]); } catch (Exception e) when (e is FormatException or ArgumentException) { throw new ArgumentException("The trigger-binding continuation token is invalid or belongs to another query.", nameof(token), e); } }
    private static string QueryBinding(WorkflowTriggerBindingPageRequest q) => q switch { WorkflowTriggerBindingPageQuery x => $"exact\0{x.StimulusType}\0{x.StimulusHash}", WorkflowTriggerBindingTypePageQuery x => $"type\0{x.StimulusType}", WorkflowTriggerBindingActivationPageQuery x => $"activation\0{x.ActivationId}", WorkflowTriggerBindingArtifactPageQuery x => $"artifact\0{x.ArtifactId}", _ => throw new ArgumentOutOfRangeException(nameof(q)) };
    private static string B64(byte[] x) => Convert.ToBase64String(x).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] B64D(string x) => Convert.FromBase64String(x.Replace('-', '+').Replace('_', '/').PadRight(x.Length + ((4 - x.Length % 4) % 4), '='));
}
