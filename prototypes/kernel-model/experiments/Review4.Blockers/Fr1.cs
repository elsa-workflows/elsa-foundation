namespace Kernel.Review4;

/// <summary>
/// The rule sets of FR-1 (R4-B1). Mutations of the repaired set:
/// <c>HeldBlocks</c> counts a held cohort row in the precedence (E-8);
/// <c>MemoryPrecedence</c> keeps the cohort in the owner's memory only (E-7);
/// <c>TrialPlainSeq</c> turns cohort-first trials off: suspects are tried in plain seq order, as the built NextTrial does;
/// <c>SkipTarget</c> lets a withheld suspect skip its whole target, as the built NextTrial does;
/// <c>NoShelf</c> does not shelve (mark seen) a suspect the cohort rule passes over;
/// <c>ControlsWaitOnTicket</c> counts job tickets as cohort rows, so controls wait on a hanging ticket (E-11);
/// <c>BoundaryHoldsCohort</c> keeps cohort suspects behind a control's recovery boundary, as built;
/// <c>ClearWithHeld</c> clears the cohort mark as soon as the read finds no non-held cohort row, while a held one remains;
/// <c>BoundaryHoldsCohortNonsuspect</c> keeps a cohort row that is no suspect behind the boundary in ordinary delivery, as built (E-7 reset run);
/// <c>NoCohortMark</c> drops the durable cohort mark, so the owner knows only the cohort rows it has read;
/// <c>OneRowReadCountsHeld</c> makes the cohort read return one row, held keys included (E-8 run 2);
/// <c>MarkIgnoresControls</c> lets a control polled while the mark is set run ahead of the cohort read (E-7 control run).
/// </summary>
enum FrRules { Reviewed, Repaired, Negative, HeldBlocks, MemoryPrecedence, TrialPlainSeq, NoCohortMark, OneRowReadCountsHeld, MarkIgnoresControls,
    SkipTarget, NoShelf, ControlsWaitOnTicket, BoundaryHoldsCohort, BoundaryHoldsCohortNonsuspect, ClearWithHeld }

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
    public bool Deleted;   // a recalled row: no longer in the inbox
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

/// <summary>The durable facts of the instance row the drain needs: creation, ParkRecord's generation, the open Resolution, the cohort mark, status, model state.</summary>
sealed class FrInstance
{
    public long Creation = 1;
    public long Revision = 1;
    public long ResolutionGeneration;
    public long? OpenGeneration;          // ParkRecord.Resolution, null when none is open
    public InboxRow? Cursor;              // the last tuple a page reached
    public InboxKey? Gate;                // the last row the previous page restored
    public (long Creation, long Generation)? CohortMark; // proposed: set by Complete while a released, not-Done row may remain
    public InstanceStatus Status = InstanceStatus.Running;
    public bool ModelEnded;
    public string State = "{}";
}

/// <summary>
/// FR-1: the final released row R and a late model-ending arrival A, with the drain control C between them.
/// The model is the real <see cref="NativeModel"/> with its real handlers. The drain, the poll, the suspect trials and
/// ownership are the proposed transitions over the built Worker's recovery path (Worker.cs 516–537 poll, 577–578
/// suspects, 734–760 NextTrial, 1397 marks), applied here to durable rows the harness keeps.
/// </summary>
sealed class Fr1(string variant, FrRules rules)
{
    public const string DrainKind = "kernel.drain";
    private const long Now = 1060;      // F's timer (due 1050) is due; F2's (due 1100) is not
    private const int PageLimit = 170;  // P_page
    private const int TrialGroup = 1;   // Worker.cs 35, the default

    private static readonly InstanceRef Self = new("0/subject");
    private readonly List<InboxRow> _rows = [];
    private readonly FrInstance _instance = new();
    // Owner memory: what a takeover loses.
    private readonly HashSet<InboxKey> _memory = [];       // rows a poll (or a cohort read) has returned
    private readonly HashSet<InboxKey> _suspects = [];     // polled suspects not yet tried (Worker.cs 577-578)
    private readonly HashSet<InboxKey> _held = [];         // rows a model turn handed back
    private readonly HashSet<InboxKey> _memoryCohort = []; // rows this owner saw a page release (MemoryPrecedence only)
    private readonly HashSet<long> _mine = [1];            // epochs this process held: their marks are not suspect
    private readonly List<string> _modelInputs = [];
    private readonly List<string> _records = [];
    private readonly List<string> _transitions = [];
    private long _epoch = 1;
    private int _pollLimit = 500;                          // Worker.cs 14, the default
    private long _nextSeq = 2000;
    private bool _completed, _stalled;
    private NativeModel _model = null!;
    private InstanceInfo _info = null!;
    private Report _report = null!;
    private bool _aWhileHeld;
    private bool _f2WaitingWhenAArrived;
    private string _f2Wait = "";
    private InboxRow _r = null!;
    private InboxRow? _r1;
    private string _xFrame = "";
    private bool _r1HeldAtR2;
    private string _terminatedAfter = "";
    private readonly List<string> _applied = [];
    private bool _suspended;
    private bool _readFound;
    private bool _crashed, _r1AfterCrash;
    private readonly HashSet<InboxKey> _recoveryOrdering = [];
    private string _lastRead = "";
    private static readonly InstanceRef Neighbour = new("0/neighbour");

    public static readonly string[] Variants =
        ["base", "takeover-before-complete", "takeover-after-complete", "trial-takeover", "trial-takeover-open", "poll-limit", "model-end", "model-end-recall", "held",
         "held-poll-limit", "control-after-complete", "ticket-control", "ticket-control-parent", "ticket-control-suspend",
         "control-crash", "control-reset", "trial-takeover-open-poll-limit", "held-crash"];

    private bool Precedence => rules is not (FrRules.Reviewed or FrRules.Negative);
    private bool DurableMark => Precedence && rules is not (FrRules.MemoryPrecedence or FrRules.NoCohortMark);
    private bool CohortTrials => Precedence && rules != FrRules.TrialPlainSeq;
    private bool SkipsTarget => rules is FrRules.Reviewed or FrRules.SkipTarget;
    private bool Shelves => rules != FrRules.NoShelf;
    private bool TicketsCount => rules == FrRules.ControlsWaitOnTicket;
    private bool CohortBeforeBoundary => CohortTrials && rules != FrRules.BoundaryHoldsCohort;
    private bool CohortOfferedPastBoundary => CohortTrials && rules != FrRules.BoundaryHoldsCohortNonsuspect;
    private static bool IsTicket(string kind) => kind is Kinds.Job or Kinds.Meet or Kinds.Publish;

    public async Task<Report> RunAsync()
    {
        var held = variant == "held";
        var modelEnd = variant is "model-end" or "model-end-recall";
        _report = new Report($"FR-1/{variant}", Program.Name(rules), variant switch
        {
            "model-end" => "an earlier cohort row that ends the model leaves R to the finished-instance rule (dropped), and the drain completes with no KeptBy left",
            "model-end-recall" => "control: the ending turn recalls R, so R never reaches a model turn, and the drain completes with no KeptBy left",
            "held" => "a row the model holds never blocks delivery; the held cohort row still receives the input it waits for",
            "held-poll-limit" => "with R1 held and R2 not yet polled, the mark's check skips held keys and finds R2, so R2 reaches the model before A ends it",
            "control-after-complete" or "control-crash" or "control-reset" => "a control polled while the cohort mark is set waits until the check finds no non-held cohort row, so R reaches the model before the terminate",
            "held-crash" => "the mark stays while a held cohort row remains: after a crash, the new owner's read finds R1 and delivers it before the untagged A",
            "ticket-control" or "ticket-control-parent" or "ticket-control-suspend" => "a restored job ticket is never a cohort row: with J hanging and the mark set, controls apply at once",
            _ => "the final released cohort reaches the model before a later-delivered arrival that ends it (F completes once)",
        });
        HeldGate.Accepts = 0;
        var heldPoll = variant == "held-poll-limit";
        var heldCrash = variant == "held-crash";
        var control = variant is "control-after-complete" or "control-crash" or "control-reset";
        var ticket = variant.StartsWith("ticket-control", StringComparison.Ordinal);
        var workflow = held || heldCrash ? "held" : heldPoll ? "held2" : variant == "model-end" || ticket ? "evt" : "two";
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("two", Wf.Parallel("p", null, Wf.Delay("F", 50), Wf.Delay("F2", 100))))
            .Add(Wf.Workflow("evt", Wf.Event("go", "go")))
            .Add(Wf.Workflow("held", Wf.Node<HeldGate>("H").With(Wf.Event("go", "go"))))
            .Add(Wf.Workflow("held2", Wf.Parallel("p", null, Wf.Node<HeldGate>("H").With(Wf.Event("go", "go")), Wf.Event("X", "x"))));
        _model = new NativeModel(registry);
        var spec = NativeModel.Spec(workflow);
        _info = new InstanceInfo(Self, NativeModel.ModelKey, workflow, null, _instance.Creation);
        _instance.State = Json.Write(_model.CreateState(spec));

        // 1. Start: a real Native turn arms the waits (existing prototype behaviour).
        var start = new InboxRow("start", new Message("start", Self, Kinds.Start, Start: spec), 100);
        _rows.Add(start);
        await ModelTurnAsync(start, now: 1000, seqs: workflow == "two" ? [1100, 1101] : [], names: workflow == "two" ? ["R", "T2"] : []);
        var state = Json.Read<NativeState>(_instance.State);
        string fFrame = "", f2Frame = "";
        if (workflow == "two")
        {
            fFrame = state.Frames.Values.Single(f => f.Node == "F").Id;
            f2Frame = state.Frames.Values.Single(f => f.Node == "F2").Id;
            _f2Wait = state.Waits.Single(w => w.Value.Frame == f2Frame).Key;
            _r = _rows.Single(x => x.Name == "R");
            Require(state.Waits.Single(w => w.Value.Frame == fFrame).Value.Key == _r.Key.Id, "R is F's timer");
            _report.Line($"start: real Native turn armed F ({fFrame}) with timer R = {_r.Key.Id} due {_r.Message.Due} and F2 ({f2Frame}) with timer T2 due {_rows.Single(x => x.Name == "T2").Message.Due}");
        }
        else if (heldPoll)
        {
            var hFrame = state.Frames.Values.Single(f => f.Node == "H").Id;
            _xFrame = state.Frames.Values.Single(f => f.Node == "X").Id;
            _r1 = new InboxRow("R1", new Message("signal-1", Self, CompositeSignal.Kind, Json.Write(new CompositeSignal(hFrame, "deferred")), "0/operator", TargetCreation: 1), 1000);
            _r = new InboxRow("R2", new Message("resume-x", Self, NativeKinds.Resume, Json.Write(new ResumeStimulus("x")), "0/operator", TargetCreation: 1), 1100);
            _rows.Add(_r1); _rows.Add(_r);
            _report.Line($"start: real Native turn ran Parallel(H ({hFrame}), a composite that defers signals until its child Event 'go' completes; Event X ({_xFrame})); R1 = composite signal to H at seq 1000, R2 = native.resume x at seq 1100");
        }
        else if (heldCrash)
        {
            var hFrame = state.Frames.Values.Single(f => f.Node == "H").Id;
            _r = new InboxRow("R1", new Message("signal-1", Self, CompositeSignal.Kind, Json.Write(new CompositeSignal(hFrame, "deferred")), "0/operator", TargetCreation: 1), 1000);
            _rows.Add(_r);
            _report.Line($"start: real Native turn ran H ({hFrame}), a composite that defers signals until its child Event 'go' completes; R1 = composite signal to H at seq 1000");
        }
        else if (ticket)
        {
            var key = InboxKey.Ticket(Self.Id, "j", 1);
            _r = new InboxRow("J", new Message(key.Id, Self, Kinds.Job, Json.Write(new { Key = "j" }), Self.Id, TargetCreation: 1, SenderCreation: 1).WithInboxScope(key.Scope), 1100);
            _rows.Add(_r);
            _report.Line($"start: real Native turn subscribed Event 'go'; J = job ticket {key.Id} at seq 1100, which a retry selects");
        }
        else if (held)
        {
            var hFrame = state.Frames.Values.Single(f => f.Node == "H").Id;
            _r = new InboxRow("S", new Message("signal-1", Self, CompositeSignal.Kind, Json.Write(new CompositeSignal(hFrame, "deferred")), "0/operator", TargetCreation: 1), 1100);
            _rows.Add(_r);
            _report.Line($"start: real Native turn ran H ({hFrame}), a composite that defers signals until its child Event 'go' completes; S = composite signal to H at seq 1100");
        }
        else
        {
            _r = new InboxRow("R", new Message("resume-go", Self, NativeKinds.Resume, Json.Write(new ResumeStimulus("go")), "0/operator", TargetCreation: 1), 1100);
            _rows.Add(_r);
            _report.Line("start: real Native turn subscribed Event 'go'; R = native.resume go at seq 1100, a row the model never recalls");
        }

        // 2. Park (proposed): R arrived and was consumed while parked, so it is kept with KeptBy = Arrived.
        _instance.Status = InstanceStatus.Faulted;
        if (_r1 is not null) { _r1.Done = true; _r1.KeptBy = "Arrived"; _r1.Fate = "kept"; _report.Line($"park: {_r1.Describe()}"); }
        _r.Done = true; _r.KeptBy = ticket ? "Selected" : "Arrived"; _r.Fate = "kept";
        _report.Line($"park: {_r.Describe()}");
        if (modelEnd)
        {
            var q = new InboxRow("Q", new Message("parked-cancel", Self, NativeKinds.Cancel, null, "0/other2", TargetCreation: 1), 1000) { Done = true, KeptBy = "Arrived", Fate = "kept" };
            _rows.Add(q);
            _report.Line($"park: {q.Describe()}, an earlier kept row that ends the model");
        }

        // 3. Recover Open (proposed): generation 1, cursor before the first row, instance running, first kernel.drain.
        _instance.ResolutionGeneration = 1; _instance.OpenGeneration = 1; _instance.Cursor = null; _instance.Status = InstanceStatus.Running;
        _rows.Add(new InboxRow("C0", Drain(800), 800));
        _report.Line("open: recover opens generation 1; ParkRecord.ResolutionGeneration = 1; C0 = kernel.drain seq 800");

        // 4. A arrives during the drain from another sender, with a lower seq than R: withheld (not released).
        InboxRow? a = modelEnd || control || ticket ? null : held
            ? new InboxRow("A", new Message("late-go", Self, NativeKinds.Resume, Json.Write(new ResumeStimulus("go")), "0/other", TargetCreation: 1), 900)
            : new InboxRow("A", new Message("late-cancel", Self, NativeKinds.Cancel, null, "0/other", TargetCreation: 1), 900);
        if (a is not null)
        {
            _rows.Add(a);
            _report.Line($"arrival: {a.Describe()} from sender 0/other, withheld while the recover drain is open");
        }

        // 5. The owner polls; its flush marks every polled row, the withheld A included (Worker.cs 1397). C0's page is the
        // final page: it restores R and sends C at seq 950 (another clock; valid on MongoDB).
        PollStore();
        var first = Offer();
        Require(first.Count == 1 && first[0].Name == "C0", "only C0 is eligible after the open");
        Page(first[0], nextDrainSeq: 950);

        // 6. A poll at the current epoch takes C and R up: R is Seen = epoch, not yet handed to a model turn.
        PollStore();
        _report.Line($"marks at epoch {_epoch}: {string.Join("; ", _rows.Where(x => _memory.Contains(x.Key) && !x.Done).Select(x => x.Describe()))}");
        if (variant == "takeover-before-complete") Takeover(crash: false, "after R was marked seen, before any model turn received it");
        if (variant is "trial-takeover-open" or "trial-takeover-open-poll-limit") Takeover(crash: true, "after R and A were marked seen, before Complete and before any model turn received R");
        if (variant == "trial-takeover-open-poll-limit") { _pollLimit = 1; _report.Line("the new owner's PollLimit is 1: one row per poll, in seq order"); }

        // 7. Delivery, one input per model turn, in the order the rule set dictates.
        var step = 0;
        for (; step < 40; step++)
        {
            var polled = PollStore();
            if (rules == FrRules.MarkIgnoresControls && _instance.CohortMark is not null &&
                Offer(ignoreMark: true).FirstOrDefault(x => IsControl(x.Kind)) is { } early)
            {
                _report.Line($"deliver {early.Name}: a control, offered in the built order while the cohort mark is set (mark-ignores-controls)");
                await DeliverAsync(early);
                continue;
            }
            var read = CohortRead();
            if (heldCrash && !_crashed && _instance.OpenGeneration is null && _rows.Any(x => _held.Contains(x.Key) && Cohort(x)) && !CohortPending())
            {
                _crashed = true;
                Takeover(crash: true, "after the read found no non-held cohort row, while the model still holds R1 and before R1 is consumed");
                _pollLimit = 1; _report.Line("the new owner's PollLimit is 1: one row per poll, in seq order");
                continue;
            }
            var trial = NextTrial();
            if (trial is not null)
            {
                trial.Seen = _epoch;
                _suspects.Remove(trial.Key);
                _report.Line($"trial of {trial.Name} alone (Worker.cs 734-760, TrialGroup {TrialGroup}); the trial commit marks it seen at epoch {_epoch}");
                await DeliverAsync(trial);
            }
            else
            {
                var next = Offer();
                if (next.Count == 0)
                {
                    if (polled == 0 && !read && !CohortPending()) break;
                    continue;
                }
                _report.Line($"deliver {next[0].Name} (order: [{string.Join(", ", next.Select(n => n.Name))}])");
                await DeliverAsync(next[0]);
            }
            if (_completed)
            {
                _completed = false;
                if (variant == "takeover-after-complete") Takeover(crash: false, "after Complete committed, before R reached a model turn");
                if (variant == "trial-takeover") Takeover(crash: true, "after Complete committed, before R reached a model turn");
                if (variant == "poll-limit") { Takeover(crash: true, "after Complete committed, before R reached a model turn"); _pollLimit = 1; _report.Line("the new owner's PollLimit is 1: one row per poll, in seq order"); }
                if (heldPoll)
                {
                    for (var n = 1; n <= 4; n++)
                        _rows.Add(new InboxRow($"N{n}", new Message($"neighbour-{n}", Neighbour, NativeKinds.Resume, null, "0/other", TargetCreation: 1), 1000 + n));
                    _report.Line("arrivals for another instance of the partition: N1..N4 at seq 1001-1004");
                    Takeover(crash: false, "after Complete committed, before R1 or R2 reached a model turn");
                    _pollLimit = 2; _report.Line("the new owner's PollLimit is 2: two rows per poll, in seq order");
                }
                if (ticket)
                {
                    _report.Line("J's driver entered the job, which hangs: the ticket stays undone");
                    var controls = variant switch
                    {
                        "ticket-control" => new[] { new InboxRow("K", new Message("op-terminate", Self, Kinds.Terminate, null, "operator", TargetCreation: 1), 1000) },
                        "ticket-control-parent" => [new InboxRow("K", new Message("parent-terminate", Self, Kinds.Terminate, null, "0/parent", TargetCreation: 1, SenderCreation: 1), 1000)],
                        _ => [new InboxRow("S1", new Message("op-suspend", Self, Kinds.Suspend, null, "operator", TargetCreation: 1), 1000),
                              new InboxRow("S2", new Message("op-resume", Self, Kinds.Resume, null, "operator", TargetCreation: 1), 1001)],
                    };
                    foreach (var c in controls) { _rows.Add(c); _report.Line($"arrival: {c.Describe()} from {c.Message.Sender}"); }
                }
                if (control)
                {
                    var k = new InboxRow("K", new Message("parent-terminate", Self, Kinds.Terminate, null, "0/parent", TargetCreation: 1, SenderCreation: 1), 1000);
                    _rows.Add(k);
                    _report.Line($"arrival: {k.Describe()}, the parent's tree terminate (a control kind)");
                    if (variant == "control-crash")
                        Takeover(crash: true, "after Complete committed, before R reached a model turn; R carries the lost owner's mark");
                    else if (variant == "control-reset")
                        Takeover(crash: false, "after an unknown commit outcome: Reset sets Epoch = 0 and drops memory (Worker.cs 476-502), then the same process takes the partition again; R keeps this process's earlier mark");
                    else
                    {
                        Takeover(crash: false, "after Complete committed, before R reached a model turn");
                        _pollLimit = 1; _report.Line("the new owner's PollLimit is 1: one row per poll, in seq order");
                    }
                }
            }
        }
        if (step == 40) { _stalled = true; _report.Line("stalled: the step bound was reached with work still pending"); }
        else if (!_instance.ModelEnded && _rows.Any(x => !x.Done && !x.Deleted && (x.Message.Due ?? 0) <= Now && x.Message.Target.Id == Self.Id && !IsTicket(x.Kind)))
        { _stalled = true; _report.Line("stalled: no row can move, and work is still pending"); }

        var undone = _rows.Where(x => !x.Done && !x.Deleted && (x.Message.Due ?? 0) <= Now).ToList();
        _report.Line($"model inputs: [{string.Join(", ", _modelInputs)}]");
        _report.Line($"rows: {string.Join("; ", _rows.Where(x => x.Name is not "start").Select(x => $"{x.Name} {x.Fate}"))}");
        _report.Line($"instance: status {_instance.Status}, resolution {(_instance.OpenGeneration is null ? "cleared" : "open")}, ResolutionGeneration {_instance.ResolutionGeneration}, cohort mark {(_instance.CohortMark is { } m ? $"({m.Creation},{m.Generation})" : "none")}");

        if (ticket)
        {
            _report.Expect("model inputs", string.Join(",", _modelInputs), "");
            _report.Expect("controls applied, in arrival order", string.Join(",", _applied), variant == "ticket-control-suspend" ? "S1 suspend,S2 resume" : "K terminate");
            _report.Expect("drain completed and nothing stalled", _instance.OpenGeneration is null && !_stalled, true);
            if (variant == "ticket-control-suspend")
            {
                _report.Expect("instance", $"{_instance.Status}, suspended {_suspended}", "Running, suspended False");
                _report.Expect("J", _r.Fate, "released");
            }
            else
            {
                _report.Expect("instance ended", _instance.Status, InstanceStatus.Cancelled);
                _report.Expect("J", _r.Fate, "cancelled by the terminate");
                _report.Expect("J's late result", _rows.SingleOrDefault(x => x.Name == "Jr")?.Fate, "dropped (finished instance, Cancelled)");
            }
        }
        else if (heldCrash)
        {
            _report.Expect("model inputs", string.Join(",", _modelInputs), "R1,R1,A");
            _report.Expect("R1 reached the model again after the takeover, before A", _r1AfterCrash, true);
            _report.Expect("R1's fate (handed back with the ending input)", _r.Fate, "consumed by a model turn");
            _report.Expect("H's deferred-signal records (signal-deferred:signal-1)", _records.Count(x => x.StartsWith("signal-deferred:signal-1:", StringComparison.Ordinal)), 2);
            _report.Expect("instance ended", _instance.Status, InstanceStatus.Cancelled);
            _report.Expect("undone rows remaining", undone.Count, 0);
        }
        else if (heldPoll)
        {
            _report.Expect("model inputs", string.Join(",", _modelInputs), "R1,R2,A");
            _report.Expect("R1 held by the model when R2 was delivered", _r1HeldAtR2, true);
            _report.Expect("X completions (records c:" + _xFrame + ":X)", _records.Count(x => x == $"c:{_xFrame}:X"), 1);
            _report.Expect("R2's fate", _r.Fate, "consumed by a model turn");
            _report.Expect("instance ended", _instance.Status, InstanceStatus.Cancelled);
            _report.Expect("R1's fate (handed back with the ending input, as held rows are)", _r1!.Fate, "consumed by a model turn");
            _report.Expect("undone rows remaining", undone.Count, 0);
        }
        else if (control)
        {
            _report.Expect("model inputs", string.Join(",", _modelInputs), "R");
            _report.Expect("F completions (records c:" + fFrame + ":F)", _records.Count(x => x == $"c:{fFrame}:F"), 1);
            _report.Expect("R's fate", _r.Fate, "consumed by a model turn");
            _report.Expect("the terminate ran after R (built terminate)", _terminatedAfter, "R");
            _report.Expect("instance ended", _instance.Status, InstanceStatus.Cancelled);
            _report.Expect("undone rows remaining", undone.Count, 0);
        }
        else if (modelEnd)
        {
            _report.Expect("model inputs", string.Join(",", _modelInputs), "Q");
            _report.Expect("instance ended", _instance.Status, InstanceStatus.Cancelled);
            _report.Expect("drain completed", _instance.OpenGeneration is null && !_stalled, true);
            _report.Expect("R's fate", _r.Fate, variant == "model-end" ? "dropped (finished instance, Cancelled)" : "recalled by Q's turn (never reached the model)");
            _report.Expect("rows still kept", _rows.Count(x => x.KeptBy is not null), 0);
            _report.Expect("undone rows remaining", undone.Count, 0);
        }
        else if (!held)
        {
            _report.Expect("F completions (records c:" + fFrame + ":F)", _records.Count(x => x == $"c:{fFrame}:F"), 1);
            _report.ExpectSequence("actual model transitions after the start", _transitions, [$"R {NativeKinds.Timer} -> c:{fFrame}:F", $"A {NativeKinds.Cancel} -> end Cancelled"]);
            _report.Expect("F2 still waiting when A reached the model", _f2WaitingWhenAArrived, true);
            _report.Expect("F2 completions", _records.Count(x => x == $"c:{f2Frame}:F2"), 0);
            _report.Expect("instance ended", _instance.Status, InstanceStatus.Cancelled);
            _report.Expect("drain completed", _instance.OpenGeneration is null && !_stalled, true);
            _report.Expect("R's fate", _r.Fate, "consumed by a model turn");
            _report.Expect("undone rows remaining", undone.Count, 0);
        }
        else
        {
            _report.Line($"S held by the model when A was delivered: {(_modelInputs.Contains("A") ? _aWhileHeld ? "yes" : "no" : "A never delivered")}");
            _report.Expect("A reached a model turn", _modelInputs.Contains("A"), true);
            if (rules is FrRules.Repaired or FrRules.HeldBlocks)
                _report.Expect("the schedule exercised a held cohort row (S held when A was due)", _aWhileHeld || !_modelInputs.Contains("A") && _held.Contains(_r.Key), true);
            _report.Expect("H accepted the held signal (HeldGate.Accepts)", HeldGate.Accepts, 1);
            _report.Expect("instance ended", _instance.Status, InstanceStatus.Finished);
            _report.Expect("S's fate", _r.Fate, "consumed by a model turn");
            _report.Expect("undone rows remaining", undone.Count, 0);
        }
        return _report;
    }

    private Message Drain(long seq) =>
        new Message($"{CreationProtocol.Origin(Self.Id, _instance.Creation)}#drain.{seq}", Self, DrainKind, Json.Write(new { Generation = _instance.OpenGeneration }), Self.Id,
            TargetCreation: _instance.Creation, SenderCreation: _instance.Creation).AsIssued();

    private static bool IsControl(string kind) => Kinds.IsControl(kind) || kind == DrainKind;

    /// <summary>A cohort row: released by the creation's latest generation and not Done. A job ticket never is one (proposed, E-11).</summary>
    private bool Cohort(InboxRow row) => !row.Done && !row.Deleted && row.ReleasedBy == (_instance.Creation, _instance.ResolutionGeneration) &&
        (TicketsCount || !IsTicket(row.Kind)) && (rules != FrRules.MemoryPrecedence || _memoryCohort.Contains(row.Key));

    /// <summary>
    /// A non-held cohort row the owner knows of, in memory or found by the mark's read: what the precedence waits for.
    /// </summary>
    private bool CohortPending() => Precedence && (_readFound || _rows.Any(x => _memory.Contains(x.Key) && !_held.Contains(x.Key) && Cohort(x)));

    /// <summary>The proposed drain eligibility: while a recover drain is open, only control kinds and this resolution's released rows.</summary>
    private bool Eligible(InboxRow row) =>
        _instance.OpenGeneration is not { } open || IsControl(row.Kind) || row.ReleasedBy == (_instance.Creation, open);

    private bool Blocked(InboxRow row) =>
        rules == FrRules.HeldBlocks && !IsControl(row.Kind) && !Cohort(row) && _rows.Any(x => _held.Contains(x.Key) && Cohort(x));

    /// <summary>
    /// The store poll as built, with Horizon 0 (InMemoryStore.cs 217–221: <c>Seen &lt; epoch</c>, not Done,
    /// <c>Due &lt;= now + Horizon</c>, in seq order, at most the
    /// poll limit) and the owner's handling of each row: a row already in memory is ignored (Worker.cs 532, the item is
    /// held already); a row another owner marked is a suspect (Worker.cs 577–578); every other polled row is marked seen
    /// at this epoch by the next flush (Worker.cs 1397), whether or not the drain withholds it. An untried, unshelved
    /// suspect keeps its old Seen, so it comes back in every poll and fills a place there (F12).
    /// </summary>
    private int PollStore()
    {
        var rows = _rows.Where(x => !x.Done && !x.Deleted && (x.Message.Due ?? 0) <= Now && x.Seen < _epoch)
            .OrderBy(x => x, Comparer<InboxRow>.Create(InboxRow.Compare)).Take(_pollLimit).ToList();
        var fresh = rows.Where(x => !_memory.Contains(x.Key)).ToList();
        foreach (var row in fresh)
            if (row.Message.Target.Id == Self.Id) Remember(row);
            else { row.Done = true; row.Fate = "consumed by its own instance's turn"; _memory.Add(row.Key); }
        if (rows.Count > 0 && _epoch > 1)
            _report.Line($"poll at epoch {_epoch} (limit {_pollLimit}): [{string.Join(", ", rows.Select(x => x.Name + (_suspects.Contains(x.Key) ? " (suspect)" : "") + (fresh.Contains(x) ? "" : ", already in memory")))}]");
        return fresh.Count;
    }

    private void Remember(InboxRow row)
    {
        _memory.Add(row.Key);
        // Worker.cs 617/636: a previously marked row (Seen or Trial) carries RecoveryOrdering, even when this process marked it.
        if (row.Seen > 0) _recoveryOrdering.Add(row.Key);
        if (!IsControl(row.Kind) && row.Seen > 0 && !_mine.Contains(row.Seen)) _suspects.Add(row.Key);
        else row.Seen = _epoch;
    }

    /// <summary>
    /// Worker.cs 796–820, RecoveryBoundary: for an owner that is not on trial, there is a boundary only while some row in
    /// memory carries RecoveryOrdering or is a suspect; it is then the earliest due control still pending.
    /// </summary>
    private InboxRow? RecoveryBoundary() =>
        _rows.Any(x => _memory.Contains(x.Key) && !x.Done && !x.Deleted && (_recoveryOrdering.Contains(x.Key) || _suspects.Contains(x.Key)))
            ? _rows.Where(x => _memory.Contains(x.Key) && !x.Done && !x.Deleted && IsControl(x.Kind) && (x.Message.Due ?? 0) <= Now)
                .OrderBy(x => x, Comparer<InboxRow>.Create(InboxRow.Compare)).FirstOrDefault()
            : null;

    /// <summary>
    /// Proposed: the mark's read, an existence check. While the durable cohort mark is set and this owner holds no
    /// non-held cohort row in memory, it asks whether the instance has a cohort row (released by the marked generation,
    /// not Done, not a ticket kind), passing over the keys the model holds. It loads nothing: the row reaches memory
    /// through the ordinary poll. Two outcomes are kept apart, as on the page ("The durable cohort mark", "Clearing the
    /// mark"): when it finds no non-held cohort row, offering resumes, with the held rows handed back, and the mark stays
    /// while any held cohort row remains; only a read that finds no cohort row at all, held or not, clears the mark, in
    /// that read's commit. The <c>OneRowReadCountsHeld</c> mutation checks one row, held keys included; the
    /// <c>ClearWithHeld</c> mutation clears the mark as soon as no non-held cohort row remains.
    /// </summary>
    private bool CohortRead()
    {
        _readFound = false;
        if (!DurableMark || _instance.CohortMark is not { } mark || _rows.Any(x => _memory.Contains(x.Key) && !_held.Contains(x.Key) && Cohort(x))) return false;
        var candidates = _rows.Where(x => !x.Done && !x.Deleted && x.ReleasedBy == mark && (TicketsCount || !IsTicket(x.Kind)))
            .OrderBy(x => x, Comparer<InboxRow>.Create(InboxRow.Compare)).ToList();
        if (rules == FrRules.OneRowReadCountsHeld)
        {
            var one = candidates.Take(1).ToList();
            if (one.Count == 1 && _held.Contains(one[0].Key))
            {
                ReadLine($"one-row cohort check returns {one[0].Name}, which the model holds: nothing for the mark to wait for");
                return false;
            }
            candidates = one;
        }
        var found = candidates.FirstOrDefault(x => !_held.Contains(x.Key));
        if (found is null)
        {
            var heldRows = candidates.Where(x => _held.Contains(x.Key)).Select(x => x.Name).ToList();
            if (heldRows.Count == 0 || rules == FrRules.ClearWithHeld)
            {
                _instance.CohortMark = null;
                ReadLine(heldRows.Count == 0 ? "cohort read finds no cohort row at all: its commit clears the mark"
                    : $"cohort read finds no non-held cohort row: the mark is cleared although the model holds [{string.Join(", ", heldRows)}] (clear-with-held)");
                return true;
            }
            ReadLine($"cohort read finds no non-held cohort row: offering resumes with [{string.Join(", ", heldRows)}] handed back, and the mark stays while they remain");
            return false;
        }
        _readFound = true;
        ReadLine($"cohort check (mark set, held keys and tickets skipped): {found.Name} exists and is not in memory; the mark stays");
        return false;
    }

    /// <summary>The page: a model end clears the mark in the end's commit.</summary>
    private void EndClearsMark()
    {
        if (_instance.CohortMark is null) return;
        _instance.CohortMark = null;
        _report.Line("the model's end clears the cohort mark in the end's commit");
    }

    private void ReadLine(string line)
    {
        if (line == _lastRead) return;
        _lastRead = line;
        _report.Line(line);
    }

    /// <summary>A suspend or resume of the running instance: the actor type's built control (WorkflowInstance.cs, Control), mirrored.</summary>
    private void SuspendOrResume(InboxRow head)
    {
        head.Done = true; head.Fate = "consumed (built control)";
        _suspended = head.Kind == Kinds.Suspend;
        _applied.Add($"{head.Name} {(head.Kind == Kinds.Suspend ? "suspend" : "resume")}");
        _report.Line($"{head.Name}: built {(head.Kind == Kinds.Suspend ? "suspend" : "resume")} (WorkflowInstance.cs Control, mirrored), applied; suspended {_suspended}");
    }

    /// <summary>
    /// A terminate of the running instance: the actor type's built terminate (WorkflowInstance.cs 179–203, mirrored),
    /// with no model turn. The instance ends Cancelled, and what is still pending meets the finished-instance rule.
    /// </summary>
    private void BuiltTerminate(InboxRow head)
    {
        head.Done = true; head.Fate = "consumed (built terminate)";
        if (_instance.ModelEnded) { _report.Line($"{head.Name}: the instance has ended; the terminate changes nothing"); return; }
        _terminatedAfter = string.Join(",", _modelInputs);
        _instance.Status = InstanceStatus.Cancelled; _instance.ModelEnded = true; _held.Clear(); EndClearsMark();
        _applied.Add($"{head.Name} terminate");
        var cancelled = _rows.Where(x => IsTicket(x.Kind) && !x.Done && !x.Deleted).ToList();
        foreach (var t in cancelled)
        {
            t.Deleted = true; t.Fate = "cancelled by the terminate";
            // The hanging driver finishes later; its result reaches the ended instance.
            _rows.Add(new InboxRow($"{t.Name}r", new Message($"{t.Key.Id}.late", Self, Kinds.JobFinished, Json.Write(new JobFinished("j", "late", null)), Self.Id, TargetCreation: 1, SenderCreation: 1), _nextSeq++));
        }
        _report.Line($"{head.Name}: built terminate (WorkflowInstance.cs 179-203, mirrored), Running -> Cancelled, no model turn" +
            (cancelled.Count > 0 ? $"; cancels [{string.Join(", ", cancelled.Select(t => t.Name))}]" : ""));
    }

    /// <summary>
    /// Worker.cs 734–760, NextTrial, mirrored for one instance: suspects in seq order; those past the earliest pending
    /// control (the recovery boundary, 796–820, by seq for durable rows) wait; a suspect the instance cannot be given
    /// skips the whole target (the built rule, which the proposed per-row drain withholding feeds); one suspect per group
    /// with TrialGroup 1. "Cannot be given" is the built instance-level <c>Withheld(slot)</c> with its exemptions
    /// (Worker.cs 756); feeding the drain's per-row withholding into it is proposed. The repaired rules change three
    /// things: a withheld suspect skips only itself, never its target; no non-cohort suspect is tried ahead of a cohort
    /// row or grouped with one; and a suspect the cohort rule passes over is shelved, marked seen at this epoch and
    /// neither on trial nor cleared to run, as the built flush shelves a suspect of a withheld instance (Worker.cs
    /// 691–699, F12), so polls move past it.
    /// </summary>
    private InboxRow? NextTrial()
    {
        var suspects = _rows.Where(x => _suspects.Contains(x.Key) && !x.Done && !x.Deleted)
            .OrderBy(x => x, Comparer<InboxRow>.Create(InboxRow.Compare)).ToList();
        if (suspects.Count == 0) return null;
        var boundary = _rows.Where(x => _memory.Contains(x.Key) && !x.Done && !x.Deleted && IsControl(x.Kind))
            .OrderBy(x => x, Comparer<InboxRow>.Create(InboxRow.Compare)).FirstOrDefault();
        var skipped = false;
        // Proposed: while the drain is open or the cohort mark is set, cohort suspects are tried first, in tuple order,
        // ahead of a control's cut. The control takes its built place once no non-held cohort row remains.
        if (CohortBeforeBoundary && (_instance.OpenGeneration is not null || _instance.CohortMark is not null))
            foreach (var suspect in suspects)
            {
                if ((suspect.Message.Due ?? 0) > Now) continue;
                if (!Eligible(suspect)) { if (SkipsTarget) break; continue; }   // the skip-target rule applies here too
                if (!Cohort(suspect) || _held.Contains(suspect.Key)) continue;
                if (boundary is not null && suspect.Seq >= boundary.Seq)
                    _report.Line($"{suspect.Name} is a cohort suspect behind {boundary.Name}'s recovery boundary: it is tried first");
                return suspect;
            }
        foreach (var suspect in suspects)
        {
            if ((suspect.Message.Due ?? 0) > Now || boundary is not null && suspect.Seq >= boundary.Seq) continue;
            if (skipped) continue;
            if (!Eligible(suspect))
            {
                // The built flush shelves a withheld instance's suspect (Worker.cs 691–699, F12) under either rule; the
                // built NextTrial also skips its target, the repaired one does not.
                if (SkipsTarget) skipped = true;
                if (Shelves && suspect.Seen != _epoch)
                {
                    suspect.Seen = _epoch;
                    _report.Line($"{suspect.Name} is shelved: the open drain withholds it, so it is passed over{(SkipsTarget ? " with its target skipped" : "")} and the flush marks it seen at epoch {_epoch}");
                }
                continue;
            }
            if (CohortTrials && !Cohort(suspect) && CohortPending())
            {
                if (Shelves && suspect.Seen != _epoch)
                {
                    suspect.Seen = _epoch;
                    _report.Line($"{suspect.Name} is shelved: the cohort rule passes it over, so the flush marks it seen at epoch {_epoch}, neither on trial nor cleared to run");
                }
                continue;
            }
            return suspect;   // Math.Max(1, TrialGroup >> attempts) == 1
        }
        return null;
    }

    /// <summary>What the owner offers to a turn: polled, marked, not held, eligible rows, in the rule set's order.</summary>
    private List<InboxRow> Offer(bool ignoreMark = false)
    {
        var deliverable = _rows.Where(x => _memory.Contains(x.Key) && !_suspects.Contains(x.Key) && !x.Deleted && !x.Done && !_held.Contains(x.Key) && !IsTicket(x.Kind)
            && (x.Message.Due ?? 0) <= Now && Eligible(x) && !Blocked(x)).ToList();
        deliverable.Sort(InboxRow.Compare);
        // Worker.cs 829–842, Deliverable: with a recovery boundary, only the inputs in front of it (BeforeRecoveryControl,
        // by seq for durable rows), then the control itself. Proposed: while the drain is open or the mark is set, a
        // cohort row is exempt from the cut.
        if (RecoveryBoundary() is { } cut)
        {
            var exempt = CohortOfferedPastBoundary && (_instance.OpenGeneration is not null || _instance.CohortMark is not null);
            deliverable = deliverable.Where(x => x == cut || !IsControl(x.Kind) && (x.Seq < cut.Seq || exempt && Cohort(x))).ToList();
            var held = deliverable.Where(x => !IsControl(x.Kind) && x.Seq >= cut.Seq).Select(x => x.Name).ToList();
            ReadLine($"recovery boundary at {cut.Name}: rows past it are cut{(held.Count > 0 ? $", except the cohort rows [{string.Join(", ", held)}]" : "")}");
        }
        var open = _instance.OpenGeneration is not null;
        if (rules == FrRules.Reviewed) return deliverable;
        if (rules == FrRules.Negative)
            return open ? [.. deliverable.Where(x => IsControl(x.Kind)), .. deliverable.Where(x => !IsControl(x.Kind))] : deliverable;
        if (open)
            return [.. deliverable.Where(x => IsControl(x.Kind)), .. deliverable.Where(x => !IsControl(x.Kind) && Cohort(x)),
                    .. deliverable.Where(x => !IsControl(x.Kind) && !Cohort(x))];
        // After Complete: the remaining non-held cohort rows first; a control waits behind them; no other row of the
        // instance is offered while one remains (the cohort mark's rule, proposed).
        var cohort = deliverable.Where(Cohort).ToList();
        return CohortPending() && !ignoreMark ? cohort : [.. cohort, .. deliverable.Where(x => !Cohort(x))];
    }

    private async Task DeliverAsync(InboxRow head)
    {
        if (head.Kind == DrainKind) { Page(head, nextDrainSeq: _nextSeq++); return; }
        if (head.Kind == Kinds.Terminate) { BuiltTerminate(head); return; }
        if (head.Kind is Kinds.Suspend or Kinds.Resume) { SuspendOrResume(head); return; }
        if (_r1 is not null && head == _r) _r1HeldAtR2 = _held.Contains(_r1.Key);
        if (_crashed && head == _r && !_instance.ModelEnded) _r1AfterCrash = true;
        if (head.Name == "A")
        {
            _aWhileHeld = _held.Contains(_r.Key);
            if (_f2Wait.Length > 0) _f2WaitingWhenAArrived = Json.Read<NativeState>(_instance.State).Waits.ContainsKey(_f2Wait);
        }
        await ModelTurnAsync(head, Now, [], []);
    }

    /// <summary>A kernel.drain turn: a page, Complete when no kept row remains past the cursor and the gate row is taken up, or a delayed retry.</summary>
    private void Page(InboxRow control, long nextDrainSeq)
    {
        control.Done = true; control.Fate = "consumed (drain turn)";
        if (_instance.OpenGeneration is not { } generation) { _report.Line($"{control.Name}: no open resolution, consumed"); return; }
        var page = _rows.Where(x => x.KeptBy is not null && !x.Deleted && (_instance.Cursor is null || InboxRow.Compare(x, _instance.Cursor) > 0))
            .OrderBy(x => x, Comparer<InboxRow>.Create(InboxRow.Compare)).Take(PageLimit).ToList();
        if (page.Count > 0)
        {
            foreach (var row in page)
            { row.Done = false; row.KeptBy = null; row.Seen = 0; row.ReleasedBy = (_instance.Creation, generation); row.Fate = "released"; _memoryCohort.Add(row.Key); }
            _instance.Cursor = page[^1]; _instance.Gate = page[^1].Key;
            _rows.Add(new InboxRow("C", Drain(nextDrainSeq), nextDrainSeq));
            _report.Line($"{control.Name} page: restored [{string.Join(", ", page.Select(p => p.Name))}] with ReleasedBy (1,{generation}); cursor -> {page[^1].Name}; gate row {page[^1].Name}; sends C = kernel.drain seq {nextDrainSeq}");
            return;
        }
        var gate = _rows.FirstOrDefault(x => x.Key == _instance.Gate);
        var takenUp = gate is null || gate.Deleted || gate.Done || gate.Seen == _epoch && !_suspects.Contains(gate.Key) || _held.Contains(gate.Key);
        if (!takenUp)
        {
            var retry = new InboxRow($"C{nextDrainSeq}", Drain(nextDrainSeq), nextDrainSeq);
            _rows.Add(retry);
            _report.Line($"{control.Name}: gate row {gate!.Name} not taken up (seen {gate.Seen}, epoch {_epoch}{(_suspects.Contains(gate.Key) ? ", an untried suspect" : "")}): the page waits, sending {retry.Name}");
            return;
        }
        _instance.OpenGeneration = null; _instance.Cursor = null;
        if (DurableMark && _rows.Any(x => !x.Done && !x.Deleted && x.ReleasedBy == (_instance.Creation, generation)))
            _instance.CohortMark = (_instance.Creation, generation);
        _completed = true;
        _report.Line($"{control.Name}: no kept row past the cursor and gate row {gate?.Name} taken up (seen {gate?.Seen} = epoch {_epoch}): Complete clears the resolution" +
            (_instance.CohortMark is not null ? $" and sets the cohort mark (1,{generation})" : ""));
    }

    /// <summary>
    /// A new owner at the next epoch keeps only the durable rows. A crash hands the partition to another process, so the
    /// lost owner's marks make suspects (Worker.cs 577–578); a retake by the same process does not.
    /// </summary>
    private void Takeover(bool crash, string when)
    {
        _epoch++;
        if (crash) _mine.Clear();
        _mine.Add(_epoch);
        _memory.Clear(); _suspects.Clear(); _held.Clear(); _memoryCohort.Clear(); _recoveryOrdering.Clear();
        _report.Line($"{(crash ? "crash takeover by another process" : "retake by the same process")} {when}: new owner at epoch {_epoch} rebuilds from durable rows only (memory, holds and polled rows are lost)");
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
        if (host.EndStatus is { } end) { _instance.Status = end; _instance.ModelEnded = true; _held.Clear(); } // held rows meet the finished-instance rule
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
        if (host.EndStatus is not null) EndClearsMark();
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
