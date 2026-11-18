namespace Kernel.Review5;

/// <summary>
/// The rule sets of FR-2's R4-B1 base traces. <c>Reviewed</c>: the fourth round's rules (Seen gate, release cohort
/// precedence and mark). <c>Repaired</c>: the handoff gate with the cohort precedence. <c>Negative</c>: the handoff gate with
/// the cohort precedence removed. <c>SeenGateNoPrecedence</c>: both removed, the rules the fourth review found failing.
/// </summary>
enum BaseRules { Reviewed, Repaired, Negative, SeenGateNoPrecedence }

/// <summary>
/// The two R4-B1 traces that passed the fifth review (Review4.Blockers' fr1-base and fr1-held-crash), re-staged under
/// FR-2's handoff gate, after FR-1 (copied, not edited). <c>base</c>: R is F's real Native timer at seq 1100, kept while
/// parked; the final page restores it and sends C at 950; A, a native.cancel at 900 from another sender, is withheld during
/// the drain. <c>held</c>: R is a composite signal to a HeldGate H the model holds, and a crash takeover follows Complete.
/// The model is the real <see cref="NativeModel"/>; the drain, poll, marks, trials and ownership are the fixture's, as in
/// <see cref="Fr2"/>.
/// </summary>
sealed class Fr2Base(string variant, BaseRules rules)
{
    private static readonly InstanceRef Self = new("0/subject");
    public static readonly string[] Variants = ["r4b1-base", "r4b1-held"];

    private readonly List<Row> _rows = [];
    private long _now = 1060, _epoch = 1, _nextSeq = 2000;
    private readonly HashSet<InboxKey> _memory = [], _held = [], _suspects = [];
    private readonly HashSet<long> _mine = [1];
    private bool _open = true, _mark, _ended, _crashed, _rBeforeComplete, _rAgainBeforeA, _f2WaitingAtA;
    private InboxKey? _gate;
    private string _state = "", _f2Wait = "";
    private InstanceStatus _status = InstanceStatus.Running;
    private NativeModel _model = null!;
    private InstanceInfo _info = null!;
    private long _revision = 1;
    private Report _report = null!;
    private readonly List<string> _inputs = [], _transitions = [], _records = [];
    private Row _r = null!;

    private bool HandoffGate => rules is BaseRules.Repaired or BaseRules.Negative;
    private bool Precedence => rules is BaseRules.Repaired or BaseRules.Reviewed;
    private bool Held => variant == "r4b1-held";

    public async Task<Report> RunAsync()
    {
        _report = new Report($"FR-2/{variant}", Program.Name(rules), Held
            ? "R, held by the model at Complete, reaches the model again after a takeover before A ends it"
            : "the final released row R reaches the model (F completes once) before the later-delivered A ends it");
        HeldGate.Accepts = 0;
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("two", Wf.Parallel("p", null, Wf.Delay("F", 50), Wf.Delay("F2", 100_000))))
            .Add(Wf.Workflow("held", Wf.Node<HeldGate>("H").With(Wf.Event("go", "go"))));
        _model = new NativeModel(registry);
        var workflow = Held ? "held" : "two";
        _info = new InstanceInfo(Self, NativeModel.ModelKey, workflow, null, 1);
        var spec = NativeModel.Spec(workflow);
        var start = new NativeHost(_info, 1000, ++_revision) { Inbox = [new Message("start", Self, Kinds.Start, Start: spec)], State = _model.CreateState(spec) };
        await _model.TurnAsync(start, CancellationToken.None); Real.CountModelTurn();
        var state = (NativeState)start.State;
        _state = Json.Write(state);
        string fFrame = "";
        if (Held)
        {
            var hFrame = state.Frames.Values.Single(f => f.Node == "H").Id;
            _r = new Row("R", new Message("signal-1", Self, CompositeSignal.Kind, Json.Write(new CompositeSignal(hFrame, "deferred")), "0/operator", TargetCreation: 1), 1000);
            _report.Line($"start: real Native turn ran H ({hFrame}), a composite that holds signals until its child Event 'go' completes; R = composite signal to H at seq 1000");
        }
        else
        {
            fFrame = state.Frames.Values.Single(f => f.Node == "F").Id;
            var f2Frame = state.Frames.Values.Single(f => f.Node == "F2").Id;
            _f2Wait = state.Waits.Single(w => w.Value.Frame == f2Frame).Key;
            _r = new Row("R", start.Sent.Single(m => m.Due == 1050), 1100);
            _report.Line($"start: real Native turn armed F ({fFrame}) with timer R due 1050 at seq 1100, and F2 much later");
        }
        _rows.Add(_r);
        // Park kept R Arrived; the recover's final page restores it (ReleasedBy (1,1)) and sends C at 950.
        _r.ReleasedBy = (1, 1); _gate = _r.Key;
        _rows.Add(new Row("C", Drain(), 950));
        _rows.Add(new Row("A", new Message("late-cancel", Self, NativeKinds.Cancel, null, "0/other", TargetCreation: 1), 900));
        _report.Line("park kept R; recover's final page restored R with ReleasedBy (1,1) and sent C = kernel.drain at seq 950; A = native.cancel at 900 from 0/other, withheld during the drain");

        for (var step = 0; step < 40 && !_ended; step++, _now++)
        {
            if (Held && !_crashed && !_open && _held.Contains(_r.Key))
            {
                _crashed = true;
                _epoch++; _mine.Clear(); _mine.Add(_epoch); _memory.Clear(); _held.Clear(); _suspects.Clear();
                _report.Line($"crash takeover after Complete, while the model holds R: another process owns epoch {_epoch}; memory and holds are lost");
            }
            Poll();
            await SubjectTurnAsync();
            var trial = NextTrial();
            if (trial is not null)
            {
                trial.Seen = _epoch; _suspects.Remove(trial.Key);
                _report.Line($"trial of {trial.Name} alone at epoch {_epoch}");
                await ModelTurnAsync(trial);
            }
        }
        var undone = _rows.Count(r => !r.Done && (r.Message.Due ?? 0) <= _now && r.Kind != Fr2.DrainKind);
        _report.Line($"model inputs [{string.Join(", ", _inputs)}]; S {_status}; drain {(_open ? "open" : "completed")}");
        if (Held)
        {
            _report.Expect("model inputs", string.Join(",", _inputs), "R,R,A");
            _report.Expect("R reached the model again after the takeover, before A", _rAgainBeforeA, true);
            _report.Expect("H's deferred-signal records (signal-deferred:signal-1)", _records.Count(x => x.StartsWith("signal-deferred:signal-1:", StringComparison.Ordinal)), 2);
        }
        else
        {
            _report.ExpectSequence("actual model transitions after the start", _transitions, [$"R {NativeKinds.Timer} -> c:{fFrame}:F", $"A {NativeKinds.Cancel} -> end Cancelled"]);
            _report.Expect("F completions", _records.Count(x => x == $"c:{fFrame}:F"), 1);
            _report.Expect("F2 still waiting when A reached the model", _f2WaitingAtA, true);
            if (HandoffGate) _report.Expect("R reached a committed model turn before Complete (the handoff gate)", _rBeforeComplete, true);
        }
        _report.Expect("R's fate", _r.Fate, "consumed by a model turn");
        _report.Expect("S ended", _status, InstanceStatus.Cancelled);
        _report.Expect("drain completed", !_open, true);
        _report.Expect("undone rows remaining", undone, 0);
        return _report;
    }

    private Message Drain() => new Message($"{CreationProtocol.Origin(Self.Id, 1)}#drain.{_nextSeq++}", Self, Fr2.DrainKind, Json.Write(new { Generation = 1 }), Self.Id,
        TargetCreation: 1, SenderCreation: 1).AsIssued();

    private static bool IsControl(string kind) => Kinds.IsControl(kind) || kind == Fr2.DrainKind;
    private bool Cohort(Row r) => r.ReleasedBy is not null && !r.Done;

    private void Poll()
    {
        foreach (var row in _rows.Where(r => !r.Done && r.Seen < _epoch && (r.Message.Due ?? 0) <= _now).OrderBy(r => r.Seq).ToList())
        {
            if (!_memory.Add(row.Key)) continue;
            if (!IsControl(row.Kind) && row.Seen > 0 && !_mine.Contains(row.Seen)) _suspects.Add(row.Key);
            else row.Seen = _epoch;
        }
    }

    /// <summary>Trials (Worker.cs 733–763, TrialGroup 1): with the precedence, cohort suspects first while the drain is open or the mark is set.</summary>
    private Row? NextTrial()
    {
        var suspects = _rows.Where(r => _suspects.Contains(r.Key) && !r.Done && (r.ReleasedBy is not null || !_open)).OrderBy(r => r.Seq).ToList();
        if (Precedence && (_open || _mark))
        {
            if (suspects.FirstOrDefault(Cohort) is { } cohort) return cohort;
            if (_rows.Any(r => Cohort(r) && !_held.Contains(r.Key))) return null;     // no non-cohort suspect ahead of a cohort row
        }
        return suspects.FirstOrDefault();
    }

    private async Task SubjectTurnAsync()
    {
        var offered = _rows.Where(r => _memory.Contains(r.Key) && !_suspects.Contains(r.Key) && !r.Done && !_held.Contains(r.Key) && (r.Message.Due ?? 0) <= _now
            && (IsControl(r.Kind) || !_open || r.ReleasedBy is not null)).ToList();
        if (Precedence && _open) offered = [.. offered.Where(r => IsControl(r.Kind)).OrderBy(r => r.Seq), .. offered.Where(r => !IsControl(r.Kind)).OrderBy(r => r.Seq)];
        else if (Precedence && _mark && _rows.Any(r => Cohort(r) && !_held.Contains(r.Key))) offered = offered.Where(Cohort).OrderBy(r => r.Seq).ToList();
        else offered = offered.OrderBy(r => r.Seq).ToList();
        if (offered.FirstOrDefault() is not { } head) return;
        if (head.Kind == Fr2.DrainKind) { Page(head); return; }
        await ModelTurnAsync(head);
    }

    /// <summary>The final page's kernel.drain: no kept row is left, so it completes once the gate passes (handoff or Seen).</summary>
    private void Page(Row control)
    {
        control.Done = true; control.Fate = "consumed (drain turn)";
        if (!_open) return;
        var gate = _rows.Single(r => r.Key == _gate);
        var passes = HandoffGate ? gate.Done || _held.Contains(gate.Key) : gate.Done || _held.Contains(gate.Key) || gate.Seen == _epoch && !_suspects.Contains(gate.Key);
        if (!passes)
        {
            _rows.Add(new Row(control.Name + "'", Drain() with { Due = _now + 3 }, _nextSeq));
            _report.Line($"{control.Name} at {_now}: gate row R not {(HandoffGate ? "handed off" : "taken up")}; retry due {_now + 3}");
            return;
        }
        _open = false;
        _mark = Precedence && _rows.Any(Cohort);
        _report.Line($"{control.Name} at {_now}: gate row R {(HandoffGate ? "handed off" : "taken up")}: Complete{(_mark ? " sets the cohort mark (1,1)" : "")}");
    }

    private async Task ModelTurnAsync(Row head)
    {
        if (_ended) { head.Done = true; head.Fate = "dropped (finished instance)"; return; }
        if (head == _r && _open) _rBeforeComplete = true;
        if (head == _r && _crashed && !_inputs.Contains("A")) _rAgainBeforeA = true;
        if (head.Name == "A" && _f2Wait.Length > 0) _f2WaitingAtA = Json.Read<NativeState>(_state).Waits.ContainsKey(_f2Wait);
        var inbox = _rows.Where(r => _held.Contains(r.Key) && !r.Done).Append(head).Distinct().OrderBy(r => r.Seq).ToList();
        var host = new NativeHost(_info, _now, ++_revision) { Inbox = inbox.Select(r => r.Message).ToList(), State = Json.Read<NativeState>(_state) };
        await _model.TurnAsync(host, CancellationToken.None); Real.CountModelTurn();
        _inputs.Add(head.Name);
        var holds = host.HeldMessages.Select(m => m.InboxKey).ToHashSet();
        foreach (var row in inbox)
            if (holds.Contains(row.Key)) _held.Add(row.Key);
            else { row.Done = true; _held.Remove(row.Key); row.Fate = "consumed by a model turn"; }
        _state = Json.Write(host.State);
        _records.AddRange(host.Records);
        var parts = host.Records.Where(x => x.StartsWith("c:", StringComparison.Ordinal)).ToList();
        if (host.EndStatus is { } end) { _status = end; _ended = true; _held.Clear(); parts.Add($"end {end}"); _mark = false; }
        _transitions.Add($"{head.Name} {head.Kind} -> {string.Join(", ", parts)}");
        if (_mark && !_rows.Any(Cohort)) _mark = false;
        _report.Line($"model turn {string.Join("+", inbox.Select(r => r.Name))} at {_now}: {(parts.Count > 0 ? string.Join(", ", parts) : holds.Count > 0 ? "held [" + string.Join(", ", inbox.Where(r => holds.Contains(r.Key)).Select(r => r.Name)) + "]" : "no records")}");
        foreach (var later in _rows.Where(r => _ended && !r.Done && r.Message.Target == Self && r.Kind != Fr2.DrainKind && (r.Message.Due ?? 0) <= _now).ToList())
        { later.Done = true; later.Fate = "dropped (finished instance)"; _report.Line($"{later.Name}: dropped under the finished-instance rule"); }
    }
}
