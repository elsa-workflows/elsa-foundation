using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Recovery;

/// <summary>What a resumption re-drive of an execution could achieve in the current sweep (#2188).</summary>
internal enum Readiness
{
    /// <summary>
    /// Its drain could make progress, or it has nothing queued and the re-drive itself is the work (recovery).
    /// </summary>
    Ready,

    /// <summary>
    /// The pause gate would stop its drain at the next item: re-driving it would only queue another trigger.
    /// </summary>
    Held,

    /// <summary>The pause gate could not be consulted; its drain would consult the same gate.</summary>
    CheckFailed,

    /// <summary>The caller is backing it off this sweep.</summary>
    Excluded
}

/// <summary>
/// Decides, once per execution per resumption sweep, whether re-driving it could make progress (#2188). Next items are
/// read in batches (one request per page of executions), and the drainer's own pause gate is asked only when the sweep
/// is about to use an execution. Failures are counted for one warning per sweep.
/// </summary>
internal sealed class PauseCheck(IWorkflowSchedulerWorkQueue queue, IWorkflowSchedulerPauseGate gate, IReadOnlySet<string> excluded)
{
    private readonly Dictionary<string, RuntimeSchedulerWorkItem?> _nextItems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Readiness> _readiness = new(StringComparer.Ordinal);

    public int Failures { get; private set; }

    public (string WorkflowExecutionId, Exception Exception)? FirstFailure { get; private set; }

    public async ValueTask ReadHeadsAsync(IEnumerable<string> workflowExecutionIds, CancellationToken cancellationToken)
    {
        var unread = workflowExecutionIds
            .Where(id => !excluded.Contains(id) && !_nextItems.ContainsKey(id) && !_readiness.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unread.Length == 0)
            return;

        try
        {
            Remember(unread, await queue.ListNextWorkItemsAsync(unread, cancellationToken));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // One unreadable row must not cost the rest of the page their check: read them one by one instead.
            foreach (var workflowExecutionId in unread)
            {
                try
                {
                    Remember([workflowExecutionId], await queue.ListNextWorkItemsAsync([workflowExecutionId], cancellationToken));
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Fail(workflowExecutionId, exception);
                }
            }
        }
    }

    public async ValueTask<Readiness> ClassifyAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        if (_readiness.TryGetValue(workflowExecutionId, out var known))
            return known;
        if (excluded.Contains(workflowExecutionId))
            return _readiness[workflowExecutionId] = Readiness.Excluded;
        if (!_nextItems.ContainsKey(workflowExecutionId))
        {
            await ReadHeadsAsync([workflowExecutionId], cancellationToken);
            if (_readiness.TryGetValue(workflowExecutionId, out known))
                return known;
        }

        try
        {
            var held = _nextItems[workflowExecutionId] is { } next &&
                       await gate.EvaluateAsync(next, cancellationToken) is { CanAdvance: false };
            return _readiness[workflowExecutionId] = held ? Readiness.Held : Readiness.Ready;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(workflowExecutionId, exception);
        }
    }

    private void Remember(IEnumerable<string> workflowExecutionIds, IReadOnlyDictionary<string, RuntimeSchedulerWorkItem> nextItems)
    {
        foreach (var workflowExecutionId in workflowExecutionIds)
            _nextItems[workflowExecutionId] = nextItems.GetValueOrDefault(workflowExecutionId);
    }

    private Readiness Fail(string workflowExecutionId, Exception exception)
    {
        Failures++;
        FirstFailure ??= (workflowExecutionId, exception);
        return _readiness[workflowExecutionId] = Readiness.CheckFailed;
    }
}
