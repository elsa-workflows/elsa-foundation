namespace Kernel.Review4;

/// <summary>The rule sets of FR-1 (R4-B1). <c>HeldBlocks</c> is a mutation of the repaired set for the held variant.</summary>
enum FrRules { Reviewed, Repaired, Negative, HeldBlocks }

/// <summary>One inbox row as the proposed drain sees it: full key, seq, kind, release tag, Seen epoch, Done, KeptBy.</summary>
sealed class InboxRow(string name, Message message, long seq)
{
    public string Name { get; } = name;
    public Message Message { get; } = message;
    public long Seq { get; } = seq;
    public (long Creation, long Generation)? ReleasedBy;
    public long Seen;
    public bool Done;
    public string? KeptBy;
    public bool Deleted;   // a recalled or cleared-and-swept row: no longer in the inbox
    public string Fate = "pending";
    public InboxKey Key => Message.InboxKey;
    public string Kind => Message.Kind;

    /// <summary>The store's poll order with the full key as its tie-break: (Seq, Id, Scope, Match).</summary>
    public static int Compare(InboxRow a, InboxRow b)
    {
        var c = a.Seq.CompareTo(b.Seq);
        if (c == 0) c = string.CompareOrdinal(a.Key.Id, b.Key.Id);
        if (c == 0) c = a.Key.Scope.CompareTo(b.Key.Scope);
        if (c == 0) c = string.CompareOrdinal(a.Key.Match, b.Key.Match);
        return c;
    }

    public string Describe() =>
        $"{Name}(seq {Seq}, {Kind}, released {(ReleasedBy is { } r ? $"({r.Creation},{r.Generation})" : "-")}, seen {Seen}, done {Done}, kept {KeptBy ?? "-"})";
}

/// <summary>The durable facts of the instance row the drain needs: creation, ParkRecord's generation, the open Resolution, status, model state.</summary>
sealed class FrInstance
{
    public long Creation = 1;
    public long Revision = 1;
    public long ResolutionGeneration;
    public long? OpenGeneration;          // ParkRecord.Resolution, null when none is open
    public InboxRow? Cursor;              // the last tuple a page reached
    public InboxKey? Gate;                // the last row the previous page restored
    public InstanceStatus Status = InstanceStatus.Running;
    public bool ModelEnded;
    public string State = "{}";
}

/// <summary>
/// FR-1: the final released row R and a late model-ending arrival A, with the drain control C between them.
/// The model is the real <see cref="NativeModel"/> with its real handlers; the drain, poll and ownership are the
/// proposed transitions, applied here to durable rows the harness keeps.
/// </summary>
sealed class Fr1(string variant, FrRules rules)
{
    public const string DrainKind = "kernel.drain";
    private const long Now = 1060;      // F's timer (due 1050) is due; F2's (due 1100) is not
    private const int PageLimit = 170;   // P_page

    private static readonly InstanceRef Self = new("0/subject");
    private readonly List<InboxRow> _rows = [];
    private readonly FrInstance _instance = new();
    private readonly HashSet<InboxKey> _held = [];   // owner memory: rows a model turn handed back
    private readonly List<string> _modelInputs = [];
    private long _epoch = 1;
    private long _nextSeq = 2000;
    private bool _completed;
    private NativeModel _model = null!;
    private InstanceInfo _info = null!;
    private Report _report = null!;
    private bool _aWhileHeld;
    private bool _f2WaitingWhenAArrived;

    public static readonly string[] Variants = ["base", "takeover-before-complete", "takeover-after-complete", "held"];

    public async Task<Report> RunAsync()
    {
        var held = variant == "held";
        _report = new Report($"FR-1/{variant}", Program.Name(rules), held
            ? "a row the model holds never blocks delivery; the held cohort row still receives the input it waits for"
            : "the final released cohort reaches the model before a later-delivered arrival that ends it (F completes once)");
        HeldGate.Accepts = 0;
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("two", Wf.Parallel("p", null, Wf.Delay("F", 50), Wf.Delay("F2", 100))))
            .Add(Wf.Workflow("held", Wf.Node<HeldGate>("H").With(Wf.Event("go", "go"))));
        _model = new NativeModel(registry);
        var spec = NativeModel.Spec(held ? "held" : "two");
        _info = new InstanceInfo(Self, NativeModel.ModelKey, held ? "held" : "two", null, _instance.Creation);
        _instance.State = Json.Write(_model.CreateState(spec));

        // 1. Start: a real Native turn arms the waits (existing prototype behaviour).
        var start = new InboxRow("start", new Message("start", Self, Kinds.Start, Start: spec), 100);
        _rows.Add(start);
        await ModelTurnAsync(start, now: 1000, seqs: held ? [] : [1100, 1101], names: held ? [] : ["R", "T2"]);
        var state = Json.Read<NativeState>(_instance.State);
        string fFrame = "", f2Frame = "", f2Wait = "";
        InboxRow r;
        if (!held)
        {
            fFrame = state.Frames.Values.Single(f => f.Node == "F").Id;
            f2Frame = state.Frames.Values.Single(f => f.Node == "F2").Id;
            f2Wait = state.Waits.Single(w => w.Value.Frame == f2Frame).Key;
            r = _rows.Single(x => x.Name == "R");
            Require(state.Waits.Single(w => w.Value.Frame == fFrame).Value.Key == r.Key.Id, "R is F's timer");
            _report.Line($"start: real Native turn armed F ({fFrame}) with timer R = {r.Key.Id} due {r.Message.Due} and F2 ({f2Frame}) with timer T2 due {_rows.Single(x => x.Name == "T2").Message.Due}");
        }
        else
        {
            var hFrame = state.Frames.Values.Single(f => f.Node == "H").Id;
            r = new InboxRow("S", new Message("signal-1", Self, CompositeSignal.Kind, Json.Write(new CompositeSignal(hFrame, "deferred")), "0/operator", TargetCreation: 1), 1100);
            _rows.Add(r);
            _report.Line($"start: real Native turn ran H ({hFrame}), a composite that defers signals until its child Event 'go' completes; S = composite signal to H at seq 1100");
        }

        // 2. Park (proposed): R arrived and was consumed while parked, so it is kept with KeptBy = Arrived.
        _instance.Status = InstanceStatus.Faulted;
        r.Done = true; r.KeptBy = "Arrived"; r.Fate = "kept";
        _report.Line($"park: {r.Describe()}");

        // 3. Recover Open (proposed): generation 1, cursor before the first row, instance running, first kernel.drain.
        _instance.ResolutionGeneration = 1; _instance.OpenGeneration = 1; _instance.Cursor = null; _instance.Status = InstanceStatus.Running;
        _rows.Add(new InboxRow("C0", Drain(800), 800));
        _report.Line("open: recover opens generation 1; ParkRecord.ResolutionGeneration = 1; C0 = kernel.drain seq 800");

        // 4. A arrives during the drain from another sender, with a lower seq than R: withheld (not released).
        var a = held
            ? new InboxRow("A", new Message("late-go", Self, NativeKinds.Resume, Json.Write(new ResumeStimulus("go")), "0/other", TargetCreation: 1), 900)
            : new InboxRow("A", new Message("late-cancel", Self, NativeKinds.Cancel, null, "0/other", TargetCreation: 1), 900);
        _rows.Add(a);
        _report.Line($"arrival: {a.Describe()} from sender 0/other, withheld while the recover drain is open");

        // 5. C0's page is the final page: it restores R and sends C at seq 950 (another clock; valid on MongoDB).
        var first = Poll();
        Require(first.Count == 1 && first[0].Name == "C0", "only C0 is eligible after the open");
        Page(first[0], nextDrainSeq: 950);

        // 6. A poll at the current epoch takes C and R up: R is Seen = epoch, not yet handed to a model turn.
        var polled = Poll();
        _report.Line($"poll at epoch {_epoch}: [{string.Join(", ", polled.Select(p => p.Name))}]; {r.Describe()}");
        if (variant == "takeover-before-complete") Takeover("after R was marked seen, before any model turn received it");

        // 7. Delivery, one input per model turn, in the order the rule set dictates.
        for (var step = 0; step < 40; step++)
        {
            var next = Poll();
            if (next.Count == 0) break;
            var head = next[0];
            _report.Line($"deliver {head.Name} (order: [{string.Join(", ", next.Select(n => n.Name))}])");
            if (head.Kind == DrainKind) Page(head, nextDrainSeq: _nextSeq++);
            else
            {
                if (head.Name == "A")
                {
                    _aWhileHeld = _held.Contains(r.Key);
                    if (!held) _f2WaitingWhenAArrived = Json.Read<NativeState>(_instance.State).Waits.ContainsKey(f2Wait);
                }
                await ModelTurnAsync(head, Now, [], []);
            }
            if (_completed && variant == "takeover-after-complete") { _completed = false; Takeover("after Complete committed, before R reached a model turn"); }
        }

        var undone = _rows.Where(x => !x.Done && !x.Deleted).ToList();
        _report.Line($"model inputs: [{string.Join(", ", _modelInputs)}]");
        _report.Line($"rows: {string.Join("; ", _rows.Where(x => x.Name is not "start").Select(x => $"{x.Name} {x.Fate}"))}");
        _report.Line($"instance: status {_instance.Status}, resolution {(_instance.OpenGeneration is null ? "cleared" : "open")}, ResolutionGeneration {_instance.ResolutionGeneration}");

        if (!held)
        {
            var records = _records;
            _report.Expect("F completions (records c:" + fFrame + ":F)", records.Count(x => x == $"c:{fFrame}:F"), 1);
            _report.ExpectSequence("actual model transitions after Complete", _transitions, [$"R {NativeKinds.Timer} -> c:{fFrame}:F", $"A {NativeKinds.Cancel} -> end Cancelled"]);
            _report.Expect("F2 still waiting when A reached the model", _f2WaitingWhenAArrived, true);
            _report.Expect("F2 completions", records.Count(x => x == $"c:{f2Frame}:F2"), 0);
            _report.Expect("instance ended", _instance.Status, InstanceStatus.Cancelled);
            _report.Expect("R's fate", r.Fate, "consumed by a model turn");
            _report.Expect("undone rows remaining", undone.Count, 0);
        }
        else
        {
            _report.Line($"S held by the model when A was delivered: {(_modelInputs.Contains("A") ? _aWhileHeld ? "yes" : "no" : "A never delivered")}");
            _report.Expect("A reached a model turn", _modelInputs.Contains("A"), true);
            if (rules is FrRules.Repaired or FrRules.HeldBlocks)
                _report.Expect("the schedule exercised a held cohort row (S held when A was due)", _aWhileHeld || !_modelInputs.Contains("A") && _rows.Any(x => x.Name == "S" && _held.Contains(x.Key)), true);
            _report.Expect("H accepted the held signal (HeldGate.Accepts)", HeldGate.Accepts, 1);
            _report.Expect("instance ended", _instance.Status, InstanceStatus.Finished);
            _report.Expect("S's fate", r.Fate, "consumed by a model turn");
            _report.Expect("undone rows remaining", undone.Count, 0);
        }
        return _report;
    }

    private readonly List<string> _records = [];
    private readonly List<string> _transitions = [];

    private Message Drain(long seq) =>
        new Message($"{CreationProtocol.Origin(Self.Id, _instance.Creation)}#drain.{seq}", Self, DrainKind, Json.Write(new { Generation = _instance.OpenGeneration }), Self.Id,
            TargetCreation: _instance.Creation, SenderCreation: _instance.Creation).AsIssued();

    private static bool IsControl(string kind) => Kinds.IsControl(kind) || kind == DrainKind;

    private bool Cohort(InboxRow row) => !row.Done && row.ReleasedBy == (_instance.Creation, _instance.ResolutionGeneration);

    private bool Eligible(InboxRow row) =>
        _instance.OpenGeneration is not { } open || IsControl(row.Kind) || row.ReleasedBy == (_instance.Creation, open);

    private bool Blocked(InboxRow row) =>
        rules == FrRules.HeldBlocks && !IsControl(row.Kind) && !Cohort(row) && _rows.Any(x => _held.Contains(x.Key) && !x.Deleted && Cohort(x));

    /// <summary>The ordinary poll of the owner at its epoch: deliverable rows, marked seen, in the rule set's order.</summary>
    private List<InboxRow> Poll()
    {
        var deliverable = _rows.Where(x => !x.Deleted && !x.Done && !_held.Contains(x.Key) && (x.Message.Due ?? 0) <= Now && Eligible(x) && !Blocked(x)).ToList();
        foreach (var row in deliverable) row.Seen = _epoch;
        deliverable.Sort(InboxRow.Compare);
        return rules switch
        {
            FrRules.Reviewed => deliverable,
            FrRules.Negative => [.. deliverable.Where(x => IsControl(x.Kind)), .. deliverable.Where(x => !IsControl(x.Kind))],
            _ => [.. deliverable.Where(x => IsControl(x.Kind)), .. deliverable.Where(x => !IsControl(x.Kind) && Cohort(x)),
                  .. deliverable.Where(x => !IsControl(x.Kind) && !Cohort(x))],
        };
    }

    /// <summary>A kernel.drain turn: a page, or Complete when no kept row remains past the cursor and the gate row is taken up.</summary>
    private void Page(InboxRow control, long nextDrainSeq)
    {
        control.Done = true; control.Fate = "consumed (drain turn)";
        if (_instance.OpenGeneration is not { } generation) { _report.Line($"{control.Name}: no open resolution, consumed"); return; }
        var page = _rows.Where(x => x.KeptBy is not null && !x.Deleted && (_instance.Cursor is null || InboxRow.Compare(x, _instance.Cursor) > 0))
            .OrderBy(x => x, Comparer<InboxRow>.Create(InboxRow.Compare)).Take(PageLimit).ToList();
        if (page.Count > 0)
        {
            foreach (var row in page)
            { row.Done = false; row.KeptBy = null; row.Seen = 0; row.ReleasedBy = (_instance.Creation, generation); row.Fate = "released"; }
            _instance.Cursor = page[^1]; _instance.Gate = page[^1].Key;
            var next = new InboxRow("C", Drain(nextDrainSeq), nextDrainSeq);
            _rows.Add(next);
            _report.Line($"{control.Name} page: restored [{string.Join(", ", page.Select(p => p.Name))}] with ReleasedBy (1,{generation}); cursor -> {page[^1].Name}; gate row {page[^1].Name}; sends C = kernel.drain seq {nextDrainSeq}");
            return;
        }
        var gate = _rows.FirstOrDefault(x => x.Key == _instance.Gate);
        var takenUp = gate is null || gate.Deleted || gate.Done || gate.Seen == _epoch || _held.Contains(gate.Key);
        if (!takenUp) throw new InvalidOperationException("the gate waits; the harness schedules never reach this");
        _instance.OpenGeneration = null; _instance.Cursor = null;
        _completed = true;
        _report.Line($"{control.Name}: no kept row past the cursor and gate row {gate?.Name} taken up (seen {gate?.Seen} = epoch {_epoch}): Complete clears the resolution");
    }

    private void Takeover(string when)
    {
        _epoch++;
        _held.Clear();
        _report.Line($"takeover {when}: new owner at epoch {_epoch} rebuilds from durable rows only (memory, holds and polled rows are lost)");
    }

    /// <summary>One real Native turn over the head row and the held rows, then the commit of its effects.</summary>
    private async Task ModelTurnAsync(InboxRow head, long now, long[] seqs, string[] names)
    {
        if (_instance.ModelEnded)
        {
            head.Done = true; head.Fate = $"dropped (finished instance, {_instance.Status})";
            _report.Line($"{head.Name}: the instance has ended; consumed and dropped without a model turn (built finished-instance rule)");
            return;
        }
        var inbox = _rows.Where(x => _held.Contains(x.Key) && !x.Deleted && !x.Done).Append(head).Distinct().ToList();
        inbox.Sort(InboxRow.Compare);
        var host = new NativeHost(_info, now, ++_instance.Revision) { Inbox = inbox.Select(x => x.Message).ToList(), State = Json.Read<NativeState>(_instance.State) };
        await _model.TurnAsync(host, CancellationToken.None);
        Real.CountModelTurn();
        if (head.Name != "start") _modelInputs.Add(head.Name);

        var holds = host.HeldMessages.Select(m => m.InboxKey).ToHashSet();
        foreach (var row in inbox)
            if (holds.Contains(row.Key)) _held.Add(row.Key);
            else { row.Done = true; _held.Remove(row.Key); row.Fate = "consumed by a model turn"; }
        var recalled = new List<string>();
        foreach (var id in host.Recalled)
            foreach (var row in _rows.Where(x => x.Message.Id == id && !x.Done && !x.Deleted))
            { row.Deleted = true; row.Fate = $"recalled by {head.Name}'s turn (never reached the model)"; recalled.Add(row.Name); }
        for (var i = 0; i < host.Sent.Count; i++)
        {
            var sent = host.Sent[i];
            var name = i < names.Length ? names[i] : sent.Kind == NativeKinds.Continue ? $"K{_nextSeq}" : $"M{_nextSeq}";
            _rows.Add(new InboxRow(name, sent, i < seqs.Length ? seqs[i] : _nextSeq++));
        }
        if (host.EndStatus is { } end) { _instance.Status = end; _instance.ModelEnded = true; }
        _instance.State = Json.Write(host.State);
        _records.AddRange(host.Records);

        var parts = new List<string>();
        parts.AddRange(host.Records.Where(x => x.StartsWith("c:", StringComparison.Ordinal) || x.StartsWith("signal-", StringComparison.Ordinal)));
        if (host.EndStatus is { } status) parts.Add($"end {status}");
        if (holds.Count > 0) parts.Add($"held [{string.Join(", ", inbox.Where(x => holds.Contains(x.Key)).Select(x => x.Name))}]");
        if (recalled.Count > 0) parts.Add($"recalled [{string.Join(", ", recalled)}]");
        var sentNames = _rows.Skip(_rows.Count - host.Sent.Count).Select(x => x.Name).ToList();
        if (sentNames.Count > 0) parts.Add($"sent [{string.Join(", ", sentNames)}]");
        var handed = string.Join("+", inbox.Select(x => x.Name));
        if (head.Name != "start") _transitions.Add($"{handed} {head.Kind} -> {string.Join(", ", parts.Where(p => !p.StartsWith("sent", StringComparison.Ordinal) && !p.StartsWith("recalled", StringComparison.Ordinal)))}");
        _report.Line($"model turn {handed} ({head.Kind}): {(parts.Count == 0 ? "no records" : string.Join("; ", parts))}");
    }

    private static void Require(bool ok, string what) { if (!ok) throw new InvalidOperationException("harness schedule: " + what); }
}

/// <summary>
/// A harness-authored composite activity (an authored activity, not a stub model) that defers every signal until its
/// child completes, as Incarnation.Run's HeldReceiver does. It runs inside the real NativeModel, whose real signal path
/// (CompositeSignal.cs, FinishSignals) holds the deferred signal with ITurn.Hold.
/// </summary>
public sealed class HeldGate : CompositeActivity, ISignalledComposite
{
    public static int Accepts;
    protected override void Execute(CompositeContext context) { context.SetState(false); context.Schedule(context.Children.Single()); }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.SetState(true);
    public bool CanAccept(CompositeContext context) => context.GetState<bool>();
    public void Accept(CompositeContext context, string messageId, CompositeSignal signal) { Accepts++; context.Complete(); }
}
