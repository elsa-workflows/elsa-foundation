using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Agent.Core.Exceptions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Contracts;

namespace Elsa.Agent.Journal;

internal sealed class InMemoryAgentTurnState
{
    public Lock Gate { get; } = new();
    public Dictionary<string, AgentTurnDocument> Rows { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, AgentOperationDocument> Operations { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);
    public Dictionary<(string? Tenant, string Partition, string Id), AgentConversationDocument> Conversations { get; } = new();
}

internal sealed partial class InMemoryAgentTurnStore(IPersistenceAccessContextAccessor access, InMemoryAgentTurnState state) : IAgentTurnStore
{
    public ValueTask<AgentJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AgentJournalCapabilities("in-memory", false));
    }

    private Lock gate => state.Gate;
    private Dictionary<string, AgentTurnDocument> rows => state.Rows;
    private Dictionary<string, string> keys => state.Keys;

    public ValueTask<AgentTurnDocument> StageAsync(AgentTurnDocument candidate, CancellationToken cancellationToken) => StageAsync(candidate, true, cancellationToken);

    public ValueTask<AgentTurnDocument> StageAsync(AgentTurnDocument candidate, bool allowNewOperation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureScope(candidate.Identity);
        ValidateRecord(candidate, prepared: true);
        lock (gate)
        {
            var operation = Operation(candidate.Identity);
            if (AgentOperationRules.Cancelled(operation, candidate.Identity))
                throw new AgentJournalException("agent.journal.cancelled", null, "This native parent was cancelled before admission.", new InvalidOperationException("A retained native cancellation blocks this delivery."));
            if (keys.TryGetValue(candidate.Key, out var handle))
            {
                var existing = rows[handle];
                if (!AgentTurnDeliveryRules.SameLogical(existing.Identity, candidate.Identity) || existing.RequestHash != candidate.RequestHash || existing.DeliveryProtocol != candidate.DeliveryProtocol ||
                    existing.DeliveryProtocol == 0 && existing.Identity != candidate.Identity)
                    throw new AgentJournalException("agent.journal.conflict", existing.Handle, "A different agent request already owns this native operation.", new InvalidOperationException("The native operation fingerprint changed."));
                var associated = AgentTurnDeliveryRules.Register(existing, candidate.Identity);
                ValidateRecord(associated, prepared: false);
                rows[handle] = AgentTurnDocumentRules.Copy(associated);
                state.Operations[candidate.Key] = operation;
                return ValueTask.FromResult(AgentTurnDocumentRules.Copy(associated));
            }
            if (!allowNewOperation)
                throw new AgentJournalException("agent.journal.admission-disabled", null, "New agent operations are disabled for this host.", new InvalidOperationException("The creation policy permits only retained operations."));
            if (rows.Count >= 10_000) throw new AgentJournalException("agent.journal.capacity", null, "The process-local agent journal is full.", new InvalidOperationException("The development journal capacity was reached."));
            rows.Add(candidate.Handle, AgentTurnDocumentRules.Copy(candidate));
            keys.Add(candidate.Key, candidate.Handle);
            state.Operations[candidate.Key] = operation;
            return ValueTask.FromResult(AgentTurnDocumentRules.Copy(candidate));
        }
    }

    public ValueTask<AgentTurnDocument?> ReadAsync(string handle, AgentInvocationIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(Find(handle, identity));
    }

    public ValueTask<AgentTurnDocument?> ClaimAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null) return ValueTask.FromResult<AgentTurnDocument?>(null);
            var operation = Operation(identity);
            if (AgentOperationRules.Cancelled(operation, identity)) return ValueTask.FromResult<AgentTurnDocument?>(null);
            if (row.Status == AgentTurnStatus.Dispatching && (row.LeaseExpiresAt is null || row.LeaseExpiresAt <= now || row.Deadline <= now))
            {
                _ = TrySave(row with { Status = AgentTurnStatus.OutcomeUnknown, FailureCode = "agent.turn.outcome-unknown" });
                return ValueTask.FromResult<AgentTurnDocument?>(null);
            }
            if (now >= row.Deadline || !AgentTurnLeaseRules.CanClaim(row, identity, childInvocationId, now)) return ValueTask.FromResult<AgentTurnDocument?>(null);
            row = AgentTurnLeaseRules.Claim(row, identity, childInvocationId, fence, now);
            if (!TrySave(row)) return ValueTask.FromResult<AgentTurnDocument?>(null);
            state.Operations[row.Key] = operation;
            return ValueTask.FromResult<AgentTurnDocument?>(row);
        }
    }

    public ValueTask<AgentTurnDocument?> ReplayAcceptedAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var document = Find(handle, identity);
            if (document is null || document.Status != AgentTurnStatus.Accepted || !AgentTurnDeliveryRules.CanBindChild(document, identity, childInvocationId))
                return ValueTask.FromResult<AgentTurnDocument?>(null);
            var next = AgentTurnDeliveryRules.BindChild(document, identity, childInvocationId);
            ValidateRecord(next, prepared: false);
            // Delivery metadata never reapplies the accepted conversation transition.
            rows[handle] = AgentTurnDocumentRules.Copy(next);
            return ValueTask.FromResult<AgentTurnDocument?>(AgentTurnDocumentRules.Copy(next));
        }
    }

    public ValueTask<AgentTurnDocument?> ApplyAsync(string handle, AgentTurnApplication application, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(application);
        lock (gate)
        {
            var document = Find(handle, application.Identity);
            if (document is null || !AgentTurnApplicationRules.Admits(document, application)) return ValueTask.FromResult<AgentTurnDocument?>(null);
            var next = AgentTurnApplicationRules.Apply(document, application);
            ValidateRecord(next, prepared: false);
            rows[handle] = AgentTurnDocumentRules.Copy(next);
            return ValueTask.FromResult<AgentTurnDocument?>(AgentTurnDocumentRules.Copy(next));
        }
    }

    public ValueTask<AgentCancellationReceipt> CancelAsync(AgentTurnCancellation cancellation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ArgumentNullException.ThrowIfNull(cancellation);
            EnsureScope(cancellation.Identity);
            AgentOperationRules.Cancellation(cancellation);
            lock (gate)
            {
                var key = AgentTurnDocument.CalculateOperationKey(cancellation.Identity);
                var operation = Operation(cancellation.Identity);
                var turn = keys.TryGetValue(key, out var handle) ? rows[handle] : null;
                var decision = AgentOperationRules.Cancel(operation, cancellation, turn);
                if (!ReferenceEquals(decision.Turn, turn) && decision.Turn is { } next)
                {
                    // A prepared turn has not reserved a conversation. Terminal metadata may not
                    // touch a newer conversation owner or replay its accepted history.
                    if (turn!.Status == AgentTurnStatus.Prepared)
                    {
                        ValidateRecord(next, prepared: false);
                        rows[next.Handle] = AgentTurnDocumentRules.Copy(next);
                    }
                    else if (!TrySave(next)) throw AgentOperationRules.Conflict();
                }
                state.Operations[key] = decision.Operation;
                return ValueTask.FromResult(decision.Receipt);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (AgentJournalException) { throw; }
        catch (Exception exception)
        { throw new AgentJournalException("agent.journal.cancellation", null, "The scoped agent cancellation could not be confirmed.", exception); }
    }

    private AgentOperationDocument Operation(AgentInvocationIdentity identity)
    {
        try { return OperationCore(identity); }
        catch (AgentJournalException) { throw; }
        catch (Exception exception) { throw new AgentJournalException("agent.journal.identity", null, "The agent operation identity could not be validated.", exception); }
    }

    private AgentOperationDocument OperationCore(AgentInvocationIdentity identity)
    {
        AgentOperationRules.Identity(identity);
        var key = AgentTurnDocument.CalculateOperationKey(identity);
        if (!state.Operations.TryGetValue(key, out var operation))
        {
            if (state.Operations.Count >= 10_000)
                throw new AgentJournalException("agent.journal.capacity", null, "The process-local agent operation journal is full.", new InvalidOperationException("The development operation capacity was reached."));
            operation = new(identity, []);
        }
        AgentOperationRules.Match(operation, identity);
        return operation;
    }

    public ValueTask<AgentTurnDocument?> RenewAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AgentTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status is not (AgentTurnStatus.Claimed or AgentTurnStatus.Dispatching) || !AgentTurnLeaseRules.Live(row, lease, now))
                return ValueTask.FromResult<AgentTurnDocument?>(null);
            var expiry = AgentTurnLeaseRules.Expires(row, now);
            var next = row with { LeaseExpiresAt = expiry > row.LeaseExpiresAt ? expiry : row.LeaseExpiresAt };
            return ValueTask.FromResult<AgentTurnDocument?>(TrySave(next) ? next : null);
        }
    }

    public ValueTask<bool> DispatchAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AgentTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != AgentTurnStatus.Claimed || !AgentTurnLeaseRules.Live(row, lease, now)) return ValueTask.FromResult(false);
            return ValueTask.FromResult(TrySave(row with { Status = AgentTurnStatus.Dispatching }));
        }
    }

    public ValueTask<bool> AcceptAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AgentTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != AgentTurnStatus.Dispatching || !AgentTurnLeaseRules.Live(row, lease, now) ||
                row.ModelRequests is { Count: > 0 } && row.ResponseObservation is null) return ValueTask.FromResult(false);
            return ValueTask.FromResult(TrySave(row with { Status = AgentTurnStatus.Accepted, ProtectedResult = protectedResult }));
        }
    }

    public ValueTask FailAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, string code, bool outcomeUnknown, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AgentTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is not null && AgentTurnLeaseRules.Matches(row, lease) && row.Status is AgentTurnStatus.Claimed or AgentTurnStatus.Dispatching)
                _ = TrySave(row with { Status = outcomeUnknown ? AgentTurnStatus.OutcomeUnknown : AgentTurnStatus.Failed, FailureCode = code });
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentModelRequestReservation?> ReserveModelRequestAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease,
        AgentModelRequestReservation reservation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AgentTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != AgentTurnStatus.Dispatching || !AgentTurnLeaseRules.Live(row, lease, now) || row.ResponseObservation is not null ||
                (row.ModelRequests?.Count ?? 0) >= row.MaximumModelCalls || (row.ModelRequests ?? []).Any(x => x.Id == reservation.Id))
                return ValueTask.FromResult<AgentModelRequestReservation?>(null);
            return ValueTask.FromResult<AgentModelRequestReservation?>(TrySave(row with { ModelRequests = [.. row.ModelRequests ?? [], reservation] }) ? reservation : null);
        }
    }

    public ValueTask<bool> CompleteModelResponseAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, IReadOnlyList<string> requestIds,
        AgentModelResponseObservation observation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AgentTurnDocumentRules.Lease(lease);
        ArgumentNullException.ThrowIfNull(requestIds);
        ArgumentNullException.ThrowIfNull(observation);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != AgentTurnStatus.Dispatching || !AgentTurnLeaseRules.Live(row, lease, now) || row.ResponseObservation is not null ||
                requestIds.Count == 0 || !(row.ModelRequests ?? []).Select(x => x.Id).SequenceEqual(requestIds, StringComparer.Ordinal))
                return ValueTask.FromResult(false);
            return ValueTask.FromResult(TrySave(row with { ResponseObservation = observation }));
        }
    }

    public ValueTask<AgentConversationDocument?> ReadConversationAsync(string conversationId, AgentInvocationIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureScope(identity);
        try { AgentTurnDocumentRules.Token(conversationId); }
        catch (Exception exception) { throw new AgentJournalException("agent.journal.record", null, "The agent conversation identity is invalid.", exception); }
        lock (gate)
        {
            state.Conversations.TryGetValue((identity.TenantId, identity.PersistencePartition, conversationId), out var document);
            return ValueTask.FromResult(document?.Owner == AgentConversationOwner.FromInvocation(identity) ? document : null);
        }
    }

    private bool TrySave(AgentTurnDocument document)
    {
        ValidateRecord(document, prepared: false);
        if (document.Conversation is { } admission)
        {
            var key = (document.Identity.TenantId, document.Identity.PersistencePartition, admission.ConversationId);
            state.Conversations.TryGetValue(key, out var current);
            var next = AgentConversationRules.Change(rows[document.Handle], document, current);
            if (next is null) return false;
            AgentConversationRules.Validate(next);
            state.Conversations[key] = next;
        }
        rows[document.Handle] = AgentTurnDocumentRules.Copy(document);
        return true;
    }

    private static void ValidateRecord(AgentTurnDocument document, bool prepared)
    {
        try
        {
            if (prepared) AgentTurnDocumentRules.Prepared(document);
            else AgentTurnDocumentRules.Validate(document);
        }
        catch (AgentJournalException) { throw; }
        catch (Exception exception)
        {
            throw new AgentJournalException("agent.journal.record", document.Handle, "The agent journal record is invalid.", exception);
        }
    }

    private AgentTurnDocument? Find(string handle, AgentInvocationIdentity identity)
    {
        EnsureScope(identity);
        return rows.TryGetValue(handle, out var row) && AgentTurnDeliveryRules.Admits(row, identity) ? AgentTurnDocumentRules.Copy(row) : null;
    }

    private void EnsureScope(AgentInvocationIdentity identity) => EnsureScope(identity.TenantId, identity.PersistencePartition);

    private void EnsureScope(string? tenantId, string persistencePartition)
    {
        try
        {
            var scope = access.Current.RequireScope();
            access.Current.EnsureTenantScope(tenantId);
            if (scope.Value != persistencePartition) throw new InvalidOperationException("The agent operation does not belong to the selected persistence scope.");
        }
        catch (Exception exception)
        {
            throw new AgentJournalException("agent.scope", null, "The agent turn is unavailable in the selected ordinary scope.", exception);
        }
    }
}
