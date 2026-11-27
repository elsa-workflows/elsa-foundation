using Bpmn.Model.State;
using Bpmn.Semantics;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Resolves semantic work against exact committed direct-child provenance, including terminal rows.</summary>
internal sealed class BpmnWorkLedger(IActivityExecutionStateStore store)
{
    public async ValueTask<IReadOnlyList<ActivityExecutionState>> ReadAsync(IRuntimeActivityExecutionContext context)
    {
        var children = new List<ActivityExecutionState>();
        string? next = null;
        do
        {
            var page = await store.ListByParentPageAsync(new ActivityExecutionStateParentPageQuery(
                context.WorkflowExecutionId, context.ActivityExecutionState.InvocationId, continuationToken: next), context.CancellationToken);
            children.AddRange(page.Items);
            next = page.NextContinuationToken;
        } while (next is not null);
        return children;
    }

    public static ActivityExecutionState Child(
        IReadOnlyList<ActivityExecutionState> children, BpmnExecutionState state, string owner, string handle, string binding, string? iteration)
    {
        var matches = children.Where(candidate => candidate.InvocationId == handle).ToArray();
        if (matches.Length != 1)
            throw new BpmnHostException($"BPMN child '{handle}' has no unique durable direct-child identity.");
        var child = matches[0];
        if (child.ParentActivityExecutionId != owner || child.Provenance.ParentActivityExecutionId != owner ||
            child.Execution.ExecutableNodeId != binding || child.IterationId != iteration || child.Provenance.IterationId != iteration)
            throw new BpmnHostException($"BPMN child '{handle}' does not match its parent, binding and iteration.");
        var token = TokenId(child);
        ValidateWorkHistory(children, child, token);
        var work = state.ActiveWork.Where(candidate => candidate.TokenId == token).ToArray();
        if (work.Length > 1 || work.Length == 1 &&
            (work[0].NodeId != binding || work[0].IterationId != iteration ||
             work[0].ElementId != child.Provenance.Metadata.GetValueOrDefault("bpmn.elementId")))
            throw new BpmnHostException($"BPMN child '{handle}' does not match its active work record.");
        var stopped = state.Tokens.Where(candidate => candidate.Status == BpmnTokenStatus.Canceled && candidate.StoppedHandle == handle).ToArray();
        if (stopped.Length > 1 || stopped.Length == 1 &&
            (stopped[0].TokenId != token || stopped[0].AtElementId != child.Provenance.Metadata.GetValueOrDefault("bpmn.elementId")))
            throw new BpmnHostException($"BPMN child '{handle}' does not match its retired work record.");
        return child;
    }

    private static void ValidateWorkHistory(IReadOnlyList<ActivityExecutionState> children, ActivityExecutionState child, string token)
    {
        var history = children.Where(candidate => candidate.Provenance.ExecutionPathId == token).ToArray();
        // Operator successors retain the semantic work token. Their mutually linked supersession
        // chain is history, not a competing invocation; unrelated current/terminal claims are ambiguous.
        if (history.Count(candidate => candidate.Status != ActivityExecutionStatus.Superseded) != 1 ||
            child.Status == ActivityExecutionStatus.Superseded || history.Select(candidate => candidate.InvocationId).Distinct().Count() != history.Length)
            throw new BpmnHostException($"BPMN child '{child.InvocationId}' has ambiguous durable work-token identity '{token}'.");
        foreach (var source in history.Where(candidate => candidate.InvocationId != child.InvocationId))
        {
            var current = source;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current.InvocationId != child.InvocationId)
            {
                var next = history.SingleOrDefault(candidate => candidate.InvocationId == current.SupersededByActivityExecutionId);
                if (!visited.Add(current.InvocationId) || current.Status != ActivityExecutionStatus.Superseded || current.SupersededAt is null ||
                    next is null || next.Metadata.GetValueOrDefault(RuntimeMetadataKeys.SupersedesActivityExecutionId) != current.InvocationId ||
                    current.ParentActivityExecutionId != child.ParentActivityExecutionId ||
                    current.Provenance.ParentActivityExecutionId != child.Provenance.ParentActivityExecutionId ||
                    current.Execution.ExecutableNodeId != child.Execution.ExecutableNodeId || current.IterationId != child.IterationId ||
                    current.Provenance.IterationId != child.Provenance.IterationId ||
                    current.Provenance.Metadata.GetValueOrDefault("bpmn.elementId") != child.Provenance.Metadata.GetValueOrDefault("bpmn.elementId"))
                    throw new BpmnHostException($"BPMN child '{child.InvocationId}' has incompatible work-token supersession history '{token}'.");
                current = next;
            }
        }
    }

    public static string TokenId(ActivityExecutionState child) =>
        !string.IsNullOrWhiteSpace(child.Provenance.ExecutionPathId)
            ? child.Provenance.ExecutionPathId
            : throw new BpmnHostException($"BPMN child '{child.InvocationId}' has no durable work-token identity.");

    public static BpmnStateDocument RecoverLegacy(
        BpmnStateDocument document, IReadOnlyList<ActivityExecutionState> children, string owner, global::Bpmn.Semantics.BpmnGraph graph)
    {
        if (document.Version != 1)
            return document;

        var tokens = document.State.Tokens.Select(token =>
        {
            if (token.Status != BpmnTokenStatus.Canceled || token.StoppedHandle is not null)
                return token;
            var matches = children.Where(child => child.Provenance.ExecutionPathId == token.TokenId).ToArray();
            var element = graph.Elements.SingleOrDefault(entry => entry.ElementId == token.AtElementId)
                ?? throw new BpmnHostException($"Legacy BPMN owner '{owner}', token '{token.TokenId}' refers to missing element '{token.AtElementId}'.");
            if (matches.Length == 0)
            {
                // A bound MI coordinator owns instance tokens, not a host invocation of its own.
                if (element.LoopCharacteristics is not null &&
                    document.State.Tokens.Any(instance => instance.ParentTokenId == token.TokenId && instance.AtElementId == token.AtElementId))
                    return token;
                if (element.BindingRef is null && element.ListenerBindingRef is null)
                    return token;
                var history = document.State.Diagnostics.Where(entry => entry.TokenId == token.TokenId).ToArray();
                // In the known v1 writer every ordinary token emits before it can schedule work. The
                // capped FIFO log still containing that emission is a complete lifetime witness; a
                // missing/aged-out witness cannot establish that the durable child never existed.
                if (history.Any(entry => entry.Kind == BpmnDiagnosticKind.TokenEmitted) &&
                    !history.Any(entry => entry.Kind is BpmnDiagnosticKind.Scheduled or BpmnDiagnosticKind.ScopeListenerArmed))
                    return token;
                throw new BpmnHostException($"Legacy BPMN token '{token.TokenId}' cannot recover an exact durable stopped-work identity.");
            }

            var current = matches.Where(child => child.Status != ActivityExecutionStatus.Superseded).ToArray();
            if (current.Length != 1)
                throw new BpmnHostException($"Legacy BPMN token '{token.TokenId}' has ambiguous child provenance.");
            var child = current[0];
            ValidateWorkHistory(children, child, token.TokenId);
            if (child.ParentActivityExecutionId != owner || child.Provenance.ParentActivityExecutionId != owner ||
                child.IterationId != child.Provenance.IterationId ||
                child.Provenance.Metadata.GetValueOrDefault("bpmn.elementId") != token.AtElementId ||
                (element.BindingRef != child.Execution.ExecutableNodeId && element.ListenerBindingRef != child.Execution.ExecutableNodeId))
                throw new BpmnHostException($"Legacy BPMN token '{token.TokenId}' has incompatible child provenance.");
            // Version 1 represented logical cancellation by retaining the token. Its exact handle now
            // represents that retired invocation; a still-live child is also reconciled by host teardown.
            return token with { StoppedHandle = child.InvocationId };
        }).ToArray();
        return document with { State = document.State with { Tokens = tokens } };
    }

    public static IReadOnlyList<BpmnLiveWork> Live(
        IRuntimeActivityExecutionContext context, BpmnExecutionState state, IReadOnlyList<ActivityExecutionState> children)
    {
        var owner = context.ActivityExecutionState.InvocationId;
        return context.GetLiveChildActivities().Where(live => live.Status != ActivityExecutionStatus.Superseded).Select(live =>
        {
            var child = Child(children, state, owner, live.ActivityExecutionId, live.ExecutableNodeId, live.IterationId);
            var token = TokenId(child);
            var work = state.ActiveWork.Where(candidate => candidate.TokenId == token).ToArray();
            if (work.Length > 1 || work.Length == 1 &&
                (work[0].NodeId != live.ExecutableNodeId || work[0].IterationId != live.IterationId ||
                 child.Provenance.Metadata.GetValueOrDefault("bpmn.elementId") != work[0].ElementId))
                throw new BpmnHostException($"BPMN child '{live.ActivityExecutionId}' does not match its active work record.");
            if (work.Length == 0 && !state.Terminated && state.PendingFault is null &&
                !state.Tokens.Any(candidate => candidate.Status == BpmnTokenStatus.Canceled && candidate.StoppedHandle == child.InvocationId))
                throw new BpmnHostException($"BPMN child '{live.ActivityExecutionId}' has no active or retired semantic invocation.");
            return new BpmnLiveWork(live.ExecutableNodeId, live.IterationId, live.ActivityExecutionId)
                { WorkTokenId = work.Length == 1 ? token : null };
        }).ToArray();
    }
}
