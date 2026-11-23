using Elsa.Agent.Core.Exceptions;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Journal;

internal sealed partial class InMemoryAgentTurnStore
{
    public ValueTask<AgentRetentionPage> QueryRetentionAsync(AgentRetentionQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            AgentRetentionRules.Query(query);
            EnsureScope(query.TenantId, query.PersistencePartition);
            lock (gate)
            {
                var scanned = rows.Values.Where(row => row.Identity.TenantId == query.TenantId && row.Identity.PersistencePartition == query.PersistencePartition &&
                    (query.AfterHandle is null || StringComparer.Ordinal.Compare(row.Handle, query.AfterHandle) > 0))
                    .OrderBy(row => row.Handle, StringComparer.Ordinal).Take(query.PageSize + 1).ToArray();
                var page = scanned.Take(query.PageSize).ToArray();
                var items = page.Where(row => AgentRetentionRules.Settled(row.Status) && row.ContentPurgedAt is null)
                    .Select(row => { AgentTurnDocumentRules.Validate(row); return AgentRetentionRules.Candidate(row, AgentRetentionRules.Snapshot(row)); }).ToArray();
                return ValueTask.FromResult(new AgentRetentionPage(items, scanned.Length > query.PageSize ? page[^1].Handle : null));
            }
        }
        catch (AgentJournalException) { throw; }
        catch (Exception exception) { throw new AgentJournalException("agent.journal.store", null, "The agent retention scan could not be confirmed.", exception); }
    }

    public ValueTask<bool> PurgeContentAsync(AgentRetentionCandidate expected, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            EnsureScope(expected.Identity);
            lock (gate)
            {
                var current = Find(expected.Handle, expected.Identity);
                if (current is null || current.ContentPurgedAt is not null || !AgentRetentionRules.Settled(current.Status) || current.Deadline >= now ||
                    !AgentRetentionRules.Matches(current, expected, AgentRetentionRules.Snapshot(current))) return ValueTask.FromResult(false);
                if (current.Conversation is { } admission)
                {
                    state.Conversations.TryGetValue((current.Identity.TenantId, current.Identity.PersistencePartition, admission.ConversationId), out var conversation);
                    if (conversation is null ? !AgentRetentionRules.CanLackConversation(expected) : conversation.ActiveTurnHandle is not null || conversation.OutcomeUnknown)
                        return ValueTask.FromResult(false);
                }
                rows[current.Handle] = AgentRetentionRules.Purge(current, now);
                return ValueTask.FromResult(true);
            }
        }
        catch (AgentJournalException) { throw; }
        catch (Exception exception) { throw new AgentJournalException("agent.journal.store", expected.Handle, "The agent content purge could not be confirmed.", exception); }
    }
}
