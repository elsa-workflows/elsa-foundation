using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

public sealed class GroundworkV2WorkflowActivationAuthority : IWorkflowActivationAuthority
{
    private const int MaxTransitionAttempts = 16;
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly GroundworkStorageTransactionFactory transactions;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2WorkflowActivationAuthority(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        GroundworkStorageTransactionFactory transactions,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        ArgumentNullException.ThrowIfNull(transactions);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.transactions = transactions;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind, targetName);
    }

    public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
    {
        var id = SlotId(workflowDefinitionId, slotName, cancellationToken);
        var entry = Open().Read(GroundworkRuntimeRowStore.Key(id));
        return ValueTask.FromResult(entry is null ? null : Read(entry));
    }

    public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(unit.Name);
        var definition = Column(table, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDefinitionIdField);
        var slotName = Column(table, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotNameField);
        var predicate = new Predicate.Equal(definition, QueryConstant.Of(definition, workflowDefinitionId));
        var slots = new List<WorkflowActivationSlot>();
        string? continuation = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Open().Query(new QueryRequest(
                table,
                predicate,
                [new OrderTerm(slotName, OrderDirection.Ascending, NullOrder.Last), new OrderTerm(Column(table, ElsaRuntimeV2StorageManifest.IdField), OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                continuation is null ? Paging.Keyset(100) : Paging.Continuation(continuation, 100)));
            slots.AddRange(result.Rows.Select(values => Read(values)));
            continuation = result.NextContinuationToken;
            if (continuation is not null && !seen.Add(continuation))
                throw new InvalidOperationException("Groundwork activation-slot query repeated a continuation token.");
        } while (continuation is not null);
        var orderedSlots = slots.OrderBy(slot => slot.SlotName, StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult<IReadOnlyCollection<WorkflowActivationSlot>>(orderedSlots);
    }

    public async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        var slotId = SlotId(request.WorkflowDefinitionId, request.SlotName, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivationId);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedRevision);
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = Open().Read(GroundworkRuntimeRowStore.Key(slotId));
            var current = entry is null ? Empty(slotId, request.WorkflowDefinitionId, request.SlotName, request.UpdatedAt) : Read(entry);
            if (current.Revision != request.ExpectedRevision)
                return Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation slot revision changed; another writer moved it first.");
            if (current.ActiveActivationId is not null && current.Source is not null && request.OwnershipIntent != WorkflowActivationOwnershipIntent.TakeOver && !current.Source.IsSameOwnerAs(request.Source))
                return Conflict(current, WorkflowActivationConflict.ForeignSource, $"Definition '{request.WorkflowDefinitionId}' slot '{request.SlotName}' is owned by activation source '{current.Source.Describe()}'; '{request.Source.Describe()}' cannot activate a different artifact on it. Ownership transfer is an explicit operator action.");
            if (await IsLiveInAnotherSlotAsync(request.ActivationId, slotId, cancellationToken))
                return Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation is already live in another slot.");
            var next = current with { ActiveActivationId = request.ActivationId, Source = request.Source, Revision = current.Revision + 1, UpdatedAt = request.UpdatedAt };
            var outcome = await CommitAsync(next, entry, cancellationToken);
            if (outcome == TransitionWriteOutcome.Succeeded)
                return new WorkflowActivationTransition(true, next, current.ActiveActivationId, ReplacedSource: current.Source);
            if (outcome == TransitionWriteOutcome.Conflict)
                continue;
            throw new InvalidOperationException("Groundwork rejected activation-slot transition.");
        }
        return Conflict((await FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken)) ?? Empty(slotId, request.WorkflowDefinitionId, request.SlotName, request.UpdatedAt), WorkflowActivationConflict.RevisionMismatch, "The activation slot changed concurrently and did not settle.");
    }

    public async ValueTask<WorkflowActivationTransition> TryDeactivateAsync(string workflowDefinitionId, string slotName, WorkflowActivationSource source, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var slotId = SlotId(workflowDefinitionId, slotName, cancellationToken);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = Open().Read(GroundworkRuntimeRowStore.Key(slotId));
            var current = entry is null ? Empty(slotId, workflowDefinitionId, slotName, updatedAt) : Read(entry);
            if (current.Revision != expectedRevision)
                return Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation slot revision changed; another writer moved it first.");
            if (current.ActiveActivationId is not null && current.Source is not null && !current.Source.IsSameOwnerAs(source))
                return Conflict(current, WorkflowActivationConflict.ForeignSource, $"Definition '{workflowDefinitionId}' slot '{slotName}' is owned by activation source '{current.Source.Describe()}'; '{source.Describe()}' cannot deactivate it.");
            var next = current with { ActiveActivationId = null, Source = null, Revision = current.Revision + 1, UpdatedAt = updatedAt };
            var outcome = await CommitAsync(next, entry, cancellationToken);
            if (outcome == TransitionWriteOutcome.Succeeded)
                return new WorkflowActivationTransition(true, next, current.ActiveActivationId, ReplacedSource: current.Source);
            if (outcome == TransitionWriteOutcome.Conflict)
                continue;
            throw new InvalidOperationException("Groundwork rejected activation-slot transition.");
        }
        return Conflict((await FindAsync(workflowDefinitionId, slotName, cancellationToken)) ?? Empty(slotId, workflowDefinitionId, slotName, updatedAt), WorkflowActivationConflict.RevisionMismatch, "The activation slot changed concurrently and did not settle.");
    }

    private async ValueTask<TransitionWriteOutcome> CommitAsync(WorkflowActivationSlot slot, StoredEntry? existing, CancellationToken cancellationToken)
    {
        using var transaction = transactions.Begin("workflow-activation-authority", [unit.Id.Value], targetName);
        if (existing is null)
            transaction.StageInsert(unit.Id.Value, GroundworkV2WorkflowActivationSlotStorageConventions.Values(slot), WriteOptions.CreateOnly);
        else
            transaction.Stage(unit.Id.Value, GroundworkV2WorkflowActivationSlotStorageConventions.Values(slot, clearInactiveProjection: true), WriteOptions.IfVersion(existing.Version ?? throw new InvalidDataException("Activation-slot row did not expose an optimistic revision.")));
        BatchWriteReport report;
        try
        {
            report = await transaction.Inner.CommitWithOutcomesAsync(cancellationToken);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
        if (report.IsSuccessful) return TransitionWriteOutcome.Succeeded;
        try { transaction.Rollback(); } catch { }
        if (report.Outcomes.Any(outcome => outcome.Outcome.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.UniqueViolation))
            return TransitionWriteOutcome.Conflict;
        return TransitionWriteOutcome.Failed;
    }

    private async ValueTask<bool> IsLiveInAnotherSlotAsync(string activationId, string slotId, CancellationToken cancellationToken)
    {
        var table = new TableId(unit.Name);
        var active = Column(table, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotActiveActivationIdField);
        var result = Open().Query(new QueryRequest(table, new Predicate.Equal(active, QueryConstant.Of(active, activationId)), [new OrderTerm(Column(table, ElsaRuntimeV2StorageManifest.IdField), OrderDirection.Ascending, NullOrder.Last)], Projection.All, Paging.Keyset(2)));
        cancellationToken.ThrowIfCancellationRequested();
        return result.Rows.Select(values => Read(values)).Any(slot => !StringComparer.Ordinal.Equals(slot.SlotId, slotId));
    }

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current ?? throw new InvalidOperationException("Workflow activation persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
            throw new InvalidOperationException("Groundwork workflow activation authority requires one explicit persistence scope.");
        return sessions.Open(unit.Id.Value, StorageAccess.Scoped(new StorageScope(context.Scope.Value)), targetName);
    }

    private WorkflowActivationSlot Read(StoredEntry entry) => GroundworkV2WorkflowActivationSlotStorageConventions.Deserialize(entry.Values.Values);
    private WorkflowActivationSlot Read(IReadOnlyDictionary<string, object?> values) => GroundworkV2WorkflowActivationSlotStorageConventions.Deserialize(values);
    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.Single(column => StringComparer.Ordinal.Equals(column.Name, name));
        var type = definition.Type switch { PortableType.String => QueryType.String, _ => throw new InvalidOperationException($"Unsupported activation-slot query column type '{definition.Type}'.") };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }
    private static string SlotId(string definition, string name, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return WorkflowActivationSlotIdentity.Create(definition, name); }
    private static WorkflowActivationSlot Empty(string id, string definition, string name, DateTimeOffset updatedAt) => new(id, definition, name, null, null, 0, updatedAt);
    private static WorkflowActivationTransition Conflict(WorkflowActivationSlot slot, WorkflowActivationConflict conflict, string message) => new(false, slot, Conflict: conflict, Diagnostic: message);
    private enum TransitionWriteOutcome { Succeeded, Conflict, Failed }
}
