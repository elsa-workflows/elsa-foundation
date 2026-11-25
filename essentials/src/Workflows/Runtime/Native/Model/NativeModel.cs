using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

public static class Modes
{
    public const string Ephemeral = "Ephemeral", Balanced = "Balanced", Durable = "Durable";
}

/// <summary>Messages the native model understands besides the kernel's own kinds (start, child-finished, job-finished, event).</summary>
public static class NativeKinds
{
    public const string Continue = "native.continue";   // a Durable boundary: the burst goes on in the next turn
    public const string Timer = "native.timer";         // a Delay is due
    public const string Resume = "native.resume";       // an outsider resumes a wait by its stimulus, without the event tables
    public const string Cancel = "native.cancel";
}

/// <summary>Deliberate defects of the model, one per check of the simulation (NATIVE.md, section 5).</summary>
[Flags]
public enum NativeFlaw
{
    None = 0,
    InFlightInMemory = 1 << 0,  // Durable: the executed work item leaves the agenda only in memory
    JoinWithoutGuard = 1 << 1,  // Parallel: no joined flag, and the join does not cancel the other branches
    ResumeAnyChild = 1 << 2,    // a child-finished resumes every child wait, not the one for that child
    NoAttemptCommit = 1 << 3,   // Durable: an External activity runs without its attempt committed first
    ScopeInMemory = 1 << 4,     // an iteration scope is kept in memory only
    // The graph engine's flaws (FlowchartFlaws), the sketch's four, for G1, G2, G3 and G5 (NATIVE.md, section 6):
    NoDeadPaths = 1 << 5,       // an untaken connection emits no dead arrival
    NoRoundReset = 1 << 6,      // a loop-closing connection opens no new round
    DeadPathRunsNode = 1 << 7,  // a node whose every inbound arrived dead is run
    CancelLeavesPaths = 1 << 8, // a first-wins race completes without cancelling its losers
    // Round 5 C, the effect key (N7): minted per turn instead of derived from committed facts, so a resend carries another
    // key; without the node, so two activities in one scope share a key; without the iteration path, so two iterations
    // of one activity do.
    UnstableEffectKey = 1 << 9,
    NodelessEffectKey = 1 << 10,
    PathlessEffectKey = 1 << 11,
    TurnLocalJoinOrdinal = 1 << 12, // a Flowchart join's firing ordinal counted per turn in memory instead of from the committed firings
    FlatRetryGeneration = 1 << 13,  // every retry successor is generation 1, so the second retry of one activation shares its key with the first
    FreshTurnSwapsLoopKeys = 1 << 14, // a turn that is not a redelivery swaps a While's first two iterations in the key, so a lost first attempt sends another activation's valid key
}

/// <summary>An HTTP start gives its trigger as the turn's ambient value instead (the kernel's Ambient: never stored).</summary>
public sealed record NativeStart(string Mode = Modes.Balanced, JsonObject? Input = null, string? TriggerNode = null, JsonNode? Trigger = null);

public sealed record TimerFired(string Wait);

public sealed record ResumeStimulus(string Event, JsonNode? Payload = null);

/// <summary>A background job's ticket. Its iteration path and effect key are computed in the turn that starts it and committed with it (round 5 C).</summary>
public sealed record BackgroundArgs(string Workflow, string Node, string Frame, JsonObject Inputs, string? IterationPath = null, string? EffectKey = null);

public sealed record BackgroundResult(TransitionKind Kind, JsonNode? Result, string Outcome, ActivityFault? Fault);

public class NativeExecutionOptions
{
    public NativeFlaw Flaws { get; init; }
    public int MaxActivitiesPerTurn { get; init; } = 1000;

    /// <summary>
    /// Whether a Balanced or Ephemeral burst that reaches <see cref="MaxActivitiesPerTurn"/> continues in the next turn, through
    /// a self-send. A host whose foundation takes no self-sends sets it off: the burst runs on to its next wait or end, and the
    /// turn is bounded by the state and history size limits instead (an oversized turn faults with its reason).
    /// </summary>
    public bool SplitsBursts { get; init; } = true;
    public AdmissionFlaw AdmissionFlaws { get; init; }
    public TimeSpan EventWindow { get; init; } = TimeSpan.FromHours(1);
    /// <summary>The host's outbound HTTP for SendHttpRequest's job; without it the activity faults.</summary>
    public NativeHttpOptions? Http { get; init; }
    /// <summary>The host's script engine for RunJavaScript; without it a script faults.</summary>
    public INativeScripts? Scripts { get; init; }
    /// <summary>The host's evaluator for host expressions (JavaScript, Liquid); without it such a binding faults its activation.</summary>
    public INativeExpressions? Expressions { get; init; }

    /// <summary>
    /// What a publishing host serves for the id a Dispatch Workflow names. A designer's Dispatch names a definition, and a
    /// publishing host serves each definition as one artifact at a time, so it answers with the workflow id it serves now,
    /// with none, or with several (<see cref="ServedDefinition"/>). It is read once per child start, and that one answer
    /// decides the start:
    /// <list type="bullet">
    /// <item>one workflow: the child starts it;</item>
    /// <item>several, none of them the default: the Dispatch faults with <c>dispatch.ambiguous</c>;</item>
    /// <item>none: the child starts the workflow the id names if the registry holds it, and otherwise the Dispatch faults
    /// with <c>dispatch.unpublished</c>. A child start never goes through <see cref="ResolveWorkflow"/>.</item>
    /// </list>
    /// Without it, the child starts the workflow the id names. It is read inside the turn, so a turn that runs again after a
    /// lost commit may start a version published in between.
    /// </summary>
    public Func<string, ServedDefinition>? ServedWorkflow { get; init; }

    /// <summary>
    /// The workflow for an id the registry does not hold, which the host loads (null when it has none). A publishing host's
    /// registry holds, after a restart, the versions its slots serve, and an instance started on an earlier version still runs
    /// that version: the host loads it from its artifacts. Consulted before a turn or a job whose workflow the registry lacks;
    /// what it returns joins the registry. A migration's check reads the registry only, and a Dispatch never starts a child
    /// through it (<see cref="ServedWorkflow"/>).
    /// </summary>
    public Func<string, CancellationToken, ValueTask<Workflow?>>? ResolveWorkflow { get; init; }
}

/// <summary>What a publishing host serves for a definition (<see cref="NativeExecutionOptions.ServedWorkflow"/>): one workflow, none, or several.</summary>
public readonly record struct ServedDefinition
{
    private ServedDefinition(string? workflow, bool several) => (Workflow, Several) = (workflow, several);

    /// <summary>The workflow that serves the definition, when one does.</summary>
    public string? Workflow { get; }

    /// <summary>Whether several serve it and none is the one a Dispatch reaches first.</summary>
    public bool Several { get; }

    public static ServedDefinition None => default;
    public static ServedDefinition SeveralServe => new(null, true);
    public static ServedDefinition By(string workflow) => new(workflow ?? throw new ArgumentNullException(nameof(workflow)), false);
}

// ---- the state: live frames, scheduled work, waits, variables, results ---------------------------------------------

public sealed partial class NativeState
{
    public string Mode { get; set; } = Modes.Balanced;
    public bool Started { get; set; }
    public long Next { get; set; }
    public Dictionary<string, Frame> Frames { get; set; } = new(StringComparer.Ordinal);
    public List<Work> Agenda { get; set; } = [];
    public Dictionary<string, WaitEntry> Waits { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonNode?> Variables { get; set; } = new(StringComparer.Ordinal);
    /// <summary>The correlation id Set Correlation Id set, which an expression's instance parameter reads; the label holds it too.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; set; }
    public Dictionary<string, JsonNode?> Results { get; set; } = new(StringComparer.Ordinal);
    public string? TriggerNode { get; set; }
    public JsonNode? Trigger { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, InvocationInbox>? Inboxes { get; set; }
}

/// <summary>Owned activity state: running/waiting, or a retained fault awaiting intervention.</summary>
public sealed partial class Frame
{
    public string Id { get; set; } = "";
    public string Node { get; set; } = "";
    public string? Parent { get; set; }
    public string? Tag { get; set; }
    public JsonNode? State { get; set; }
    public Dictionary<string, JsonNode?>? Scope { get; set; }
    public int Attempt { get; set; }
    [JsonIgnore] internal Dictionary<string, JsonNode?>? LiveScope;   // only under NativeFlaw.ScopeInMemory
}

/// <summary>Scheduled work: execute a node as a new frame, or resume a frame's wait.</summary>
public sealed partial class Work
{
    public bool Resume { get; set; }
    public string Frame { get; set; } = "";
    public string? Node { get; set; }
    public string? Parent { get; set; }
    public string? Tag { get; set; }
    public Dictionary<string, JsonNode?>? Scope { get; set; }
    public string? Wait { get; set; }
    public JsonNode? Payload { get; set; }
    public int Attempt { get; set; }       // External in Durable: the attempt committed before it runs
    [JsonIgnore] internal Dictionary<string, JsonNode?>? LiveScope;
    [JsonIgnore] internal bool Gone;       // only under NativeFlaw.InFlightInMemory
}

public sealed record WaitEntry(string Frame, string Kind, string Key, long Creation = 0)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Due { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Contract.JobSpec? Job { get; init; }
}

// ---- the model -----------------------------------------------------------------------------------------------------

/// <summary>One turn: take the inbox, then run the scheduled work until everything waits, ends or meets a boundary.</summary>
internal sealed partial class Burst(WorkflowRegistry registry, NativeExecutionOptions options, INativeTurn turn, CancellationToken cancellationToken,
    Contract.ActorStatus? ended = null)
{
    internal bool HasExactIdentity => turn.HasExactIdentity;
    private readonly NativeState _s = turn.State;
    private Workflow _wf => registry.Get(turn.Workflow); // metadata-only turns never resolve authored definitions
    private Contract.ActorStatus _status = Contract.ActorStatus.Running;
    private int _executed;

    internal static bool HandlesMetadata(string kind) => kind is InvocationInput.Kind or ReceiptQuery.Kind;

    private bool Has(NativeFlaw flaw) => (options.Flaws & flaw) != 0;
    internal bool Flawed(NativeFlaw flaw) => Has(flaw);
    private bool Durable => _s.Mode == Modes.Durable;
    private string NewId(string prefix) => prefix + ++_s.Next;

    public async ValueTask RunAsync()
    {
        try { await RunTurnAsync(); }
        catch
        {
            // The turn's own failure is the one to report, not a session that also fails to close.
            try { await CloseExpressionsAsync(); }
            catch (Exception) { /* the turn already failed */ }
            throw;
        }
        await CloseExpressionsAsync();
    }

    private async ValueTask RunTurnAsync()
    {
        LexicalScopes.Validate(_s);
        NativeInterventions.Validate(_s);
        if (AdmitPrefix()) return;
        foreach (var message in turn.Items)
            if (HandlesMetadata(message.Kind)) turn.Hold(message);
            else Accept(message);
        if (_retryAccepted) turn.SendToSelf(NativeKinds.Continue);
        else { await DrainAsync(); if (!_checkpointRequested) DrainInputs(); }
        if (_checkpointRequested) turn.SendToSelf(NativeKinds.Continue);
        FinishSignals();
        if (turn.Items.Any(message => HandlesMetadata(message.Kind))) turn.SendToSelf(NativeKinds.Continue);
    }

    // ---- messages in -------------------------------------------------------------------------------------------------

    private static T Read<T>(Contract.Message message) => System.Text.Json.JsonSerializer.Deserialize<T>(message.Payload!)!;

    private void Accept(Contract.Message m)
    {
        switch (m.Kind)
        {
            case NativeRetry.Kind:
            {
                var result = Retry(m);
                turn.Reply(m, result);
                turn.Record($"recovery:{result.Id}:{result.Result}:{result.Successor}");
                break;
            }
            case Contract.Kinds.Start when !_s.Started:
            {
                _s.Started = true;
                InitializeIntervention();
                var start = System.Text.Json.JsonSerializer.Deserialize<NativeStart>(m.Payload ?? "{}")!;
                foreach (var (name, value) in _wf.Variables) _s.Variables[name] = value?.DeepClone();
                if (start.Input is { } input) _s.Variables["input"] = input.DeepClone();
                (_s.TriggerNode, _s.Trigger) = (start.TriggerNode, start.Trigger ?? turn.Ambient as JsonNode);
                _s.Agenda.Add(new Work { Frame = NewId("f"), Node = _wf.Root.Id });
                break;
            }
            case CompositeSignal.Kind:
                AcceptSignal(m);
                break;
            case NativeKinds.Timer:
            {
                // The timer's message is consumed by this turn. Its resume may wait in the agenda past a Durable boundary,
                // and a cancel meanwhile must not recall a message that is gone: Recall is for one not yet received.
                if (turn.TimerWait(m) is { } wait && _s.Waits.TryGetValue(wait, out var timer) && timer.Kind == "timer")
                {
                    _s.Waits[wait] = timer with { Key = "" };
                    Resume(wait, null);
                }
                break;
            }
            case Contract.Kinds.Event:
                // Friction F-N3, fixed in the merge: the kernel says which wait a delivery is for.
                if (turn.EventWait(m) is { } eventWait) Resume(eventWait, Values.Parse(m.Payload));
                break;
            case NativeKinds.Resume:
            {
                var stimulus = Read<ResumeStimulus>(m);
                if (_s.Waits.FirstOrDefault(w => w.Value.Key == "event:" + stimulus.Event).Key is { } wait) Resume(wait, stimulus.Payload);
                break;
            }
            case Contract.Kinds.ChildFinished:
            {
                var finished = turn.ChildFinished(m);
                if (!finished.Final) break;
                var payload = new JsonObject { ["Status"] = finished.Status.ToString(), ["Output"] = finished.Output is null ? null : JsonNode.Parse(finished.Output), ["Child"] = finished.Child.Id };
                foreach (var (id, wait) in _s.Waits.Where(w => w.Value.Kind == "child" && (w.Value.Key == finished.Child.Id || Has(NativeFlaw.ResumeAnyChild))).ToList())
                    Resume(id, payload.DeepClone());
                break;
            }
            case Contract.Kinds.JobFinished:
            {
                var finished = Read<Contract.JobFinished>(m);
                Resume(finished.Key, Values.From(finished));
                break;
            }
            case NativeKinds.Cancel:
                CancelWorkflow();
                break;
        }
    }

    // A wait is removed when its resume is processed, so a second delivery for it (two events, a stale timer) does nothing.
    private void Resume(string wait, JsonNode? payload)
    {
        if (_s.Waits.TryGetValue(wait, out var entry)) _s.Agenda.Add(new Work { Resume = true, Frame = entry.Frame, Wait = wait, Payload = payload });
    }

    // ---- the burst ---------------------------------------------------------------------------------------------------

    private async ValueTask DrainAsync()
    {
        _s.Agenda.RemoveAll(w => w.Gone);      // only under NativeFlaw.InFlightInMemory: memory forgets now what the row still has
        while (!_checkpointRequested && _s.Agenda.Exists(w => !w.Gone) && _status == Contract.ActorStatus.Running)
        {
            // Durable: a boundary after every activity. Balanced and Ephemeral: one turn until all waits or ends (capped).
            if ((Durable && _executed > 0) || options.SplitsBursts && _executed >= options.MaxActivitiesPerTurn)
            {
                turn.SendToSelf(NativeKinds.Continue);
                return;
            }
            var work = _s.Agenda.FirstOrDefault(w => !w.Gone && Runnable(w));
            if (work is null) return;
            if (!work.Resume && Durable && _wf.Nodes[work.Node!].External && work.Attempt == 0 && !Has(NativeFlaw.NoAttemptCommit))
            {
                // Not replay-safe: commit the attempt first, run it in the next turn. After a crash the row says it may have run.
                work.Attempt = 1;
                turn.Record($"a:{work.Frame}:{work.Attempt}");
                turn.SendToSelf(NativeKinds.Continue);
                return;
            }
            if (Has(NativeFlaw.InFlightInMemory) && Durable) work.Gone = true;     // MUTATION: dequeued in memory only; the commit still carries it
            else _s.Agenda.Remove(work);
            _executed++;
            try
            {
                if (work.Resume) await ResumeAsync(work);
                else await ExecuteAsync(work);
            }
            catch (NativeExpressionException failed) when (FrameOf(work) is { } frame)
            {
                Fail(frame, ExpressionFault(frame, failed));
            }
        }
    }

    // The frame a work item runs, for a host expression's fault: a resume's waiting frame, or the frame it starts.
    private Frame? FrameOf(Work work) => work.Resume
        ? _s.Waits.TryGetValue(work.Wait!, out var wait) ? _s.Frames.GetValueOrDefault(wait.Frame) : null
        : _s.Frames.GetValueOrDefault(work.Frame) ?? (work.Parent is null || _s.Frames.ContainsKey(work.Parent)
            ? new Frame { Id = work.Frame, Node = work.Node!, Parent = work.Parent, Tag = work.Tag, Scope = work.Scope, Attempt = work.Attempt, LiveScope = work.LiveScope, LexicalScope = work.LexicalScope }
            : null);

    private async ValueTask ExecuteAsync(Work work)
    {
        if (work.Parent is not null && !_s.Frames.ContainsKey(work.Parent)) return;     // its parent was cancelled meanwhile
        var node = _wf.Nodes[work.Node!];
        var frame = new Frame { Id = work.Frame, Node = node.Id, Parent = work.Parent, Tag = work.Tag, Scope = work.Scope, Attempt = work.Attempt, LiveScope = work.LiveScope, LexicalScope = work.LexicalScope };
        if (work.RetrySource is not null)
        {
            frame.RetrySource = work.RetrySource; frame.OperationKey = work.OperationKey;
            frame.RecoveryStateful = _s.Retries!.Values.Single(r => r.Source.Id == work.RetrySource && r.Successor == work.Frame).Source.RecoveryStateful;
            frame.RecoveryInputs = (JsonObject)work.RecoveryInputs!.DeepClone(); frame.RecoveryMaterial = (JsonObject)work.RecoveryMaterial!.DeepClone();
            if (!JsonNode.DeepEquals(frame.RecoveryMaterial, NativeRecoveryMaterial.Describe(_wf)))
                throw new InvalidOperationException("Scheduled retry material is unavailable.");
            if (!InterventionBoundary(frame)) throw new InvalidOperationException("Scheduled retry boundary is unsupported.");
        }
        var scope = new FrameScope(this, frame);
        if (node.Background)
        {
            // The slow activity is a background job: its intent rides in this commit, its result comes back as a message.
            _s.Frames[frame.Id] = frame;
            var wait = NewId("w");
            var path = IterationPath(frame);
            var arguments = new BackgroundArgs(_wf.Id, node.Id, frame.Id, Hydrator.Snapshot(node, b => Evaluate(b, scope)), path, EffectKeyOf(frame, path));
            var jobTimeout = node.JobTimeout ?? TimeoutInput(node, arguments.Inputs);
            _s.Waits[wait] = new WaitEntry(frame.Id, "job", wait)
            {
                Job = turn.SavesWaitRecipes ? new Contract.JobSpec(wait, System.Text.Json.JsonSerializer.Serialize(arguments))
                {
                    Deadline = jobTimeout is { } timeout ? checked(turn.Now + (long)timeout.TotalMilliseconds) : null
                } : null
            };
            turn.StartJob(wait, arguments, jobTimeout);
            return;
        }
        var pinned = frame.RecoveryInputs is { } saved ? (JsonObject)saved.DeepClone() : PinRecoveryInputs(frame, node, scope);
        var activity = CreateActivity(frame, node, pinned, scope);
        if (activity is null) return;
        switch (activity)
        {
            case IIntrinsic set:
                Assign(frame, set.Name, set.Value);
                Complete(frame, "Done", null);
                break;
            case var other when Intrinsic(frame, other):
                break;
            case CompositeActivity composite:
                _s.Frames[frame.Id] = frame;
                composite.Execute(new CompositeContext(this, frame, node));
                break;
            case IStateful stateful:
            {
                var delivery = _s.TriggerNode == node.Id && _s.Trigger is not null;
                var trigger = delivery ? _s.Trigger : null;
                if (delivery) (_s.TriggerNode, _s.Trigger) = (null, null);
                var context = Context(frame);
                Apply(frame, await InvokeAuthored(frame, () => stateful.StartAsync(context, delivery, trigger)));
                break;
            }
            case ILeaf leaf:
                if (node.External)
                {
                    // The observed delivery history can flag a possible earlier turn; a false hint does not rule out a race.
                    var again = turn.MayHaveRun();
                    if (again && Durable) turn.Record($"m:{frame.Id}:{frame.Attempt}");
                    turn.ExternalAttempt(frame.Id, node.Id, frame.Attempt, again);
                }
                var leafContext = Context(frame);
                Apply(frame, await InvokeAuthored(frame, () => leaf.ExecuteAsync(leafContext)));
                break;
        }
    }

    private async ValueTask ResumeAsync(Work work)
    {
        if (!_s.Waits.TryGetValue(work.Wait!, out var wait) || !_s.Frames.TryGetValue(wait.Frame, out var frame)) return;
        if (frame.RecoveryStateful && frame.Id != work.Frame) throw new InvalidOperationException("Invalid native qualified resume ownership.");
        var node = _wf.Nodes[frame.Node];
        if (frame.RecoveryStateful && (!RecoverableKind(node) || !typeof(IStateful).IsAssignableFrom(node.Type) ||
            !JsonNode.DeepEquals(frame.RecoveryMaterial, NativeRecoveryMaterial.Describe(_wf))))
            throw new InvalidOperationException("Suspended recovery material is unavailable.");
        _s.Waits.Remove(work.Wait!);
        // The first valid delivery owns this occurrence. Already queued duplicates cannot keep a fault runnable.
        if (frame.RecoveryStateful) _s.Agenda.RemoveAll(w => w.Resume && w.Frame == frame.Id);
        Unregister(work.Wait!, wait, cancel: false);
        foreach (var (id, other) in _s.Waits.Where(w => w.Value.Frame == frame.Id).ToList())     // an activity resumes once
        {
            _s.Waits.Remove(id);
            Unregister(id, other, cancel: true);
        }
        if (wait.Kind == "child") turn.Record($"r:{work.Wait}:{wait.Key}:{work.Payload?["Child"]}");
        _s.Frames.Remove(frame.Id);
        if (node.Background)
        {
            var finished = Values.To<Contract.JobFinished>(work.Payload)!;
            // A job whose deadline passed completes an activity that names a timeout outcome on it (SendHttpRequest's Timeout).
            if (finished.Error == "timeout" && node.Type.GetCustomAttributes(typeof(BackgroundAttribute), false) is [BackgroundAttribute { TimeoutOutcome: { } timedOut }])
                Apply(frame, new Transition(TransitionKind.Complete, null, timedOut));
            else if (finished.Error is { } error) Fail(frame, new ActivityFault("job", error));
            else if (System.Text.Json.JsonSerializer.Deserialize<BackgroundResult>(finished.Result!)! is var r) Apply(frame, new Transition(r.Kind, r.Result, r.Outcome, r.Fault));
            return;
        }
        // The wait and the frame are gone by now, so a host expression that fails here faults the frame directly.
        IStateful? activity;
        try { activity = (IStateful?)CreateActivity(frame, node, frame.RecoveryStateful ? (JsonObject)frame.RecoveryInputs!.DeepClone() : null, new FrameScope(this, frame)); }
        catch (NativeExpressionException failed) { Fail(frame, ExpressionFault(frame, failed)); return; }
        if (activity is null) return;
        var context = Context(frame);
        var state = frame.RecoveryStateful ? frame.State?.DeepClone() : frame.State;
        Apply(frame, await InvokeAuthored(frame, () => activity.ResumeAsync(context, state, work.Payload)));
    }

    private ActivityContext Context(Frame frame)
    {
        var path = IterationPath(frame);
        return new(turn.Actor, frame.Id, frame.Node, frame.Attempt, cancellationToken, frame.OperationKey, frame.RetrySource, path, EffectKeyOf(frame, path)) { Scripts = options.Scripts };
    }

    // ---- the effect key (round 5 C) ----------------------------------------------------------------------------------

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, CompositeActivity> IterationProbes = new();
    private static long _unstableKeys;
    private readonly long _unstableStamp = Interlocked.Increment(ref _unstableKeys);   // MUTATION: one value per turn, so a resend in another turn carries another key

    /// <summary>
    /// Which activation of its node this frame is, within the instance: for every frame on the way from the root to it,
    /// what its parent says the child's iteration is (<see cref="CompositeActivity.IterationOf"/>, from the parent's
    /// committed state: a ForEach index, a While pass, a Flowchart round) and, for a retried activity, its retry
    /// generation. Root first, <c>parent=iteration</c> segments joined by dots; <see cref="Kernel.Model.Contracts.EffectKey.RootPath"/> when
    /// nothing repeats it. Frame numbers are not in it: a replay over a longer inbox mints them in another order
    /// (the owner's decision of 16 November).
    /// </summary>
    internal string IterationPath(Frame frame)
    {
        var segments = new List<string>();
        for (Frame? f = frame; f is not null; f = f.Parent is { } id ? _s.Frames.GetValueOrDefault(id) : null)
        {
            if (f.RetrySource is not null) segments.Add($"{f.Node}=retry{RetryGeneration(f)}");
            if (f.Parent is not { } parentId || !_s.Frames.TryGetValue(parentId, out var parent)) continue;
            var probe = IterationProbes.GetOrAdd(_wf.Nodes[parent.Node].Type, static t => (CompositeActivity)Activator.CreateInstance(t)!);
            if (probe.IterationOf(parent.State, f.Tag, f.Scope ?? f.LiveScope, f.LexicalScope) is { } iteration) segments.Add($"{parent.Node}={iteration}");
        }
        if (segments.Count == 0) return Kernel.Model.Contracts.EffectKey.RootPath;
        segments.Reverse();
        return string.Join('.', segments);
    }

    /// <summary>
    /// How many retries lie between the original activation and this successor frame, from the committed lineage: the
    /// receipt that made this frame the successor of its source, then the receipt that made that source a successor, and
    /// so on (receipts are only ever added). The MUTATION gives every successor generation 1.
    /// </summary>
    private int RetryGeneration(Frame frame)
    {
        if (Has(NativeFlaw.FlatRetryGeneration)) return 1;
        var generation = 0;
        for (var current = frame.Id; _s.Retries is { } retries && retries.Values.FirstOrDefault(r => r.Successor == current) is { } receipt; current = receipt.Source.Id) generation++;
        return generation;
    }

    /// <summary>The key from committed facts (<see cref="INativeTurn.EffectKey"/>); only the three MUTATIONS derive anything else.</summary>
    private string EffectKeyOf(Frame frame, string path)
    {
        if (Has(NativeFlaw.NodelessEffectKey)) return turn.EffectKey("node", path, frame.Attempt);
        if (Has(NativeFlaw.PathlessEffectKey)) return turn.EffectKey(frame.Node, Kernel.Model.Contracts.EffectKey.RootPath, frame.Attempt);
        // MUTATION: on a first attempt (no redelivery hint) iteration 1 takes iteration 2's key and 2 takes 1's; the replay
        // after a lost first attempt derives the right keys, so the wrong ones live only in turns that never landed.
        if (Has(NativeFlaw.FreshTurnSwapsLoopKeys) && !turn.MayHaveRun()) path = path.Replace("loop=1", "loop=#").Replace("loop=2", "loop=1").Replace("loop=#", "loop=2");
        var key = turn.EffectKey(frame.Node, path, frame.Attempt);
        return Has(NativeFlaw.UnstableEffectKey) ? key + "." + _unstableStamp : key;
    }

    // ---- transitions -------------------------------------------------------------------------------------------------

    private void Apply(Frame frame, Transition t)
    {
        // Each child the transition starts, read off the host once: that answer both checks the start and makes it.
        var children = ChildTargets(t, out var refused);
        if (refused is not null)
        {
            Fail(frame, refused);
            return;
        }
        if (t.Kind == TransitionKind.Complete && t.Effects?.OfType<ExitEffect>().FirstOrDefault() is { } exit)
        {
            foreach (var effect in t.Effects) if (effect is not ExitEffect) Do(frame, effect, children);
            Exit(frame, exit);
            return;
        }
        foreach (var effect in t.Effects ?? []) Do(frame, effect, children);
        switch (t.Kind)
        {
            case TransitionKind.Complete: Complete(frame, t.Outcome, t.Result); break;
            case TransitionKind.Cancel: Complete(frame, "Cancelled", null); break;
            case TransitionKind.Fault: Fail(frame, t.Fault!); break;
            case TransitionKind.Suspend:
                frame.State = t.State;
                _s.Frames[frame.Id] = frame;
                foreach (var trigger in t.Triggers ?? []) Register(frame, trigger, children);
                break;
        }
    }

    // Every leak of the activity map ends here: each trigger and effect is one kernel call, made by the model.
    private void Register(Frame frame, Trigger trigger, IReadOnlyDictionary<string, string>? children)
    {
        var id = NewId("w");
        switch (trigger)
        {
            case EventTrigger e:
                _s.Waits[id] = new WaitEntry(frame.Id, "event", "event:" + e.Name);
                turn.Subscribe("event:" + e.Name, id);
                break;
            case HttpTrigger h:
                // A path resolved from a route template keeps its values' case; any other path is a literal route's.
                var key = h.Resolved ? HttpRoutes.ResolvedKey(h.Method, h.Path) : HttpRoutes.Key(h.Method, h.Path);
                _s.Waits[id] = new WaitEntry(frame.Id, "event", key);
                turn.Subscribe(key, id);
                break;
            case TimerTrigger t:
                // The key is the timer message's id, so that a cancel can take it back (friction F-N4, fixed in the merge).
                var delay = TimeSpan.FromMilliseconds(t.Milliseconds);
                var issuedAt = turn.Now;
                if (System.Diagnostics.Activity.Current is { IsAllDataRequested: true } trace)
                    trace.AddEvent(new ActivityEvent("native.timer.issued", tags: new ActivityTagsCollection
                    {
                        { "actor", turn.Actor.Id }, { "issuedAt", issuedAt }
                    }));
                var dueAt = checked(issuedAt + (long)delay.TotalMilliseconds);
                _s.Waits[id] = new WaitEntry(frame.Id, "timer", turn.SendToSelf(NativeKinds.Timer, new TimerFired(id), dueAt))
                    { Due = turn.SavesWaitRecipes ? dueAt : null };
                break;
            case ChildTrigger c:
                var child = turn.StartChild(frame.Id, children![c.Workflow], c.Mode, c.Input as JsonObject);
                _s.Waits[id] = child;
                turn.Record($"s:{frame.Id}:{child.Key}");
                break;
        }
    }

    internal const string UnpublishedFaultCode = "dispatch.unpublished";
    internal const string AmbiguousFaultCode = "dispatch.ambiguous";

    /// <summary>
    /// The workflow each child start of <paramref name="t"/> starts, by the id its Dispatch names. A Dispatch names a
    /// definition or a workflow, and the host says which workflow serves a definition (<see cref="NativeExecutionOptions.ServedWorkflow"/>),
    /// read once per id here. On a publishing host, a Dispatch the host serves nothing for, and whose id the registry does not
    /// hold, is refused with the fault returned, as is one several serve; nothing starts then.
    /// </summary>
    private Dictionary<string, string>? ChildTargets(Transition t, out ActivityFault? refused)
    {
        refused = null;
        Dictionary<string, string>? targets = null;
        var named = (t.Triggers ?? []).OfType<ChildTrigger>().Select(child => child.Workflow)
            .Concat((t.Effects ?? []).OfType<DispatchEffect>().Select(dispatch => dispatch.Workflow));
        foreach (var id in named)
        {
            targets ??= new(StringComparer.Ordinal);
            if (targets.ContainsKey(id)) continue;
            if (options.ServedWorkflow is not { } served)
            {
                targets[id] = id;
                continue;
            }
            var answer = served(id);
            if (answer.Workflow is { } workflow) targets[id] = workflow;
            else if (answer.Several)
            {
                refused = new ActivityFault(AmbiguousFaultCode,
                    $"Definition '{id}' is published in several channels and none is 'default'. Publish it to the default channel.");
                return null;
            }
            else if (registry.Contains(id)) targets[id] = id;
            else
            {
                refused = new ActivityFault(UnpublishedFaultCode,
                    $"Dispatch Workflow names definition '{id}', which is not published on this host. Publish it, then run the workflow again.");
                return null;
            }
        }
        return targets;
    }

    private void Unregister(string id, WaitEntry wait, bool cancel)
    {
        switch (wait.Kind)
        {
            case "event": turn.Unsubscribe(wait.Key, id); break;
            case "job" when cancel: turn.CancelJob(wait.Key); break;
            case "child" when cancel:
                turn.CancelChild(wait);
                break;
            case "timer" when cancel && wait.Key.Length > 0: turn.Recall(wait.Key); break;      // its row goes with this commit
        }
    }

    private void Do(Frame frame, Effect effect, IReadOnlyDictionary<string, string>? children)
    {
        switch (effect)
        {
            case PublishEffect p:
                turn.Publish("event:" + p.Event, p.Payload?.ToJsonString(), options.EventWindow);
                break;
            case DispatchEffect d:
            {
                // A detached start (friction F-N5, fixed in the merge): no parent, and minted here, so no read.
                var child = turn.StartDetached(children![d.Workflow], d.Mode, d.Input as JsonObject);
                turn.Record($"d:{frame.Id}:{child.Id}");
                break;
            }
            case EmitEffect e:
                // The kernel's live output: it knows who waits (the reply addresses of the messages this instance
                // consumed), releases it after this turn's commit, and a request takes only its first response.
                turn.Emit(e.Channel, e.Item);
                break;
            case OutboundEffect o:
                // Round 5 C: the host sends it now, with the key the activity put in it. Nothing is recorded for it: the
                // key is derived from the node, iteration path and attempt the commit already carries, so a replay re-derives it.
                var path = IterationPath(frame);
                turn.SendOutbound(frame.Id, frame.Node, path, frame.Attempt, EffectKeyOf(frame, path), o);
                break;
        }
    }

    /// <summary>
    /// A graph Complete's exit: the leaf completes in this turn without telling its parent, so nothing after it runs,
    /// and its nearest enclosing boundary takes the exit, which ends the rest of its body in the same commit.
    /// </summary>
    private void Exit(Frame frame, ExitEffect exit)
    {
        var boundary = ParentOf(frame);
        while (boundary is not null && !typeof(IExitBoundary).IsAssignableFrom(_wf.Nodes[boundary.Node].Type)) boundary = ParentOf(boundary);
        if (boundary is null)
        {
            Fail(frame, new ActivityFault(GraphActivity.CompleteOutsideFaultCode, $"Complete node '{frame.Node}' has no enclosing GraphActivity."));
            return;
        }
        CloseInbox(frame.Id, "completed");
        _s.Frames.Remove(frame.Id);
        turn.Record($"c:{frame.Id}:{frame.Node}");
        var node = _wf.Nodes[boundary.Node];
        Guard(boundary, () => ((IExitBoundary)RestoreActivity(boundary, node)).OnExit(new CompositeContext(this, boundary, node), exit.Outcome));
    }

    internal void Complete(Frame frame, string outcome, JsonNode? result)
    {
        CloseInbox(frame.Id, "completed");
        _s.Frames.Remove(frame.Id);
        CancelChildren(frame.Id);
        DisposeLexicalScopes(frame.Id);
        if (result is not null) _s.Results[frame.Node] = result;
        foreach (var (variable, path) in _wf.Nodes[frame.Node].Captures) Assign(frame, variable, Values.Path(result, path)?.DeepClone());
        turn.Record($"c:{frame.Id}:{frame.Node}");
        NotifyParent(frame, outcome, result);
    }

    private void NotifyParent(Frame frame, string outcome, JsonNode? result)
    {
        if (frame.Parent is null)
        {
            Finish();
            return;
        }
        if (!_s.Frames.TryGetValue(frame.Parent, out var parent)) return;
        var node = _wf.Nodes[parent.Node];
        // A composite's inputs are read again for every callback, so a host expression among them can fail here too.
        Guard(parent, () =>
        {
            var composite = (CompositeActivity)RestoreActivity(parent, node);
            composite.OnChildCompleted(new CompositeContext(this, parent, node), new ChildCompleted(frame.Id, frame.Node, outcome, result, frame.Tag));
        });
    }

    /// <summary>A threshold join fired: record it once, complete the composite, cancel what still runs under it.</summary>
    internal void Join(Frame frame)
    {
        turn.Record($"j:{frame.Id}");
        if (!Has(NativeFlaw.JoinWithoutGuard))
        {
            Complete(frame, "Done", null);
            return;
        }
        turn.Record($"c:{frame.Id}:{frame.Node}");     // MUTATION: the parent hears of it, the frame and its branches live on
        NotifyParent(frame, "Done", null);
    }

    internal void RecordJoin(Frame frame, string node) => turn.Record($"j:{frame.Id}:{node}");

    internal void Record(string entry) => turn.Record(entry);

    // The job timeout a background activity takes from one of its inputs (BackgroundAttribute.TimeoutInput), else its default.
    private static TimeSpan? TimeoutInput(Node node, JsonObject inputs)
    {
        if (node.Type.GetCustomAttributes(typeof(BackgroundAttribute), false) is not [BackgroundAttribute { TimeoutInput: { } input } background]) return null;
        // One rule with the activity: milliseconds or a time span, at most a day; an unbound or refused one takes the default
        // (the activity faults on a refused one when its job runs).
        return ActivityTimeouts.TryParse(inputs[input], ActivityTimeouts.MaxTimeout, out var bound) && bound is { } given ? given
            : background.DefaultTimeoutMilliseconds > 0 ? TimeSpan.FromMilliseconds(background.DefaultTimeoutMilliseconds) : null;
    }

    internal void Fail(Frame frame, ActivityFault fault)
    {
        if (CaptureIntervention(frame, fault)) return;
        CloseInbox(frame.Id, "faulted");
        _s.Frames.Remove(frame.Id);
        CancelChildren(frame.Id);
        DisposeLexicalScopes(frame.Id);
        turn.Record($"f:{frame.Id}:{frame.Node}");
        if (frame.Parent is null || !_s.Frames.TryGetValue(frame.Parent, out var parent))
        {
            if (frame.Parent is null) End(Contract.ActorStatus.Faulted, fault.Code, Values.From(fault));
            return;
        }
        var node = _wf.Nodes[parent.Node];
        Guard(parent, () =>
        {
            var composite = (CompositeActivity)RestoreActivity(parent, node);
            composite.OnChildFaulted(new CompositeContext(this, parent, node), new ChildFaulted(frame.Id, frame.Node, fault, frame.Tag));
        });
    }

    internal string Schedule(Frame parent, Node child, string? tag, IReadOnlyDictionary<string, JsonNode?>? scope, string? lexicalScope = null)
    {
        if (lexicalScope is not null) _ = LexicalScopes.Accessible(_s, parent.Id, lexicalScope);
        var work = new Work { Frame = NewId("f"), Node = child.Id, Parent = parent.Id, Tag = tag, LexicalScope = lexicalScope };
        if (scope is not null)
        {
            if (Has(NativeFlaw.ScopeInMemory)) work.LiveScope = new(scope);     // MUTATION: the iteration scope is not in the row
            else work.Scope = new(scope);
        }
        _s.Agenda.Add(work);
        return work.Frame;
    }

    /// <summary>The children of a frame that run or are scheduled, with the tags they were scheduled with.</summary>
    internal IReadOnlyList<(string Frame, string? Tag)> RunningWithTags(string frame) =>
        [.. _s.Frames.Values.Where(f => f.Parent == frame).Select(f => (f.Id, f.Tag)), .. _s.Agenda.Where(w => !w.Resume && !w.Gone && w.Parent == frame).Select(w => (w.Frame, w.Tag))];

    /// <summary>The node whose start trigger is still to be delivered, if any.</summary>
    internal string? PendingTrigger => _s.Trigger is null ? null : _s.TriggerNode;

    internal IReadOnlyList<string> Running(string frame) =>
        [.. _s.Frames.Values.Where(f => f.Parent == frame).Select(f => f.Id), .. _s.Agenda.Where(w => !w.Resume && !w.Gone && w.Parent == frame).Select(w => w.Frame)];

    internal void Cancel(string frame)
    {
        if (_s.Frames.Remove(frame, out var f))
        {
            CloseInbox(frame, "cancelled");
            foreach (var (id, wait) in _s.Waits.Where(w => w.Value.Frame == frame).ToList())
            {
                _s.Waits.Remove(id);
                Unregister(id, wait, cancel: true);
            }
            CancelChildren(frame);
            DisposeLexicalScopes(frame);
            turn.Record($"x:{f.Id}:{f.Node}");
        }
        _s.Agenda.RemoveAll(w => !w.Resume && w.Frame == frame);
    }

    private void CancelChildren(string frame)
    {
        foreach (var child in _s.Frames.Values.Where(f => f.Parent == frame).Select(f => f.Id).ToList()) Cancel(child);
        _s.Agenda.RemoveAll(w => !w.Resume && w.Parent == frame);
    }

    private void CancelWorkflow()
    {
        foreach (var frame in _s.Frames.Values.Where(f => f.Parent is null).Select(f => f.Id).ToList()) Cancel(frame);
        End(Contract.ActorStatus.Cancelled, "cancelled", null);
    }

    private void Finish() => End(Contract.ActorStatus.Finished, null, _s.Variables.GetValueOrDefault("output"));

    private void End(Contract.ActorStatus status, string? subStatus, JsonNode? output)
    {
        foreach (var frame in _s.Frames.Keys.ToList()) Cancel(frame);
        _s.Agenda.Clear();
        _status = status;
        turn.SetStatus(status, subStatus, output);
    }

    // ---- variables and bindings --------------------------------------------------------------------------------------

    internal JsonNode? Evaluate(Binding binding, IScope scope) => binding switch
    {
        LiteralBinding l => l.Value?.DeepClone(),
        VariableBinding v => Values.Path(scope.Variable(v.Name), v.Path)?.DeepClone(),
        InstanceBinding { Field: InstanceField.Id } => JsonValue.Create(turn.Actor.Id),
        InstanceBinding { Field: InstanceField.CorrelationId } => _s.CorrelationId is { } correlation ? JsonValue.Create(correlation) : null,
        HostExpressionBinding h => EvaluateHost(h, scope),
        ResultBinding r => Values.Path(_s.Results.GetValueOrDefault(r.Node), r.Path)?.DeepClone(),
        ExpressionBinding e => Expression.Evaluate(e.Text, scope),
        _ => throw new InvalidOperationException(binding.ToString()),
    };

    private Frame? ParentOf(Frame frame) => frame.Parent is { } id ? _s.Frames.GetValueOrDefault(id) : null;

    internal JsonNode? Read(Frame? frame, string name)
    {
        foreach (var scope in VisibleScopes(frame))
            if (scope.TryGetValue(name, out var value)) return value;
        // A GraphActivity's body sees only what its boundary declares, as the old core's closed graph scope does.
        return BodyScope(frame) is not null ? null : _s.Variables.GetValueOrDefault(name);
    }

    internal JsonNode? Result(string node) => _s.Results.GetValueOrDefault(node);

    /// <summary>
    /// A write goes to the nearest scope that declares the variable, else to the workflow's; inside a GraphActivity's body,
    /// else to the body's own variables, so that nothing outside the boundary changes.
    /// </summary>
    private void Assign(Frame frame, string name, JsonNode? value)
    {
        foreach (var scope in VisibleScopes(frame))
            if (scope.ContainsKey(name)) { scope[name] = value; return; }
        (BodyScope(frame) ?? _s.Variables)[name] = value;
    }
}

internal sealed class FrameScope(Burst burst, Frame frame) : IScope, IVisibleScope
{
    public IReadOnlyDictionary<string, JsonNode?> Visible() => burst.VisibleVariables(frame);
    public JsonNode? Variable(string name) => burst.Read(frame, name);
    public JsonNode? Result(string node) => burst.Result(node);
}

/// <summary>What a composite sees: its children, its private state, and the structural requests it may make.</summary>
public sealed partial class CompositeContext
{
    private readonly Burst _burst;
    private readonly Frame _frame;

    internal CompositeContext(Burst burst, Frame frame, Node node) => (_burst, _frame, Node) = (burst, frame, node);

    public Node Node { get; }
    public string FrameId => _frame.Id;
    public bool HasExactIdentity => _burst.HasExactIdentity;
    public IReadOnlyList<Node> Children => Node.Children;
    public IReadOnlyList<string> Running => _burst.Running(_frame.Id);
    internal bool Flawed(NativeFlaw flaw) => _burst.Flawed(flaw);

    public T GetState<T>() where T : new() => Values.To<T>(_frame.State) ?? new T();
    /// <summary>The private state, or null before the first <see cref="SetState{T}"/>.</summary>
    public T? ReadState<T>() where T : class => Values.To<T>(_frame.State);
    /// <summary>The children that run or are scheduled, with their tags.</summary>
    public IReadOnlyList<(string Frame, string? Tag)> RunningWithTags => _burst.RunningWithTags(_frame.Id);
    /// <summary>The node whose start trigger (an HTTP request, an event) is still to be delivered, if any.</summary>
    public string? PendingTrigger => _burst.PendingTrigger;
    public void SetState<T>(T state) => _frame.State = Values.From(state);
    /// <summary>A binding of this node that is no input, evaluated where the node runs, when the composite needs it (a Switch case's condition).</summary>
    internal JsonNode? Evaluate(Binding binding) => _burst.Evaluate(binding, new FrameScope(_burst, _frame));

    public string Schedule(Node child, string? tag = null, IReadOnlyDictionary<string, JsonNode?>? scope = null) => _burst.Schedule(_frame, child, tag, scope);
    public void Complete(string outcome = "Done", JsonNode? result = null) => _burst.Complete(_frame, outcome, result);
    public void Join() => _burst.Join(_frame);
    public void RecordJoin(string node) => _burst.RecordJoin(_frame, node);
    public void Record(string entry) => _burst.Record(entry);
    public void Fault(ActivityFault fault) => _burst.Fail(_frame, fault);
    public void Cancel(string childFrame) => _burst.Cancel(childFrame);
}
