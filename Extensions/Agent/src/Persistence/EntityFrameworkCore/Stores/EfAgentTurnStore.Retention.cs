using System.Globalization;
using System.Text.Json;
using Elsa.Agent.Core.Exceptions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfAgentTurnStore
{
    public async ValueTask<AgentRetentionPage> QueryRetentionAsync(AgentRetentionQuery query, CancellationToken cancellationToken)
    {
        try
        {
            AgentRetentionRules.Query(query);
            var scope = Scope(query.TenantId, query.PersistencePartition);
            var rows = context.Set<AgentTurnRecord>().AsNoTracking().Where(row => row.ScopeKey == scope &&
                (query.AfterHandle == null || string.Compare(row.Handle, query.AfterHandle) > 0)).OrderBy(row => row.Handle).Take(query.PageSize + 1);
            var items = new List<AgentRetentionCandidate>();
            var scanned = 0;
            string? last = null;
            await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                if (++scanned > query.PageSize) return new(items, last);
                last = row.Handle;
                if (row.SchemaVersion != AgentEfModule.SchemaVersion || row.Version < 0 || row.Payload.Length > MaximumRecordBytes || Utf8.GetByteCount(row.Payload) > MaximumRecordBytes)
                    throw new InvalidDataException("The agent journal retention record schema or bounds are unsupported.");
                // Streaming one row at a time avoids a page of large ciphertexts. Unknown properties,
                // including protected request/result, are skipped by this private metadata reader.
                var header = JsonSerializer.Deserialize<RetentionHeader>(row.Payload, Json) ?? throw new InvalidDataException("The agent retention header is missing.");
                AgentOperationRules.Identity(header.Identity);
                if (header.Handle != row.Handle || AgentTurnDocument.CalculateOperationKey(header.Identity) != row.OperationKey ||
                    header.Identity.TenantId != query.TenantId || header.Identity.PersistencePartition != query.PersistencePartition ||
                    (int)header.Status != row.Status || header.Deadline.UtcTicks != row.DeadlineUtcTicks || !Enum.IsDefined(header.Status))
                    throw new InvalidDataException("The agent retention header does not match its selected record.");
                if (AgentRetentionRules.Settled(header.Status) && header.ContentPurgedAt is null)
                    items.Add(new(header.Handle, header.Identity, header.Status, header.Deadline, header.DeliveryProtocol,
                        header.Deliveries, header.Applications, row.Version.ToString(CultureInfo.InvariantCulture)) { Conversation = header.Conversation, AdmissionGeneration = header.AdmissionGeneration });
            }
            return new(items, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (AgentJournalException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    public async ValueTask<bool> PurgeContentAsync(AgentRetentionCandidate expected, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            var scope = Scope(expected.Identity);
            AgentTurnDocumentRules.Token(expected.Handle);
            await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
            var owned = transaction.Context<DbContext>();
            var row = await owned.Set<AgentTurnRecord>().AsNoTracking().SingleOrDefaultAsync(value => value.ScopeKey == scope && value.Handle == expected.Handle, cancellationToken);
            if (row is null) return false;
            var before = Read(row);
            if (before.ContentPurgedAt is not null || !AgentRetentionRules.Settled(before.Status) || before.Deadline >= now ||
                !AgentRetentionRules.Matches(before, expected, row.Version.ToString(CultureInfo.InvariantCulture))) return false;
            if (before.Conversation is { } admission)
            {
                var conversationRow = await owned.Set<AgentConversationRecord>().AsNoTracking().SingleOrDefaultAsync(value => value.ScopeKey == scope && value.Id == admission.ConversationId, cancellationToken);
                if (conversationRow is null)
                {
                    if (!AgentRetentionRules.CanLackConversation(expected)) return false;
                }
                else
                {
                    var conversation = ReadConversation(conversationRow);
                    if (conversation.ActiveTurnHandle is not null || conversation.OutcomeUnknown) return false;
                    // Compete with claim/accept/fail against the same conversation version. Preserve
                    // history exactly; a concurrent owner must invalidate the purge proof atomically.
                    var locked = await owned.Set<AgentConversationRecord>().Where(value => value.ScopeKey == scope && value.Id == conversationRow.Id &&
                            value.SchemaVersion == AgentEfModule.SchemaVersion && value.Version == conversationRow.Version)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Version, conversationRow.Version + 1), cancellationToken);
                    if (locked != 1) return false;
                }
            }
            var next = AgentRetentionRules.Purge(before, now);
            var payload = Serialize(next);
            var changed = await owned.Set<AgentTurnRecord>().Where(value => value.ScopeKey == scope && value.Handle == row.Handle &&
                    value.SchemaVersion == AgentEfModule.SchemaVersion && value.Version == row.Version)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Version, row.Version + 1).SetProperty(value => value.Payload, payload), cancellationToken);
            if (changed != 1) return false;
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (AgentJournalException) { throw; }
        catch (Exception exception) { throw Failure(exception, expected.Handle); }
    }

    private sealed record RetentionHeader(string Handle, AgentInvocationIdentity Identity, AgentTurnStatus Status, DateTimeOffset Deadline,
        int DeliveryProtocol, IReadOnlyList<AgentTurnDelivery>? Deliveries, IReadOnlyList<AgentTurnApplication>? Applications, DateTimeOffset? ContentPurgedAt, AgentConversationAdmission? Conversation, int AdmissionGeneration);
}
