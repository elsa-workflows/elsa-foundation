using Bpmn.Model;
using Bpmn.Semantics;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Translates Foundation deliveries and commands; the released interpreter owns all BPMN routing.</summary>
internal sealed class BpmnProcessRuntime(BpmnStateCodec codec, BpmnWorkLedger ledger) : IBpmnProcessRuntime
{
    private static readonly BpmnInterpreter Interpreter = BpmnInterpreter.CreateDefault();
    private const string BpmnErrorCategory = "bpmn.error";

    public async ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context)
    {
        var frame = await ReadAsync(context, starting: true);
        if (frame.Graph.Elements.Count == 0)
            return Finish(context, frame, new BpmnEvaluation(frame.Document.State, new BpmnContinuation.Complete("Done"), []));
        BpmnStartSignal? signal = null;
        if (context.TriggerNodeId == context.ExecutableNode.ExecutableNodeId)
        {
            if (context.TriggerMetadata?.GetValueOrDefault(BpmnStartTrigger.StartElementIdMetadataKey) is not { Length: > 0 } start)
                return RuntimeStructuralContinuation.Faulted(new ActivityFault("bpmn.start.unresolved-trigger",
                    "BPMN process was started by a trigger delivery but the matched trigger binding carried no start element id."));
            signal = new BpmnStartSignal(start);
        }
        return Finish(context, frame, Evaluate(() => Interpreter.Start(new BpmnStartRequest(frame.Graph,
            context.ActivityExecutionState.PrivateState is null ? null : frame.Document.State, Snapshot(context, frame), signal))));
    }

    public async ValueTask<RuntimeStructuralContinuation> CompleteAsync(IRuntimeActivityExecutionContext context, ActivityChildCompletedContext completion)
    {
        var frame = await ReadAsync(context);
        var child = BpmnWorkLedger.Child(frame.Children, frame.Document.State, Owner(context), completion.CompletedChildActivityExecutionId,
            completion.CompletedChildExecutableNodeId, completion.CompletedChildIterationId);
        var result = Evaluate(() => Interpreter.OnWorkCompleted(new BpmnWorkCompletedRequest(frame.Graph, frame.Document.State,
            Snapshot(context, frame, exclude: child.InvocationId), child.Execution.ExecutableNodeId, child.InvocationId,
            completion.OutcomeNames, child.IterationId) { WorkTokenId = BpmnWorkLedger.TokenId(child), ErrorCode = ErrorCode(child) }));
        return Finish(context, frame, result);
    }

    public async ValueTask<RuntimeStructuralContinuation> FaultAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext fault)
    {
        var frame = await ReadAsync(context);
        var child = BpmnWorkLedger.Child(frame.Children, frame.Document.State, Owner(context), fault.FaultedChildActivityExecutionId,
            fault.FaultedChildExecutableNodeId, fault.FaultedChildIterationId);
        var result = Fault(context, frame, child);
        return Finish(context, frame, result, fault.IncidentId);
    }

    public async ValueTask<RuntimeStructuralContinuation> SignalAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext signal)
    {
        var frame = await ReadAsync(context);
        var child = BpmnWorkLedger.Child(frame.Children, frame.Document.State, Owner(context), signal.NotifyingChildActivityExecutionId,
            signal.NotifyingChildExecutableNodeId, signal.NotifyingChildIterationId);
        // Delivery may follow the child's terminal checkpoint. Its pending terminal callback, rather than
        // the database status, determines when its semantic invocation stops being live.
        if (!frame.Document.State.Terminated && frame.Document.State.PendingFault is null &&
            !frame.Document.State.ActiveWork.Any(work => work.TokenId == BpmnWorkLedger.TokenId(child)))
            return RuntimeStructuralContinuation.Defer.WithState(codec.Write(frame.Document, frame.Document.State), frame.Document.Version);
        return Finish(context, frame, Signal(context, frame, child, signal));
    }

    public async ValueTask<RuntimeStructuralContinuation> OutcomeAsync(IRuntimeActivityExecutionContext context, ActivityChildOutcomeContext outcome)
    {
        var frame = await ReadAsync(context);
        var child = BpmnWorkLedger.Child(frame.Children, frame.Document.State, Owner(context), outcome.FaultedChildActivityExecutionId,
            outcome.FaultedChildExecutableNodeId, outcome.FaultedChildIterationId);
        var commands = new List<BpmnHostCommand>();
        BpmnContinuation.Fault? firstFault = null;
        var retired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var notification in outcome.Notifications)
        {
            if (retired.Contains(child.InvocationId) || firstFault is not null)
                continue;
            var evaluation = Signal(context, frame, child, notification, retired);
            commands.AddRange(evaluation.Commands);
            foreach (var cancellation in evaluation.Commands.OfType<BpmnHostCommand.CancelWorkSubtree>())
                retired.Add(cancellation.Handle);
            firstFault = evaluation.Continuation as BpmnContinuation.Fault;
            frame = frame with { Document = frame.Document with { State = evaluation.State } };
        }
        // Retain stopped handles until the original terminal fault has been evaluated. No intermediate prune.
        // A terminal semantic fault has already abandoned its work and need not retain a stopped
        // handle. It is the first verdict, so do not invent another callback into that abandoned state.
        // The original child fault and incident remain durable and unabsorbed.
        var final = firstFault is not null
            ? new BpmnEvaluation(frame.Document.State, firstFault, []) { ErrorDisposition = BpmnErrorDisposition.Propagated.Instance }
            : Fault(context, frame, child, retired);
        commands.AddRange(final.Commands);
        final = final with { Commands = commands };
        return Finish(context, frame, final, outcome.IncidentId);
    }

    private static BpmnEvaluation Signal(IRuntimeActivityExecutionContext context, Frame frame, ActivityExecutionState child,
        ActivityChildNotifiedContext signal, ISet<string>? retired = null) => Evaluate(() => Interpreter.OnWorkSignalled(
        new BpmnWorkSignalledRequest(frame.Graph, frame.Document.State, Snapshot(context, frame, reporting: child, retired: retired),
            child.Execution.ExecutableNodeId, child.InvocationId, signal.Code, signal.Payload, child.IterationId)
        { WorkTokenId = BpmnWorkLedger.TokenId(child) }));

    private static BpmnEvaluation Fault(IRuntimeActivityExecutionContext context, Frame frame, ActivityExecutionState child,
        ISet<string>? retired = null) => Evaluate(() => Interpreter.OnWorkFaulted(new BpmnWorkFaultedRequest(frame.Graph,
        frame.Document.State, Snapshot(context, frame, exclude: child.InvocationId, retired: retired),
        child.Execution.ExecutableNodeId, child.InvocationId, child.Fault?.Message)
        { WorkTokenId = BpmnWorkLedger.TokenId(child), FaultedIterationId = child.IterationId, ErrorCode = ErrorCode(child) }));

    private async ValueTask<Frame> ReadAsync(IRuntimeActivityExecutionContext context, bool starting = false)
    {
        if (!starting && context.ActivityExecutionState.PrivateState is null)
            throw new BpmnHostException("BPMN callback has no initialized private-state checkpoint.");
        var graph = BpmnGraphAdapter.Build(context.ExecutableNode);
        var document = codec.Read(context.ActivityExecutionState.PrivateState, context.SchedulerWorkItem.CommandMetadata);
        var children = await ledger.ReadAsync(context);
        document = BpmnWorkLedger.RecoverLegacy(document, children, Owner(context), graph);
        return new Frame(graph, document, children);
    }

    private static BpmnHostSnapshot Snapshot(IRuntimeActivityExecutionContext context, Frame frame, string? exclude = null,
        ActivityExecutionState? reporting = null, ISet<string>? retired = null)
    {
        var live = BpmnWorkLedger.Live(context, frame.Document.State, frame.Children)
            .Where(work => work.Handle != exclude && retired?.Contains(work.Handle) != true).ToList();
        if (reporting is not null && retired?.Contains(reporting.InvocationId) != true &&
            live.All(work => work.Handle != reporting.InvocationId))
            live.Add(new BpmnLiveWork(reporting.Execution.ExecutableNodeId, reporting.IterationId, reporting.InvocationId)
                { WorkTokenId = BpmnWorkLedger.TokenId(reporting) });
        return new BpmnHostSnapshot(Owner(context), context.ActivityExecutionState.ParentActivityExecutionId is not null,
            live, frame.Document.InvocationCorrelation, new BpmnVariableReader(context), BpmnGraphAdapter.Capabilities);
    }

    private RuntimeStructuralContinuation Finish(IRuntimeActivityExecutionContext context, Frame frame, BpmnEvaluation evaluation, string? incident = null)
    {
        var faulted = evaluation.Continuation is BpmnContinuation.Fault;
        var live = context.GetLiveChildActivities().Select(child => child.ActivityExecutionId).ToHashSet(StringComparer.Ordinal);
        var canceled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in evaluation.Commands)
        {
            switch (command)
            {
                case BpmnHostCommand.StartWork start when !faulted && evaluation.State.ActiveWork.Any(work => work.TokenId == start.TokenId):
                    Schedule(context, start);
                    break;
                case BpmnHostCommand.CancelWorkSubtree stop when !faulted && live.Contains(stop.Handle) && canceled.Add(stop.Handle):
                    context.RequestChildSubtreeCancellation(stop.Handle, stop.Reason);
                    break;
                case BpmnHostCommand.SignalEnclosingScope signal:
                    context.RequestParentNotification(signal.Code, signal.Payload);
                    break;
            }
        }
        if (!faulted && incident is not null && evaluation.ErrorDisposition is BpmnErrorDisposition.Caught or BpmnErrorDisposition.Absorbed)
        {
            var reason = evaluation.ErrorDisposition is BpmnErrorDisposition.Caught caught
                ? frame.Graph.GetRequiredElement(caught.CatchingElementId).TriggeredByEvent
                    ? "bpmn.event-subprocess.error-absorbed" : "bpmn.boundary.error-absorbed"
                : "bpmn.work.late-fault-absorbed";
            context.RequestChildFaultAbsorption(incident, reason);
        }
        var continuation = evaluation.Continuation switch
        {
            BpmnContinuation.Complete complete => RuntimeStructuralContinuation.Complete(complete.Outcome),
            BpmnContinuation.Fault fault => RuntimeStructuralContinuation.Faulted(new ActivityFault(fault.Code, fault.Message,
                category: fault.ErrorCode is null ? null : BpmnErrorCategory, faultType: fault.ErrorCode)),
            _ => RuntimeStructuralContinuation.Defer
        };
        return continuation.WithState(codec.Write(frame.Document, evaluation.State.Prune()), frame.Document.Version);
    }

    private static void Schedule(IRuntimeActivityExecutionContext context, BpmnHostCommand.StartWork start)
    {
        var owner = Owner(context);
        var producer = start.ProducedByHandle ?? owner;
        var metadata = new Dictionary<string, string>(start.Correlation)
        {
            ["bpmn.parentActivityExecutionId"] = owner, ["bpmn.tokenId"] = start.TokenId,
            ["bpmn.elementId"] = start.ElementId, ["bpmn.targetNodeId"] = start.BindingRef,
            ["bpmn.schedulingCause"] = start.SchedulingCause
        };
        if (start.StartElementHint is not null)
            metadata[BpmnStartTrigger.StartElementIdMetadataKey] = start.StartElementHint;
        var iteration = start.IterationScope;
        var values = iteration?.Values.ToDictionary(pair => pair.Key, pair =>
            ValueEnvelope.Inline(new ValueTypeDescriptor(pair.Value.TypeHint == BpmnValueTypes.Integer ? "Int32" : "Elsa.Any"),
                pair.Value.Json ?? throw new BpmnHostException("BPMN iteration value is not inline."), ValueProtectionPolicy.InstanceInline));
        context.ScheduleChildActivity(start.BindingRef, producer, metadata,
            ActivitySchedulingProvenance.From(context.WorkflowExecutionId, owner, producer,
                branchId: context.ActivityExecutionState.BranchId, iterationId: iteration?.IterationId,
                executionPathId: start.TokenId, executionScopeId: null, schedulingCause: start.SchedulingCause,
                metadata: new Dictionary<string, string> { ["bpmn.targetNodeId"] = start.BindingRef, ["bpmn.elementId"] = start.ElementId }),
            iteration is null ? null : new LoopIterationScopeRequest(context.ExecutableNode.ExecutableNodeId, iteration.IterationId, values!));
    }

    private static string Owner(IRuntimeActivityExecutionContext context) => context.ActivityExecutionState.InvocationId;
    private static string? ErrorCode(ActivityExecutionState child) => child.Fault?.Metadata.GetValueOrDefault(ActivityFault.CategoryMetadataKey) == BpmnErrorCategory
        ? child.Fault.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey) : null;
    private static BpmnEvaluation Evaluate(Func<BpmnEvaluation> action)
    {
        try { return action(); }
        catch (global::Bpmn.Semantics.BpmnExecutionException exception) { throw new BpmnHostException(exception.Message, exception); }
    }
    private sealed record Frame(global::Bpmn.Semantics.BpmnGraph Graph, BpmnStateDocument Document, IReadOnlyList<ActivityExecutionState> Children);
}
