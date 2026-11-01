using System.Text.Json;
using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfSpecialistTurnStore
{
    // Only confirmed gate insert/CAS loss can retry. A commit exception is never caught here.
    private sealed class OperationGateRaceException(Exception cause) : Exception("A competing operation gate was committed.", cause);

    private async ValueTask<(SpecialistOperationRecord Row, SpecialistOperationDocument Document)?> LockOperationAsync(
        DbContext owned, string scope, SpecialistInvocationIdentity identity, CancellationToken cancellationToken)
    {
        SpecialistOperationRules.Identity(identity);
        var key = SpecialistTurnDocument.CalculateOperationKey(identity);
        var row = await owned.Set<SpecialistOperationRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scope && item.OperationKey == key, cancellationToken);
        if (row is null)
        {
            var document = new SpecialistOperationDocument(identity, []);
            row = new() { ScopeKey = scope, OperationKey = key, SchemaVersion = SpecialistsEfModule.SchemaVersion, Payload = OperationPayload(document) };
            owned.Set<SpecialistOperationRecord>().Add(row);
            try { await owned.SaveChangesAsync(cancellationToken); }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
            { throw new OperationGateRaceException(exception); }
            return (row, document);
        }
        if (row.SchemaVersion != SpecialistsEfModule.SchemaVersion || row.Version < 0 || row.Payload.Length > MaximumRecordBytes)
            throw new InvalidDataException("The specialist operation gate schema or bounds are unsupported.");
        var operation = JsonSerializer.Deserialize<SpecialistOperationDocument>(row.Payload, Json) ?? throw new InvalidDataException("The specialist operation gate is missing.");
        SpecialistOperationRules.Match(operation, identity);
        if (SpecialistTurnDocument.CalculateOperationKey(operation.Identity) != key) throw new InvalidDataException("The operation gate belongs to another native operation.");
        var nextVersion = checked(row.Version + 1);
        var changed = await owned.Set<SpecialistOperationRecord>().Where(item => item.ScopeKey == scope && item.OperationKey == key &&
                item.SchemaVersion == SpecialistsEfModule.SchemaVersion && item.Version == row.Version)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, nextVersion), cancellationToken);
        if (changed != 1) return null;
        row.Version = nextVersion;
        return (row, operation);
    }

    public async ValueTask<SpecialistCancellationReceipt> CancelAsync(SpecialistTurnCancellation cancellation, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(cancellation);
            SpecialistOperationRules.Cancellation(cancellation);
            var scope = Scope(cancellation.Identity);
            var key = SpecialistTurnDocument.CalculateOperationKey(cancellation.Identity);
            for (var attempt = 0; attempt < 32; attempt++)
            {
                try
                {
                    await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
                    var owned = transaction.Context<DbContext>();
                    var operation = await LockOperationAsync(owned, scope, cancellation.Identity, cancellationToken);
                    if (operation is null) continue;
                    var row = await owned.Set<SpecialistTurnRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scope && item.OperationKey == key, cancellationToken);
                    var before = row is null ? null : Read(row);
                    var decision = SpecialistOperationRules.Cancel(operation.Value.Document, cancellation, before);
                    if (!ReferenceEquals(before, decision.Turn) && decision.Turn is { } next)
                    {
                        SpecialistTurnDocumentRules.Validate(next);
                        var changed = await owned.Set<SpecialistTurnRecord>().Where(item => item.ScopeKey == scope && item.Handle == row!.Handle &&
                                item.SchemaVersion == SpecialistsEfModule.SchemaVersion && item.Version == row.Version && item.Status == row.Status && item.Fence == row.Fence)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, row!.Version + 1).SetProperty(item => item.Status, (int)next.Status)
                                .SetProperty(item => item.Payload, Serialize(next)), cancellationToken);
                        if (changed != 1) continue;
                        if (before!.Status != SpecialistTurnStatus.Prepared && before.Conversation is { } admission)
                        {
                            var conversation = await owned.Set<SpecialistConversationRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scope && item.Id == admission.ConversationId, cancellationToken);
                            var updated = SpecialistConversationRules.Change(before, next, conversation is null ? null : ReadConversation(conversation));
                            if (updated is null || conversation is null) throw SpecialistOperationRules.Conflict();
                            SpecialistConversationRules.Validate(updated);
                            var payload = JsonSerializer.Serialize(updated, Json);
                            var saved = await owned.Set<SpecialistConversationRecord>().Where(item => item.ScopeKey == scope && item.Id == admission.ConversationId && item.Version == conversation.Version)
                                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, conversation.Version + 1).SetProperty(item => item.Payload, payload), cancellationToken);
                            if (saved != 1) continue;
                        }
                    }
                    var gate = operation.Value.Row;
                    var cancellationPayload = OperationPayload(decision.Operation);
                    var committed = await owned.Set<SpecialistOperationRecord>().Where(item => item.ScopeKey == scope && item.OperationKey == key &&
                            item.SchemaVersion == SpecialistsEfModule.SchemaVersion && item.Version == gate.Version)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Payload, cancellationPayload), cancellationToken);
                    if (committed != 1) continue;
                    await transaction.CommitAsync(cancellationToken);
                    return decision.Receipt;
                }
                catch (OperationGateRaceException) { /* Confirmed competing insert; reopen its gate in a fresh owned transaction. */ }
            }
            throw Failure(new InvalidOperationException("The bounded specialist cancellation retries were exhausted."));
        }
        catch (OperationCanceledException) { throw; }
        catch (SpecialistJournalException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    private static string OperationPayload(SpecialistOperationDocument document)
    {
        SpecialistOperationRules.Validate(document);
        var payload = JsonSerializer.Serialize(document, Json);
        if (Utf8.GetByteCount(payload) > MaximumRecordBytes) throw new InvalidDataException("The operation gate exceeds the journal record bound.");
        return payload;
    }
}
