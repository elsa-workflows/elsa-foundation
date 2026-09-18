using Elsa.Attention.Core;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Complete EF-owned runtime attention query over R10 workflow-execution and R18 incident rows.
/// </summary>
/// <remarks>
/// The adapter reads both projections from the same EF context. It never delegates to another store or
/// another runtime store, uses bounded keyset pages, and keeps only the requested urgency frontier in memory.
/// </remarks>
public sealed class EfWorkflowRuntimeAttentionQuery(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    TimeProvider? timeProvider = null) : IWorkflowRuntimeAttentionQuery
{
    private const int ProviderPageSize = RuntimeStorePageRequest.MaximumLimit;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<WorkflowRuntimeAttentionSnapshot> QueryAsync(
        WorkflowRuntimeAttentionQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum items must be positive.");
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return WorkflowRuntimeAttentionSnapshot.Unavailable(
                "RUNTIME_ATTENTION_TENANT_REQUIRED",
                "Workflow runtime attention requires an authenticated tenant scope.");
        }

        // Resolve and validate authorization before constructing or executing any provider query.
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        accessContextAccessor.Current.EnsureTenantScope(request.TenantId);
        cancellationToken.ThrowIfCancellationRequested();

        // A projected tenant filter alone can hide an authorized execution whose tenant column drifted away
        // from its authoritative JSON. Validate every row in the authorized scope before computing an exact total.
        await ValidateScopeExecutionProjectionsAsync(scope, cancellationToken);

        var observedAt = _timeProvider.GetUtcNow();
        var top = new List<WorkflowRuntimeAttentionRecord>(request.MaximumItems);
        var incidentQuery = IncidentQuery(scope, request.TenantId);
        var projectedActiveCount = await incidentQuery
            .Where(row => row.Status == (int)IncidentStatus.Open || row.Status == (int)IncidentStatus.Blocking)
            .CountAsync(cancellationToken);

        var observedActiveCount = await ReadIncidentPagesAsync(
            incidentQuery,
            scope,
            request.TenantId,
            observedAt,
            request.MaximumItems,
            top,
            cancellationToken);
        if (observedActiveCount != projectedActiveCount)
        {
            throw new InvalidDataException(
                "The persisted incident active-status projection does not match its current content.");
        }

        var faultedQuery = FaultedExecutionQuery(scope, request.TenantId);
        var projectedFaultedCount = await faultedQuery
            .Where(row => row.Status == (int)WorkflowExecutionStatus.Faulted)
            .LongCountAsync(cancellationToken);
        var observedFaultedCount = await ReadFaultedExecutionPagesAsync(
            faultedQuery,
            scope,
            observedAt,
            request.MaximumItems,
            top,
            cancellationToken);
        if (observedFaultedCount != projectedFaultedCount)
        {
            throw new InvalidDataException(
                "The persisted workflow-execution fault projection does not match its current content.");
        }

        var total = checked((long)observedActiveCount + observedFaultedCount);
        return new(checked((int)total), top.ToArray());
    }

    private async Task ValidateScopeExecutionProjectionsAsync(string scope, CancellationToken cancellationToken)
    {
        var source = context.WorkflowExecutionStates.AsNoTracking().Where(row =>
            row.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            row.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope));
        string? lastOrderKey = null;
        string? lastId = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = source;
            if (lastOrderKey is not null && lastId is not null)
            {
                page = page.Where(row =>
                    string.Compare(row.WorkflowExecutionIdOrderKey, lastOrderKey) > 0 ||
                    row.WorkflowExecutionIdOrderKey == lastOrderKey && string.Compare(row.Id, lastId) > 0);
            }

            var rows = await page.OrderBy(row => row.WorkflowExecutionIdOrderKey).ThenBy(row => row.Id)
                .Take(ProviderPageSize + 1).ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > ProviderPageSize;
            if (hasNext)
                rows = rows[..ProviderPageSize];
            foreach (var row in rows)
            {
                var workflowExecutionId = DecodeProjection(row.WorkflowExecutionId);
                _ = EfWorkflowExecutionStateStore.ReadChecked(row, scope, workflowExecutionId);
            }

            if (!hasNext)
                return;
            lastOrderKey = rows[^1].WorkflowExecutionIdOrderKey;
            lastId = rows[^1].Id;
        }
    }

    private async Task<int> ReadIncidentPagesAsync(
        IQueryable<IncidentStateEntity> source,
        string scope,
        string tenantId,
        DateTimeOffset observedAt,
        int maximumItems,
        List<WorkflowRuntimeAttentionRecord> top,
        CancellationToken cancellationToken)
    {
        var count = 0;
        long? lastCreatedAt = null;
        string? lastWorkflowOrderKey = null;
        string? lastIncidentOrderKey = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = source;
            if (lastCreatedAt is { } createdAt && lastWorkflowOrderKey is not null && lastIncidentOrderKey is not null)
            {
                page = page.Where(row =>
                    row.CreatedAtUtcTicks < createdAt ||
                    row.CreatedAtUtcTicks == createdAt &&
                    (string.Compare(row.WorkflowExecutionIdOrderKey, lastWorkflowOrderKey) > 0 ||
                     string.Equals(row.WorkflowExecutionIdOrderKey, lastWorkflowOrderKey) &&
                     string.Compare(row.IncidentIdOrderKey, lastIncidentOrderKey) > 0));
            }

            var rows = await page
                .OrderByDescending(row => row.CreatedAtUtcTicks)
                .ThenBy(row => row.WorkflowExecutionIdOrderKey)
                .ThenBy(row => row.IncidentIdOrderKey)
                .Take(ProviderPageSize + 1)
                .ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > ProviderPageSize;
            if (hasNext)
                rows = rows[..ProviderPageSize];

            var workflowIds = rows
                .Select(row => row.WorkflowExecutionId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var executions = await context.WorkflowExecutionStates.AsNoTracking()
                .Where(row => row.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
                             row.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                             row.TenantIdHash == EfRuntimeOperationalStoreSupport.Hash(tenantId) &&
                             row.TenantId == EfRuntimeOperationalStoreSupport.Encode(tenantId) &&
                             workflowIds.Contains(row.WorkflowExecutionId))
                .ToArrayAsync(cancellationToken);
            var executionsById = executions.ToDictionary(row => row.WorkflowExecutionId, StringComparer.Ordinal);

            foreach (var row in rows)
            {
                var workflowExecutionId = DecodeProjection(row.WorkflowExecutionId);
                if (!executionsById.TryGetValue(row.WorkflowExecutionId, out var executionRow))
                    continue;
                var incident = EfIncidentStateStore.ReadChecked(
                    row,
                    scope,
                    workflowExecutionId,
                    DecodeProjection(row.IncidentId));
                var execution = EfWorkflowExecutionStateStore.ReadChecked(
                    executionRow,
                    scope,
                    workflowExecutionId);
                if (incident.Status is not (IncidentStatus.Open or IncidentStatus.Blocking))
                    continue;

                count = checked(count + 1);
                Consider(top, WorkflowRuntimeAttentionRecords.MapIncident(incident, execution, observedAt), maximumItems);
            }

            if (!hasNext)
                return count;
            lastCreatedAt = rows[^1].CreatedAtUtcTicks;
            lastWorkflowOrderKey = rows[^1].WorkflowExecutionIdOrderKey;
            lastIncidentOrderKey = rows[^1].IncidentIdOrderKey;
        }
    }

    private async Task<long> ReadFaultedExecutionPagesAsync(
        IQueryable<WorkflowExecutionStateEntity> source,
        string scope,
        DateTimeOffset observedAt,
        int maximumItems,
        List<WorkflowRuntimeAttentionRecord> top,
        CancellationToken cancellationToken)
    {
        long count = 0;
        long? lastSortTicks = null;
        string? lastExecutionOrderKey = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = source;
            if (lastSortTicks is { } sortTicks && lastExecutionOrderKey is not null)
            {
                page = page.Where(row =>
                    row.SortTimestampUtcTicks < sortTicks ||
                    row.SortTimestampUtcTicks == sortTicks &&
                    string.Compare(row.WorkflowExecutionIdOrderKey, lastExecutionOrderKey) > 0);
            }

            var rows = await page
                .OrderByDescending(row => row.SortTimestampUtcTicks)
                .ThenBy(row => row.WorkflowExecutionIdOrderKey)
                .Take(ProviderPageSize + 1)
                .ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > ProviderPageSize;
            if (hasNext)
                rows = rows[..ProviderPageSize];

            foreach (var row in rows)
            {
                var workflowExecutionId = DecodeProjection(row.WorkflowExecutionId);
                var execution = EfWorkflowExecutionStateStore.ReadChecked(row, scope, workflowExecutionId);
                if (execution.Status != WorkflowExecutionStatus.Faulted)
                {
                    if (row.Status == (int)WorkflowExecutionStatus.Faulted)
                        throw new InvalidDataException(
                            "The persisted workflow-execution fault projection does not match its current content.");
                    continue;
                }

                count = checked(count + 1);
                Consider(top, WorkflowRuntimeAttentionRecords.MapFault(execution, observedAt), maximumItems);
            }

            if (!hasNext)
                return count;
            lastSortTicks = rows[^1].SortTimestampUtcTicks;
            lastExecutionOrderKey = rows[^1].WorkflowExecutionIdOrderKey;
        }
    }

    private IQueryable<IncidentStateEntity> IncidentQuery(string scope, string tenantId)
    {
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var tenantKey = EfRuntimeOperationalStoreSupport.Encode(tenantId);
        var tenantHash = EfRuntimeOperationalStoreSupport.Hash(tenantId);
        return context.IncidentStates.AsNoTracking()
            .Where(row => row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey)
            .Where(incident => context.WorkflowExecutionStates.AsNoTracking().Any(execution =>
                execution.ScopeKeyHash == scopeHash && execution.ScopeKey == scopeKey &&
                execution.TenantIdHash == tenantHash && execution.TenantId == tenantKey &&
                execution.WorkflowExecutionIdHash == incident.WorkflowExecutionIdHash &&
                execution.WorkflowExecutionId == incident.WorkflowExecutionId));
    }

    private IQueryable<WorkflowExecutionStateEntity> FaultedExecutionQuery(string scope, string tenantId)
    {
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var tenantKey = EfRuntimeOperationalStoreSupport.Encode(tenantId);
        var tenantHash = EfRuntimeOperationalStoreSupport.Hash(tenantId);
        return context.WorkflowExecutionStates.AsNoTracking()
            .Where(row => row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
                         row.TenantIdHash == tenantHash && row.TenantId == tenantKey)
            .Where(row => !context.IncidentStates.Any(incident =>
                incident.ScopeKeyHash == scopeHash && incident.ScopeKey == scopeKey &&
                incident.WorkflowExecutionIdHash == row.WorkflowExecutionIdHash &&
                incident.WorkflowExecutionId == row.WorkflowExecutionId &&
                (incident.Status == (int)IncidentStatus.Open || incident.Status == (int)IncidentStatus.Blocking)));
    }

    private static string DecodeProjection(string encoded)
    {
        try
        {
            return EfRuntimeOperationalStoreSupport.Decode(encoded);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("A persisted runtime identity projection is not valid.", exception);
        }
    }

    private static void Consider(
        List<WorkflowRuntimeAttentionRecord> top,
        WorkflowRuntimeAttentionRecord candidate,
        int maximumItems)
    {
        top.Add(candidate);
        top.Sort(WorkflowRuntimeAttentionRecords.UrgencyComparer);
        if (top.Count > maximumItems)
            top.RemoveAt(top.Count - 1);
    }

}
