using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Preserves actual owner inputs on their logical checkpoint before capture or coalescing.</summary>
internal sealed class DefaultRuntimeHistorySourceExtension(IWorkflowExecutionStateStore executions) : IRuntimeHistorySourceExtension
{
    public async ValueTask<RuntimeCheckpointCommit> AddFactsAsync(RuntimeCheckpointCommit commit,
        Func<RuntimeHistoryCapturePolicy, IEnumerable<RuntimeHistorySourceFact>> facts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(facts);
        cancellationToken.ThrowIfCancellationRequested();
        WorkflowExecutionState? native;
        try { native = await executions.FindAsync(commit.WorkflowExecutionId, cancellationToken); }
        catch (Exception exception) when (RuntimeHistoryReadFailures.IsMalformed(exception))
        {
            throw new RuntimeHistoryIntegrityException(commit.WorkflowExecutionId,
                "the semantic owner could not interpret its admitted policy", exception);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && RuntimeHistoryReadFailures.IsUnavailable(exception))
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                "The semantic owner could not read its admitted native policy.", exception);
        }
        // An existing native choice, including legacy absence, wins over a proposed new state.
        var policy = native is not null ? native.HistoryCapturePolicy : commit.StateChanges.WorkflowExecution?.State.HistoryCapturePolicy;
        if (!RuntimeHistorySourceFacts.IsFinalProfile(policy)) return commit;

        var combined = new List<RuntimeHistorySourceFact>(commit.HistorySourceFacts ?? []);
        var bySubject = new Dictionary<(RuntimeHistorySourceFactKind Kind, string Subject), (RuntimeHistorySourceFact First, int Count)>();
        foreach (var prior in combined)
        {
            var key = (prior.Kind, prior.SubjectId);
            bySubject[key] = bySubject.TryGetValue(key, out var seen) ? (seen.First, checked(seen.Count + 1)) : (prior, 1);
        }
        foreach (var window in facts(policy!).Chunk(256))
        foreach (var fact in window)
        {
            var key = (fact.Kind, fact.SubjectId);
            if (!bySubject.TryGetValue(key, out var prior))
            {
                combined.Add(fact);
                bySubject.Add(key, (fact, 1));
            }
            else if (prior.Count != 1 || !prior.First.IsEquivalentTo(fact))
                throw new InvalidOperationException("One checkpoint owner supplied conflicting or ambiguous semantic facts.");
        }
        // Untouched prior inputs retain their exact order/content, including kinds omitted by a pinned vocabulary.
        return commit with { HistorySourceFacts = RuntimeHistorySourceFacts.ForPolicy(policy, combined) };
    }
}
