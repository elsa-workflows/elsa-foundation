using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Core.Contracts;

namespace Elsa.Agent.Specialists.Journal;

internal sealed class InMemorySpecialistTurnState
{
    public Lock Gate { get; } = new();
    public Dictionary<string, SpecialistTurnDocument> Rows { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);
    public Dictionary<(string? Tenant, string Partition, string Id), SpecialistConversationDocument> Conversations { get; } = new();
}

internal sealed class InMemorySpecialistTurnStore(IPersistenceAccessContextAccessor access, InMemorySpecialistTurnState state) : ISpecialistTurnStore
{
    public ValueTask<SpecialistJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new SpecialistJournalCapabilities("in-memory", false));
    }

    private Lock gate => state.Gate;
    private Dictionary<string, SpecialistTurnDocument> rows => state.Rows;
    private Dictionary<string, string> keys => state.Keys;

    public ValueTask<SpecialistTurnDocument> StageAsync(SpecialistTurnDocument candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureScope(candidate.Identity);
        ValidateRecord(candidate, prepared: true);
        lock (gate)
        {
            if (keys.TryGetValue(candidate.Key, out var handle))
            {
                var existing = rows[handle];
                if (existing.Identity != candidate.Identity || existing.RequestHash != candidate.RequestHash)
                    throw new SpecialistJournalException("specialist.journal.conflict", existing.Handle, "A different specialist request already owns this native operation.", new InvalidOperationException("The native operation fingerprint changed."));
                return ValueTask.FromResult(SpecialistTurnDocumentRules.Copy(existing));
            }
            if (rows.Count >= 10_000) throw new SpecialistJournalException("specialist.journal.capacity", null, "The process-local specialist journal is full.", new InvalidOperationException("The development journal capacity was reached."));
            rows.Add(candidate.Handle, SpecialistTurnDocumentRules.Copy(candidate));
            keys.Add(candidate.Key, candidate.Handle);
            return ValueTask.FromResult(SpecialistTurnDocumentRules.Copy(candidate));
        }
    }

    public ValueTask<SpecialistTurnDocument?> ReadAsync(string handle, SpecialistInvocationIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(Find(handle, identity));
    }

    public ValueTask<SpecialistTurnDocument?> ClaimAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null) return ValueTask.FromResult<SpecialistTurnDocument?>(null);
            if (row.Status == SpecialistTurnStatus.Dispatching && (row.LeaseExpiresAt is null || row.LeaseExpiresAt <= now || row.Deadline <= now))
            {
                _ = TrySave(row with { Status = SpecialistTurnStatus.OutcomeUnknown, FailureCode = "specialist.turn.outcome-unknown" });
                return ValueTask.FromResult<SpecialistTurnDocument?>(null);
            }
            if (now >= row.Deadline || !SpecialistTurnLeaseRules.CanClaim(row, childInvocationId, now)) return ValueTask.FromResult<SpecialistTurnDocument?>(null);
            row = SpecialistTurnLeaseRules.Claim(row, childInvocationId, fence, now);
            return ValueTask.FromResult<SpecialistTurnDocument?>(TrySave(row) ? row : null);
        }
    }

    public ValueTask<SpecialistTurnDocument?> RenewAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpecialistTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status is not (SpecialistTurnStatus.Claimed or SpecialistTurnStatus.Dispatching) || !SpecialistTurnLeaseRules.Live(row, lease, now))
                return ValueTask.FromResult<SpecialistTurnDocument?>(null);
            var expiry = SpecialistTurnLeaseRules.Expires(row, now);
            var next = row with { LeaseExpiresAt = expiry > row.LeaseExpiresAt ? expiry : row.LeaseExpiresAt };
            return ValueTask.FromResult<SpecialistTurnDocument?>(TrySave(next) ? next : null);
        }
    }

    public ValueTask<bool> DispatchAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpecialistTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != SpecialistTurnStatus.Claimed || !SpecialistTurnLeaseRules.Live(row, lease, now)) return ValueTask.FromResult(false);
            return ValueTask.FromResult(TrySave(row with { Status = SpecialistTurnStatus.Dispatching }));
        }
    }

    public ValueTask<bool> AcceptAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpecialistTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != SpecialistTurnStatus.Dispatching || !SpecialistTurnLeaseRules.Live(row, lease, now) ||
                row.ModelRequests is { Count: > 0 } && row.ResponseObservation is null) return ValueTask.FromResult(false);
            return ValueTask.FromResult(TrySave(row with { Status = SpecialistTurnStatus.Accepted, ProtectedResult = protectedResult }));
        }
    }

    public ValueTask FailAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, string code, bool outcomeUnknown, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpecialistTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is not null && SpecialistTurnLeaseRules.Matches(row, lease) && row.Status is SpecialistTurnStatus.Claimed or SpecialistTurnStatus.Dispatching)
                _ = TrySave(row with { Status = outcomeUnknown ? SpecialistTurnStatus.OutcomeUnknown : SpecialistTurnStatus.Failed, FailureCode = code });
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<SpecialistModelRequestReservation?> ReserveModelRequestAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease,
        SpecialistModelRequestReservation reservation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpecialistTurnDocumentRules.Lease(lease);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != SpecialistTurnStatus.Dispatching || !SpecialistTurnLeaseRules.Live(row, lease, now) || row.ResponseObservation is not null ||
                (row.ModelRequests?.Count ?? 0) >= row.MaximumModelCalls || (row.ModelRequests ?? []).Any(x => x.Id == reservation.Id))
                return ValueTask.FromResult<SpecialistModelRequestReservation?>(null);
            return ValueTask.FromResult<SpecialistModelRequestReservation?>(TrySave(row with { ModelRequests = [.. row.ModelRequests ?? [], reservation] }) ? reservation : null);
        }
    }

    public ValueTask<bool> CompleteModelResponseAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, IReadOnlyList<string> requestIds,
        SpecialistModelResponseObservation observation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpecialistTurnDocumentRules.Lease(lease);
        ArgumentNullException.ThrowIfNull(requestIds);
        ArgumentNullException.ThrowIfNull(observation);
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != SpecialistTurnStatus.Dispatching || !SpecialistTurnLeaseRules.Live(row, lease, now) || row.ResponseObservation is not null ||
                requestIds.Count == 0 || !(row.ModelRequests ?? []).Select(x => x.Id).SequenceEqual(requestIds, StringComparer.Ordinal))
                return ValueTask.FromResult(false);
            return ValueTask.FromResult(TrySave(row with { ResponseObservation = observation }));
        }
    }

    public ValueTask<SpecialistConversationDocument?> ReadConversationAsync(string conversationId, SpecialistInvocationIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureScope(identity);
        try { SpecialistTurnDocumentRules.Token(conversationId); }
        catch (Exception exception) { throw new SpecialistJournalException("specialist.journal.record", null, "The specialist conversation identity is invalid.", exception); }
        lock (gate)
        {
            state.Conversations.TryGetValue((identity.TenantId, identity.PersistencePartition, conversationId), out var document);
            return ValueTask.FromResult(document?.Owner == SpecialistConversationOwner.FromInvocation(identity) ? document : null);
        }
    }

    private bool TrySave(SpecialistTurnDocument document)
    {
        ValidateRecord(document, prepared: false);
        if (document.Conversation is { } admission)
        {
            var key = (document.Identity.TenantId, document.Identity.PersistencePartition, admission.ConversationId);
            state.Conversations.TryGetValue(key, out var current);
            var next = SpecialistConversationRules.Change(rows[document.Handle], document, current);
            if (next is null) return false;
            SpecialistConversationRules.Validate(next);
            state.Conversations[key] = next;
        }
        rows[document.Handle] = SpecialistTurnDocumentRules.Copy(document);
        return true;
    }

    private static void ValidateRecord(SpecialistTurnDocument document, bool prepared)
    {
        try
        {
            if (prepared) SpecialistTurnDocumentRules.Prepared(document);
            else SpecialistTurnDocumentRules.Validate(document);
        }
        catch (SpecialistJournalException) { throw; }
        catch (Exception exception)
        {
            throw new SpecialistJournalException("specialist.journal.record", document.Handle, "The specialist journal record is invalid.", exception);
        }
    }

    private SpecialistTurnDocument? Find(string handle, SpecialistInvocationIdentity identity)
    {
        EnsureScope(identity);
        return rows.TryGetValue(handle, out var row) && row.Identity == identity ? SpecialistTurnDocumentRules.Copy(row) : null;
    }

    private void EnsureScope(SpecialistInvocationIdentity identity)
    {
        try
        {
            var scope = access.Current.RequireScope();
            access.Current.EnsureTenantScope(identity.TenantId);
            if (scope.Value != identity.PersistencePartition) throw new InvalidOperationException("The specialist operation does not belong to the selected persistence scope.");
        }
        catch (Exception exception)
        {
            throw new SpecialistJournalException("specialist.scope", null, "The specialist turn is unavailable in the selected ordinary scope.", exception);
        }
    }
}
