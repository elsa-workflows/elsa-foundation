using Elsa.Attention.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>
/// Complete, tenant-bound workflow runtime attention query over the current Groundwork v2 runtime
/// projections. Active incidents and faulted executions are traversed as bounded provider queries;
/// the final urgency selection is the shared public attention contract.
/// </summary>
/// <remarks>
/// This adapter deliberately uses the public Groundwork query AST and the current v2
/// workflow-execution and incident units. It does not inspect provider connections, execute SQL or Mongo
/// expressions, materialize an unbounded table, or consult the v1 stores.
/// </remarks>
public sealed class GroundworkV2WorkflowRuntimeAttentionQuery(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    TimeProvider? timeProvider = null,
    string? targetName = null) : IWorkflowRuntimeAttentionQuery
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask<WorkflowRuntimeAttentionSnapshot> QueryAsync(
        WorkflowRuntimeAttentionQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum items must be positive.");
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return ValueTask.FromResult(WorkflowRuntimeAttentionSnapshot.Unavailable(
                "RUNTIME_ATTENTION_TENANT_REQUIRED",
                "Workflow runtime attention requires an authenticated tenant scope."));
        }

        var access = RequireScopedAccess(request.TenantId);
        cancellationToken.ThrowIfCancellationRequested();
        var observedAt = timeProvider.GetUtcNow();
        var top = new List<WorkflowRuntimeAttentionRecord>(request.MaximumItems);
        var activeExecutionIds = new HashSet<string>(StringComparer.Ordinal);
        var executions = Open(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, access);
        var incidents = Open(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind, access);
        var executionCache = new Dictionary<string, WorkflowExecutionState?>(StringComparer.Ordinal);
        var totalCount = 0;

        foreach (var status in new[] { IncidentStatus.Open, IncidentStatus.Blocking })
        {
            foreach (var incident in QueryIncidents(incidents, status, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!executionCache.TryGetValue(incident.WorkflowExecutionId, out var execution))
                {
                    execution = ReadExecution(executions, incident.WorkflowExecutionId);
                    executionCache.Add(incident.WorkflowExecutionId, execution);
                }

                if (execution is null || !StringComparer.Ordinal.Equals(execution.TenantId, request.TenantId))
                    continue;

                activeExecutionIds.Add(incident.WorkflowExecutionId);
                totalCount = checked(totalCount + 1);
                Consider(
                    top,
                    WorkflowRuntimeAttentionRecords.MapIncident(incident, execution, observedAt),
                    request.MaximumItems);
            }
        }

        foreach (var execution in QueryFaultedExecutions(executions, request.TenantId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (activeExecutionIds.Contains(execution.WorkflowExecutionId))
                continue;

            totalCount = checked(totalCount + 1);
            Consider(top, WorkflowRuntimeAttentionRecords.MapFault(execution, observedAt), request.MaximumItems);
        }

        return ValueTask.FromResult<WorkflowRuntimeAttentionSnapshot>(new(totalCount, top));
    }

    private StorageAccess RequireScopedAccess(string tenantId)
    {
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException(
                          "Groundwork workflow runtime attention persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork workflow runtime attention requires one explicit persistence scope; global and across-scope access are refused.");
        }

        context.EnsureTenantScope(tenantId);
        return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
    }

    private IStorageSession Open(string unitId, StorageAccess access) => sessions.Open(unitId, access, targetName);

    private IEnumerable<IncidentState> QueryIncidents(
        IStorageSession session,
        IncidentStatus status,
        CancellationToken cancellationToken)
    {
        var unit = sessions.Unit(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind, targetName);
        var table = new TableId(unit.Name);
        var statusColumn = Column(unit, table, ElsaRuntimeV2StorageManifest.StatusField);
        var createdAt = Column(unit, table, ElsaRuntimeV2StorageManifest.CreatedAtField);
        var workflowExecutionId = Column(unit, table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var incidentId = Column(unit, table, ElsaRuntimeV2StorageManifest.IncidentIdField);
        var predicate = new Predicate.Equal(statusColumn, QueryConstant.Of(statusColumn, status.ToString()));
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                table,
                predicate,
                [
                    new OrderTerm(createdAt, OrderDirection.Ascending, NullOrder.Last),
                    new OrderTerm(workflowExecutionId, OrderDirection.Ascending, NullOrder.Last),
                    new OrderTerm(incidentId, OrderDirection.Ascending, NullOrder.Last)
                ],
                Projection.All,
                PagingFor(RuntimeStorePageRequest.MaximumLimit, continuation)));
            foreach (var row in result.Rows)
            {
                var incident = GroundworkV2IncidentStateStorageConventions.Deserialize(row);
                if (incident.Status != status)
                    throw new InvalidDataException(
                        "Groundwork runtime attention returned an incident whose status projection is not the requested active status.");
                yield return incident;
            }

            if (result.NextContinuationToken is { } next && !seenContinuations.Add(next))
            {
                throw new InvalidDataException(
                    "Groundwork runtime attention incident continuation repeated or cycled.");
            }

            continuation = result.NextContinuationToken;
        } while (continuation is not null);
    }

    private IEnumerable<WorkflowExecutionState> QueryFaultedExecutions(
        IStorageSession session,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, targetName);
        var table = new TableId(unit.Name);
        var status = Column(unit, table, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryStatusField);
        var tenant = Column(unit, table, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryTenantIdField);
        var sortTicks = Column(unit, table, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistorySortTicksField);
        var executionId = Column(unit, table, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField);
        var predicate = new Predicate.And([
            new Predicate.Equal(
                status,
                QueryConstant.Of(status, (int)WorkflowExecutionStatus.Faulted)),
            new Predicate.Equal(tenant, QueryConstant.Of(tenant, tenantId))
        ]);
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                table,
                predicate,
                [
                    new OrderTerm(sortTicks, OrderDirection.Descending, NullOrder.Last),
                    new OrderTerm(executionId, OrderDirection.Ascending, NullOrder.Last)
                ],
                Projection.All,
                PagingFor(RuntimeStorePageRequest.MaximumLimit, continuation)));
            foreach (var row in result.Rows)
            {
                var state = GroundworkV2WorkflowExecutionStorageConventions.Deserialize(row);
                if (!StringComparer.Ordinal.Equals(state.TenantId, tenantId))
                    throw new InvalidDataException(
                        "Groundwork runtime attention returned a workflow execution outside its authorized tenant partition.");
                if (state.Status != WorkflowExecutionStatus.Faulted)
                    throw new InvalidDataException(
                        "Groundwork runtime attention returned a workflow execution whose status projection is not faulted.");
                yield return state;
            }

            if (result.NextContinuationToken is { } next && !seenContinuations.Add(next))
            {
                throw new InvalidDataException(
                    "Groundwork runtime attention workflow-execution continuation repeated or cycled.");
            }

            continuation = result.NextContinuationToken;
        } while (continuation is not null);
    }

    private static WorkflowExecutionState? ReadExecution(IStorageSession session, string workflowExecutionId)
    {
        var entry = session.Read(GroundworkRuntimeRowStore.Key(workflowExecutionId));
        if (entry is null)
            return null;

        var state = GroundworkV2WorkflowExecutionStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId))
            throw new InvalidDataException(
                "Groundwork runtime attention execution content does not match its requested identity.");
        return state;
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

    private static ColumnRef Column(StorageUnit unit, TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork runtime attention unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork runtime attention query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);
}
