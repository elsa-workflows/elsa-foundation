using System.Text.Json.Nodes;

namespace Kernel.Review5;

/// <summary>
/// The rule sets of FR-2 (R5-M3). <c>Reviewed</c>: the Seen gate and pages of P_page rows. <c>Repaired</c>: the
/// coordinator's final rule (a page is admitted once the previous gate row is handed off; it restores at most P_page - u
/// rows and stops before a row that would take the stored bytes of the u rows and the page above B_credit; with u = 0 its
/// first row always goes; a page cut to zero waits). <c>Negative</c>: the repaired rule removed, which is the reviewed set
/// again. Mutations of the repaired set: <c>SeenGate</c> (the Seen test in place of handoff, the credit kept),
/// <c>CreditMetadataOnly</c>, <c>PPageWhateverU</c>, <c>ZeroCutCompletes</c>, <c>PagesWhileSuspended</c> (a page runs while
/// suspended, the withheld rows counted as handed off) and <c>HeldGateWaits</c> (a held gate row counted as not handed off).
/// </summary>
enum Fr2Rules { Reviewed, Repaired, Negative, SeenGate, CreditMetadataOnly, PPageWhateverU, ZeroCutCompletes, PagesWhileSuspended, HeldGateWaits }

/// <summary>One inbox row as the fixture sees it (after FR-1's InboxRow): full key, seq, kind, release tag, Seen, Done, KeptBy, stored bytes.</summary>
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
    /// <summary>Stored metadata: the row's encoding without its payload.</summary>
    public int MetaBytes { get; } = System.Text.Encoding.UTF8.GetByteCount(Json.Write(message with { Payload = null }));
    /// <summary>Stored bytes: metadata plus payload length.</summary>
    public int Bytes { get; } = System.Text.Encoding.UTF8.GetByteCount(Json.Write(message with { Payload = null })) + System.Text.Encoding.UTF8.GetByteCount(message.Payload ?? "");
    public InboxKey Key => Message.InboxKey;
    public string Kind => Message.Kind;
}

/// <summary>
/// FR-2: restored pages while the drain's controls win priority before any model handoff. The model is the real
/// <see cref="NativeModel"/>; the drain, the poll, the marks, the trials and ownership are the proposed transitions over the
/// built recovery path (poll InMemoryStore.cs 219–221 and Worker.cs 516–537, marks 1397, suspects 577–578, trials 733–763,
/// batching 845–877), applied to rows the fixture keeps, as FR-1 does. One owner pass, in RunAsync's order (Worker.cs
/// 322–354): poll and mark, one turn of S and one of the neighbour N (each ready instance one turn per pass), then the
/// flush's trial selection (1396–1398), at most one suspect, delivered alone.
/// </summary>
sealed class Fr2(string variant, Fr2Rules rules)
{
    public const string DrainKind = "kernel.drain";
    private const int PageLimit = 170;                 // P_page
    private const long BCredit = 4L * 1024 * 1024;     // B_credit = 4 MiB
    private const long RetryDelay = 3;                 // a waiting page sends its kernel.drain with this delay (ms)
    private const int StepBound = 400;

    private static readonly InstanceRef Self = new("0/subject");
    private static readonly InstanceRef Neighbour = new("0/neighbour");
    private readonly List<Row> _rows = [];
    private readonly HashSet<InboxKey> _kept = [];
    private long _now = 2000, _epoch = 1, _nextSeq = 3000;
    // Owner memory: what a takeover loses.
    private readonly HashSet<InboxKey> _memory = [], _held = [], _suspects = [];
    private readonly HashSet<long> _mine = [1];
    // The instance row's durable facts.
    private long? _openGeneration;
    private Row? _cursor;
    private InboxKey? _gate;
    private bool _suspended, _completed, _stalled, _tookOver;
    private int _suspendedAt = int.MaxValue;
    private string _state = "", _neighbourState = "", _lastWait = "";
    private NativeModel _model = null!;
    private InstanceInfo _info = null!, _neighbourInfo = null!;
    private long _revision = 1, _neighbourRevision = 1;
    private Report _report = null!;
    private readonly List<List<Row>> _pageRows = [];
    // Observations.
    private int _peakPending, _peakHeld, _modelTurns, _controlTurns, _trials, _neighbourTurns, _passes, _turnsWhileSuspended;
    private long _peakPendingBytes;
    private int _pendingBeforeFirstHandoff = -1, _peakMiB = 0;
    private bool _bigAlone;
    private readonly List<string> _gauge = [];

    public static readonly string[] Variants = ["base", "hold", "suspend", "takeover", "hold-takeover", "bytes", "zero-cut"];

    private bool Hold => variant is "hold" or "hold-takeover";
    private bool SeenTest => rules is Fr2Rules.Reviewed or Fr2Rules.Negative or Fr2Rules.SeenGate;
    private bool Credit => rules is not (Fr2Rules.Reviewed or Fr2Rules.Negative);

    public async Task<Report> RunAsync()
    {
        _report = new Report($"FR-2/{variant}", Program.Name(rules), variant switch
        {
            "hold" => "with a held gate row, the pages advance on handoff and pending non-held restored rows stay within one page",
            "suspend" => "a suspension stops the pages, and after resume the next page waits for the handoff",
            "takeover" => "after a takeover between pages, the new owner hands off the durable restored rows (by trial) before the next page",
            "hold-takeover" => "after a takeover that loses the model's holds, the next page restores at most P_page - u rows",
            "bytes" => "1 MiB rows restore at most three at a time, and a 16 MiB row is restored alone",
            "zero-cut" => "a page the byte credit cuts to zero waits and never completes the drain",
            _ => "pending non-held restored rows never exceed P_page or B_credit, whatever the control priority",
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

        // Park kept rows: no-op native.resume rows (about 1.2 KiB, 1 MiB or 16 MiB) and composite signals to H, which the model holds.
        Message Noop(int i, int payloadChars) => new($"noop-{i}", Self, NativeKinds.Resume, Json.Write(new ResumeStimulus("noop", JsonValue.Create(new string('x', payloadChars)))), "0/operator", TargetCreation: 1);
        Message Signal(int i, int valueChars) => new($"signal-{i}", Self, CompositeSignal.Kind, Json.Write(new CompositeSignal(hFrame, "deferred", valueChars == 0 ? null : new string('v', valueChars))), "0/operator", TargetCreation: 1);
        var kept = variant switch
        {
            "bytes" => [.. Enumerable.Range(0, 10).Select(i => Noop(i, 1024 * 1024 - 64)), Noop(10, 16 * 1024 * 1024 - 2048)],
            "zero-cut" => [Signal(0, 4 * 1024 * 1024 - 1024), .. Enumerable.Range(1, 2 * PageLimit).Select(i => Noop(i, 900))],
            _ => Enumerable.Range(0, 3 * PageLimit).Select(i => Hold && i is 20 or 60 or 100 or 140 or PageLimit - 1 ? Signal(i, 0) : Noop(i, 900)).ToList(),
        };
        for (var i = 0; i < kept.Count; i++)
        {
            var m = kept[i];
            _rows.Add(new Row(m.Kind == CompositeSignal.Kind ? $"S{i}" : $"R{i}", m, 1000 + i) { Done = true, KeptBy = "Arrived", Fate = "kept" });
            _kept.Add(m.InboxKey);
        }
        _report.Line($"park: {kept.Count} kept rows at seq 1000-{999 + kept.Count}: {Describe()}");
        _openGeneration = 1;
        _rows.Add(new Row("C1", Drain(), _nextSeq++));
        _report.Line("open: recover opens generation 1 and sends C1 = kernel.drain");
        if (Hold || variant == "zero-cut")
        {
            _rows.Add(new Row("A", new Message("late-go", Self, NativeKinds.Resume, Json.Write(new ResumeStimulus("go")), "0/other", TargetCreation: 1), _nextSeq++));
            _report.Line("arrival: A = native.resume go from 0/other, withheld while the recover drain is open");
        }

        var pass = 0;
        for (; pass < StepBound; pass++, _now++)
        {
            Schedule(pass);
            _rows.Add(new Row($"N{pass}", new Message($"n-{pass}", Neighbour, NativeKinds.Resume, Json.Write(new ResumeStimulus("noop")), "0/other", TargetCreation: 1), _nextSeq++));
            Poll();
            await SubjectTurnAsync();
            await NeighbourTurnAsync();
            // The flush after the turns selects a trial (Worker.cs 1396–1398), delivered alone.
            var trial = NextTrial();
            if (trial is not null) await TrialAsync(trial);
            Sample(pass);
            if (_completed && !_rows.Any(r => !r.Done && (r.ReleasedBy is not null || r.Name == "A"))) break;
        }
        _passes = pass + 1;
        if (pass == StepBound) { _stalled = true; _report.Line("stalled: the step bound was reached with work still pending"); }

        foreach (var line in _gauge) _report.Line(line);
        var restored = _rows.Where(r => _kept.Contains(r.Key)).ToList();
        _report.Line($"pages restored {_pageRows.Count} [{string.Join(", ", _pageRows.Select(p => p.Count))}]; drain {(_completed ? "completed" : "open")}; rows still kept {restored.Count(r => r.KeptBy is not null)}");
        _report.Line($"turns: S {_modelTurns} model (trials {_trials}) and {_controlTurns} control; the neighbour {_neighbourTurns} in {_passes} passes (one turn per instance per pass)");
        _report.Line($"pending non-held restored rows when the model first received one: {(_pendingBeforeFirstHandoff < 0 ? "never" : _pendingBeforeFirstHandoff.ToString())}");
        var largest = kept.Max(m => _rows.First(r => r.Key == m.InboxKey).Bytes);
        _report.Check(_peakPending <= PageLimit, $"peak pending non-held restored rows: {_peakPending} (bound P_page = {PageLimit})");
        _report.Check(_peakPendingBytes <= Math.Max(BCredit, largest), $"peak pending non-held restored bytes: {_peakPendingBytes} (bound: B_credit {BCredit}, or one larger row alone, {largest})");
        _report.Expect("held-row gauge peak (elsa.kernel.drain.held)", _peakHeld, Hold ? 5 : variant == "zero-cut" ? 1 : 0);
        _report.Expect("drain completed and nothing stalled", _completed && !_stalled, true);
        _report.Expect("restored rows consumed by model turns", restored.Count(r => r.Fate == "consumed by a model turn"), kept.Count);
        _report.Expect("rows still kept (never restored)", restored.Count(r => r.KeptBy is not null), 0);
        if (Hold || variant == "zero-cut") _report.Expect("H accepted a held signal once A completed its child (HeldGate.Accepts)", HeldGate.Accepts, 1);
        if (variant == "suspend") _report.Expect("model turns of S while suspended", _turnsWhileSuspended, 0);
        if (variant == "bytes")
        {
            _report.Expect("most 1 MiB rows pending at once", _peakMiB, 3);
            _report.Expect("the 16 MiB row restored alone, with no other restored row pending", _bigAlone, true);
        }
        _report.Expect("S still running (its Event 'never' waits)", Json.Read<NativeState>(_state).Waits.Values.Any(w => w.Key == "event:never"), true);
        return _report;
    }

    private string Describe() => variant switch
    {
        "bytes" => $"ten no-op rows of {_rows[0].Bytes} stored bytes, then one of {_rows[10].Bytes}",
        "zero-cut" => $"S0, a composite signal to H of {_rows[0].Bytes} stored bytes, then {2 * PageLimit} no-op rows of {_rows[1].Bytes}",
        _ => Hold ? $"five composite signals to H (one of them page 1's last row) and no-op rows of {_rows[0].Bytes} stored bytes" : $"no-op native.resume rows of {_rows[0].Bytes} stored bytes",
    };

    private Message Drain() =>
        new Message($"{CreationProtocol.Origin(Self.Id, 1)}#drain.{_nextSeq}", Self, DrainKind, Json.Write(new { Generation = _openGeneration }), Self.Id,
            TargetCreation: 1, SenderCreation: 1).AsIssued();

    private static bool IsControl(string kind) => Kinds.IsControl(kind) || kind == DrainKind;

    /// <summary>The variant's outside events, by pass.</summary>
    private void Schedule(int pass)
    {
        if (variant == "suspend" && _pageRows.Count == 1 && !_rows.Any(r => r.Name == "S-op"))
        {
            _rows.Add(new Row("S-op", new Message("op-suspend", Self, Kinds.Suspend, null, "operator", TargetCreation: 1), _nextSeq++));
            _report.Line($"pass {pass}: an operator's suspend arrives after page 1");
        }
        if (variant == "suspend" && _suspended && !_rows.Any(r => r.Name == "R-op") && pass >= _suspendedAt + 6)
        {
            _rows.Add(new Row("R-op", new Message("op-resume", Self, Kinds.Resume, null, "operator", TargetCreation: 1), _nextSeq++));
            _report.Line($"pass {pass}: the operator's resume arrives");
        }
        if (_tookOver || _pageRows.Count < 2) return;
        var gate2 = _pageRows[1][^1];
        var when = variant switch
        {
            // Page 2's rows were polled and marked at this epoch, and none has reached the model yet.
            "takeover" when gate2.Seen == _epoch && !gate2.Done && !_pageRows[1].Any(r => r.Done) => "after page 2's rows were marked seen, before any reached the model",
            // Page 2 is handed off (its gate row consumed) while the model holds rows of page 1.
            "hold-takeover" or "zero-cut" when gate2.Done && _held.Count > 0 && DrainDue => $"after page 2 was consumed and as the next page's control comes due, while the model holds [{string.Join(", ", _rows.Where(r => _held.Contains(r.Key)).Select(r => r.Name))}]",
            _ => null,
        };
        if (when is null) return;
        _tookOver = true;
        _epoch++; _mine.Clear(); _mine.Add(_epoch);
        _memory.Clear(); _held.Clear(); _suspects.Clear();
        _report.Line($"pass {pass}: crash takeover {when}: another process owns epoch {_epoch}; the credit resets to durable state (memory and holds are lost)");
    }

    /// <summary>
    /// The built poll with Horizon 0 (InMemoryStore.cs 219–221): not Done, Seen &lt; epoch, due, in seq order. A row in
    /// memory is skipped (Worker.cs 627–628); one another process marked is a suspect (577–578); every other polled row is
    /// marked seen at this epoch by the next flush (1397).
    /// </summary>
    private void Poll()
    {
        foreach (var row in _rows.Where(r => !r.Done && r.Seen < _epoch && (r.Message.Due ?? 0) <= _now).OrderBy(r => r.Seq).Take(500).ToList())
        {
            if (!_memory.Add(row.Key)) continue;
            if (!IsControl(row.Kind) && row.Seen > 0 && !_mine.Contains(row.Seen)) _suspects.Add(row.Key);
            else row.Seen = _epoch;
        }
    }

    private bool DrainDue => _rows.Any(r => r.Kind == DrainKind && !r.Done && (r.Message.Due ?? 0) <= _now);

    private static bool Released(Row row) => row.ReleasedBy is not null && !row.Done;

    /// <summary>What S may be given now: control kinds; while the drain is open, its released rows; never while suspended.</summary>
    private bool Eligible(Row row) => IsControl(row.Kind) || !_suspended && (_openGeneration is null || row.ReleasedBy == (1, _openGeneration));

    /// <summary>Worker.cs 733–763 with TrialGroup 1, cohort suspects first (FR-1's repaired NextTrial for one instance).</summary>
    private Row? NextTrial() => _suspended ? null
        : _rows.Where(r => _suspects.Contains(r.Key) && !r.Done && Eligible(r)).OrderBy(r => r.ReleasedBy is null ? 1 : 0).ThenBy(r => r.Seq).FirstOrDefault();

    /// <summary>A trial: the fenced trial commit marks the row seen at this epoch, then it is delivered alone; it counts as handed off once its turn commits.</summary>
    private async Task TrialAsync(Row row)
    {
        row.Seen = _epoch;
        _suspects.Remove(row.Key);
        _trials++;
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
            if (head.Kind == DrainKind) { Page(head); Sample((int)(_now - 2000)); }   // the gauge right after the page commit too
            else if (head.Kind is Kinds.Suspend or Kinds.Resume)
            {
                _suspended = head.Kind == Kinds.Suspend;
                if (_suspended) _suspendedAt = (int)(_now - 2000);
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
            if (holds.Contains(row.Key)) _held.Add(row.Key);
            else { row.Done = true; _held.Remove(row.Key); row.Fate = "consumed by a model turn"; }
        if (inbox.Count > 1 || holds.Count > 0) _report.Line($"model turn at {_now}: {inbox.Count} input(s) [{inbox[0].Name}..{inbox[^1].Name}]{(holds.Count > 0 ? $", held [{string.Join(", ", inbox.Where(r => holds.Contains(r.Key)).Select(r => r.Name))}]" : "")}");
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
    private (int Rows, long Bytes) Pending()
    {
        var pending = _rows.Where(r => Released(r) && !_held.Contains(r.Key)).ToList();
        return (pending.Count, pending.Sum(r => (long)(rules == Fr2Rules.CreditMetadataOnly ? r.MetaBytes : r.Bytes)));
    }

    private void Sample(int pass)
    {
        var pending = _rows.Where(r => Released(r) && !_held.Contains(r.Key)).ToList();
        var (rows, bytes) = (pending.Count, pending.Sum(r => (long)r.Bytes));
        var held = _rows.Count(r => Released(r) && _held.Contains(r.Key));
        if (rows > _peakPending || bytes > _peakPendingBytes || held > _peakHeld)
            _gauge.Add($"gauge at pass {pass}: pending non-held restored rows {rows} ({bytes} bytes), held {held}");
        _peakPending = Math.Max(_peakPending, rows);
        _peakPendingBytes = Math.Max(_peakPendingBytes, bytes);
        _peakHeld = Math.Max(_peakHeld, held);
        _peakMiB = Math.Max(_peakMiB, pending.Count(r => r.Bytes > 1_000_000 && r.Bytes < 2_000_000));
    }

    /// <summary>
    /// A kernel.drain turn. A page waits while S is suspended (M3-5). Reviewed: the previous gate row taken up (Done, held, or
    /// seen at this epoch), then the next P_page kept rows. Repaired: the gate row handed off (consumed by a committed model
    /// turn, or held), then at most P_page - u rows, stopping before a row that would take the stored bytes of the u rows and
    /// the page above B_credit; with u = 0 the first row always goes; a page cut to zero waits. Complete only when no kept row
    /// remains past the cursor.
    /// </summary>
    private void Page(Row control)
    {
        if (_openGeneration is not { } generation) return;
        if (_suspended && rules != Fr2Rules.PagesWhileSuspended) { Retry(control, "S is suspended"); return; }
        var gate = _rows.FirstOrDefault(r => r.Key == _gate);
        if (gate is not null && !GatePasses(gate, out var why)) { Retry(control, why); return; }
        var remaining = _rows.Where(r => r.KeptBy is not null && (_cursor is null || r.Seq > _cursor.Seq)).OrderBy(r => r.Seq).ToList();
        var (u, uBytes) = _suspended && rules == Fr2Rules.PagesWhileSuspended ? (0, 0L) : Pending();
        var page = new List<Row>();
        if (!Credit) page = remaining.Take(PageLimit).ToList();
        else
        {
            var rowBudget = rules == Fr2Rules.PPageWhateverU ? PageLimit : PageLimit - u;
            var bytes = uBytes;
            foreach (var row in remaining)
            {
                var size = rules == Fr2Rules.CreditMetadataOnly ? row.MetaBytes : row.Bytes;
                var first = page.Count == 0 && u == 0;
                if (!first && (page.Count + 1 > rowBudget || bytes + size > BCredit)) break;
                page.Add(row); bytes += size;
            }
        }
        if (page.Count > 0)
        {
            foreach (var row in page) { row.Done = false; row.KeptBy = null; row.Seen = 0; row.ReleasedBy = (1, generation); row.Fate = "released"; }
            _cursor = page[^1]; _gate = page[^1].Key;
            _pageRows.Add(page);
            if (page.Any(r => r.Bytes > 8_000_000)) _bigAlone = page.Count == 1 && u == 0;
            var next = new Row($"C{_pageRows.Count + 1}", Drain(), _nextSeq++);
            _rows.Add(next);
            _report.Line($"{control.Name} at {_now}: page {_pageRows.Count} restores {page.Count} rows [{page[0].Name}..{page[^1].Name}], {page.Sum(r => (long)r.Bytes)} bytes, with u = {u} ({uBytes} bytes) not handed off; sends {next.Name}");
            return;
        }
        if (remaining.Count > 0 && rules != Fr2Rules.ZeroCutCompletes)
        {
            Retry(control, $"the credit cuts the page to zero: u = {u} rows of {uBytes} bytes, and the next row has {remaining[0].Bytes}");
            return;
        }
        _openGeneration = null; _cursor = null; _completed = true;
        _report.Line($"{control.Name} at {_now}: {(remaining.Count > 0 ? $"a page cut to zero completes the drain, with {remaining.Count} rows still kept (zero-cut-completes)" : "no kept row past the cursor and the gate passes: Complete")}");
    }

    private bool GatePasses(Row gate, out string why)
    {
        if (SeenTest)
        {
            why = $"gate row {gate.Name} not taken up (seen {gate.Seen}, epoch {_epoch})";
            return gate.Done || _held.Contains(gate.Key) || gate.Seen == _epoch && !_suspects.Contains(gate.Key);
        }
        why = $"gate row {gate.Name} not handed off{(_held.Contains(gate.Key) ? " (held, counted as not handed off)" : "")}";
        return gate.Done || _held.Contains(gate.Key) && rules != Fr2Rules.HeldGateWaits || _suspended && rules == Fr2Rules.PagesWhileSuspended;
    }

    private void Retry(Row control, string why)
    {
        _rows.Add(new Row($"{control.Name}'", Drain() with { Due = _now + RetryDelay }, _nextSeq++));
        if (_lastWait != why) _report.Line($"{control.Name} at {_now}: the page waits ({why}); retry due {_now + RetryDelay}");
        _lastWait = why;
    }
}
