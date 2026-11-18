namespace Kernel.Review5;

/// <summary>
/// The rule sets of JR-1 (R5-M4), two readings of the reviewed design and the repair:
/// <c>Reviewed</c>: Park keeps the consumed delivery by adding Done and KeptBy, so the kept row is the ticket itself;
/// <c>ReviewedKind</c>: the kept row also persists the changed kind, JobFinished under the ticket's own key;
/// <c>Repaired</c>: a fenced conversion consumes the ticket and inserts a result row under the kernel-issued identity of the transitions page (the ticket's full key plus /result, in the ticket's own scope);
/// <c>Negative</c>: the conversion with that identity removed, an ordinary-scope result row;
/// <c>GenerationBlind</c>: the repaired set with the generation check removed from the authentication and the Open;
/// <c>ReviewedKeepsRegistry</c>: a third reading of the reviewed design, in which the parked consumption keeps J's key, so
/// the kept ticket is restored and re-entered. The repaired set also keeps the registry entry of an undone current-generation
/// ticket that a parked instance consumes, and recover restarts it from its JobSpec under the next generation (the
/// coordinator's decision of 18 November); in <c>crash-before</c> the negative removes that rule.
/// </summary>
/// <c>TicketOnlyLookup</c>: the repaired set with the Open's disposition lookup probing the ticket only, never its result row.
enum JrRules { Reviewed, ReviewedKind, Repaired, Negative, GenerationBlind, ReviewedKeepsRegistry, TicketOnlyLookup }

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
    /// <summary>
    /// The transitions page's identities (proposed): J's ticket of generation g is <c>{origin}!{len}_{key}#g{g}</c> (generation 0:
    /// the built <c>Worker.TicketId</c>, Worker.cs 295), and its converted result row is the ticket's full key plus
    /// <c>/result</c>, in the ticket's own scope (origin length + 1, which only the kernel issues).
    /// </summary>
    private InboxKey TicketKey(long generation) =>
        generation == 0 ? _builtTicket : new InboxKey(FormattableString.Invariant($"{_builtTicket.Id}#g{generation}"), _builtTicket.Scope);
    private InboxKey _builtTicket;
    private static readonly InstanceRef Self = new("0/subject");
    private const long Generation = 1;                 // proposed JobGeneration of J's ticket

    public static readonly string[] Variants = ["crash-before", "crash-after", "crash-after-keep", "running", "obsolete", "result-parked-consume", "result-kept-lookup", "result-pending-lookup"];
    /// <summary>
    /// The lookup variants (the integration review's trace): every rule set converts under the kernel-issued identity and
    /// keeps J's registry entry, so they isolate the Open's disposition lookup. Reviewed applies the stated table without the
    /// result probe; repaired probes <c>{ticket id}/result</c>.
    /// </summary>
    private bool LookupCase => variant is "result-kept-lookup" or "result-pending-lookup";
    /// <summary>The Open's lookup also probes the minted result row (the coordinator's rule after the integration review).</summary>
    private bool ProbesResult => rules is JrRules.Repaired or JrRules.GenerationBlind;
    private string? _resultSource;
    /// <summary>
    /// <c>result-parked-consume</c> isolates the registry rule for a converted result: every rule set converts with the
    /// kernel-issued identity; reviewed and negative clear J's entry when the parked instance consumes its result row (the
    /// transitions page's rule before the coordinator's fix), repaired keeps it.
    /// </summary>
    private bool ResultCase => variant == "result-parked-consume";
    private bool Converts => rules is JrRules.Repaired or JrRules.Negative or JrRules.GenerationBlind || ResultCase || LookupCase;
    private bool ReservedIdentity => rules != JrRules.Negative || ResultCase || LookupCase;
    private readonly HashSet<InboxKey> _restored = [];
    private string? _restartedId;
    private bool KeepsUndoneTicket => rules is JrRules.Repaired or JrRules.GenerationBlind || rules == JrRules.Negative && variant != "crash-before";

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
        _builtTicket = Real.Ticket(Self.Id, _jobKey, 1);
        _ticket = TicketKey(Generation);
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
                result = ReservedIdentity
                    ? new Message(ResultId(Generation), Self, Kinds.JobFinished, completion.Payload, Self.Id, TargetCreation: 1, SenderCreation: 1).WithInboxScope(_ticket.Scope)
                    : new Message(_ticket.Id + ".result", Self, Kinds.JobFinished, completion.Payload, Self.Id, TargetCreation: 1, SenderCreation: 1);
                await Commit(new TurnCommit(Self.Id, null, null, [_ticket], [], [result], [], [], null));
                _authenticInMemory.Add(result.InboxKey);
                _report.Line($"conversion: one fenced commit consumes ticket {_ticket.Id} (counted) and inserts result row {result.Id} (scope {(ReservedIdentity ? $"{_ticket.Scope}, the ticket's own" : "0, ordinary")})");
                if (variant == "crash-after") await Takeover("after the conversion commit, before the parked instance consumed the result");
                if (variant == "result-pending-lookup") await Takeover("after the conversion commit; the result row is still pending at the Open");
                else await ParkedConsume(result);
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
            if (variant is "crash-after-keep" or "obsolete" or "result-kept-lookup") await Takeover("after the parked consumption kept the result");
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
        if (LookupCase)
        {
            _report.Expect("Restarted(g2) written by the Open", _restartedId ?? "none", "none");
            _report.Expect("the row whose result reached the model", _resultSource ?? "none", ResultId(Generation));
        }
        if (crashBefore && KeepsUndoneTicket)
            _report.Expect("the g1 ticket never restored beside its g2 restart", $"g1 restored {_restored.Contains(_ticket)}, g2 written {_restartedId is not null}", "g1 restored False, g2 written True");
        return _report;
    }

    /// <summary>
    /// The obsolete-generation variant: a kernel-issued result row of J's generation 0, left kept Arrived by an earlier Park
    /// with another value (fixture setup: inserted by a commit, then kept), sits beside the current generation's.
    /// </summary>
    private async Task StaleResult()
    {
        var stale = new Message(ResultId(0), Self, Kinds.JobFinished,
            Json.Write(new JobFinished(_jobKey, Json.Write(new BackgroundResult(TransitionKind.Complete, System.Text.Json.Nodes.JsonValue.Create("stale"), "Done", null)), null)),
            Self.Id, TargetCreation: 1, SenderCreation: 1).WithInboxScope(_ticket.Scope);
        await Commit(new TurnCommit(Self.Id, null, null, [], [], [stale], [], [], null));
        await Commit(new TurnCommit(Self.Id, null, null, [], [stale.InboxKey], [], [], [], null));
        _keptBy[stale.InboxKey] = "Arrived";
        _report.Line($"a result row of J's obsolete generation g0 ({stale.Id}, value 'stale') is kept Arrived from an earlier Park; the registry holds {_jobKey} -> g{Generation}");
    }

    /// <summary>A kept or restored job row's key and generation: a ticket is of the current ticket's generation; a result row's is in its id.</summary>
    private (string Key, long Generation) JobOf(Message m)
    {
        var key = m.Kind == Kinds.Job ? m.Read<JobTicketPayload>().Key : m.Read<JobFinished>().Key;
        if (m.InboxScope != _ticket.Scope) return (key, Generation);     // an ordinary-scope row carries no generation (negative)
        var id = m.Kind == Kinds.JobFinished && m.Id.EndsWith("/result", StringComparison.Ordinal) ? m.Id[..^"/result".Length] : m.Id;
        var at = id.LastIndexOf("#g", StringComparison.Ordinal);
        return (key, at >= 0 ? long.Parse(id[(at + 2)..], System.Globalization.CultureInfo.InvariantCulture) : 0);
    }

    private string ResultId(long generation) => TicketKey(generation).Id + "/result";

    /// <summary>Proposed authentication of a result row: the ticket's own scope, the id derived from J's ticket, the registry's current generation.</summary>
    private bool AuthenticResult(Message m) =>
        m.Kind == Kinds.JobFinished && m.InboxScope == Real.Ticket(m.Target.Id, m.Read<JobFinished>().Key, m.TargetCreation ?? 0).Scope &&
        _registry.TryGetValue(m.Read<JobFinished>().Key, out var g) && m.Id == ResultId(rules == JrRules.GenerationBlind ? JobOf(m).Generation : g);

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
        // full key is J's ticket key takes J's key out of the row's registry. The built function derives the key with
        // Worker.TicketId (no generation); here it is the registry's current generation's ticket key (R4-M3, proposed).
        var (key, _) = JobOf(m);
        var undoneTicket = m.Kind == Kinds.Job && m.InboxScope > 0;
        var drops = (undoneTicket || _authenticInMemory.Contains(m.InboxKey)) && _registry.TryGetValue(key, out var held) && m.InboxKey == TicketKey(held)
            && rules != JrRules.ReviewedKeepsRegistry && !(undoneTicket && KeepsUndoneTicket);
        if (undoneTicket && KeepsUndoneTicket) _report.Line($"the repaired rule keeps {key} in the registry: the consumed ticket is undone and of the current generation");
        var resultRow = m.Kind == Kinds.JobFinished && m.InboxScope == _ticket.Scope && _registry.TryGetValue(key, out var rg) && JobOf(m).Generation == rg;
        if (ResultCase && resultRow)
        {
            // The coordinator's rule: a parked consumption keeps the entry of a current-generation result row; it clears only
            // when a model turn of a running row consumes the result. Reviewed and negative clear it here.
            drops = rules != JrRules.Repaired;
            if (!drops) _report.Line($"the repaired rule keeps {key} in the registry: the consumed row is the current generation's result");
        }
        var next = drops ? _row with { Revision = _row.Revision + 1, Jobs = null } : null;
        await Commit(new TurnCommit(Self.Id, next, next is null ? null : _row.Revision, [], [m.InboxKey], [], [], [], null));
        if (next is not null) { _row = next; _registry.Remove(key); }
        _keptBy[m.InboxKey] = "Arrived";
        _report.Line($"the parked instance consumes {m.Id} ({m.Kind}) as late input: kept Arrived ({why}){(drops ? ResultCase && resultRow ? $"; the reviewed rule drops {key} from the registry" : $"; the built RemainingJobs drops {key} from the registry" : "")}");
    }

    /// <summary>Whether the owner may give this delivery to the instance, and why.</summary>
    private bool Accepted(Message m, out string why)
    {
        if (m.Kind != Kinds.JobFinished) { why = "a ticket"; return true; }
        if (_authenticInMemory.Contains(m.InboxKey)) { why = "this owner's in-memory completion (CompletedJob)"; return true; }
        var builtRefuses = Real.DiscardedAt(m, _row);
        if (Converts && AuthenticResult(m))
        { why = $"kernel-issued result row (the ticket's scope) of generation g{JobOf(m).Generation}, the registry's g{_registry[_jobKey]} (the built DiscardedAt alone {(builtRefuses ? "refuses it" : "admits it")})"; return true; }
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
        // Pass 0: the Open's disposition lookup for each job the restored waits name (observability.md, the lookup table):
        // a pending ticket runs; a kept Arrived ticket restarts (pass 1); a done or absent ticket with no KeptBy restarts,
        // unless (repaired) the minted {ticket id}/result row is pending (it stays) or kept Arrived (its page restores it).
        foreach (var (job, current) in _registry.ToList())
        {
            var ticketRow = _store.Messages.GetValueOrDefault(TicketKey(current));
            if (ticketRow is { Done: false }) { _report.Line($"recover: lookup {job}: ticket {TicketKey(current).Id} pending; it runs"); continue; }
            if (ticketRow is not null && _keptBy.GetValueOrDefault(ticketRow.Message.InboxKey) == "Arrived") continue;   // pass 1
            var resultKey = new InboxKey(ResultId(current), _ticket.Scope);
            var resultRow = _store.Messages.GetValueOrDefault(resultKey);
            if (ProbesResult && resultRow is { Done: false }) { _report.Line($"recover: lookup {job}: ticket done or absent, result row {resultKey.Id} pending; it stays, no restart"); continue; }
            if (ProbesResult && resultRow is not null && _keptBy.GetValueOrDefault(resultKey) == "Arrived") { _report.Line($"recover: lookup {job}: ticket done or absent, result row {resultKey.Id} kept Arrived; its page restores it, no restart"); continue; }
            var restartedRow = new Message(TicketKey(current + 1).Id, Self, Kinds.Job,
                Json.Write(new JobTicketPayload(NativeModel.ModelKey, job, _arguments)), Self.Id, TargetCreation: 1, SenderCreation: 1).WithInboxScope(_ticket.Scope);
            await Commit(new TurnCommit(Self.Id, null, null, [], [], [restartedRow], [], [], null));
            _registry[job] = current + 1;
            _restartedId = restartedRow.Id;
            _report.Line($"recover: lookup {job}: ticket done or absent{(ProbesResult ? ", no result row" : " (the result row is not probed)")}: Restarted(g{current + 1}) from its JobSpec ({restartedRow.Id})");
        }
        // Pass 1: the Open's restart decisions are in force before any page decides what is obsolete (R4-M3): a kept Arrived
        // current-generation ticket restarts from its JobSpec under the next generation, and the old ticket is cleared.
        foreach (var (key, reason) in _keptBy.ToList())
        {
            if (reason != "Arrived" || !_store.Messages.TryGetValue(key, out var row) || row.Message.Kind != Kinds.Job || !KeepsUndoneTicket) continue;
            var (job, generation) = JobOf(row.Message);
            if (!_registry.TryGetValue(job, out var current) || generation != current) continue;
            var restarted = new Message(TicketKey(current + 1).Id, Self, Kinds.Job,
                Json.Write(new JobTicketPayload(NativeModel.ModelKey, job, _arguments)), Self.Id, TargetCreation: 1, SenderCreation: 1).WithInboxScope(_ticket.Scope);
            await Commit(new TurnCommit(Self.Id, null, null, [], [], [restarted], [], [], null));
            _registry[job] = current + 1;
            _restartedId = restarted.Id;
            _keptBy.Remove(key);
            _report.Line($"recover: the Open restarts {job} from its JobSpec under g{current + 1} ({restarted.Id}); the old ticket {key.Id} is cleared, never restored");
        }
        // Pass 2: the pages restore the kept Arrived rows, clearing job rows the registry no longer holds or of another generation.
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
            _restored.Add(key);
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
        _resultSource ??= consumed.Id;
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
