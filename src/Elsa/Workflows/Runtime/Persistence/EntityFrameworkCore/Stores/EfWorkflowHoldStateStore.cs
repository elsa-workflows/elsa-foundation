using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core administrative workflow-hold store (R17).</summary>
/// <remarks>
/// A control-plane state is keyed by its control-plane state ID. Workflow visibility is a separate projection and
/// the workflow query also inspects global rows for embedded workflow-scoped holds, matching the in-memory and
/// the provider-neutral semantics without moving administrative state into workflow continuation documents.
/// </remarks>
public sealed class EfWorkflowHoldStateStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowHoldStateStore
{
    private const int ProviderPageSize = RuntimeStorePageRequest.MaximumLimit;

    public async ValueTask<WorkflowHoldState> SaveAsync(WorkflowHoldState state, CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        context.ChangeTracker.Clear();
        var row = await LoadAsync(scope, state.ControlPlaneStateId, tracking: true, cancellationToken);
        if (row is null)
            context.WorkflowHoldStates.Add(ToEntity(state, scope, 1));
        else
        {
            _ = Read(row, scope, state.ControlPlaneStateId);
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
            throw new InvalidOperationException("The workflow-hold state changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The workflow-hold state changed concurrently; retry the operation.", exception);
        }
    }

    public async ValueTask<WorkflowHoldState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default)
    {
        ValidateControlPlaneStateId(controlPlaneStateId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadAsync(scope, controlPlaneStateId, tracking: false, cancellationToken);
        return row is null ? null : Read(row, scope, controlPlaneStateId);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListForWorkflowExecutionAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        return await ListVisibleAsync(scope, workflowExecutionId, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        return await ListVisibleAsync(scope, workflowExecutionId: null, cancellationToken);
    }

    private async Task<IReadOnlyCollection<WorkflowHoldState>> ListVisibleAsync(
        string scope,
        string? workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var query = context.WorkflowHoldStates.AsNoTracking().Where(row => row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey);
        if (workflowExecutionId is not null)
        {
            var workflow = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
            var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
            // Direct workflow rows are indexed. Global rows must still be read because a hold embedded in their
            // payload can target this workflow; the bounded keyset walk keeps each provider round-trip finite.
            query = query.Where(row => row.WorkflowExecutionId == null || row.WorkflowExecutionIdHash == workflowHash && row.WorkflowExecutionId == workflow);
        }

        var rows = new List<WorkflowHoldState>();
        string? lastOrderKey = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = query;
            if (lastOrderKey is not null)
                page = page.Where(row => string.Compare(row.ControlPlaneStateIdOrderKey, lastOrderKey) > 0);
            var pageRows = await page.OrderBy(row => row.ControlPlaneStateIdOrderKey).Take(ProviderPageSize + 1).ToArrayAsync(cancellationToken);
            var hasMore = pageRows.Length > ProviderPageSize;
            if (hasMore)
                pageRows = pageRows[..ProviderPageSize];
            foreach (var row in pageRows)
            {
                var state = Read(row, scope);
                if (workflowExecutionId is null || IsVisibleToWorkflow(state, workflowExecutionId))
                    rows.Add(state);
            }
            if (!hasMore)
                return rows;
            lastOrderKey = pageRows[^1].ControlPlaneStateIdOrderKey;
        }
    }

    private static bool IsVisibleToWorkflow(WorkflowHoldState state, string workflowExecutionId) =>
        StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
        state.ActiveHolds.Concat(state.ReleasedHolds).Any(hold => StringComparer.Ordinal.Equals(hold.WorkflowExecutionId, workflowExecutionId));

    private async Task<WorkflowHoldStateEntity?> LoadAsync(string scope, string controlPlaneStateId, bool tracking, CancellationToken cancellationToken)
    {
        var encodedScope = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var encodedId = EfRuntimeOperationalStoreSupport.Encode(controlPlaneStateId);
        var idHash = EfRuntimeOperationalStoreSupport.Hash(controlPlaneStateId);
        var id = EfRuntimeOperationalStoreSupport.Hash(EfRuntimeOperationalStoreSupport.Encode(scope) + "\u001f" + controlPlaneStateId);
        var query = context.WorkflowHoldStates.Where(row => row.Id == id && row.ScopeKey == encodedScope && row.ScopeKeyHash == scopeHash && row.ControlPlaneStateId == encodedId && row.ControlPlaneStateIdHash == idHash);
        return tracking ? await query.SingleOrDefaultAsync(cancellationToken) : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    private static WorkflowHoldStateEntity ToEntity(WorkflowHoldState state, string scope, long revision)
    {
        var workflow = state.WorkflowExecutionId;
        return new WorkflowHoldStateEntity
        {
            Id = EfRuntimeOperationalStoreSupport.Hash(EfRuntimeOperationalStoreSupport.Encode(scope) + "\u001f" + state.ControlPlaneStateId),
            ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope), ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
            ControlPlaneStateId = EfRuntimeOperationalStoreSupport.Encode(state.ControlPlaneStateId), ControlPlaneStateIdHash = EfRuntimeOperationalStoreSupport.Hash(state.ControlPlaneStateId), ControlPlaneStateIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.ControlPlaneStateId),
            WorkflowExecutionId = workflow is null ? null : EfRuntimeOperationalStoreSupport.Encode(workflow), WorkflowExecutionIdHash = workflow is null ? null : EfRuntimeOperationalStoreSupport.Hash(workflow), WorkflowExecutionIdOrderKey = workflow is null ? null : EfRuntimeOperationalStoreSupport.Order(workflow),
            ContentJson = RuntimeArtifactJson.Serialize(state), SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = revision
        };
    }

    private static void Copy(WorkflowHoldStateEntity row, WorkflowHoldState state, string scope, long revision)
    {
        var replacement = ToEntity(state, scope, revision);
        row.Id = replacement.Id; row.ScopeKey = replacement.ScopeKey; row.ScopeKeyHash = replacement.ScopeKeyHash;
        row.ControlPlaneStateId = replacement.ControlPlaneStateId; row.ControlPlaneStateIdHash = replacement.ControlPlaneStateIdHash; row.ControlPlaneStateIdOrderKey = replacement.ControlPlaneStateIdOrderKey;
        row.WorkflowExecutionId = replacement.WorkflowExecutionId; row.WorkflowExecutionIdHash = replacement.WorkflowExecutionIdHash; row.WorkflowExecutionIdOrderKey = replacement.WorkflowExecutionIdOrderKey;
        row.ContentJson = replacement.ContentJson; row.SchemaVersion = replacement.SchemaVersion; row.Revision = revision;
    }

    private static WorkflowHoldState Read(WorkflowHoldStateEntity row, string scope, string? expectedControlPlaneStateId = null)
    {
        if (row.Revision <= 0 || row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) || row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
            throw new InvalidDataException("The workflow-hold row scope or revision projection is corrupt.");
        WorkflowHoldState state;
        try { state = RuntimeArtifactJson.Deserialize<WorkflowHoldState>(row.ContentJson); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { throw new InvalidDataException("The persisted workflow-hold state is not valid current data.", exception); }
        var workflow = state.WorkflowExecutionId;
        var valid = (expectedControlPlaneStateId is null || state.ControlPlaneStateId == expectedControlPlaneStateId) &&
                    row.Id == EfRuntimeOperationalStoreSupport.Hash(EfRuntimeOperationalStoreSupport.Encode(scope) + "\u001f" + state.ControlPlaneStateId) &&
                    row.SchemaVersion == RuntimeOperationalStateEfModule.SchemaVersion &&
                    row.ControlPlaneStateId == EfRuntimeOperationalStoreSupport.Encode(state.ControlPlaneStateId) && row.ControlPlaneStateIdHash == EfRuntimeOperationalStoreSupport.Hash(state.ControlPlaneStateId) && row.ControlPlaneStateIdOrderKey == EfRuntimeOperationalStoreSupport.Order(state.ControlPlaneStateId) &&
                    row.WorkflowExecutionId == (workflow is null ? null : EfRuntimeOperationalStoreSupport.Encode(workflow)) && row.WorkflowExecutionIdHash == (workflow is null ? null : EfRuntimeOperationalStoreSupport.Hash(workflow)) && row.WorkflowExecutionIdOrderKey == (workflow is null ? null : EfRuntimeOperationalStoreSupport.Order(workflow));
        if (!valid)
            throw new InvalidDataException("The workflow-hold row identity or visible-scope projection is corrupt.");
        return state;
    }

    private static void ValidateState(WorkflowHoldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateControlPlaneStateId(state.ControlPlaneStateId);
        if (state.WorkflowExecutionId is not null)
            ValidateWorkflowExecutionId(state.WorkflowExecutionId);
        foreach (var hold in state.ActiveHolds.Concat(state.ReleasedHolds))
        {
            ValidateControlPlaneStateId(hold.HoldId);
            if (hold.WorkflowExecutionId is not null) ValidateWorkflowExecutionId(hold.WorkflowExecutionId);
            if (hold.ActivityExecutionId is not null) ValidateWorkflowExecutionId(hold.ActivityExecutionId);
            if (hold.GeneratorId is not null) ValidateWorkflowExecutionId(hold.GeneratorId);
            if (hold.IngressSourceId is not null) ValidateWorkflowExecutionId(hold.IngressSourceId);
            if (hold.WorkerId is not null) ValidateWorkflowExecutionId(hold.WorkerId);
            if (hold.HostId is not null) ValidateWorkflowExecutionId(hold.HostId);
        }
    }

    private static void ValidateControlPlaneStateId(string value) => EfRuntimeOperationalStoreSupport.ValidateIdentity(value, nameof(value));
    private static void ValidateWorkflowExecutionId(string value) => EfRuntimeOperationalStoreSupport.ValidateIdentity(value, nameof(value));
}
