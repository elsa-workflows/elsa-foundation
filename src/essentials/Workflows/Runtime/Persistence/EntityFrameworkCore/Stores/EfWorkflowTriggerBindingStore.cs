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
            if (!state.IsActive && ProjectionMatches(state, existing, scope, activationId) && ProjectionsEqual(existing, prepared, scope))
            {
                await CommitAndClearAsync(transaction, cancellationToken, noChanges: true);
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
            SchemaVersion = RuntimeTriggerBindingEfModule.SchemaVersion, Revision = 1
        });
        try
        {
            await CommitAndClearAsync(transaction, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAndClearAsync(transaction);
            throw new InvalidOperationException($"Trigger-binding activation projection '{activationId}' changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            await RollbackAndClearAsync(transaction);
            var winner = await context.WorkflowTriggerBindingProjectionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, activationId), cancellationToken);
            var winnerRows = await RowsForActivation(scope, activationId, cancellationToken);
            if (winner is { IsActive: false } && ProjectionMatches(winner, winnerRows, scope, activationId) && ProjectionsEqual(winnerRows, prepared, scope))
                return;
            throw new InvalidOperationException($"Trigger-binding activation projection '{activationId}' changed concurrently with different state.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            await RollbackAndClearAsync(transaction);
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
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var candidate = await context.WorkflowTriggerBindingProjectionStates.SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, activationId), cancellationToken)
            ?? throw new InvalidOperationException($"Activation '{activationId}' has no prepared trigger-binding projection.");
        var candidateRows = await RowsForActivation(scope, activationId, cancellationToken);
        EnsureProjection(candidate, candidateRows, scope, activationId);
        var distinct = replacedActivationId is not null && !StringComparer.Ordinal.Equals(activationId, replacedActivationId);
        WorkflowTriggerBindingProjectionStateEntity? replaced = null;
        WorkflowTriggerBindingEntity[] replacedRows = [];
        if (distinct)
        {
            replaced = await context.WorkflowTriggerBindingProjectionStates.SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, replacedActivationId!), cancellationToken);
            if (replaced is null || !replaced.IsActive) throw new InvalidOperationException($"Activation '{activationId}' cannot replace a projection that is missing or no longer active.");
            replacedRows = await RowsForActivation(scope, replacedActivationId!, cancellationToken);
            EnsureProjection(replaced, replacedRows, scope, replacedActivationId!);
        }
        if (candidate.IsActive)
        {
            if (!distinct || replaced is null || !replaced.IsActive)
            {
                await CommitAndClearAsync(transaction, cancellationToken, noChanges: true);
                return;
            }
            throw new InvalidOperationException($"Activation '{activationId}' is active while replaced activation '{replacedActivationId}' is still active.");
        }
        foreach (var row in candidateRows) SetActive(row, true);
        candidate.IsActive = true; candidate.Revision = checked(candidate.Revision + 1);
        if (replaced is not null)
        {
            foreach (var row in replacedRows) SetActive(row, false);
            replaced.IsActive = false; replaced.Revision = checked(replaced.Revision + 1);
        }
        await CommitMutationAndClearAsync(transaction, cancellationToken, $"Trigger-binding activation projection '{activationId}'");
    }

    public async ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
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
        await CommitMutationAndClearAsync(transaction, cancellationToken, $"Trigger-binding activation projection '{activationId}' deletion");
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
            var all = await RowsForActivation(scope, Decode(activation!), cancellationToken);
            if (all.Any(x => x.ArtifactId != Encode(artifactId))) throw new InvalidOperationException($"Cannot delete artifact '{artifactId}' because its activation contains another artifact.");
            var state = await context.WorkflowTriggerBindingProjectionStates.SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, Decode(activation!)), cancellationToken);
            if (state is not null) { EnsureProjection(state, all, scope, Decode(activation!)); context.WorkflowTriggerBindingProjectionStates.Remove(state); }
        }
        context.WorkflowTriggerBindings.RemoveRange(rows);
        await CommitMutationAndClearAsync(transaction, cancellationToken, $"Trigger-binding artifact '{artifactId}' deletion");
        return rows.Length;
    }

    public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) =>
        QueryPageAsync(query, $"exact\0{query.StimulusType}\0{query.StimulusHash}", x => x.StimulusType == Encode(query.StimulusType) && x.StimulusHash == Encode(query.StimulusHash) && x.IsActive, cancellationToken);

    public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) =>
        QueryPageAsync(query, query.ArtifactId, x => x.ArtifactId == Encode(query.ArtifactId), cancellationToken);

    public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) =>
        QueryPageAsync(query, $"type\0{query.StimulusType}", x => x.StimulusType == Encode(query.StimulusType) && x.IsActive, cancellationToken);

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

    private async Task CommitAndClearAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct, bool noChanges = false)
    { if (!noChanges) await context.SaveChangesAsync(ct); await transaction.CommitAsync(ct); context.ChangeTracker.Clear(); }

    private async Task CommitMutationAndClearAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct, string operation)
    {
        try
        {
            await CommitAndClearAsync(transaction, ct);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAndClearAsync(transaction);
            throw new InvalidOperationException($"{operation} changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Transient))
        {
            await RollbackAndClearAsync(transaction);
            throw new InvalidOperationException($"{operation} encountered a transient write conflict; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            await RollbackAndClearAsync(transaction);
            throw new InvalidOperationException($"{operation} could not be committed.", exception);
        }
    }

    private async Task RollbackAndClearAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(); } catch { }
        context.ChangeTracker.Clear();
    }

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
        if (EfSchemaVersion.NotReadable("RuntimeTriggerBinding", e.SchemaVersion, RuntimeTriggerBindingEfModule.SchemaVersion) || e.Id != Id(scope, Decode(e.TriggerBindingId)) || e.ScopeKey != Encode(scope) || e.ScopeKeyHash != Hash(scope) || expectedId is not null && e.TriggerBindingId != Encode(expectedId) || e.Revision <= 0)
            throw new InvalidDataException("The persisted EF trigger-binding row does not match its identity envelope.");
        WorkflowTriggerBinding x;
        try
        {
            x = RuntimeArtifactJson.Deserialize<WorkflowTriggerBinding>(e.ContentJson);
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
    private static void SetActive(WorkflowTriggerBindingEntity row, bool active) { var binding = RuntimeArtifactJson.Deserialize<WorkflowTriggerBinding>(row.ContentJson); row.IsActive = active; row.ContentJson = RuntimeArtifactJson.Serialize(binding with { IsActive = active }); row.Revision = checked(row.Revision + 1); }
    private static void ValidateActivation(string id, IReadOnlyCollection<WorkflowTriggerBinding> xs) { ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentNullException.ThrowIfNull(xs); var ids = new HashSet<string>(StringComparer.Ordinal); foreach (var x in xs) { Validate(x); if (x.ActivationId != id || string.IsNullOrWhiteSpace(x.SlotId) || !ids.Add(x.TriggerBindingId)) throw new ArgumentException("Activation bindings must have matching activation, slot, and unique binding ids.", nameof(xs)); } }
    private static bool ProjectionsEqual(IEnumerable<WorkflowTriggerBindingEntity> rows, IEnumerable<WorkflowTriggerBinding> xs, string scope) => Fingerprint(rows.Select(x => Read(x, scope))) == Fingerprint(xs);
    private static bool ProjectionMatches(WorkflowTriggerBindingProjectionStateEntity state, IEnumerable<WorkflowTriggerBindingEntity> rows, string scope, string activation)
    {
        var bindings = rows.Select(row => Read(row, scope)).ToArray();
        var fingerprint = Fingerprint(bindings);
        return EfSchemaVersion.Readable("RuntimeTriggerBinding", state.SchemaVersion, RuntimeTriggerBindingEfModule.SchemaVersion) &&
               state.Id == ProjectionId(scope, activation) &&
               state.ScopeKey == Encode(scope) && state.ScopeKeyHash == Hash(scope) &&
               state.ActivationId == Encode(activation) && state.ActivationIdHash == Hash(activation) &&
               state.ActivationIdOrderKey == Order(activation) &&
               state.Revision > 0 &&
               state.BindingCount == bindings.Length &&
               state.ProjectionFingerprint == fingerprint && state.ContentJson == fingerprint &&
               bindings.All(binding => binding.ActivationId == activation && binding.IsActive == state.IsActive);
    }
    private static void EnsureProjection(WorkflowTriggerBindingProjectionStateEntity state, IEnumerable<WorkflowTriggerBindingEntity> rows, string scope, string activation) { if (!ProjectionMatches(state, rows, scope, activation)) throw new InvalidDataException($"Trigger-binding activation projection '{activation}' does not match its rows."); }
    private static string Fingerprint(IEnumerable<WorkflowTriggerBinding> xs) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(xs.Select(x => x with { IsActive = false }).OrderBy(x => x.TriggerBindingId, StringComparer.Ordinal).ToArray()))));

    private static string EncodeCursor(WorkflowTriggerBindingPageRequest q, string scope, string binding, string id) { var payload = Encoding.UTF8.GetBytes($"{Hash(QueryBinding(q))}\0{Hash(scope)}\0{id}"); var sum = SHA256.HashData(payload); return $"tbq1.{B64(payload)}.{B64(sum)}"; }
    private static string? DecodeCursor(string? token, WorkflowTriggerBindingPageRequest q, string scope, string binding) { if (token is null) return null; try { var p = token.Split('.'); if (p is not ["tbq1", var a, var b]) throw new FormatException(); var payload = B64D(a); var sum = B64D(b); if (!CryptographicOperations.FixedTimeEquals(sum, SHA256.HashData(payload))) throw new FormatException(); var parts = Encoding.UTF8.GetString(payload).Split('\0'); if (parts.Length != 3 || parts[0] != Hash(QueryBinding(q)) || parts[1] != Hash(scope) || string.IsNullOrWhiteSpace(parts[2])) throw new FormatException(); return Order(parts[2]); } catch (Exception e) when (e is FormatException or ArgumentException) { throw new ArgumentException("The trigger-binding continuation token is invalid or belongs to another query.", nameof(token), e); } }
    private static string QueryBinding(WorkflowTriggerBindingPageRequest q) => q switch { WorkflowTriggerBindingPageQuery x => $"exact\0{x.StimulusType}\0{x.StimulusHash}", WorkflowTriggerBindingTypePageQuery x => $"type\0{x.StimulusType}", WorkflowTriggerBindingActivationPageQuery x => $"activation\0{x.ActivationId}", WorkflowTriggerBindingArtifactPageQuery x => $"artifact\0{x.ArtifactId}", _ => throw new ArgumentOutOfRangeException(nameof(q)) };
    private static string B64(byte[] x) => Convert.ToBase64String(x).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] B64D(string x) => Convert.FromBase64String(x.Replace('-', '+').Replace('_', '/').PadRight(x.Length + ((4 - x.Length % 4) % 4), '='));
}
