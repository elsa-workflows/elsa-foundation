namespace Kernel.Review5;

/// <summary>
/// The rule sets of JR-1 (R5-M4), two readings of the reviewed design and the repair:
/// <c>Reviewed</c>: Park keeps the consumed delivery by adding Done and KeptBy, so the kept row is the ticket itself;
/// <c>ReviewedKind</c>: the kept row also persists the changed kind, JobFinished under the ticket's own key;
/// <c>Repaired</c>: a fenced conversion consumes the ticket and inserts a result row under a reserved-scope identity;
/// <c>Negative</c>: the conversion with that identity removed, an ordinary-scope result row;
/// <c>GenerationBlind</c>: the repaired set with the generation check removed from the authentication and the Open.
/// </summary>
enum JrRules { Reviewed, ReviewedKind, Repaired, Negative, GenerationBlind }

/// <summary>
/// JR-1: an in-process job J completes while its instance S is parked; Park, takeover, recover. The model is the real
/// <see cref="NativeModel"/>, whose real start turn arms the Background node <c>c</c> (NativeModel.cs 347–355) and whose
/// real result turn resumes it (412–416); the job body is the real <c>NativeModel.ExecuteJobAsync</c> (186–197) running
/// the harness-authored <see cref="EffectEcho"/>, which counts physical invocations. Durable rows live in the real
/// <see cref="InMemoryStore"/>: every turn, the conversion and the parked consumption go through its fenced, count-checked
/// <c>CommitAsync</c>, takeovers through <c>TakeAsync</c>, reads through <c>PollAsync</c>. The built check that refuses a
/// supplied completion is the real <c>Delivery.DiscardedAt</c> (Turns.cs 109–112) over a real <c>Slot</c>. Park, the
/// kept reasons, the recover page and the job generation are the proposed transitions, applied by the fixture.
/// </summary>
sealed class Jr1(string variant, JrRules rules)
{
    /// <summary>Proposed: the reserved scope of a kernel-issued job result row (2^62; only the kernel writes it).</summary>
    public const long ResultScope = 1L << 62;
    private static readonly InstanceRef Self = new("0/subject");
    private const long Generation = 1;                 // proposed JobGeneration of J's ticket

    public static readonly string[] Variants = ["crash-before", "crash-after", "crash-after-keep", "running", "obsolete"];
    private bool Converts => rules is JrRules.Repaired or JrRules.Negative or JrRules.GenerationBlind;

    private long _now = 100;
    private InMemoryStore _store = null!;
    private Lease _lease = null!;
    private readonly TimeSpan _leaseFor = TimeSpan.FromMilliseconds(10);
    private NativeModel _model = null!;
    private InstanceInfo _info = null!;
    private InstanceRow _row = null!;
    private readonly Dictionary<string, long> _registry = new(StringComparer.Ordinal);     // proposed: job key -> generation
    private readonly Dictionary<InboxKey, string> _keptBy = [];                             // proposed KeptBy column
    private readonly HashSet<InboxKey> _authenticInMemory = [];                             // the owner's in-memory CompletedJob provenance
    private Report _report = null!;
    private string _jobKey = "", _arguments = "", _effectKey = "";
    private InboxKey _ticket;
    private readonly List<string> _results = [];
    private int _resultTurns;

    private sealed record HarnessJob(InstanceRef Instance, string Key, string? Arguments, long Creation) : IJob;
    private sealed record JobTicketPayload(string Model, string Key, string? Arguments);   // the shape of Worker.cs 1885's JobTicket

    public async Task<Report> RunAsync()
    {
        _report = new Report($"JR-1/{variant}", Program.Name(rules), variant == "running"
            ? "control: a job that completes while its instance runs reaches the model once, as built"
            : "a job result that completes while its instance is parked reaches the model once after takeover and recover, with its original value, and the job runs again only if the crash preceded the conversion commit");
        EffectEcho.Invocations = 0;
        _store = new InMemoryStore(1, () => _now);
        _lease = await Real.TakeAsync(_store, 0, "owner-1", _leaseFor) ?? throw new InvalidOperationException("harness schedule: no lease");
        var wf = Wf.Workflow("job", Wf.Sequence("root",
            Wf.Set("s1", "svc", Binding.Literal("alpha")),
            Wf.Parallel("p", null, Wf.Node<EffectEcho>("c", ("Service", Binding.Var("svc"))), Wf.Set("s2", "svc", Binding.Literal("beta")))));
        _model = new NativeModel(new WorkflowRegistry().Add(wf));
        _info = new InstanceInfo(Self, NativeModel.ModelKey, "job", null, 1);

        // 1. The real start turn arms c as a background job; its ticket and the row commit together (Turns.cs 394–403).
        var spec = NativeModel.Spec("job");
        var start = new NativeHost(_info, _now, 1) { Inbox = [new Message("start", Self, Kinds.Start, Start: spec)], State = _model.CreateState(spec) };
        await _model.TurnAsync(start, CancellationToken.None); Real.CountModelTurn();
        var (key, arguments) = start.Jobs.Single();
        (_jobKey, _arguments) = (key, Json.Write(arguments));
        _effectKey = Json.Read<BackgroundArgs>(_arguments).EffectKey!;
        _ticket = Real.Ticket(Self.Id, _jobKey, 1);
        var ticket = new Message(_ticket.Id, Self, Kinds.Job, Json.Write(new JobTicketPayload(NativeModel.ModelKey, _jobKey, _arguments)), Self.Id,
            TargetCreation: 1, SenderCreation: 1).WithInboxScope(_ticket.Scope);
        _row = new InstanceRow(Self.Id, NativeModel.ModelKey, "job", InstanceStatus.Running, Json.Write(start.State), 1, null,
            Jobs: DurableKeyJson.WriteKeys([_jobKey]), Creation: 1, StartIntent: "harness-intent");
        await Commit(new TurnCommit(Self.Id, _row, null, [], [], [ticket], [], [], null));
        _registry[_jobKey] = Generation;
        _report.Line($"start: real Native turn armed c as job {_jobKey} with Service = alpha and effect key {_effectKey}; ticket {_ticket.Id} committed with the row; registry {_jobKey} -> g{Generation}");

        // 2. The owner enters the job (Worker.cs 1166–1203): physical invocation 1 begins. While it runs, S parks.
        var running = variant == "running";
        if (!running)
        {
            await Park("an authored fault in another branch parks S while J runs");
            if (variant == "obsolete") await StaleResult();
        }

        // 3. J's body finishes: the real background activity returns its result (Worker.cs 1238–1252 publishes it).
        var completion = await RunJob(ticket);

        if (running)
        {
            // The built path: the completion replaces the ticket in memory (Worker.cs 1297–1306), and the next model turn
            // consumes the ticket's key with the result. Repaired: the conversion rides in that turn's commit.
            await ResultTurn(completion, consumed: _ticket, "the running instance's next turn takes the in-memory completion; its commit consumes the ticket");
            Finish(allowed: 1);
            return _report;
        }

        var crashBefore = variant == "crash-before";
        Message? result = null;
        if (!crashBefore)
        {
            if (Converts)
            {
                // 4a. The conversion: a standalone fenced write consumes the ticket and inserts the result row (proposed).
                result = rules != JrRules.Negative
                    ? new Message(ResultId(_ticket, Generation), Self, Kinds.JobFinished, completion.Payload, Self.Id, TargetCreation: 1, SenderCreation: 1).WithInboxScope(ResultScope)
                    : new Message(_ticket.Id + ".result", Self, Kinds.JobFinished, completion.Payload, Self.Id, TargetCreation: 1, SenderCreation: 1);
                await Commit(new TurnCommit(Self.Id, null, null, [_ticket], [], [result], [], [], null));
                _authenticInMemory.Add(result.InboxKey);
                _report.Line($"conversion: one fenced commit consumes ticket {_ticket.Id} (counted) and inserts result row {result.Id} (scope {(rules != JrRules.Negative ? "2^62, reserved" : "0, ordinary")})");
                if (variant == "crash-after") await Takeover("after the conversion commit, before the parked instance consumed the result");
                await ParkedConsume(result);
            }
            else
            {
                // 4b. Reviewed: the completion stays in memory (CompletedJob); the parked instance consumes it, and Park keeps the
                // consumed row: the ticket's own key, with Done and KeptBy, and in the second reading its changed kind.
                _authenticInMemory.Add(_ticket);
                await ParkedConsume(completion);
                if (rules == JrRules.ReviewedKind)
                {
                    _store.Messages[_ticket].Message = completion;    // fixture write of the reviewed proposal: the kept row persists the result kind
                    _report.Line($"the kept row {_ticket.Id} now holds kind {Kinds.JobFinished} and the result payload under the ticket's key");
                }
                if (variant == "crash-after") await Takeover("after the parked consumption kept the result (the reviewed design's first durable result)");
            }
            if (variant is "crash-after-keep" or "obsolete") await Takeover("after the parked consumption kept the result");
        }
        else
        {
            await Takeover("after J's body returned and before any commit recorded its result");
            // The new owner polls the undone ticket. S is parked, so the built StartJob does not enter it (Worker.cs 1169–1177)
            // and dispatch hands it to the parked instance as late input; Park keeps it Arrived (proposed).
            var polled = await Real.PollAsync(_store, 0, _lease.Epoch, 500);
            var stale = polled.Messages.Single(m => m.Message.InboxKey == _ticket).Message;
            _report.Line($"the new owner polls ticket {stale.Id}: S is {_row.Status}, so the built StartJob does not enter it");
            await ParkedConsume(stale);
        }

        // 5. Recover (resume): Open restores the Arrived rows of S whose job generation is current (proposed).
        await Recover();
        // 6. The owner polls the restored rows and acts on them.
        await AfterRecover();
        Finish(allowed: crashBefore ? 2 : 1);
        if (crashBefore && rules == JrRules.Repaired && _resultTurns == 0)
            _report.Stop("the brief's 'before the conversion commits: at-least-once, so a crash re-runs the job' does not hold for a parked instance: the new " +
                "owner does not enter the ticket of a parked row (Worker.cs 1169-1177), dispatch hands it to the parked instance as late input, the built " +
                "RemainingJobs drops J's key from the registry when that consumption takes the durable ticket (WorkflowInstance.cs 78-90, 135-144), and the " +
                "recover Open then clears a kept job row whose key the registry no longer holds (observability.md 1336-1343). J never runs again and its " +
                "result never reaches the model. Recommendation in the README.");
        return _report;
    }

    /// <summary>
    /// The obsolete-generation variant: a reserved-scope result row of J's generation 0, left kept Arrived by an earlier Park
    /// with another value (fixture setup: inserted by a commit, then kept), sits beside the current generation's.
    /// </summary>
    private async Task StaleResult()
    {
        var stale = new Message(ResultId(_ticket, 0), Self, Kinds.JobFinished,
            Json.Write(new JobFinished(_jobKey, Json.Write(new BackgroundResult(TransitionKind.Complete, System.Text.Json.Nodes.JsonValue.Create("stale"), "Done", null)), null)),
            Self.Id, TargetCreation: 1, SenderCreation: 1).WithInboxScope(ResultScope);
        await Commit(new TurnCommit(Self.Id, null, null, [], [], [stale], [], [], null));
        await Commit(new TurnCommit(Self.Id, null, null, [], [stale.InboxKey], [], [], [], null));
        _keptBy[stale.InboxKey] = "Arrived";
        _report.Line($"a result row of J's obsolete generation g0 ({stale.Id}, value 'stale') is kept Arrived from an earlier Park; the registry holds {_jobKey} -> g{Generation}");
    }

    /// <summary>A kept or restored job row's key and generation: a ticket is of the current ticket's generation; a result row's is in its id.</summary>
    private (string Key, long Generation) JobOf(Message m)
    {
        if (m.Kind == Kinds.Job) return (m.Read<JobTicketPayload>().Key, Generation);
        var key = m.Read<JobFinished>().Key;
        var at = m.Id.LastIndexOf("!result!g", StringComparison.Ordinal);
        return (key, at >= 0 && m.InboxScope == ResultScope ? long.Parse(m.Id[(at + 9)..], System.Globalization.CultureInfo.InvariantCulture) : Generation);
    }

    private static string ResultId(InboxKey ticket, long generation) => FormattableString.Invariant($"{ticket.Id}!result!g{generation}");

    /// <summary>Proposed authentication of a result row: the reserved scope, the id derived from S's ticket, a current generation.</summary>
    private bool AuthenticResult(Message m) =>
        m.Kind == Kinds.JobFinished && m.InboxScope == ResultScope && m.Read<JobFinished>() is { } finished &&
        _registry.TryGetValue(finished.Key, out var g) && m.Id == ResultId(Real.Ticket(m.Target.Id, finished.Key, m.TargetCreation ?? 0), rules == JrRules.GenerationBlind ? JobOf(m).Generation : g);

    private async Task<Message> RunJob(Message ticket)
    {
        var payload = ticket.Read<JobTicketPayload>();
        var value = await _model.ExecuteJobAsync(new HarnessJob(Self, payload.Key, payload.Arguments, 1), CancellationToken.None);
        Real.Count("NativeModel.ExecuteJobAsync");
        var completed = ticket with { Kind = Kinds.JobFinished, Payload = Json.Write(new JobFinished(payload.Key, Json.Write(value), null)) };
        _report.Line($"J's body ran (physical invocation {EffectEcho.Invocations}): {((BackgroundResult)value!).Result}");
        return completed;
    }

    private async Task Park(string why)
    {
        var x = new Message("fault-input", Self, NativeKinds.Resume, Json.Write(new ResumeStimulus("boom")), "0/other", TargetCreation: 1);
        await Real.AddAsync(_store, x);
        // WorkflowInstance.Park (120–132), mirrored: Faulted, the waits removed, the job registry kept (RemainingJobs keeps J,
        // whose ticket the parking turn did not take). Proposed: the consumed input is kept with KeptBy = Consumed.
        var next = _row with { Status = InstanceStatus.Faulted, Revision = _row.Revision + 1, Fault = "parked: " + why };
        await Commit(new TurnCommit(Self.Id, next, _row.Revision, [], [x.InboxKey], [], [], [], null, Unsubscribe: true));
        _row = next;
        _keptBy[x.InboxKey] = "Consumed";
        _report.Line($"park: {why}; S Faulted at revision {_row.Revision}, fault-input kept Consumed, registry still {_jobKey} -> g{_registry[_jobKey]}");
    }

    /// <summary>
    /// The parked instance consumes late input with no model turn (WorkflowInstance.cs 78–90, mirrored), and Park keeps it
    /// Arrived (proposed). A delivery read from a row that the built check classifies as a supplied completion, and that
    /// carries no authentication the rule set accepts, is consumed with no turn and not kept (Worker.cs 1030–1037).
    /// </summary>
    private async Task ParkedConsume(Message m)
    {
        if (!Accepted(m, out var why))
        {
            await Commit(new TurnCommit(Self.Id, null, null, [m.InboxKey], [], [], [], [], null));
            _report.Line($"the parked instance's owner refuses {m.Id}: {why}; it is consumed with no turn and not kept");
            return;
        }
        // RemainingJobs (WorkflowInstance.cs 135-144, called at 82): a consumed durable ticket, or this worker's completion, whose
        // full key is J's ticket key takes J's key out of the row's registry.
        var (key, _) = JobOf(m);
        var drops = (m.Kind == Kinds.Job && m.InboxScope > 0 || _authenticInMemory.Contains(m.InboxKey)) && m.InboxKey == Real.Ticket(Self.Id, key, 1) && _registry.ContainsKey(key);
        var next = drops ? _row with { Revision = _row.Revision + 1, Jobs = null } : null;
        await Commit(new TurnCommit(Self.Id, next, next is null ? null : _row.Revision, [], [m.InboxKey], [], [], [], null));
        if (next is not null) { _row = next; _registry.Remove(key); }
        _keptBy[m.InboxKey] = "Arrived";
        _report.Line($"the parked instance consumes {m.Id} ({m.Kind}) as late input: kept Arrived ({why}){(drops ? $"; the built RemainingJobs drops {key} from the registry" : "")}");
    }

    /// <summary>Whether the owner may give this delivery to the instance, and why.</summary>
    private bool Accepted(Message m, out string why)
    {
        if (m.Kind != Kinds.JobFinished) { why = "a ticket"; return true; }
        if (_authenticInMemory.Contains(m.InboxKey)) { why = "this owner's in-memory completion (CompletedJob)"; return true; }
        var builtRefuses = Real.DiscardedAt(m, _row);
        if (rules is JrRules.Repaired or JrRules.GenerationBlind && AuthenticResult(m))
        { why = $"reserved-scope result of generation g{JobOf(m).Generation}, the registry's g{_registry[_jobKey]} (the built DiscardedAt alone {(builtRefuses ? "refuses it" : "admits it")})"; return true; }
        why = builtRefuses ? "the real DiscardedAt classifies it as a supplied completion" : "admitted";
        return !builtRefuses;
    }

    private async Task Takeover(string when)
    {
        _now += 20;     // past the 10 ms lease
        _authenticInMemory.Clear();
        _lease = await Real.TakeAsync(_store, 0, "owner-2-" + _now, _leaseFor) ?? throw new InvalidOperationException("harness schedule: the takeover failed");
        _report.Line($"crash takeover {when}: another process takes epoch {_lease.Epoch} at {_now}; the in-memory completion and its provenance are lost");
    }

    /// <summary>
    /// The recover Open and its page (proposed; observability.md, "Restore or clear, per row", and R4-M3): S runs again, and
    /// each kept Arrived row of S is restored (Done = false, KeptBy = null, Seen = 0, Attempts = 0, Trial none) unless it is
    /// a job row whose key the registry no longer holds or whose generation is not the registry's current one, which is
    /// cleared. The row updates are fixture writes of the proposed page's server-side update.
    /// </summary>
    private async Task Recover()
    {
        var next = _row with { Status = InstanceStatus.Running, Fault = null, Revision = _row.Revision + 1 };
        await Commit(new TurnCommit(Self.Id, next, _row.Revision, [], [], [], [], [], null));
        _row = next;
        _report.Line($"recover: S runs again (revision {_row.Revision})");
        foreach (var (key, reason) in _keptBy.ToList())
        {
            if (reason != "Arrived" || !_store.Messages.TryGetValue(key, out var row)) { _keptBy.Remove(key); continue; }
            _keptBy.Remove(key);
            if (row.Message.Kind is Kinds.Job or Kinds.JobFinished)
            {
                var (job, generation) = JobOf(row.Message);
                var current = _registry.TryGetValue(job, out var g) ? g : (long?)null;
                if (current is null || generation != current && rules != JrRules.GenerationBlind)
                {
                    _report.Line($"recover: the page clears {key.Id} ({row.Message.Kind}): {(current is null ? $"the registry no longer holds {job}" : $"generation g{generation} is not the registry's g{current}")}");
                    continue;
                }
            }
            row.Done = false; row.Seen = 0; row.Attempts = 0; row.Trial = 0;
            _report.Line($"recover: the page restores {key.Id} ({row.Message.Kind})");
        }
    }

    private async Task AfterRecover()
    {
        var polled = await Real.PollAsync(_store, 0, _lease.Epoch, 500);
        foreach (var stored in polled.Messages.Where(m => m.Message.Target == Self).ToList())
        {
            var m = stored.Message;
            if (m.Kind == Kinds.Job && m.InboxScope > 0)
            {
                // Hold (Worker.cs 654–658, 677) makes an undone kernel.job row a ticket and StartJob enters it: physical entry.
                _report.Line($"the owner polls {m.Id} ({Kinds.Job}): a ticket, which the built Hold and StartJob enter again");
                var completion = await RunJob(m);
                await ResultTurn(completion, consumed: m.InboxKey, "the completion reaches the running instance's next turn, as built");
            }
            else if (m.Kind == Kinds.JobFinished)
            {
                if (!Accepted(m, out var why))
                {
                    await Commit(new TurnCommit(Self.Id, null, null, [m.InboxKey], [], [], [], [], null));
                    _report.Line($"the owner polls {m.Id}: {why}; consumed with no turn (Worker.cs 1030–1037): the result is lost");
                    continue;
                }
                await ResultTurn(m, consumed: m.InboxKey, $"the owner polls {m.Id} and gives it to the model ({why})");
            }
        }
    }

    /// <summary>One real Native turn with the result; its commit consumes the row and writes the instance row.</summary>
    private async Task ResultTurn(Message result, InboxKey consumed, string why)
    {
        var host = new NativeHost(_info, _now, _row.Revision + 1) { Inbox = [result], State = Json.Read<NativeState>(_row.State) };
        await _model.TurnAsync(host, CancellationToken.None); Real.CountModelTurn();
        var finished = result.Read<JobFinished>();
        _results.Add(Json.Read<BackgroundResult>(finished.Result!).Result?.ToString() ?? "null");
        _resultTurns += host.Records.Count(r => r.StartsWith("c:", StringComparison.Ordinal) && r.EndsWith(":c", StringComparison.Ordinal));
        _registry.Remove(finished.Key);
        var next = _row with { Status = host.EndStatus ?? InstanceStatus.Running, State = Json.Write(host.State), Revision = _row.Revision + 1, Jobs = null };
        await Commit(new TurnCommit(Self.Id, next, _row.Revision, [consumed], [], [], [], [], null));
        _row = next;
        _report.Line($"{why}: real Native turn resumes c with {_results[^1]}; S {_row.Status}");
    }

    private void Finish(int allowed)
    {
        _report.Line($"physical invocations {EffectEcho.Invocations} (allowed {allowed}); model results [{string.Join(", ", _results)}]; S {_row.Status}");
        _report.Expect("model results (c completed by a result turn)", _resultTurns, 1);
        _report.Expect("the result value (the arm-time input and the original effect key)", _results.Count == 1 ? _results[0] : string.Join(",", _results), $"alpha|{_effectKey}");
        _report.Check(EffectEcho.Invocations <= allowed && EffectEcho.Invocations >= 1,
            $"physical invocations: {EffectEcho.Invocations} (at-least-once allowance: {allowed}{(allowed == 2 ? ", one re-run since the crash preceded the conversion commit" : ", no re-run after the result committed")})");
        _report.Expect("S finished", _row.Status, InstanceStatus.Finished);
        _report.Expect("rows of S left in the store", _store.Messages.Values.Count(m => m.Message.Target == Self && !m.Done), 0);
    }

    private async Task Commit(TurnCommit turn)
    {
        var committed = await Real.CommitAsync(_store, new CommitBatch(0, _lease.Epoch, _lease.Position, _leaseFor, false, [turn], [], []));
        if (!committed.Landed) throw new InvalidOperationException("harness schedule: a commit did not land");
    }
}
