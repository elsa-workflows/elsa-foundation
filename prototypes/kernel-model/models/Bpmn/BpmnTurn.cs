using System.Text.Json.Nodes;
using Bpmn.Model;
using Bpmn.Model.State;
using Bpmn.Semantics;

namespace Kernel.Bpmn;

/// <summary>
/// One turn of a BPMN instance. Each inbox message is at most one interpreter call; its evaluation is applied before the
/// next message is read: the commands in the order returned, then the late reports of the work they stopped.
/// </summary>
internal sealed class BpmnTurn(BpmnModel model, ITurn turn)
{
    private readonly BpmnState _s = (BpmnState)turn.State;
    private readonly BpmnProcess _process = model.Registry.Get(turn.Instance.Definition);
    private readonly Queue<(string Handle, LiveWork Work)> _stopped = new();
    private BpmnContinuation? _last;

    public void Run()
    {
        foreach (var message in turn.Inbox)
        {
            // The scope ended (a fault, a terminate end event): what else this turn took is consumed and changes nothing.
            // The interpreter would go on evaluating a scope that an uncaught fault ended, and a later message would hide it.
            if (_last is BpmnContinuation.Complete or BpmnContinuation.Fault) break;
            Accept(message);
        }
        switch (_last)
        {
            case BpmnContinuation.Complete complete:
                turn.Record($"end {complete.Outcome}");
                // The workflow kernel carries the status (not the BPMN substatus) in the durable child notice.
                var status = complete.Outcome == BpmnInterpreter.CancelledOutcomeName ? InstanceStatus.Cancelled : InstanceStatus.Finished;
                turn.SetStatus(status, complete.Outcome, new JsonObject(_s.Variables.Where(v => _s.Set.Contains(v.Key)).Select(v => KeyValuePair.Create(v.Key, v.Value?.DeepClone()))));
                break;
            case BpmnContinuation.Fault fault:
                // The interpreter stops nothing on a fault; the instance ends, so nothing it started may run on.
                foreach (var (handle, work) in _s.Live.ToList()) Undo(handle, work);
                _s.Live.Clear();
                turn.Record($"fault {fault.Code}");
                // A parent's call activity reads the BPMN error back (the port: a crossing error keeps its code).
                turn.SetStatus(InstanceStatus.Faulted, fault.Code, new JsonObject { ["errorCode"] = fault.ErrorCode });
                break;
        }
    }

    // ---- messages in ---------------------------------------------------------------------------------------------------

    private void Accept(Message m)
    {
        switch (m.Kind)
        {
            case Kinds.Start when !_s.Started:
                Start(m, null);
                break;
            case Kinds.Event when !_s.Started && m.Start is not null:
                Start(m, Events.Delivered(m) ?? throw new InvalidOperationException("Malformed BPMN event-start delivery."));
                break;
            case BpmnKinds.Timer:
                Completed(m.Read<TimerDue>().Handle, []);
                break;
            case Kinds.Event when m.Start is null && Events.Delivered(m) is { } delivered && _s.Live.TryGetValue(delivered.Wait, out var wait) && wait.Kind == WorkKinds.Message:
                // A kept event is given to every catch that subscribes within its window; BPMN consumes a message once,
                // so a catch that comes round again in a loop must not take the one an earlier catch took.
                if (_s.Taken?.Contains(delivered.Event) == true || !_s.TakenEvents.Add(new(wait.Key, delivered.Event))) break;
                Merge(Object(m.Payload));
                Completed(delivered.Wait, []);
                break;
            case Kinds.JobFinished:
            {
                var finished = m.Read<JobFinished>();
                if (!_s.Live.ContainsKey(finished.Key)) break;
                if (finished.Error is { } error)
                {
                    Faulted(finished.Key, error);
                    break;
                }
                var result = finished.Result is null ? new ServiceResult() : Json.Read<ServiceResult>(finished.Result);
                Merge(result.Variables);
                Completed(finished.Key, result.Outcomes ?? []);
                break;
            }
            case BpmnKinds.Signal:
            {
                // Not terminal: the call stays in the ledger. A signal for a call already stopped or done is absorbed here.
                var signal = m.Read<ScopeSignal>();
                if (_s.Live.FirstOrDefault(w => w.Value is { Kind: WorkKinds.Call } && w.Value.Key == signal.Child) is not { Key: { } handle, Value: var work }) break;
                turn.Record($"s {handle} {signal.Code}");
                var payload = signal.Payload is null ? (System.Text.Json.JsonElement?)null : System.Text.Json.JsonDocument.Parse(signal.Payload).RootElement.Clone();
                Apply(BpmnModel.Semantics.OnWorkSignalled(new BpmnWorkSignalledRequest(_process.Graph, _s.Tokens, Snapshot(work), work.Binding, handle, signal.Code, payload, work.Iteration) { WorkTokenId = work.Token }));
                break;
            }
            case WorkflowKinds.ChildFinished:
            {
                var finished = m.Read<ChildFinished>();
                if (_s.Live.FirstOrDefault(w => w.Value is { Kind: WorkKinds.Call } && w.Value.Key == finished.Child).Key is not { } handle) break;     // a child it stopped
                turn.Record($"r {handle} {finished.Child}");
                var output = Object(finished.Output);
                if (finished.Status == InstanceStatus.Finished) Merge(output);
                Completed(handle, finished.Status switch
                {
                    InstanceStatus.Finished => [],
                    InstanceStatus.Cancelled => [BpmnInterpreter.CallActivityCancelledOutcomeName],
                    _ => [BpmnInterpreter.CallActivityFaultedOutcomeName],
                }, finished.Status == InstanceStatus.Faulted ? output?["errorCode"]?.GetValue<string>() : null);
                break;
            }
        }
    }

    private void Start(Message message, (string Event, string Wait)? delivery)
    {
        var input = Json.Read<BpmnStart>(message.Start?.Input ?? "{}");
        BpmnStartSignal? signal = null;
        if (delivery is { } supplied)
        {
            var trigger = input.Trigger;
            if (trigger is null || string.IsNullOrEmpty(trigger.Key) || string.IsNullOrEmpty(trigger.Element) ||
                BpmnModel.StartWait(_process.Id, trigger) != supplied.Wait)
                throw new InvalidOperationException("Invalid BPMN event start; register it through BpmnModel.MessageStart.");
            if (!_process.Graph.StartEvents.Any(e => e.ElementId == trigger.Element &&
                    BpmnElementFamilies.Resolve(e) == BpmnElementFamilies.StartEventMessage))
                throw new InvalidOperationException($"'{trigger.Element}' is not a message start event of BPMN process '{_process.Id}'.");
            _s.TakenEvents.Add(new ConsumedEvent(trigger.Key, supplied.Event));
            signal = new BpmnStartSignal(trigger.Element);
        }
        _s.Started = true;
        foreach (var (name, value) in _process.Variables) _s.Variables[name] = value?.DeepClone();
        foreach (var (name, value) in input.Variables ?? []) _s.Variables[name] = value?.DeepClone();
        if (signal is not null) Merge(Object(message.Payload));
        Apply(BpmnModel.Semantics.Start(new BpmnStartRequest(_process.Graph, _s.Tokens, Snapshot(), signal)));
    }

    /// <summary>A payload that is a JSON object, or nothing: an event's payload is the publisher's, in any form.</summary>
    private static JsonObject? Object(string? json)
    {
        if (json is null) return null;
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private void Merge(JsonObject? variables)
    {
        foreach (var (name, value) in variables ?? [])
        {
            _s.Variables[name] = value?.DeepClone();
            _s.Set.Add(name);
        }
    }

    /// <summary>A report for work no longer in the ledger (a stale timer, a second delivery) changes nothing.</summary>
    private void Completed(string handle, IReadOnlyCollection<string> outcomes, string? errorCode = null)
    {
        if (Done(handle) is not { } work) return;
        Apply(BpmnModel.Semantics.OnWorkCompleted(new BpmnWorkCompletedRequest(_process.Graph, _s.Tokens, Snapshot(work), work.Binding, handle, outcomes, work.Iteration) { WorkTokenId = work.Token, ErrorCode = errorCode }));
    }

    private void Faulted(string handle, string error)
    {
        if (Done(handle) is not { } work) return;
        Apply(BpmnModel.Semantics.OnWorkFaulted(new BpmnWorkFaultedRequest(_process.Graph, _s.Tokens, Snapshot(work), work.Binding, handle, error) { FaultedIterationId = work.Iteration, WorkTokenId = work.Token }));
    }

    /// <summary>The unit reported back: it leaves the ledger, and a message catch's subscription goes with this commit.</summary>
    private LiveWork? Done(string handle)
    {
        if (!_s.Live.Remove(handle, out var work)) return null;
        if (work.Kind == WorkKinds.Message) turn.Unsubscribe(work.Key, handle);
        turn.Record($"d {handle} {work.Element}");
        return work;
    }

    // ---- what the interpreter decided ----------------------------------------------------------------------------------

    private void Apply(BpmnEvaluation evaluation)
    {
        var before = _s.Tokens;
        Journal(before, evaluation.State);
        // Diagnostics are the interpreter's record of what it decided. The row does not keep them (the interpreter never
        // reads them back); the journal keeps the joins that fired, and the rest are dropped.
        _s.Tokens = evaluation.State.Prune() with { Diagnostics = [] };
        foreach (var command in evaluation.Commands)
            switch (command)
            {
                case BpmnHostCommand.StartWork start: Begin(start); break;
                case BpmnHostCommand.CancelWorkSubtree cancel: Stop(cancel.Handle); break;
                case BpmnHostCommand.SignalEnclosingScope signal: Signal(signal); break;
            }
        _last = evaluation.Continuation;
        // A stopped unit never reports (a cancelled job's result is nobody's, a recalled timer never comes), so the host
        // reports it at once: the interpreter absorbs the report and the cancelled token's stopped handle is spent.
        while (_stopped.TryDequeue(out var stopped))
        {
            var late = BpmnModel.Semantics.OnWorkCompleted(new BpmnWorkCompletedRequest(_process.Graph, _s.Tokens, Snapshot(stopped.Work), stopped.Work.Binding, stopped.Handle, [], stopped.Work.Iteration) { WorkTokenId = stopped.Work.Token });
            Apply(late);
        }
    }

    /// <summary>An escalation (or another signal) for the enclosing scope: a called child's parent, as a message.</summary>
    private void Signal(BpmnHostCommand.SignalEnclosingScope signal)
    {
        turn.Record($"signal {signal.Code}");
        if (turn.Instance.Parent is { } parent)
            turn.Send(new InstanceRef(parent), BpmnKinds.Signal, new ScopeSignal(turn.Instance.Ref.Id, signal.Code, signal.Payload?.GetRawText()),
                new SendOptions(TargetCreation: turn.Instance.ParentCreation > 0 ? turn.Instance.ParentCreation : null));
    }

    private void Begin(BpmnHostCommand.StartWork start)
    {
        var handle = "h" + ++_s.Next;
        var self = turn.Instance.Ref;
        var (kind, key) = _process.Work[start.BindingRef] switch
        {
            JobWork job => (WorkKinds.Job, StartJob(handle, job, start)),
            TimerWork timer => (WorkKinds.Timer, turn.Send(self, BpmnKinds.Timer, new TimerDue(handle), new SendOptions(Delay: TimeSpan.FromMilliseconds(timer.Milliseconds)))),
            MessageWork message => (WorkKinds.Message, Subscribe(handle, message, start.IterationScope)),
            CallWork call => (WorkKinds.Call, turn.Start(BpmnModel.Spec(call.Process, Variables(start.IterationScope))).Id),
            var other => throw new InvalidOperationException($"No host work for {other}."),
        };
        _s.Live[handle] = new LiveWork(start.BindingRef, start.IterationScope?.IterationId, start.TokenId, start.ElementId, kind, key,
            start.IterationScope?.Values, kind == WorkKinds.Call ? turn.ChildAddress(new InstanceRef(key)).Creation : 0);
        turn.Record($"w {handle} {start.ElementId} {kind} {key}");
    }

    private string StartJob(string handle, JobWork job, BpmnHostCommand.StartWork start)
    {
        turn.StartJob(handle, new JobArgs(_process.Id, job.Service, start.ElementId, Variables(start.IterationScope)));
        return handle;
    }

    private string Subscribe(string handle, MessageWork message, BpmnIterationScope? scope)
    {
        var key = BpmnModel.MessageKey(message.Name, Conditions.Read(_s.Variables, scope?.Values, message.Correlation)?.ToString() ?? "");
        turn.Subscribe(key, handle, keep: true);      // BPMN correlation: a message published before the catch still counts
        return key;
    }

    private void Stop(string handle)
    {
        if (!_s.Live.Remove(handle, out var work)) return;
        Undo(handle, work);
        _stopped.Enqueue((handle, work));
    }

    /// <summary>The kernel's side of a unit that will not finish: its job, timer, subscription or child goes.</summary>
    private void Undo(string handle, LiveWork work)
    {
        switch (work.Kind)
        {
            case WorkKinds.Job: turn.CancelJob(work.Key); break;
            case WorkKinds.Timer: turn.Recall(work.Key); break;
            case WorkKinds.Message: turn.Unsubscribe(work.Key, handle); break;
            case WorkKinds.Call:
                turn.Send(new InstanceRef(work.Key), Kinds.Terminate, options: new SendOptions(TargetCreation: work.Creation > 0 ? work.Creation : null));
                break;
        }
        turn.Record($"x {handle} {work.Element}");
    }

    private JsonObject Variables(BpmnIterationScope? scope)
    {
        var snapshot = new JsonObject(_s.Variables.Select(v => KeyValuePair.Create(v.Key, v.Value?.DeepClone())));
        if (scope is null) return snapshot;
        foreach (var (name, value) in scope.Values)
        {
            if (value.Presence == BpmnValuePresence.Absent) snapshot.Remove(name);
            else snapshot[name] = Conditions.Read(_s.Variables, scope.Values, name);
        }
        return snapshot;
    }

    private BpmnHostSnapshot Snapshot(LiveWork? reporting = null)
    {
        var conditions = new Conditions(_s, reporting);
        return new BpmnHostSnapshot(turn.Instance.Ref.Id, HasEnclosingScope: turn.Instance.Parent is not null,
            [.. _s.Live.Select(w => new BpmnLiveWork(w.Value.Binding, w.Value.Iteration, w.Key) { WorkTokenId = w.Value.Token })],
            new Dictionary<string, string>(), conditions, BpmnProcess.Capabilities) { Conditions = conditions };
    }

    // ---- the journal ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// What the checks read: each join that fired, with the arrivals it consumed (<c>j element merged-token arrivals</c>,
    /// an arrival as <c>token/flow</c>; entries are separated by spaces, since token ids hold colons). The arrivals are the tokens at the join that this evaluation consumed.
    /// </summary>
    private void Journal(BpmnExecutionState before, BpmnExecutionState after)
    {
        var consumedBefore = before.Tokens.Where(t => t.Status == BpmnTokenStatus.Consumed).Select(t => t.TokenId).ToHashSet(StringComparer.Ordinal);
        foreach (var joined in after.Diagnostics.Where(d => d.Kind == BpmnDiagnosticKind.Joined))
        {
            var arrivals = after.Tokens.Where(t => t.AtElementId == joined.ElementId && t.FlowId is not null && t.Status == BpmnTokenStatus.Consumed
                && t.TokenId != joined.TokenId && !consumedBefore.Contains(t.TokenId) && t.IterationKey == after.Tokens.FirstOrDefault(m => m.TokenId == joined.TokenId)?.IterationKey);
            turn.Record($"j {joined.ElementId} {joined.TokenId} {string.Join(",", arrivals.Select(t => $"{t.TokenId}/{t.FlowId}"))}");
        }
    }
}
