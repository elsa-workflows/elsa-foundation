using System.Text.Json.Nodes;

namespace Kernel.Review5;

/// <summary>
/// The rule sets of FR-2 (R5-M3). <c>Reviewed</c> and <c>Negative</c> both gate a page on the Seen test (the reviewed
/// rule; the negative is the repaired set with that one rule restored, so in FR-2 the two coincide). Mutations of the
/// repaired set: <c>PagesWhileSuspended</c> lets a page run while the instance is suspended, counting its withheld rows
/// as handed off; <c>HeldGateWaits</c> counts a held gate row as not handed off.
/// </summary>
enum Fr2Rules { Reviewed, Repaired, Negative, PagesWhileSuspended, HeldGateWaits }

/// <summary>One inbox row as the fixture sees it (after FR-1's InboxRow): full key, seq, kind, release tag, Seen, Done, KeptBy.</summary>
sealed class Row(string name, Message message, long seq)
{
    public string Name { get; } = name;
    public Message Message { get; } = message;
    public long Seq { get; } = seq;
    public (long Creation, long Generation)? ReleasedBy;
    public long Seen;
    public bool Done;
    public string? KeptBy;
    public string Fate = "pending";
    public int Bytes { get; } = System.Text.Encoding.UTF8.GetByteCount(Json.Write(message));
    public InboxKey Key => Message.InboxKey;
    public string Kind => Message.Kind;
}

/// <summary>
/// FR-2: several full pages restored while the drain's controls win priority before any model handoff. The model is the
/// real <see cref="NativeModel"/>; the drain, the poll, the marks, the trials and ownership are the proposed transitions
/// over the built recovery path (poll InMemoryStore.cs 219–221 and Worker.cs 516–537, marks 1397, suspects 577–578,
/// trials 734–760), applied to rows the fixture keeps, as FR-1 does. One owner pass: poll and mark, at most one trial,
/// one turn of the subject, one turn of the neighbour.
/// </summary>
sealed class Fr2(string variant, Fr2Rules rules)
{
    public const string DrainKind = "kernel.drain";
    private const int PageLimit = 170;          // P_page
    private const int Pages = 3;                // kept rows: three full pages
    private const long RetryDelay = 3;          // a waiting page sends its kernel.drain with this delay (ms)
    private const int StepBound = 400;
    private const int ByteCredit = PageLimit * 24 * 1024;   // M x P_page, with M = 24 KiB (observability.md 774)

    private static readonly InstanceRef Self = new("0/subject");
    private static readonly InstanceRef Neighbour = new("0/neighbour");
    private readonly List<Row> _rows = [];
    private long _now = 2000, _epoch = 1, _nextSeq = 3000;
    private int _pollLimit = 500;
    // Owner memory: what a takeover loses.
    private readonly HashSet<InboxKey> _memory = [], _held = [], _suspects = [], _handedOff = [];
    private readonly HashSet<long> _mine = [1];
    private readonly HashSet<InboxKey> _kept = [];
    // The instance row's durable facts.
    private long? _openGeneration;
    private Row? _cursor;
    private InboxKey? _gate;
    private bool _suspended, _completed, _stalled;
    private string _state = "", _neighbourState = "";
    private NativeModel _model = null!;
    private InstanceInfo _info = null!, _neighbourInfo = null!;
    private long _revision = 1, _neighbourRevision = 1;
    private Report _report = null!;
    // Observations.
    private int _peakPending, _peakPendingBytes, _peakHeld, _largestPageBytes, _modelTurns, _controlTurns, _trials;
    private int _neighbourTurns, _neighbourDue, _turnsWhileSuspended, _heldAtLoss, _heldBytesAtLoss, _pages;
    private int _pendingBeforeFirstHandoff = -1;
    private readonly List<string> _gauge = [];

    public static readonly string[] Variants = ["base", "hold", "suspend", "takeover", "hold-takeover"];

    private bool Hold => variant is "hold" or "hold-takeover";
    private bool SeenGate => rules is Fr2Rules.Reviewed or Fr2Rules.Negative;

    public async Task<Report> RunAsync()
    {
        _report = new Report($"FR-2/{variant}", Program.Name(rules), variant switch
        {
            "hold" => "with a held gate row, the pages advance on handoff and pending non-held restored rows stay within one page",
            "suspend" => "a suspension stops the pages, and after resume the next page still waits for the handoff",
            "takeover" => "after a takeover between pages, the new owner hands off the durable restored rows before the next page",
            "hold-takeover" => "after a takeover that loses the model's holds, pending stays within one page plus the rows held at the loss",
            _ => "pending non-held restored rows never exceed one page (P_page rows, one page's bytes), whatever the control priority",
        });
        HeldGate.Accepts = 0;
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("fr2", Wf.Parallel("p", null, Wf.Node<HeldGate>("H").With(Wf.Event("go", "go")), Wf.Event("X", "never"))))
            .Add(Wf.Workflow("neighbour", Wf.Event("Z", "never")));
        _model = new NativeModel(registry);
        _info = new InstanceInfo(Self, NativeModel.ModelKey, "fr2", null, 1);
        _neighbourInfo = new InstanceInfo(Neighbour, NativeModel.ModelKey, "neighbour", null, 1);
        _state = Json.Write(_model.CreateState(NativeModel.Spec("fr2")));
        _neighbourState = Json.Write(_model.CreateState(NativeModel.Spec("neighbour")));
        (_state, _) = await Turn(_info, _state, [new Message("start", Self, Kinds.Start, Start: NativeModel.Spec("fr2"))], ++_revision);
        (_neighbourState, _) = await Turn(_neighbourInfo, _neighbourState, [new Message("start-n", Neighbour, Kinds.Start, Start: NativeModel.Spec("neighbour"))], ++_neighbourRevision);
        var hFrame = Json.Read<NativeState>(_state).Frames.Values.Single(f => f.Node == "H").Id;

        // Park kept three full pages of consumed rows: no-op resumes of about 1 KiB each and, in the hold variants, five
        // composite signals to H, the last of them the last row of page 1 (so page 1's gate row is one the model holds).
        var filler = JsonValue.Create(new string('x', 900));
        for (var i = 0; i < Pages * PageLimit; i++)
        {
            var seq = 1000 + i;
            var signal = Hold && (i is 20 or 60 or 100 or 140 or PageLimit - 1);
            var message = signal
                ? new Message($"signal-{i}", Self, CompositeSignal.Kind, Json.Write(new CompositeSignal(hFrame, "deferred")), "0/operator", TargetCreation: 1)
                : new Message($"noop-{i}", Self, NativeKinds.Resume, Json.Write(new ResumeStimulus("noop", filler.DeepClone())), "0/operator", TargetCreation: 1);
            _rows.Add(new Row(signal ? $"S{i}" : $"R{i}", message, seq) { Done = true, KeptBy = "Arrived", Fate = "kept" });
            _kept.Add(message.InboxKey);
        }
        _report.Line($"park: {Pages * PageLimit} kept rows at seq 1000-{1000 + Pages * PageLimit - 1}, {(Hold ? "five composite signals to H (one of them page 1's last row), the rest" : "all")} no-op native.resume rows of {_rows[0].Bytes} bytes");

        // Recover opens generation 1 and sends the first kernel.drain.
        _openGeneration = 1;
        _rows.Add(new Row("C1", Drain(), _nextSeq++));
        _report.Line("open: recover opens generation 1 and sends C1 = kernel.drain");
        if (Hold)
        {
            // The input H's child waits for arrives during the drain: unreleased, so withheld until Complete.
            _rows.Add(new Row("A", new Message("late-go", Self, NativeKinds.Resume, Json.Write(new ResumeStimulus("go")), "0/other", TargetCreation: 1), _nextSeq++));
            _report.Line("arrival: A = native.resume go from 0/other, withheld while the recover drain is open");
        }

        var pass = 0;
        for (; pass < StepBound; pass++, _now++)
        {
            Schedule(pass);
            _rows.Add(new Row($"N{pass}", new Message($"n-{pass}", Neighbour, NativeKinds.Resume, Json.Write(new ResumeStimulus("noop")), "0/other", TargetCreation: 1), _nextSeq++));
            Poll();
            var trial = NextTrial();
            if (trial is not null) await TrialAsync(trial);
            await SubjectTurnAsync();
            await NeighbourTurnAsync();
            Sample(pass);
            if (_completed && !_rows.Any(r => !r.Done && (r.ReleasedBy is not null || r.Name == "A"))) break;
        }
        if (pass == StepBound) { _stalled = true; _report.Line("stalled: the step bound was reached with work still pending"); }

        foreach (var line in _gauge) _report.Line(line);
        var restored = _rows.Where(r => _kept.Contains(r.Key)).ToList();
        _report.Line($"pages restored {_pages}; drain {(_completed ? "completed" : "open")}; subject model turns {_modelTurns} (trials {_trials}), control turns {_controlTurns}; neighbour turns {_neighbourTurns} of {_neighbourDue} passes with input");
        _report.Line($"pending non-held restored rows when the model first received one: {(_pendingBeforeFirstHandoff < 0 ? "never" : _pendingBeforeFirstHandoff.ToString())}");
        var bound = PageLimit + _heldAtLoss;
        var byteBound = _largestPageBytes + _heldBytesAtLoss;
        _report.Check(_peakPending <= bound, $"peak pending non-held restored rows: {_peakPending} (bound: P_page{(_heldAtLoss > 0 ? $" + {_heldAtLoss} held at the loss" : "")} = {bound})");
        _report.Check(_peakPendingBytes <= byteBound, $"peak pending non-held restored bytes: {_peakPendingBytes} (bound: the largest page's {_largestPageBytes} bytes{(_heldAtLoss > 0 ? $" + {_heldBytesAtLoss} bytes held at the loss" : "")} = {byteBound})");
        _report.Expect("held-row gauge peak (elsa.kernel.drain.held)", _peakHeld, Hold ? 5 : 0);
        _report.Expect("drain completed and nothing stalled", _completed && !_stalled, true);
        _report.Expect("restored rows consumed by model turns", restored.Count(r => r.Fate == "consumed by a model turn"), Pages * PageLimit);
        if (Hold) _report.Expect("H accepted a held signal once A completed its child (HeldGate.Accepts; the other held signals are consumed with H complete)", HeldGate.Accepts, 1);
        _report.Expect("the neighbour had a turn in every pass it had input (turn share)", _neighbourTurns, _neighbourDue);
        if (variant == "suspend") _report.Expect("model turns of the subject while suspended", _turnsWhileSuspended, 0);
        _report.Expect("instance still running (its Event 'never' waits)", Json.Read<NativeState>(_state).Waits.Values.Any(w => w.Key == "event:never"), true);
        return _report;
    }

    private Message Drain() =>
        new Message($"{CreationProtocol.Origin(Self.Id, 1)}#drain.{_nextSeq}", Self, DrainKind, Json.Write(new { Generation = _openGeneration }), Self.Id,
            TargetCreation: 1, SenderCreation: 1).AsIssued();

    private static bool IsControl(string kind) => Kinds.IsControl(kind) || kind == DrainKind;

    /// <summary>The variant's outside events, by pass.</summary>
    private void Schedule(int pass)
    {
        if (variant == "suspend" && _pages == 1 && !_rows.Any(r => r.Name == "S-op"))
        {
            _rows.Add(new Row("S-op", new Message("op-suspend", Self, Kinds.Suspend, null, "operator", TargetCreation: 1), _nextSeq++));
            _report.Line($"pass {pass}: an operator's suspend arrives after page 1");
        }
        if (variant == "suspend" && _suspended && !_rows.Any(r => r.Name == "R-op") && _rows.Single(r => r.Name == "S-op").Done && pass >= SuspendedAt + 6)
        {
            _rows.Add(new Row("R-op", new Message("op-resume", Self, Kinds.Resume, null, "operator", TargetCreation: 1), _nextSeq++));
            _report.Line($"pass {pass}: the operator's resume arrives");
        }
        if (variant is "takeover" or "hold-takeover" && _pages == 2 && !_tookOver)
        {
            _tookOver = true;
            _heldAtLoss = _held.Count;
            _heldBytesAtLoss = _rows.Where(r => _held.Contains(r.Key)).Sum(r => r.Bytes);
            _epoch++; _mine.Clear(); _mine.Add(_epoch);
            _memory.Clear(); _held.Clear(); _suspects.Clear(); _handedOff.Clear();
            _report.Line($"pass {pass}: crash takeover after page 2 committed: another process owns epoch {_epoch} and rebuilds from durable rows only (holds, memory and handoffs are lost; {_heldAtLoss} rows were held)");
        }
    }

    private bool _tookOver;
    private int SuspendedAt = int.MaxValue;

    /// <summary>
    /// The built poll with Horizon 0 (InMemoryStore.cs 219–221): not Done, Seen &lt; epoch, due, in seq order, up to the
    /// limit. A row in memory is skipped (Worker.cs 627–628); one another process marked is a suspect (577–578); every
    /// other polled row is marked seen at this epoch by the next flush (1397).
    /// </summary>
    private void Poll()
    {
        foreach (var row in _rows.Where(r => !r.Done && r.Seen < _epoch && (r.Message.Due ?? 0) <= _now).OrderBy(r => r.Seq).Take(_pollLimit).ToList())
        {
            if (!_memory.Add(row.Key)) continue;
            if (!IsControl(row.Kind) && row.Seen > 0 && !_mine.Contains(row.Seen)) _suspects.Add(row.Key);
            else row.Seen = _epoch;
        }
    }

    /// <summary>Released by the open (or last) generation and not Done: the drain's restored rows (FR-1's cohort).</summary>
    private static bool Released(Row row) => row.ReleasedBy is not null && !row.Done;

    /// <summary>What the subject may be given now: control kinds; while the drain is open, its released rows; never while suspended.</summary>
    private bool Eligible(Row row) => IsControl(row.Kind) || !_suspended && (_openGeneration is null || row.ReleasedBy == (1, _openGeneration));

    /// <summary>
    /// Worker.cs 734–760 with TrialGroup 1, as FR-1's repaired NextTrial for one instance: suspects in seq order, cohort
    /// suspects first while the drain is open or released rows remain; one suspect per trial.
    /// </summary>
    private Row? NextTrial()
    {
        if (_suspended) return null;
        return _rows.Where(r => _suspects.Contains(r.Key) && !r.Done && Eligible(r)).OrderBy(r => r.ReleasedBy is null ? 1 : 0).ThenBy(r => r.Seq).FirstOrDefault();
    }

    /// <summary>A trial: the fenced trial commit marks the row seen at this epoch, then it is delivered alone (Worker.cs 1398–1406).</summary>
    private async Task TrialAsync(Row row)
    {
        row.Seen = _epoch;
        _suspects.Remove(row.Key);
        _trials++;
        _report.Line($"trial of {row.Name} alone at epoch {_epoch}");
        await ModelTurnAsync([row]);
    }

    private async Task SubjectTurnAsync()
    {
        var offered = _rows.Where(r => r.Message.Target == Self && _memory.Contains(r.Key) && !_suspects.Contains(r.Key) && !r.Done && !_held.Contains(r.Key)
            && (r.Message.Due ?? 0) <= _now && Eligible(r)).OrderBy(r => IsControl(r.Kind) ? 0 : Released(r) ? 1 : 2).ThenBy(r => r.Seq).ToList();
        // Suspects still awaiting their trial block ordinary delivery behind them (Worker.cs 855: Awaits breaks the sequence).
        var firstSuspect = _rows.Where(r => _suspects.Contains(r.Key) && !r.Done).Select(r => r.Seq).DefaultIfEmpty(long.MaxValue).Min();
        if (offered.Count == 0) return;
        var head = offered[0];
        if (IsControl(head.Kind))
        {
            _controlTurns++;
            head.Done = true; head.Fate = "consumed (control turn)";
            if (head.Kind == DrainKind) Page(head);
            else if (head.Kind is Kinds.Suspend or Kinds.Resume)
            {
                _suspended = head.Kind == Kinds.Suspend;
                if (_suspended) SuspendedAt = (int)(_now - 2000);
                _report.Line($"{head.Name}: built {(_suspended ? "suspend" : "resume")} (WorkflowInstance.cs Control, mirrored); suspended {_suspended}");
            }
            return;
        }
        // The built batch: every offered row up to the first control, in seq order, behind no untried suspect. While a
        // restored (cohort) row remains, only cohort rows are offered (the release cohort precedence, R4-B1).
        var cohort = offered.Any(Released);
        var batch = offered.Where(r => !cohort || Released(r)).TakeWhile(r => !IsControl(r.Kind) && r.Seq < firstSuspect).ToList();
        if (batch.Count == 0) return;
        if (_pendingBeforeFirstHandoff < 0) _pendingBeforeFirstHandoff = Pending().Rows;
        await ModelTurnAsync(batch);
    }

    private async Task NeighbourTurnAsync()
    {
        var due = _rows.Where(r => r.Message.Target == Neighbour && _memory.Contains(r.Key) && !r.Done).OrderBy(r => r.Seq).ToList();
        if (due.Count == 0) return;
        _neighbourDue++;
        (_neighbourState, _) = await Turn(_neighbourInfo, _neighbourState, due.Select(r => r.Message).ToList(), ++_neighbourRevision);
        foreach (var row in due) { row.Done = true; row.Fate = "consumed by the neighbour"; }
        _neighbourTurns++;
    }

    /// <summary>One real Native turn over the batch and the held rows, then the commit of its effects.</summary>
    private async Task ModelTurnAsync(List<Row> batch)
    {
        if (_suspended) _turnsWhileSuspended++;
        var inbox = _rows.Where(r => _held.Contains(r.Key) && !r.Done).Concat(batch).Distinct().OrderBy(r => r.Seq).ToList();
        (_state, var host) = await Turn(_info, _state, inbox.Select(r => r.Message).ToList(), ++_revision);
        _modelTurns++;
        var holds = host.HeldMessages.Select(m => m.InboxKey).ToHashSet();
        foreach (var row in inbox)
        {
            // Handed off: delivered into a model turn that committed, or held by the model (proposed, R5-M3).
            _handedOff.Add(row.Key);
            if (holds.Contains(row.Key)) _held.Add(row.Key);
            else { row.Done = true; _held.Remove(row.Key); row.Fate = "consumed by a model turn"; }
        }
        if (inbox.Count > 1 || holds.Count > 0)
            _report.Line($"model turn at {_now}: {inbox.Count} input(s) [{inbox[0].Name}..{inbox[^1].Name}]{(holds.Count > 0 ? $", held [{string.Join(", ", inbox.Where(r => holds.Contains(r.Key)).Select(r => r.Name))}]" : "")}");
    }

    private async Task<(string State, NativeHost Host)> Turn(InstanceInfo info, string state, IReadOnlyList<Message> inbox, long revision)
    {
        var host = new NativeHost(info, _now, revision) { Inbox = inbox, State = Json.Read<NativeState>(state) };
        await _model.TurnAsync(host, CancellationToken.None);
        Real.CountModelTurn();
        foreach (var sent in host.Sent) _rows.Add(new Row($"M{_nextSeq}", sent, _nextSeq++));
        return (Json.Write(host.State), host);
    }

    /// <summary>Restored rows not handed off: released, not Done, not held (the proposed pending gauge beside elsa.kernel.drain.held).</summary>
    private (int Rows, int Bytes) Pending()
    {
        var pending = _rows.Where(r => Released(r) && !_held.Contains(r.Key)).ToList();
        return (pending.Count, pending.Sum(r => r.Bytes));
    }

    private void Sample(int pass)
    {
        var (rows, bytes) = Pending();
        var held = _rows.Count(r => Released(r) && _held.Contains(r.Key));
        if (rows > _peakPending || bytes > _peakPendingBytes || held > _peakHeld)
            _gauge.Add($"gauge at pass {pass}: pending non-held restored rows {rows} ({bytes} bytes), held {held}");
        _peakPending = Math.Max(_peakPending, rows);
        _peakPendingBytes = Math.Max(_peakPendingBytes, bytes);
        _peakHeld = Math.Max(_peakHeld, held);
    }

    /// <summary>
    /// A kernel.drain turn. It pages when the gate passes, completes when no kept row is left past the cursor and the gate
    /// passes, and otherwise sends a delayed retry. Reviewed gate: the previous page's last row is taken up (Done, held, or
    /// Seen at this epoch by a poll). Repaired gate: that row is handed off (consumed by a committed model turn, or held), and
    /// the owner has at most one page (P_page rows or one page's bytes) of restored rows not handed off. A page waits while
    /// the instance is suspended under both.
    /// </summary>
    private void Page(Row control)
    {
        if (_openGeneration is not { } generation) return;
        if (_suspended && rules != Fr2Rules.PagesWhileSuspended) { Retry(control, "the instance is suspended"); return; }
        var gate = _rows.FirstOrDefault(r => r.Key == _gate);
        if (gate is not null && !GatePasses(gate, out var why)) { Retry(control, why); return; }
        var page = _rows.Where(r => r.KeptBy is not null && (_cursor is null || r.Seq > _cursor.Seq)).OrderBy(r => r.Seq).Take(PageLimit).ToList();
        if (page.Count > 0)
        {
            foreach (var row in page) { row.Done = false; row.KeptBy = null; row.Seen = 0; row.ReleasedBy = (1, generation); row.Fate = "released"; }
            _cursor = page[^1]; _gate = page[^1].Key;
            _largestPageBytes = Math.Max(_largestPageBytes, page.Sum(r => r.Bytes));
            _pages++;
            var next = new Row($"C{_pages + 1}", Drain(), _nextSeq++);
            _rows.Add(next);
            _report.Line($"{control.Name} at {_now}: page {_pages} restores {page.Count} rows [{page[0].Name}..{page[^1].Name}], {page.Sum(r => r.Bytes)} bytes; gate row {page[^1].Name}; sends {next.Name}; pending before it {Pending().Rows - page.Count}");
            return;
        }
        _openGeneration = null; _cursor = null; _completed = true;
        _report.Line($"{control.Name} at {_now}: no kept row past the cursor and the gate passes: Complete");
    }

    private bool GatePasses(Row gate, out string why)
    {
        if (SeenGate)
        {
            var takenUp = gate.Done || _held.Contains(gate.Key) || gate.Seen == _epoch && !_suspects.Contains(gate.Key);
            why = $"gate row {gate.Name} not taken up (seen {gate.Seen}, epoch {_epoch})";
            return takenUp;
        }
        var handedOff = gate.Done || _held.Contains(gate.Key) && rules != Fr2Rules.HeldGateWaits
            || _suspended && rules == Fr2Rules.PagesWhileSuspended;
        if (!handedOff) { why = $"gate row {gate.Name} not handed off{(_held.Contains(gate.Key) ? " (held, counted as not handed off)" : "")}"; return false; }
        var (rows, bytes) = rules == Fr2Rules.PagesWhileSuspended && _suspended ? (0, 0) : Pending();
        why = $"{rows} restored rows ({bytes} bytes) not handed off, over one page";
        return rows <= PageLimit && bytes <= ByteCredit;
    }

    private void Retry(Row control, string why)
    {
        var retry = new Row($"{control.Name}'", Drain() with { Due = _now + RetryDelay }, _nextSeq++);
        _rows.Add(retry);
        if (_lastWait != why) _report.Line($"{control.Name} at {_now}: the page waits ({why}); retry due {_now + RetryDelay}");
        _lastWait = why;
    }

    private string _lastWait = "";
}
