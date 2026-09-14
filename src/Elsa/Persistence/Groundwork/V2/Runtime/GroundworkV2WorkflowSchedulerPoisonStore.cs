using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 scheduler-poison store.</summary>
/// <remarks>
/// Poison records are replaceable rows keyed by an injective workflow/work-item identity. Every write is
/// create-only or compare-and-swap; every collection read is a bounded provider keyset page. The adapter
/// requires one explicit persistence scope and validates the complete current envelope on every read.
/// </remarks>
public sealed class GroundworkV2WorkflowSchedulerPoisonStore : GroundworkV2RuntimeStoreBase, IWorkflowSchedulerPoisonStore
{
    private const int MaxRecordAttempts = 16;

    public GroundworkV2WorkflowSchedulerPoisonStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
        : base(sessions, accessContextAccessor, targetName, "scheduler poison", ElsaRuntimeV2StorageManifest.SchedulerPoisonDocumentKind)
    {
    }

    public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(
        RuntimeSchedulerPoisonRecord record,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2WorkflowSchedulerPoisonStorageConventions.Validate(record);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowSchedulerPoisonStorageConventions.PhysicalId(
                record.WorkflowExecutionId,
                record.WorkItemId));
        var values = GroundworkV2WorkflowSchedulerPoisonStorageConventions.Values(record);

        for (var attempt = 0; attempt < MaxRecordAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = session.Read(key);
            WriteOutcome result;
            if (existing is null)
            {
                result = session.Insert(values, WriteOptions.CreateOnly);
            }
            else
            {
                var previous = GroundworkV2WorkflowSchedulerPoisonStorageConventions.Deserialize(existing.Values.Values);
                EnsureIdentity(previous, record.WorkflowExecutionId, record.WorkItemId);
                var revision = existing.Version ?? throw new InvalidDataException(
                    "Groundwork scheduler-poison row did not return an optimistic revision.");
                result = ConditionalUpsert(session, values, revision);
            }

            if (IsSaved(result.Status))
                return ValueTask.FromResult(record);
            if (result.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound)
                continue;

            throw new InvalidOperationException(
                $"Groundwork rejected scheduler poison record '{record.WorkItemId}' with status '{result.Status}'.");
        }

        throw new InvalidOperationException(
            $"Recording scheduler poison record '{record.WorkItemId}' in workflow execution '{record.WorkflowExecutionId}' did not settle after {MaxRecordAttempts} compare-and-swap attempts.");
    }

    public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(
        string workflowExecutionId,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, workItemId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Open().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowSchedulerPoisonStorageConventions.PhysicalId(
                workflowExecutionId,
                workItemId)));
        if (entry is null)
            return ValueTask.FromResult<RuntimeSchedulerPoisonRecord?>(null);

        var record = GroundworkV2WorkflowSchedulerPoisonStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(record, workflowExecutionId, workItemId);
        return ValueTask.FromResult<RuntimeSchedulerPoisonRecord?>(record);
    }

    public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var table = new TableId(Unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var firstFailedAt = Column(table, ElsaRuntimeV2StorageManifest.SchedulerPoisonFirstFailedAtField);
        var lastFailedAt = Column(table, ElsaRuntimeV2StorageManifest.SchedulerPoisonLastFailedAtField);
        var workItem = Column(table, ElsaRuntimeV2StorageManifest.SchedulerPoisonWorkItemIdField);
        var records = new List<RuntimeSchedulerPoisonRecord>();
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                table,
                Equal(workflow, workflowExecutionId),
                [
                    new OrderTerm(firstFailedAt, OrderDirection.Ascending, NullOrder.Last),
                    new OrderTerm(lastFailedAt, OrderDirection.Ascending, NullOrder.Last),
                    new OrderTerm(workItem, OrderDirection.Ascending, NullOrder.Last)
                ],
                Projection.All,
                PagingFor(RuntimeStorePageRequest.MaximumLimit, cursor)));
            foreach (var row in result.Rows)
            {
                var record = GroundworkV2WorkflowSchedulerPoisonStorageConventions.Deserialize(row);
                EnsureIdentity(record, workflowExecutionId, record.WorkItemId);
                records.Add(record);
            }

            if (result.NextContinuationToken is { } next && !seenContinuations.Add(next))
            {
                throw new InvalidDataException(
                    "Groundwork scheduler-poison continuation repeated or cycled.");
            }

            cursor = result.NextContinuationToken;
        } while (cursor is not null);

        return ValueTask.FromResult<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>>(records);
    }

    private static void ValidateIdentity(string workflowExecutionId, string workItemId)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        _ = GroundworkV2WorkflowSchedulerPoisonStorageConventions.PhysicalId(workflowExecutionId, workItemId);
    }

    private static void ValidateWorkflowExecutionId(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (workflowExecutionId.Length > ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workflowExecutionId),
                workflowExecutionId,
                $"Groundwork runtime identity parts cannot exceed {ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength} characters.");
        }
    }

    private static void EnsureIdentity(
        RuntimeSchedulerPoisonRecord record,
        string workflowExecutionId,
        string workItemId)
    {
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.WorkItemId, workItemId))
        {
            throw new InvalidDataException(
                "Groundwork scheduler-poison row identity does not match its requested key.");
        }
    }
}
