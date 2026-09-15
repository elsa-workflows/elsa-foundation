using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Internal caller-owned transaction staging for the R19 workflow run-health projection.</summary>
/// <remarks>
/// The dashboard reader is a separate R25 concern. This seam only folds the workflow and incident checkpoint
/// changes into the EF projection; it never saves, creates or commits a transaction, and it never clears siblings
/// from the caller's change tracker.
/// </remarks>
internal static class EfRuntimeCheckpointRunHealthParticipantStaging
{
    public static async ValueTask StageWorkflowRunHealthAsync(
        BookmarkStateDbContext context,
        string workflowExecutionId,
        RuntimeStateChange<WorkflowExecutionState>? workflowChange,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> incidentChanges,
        string scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(incidentChanges);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (workflowExecutionId.Length > RuntimeOperationalStateEfModule.IdentityMaximumLength)
            throw new ArgumentException($"Runtime identity cannot exceed {RuntimeOperationalStateEfModule.IdentityMaximumLength} UTF-16 code units.", nameof(workflowExecutionId));
        if (scope.Length > 256)
            throw new ArgumentException("Runtime persistence scope cannot exceed 256 UTF-16 code units.", nameof(scope));
        if (workflowChange is null && incidentChanges.Count == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Workflow run-health state must be staged inside a caller-owned EF transaction.");

        ValidateWorkflowChange(workflowChange, workflowExecutionId, scope);
        var newIncidentCount = await CountNewIncidentsAsync(
            context,
            workflowExecutionId,
            incidentChanges,
            scope,
            cancellationToken);

        var workflowId = EfWorkflowExecutionStateStore.CreateId(scope, workflowExecutionId);
        var workflowRow = context.WorkflowExecutionStates.Local.SingleOrDefault(row => row.Id == workflowId);
        var workflowExists = workflowRow is not null && context.Entry(workflowRow).State is not (EntityState.Added or EntityState.Deleted);
        if (workflowRow is null)
        {
            workflowRow = await context.WorkflowExecutionStates.SingleOrDefaultAsync(row => row.Id == workflowId, cancellationToken);
            workflowExists = workflowRow is not null;
        }

        if (workflowChange is null)
        {
            if (!workflowExists)
                throw new InvalidOperationException(
                    $"An incident-only checkpoint for workflow execution '{workflowExecutionId}' requires the workflow and its run-health projection.");
            _ = EfWorkflowExecutionStateStore.ReadChecked(workflowRow!, scope, workflowExecutionId);
        }

        var healthId = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId);
        var healthRow = context.WorkflowRunHealthStates.Local.SingleOrDefault(row => row.Id == healthId);
        if (healthRow is null)
            healthRow = await context.WorkflowRunHealthStates.SingleOrDefaultAsync(row => row.Id == healthId, cancellationToken);

        WorkflowRunHealthProjection? existingHealth = null;
        if (healthRow is not null && context.Entry(healthRow).State != EntityState.Deleted)
            existingHealth = ReadChecked(healthRow, scope, workflowExecutionId);

        var newWorkflow = workflowChange is not null && !workflowExists;
        if (workflowChange is null && existingHealth is null || workflowChange is not null && newWorkflow && existingHealth is not null || workflowChange is not null && !newWorkflow && existingHealth is null)
        {
            if (workflowChange is null)
                throw new InvalidOperationException(
                    $"An incident-only checkpoint for workflow execution '{workflowExecutionId}' requires the workflow and its run-health projection.");
            if (newWorkflow)
                throw new InvalidOperationException(
                    $"Workflow execution '{workflowExecutionId}' is new but already has a run-health projection.");
            throw new InvalidOperationException(
                $"Workflow execution '{workflowExecutionId}' already exists but has no run-health projection.");
        }

        var next = workflowChange is { } workflow
            ? CreateWorkflowProjection(workflow.State, existingHealth?.StartedAt ?? workflow.State.StartedAt, existingHealth, newIncidentCount)
            : existingHealth! with
            {
                IncidentCount = checked(existingHealth.IncidentCount + newIncidentCount),
                IncidentBearingCount = checked(existingHealth.IncidentBearingCount +
                    (newIncidentCount > 0 && existingHealth.IncidentCount == 0 ? 1 : 0))
            };

        if (healthRow is null)
        {
            context.WorkflowRunHealthStates.Add(ToEntity(next, scope, healthId, 1));
            return;
        }

        CopyToEntity(healthRow, next, scope, healthId, checked(healthRow.Revision + 1));
    }

    internal static WorkflowRunHealthProjection ReadChecked(
        WorkflowRunHealthStateEntity row,
        string scope,
        string expectedWorkflowExecutionId)
    {
        try
        {
            if (row.Revision <= 0 ||
                row.SchemaVersion != RuntimeOperationalStateEfModule.SchemaVersion ||
                row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, expectedWorkflowExecutionId) ||
                row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
                row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
            {
                throw new InvalidDataException("The workflow run-health row envelope is corrupt.");
            }

            var projection = RuntimeArtifactJson.Deserialize<WorkflowRunHealthProjection>(row.ContentJson);
            ValidateProjection(projection);
            var startedAt = ReadStartedAt(row);
            if (projection.WorkflowExecutionId != expectedWorkflowExecutionId ||
                row.WorkflowExecutionId != EfRuntimeOperationalStoreSupport.Encode(projection.WorkflowExecutionId) ||
                row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(projection.WorkflowExecutionId) ||
                row.WorkflowExecutionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(projection.WorkflowExecutionId) ||
                row.DefinitionId != EfRuntimeOperationalStoreSupport.Encode(projection.DefinitionId) ||
                row.DefinitionIdHash != EfRuntimeOperationalStoreSupport.Hash(projection.DefinitionId) ||
                row.DefinitionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(projection.DefinitionId) ||
                row.RunKind != (int)projection.RunKind ||
                startedAt != projection.StartedAt ||
                row.Status != (int)projection.Status ||
                row.IncidentCount != projection.IncidentCount ||
                row.IncidentBearingCount != projection.IncidentBearingCount)
            {
                throw new InvalidDataException("The workflow run-health row identity or projection does not match its current content.");
            }

            return projection;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        {
            throw new InvalidDataException("The persisted workflow run-health state is not valid current data.", exception);
        }
    }

    private static WorkflowRunHealthProjection CreateWorkflowProjection(
        WorkflowExecutionState state,
        DateTimeOffset? startedAt,
        WorkflowRunHealthProjection? existing,
        long newIncidentCount)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.PinnedExecutable);
        EfRuntimeOperationalStoreSupport.ValidateIdentity(state.WorkflowExecutionId, nameof(state.WorkflowExecutionId));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(state.PinnedExecutable.DefinitionId, nameof(state.PinnedExecutable.DefinitionId));
        if (!Enum.IsDefined(state.RunKind) || !Enum.IsDefined(state.Status))
            throw new InvalidDataException("The workflow execution contains an undefined run-health enum value.");
        var projection = new WorkflowRunHealthProjection(
            state.WorkflowExecutionId,
            state.PinnedExecutable.DefinitionId,
            state.RunKind,
            startedAt,
            state.Status,
            checked((existing?.IncidentCount ?? 0) + newIncidentCount),
            checked((existing?.IncidentBearingCount ?? 0) +
                (newIncidentCount > 0 && (existing?.IncidentCount ?? 0) == 0 ? 1 : 0)));
        ValidateProjection(projection);
        return projection;
    }

    private static async ValueTask<long> CountNewIncidentsAsync(
        BookmarkStateDbContext context,
        string workflowExecutionId,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> changes,
        string scope,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long count = 0;
        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change);
            ArgumentNullException.ThrowIfNull(change.State);
            if (change.Operation is not (RuntimeStateChangeOperation.Append or RuntimeStateChangeOperation.Upsert))
                throw new InvalidOperationException("The workflow run-health projection only accepts incident append or upsert changes.");
            EfRuntimeOperationalStoreSupport.ValidateIdentity(change.StateId, nameof(change.StateId));
            EfRuntimeOperationalStoreSupport.ValidateIdentity(change.State.IncidentId, nameof(change.State.IncidentId));
            if (!StringComparer.Ordinal.Equals(change.StateId, change.State.IncidentId) ||
                !StringComparer.Ordinal.Equals(change.State.WorkflowExecutionId, workflowExecutionId))
            {
                throw new InvalidOperationException("Incident state change identity does not belong to the checkpoint workflow.");
            }
            if (!seen.Add(change.StateId))
                throw new InvalidOperationException($"Incident '{change.StateId}' occurs more than once in one checkpoint commit.");

            var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, change.StateId);
            var row = context.IncidentStates.Local.SingleOrDefault(candidate => candidate.Id == id);
            var exists = row is not null && context.Entry(row).State is not (EntityState.Added or EntityState.Deleted);
            if (row is null)
            {
                row = await context.IncidentStates.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
                exists = row is not null;
            }

            if (row is not null && context.Entry(row).State != EntityState.Deleted)
                _ = EfIncidentStateStore.ReadChecked(row, scope, workflowExecutionId, change.StateId);
            if (!exists)
                count = checked(count + 1);
        }

        return count;
    }

    private static void ValidateWorkflowChange(
        RuntimeStateChange<WorkflowExecutionState>? change,
        string workflowExecutionId,
        string scope)
    {
        if (change is null)
            return;
        ArgumentNullException.ThrowIfNull(change.State);
        if (change.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException("The workflow run-health projection only accepts workflow execution upserts.");
        if (!StringComparer.Ordinal.Equals(change.StateId, change.State.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(change.StateId, workflowExecutionId))
            throw new InvalidOperationException("Workflow execution state change identity does not belong to the checkpoint workflow.");
        if (change.State.TenantId is not null && !StringComparer.Ordinal.Equals(change.State.TenantId, scope))
            throw new InvalidOperationException("The workflow execution tenant does not belong to the persistence scope.");
    }

    private static WorkflowRunHealthStateEntity ToEntity(
        WorkflowRunHealthProjection projection,
        string scope,
        string id,
        long revision)
    {
        var row = new WorkflowRunHealthStateEntity { Id = id };
        CopyToEntity(row, projection, scope, id, revision);
        return row;
    }

    private static void CopyToEntity(
        WorkflowRunHealthStateEntity row,
        WorkflowRunHealthProjection projection,
        string scope,
        string id,
        long revision)
    {
        row.Id = id;
        row.ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        row.ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        row.WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(projection.WorkflowExecutionId);
        row.WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(projection.WorkflowExecutionId);
        row.WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(projection.WorkflowExecutionId);
        row.DefinitionId = EfRuntimeOperationalStoreSupport.Encode(projection.DefinitionId);
        row.DefinitionIdHash = EfRuntimeOperationalStoreSupport.Hash(projection.DefinitionId);
        row.DefinitionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(projection.DefinitionId);
        row.RunKind = (int)projection.RunKind;
        row.StartedAtUtcTicks = projection.StartedAt?.UtcTicks;
        row.StartedAtOffsetMinutes = projection.StartedAt is { } startedAt ? (int)startedAt.Offset.TotalMinutes : null;
        row.Status = (int)projection.Status;
        row.IncidentCount = projection.IncidentCount;
        row.IncidentBearingCount = projection.IncidentBearingCount;
        row.ContentJson = RuntimeArtifactJson.Serialize(projection);
        row.SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion;
        row.Revision = revision;
    }

    private static DateTimeOffset? ReadStartedAt(WorkflowRunHealthStateEntity row)
    {
        if (row.StartedAtUtcTicks is null || row.StartedAtOffsetMinutes is null)
        {
            if (row.StartedAtUtcTicks is not null || row.StartedAtOffsetMinutes is not null)
                throw new InvalidDataException("The workflow run-health started-at projection is incomplete.");
            return null;
        }

        try
        {
            return new DateTimeOffset(row.StartedAtUtcTicks.Value, TimeSpan.Zero)
                .ToOffset(TimeSpan.FromMinutes(row.StartedAtOffsetMinutes.Value));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("The workflow run-health started-at projection is invalid.", exception);
        }
    }

    private static void ValidateProjection(WorkflowRunHealthProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        EfRuntimeOperationalStoreSupport.ValidateIdentity(projection.WorkflowExecutionId, nameof(projection.WorkflowExecutionId));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(projection.DefinitionId, nameof(projection.DefinitionId));
        if (!Enum.IsDefined(projection.RunKind) || !Enum.IsDefined(projection.Status))
            throw new InvalidDataException("The workflow run-health state contains an undefined enum value.");
        if (projection.IncidentCount < 0 || projection.IncidentBearingCount is < 0 or > 1)
            throw new InvalidDataException("Workflow run-health incident counters cannot be negative or exceed one bearing run.");
        if (projection.IncidentBearingCount > projection.IncidentCount)
            throw new InvalidDataException("Workflow run-health incident-bearing count cannot exceed incident count.");
    }
}

/// <summary>Authoritative JSON shape for one EF workflow run-health row.</summary>
internal sealed record WorkflowRunHealthProjection(
    string WorkflowExecutionId,
    string DefinitionId,
    WorkflowRunKind RunKind,
    DateTimeOffset? StartedAt,
    WorkflowExecutionStatus Status,
    long IncidentCount,
    long IncidentBearingCount);
