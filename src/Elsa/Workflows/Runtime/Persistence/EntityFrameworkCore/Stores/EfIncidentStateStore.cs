using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core incident-state store (R18).</summary>
/// <remarks>
/// Incident identity is the pair of workflow-execution ID and incident ID. The scope is part of the
/// physical key and every operation requires one ordinary scope, so a caller cannot read or write another
/// tenant's incident by knowing its IDs.
/// </remarks>
public sealed class EfIncidentStateStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IIncidentStateStore
{
    private const int ProviderPageSize = RuntimeStorePageRequest.MaximumLimit;

    public async ValueTask<bool> TryAddAsync(
        IncidentState state,
        CancellationToken cancellationToken = default)
    {
        Validate(state);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.IncidentId);
        context.ChangeTracker.Clear();

        var existing = await context.IncidentStates.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (existing is not null)
        {
            _ = ReadChecked(existing, scope, state.WorkflowExecutionId, state.IncidentId);
            return false;
        }

        context.IncidentStates.Add(ToEntity(state, scope, id, 1));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            context.ChangeTracker.Clear();
            var winner = await context.IncidentStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
            if (winner is null)
                throw new InvalidOperationException("The incident creation conflicted, but the winning row could not be reloaded.", exception);

            _ = ReadChecked(winner, scope, state.WorkflowExecutionId, state.IncidentId);
            return false;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The incident creation changed concurrently; retry the operation.", exception);
        }
    }

    public async ValueTask<IncidentState> SaveAsync(
        IncidentState state,
        CancellationToken cancellationToken = default)
    {
        Validate(state);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.IncidentId);
        context.ChangeTracker.Clear();

        var row = await context.IncidentStates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null)
            context.IncidentStates.Add(ToEntity(state, scope, id, 1));
        else
        {
            var existing = ReadChecked(row, scope, state.WorkflowExecutionId, state.IncidentId);
            IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(existing, state);
            Copy(row, state, scope, checked(row.Revision + 1));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return state;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The incident state changed concurrently; retry the operation.", exception);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The incident state changed concurrently; retry the operation.", exception);
        }
    }

    public async ValueTask<IncidentState?> FindAsync(
        string workflowExecutionId,
        string incidentId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, incidentId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, incidentId);
        var row = await context.IncidentStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return row is null ? null : ReadChecked(row, scope, workflowExecutionId, incidentId);
    }

    public ValueTask<int> CountAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        EfRuntimeOperationalStoreSupport.ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        return CountCoreAsync(workflowExecutionId, cancellationToken);
    }

    public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default) =>
        ListCoreAsync(workflowExecutionId, blockingOnly: false, cancellationToken);

    public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default) =>
        ListCoreAsync(workflowExecutionId, blockingOnly: true, cancellationToken);

    /// <summary>Reads an entity and verifies every authoritative and query projection.</summary>
    internal static IncidentState ReadChecked(
        IncidentStateEntity row,
        string scope,
        string? expectedWorkflowExecutionId = null,
        string? expectedIncidentId = null)
    {
        try
        {
            if (row.Revision <= 0 ||
                row.SchemaVersion != RuntimeOperationalStateEfModule.SchemaVersion ||
                row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
                row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
            {
                throw new InvalidDataException("The incident-state row scope, schema, or revision projection is corrupt.");
            }

            var state = RuntimeArtifactJson.Deserialize<IncidentState>(row.ContentJson);
            Validate(state);
            if ((expectedWorkflowExecutionId is not null && state.WorkflowExecutionId != expectedWorkflowExecutionId) ||
                (expectedIncidentId is not null && state.IncidentId != expectedIncidentId) ||
                row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.IncidentId) ||
                row.WorkflowExecutionId != EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId) ||
                row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId) ||
                row.WorkflowExecutionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId) ||
                row.IncidentId != EfRuntimeOperationalStoreSupport.Encode(state.IncidentId) ||
                row.IncidentIdHash != EfRuntimeOperationalStoreSupport.Hash(state.IncidentId) ||
                row.IncidentIdOrderKey != EfRuntimeOperationalStoreSupport.Order(state.IncidentId) ||
                row.Status != (int)state.Status ||
                row.Severity != (int)state.Severity ||
                row.CreatedAtUtcTicks != state.CreatedAt.UtcTicks ||
                row.ResolvedAtUtcTicks != state.ResolvedAt?.UtcTicks)
            {
                throw new InvalidDataException("The incident-state row identity or projection does not match its current content.");
            }

            return state;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted incident state is not valid current data.", exception);
        }
    }

    private async ValueTask<int> CountCoreAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var query = QueryForWorkflow(scope, workflowExecutionId);
        var count = await query.CountAsync(cancellationToken);
        return count;
    }

    private async ValueTask<IReadOnlyCollection<IncidentState>> ListCoreAsync(
        string workflowExecutionId,
        bool blockingOnly,
        CancellationToken cancellationToken)
    {
        EfRuntimeOperationalStoreSupport.ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var query = QueryForWorkflow(scope, workflowExecutionId);
        var result = new List<IncidentState>();
        string? lastOrderKey = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = query;
            if (lastOrderKey is not null)
                page = page.Where(row => string.Compare(row.IncidentIdOrderKey, lastOrderKey) > 0);
            var rows = await page
                .OrderBy(row => row.IncidentIdOrderKey)
                .Take(ProviderPageSize + 1)
                .ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > ProviderPageSize;
            if (hasNext)
                rows = rows[..ProviderPageSize];

            foreach (var row in rows)
            {
                var state = ReadChecked(row, scope, workflowExecutionId);
                if (!blockingOnly || state.Status == IncidentStatus.Blocking)
                    result.Add(state);
            }

            if (!hasNext)
                return result;
            lastOrderKey = rows[^1].IncidentIdOrderKey;
        }
    }

    private IQueryable<IncidentStateEntity> QueryForWorkflow(string scope, string workflowExecutionId) =>
        context.IncidentStates.AsNoTracking().Where(row =>
            row.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            row.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            row.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId) &&
            row.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId));

    internal static IncidentStateEntity ToEntity(IncidentState state, string scope, string id, long revision) => new()
    {
        Id = id,
        ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope),
        ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
        WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId),
        WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId),
        WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId),
        IncidentId = EfRuntimeOperationalStoreSupport.Encode(state.IncidentId),
        IncidentIdHash = EfRuntimeOperationalStoreSupport.Hash(state.IncidentId),
        IncidentIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.IncidentId),
        Status = (int)state.Status,
        Severity = (int)state.Severity,
        CreatedAtUtcTicks = state.CreatedAt.UtcTicks,
        ResolvedAtUtcTicks = state.ResolvedAt?.UtcTicks,
        ContentJson = RuntimeArtifactJson.Serialize(state),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = revision
    };

    internal static void Copy(IncidentStateEntity row, IncidentState state, string scope, long revision)
    {
        var replacement = ToEntity(state, scope, row.Id, revision);
        row.ScopeKey = replacement.ScopeKey;
        row.ScopeKeyHash = replacement.ScopeKeyHash;
        row.WorkflowExecutionId = replacement.WorkflowExecutionId;
        row.WorkflowExecutionIdHash = replacement.WorkflowExecutionIdHash;
        row.WorkflowExecutionIdOrderKey = replacement.WorkflowExecutionIdOrderKey;
        row.IncidentId = replacement.IncidentId;
        row.IncidentIdHash = replacement.IncidentIdHash;
        row.IncidentIdOrderKey = replacement.IncidentIdOrderKey;
        row.Status = replacement.Status;
        row.Severity = replacement.Severity;
        row.CreatedAtUtcTicks = replacement.CreatedAtUtcTicks;
        row.ResolvedAtUtcTicks = replacement.ResolvedAtUtcTicks;
        row.ContentJson = replacement.ContentJson;
        row.SchemaVersion = replacement.SchemaVersion;
        row.Revision = revision;
    }

    internal static void Validate(IncidentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        EfRuntimeOperationalStoreSupport.ValidateIdentity(state.WorkflowExecutionId, nameof(state.WorkflowExecutionId));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(state.IncidentId, nameof(state.IncidentId));
        if (!Enum.IsDefined(state.Status) || !Enum.IsDefined(state.Severity))
            throw new InvalidDataException("The incident state contains an undefined enum value.");
        if (state.ActivityExecutionId is not null)
            EfRuntimeOperationalStoreSupport.ValidateIdentity(state.ActivityExecutionId, nameof(state.ActivityExecutionId));
        if (state.ExecutableNodeId is not null)
            EfRuntimeOperationalStoreSupport.ValidateIdentity(state.ExecutableNodeId, nameof(state.ExecutableNodeId));
    }

    private static void ValidateIdentity(string workflowExecutionId, string incidentId)
    {
        EfRuntimeOperationalStoreSupport.ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(incidentId, nameof(incidentId));
    }

}
