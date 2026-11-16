using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Pure ordered projection after the caller has validated exact immutable source authority.</summary>
internal static class ChildControlInboxEvaluation
{
    internal sealed record Decision(ActivityExecutionState CallingChild,
        IReadOnlyList<RuntimeChildControlTransport> Ready, bool AlreadyAccepted);

    internal static Decision Accept(ActivityExecutionState child, RuntimeChildControlTransport incoming)
    {
        if (child.InvocationId != incoming.CallingActivityExecutionId ||
            child.Execution.WorkflowExecutionId != incoming.ParentWorkflowExecutionId ||
            child.ParentActivityExecutionId != incoming.ConsumerActivityExecutionId)
            throw new InvalidOperationException("Child control does not belong to this exact local call invocation.");
        var target = new RuntimeChildControlTarget(incoming.ParentWorkflowExecutionId, incoming.CallingActivityExecutionId,
            incoming.ConsumerActivityExecutionId, incoming.ParentExecutable, incoming.Batch.RootActivityExecutionId,
            incoming.Batch.ConsumerKey, incoming.Batch.ConsumerVersion);
        var current = child.ChildControlInbox ?? new RuntimeChildControlInbox(1, target, 0, false);
        if (!current.Target.MatchesAddress(incoming))
            throw new InvalidOperationException("A call's inbox cannot change its retained stream identity.");
        var ordinal = incoming.Batch.Ordinal;
        if (ordinal < current.NextOrdinal || current.IsTerminalConsumed && current.NextOrdinal == long.MaxValue && ordinal == long.MaxValue)
            return new Decision(child, [], true);
        if (current.IsTerminalConsumed)
            throw new InvalidOperationException("Control cannot advance beyond its consumed terminal barrier.");

        var pending = current.Pending.ToDictionary(item => item.Batch.Ordinal);
        var alreadyPending = pending.TryGetValue(ordinal, out var existing);
        if (alreadyPending && !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(existing), JsonSerializer.SerializeToElement(incoming)))
            throw new InvalidOperationException("A pending ordinal cannot change its immutable source or payload.");
        pending[ordinal] = incoming;
        // Validate terminal placement before releasing anything; an earlier terminal cannot discard already accepted later data.
        _ = new RuntimeChildControlInbox(1, target, current.NextOrdinal, false, pending.Values.ToArray());

        var ready = new List<RuntimeChildControlTransport>();
        var next = current.NextOrdinal;
        var terminalConsumed = false;
        while (pending.Remove(next, out var item))
        {
            ready.Add(item);
            if (item.Batch.IsTerminal)
            {
                terminalConsumed = true;
                if (next != long.MaxValue) next++;
                break;
            }
            if (next == long.MaxValue)
                throw new InvalidOperationException("A nonterminal control batch exhausts the inbox ordinal space.");
            next++;
        }
        if (alreadyPending && ready.Count == 0)
            return new Decision(child, [], true);
        var projected = new RuntimeChildControlInbox(1, target, next, terminalConsumed, pending.Values.ToArray());
        return new Decision(child with { ChildControlInbox = projected }, ready.AsReadOnly(), false);
    }

}
