namespace Kernel.Review5;

/// <summary>
/// The rule sets of PO-1 (R5-M6). <c>Reviewed</c>: the fourth round's cohort rules (FR-1's repaired set) with the built
/// poison comparison. <c>Repaired</c>: while a recover drain is open or the cohort mark is set, the poison comparison uses
/// the cohort order, so a non-cohort row never precedes a cohort row there. <c>Negative</c>: the repaired set with
/// precedence off in the poison comparison, which is the built comparison again, so in PO-1 it coincides with reviewed.
/// </summary>
enum PoRules { Reviewed, Repaired, Negative }

/// <summary>
/// PO-1: an exhausted job ticket J at seq 900 and a cohort suspect R (F's real Native timer) at 1100. The durable rows,
/// their Seen, Trial and Attempts, live in the real <see cref="InMemoryStore"/>: J's attempts are counted by the real
/// <c>TakeAsync</c> as owners die with J on trial (InMemoryStore.cs 70–82), marks and trials go through the real
/// <c>CommitAsync</c> (174–177), and every new owner reads through the real <c>PollAsync</c>. The owner's selection mirrors
/// the built Worker for one instance (Suspect 577–578, PoisonInput 705–707, PrecedesPoison 714–716, NextTrial 733–763,
/// RunTurn's MaxAttempts Park 1064–1065, WorkflowInstance.Park 120–132), with FR-1's cohort rules on top. In the ordinary
/// case (no drain, no mark) the same rows are also handed to a real <see cref="Worker"/>, and its outcome is compared.
/// </summary>
sealed class Po1(string variant, PoRules rules)
{
    private const int MaxAttempts = 3;              // WorkerOptions.MaxAttempts default
    // This fixture holds the recovery state itself. The store reserves the real drain for a checkpoint transition.
    private const string ModeledDrainKind = "experiment.drain";
    private static readonly InstanceRef Self = new("0/subject");
    private static readonly TimeSpan LeaseFor = TimeSpan.FromMilliseconds(10);
    public static readonly string[] Variants = ["cohort", "open", "ordinary"];

    private long _now = 1000;
    private InMemoryStore _store = null!;
    private Lease _lease = null!;
    private int _node;
    private NativeModel _model = null!;
    private InstanceInfo _info = null!;
    private InstanceRow _row = null!;
    private InboxKey _j, _r, _c;
    private string _fFrame = "";
    private Report _report = null!;
    // Proposed durable facts beside the rows: release tags, the open drain, the cohort mark.
    private readonly Dictionary<InboxKey, (long, long)> _releasedBy = [];
    private bool _drainOpen, _mark;
    // Owner memory.
    private readonly Dictionary<InboxKey, Stored> _memory = [];
    private readonly HashSet<InboxKey> _suspects = [];
    private readonly List<string> _transitions = [];
    private bool _stalled;

    private sealed record JobTicketPayload(string Model, string Key, string? Arguments);

    public async Task<Report> RunAsync()
    {
        _report = new Report($"PO-1/{variant}", Program.Name(rules), variant == "ordinary"
            ? "outside a drain and without a mark the built comparison holds: the earlier exhausted ticket parks the instance before the later suspect's trial"
            : "a cohort suspect gets its trial before an exhausted non-cohort ticket is poisoned, and the poison then parks the instance");
        await BuildAsync(_report);
        if (variant == "ordinary")
        {
            var real = await RealWorkerAsync();
            await BuildAsync(null);          // the same history, rebuilt, for the mirrored owner
            await TakeoverAsync("the mirrored owner takes over from the lost owner 4");
            await OwnerAsync();
            var mirrored = Outcome();
            _report.Line($"real Worker outcome: {real}");
            _report.Line($"mirrored owner outcome: {mirrored}");
            _report.Expect("the mirrored owner agrees with the real Worker", mirrored, real);
            _report.ExpectSequence("transitions", _transitions, ["J -> park (MaxAttempts)", "R -> late input to the parked instance"]);
            _report.Expect("F completions", FCompletions(), 0);
        }
        else
        {
            await TakeoverAsync("cold takeover before J or R is dispatched: a new process, no memory");
            await OwnerAsync();
            _report.ExpectSequence("transitions", _transitions, [$"R {NativeKinds.Timer} -> c:{_fFrame}:F", "J -> park (MaxAttempts)"]);
            _report.Expect("F completions", FCompletions(), 1);
            _report.Expect("nothing stalled", _stalled, false);
        }
        _report.Expect("S parked, its fault naming J", _row.Status == InstanceStatus.Faulted && _row.Fault?.Contains(_j.Id) == true, true);
        return _report;
    }

    /// <summary>
    /// The history, on the real store. S starts (real Native turn: F's timer R due 1050, F2's due much later); J, a hanging
    /// job ticket, sits at seq 900 and R at 1100. Owners 1, 2 and 3 each put J on trial and die; each next take adds an
    /// attempt (the real TakeAsync), so owner 3 is the last failed owner and J reaches MaxAttempts. Owner 4 marks J and R
    /// seen. Cohort variants: R was kept while parked and the drain's final page released it (ReleasedBy (1,1)); J was
    /// released by an early page. "cohort": Complete set the mark. "open": the drain is open, its last control C at 950.
    /// </summary>
    private async Task BuildAsync(Report? log)
    {
        _now = 1000; _node = 0;
        _store = new InMemoryStore(1, () => _now);
        _releasedBy.Clear(); _said.Clear(); _drainOpen = _mark = false; _memory.Clear(); _suspects.Clear(); _transitions.Clear(); _stalled = false;
        _lease = await Real.TakeAsync(_store, 0, $"owner-{++_node}", LeaseFor) ?? throw new InvalidOperationException("harness schedule: no lease");
        var wf = Wf.Workflow("two", Wf.Parallel("p", null, Wf.Delay("F", 50), Wf.Delay("F2", 100_000)));
        _model = new NativeModel(new WorkflowRegistry().Add(wf));
        _info = new InstanceInfo(Self, NativeModel.ModelKey, "two", null, 1);
        var spec = NativeModel.Spec("two");
        var start = new NativeHost(_info, _now, 1) { Inbox = [new Message("start", Self, Kinds.Start, Start: spec)], State = _model.CreateState(spec) };
        await _model.TurnAsync(start, CancellationToken.None); Real.CountModelTurn();
        var state = (NativeState)start.State;
        _fFrame = state.Frames.Values.Single(f => f.Node == "F").Id;
        var timerF = start.Sent.Single(m => m.Due == 1050);
        _r = timerF.InboxKey;
        _j = Real.Ticket(Self.Id, "hang", 1);
        var j = ModeledTicket.Issue(new Message(_j.Id, Self, Kinds.Job, Json.Write(new JobTicketPayload(NativeModel.ModelKey, "hang", null)),
            Self.Id, TargetCreation: 1, SenderCreation: 1).WithInboxScope(_j.Scope));
        _row = new InstanceRow(Self.Id, NativeModel.ModelKey, "two", InstanceStatus.Running, Json.Write(state), 1, null,
            Jobs: DurableKeyJson.WriteKeys(["hang"]), StateVersion: _model.StateVersion, Creation: 1, StartIntent: "harness-intent");
        await Commit([new TurnCommit(Self.Id, _row, null, [], [], [j, .. start.Sent], [], [], null)]);
        _store.Messages[_j].Seq = 900;
        _store.Messages[_r].Seq = 1100;
        foreach (var other in start.Sent.Where(m => m.InboxKey != _r)) _store.Messages[other.InboxKey].Seq = 1101;
        for (var lost = 1; lost <= MaxAttempts; lost++)
        {
            await Commit([], trial: [_j]);
            await TakeoverAsync(null);
            log?.Line($"owner {lost} put J on trial at epoch {_lease.Epoch - 1} and died; the real TakeAsync gives J attempt {_store.Messages[_j].Attempts}{(lost == MaxAttempts ? " (MaxAttempts: owner 3 was the last failed owner)" : "")}");
        }
        _now = 1060;
        if (variant is "cohort" or "open")
        {
            _releasedBy[_j] = (1, 1); _releasedBy[_r] = (1, 1);
            if (variant == "open")
            {
                var c = new Message($"{CreationProtocol.Origin(Self.Id, 1)}#drain.950", Self, ModeledDrainKind,
                    Json.Write(new { Generation = 1 }), Self.Id, TargetCreation: 1, SenderCreation: 1).AsIssued();
                await Commit([new TurnCommit(Self.Id, null, null, [], [], [c], [], [], null)]);
                _store.Messages[c.InboxKey].Seq = 950;
                _c = c.InboxKey;
                _drainOpen = true;
            }
            else _mark = true;
        }
        await Commit([], seen: [_j, _r]);
        log?.Line($"owner 4 (epoch {_lease.Epoch}) marks J and R seen; {variant switch { "cohort" => "the final page released R and Complete set the cohort mark (1,1); J was released by an early page", "open" => "the final page released R (gate row R) and sent C = kernel.drain at seq 950; the drain is open; J was released by an early page", _ => "no recover drain and no mark: R is an ordinary timer" }}");
        log?.Line($"rows: J {Describe(_j)}; R {Describe(_r)}");
    }

    private string Describe(InboxKey key)
    {
        var row = _store.Messages[key];
        return $"(seq {row.Seq}, {row.Message.Kind}, seen {row.Seen}, trial {row.Trial}, attempts {row.Attempts}{(_releasedBy.TryGetValue(key, out var r) ? $", released ({r.Item1},{r.Item2})" : "")})";
    }

    private async Task TakeoverAsync(string? when)
    {
        _now += 20;
        _lease = await Real.TakeAsync(_store, 0, $"owner-{++_node}", LeaseFor) ?? throw new InvalidOperationException("harness schedule: the takeover failed");
        _memory.Clear(); _suspects.Clear();
        if (when is not null) _report.Line($"{when}: epoch {_lease.Epoch}");
    }

    private bool Cohort(InboxKey key) => _releasedBy.ContainsKey(key) && _store.Messages.ContainsKey(key) && !_store.Messages[key].Done &&
        _store.Messages[key].Message.Kind is not (Kinds.Job or Kinds.Meet or Kinds.Publish);
    private bool CohortContext => _drainOpen || _mark;
    private static bool IsControl(string kind) => Kinds.IsControl(kind) || kind == ModeledDrainKind;
    private bool Due(Stored s) => (s.Message.Due ?? 0) <= _now;

    /// <summary>Worker.cs 705–707: an exhausted input that is not a result and not a control.</summary>
    private static bool PoisonInput(Stored s) => s.Attempts >= MaxAttempts && s.Message.Kind != Kinds.JobFinished && !IsControl(s.Message.Kind);

    /// <summary>
    /// Worker.cs 714–716: the suspect precedes every due poison input. Repaired (R5-M6): in the cohort context a non-cohort
    /// poison input never precedes a cohort suspect; outside it the built comparison holds.
    /// </summary>
    private bool PrecedesPoison(Stored suspect) => _memory.Values
        .Where(i => PoisonInput(i) && Due(i) && !(rules == PoRules.Repaired && CohortContext && Cohort(suspect.Message.InboxKey) && !Cohort(i.Message.InboxKey)))
        .All(i => suspect.Seq < i.Seq);

    /// <summary>
    /// Worker.cs 733–763 for one instance with TrialGroup 1, plus FR-1's cohort-first selection in the cohort context:
    /// cohort suspects first, exempt from a control's recovery cut. While poison is pending, a job ticket suspect and a
    /// suspect that does not precede the poison are skipped (753–754).
    /// </summary>
    private Stored? NextTrial()
    {
        var suspects = _memory.Values.Where(s => _suspects.Contains(s.Message.InboxKey) && Due(s))
            .OrderBy(s => CohortContext && Cohort(s.Message.InboxKey) ? 0 : 1).ThenBy(s => s.Seq).ToList();
        var poisonPending = _memory.Values.Any(i => PoisonInput(i) && Due(i));
        foreach (var suspect in suspects)
        {
            if (poisonPending && (suspect.Message.Kind == Kinds.Job || !PrecedesPoison(suspect)))
            {
                Once($"{Name(suspect)} is not tried: the poison comparison puts J first");
                continue;
            }
            return suspect;
        }
        return null;
    }

    private async Task OwnerAsync()
    {
        for (var step = 0; step < 30; step++, _now++)
        {
            // Poll (real) and mark what is not a suspect (the flush, Worker.cs 1397).
            var polled = await Real.PollAsync(_store, 0, _lease.Epoch, 500);
            var marks = new List<InboxKey>();
            foreach (var s in polled.Messages.Where(m => m.Message.Target == Self))
            {
                var key = s.Message.InboxKey;
                if (_memory.ContainsKey(key)) continue;
                _memory[key] = s;
                var suspect = s.Message.Kind is not (Kinds.Meet or Kinds.Publish) && !IsControl(s.Message.Kind) && s.Attempts < MaxAttempts
                    && (s.Trial > 0 || s.Seen > 0 && s.Seen != _lease.Epoch);
                if (suspect) _suspects.Add(key); else marks.Add(key);
                if (step == 0) _report.Line($"poll: {Name(s)} {(suspect ? "is a suspect" : PoisonInput(s) ? "is a poison input (exhausted, no longer a ticket)" : "")}".TrimEnd());
            }
            if (marks.Count > 0) await Commit([], seen: marks);
            if (_row.Status != InstanceStatus.Running && _memory.Count == 0) return;
            var trial = NextTrial();
            if (trial is not null)
            {
                await Commit([], trial: [trial.Message.InboxKey]);
                _suspects.Remove(trial.Message.InboxKey);
                _report.Line($"trial of {Name(trial)} alone (the trial commit marks it at epoch {_lease.Epoch})");
                await DeliverAsync(trial);
                continue;
            }
            var offer = Offer();
            if (offer is null)
            {
                if (_memory.Count == 0) return;
                Once("nothing can move: " + string.Join("; ", _memory.Values.Select(s => $"{Name(s)} {(_suspects.Contains(s.Message.InboxKey) ? "waits for a trial" : "is withheld")}")));
                if (step > 5) { _stalled = true; _report.Line("stalled: the step bound was reached with work still pending"); return; }
                continue;
            }
            await DeliverAsync(offer);
        }
        _stalled = true;
    }

    /// <summary>
    /// What ordinary delivery offers (Worker.cs 823–879, with FR-1's order): while the drain is open, control kinds, then
    /// released rows (others withheld); while the mark is set and a non-held cohort row remains, only cohort rows; else seq order.
    /// </summary>
    private Stored? Offer()
    {
        var eligible = _memory.Values.Where(s => !_suspects.Contains(s.Message.InboxKey) && Due(s) && (s.Message.Kind != Kinds.Job || PoisonInput(s))
            && (!_drainOpen || IsControl(s.Message.Kind) || _releasedBy.ContainsKey(s.Message.InboxKey))).ToList();
        var cohortPending = _mark && _memory.Keys.Any(Cohort);
        if (cohortPending) eligible = eligible.Where(s => Cohort(s.Message.InboxKey)).ToList();
        return eligible.OrderBy(s => _drainOpen && IsControl(s.Message.Kind) ? 0 : 1).ThenBy(s => s.Seq).FirstOrDefault();
    }

    private async Task DeliverAsync(Stored item)
    {
        var key = item.Message.InboxKey;
        _memory.Remove(key);
        if (item.Message.Kind == ModeledDrainKind && !_drainOpen)
        {
            await Commit([new TurnCommit(Self.Id, null, null, [key], [], [], [], [], null)]);
            _report.Line("C: no open resolution (the re-park interrupted the drain); consumed");
            return;
        }
        if (item.Message.Kind == ModeledDrainKind)
        {
            // The final page found no row; it completes when gate row R is taken up (FR-1's gate): consumed, or seen at this
            // epoch and not an untried suspect. Otherwise it waits.
            var gate = _store.Messages.GetValueOrDefault(_r);
            var takenUp = gate is null || gate.Done || gate.Seen == _lease.Epoch && !_suspects.Contains(_r);
            if (!takenUp)
            {
                var retryMessage = new Message(item.Message.Id + "'", Self, ModeledDrainKind, item.Message.Payload,
                    Self.Id, Due: _now + 3, TargetCreation: 1, SenderCreation: 1).AsIssued();
                await Commit([new TurnCommit(Self.Id, null, null, [key], [], [retryMessage], [], [], null)]);
                Once($"C: gate row R not taken up (an untried suspect); C sends a retry due 3 ms later");
                return;
            }
            await Commit([new TurnCommit(Self.Id, null, null, [key], [], [], [], [], null)]);
            _drainOpen = false;
            _mark = _memory.Keys.Any(Cohort);
            _report.Line($"C: gate row R taken up: Complete{(_mark ? " sets the cohort mark" : "; no released row remains, so no mark")}");
            return;
        }
        if (_row.Status != InstanceStatus.Running)
        {
            // The parked instance consumes late input with no model turn (WorkflowInstance.cs 78–90); kept Arrived (proposed).
            await Commit([new TurnCommit(Self.Id, null, null, [], [key], [], [], [], null)]);
            _transitions.Add($"{Name(item)} -> late input to the parked instance");
            _report.Line($"{Name(item)}: consumed as late input by the parked instance");
            return;
        }
        if (item.Attempts >= MaxAttempts && item.Message.Kind != Kinds.JobFinished)
        {
            // RunTurn (Worker.cs 1064–1065) parks with the poison message; WorkflowInstance.Park (120–132), mirrored.
            var next = _row with { Status = InstanceStatus.Faulted, Revision = _row.Revision + 1, Jobs = null,
                Fault = $"message {item.Message.Id} was on trial when {item.Attempts} owners were lost" };
            await Commit([new TurnCommit(Self.Id, next, _row.Revision, [key], [], [], [], [], null, Unsubscribe: true)]);
            _row = next;
            _transitions.Add($"{Name(item)} -> park (MaxAttempts)");
            _report.Line($"{Name(item)}: on trial when {item.Attempts} owners were lost: S parks ({_row.Fault})");
            if (_drainOpen) { _drainOpen = false; _report.Line("the re-park interrupts the open drain: its Resolution is cleared in the parking commit (observability.md, Interruption)"); }
            return;
        }
        var host = new NativeHost(_info, _now, _row.Revision + 1) { Inbox = [item.Message], State = Json.Read<NativeState>(_row.State) };
        await _model.TurnAsync(host, CancellationToken.None); Real.CountModelTurn();
        var nextRow = _row with { State = Json.Write(host.State), Revision = _row.Revision + 1 };
        await Commit([new TurnCommit(Self.Id, nextRow, _row.Revision, [key], [], [], [], [], null)]);
        _row = nextRow;
        var records = host.Records.Where(r => r.StartsWith("c:", StringComparison.Ordinal)).ToList();
        _transitions.Add($"{Name(item)} {item.Message.Kind} -> {string.Join(", ", records)}");
        _report.Line($"model turn {Name(item)} ({item.Message.Kind}): {string.Join(", ", records)}");
        if (_mark && !_memory.Keys.Any(Cohort)) { _mark = false; _report.Line("the mark's read finds no cohort row: its commit clears the mark"); }
    }

    private string Name(Stored s) => s.Message.InboxKey == _j ? "J" : s.Message.InboxKey == _r ? "R" : s.Message.InboxKey == _c ? "C" : s.Message.Id;

    private int FCompletions() => _transitions.Count(t => t.Contains($"c:{_fFrame}:F", StringComparison.Ordinal));

    private string Outcome() =>
        $"S {_row.Status}, fault names J {_row.Fault?.Contains(_j.Id) == true}, F completed {FCompletions() > 0}, rows of S undone {Undone()}";

    private int Undone() => _store.Messages.Values.Count(m => m.Message.Target == Self && !m.Done && (m.Message.Due is null || m.Message.Due <= _now));

    /// <summary>The ordinary history handed to a real Worker (WorkflowInstance over the real NativeModel), pumped until idle.</summary>
    private async Task<string> RealWorkerAsync()
    {
        _now += 20;
        Real.Count("Worker (real)");
        await using var worker = new Worker("real-owner", 0, _store, new WorkflowInstance([_model]), new WorkerOptions { Monotonic = () => _now, MaxAttempts = MaxAttempts });
        await new DurableHost([worker]).ActivateAsync(0);
        for (var pass = 0; pass < 10; pass++) { await worker.PollAsync(); await worker.RunAsync(); }
        var row = _store.Instances[Self.Id];
        var f = !Json.Read<NativeState>(row.State).Frames.Values.Any(fr => fr.Node == "F");
        var undone = Undone();
        _report.Line($"real Worker at epoch {worker.Epoch}: S {row.Status}, fault '{row.Fault}'");
        return $"S {row.Status}, fault names J {row.Fault?.Contains(_j.Id) == true}, F completed {f}, rows of S undone {undone}";
    }

    private async Task Commit(IReadOnlyList<TurnCommit> turns, IReadOnlyList<InboxKey>? seen = null, IReadOnlyList<InboxKey>? trial = null)
    {
        var committed = await Real.CommitAsync(_store, new CommitBatch(0, _lease.Epoch, _lease.Position, LeaseFor, false, turns, seen ?? [], trial ?? []));
        if (!committed.Landed) throw new InvalidOperationException("harness schedule: a commit did not land");
    }

    private readonly HashSet<string> _said = [];
    private void Once(string line) { if (_said.Add(line)) _report.Line(line); }
}
