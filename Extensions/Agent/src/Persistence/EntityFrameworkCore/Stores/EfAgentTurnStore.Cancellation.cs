using System.Text.Json;
using Elsa.Agent.Core.Exceptions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfAgentTurnStore
{
    // Only confirmed gate insert/CAS loss can retry. A commit exception is never caught here.
    private sealed class OperationGateRaceException(Exception cause) : Exception("A competing operation gate was committed.", cause);

    private async ValueTask<(AgentOperationRecord Row, AgentOperationDocument Document)?> LockOperationAsync(
        DbContext owned, string scope, AgentInvocationIdentity identity, CancellationToken cancellationToken)
    {
        AgentOperationRules.Identity(identity);
        var key = AgentTurnDocument.CalculateOperationKey(identity);
        var row = await owned.Set<AgentOperationRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scope && item.OperationKey == key, cancellationToken);
        if (row is null)
        {
            var document = new AgentOperationDocument(identity, []);
            row = new() { ScopeKey = scope, OperationKey = key, SchemaVersion = AgentEfModule.SchemaVersion, Payload = OperationPayload(document) };
            owned.Set<AgentOperationRecord>().Add(row);
            try { await owned.SaveChangesAsync(cancellationToken); }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
            { throw new OperationGateRaceException(exception); }
            return (row, document);
        }
        if (row.SchemaVersion != AgentEfModule.SchemaVersion || row.Version < 0 || row.Payload.Length > MaximumRecordBytes)
            throw new InvalidDataException("The agent operation gate schema or bounds are unsupported.");
        var operation = JsonSerializer.Deserialize<AgentOperationDocument>(row.Payload, Json) ?? throw new InvalidDataException("The agent operation gate is missing.");
        AgentOperationRules.Match(operation, identity);
        if (AgentTurnDocument.CalculateOperationKey(operation.Identity) != key) throw new InvalidDataException("The operation gate belongs to another native operation.");
        var nextVersion = checked(row.Version + 1);
        var changed = await owned.Set<AgentOperationRecord>().Where(item => item.ScopeKey == scope && item.OperationKey == key &&
                item.SchemaVersion == AgentEfModule.SchemaVersion && item.Version == row.Version)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, nextVersion), cancellationToken);
        if (changed != 1) return null;
        row.Version = nextVersion;
        return (row, operation);
    }

    public async ValueTask<AgentCancellationReceipt> CancelAsync(AgentTurnCancellation cancellation, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(cancellation);
            AgentOperationRules.Cancellation(cancellation);
            var scope = Scope(cancellation.Identity);
            var key = AgentTurnDocument.CalculateOperationKey(cancellation.Identity);
            for (var attempt = 0; attempt < 32; attempt++)
            {
                try
                {
                    await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
                    var owned = transaction.Context<DbContext>();
                    var operation = await LockOperationAsync(owned, scope, cancellation.Identity, cancellationToken);
                    if (operation is null) continue;
                    var row = await owned.Set<AgentTurnRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scope && item.OperationKey == key, cancellationToken);
                    var before = row is null ? null : Read(row);
                    var decision = AgentOperationRules.Cancel(operation.Value.Document, cancellation, before);
                    if (!ReferenceEquals(before, decision.Turn) && decision.Turn is { } next)
                    {
                        AgentTurnDocumentRules.Validate(next);
                        var changed = await owned.Set<AgentTurnRecord>().Where(item => item.ScopeKey == scope && item.Handle == row!.Handle &&
                                item.SchemaVersion == AgentEfModule.SchemaVersion && item.Version == row.Version && item.Status == row.Status && item.Fence == row.Fence)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, row!.Version + 1).SetProperty(item => item.Status, (int)next.Status)
                                .SetProperty(item => item.Payload, Serialize(next)), cancellationToken);
                        if (changed != 1) continue;
                        if (before!.Status != AgentTurnStatus.Prepared && before.Conversation is { } admission)
                        {
                            var conversation = await owned.Set<AgentConversationRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scope && item.Id == admission.ConversationId, cancellationToken);
                            var updated = AgentConversationRules.Change(before, next, conversation is null ? null : ReadConversation(conversation));
                            if (updated is null || conversation is null) throw AgentOperationRules.Conflict();
                            AgentConversationRules.Validate(updated);
                            var payload = JsonSerializer.Serialize(updated, Json);
                            var saved = await owned.Set<AgentConversationRecord>().Where(item => item.ScopeKey == scope && item.Id == admission.ConversationId && item.Version == conversation.Version)
                                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, conversation.Version + 1).SetProperty(item => item.Payload, payload), cancellationToken);
                            if (saved != 1) continue;
                        }
                    }
                    var gate = operation.Value.Row;
                    var cancellationPayload = OperationPayload(decision.Operation);
                    var committed = await owned.Set<AgentOperationRecord>().Where(item => item.ScopeKey == scope && item.OperationKey == key &&
                            item.SchemaVersion == AgentEfModule.SchemaVersion && item.Version == gate.Version)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Payload, cancellationPayload), cancellationToken);
                    if (committed != 1) continue;
                    await transaction.CommitAsync(cancellationToken);
                    return decision.Receipt;
                }
                catch (OperationGateRaceException) { /* Confirmed competing insert; reopen its gate in a fresh owned transaction. */ }
            }
            throw Failure(new InvalidOperationException("The bounded agent cancellation retries were exhausted."));
        }
        catch (OperationCanceledException) { throw; }
        catch (AgentJournalException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    private static string OperationPayload(AgentOperationDocument document)
    {
        AgentOperationRules.Validate(document);
        var payload = JsonSerializer.Serialize(document, Json);
        if (Utf8.GetByteCount(payload) > MaximumRecordBytes) throw new InvalidDataException("The operation gate exceeds the journal record bound.");
        return payload;
    }
}
