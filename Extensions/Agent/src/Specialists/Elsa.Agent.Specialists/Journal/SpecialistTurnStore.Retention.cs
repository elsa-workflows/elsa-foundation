using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Models;

namespace Elsa.Agent.Specialists.Journal;

internal sealed partial class InMemorySpecialistTurnStore
{
    public ValueTask<SpecialistRetentionPage> QueryRetentionAsync(SpecialistRetentionQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            SpecialistRetentionRules.Query(query);
            EnsureScope(query.TenantId, query.PersistencePartition);
            lock (gate)
            {
                var scanned = rows.Values.Where(row => row.Identity.TenantId == query.TenantId && row.Identity.PersistencePartition == query.PersistencePartition &&
                    (query.AfterHandle is null || StringComparer.Ordinal.Compare(row.Handle, query.AfterHandle) > 0))
                    .OrderBy(row => row.Handle, StringComparer.Ordinal).Take(query.PageSize + 1).ToArray();
                var page = scanned.Take(query.PageSize).ToArray();
                var items = page.Where(row => SpecialistRetentionRules.Settled(row.Status) && row.ContentPurgedAt is null)
                    .Select(row => { SpecialistTurnDocumentRules.Validate(row); return SpecialistRetentionRules.Candidate(row, SpecialistRetentionRules.Snapshot(row)); }).ToArray();
                return ValueTask.FromResult(new SpecialistRetentionPage(items, scanned.Length > query.PageSize ? page[^1].Handle : null));
            }
        }
        catch (SpecialistJournalException) { throw; }
        catch (Exception exception) { throw new SpecialistJournalException("specialist.journal.store", null, "The specialist retention scan could not be confirmed.", exception); }
    }

    public ValueTask<bool> PurgeContentAsync(SpecialistRetentionCandidate expected, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            EnsureScope(expected.Identity);
            lock (gate)
            {
                var current = Find(expected.Handle, expected.Identity);
                if (current is null || current.ContentPurgedAt is not null || !SpecialistRetentionRules.Settled(current.Status) || current.Deadline >= now ||
                    !SpecialistRetentionRules.Matches(current, expected, SpecialistRetentionRules.Snapshot(current))) return ValueTask.FromResult(false);
                if (current.Conversation is { } admission && (!state.Conversations.TryGetValue((current.Identity.TenantId, current.Identity.PersistencePartition, admission.ConversationId), out var conversation) ||
                    conversation.ActiveTurnHandle is not null || conversation.OutcomeUnknown)) return ValueTask.FromResult(false);
                rows[current.Handle] = SpecialistRetentionRules.Purge(current, now);
                return ValueTask.FromResult(true);
            }
        }
        catch (SpecialistJournalException) { throw; }
        catch (Exception exception) { throw new SpecialistJournalException("specialist.journal.store", expected.Handle, "The specialist content purge could not be confirmed.", exception); }
    }
}
