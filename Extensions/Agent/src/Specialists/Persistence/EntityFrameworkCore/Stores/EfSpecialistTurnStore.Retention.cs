using System.Globalization;
using System.Text.Json;
using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfSpecialistTurnStore
{
    public async ValueTask<SpecialistRetentionPage> QueryRetentionAsync(SpecialistRetentionQuery query, CancellationToken cancellationToken)
    {
        try
        {
            SpecialistRetentionRules.Query(query);
            var scope = Scope(query.TenantId, query.PersistencePartition);
            var rows = context.Set<SpecialistTurnRecord>().AsNoTracking().Where(row => row.ScopeKey == scope &&
                (query.AfterHandle == null || string.Compare(row.Handle, query.AfterHandle) > 0)).OrderBy(row => row.Handle).Take(query.PageSize + 1);
            var items = new List<SpecialistRetentionCandidate>();
            var scanned = 0;
            string? last = null;
            await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                if (++scanned > query.PageSize) return new(items, last);
                last = row.Handle;
                if (row.SchemaVersion != SpecialistsEfModule.SchemaVersion || row.Version < 0 || row.Payload.Length > MaximumRecordBytes || Utf8.GetByteCount(row.Payload) > MaximumRecordBytes)
                    throw new InvalidDataException("The specialist journal retention record schema or bounds are unsupported.");
                // Streaming one row at a time avoids a page of large ciphertexts. Unknown properties,
                // including protected request/result, are skipped by this private metadata reader.
                var header = JsonSerializer.Deserialize<RetentionHeader>(row.Payload, Json) ?? throw new InvalidDataException("The specialist retention header is missing.");
                SpecialistOperationRules.Identity(header.Identity);
                if (header.Handle != row.Handle || SpecialistTurnDocument.CalculateOperationKey(header.Identity) != row.OperationKey ||
                    header.Identity.TenantId != query.TenantId || header.Identity.PersistencePartition != query.PersistencePartition ||
                    (int)header.Status != row.Status || header.Deadline.UtcTicks != row.DeadlineUtcTicks || !Enum.IsDefined(header.Status))
                    throw new InvalidDataException("The specialist retention header does not match its selected record.");
                if (SpecialistRetentionRules.Settled(header.Status) && header.ContentPurgedAt is null)
                    items.Add(new(header.Handle, header.Identity, header.Status, header.Deadline, header.DeliveryProtocol,
                        header.Deliveries, header.Applications, row.Version.ToString(CultureInfo.InvariantCulture)) { Conversation = header.Conversation, AdmissionGeneration = header.AdmissionGeneration });
            }
            return new(items, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (SpecialistJournalException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    public async ValueTask<bool> PurgeContentAsync(SpecialistRetentionCandidate expected, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            var scope = Scope(expected.Identity);
            SpecialistTurnDocumentRules.Token(expected.Handle);
            await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
            var owned = transaction.Context<DbContext>();
            var row = await owned.Set<SpecialistTurnRecord>().AsNoTracking().SingleOrDefaultAsync(value => value.ScopeKey == scope && value.Handle == expected.Handle, cancellationToken);
            if (row is null) return false;
            var before = Read(row);
            if (before.ContentPurgedAt is not null || !SpecialistRetentionRules.Settled(before.Status) || before.Deadline >= now ||
                !SpecialistRetentionRules.Matches(before, expected, row.Version.ToString(CultureInfo.InvariantCulture))) return false;
            if (before.Conversation is { } admission)
            {
                var conversationRow = await owned.Set<SpecialistConversationRecord>().AsNoTracking().SingleOrDefaultAsync(value => value.ScopeKey == scope && value.Id == admission.ConversationId, cancellationToken);
                if (conversationRow is null)
                {
                    if (!SpecialistRetentionRules.CanLackConversation(expected)) return false;
                }
                else
                {
                    var conversation = ReadConversation(conversationRow);
                    if (conversation.ActiveTurnHandle is not null || conversation.OutcomeUnknown) return false;
                    // Compete with claim/accept/fail against the same conversation version. Preserve
                    // history exactly; a concurrent owner must invalidate the purge proof atomically.
                    var locked = await owned.Set<SpecialistConversationRecord>().Where(value => value.ScopeKey == scope && value.Id == conversationRow.Id &&
                            value.SchemaVersion == SpecialistsEfModule.SchemaVersion && value.Version == conversationRow.Version)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Version, conversationRow.Version + 1), cancellationToken);
                    if (locked != 1) return false;
                }
            }
            var next = SpecialistRetentionRules.Purge(before, now);
            var payload = Serialize(next);
            var changed = await owned.Set<SpecialistTurnRecord>().Where(value => value.ScopeKey == scope && value.Handle == row.Handle &&
                    value.SchemaVersion == SpecialistsEfModule.SchemaVersion && value.Version == row.Version)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Version, row.Version + 1).SetProperty(value => value.Payload, payload), cancellationToken);
            if (changed != 1) return false;
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (SpecialistJournalException) { throw; }
        catch (Exception exception) { throw Failure(exception, expected.Handle); }
    }

    private sealed record RetentionHeader(string Handle, SpecialistInvocationIdentity Identity, SpecialistTurnStatus Status, DateTimeOffset Deadline,
        int DeliveryProtocol, IReadOnlyList<SpecialistTurnDelivery>? Deliveries, IReadOnlyList<SpecialistTurnApplication>? Applications, DateTimeOffset? ContentPurgedAt, SpecialistConversationAdmission? Conversation, int AdmissionGeneration);
}
