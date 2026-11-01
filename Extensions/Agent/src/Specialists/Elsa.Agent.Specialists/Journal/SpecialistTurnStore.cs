using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Core.Contracts;

namespace Elsa.Agent.Specialists.Journal;

internal sealed class InMemorySpecialistTurnStore : ISpecialistTurnStore
{
    public ValueTask<SpecialistJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new SpecialistJournalCapabilities("in-memory", false));
    }

    private readonly Lock gate = new();
    private readonly Dictionary<string, SpecialistTurnDocument> rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> keys = new(StringComparer.Ordinal);

    public ValueTask<SpecialistTurnDocument> StageAsync(SpecialistTurnDocument candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidate.Key != SpecialistTurnDocument.CalculateOperationKey(candidate.Identity))
            throw new InvalidDataException("The specialist operation key does not match its native scope and operation.");
        lock (gate)
        {
            if (keys.TryGetValue(candidate.Key, out var handle))
            {
                var existing = rows[handle];
                if (existing.Identity != candidate.Identity || existing.RequestHash != candidate.RequestHash)
                    throw new InvalidOperationException("A different specialist request already owns this native operation.");
                return ValueTask.FromResult(existing);
            }
            if (rows.Count >= 10_000) throw new InvalidOperationException("The process-local specialist journal is full.");
            rows.Add(candidate.Handle, candidate);
            keys.Add(candidate.Key, candidate.Handle);
            return ValueTask.FromResult(candidate);
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
            if (row is null || row.Status != SpecialistTurnStatus.Prepared || now >= row.Deadline) return ValueTask.FromResult<SpecialistTurnDocument?>(null);
            row = row with { Status = SpecialistTurnStatus.Claimed, ChildInvocationId = childInvocationId, Fence = fence };
            rows[handle] = row;
            return ValueTask.FromResult<SpecialistTurnDocument?>(row);
        }
    }

    public ValueTask<bool> DispatchAsync(string handle, SpecialistInvocationIdentity identity, string fence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != SpecialistTurnStatus.Claimed || row.Fence != fence || now >= row.Deadline) return ValueTask.FromResult(false);
            rows[handle] = row with { Status = SpecialistTurnStatus.Dispatching };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> AcceptAsync(string handle, SpecialistInvocationIdentity identity, string fence, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is null || row.Status != SpecialistTurnStatus.Dispatching || row.Fence != fence || now >= row.Deadline) return ValueTask.FromResult(false);
            rows[handle] = row with { Status = SpecialistTurnStatus.Accepted, ProtectedResult = protectedResult };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask FailAsync(string handle, SpecialistInvocationIdentity identity, string fence, string code, bool outcomeUnknown, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var row = Find(handle, identity);
            if (row is not null && row.Fence == fence && row.Status is SpecialistTurnStatus.Claimed or SpecialistTurnStatus.Dispatching)
                rows[handle] = row with { Status = outcomeUnknown ? SpecialistTurnStatus.OutcomeUnknown : SpecialistTurnStatus.Failed, FailureCode = code };
        }
        return ValueTask.CompletedTask;
    }

    private SpecialistTurnDocument? Find(string handle, SpecialistInvocationIdentity identity) =>
        rows.TryGetValue(handle, out var row) && row.Identity == identity ? row : null;
}
