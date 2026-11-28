using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Checked physical liveness rows shared by deletion and by outbox responsibility.</summary>
internal static class EfRuntimeExecutionLivenessRow
{
    internal static ExecutionLivenessStateEntity ToEntity(RuntimeDbContext context, ExecutionLivenessState state, string scope, long revision)
    {
        var lease = state.ExecutionLease;
        var heartbeat = state.Heartbeat;
        return new ExecutionLivenessStateEntity
        {
            Id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId),
            ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope), ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
            WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId), WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId), WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId),
            OperationalStateId = EfRuntimeOperationalStoreSupport.Encode(state.ExecutionLivenessStateId), OperationalStateIdHash = EfRuntimeOperationalStoreSupport.Hash(state.ExecutionLivenessStateId), OperationalStateIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.ExecutionLivenessStateId),
            InterruptedStatus = state.InterruptedExecution is { } interrupted ? (int)interrupted.Status : null,
            InterruptedAtUtcTicks = state.InterruptedExecution?.InterruptedAt.UtcTicks,
            LeaseOwnerId = lease is null ? null : EfRuntimeOperationalStoreSupport.Encode(lease.OwnerId),
            LeaseAcquiredAtUtcTicks = lease?.AcquiredAt.UtcTicks,
            LeaseExpiresAtUtcTicks = lease?.ExpiresAt.UtcTicks,
            HeartbeatOwnerId = heartbeat is null ? null : EfRuntimeOperationalStoreSupport.Encode(heartbeat.OwnerId),
            HeartbeatRecordedAtUtcTicks = heartbeat?.RecordedAt.UtcTicks,
            HasOperationalOwner = lease is not null || heartbeat is not null,
            ContentJson = RuntimeArtifactJson.Serialize(state), SchemaVersion = RuntimeSchemaWriteVersion.Operational(context), Revision = revision
        };
    }

    // Internal checkpoint staging uses the same envelope/projection validation before touching the ownership fence.
    internal static ExecutionLivenessState Read(ExecutionLivenessStateEntity row, string scope, string? expectedWorkflow = null, string? expectedOperational = null)
    {
        if (EfSchemaVersion.NotReadable(RuntimeOperationalStateEfModule.Chain, row.SchemaVersion) ||
            row.Revision <= 0 || row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) || row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
            throw new InvalidDataException("The execution-liveness row scope, schema, or revision projection is corrupt.");
        ExecutionLivenessState state;
        var content = RuntimeOperationalStateEfModule.Chain.Upcast<ExecutionLivenessStateEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson), (nameof(row.TimerHistoryOwnerJson), row.TimerHistoryOwnerJson))[nameof(row.ContentJson)]!;
        try { state = RuntimeArtifactJson.Deserialize<ExecutionLivenessState>(content); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { throw new InvalidDataException("The persisted execution-liveness state is not valid current data.", exception); }
        var lease = state.ExecutionLease;
        var heartbeat = state.Heartbeat;
        var valid = (expectedWorkflow is null || state.WorkflowExecutionId == expectedWorkflow) &&
                    (expectedOperational is null || state.ExecutionLivenessStateId == expectedOperational) &&
                    row.Id == EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId) &&
                    row.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId) && row.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId) && row.WorkflowExecutionIdOrderKey == EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId) &&
                    row.OperationalStateId == EfRuntimeOperationalStoreSupport.Encode(state.ExecutionLivenessStateId) && row.OperationalStateIdHash == EfRuntimeOperationalStoreSupport.Hash(state.ExecutionLivenessStateId) && row.OperationalStateIdOrderKey == EfRuntimeOperationalStoreSupport.Order(state.ExecutionLivenessStateId) &&
                    row.InterruptedStatus == (state.InterruptedExecution is { } interrupted ? (int)interrupted.Status : null) && row.InterruptedAtUtcTicks == state.InterruptedExecution?.InterruptedAt.UtcTicks &&
                    row.LeaseOwnerId == (lease is null ? null : EfRuntimeOperationalStoreSupport.Encode(lease.OwnerId)) && row.LeaseAcquiredAtUtcTicks == lease?.AcquiredAt.UtcTicks && row.LeaseExpiresAtUtcTicks == lease?.ExpiresAt.UtcTicks &&
                    row.HeartbeatOwnerId == (heartbeat is null ? null : EfRuntimeOperationalStoreSupport.Encode(heartbeat.OwnerId)) && row.HeartbeatRecordedAtUtcTicks == heartbeat?.RecordedAt.UtcTicks && row.HasOperationalOwner == (lease is not null || heartbeat is not null);
        if (!valid)
            throw new InvalidDataException("The execution-liveness row identity or recovery projection is corrupt.");
        return state;
    }

    internal static async ValueTask<bool> IsWorkflowExecutionDeletionTombstonedAsync(
        RuntimeDbContext context,
        string scope,
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var row = await WorkflowExecutionDeletionOwnershipRows(context, scope, workflowExecutionId)
            .SingleOrDefaultAsync(cancellationToken);
        return IsWorkflowExecutionDeletionTombstone(row, scope, workflowExecutionId);
    }

    internal static IQueryable<ExecutionLivenessStateEntity> WorkflowExecutionDeletionOwnershipRows(
        RuntimeDbContext context,
        string scope,
        string workflowExecutionId)
    {
        var stateId = RuntimeExecutionOwnershipStateId.For(workflowExecutionId);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
        var stateIdHash = EfRuntimeOperationalStoreSupport.Hash(stateId);
        var stateIdKey = EfRuntimeOperationalStoreSupport.Encode(stateId);
        return context.ExecutionLivenessStates.AsNoTracking().Where(candidate =>
            candidate.Id == EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, stateId) &&
            candidate.ScopeKeyHash == scopeHash && candidate.ScopeKey == scopeKey &&
            candidate.WorkflowExecutionIdHash == workflowHash && candidate.WorkflowExecutionId == workflowKey &&
            candidate.OperationalStateIdHash == stateIdHash && candidate.OperationalStateId == stateIdKey);
    }

    internal static bool IsWorkflowExecutionDeletionTombstone(
        ExecutionLivenessStateEntity? row,
        string scope,
        string workflowExecutionId)
    {
        if (row is null)
            return false;

        var stateId = RuntimeExecutionOwnershipStateId.For(workflowExecutionId);
        var state = Read(row, scope, workflowExecutionId, stateId);
        if (!state.Metadata.TryGetValue(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone, out var value))
            return false;
        if (!StringComparer.Ordinal.Equals(value, "true"))
            throw new InvalidDataException("The persisted workflow-execution deletion tombstone is corrupt.");
        return true;
    }
}
