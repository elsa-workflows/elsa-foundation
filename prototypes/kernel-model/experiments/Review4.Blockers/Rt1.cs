namespace Kernel.Review4;

/// <summary>
/// The rule sets of RT-1 (R4-B2). Three mutations of the repaired set:
/// <c>CascadeAny</c> cascades for any terminate refused at a marker (DT-8);
/// <c>SuccessorAdmitted</c> drops the ObligationOutstanding start refusal, so a successor replaces a marker that still
/// retains an obligation (DT-9(b));
/// <c>SuccessorInherits</c> gives a successor its predecessor's registry (DT-9(c));
/// <c>RootRetains</c> lets a root's marker keep its registry and jobs (RM-1).
/// </summary>
enum RtRules { Reviewed, Repaired, Negative, CascadeAny, SuccessorAdmitted, SuccessorInherits, RootRetains }

/// <summary>
/// The proposed extension of the retirement marker: the recorded parent, the child registry and the job keys as they
/// were at retirement. It lives in the harness, keyed by the retired creation, beside the real
/// <see cref="CreationProtocol.Retire"/> output, which Creation.cs keeps unchanged.
/// </summary>
sealed record Obligation(string? Parent, long ParentCreation, List<CreationAddress> Children, List<string> Jobs)
{
    public bool Outstanding => Children.Count + Jobs.Count > 0;
    public override string ToString() =>
        $"parent {Parent}/{ParentCreation}, children [{string.Join(", ", Children.Select(c => $"{c.Id}/{c.Creation}"))}], jobs [{string.Join(", ", Jobs)}]";
}

/// <summary>
/// RT-1: P → A → G → G2, live G and G2, a job J on A, and A in an unprotected parked-delete drain; A, G and G2 sit at
/// generated child ids. The rows are real <see cref="InstanceRow"/> records; the refusal, the start check, the marker,
/// the registry, the job keys, the parent's notice and the supplied-creator classification are the real prototype
/// functions (<see cref="Real"/>). The drain's Complete, the open-drain refusal of #4756, the marker's obligation and
/// cascade, and the ObligationOutstanding start refusal are the proposed transitions. The built terminate of a live
/// row is mirrored from WorkflowInstance.cs 179–203, because its TurnInput needs a real Worker.
/// </summary>
sealed class Rt1(string variant, RtRules rules)
{
    private const string P = "0/p", A = "g1/child/0.1.1", G = "g2/child/1.1.1", G2 = "g3/child/2.1.1", X = "4/x", Operator = "operator";
    private const string K = "5/t1:invoice", H = "g6/child/5.1.1", H2 = "g6/child/5.2.1";   // DT-9(c): a keyed root and its children
    private const long PCreation = 2;
    private readonly Dictionary<string, InstanceRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InstanceRow> _retiredFrom = new(StringComparer.Ordinal); // the row each marker replaced
    private readonly Dictionary<(string Id, long Creation), Obligation> _obligations = [];
    private readonly List<Message> _inFlight = [];
    private readonly HashSet<InboxKey> _liveJobs = [], _cancelledJobs = [];
    private readonly List<string> _refusals = [];
    private readonly List<string> _markerCascades = [];
    private readonly List<string> _sentTo = [];
    private bool _aDrainOpen = true;
    private int _aKept = 3;
    private int _noticesToP;
    private string? _recordedAtComplete;
    private string? _successorStart;
    private Report _report = null!;

    public static readonly string[] Variants = ["a", "a-g-deleted", "b", "nonparent", "successor", "successor-unreachable", "successor-gap"];

    private static readonly Dictionary<string, string> Invariants = new()
    {
        ["nonparent"] = "only the recorded parent discharges the obligation: other terminates are refused with no cascade, then P's delayed terminate ends the tree",
        ["successor"] = "a keyed root's marker keeps nothing, and its successor inherits no registry: the successor's terminate reaches only its own children",
        ["successor-unreachable"] = "a start addressed to the retired generated child id is consumed with no change, so no creation 2 appears and P's terminate still discharges the obligation",
        ["successor-gap"] = "no successor replaces a marker that still holds an obligation (ObligationOutstanding), so P's delayed terminate still discharges it",
    };

    public Report Run()
    {
        _report = new Report($"RT-1/{variant}", Program.Name(rules), Invariants.GetValueOrDefault(variant,
            "invariant 17: after P's tree terminate no instance of P's tree runs, none still lists children or jobs, J is cancelled, and P hears exactly one notice from A"));
        if (variant == "successor") return KeyedRoot();

        _rows[P] = new InstanceRow(P, "native", "p", InstanceStatus.Running, "{}", 4, null,
            Children: Real.WriteRegistry([new(A, 1)]), Creation: PCreation, StartIntent: "v1:p");
        _rows[A] = new InstanceRow(A, "native", "a", InstanceStatus.Faulted, "{}", 7, P, Fault: "parked",
            Jobs: Real.WriteJobs(["j"]), Children: Real.WriteRegistry([new(G, 1)]), Creation: 1, StartIntent: "v1:a", ParentCreation: PCreation);
        _rows[G] = new InstanceRow(G, "native", "g", variant == "a-g-deleted" ? InstanceStatus.Finished : InstanceStatus.Running, "{}", 3, A,
            Children: Real.WriteRegistry([new(G2, 1)]), Creation: 1, StartIntent: "v1:g", ParentCreation: 1);
        _rows[G2] = new InstanceRow(G2, "native", "g2", InstanceStatus.Running, "{}", 2, G, Creation: 1, StartIntent: "v1:g2", ParentCreation: 1);
        _liveJobs.Add(Real.Ticket(A, "j", 1));
        _report.Line($"setup: P {Show(P)}; A {Show(A)}; G {Show(G)}; G2 {Show(G2)}; job J = ticket {Real.Ticket(A, "j", 1).Id} live");
        _report.Line($"A: unprotected parked-delete drain open, {_aKept} kept rows; Park left G, G2 and J alone");
        // DT-7's variant: G ended on its own while G2 lives (a terminal parent still owns its registry), then G is deleted.
        if (variant == "a-g-deleted") Retire(G, "G's built delete (an ended row, no drain)");

        var t = TerminateP();
        switch (variant)
        {
            case "a":
            case "a-g-deleted":
                _report.Line("T committed while A exists and its delete drain is open; delivery delayed past A's Complete");
                PageAndComplete();
                Deliver(t);
                break;
            case "b":
                _report.Line("T delivered before A's Complete, while the delete drain is open");
                Deliver(t);
                PageAndComplete();
                break;
            case "nonparent":
                _report.Line("T committed while A exists; delayed past A's Complete and past four terminates without authority");
                PageAndComplete();
                Settle();
                var before = Ob(A, 1);
                var others = new (string What, Message M)[]
                {
                    ("an application send from non-parent X", new Message($"{CreationProtocol.Origin(X, 1)}#5.0000", new InstanceRef(A), Kinds.Terminate, Sender: X, TargetCreation: 1, SenderCreation: 1).AsIssued()),
                    ("a terminate from P's earlier creation 1", new Message($"{CreationProtocol.Origin(P, 1)}#9.0000", new InstanceRef(A), Kinds.Terminate, Sender: P, TargetCreation: 1, SenderCreation: 1).AsIssued()),
                    ("an outside add naming P as sender", new Message("outside-terminate", new InstanceRef(A), Kinds.Terminate, Sender: P, TargetCreation: 1, SenderCreation: PCreation)),
                    ("an operator's terminate carrying A's selection", new Message("op-a", new InstanceRef(A), Kinds.Terminate, Sender: Operator, TargetCreation: 1)),
                };
                foreach (var (what, m) in others)
                {
                    _report.Line($"{what}: {m.Id}");
                    Deliver(m);
                    Settle();
                    _report.Expect($"obligation after {what}", Ob(A, 1), before);
                }
                Deliver(t);
                break;
            case "successor-unreachable":
                _report.Line("T committed while A exists; delayed past A's Complete and past a start addressed to A's retired id");
                PageAndComplete();
                Settle();
                SuppliedStarts();
                Deliver(t);
                break;
            case "successor-gap":
                _report.Line("T committed while A's creation 1 exists; delayed past A's Complete and a successor start for creation 2");
                PageAndComplete();
                Settle();
                var marker = _rows[A];
                var obligation = Ob(A, 1);
                if (StartSuccessor(A, new StartSpec("native", "a", Creation: new CreationStart(2, 1, "successor-a"))) is not null)
                {
                    _report.Expect("marker unchanged after the refused start", ReferenceEquals(_rows[A], marker), true);
                    _report.Expect("obligation unchanged after the refused start", Ob(A, 1), obligation);
                }
                Deliver(t);
                break;
        }
        Settle();
        LateJobResult();

        _report.Line($"end: P {Show(P)}; A {Show(A)}; G {Show(G)}; G2 {Show(G2)}");
        _report.Line($"jobs: live [{string.Join(", ", _liveJobs.Select(j => j.Id))}], cancelled [{string.Join(", ", _cancelledJobs.Select(j => j.Id))}]");
        _report.Line($"obligations: A creation 1: {Ob(A, 1)}{(variant == "a-g-deleted" ? $"; G creation 1: {Ob(G, 1)}" : "")}");
        _report.Line($"refusals: {(_refusals.Count == 0 ? "none" : string.Join("; ", _refusals))}");

        _report.Expect("P", _rows[P].Status, InstanceStatus.Cancelled);
        if (variant == "a-g-deleted") _report.Expect("G is a retired marker", _rows[G].Retired, true);
        else _report.Expect("G", _rows[G].Status, InstanceStatus.Cancelled);
        _report.Expect("G2", _rows[G2].Status, InstanceStatus.Cancelled);
        _report.Expect("J cancelled", _cancelledJobs.Contains(Real.Ticket(A, "j", 1)) && _liveJobs.Count == 0, true);
        _report.Expect("live tree rows still listing children or jobs", _rows.Values.Count(r => !r.Retired && (r.Children is not null || r.Jobs is not null)), 0);
        _report.Expect("obligations outstanding on markers", _obligations.Values.Sum(o => o.Children.Count + o.Jobs.Count), 0);
        _report.Expect("notices P received from A", _noticesToP, 1);
        _report.Expect("creation 2 at A's id", _rows[A].Creation == 2, false);
        _report.Expect("J's late result at A's id", Refusal(Real.Ticket(A, "j", 1).Id), "RetiredCreation");
        if (variant == "b") _report.Expect("T's refusal during the open drain", Refusal(t.Id), "resolution open (with cascade)");
        else _report.Expect("T's refusal (CreationProtocol.TargetError)", Refusal(t.Id), "RetiredCreation");
        if (variant is "a" or "a-g-deleted" && rules != RtRules.Reviewed)
            _report.Expect("RM-1: the obligation recorded at A's Complete", _recordedAtComplete, $"parent {P}/{PCreation}, children [{(rules == RtRules.Negative ? "" : G + "/1")}], jobs [j]");
        if (variant == "a-g-deleted") _report.Expect("G2's notice refused at G's marker", _noticeRefusals.GetValueOrDefault((G2, G)), "RetiredCreation");
        else _report.Expect("G's notice refused at A's marker", _noticeRefusals.GetValueOrDefault((G, A)), "RetiredCreation");
        if (variant == "nonparent") _report.Expect("cascades from A's marker, all from T", _markerCascades.Count == 1 && _markerCascades[0] == t.Id, true);
        if (variant == "successor-gap" && rules is not (RtRules.Reviewed or RtRules.SuccessorAdmitted))
            _report.Expect("successor start", _successorStart, "ObligationOutstanding");
        return _report;
    }

    private string? Refusal(string id) =>
        _refusals.FirstOrDefault(r => r.StartsWith(id + ": ", StringComparison.Ordinal))?[(id.Length + 2)..];

    private string Ob(string id, long creation) => _obligations.TryGetValue((id, creation), out var o) ? o.ToString() : "none";

    private string Show(string id)
    {
        var r = _rows[id];
        if (r.Retired) return $"[retired marker, creation {r.Creation}, revision {r.Revision}, parent {r.Parent ?? "null"}, children {r.Children ?? "null"}, jobs {r.Jobs ?? "null"}]";
        return $"[{r.Status}, creation {r.Creation}, revision {r.Revision}, parent {r.Parent ?? "null"}/{r.ParentCreation}, children {r.Children ?? "null"}, jobs {r.Jobs ?? "null"}]";
    }

    /// <summary>An operator terminates P. P's built terminate sends the parent-bound terminate T to A.</summary>
    private Message TerminateP()
    {
        Deliver(new Message("op-p", new InstanceRef(P), Kinds.Terminate, Sender: Operator, TargetCreation: PCreation));
        var t = _inFlight.Single(m => m.Target.Id == A && m.Kind == Kinds.Terminate);
        _inFlight.Remove(t);
        _report.Line($"T = {t.Id} (Sender {t.Sender}/{t.SenderCreation}, TargetCreation {t.TargetCreation}, issued)");
        return t;
    }

    /// <summary>A's delete drain: the last page clears the kept rows, then Complete sends the one notice and retires A.</summary>
    private void PageAndComplete()
    {
        _report.Line($"A page: clears {_aKept} kept rows (delete clears, restores none); cursor passes the last kept row");
        _aKept = 0;
        _inFlight.Add(Real.Finished(_rows[A]));
        _report.Line($"A Complete: notice {_inFlight[^1].Id} (ChildFinished {_rows[A].Status}) to P");
        Retire(A, "A Complete finishes the built delete");
        _recordedAtComplete = Ob(A, 1);
        _aDrainOpen = false;
    }

    /// <summary>
    /// The real <see cref="CreationProtocol.Retire"/> marker, plus the proposed obligation. A root records no parent,
    /// so its marker keeps nothing: no parent-bound terminate can ever reach it.
    /// </summary>
    private void Retire(string id, string why)
    {
        var row = _rows[id];
        var marker = Real.Retire(row);
        if (rules != RtRules.Reviewed && (row.Parent is not null || rules == RtRules.RootRetains))
        {
            var children = rules == RtRules.Negative ? [] : Real.ReadRegistry(row.Children);
            _obligations[(id, row.Creation)] = new Obligation(row.Parent, row.ParentCreation, children, Real.ReadJobs(row.Jobs));
        }
        _retiredFrom[id] = row;
        _rows[id] = marker;
        _report.Line($"{why}: retirement marker {Show(id)}; obligation {Ob(id, row.Creation)}");
    }

    /// <summary>
    /// A new creation at an id. The real <see cref="CreationProtocol.StartError"/> runs first; the repaired rules then
    /// refuse it with ObligationOutstanding while the id's marker still holds an obligation (proposed, defence in depth).
    /// </summary>
    private string? StartSuccessor(string id, StartSpec spec)
    {
        var marker = _rows[id];
        var error = Real.StartError(marker, spec, Kinds.Start, null, InboxKey.Issued("start-" + id));
        if (error is null && rules is not (RtRules.Reviewed or RtRules.SuccessorAdmitted) &&
            _obligations.TryGetValue((id, marker.Creation), out var ob) && ob.Outstanding)
            error = "ObligationOutstanding";
        _successorStart = error ?? "accepted";
        _report.Line($"start of creation {spec.Creation!.Generation} at {id}: CreationProtocol.StartError -> {Real.LastStartError ?? "accepted"}; result {_successorStart}");
        if (error is not null) return error;
        var inherited = rules == RtRules.SuccessorInherits ? _retiredFrom[id].Children : null;
        _rows[id] = new InstanceRow(id, "native", spec.Definition, InstanceStatus.Running, "{}", marker.Revision + 1, spec.Parent,
            Children: inherited, Creation: spec.Creation.Generation, StartIntent: Real.Fingerprint(spec), ParentCreation: spec.ParentCreation);
        _report.Line($"  successor row {Show(id)}");
        return null;
    }

    /// <summary>
    /// DT-9(a): a start for creation 2 addressed to A's retired generated child id, as an outside add and as another
    /// actor's application send. The classification is real (Message.SuppliedCreator, and Delivery.DiscardedAt over a
    /// Slot holding the marker); the consumption the Worker then does (Worker.cs 1030–1037) is mirrored.
    /// </summary>
    private void SuppliedStarts()
    {
        var spec = new StartSpec("native", "a", Creation: new CreationStart(2, 1, "successor-a"));
        var marker = _rows[A];
        var obligation = Ob(A, 1);
        var starts = new (string What, Message M)[]
        {
            ("outside add", new Message("outside-start", new InstanceRef(A), Kinds.Start, Start: spec, TargetCreation: 2)),
            ("application send from X", new Message($"{CreationProtocol.Origin(X, 1)}#6.0000", new InstanceRef(A), Kinds.Start, Sender: X, Start: spec, TargetCreation: 2, SenderCreation: 1).AsIssued()),
        };
        foreach (var (what, m) in starts)
        {
            var supplied = Real.SuppliedCreator(m);
            var discarded = Real.DiscardedAt(m, marker);
            _report.Line($"{what} {m.Id} (scope {m.InboxScope}): Message.SuppliedCreator {supplied}, Delivery.DiscardedAt(slot holding A's marker) {discarded}" +
                (discarded ? "; the Worker consumes it with no turn and no row change (mirrored, Worker.cs 1030-1037)" : ""));
            _report.Expect($"{what} discarded by the real classification", supplied && discarded, true);
        }
        // Control: the creator scope that only StartChild, with a freshly issued id, and key admission mint.
        var creator = starts[0].M.WithInboxScope(long.MaxValue);
        var creatorSupplied = Real.SuppliedCreator(creator);
        _report.Line($"control: the same start at the creator scope {creator.InboxScope}: Message.SuppliedCreator {creatorSupplied} (StartChild issues fresh child ids, never a retired one)");
        _report.Expect("the creator-scope control is no supplied creator", creatorSupplied, false);
        _report.Expect("marker unchanged after the supplied starts", ReferenceEquals(_rows[A], marker), true);
        _report.Expect("obligation unchanged after the supplied starts", Ob(A, 1), obligation);
    }

    /// <summary>DT-7: J's late result reaches A's id after the cascade and is refused there.</summary>
    private void LateJobResult()
    {
        var ticket = Real.Ticket(A, "j", 1);
        Deliver(new Message(ticket.Id, new InstanceRef(A), Kinds.Job, Json.Write(new { Result = "late" }), A, TargetCreation: 1, SenderCreation: 1).WithInboxScope(ticket.Scope));
    }

    /// <summary>DT-9(c): a keyed root R under caller key K with a live child H is deleted; B starts under K with creation 2.</summary>
    private Report KeyedRoot()
    {
        _rows[K] = new InstanceRow(K, "native", "k", InstanceStatus.Finished, "{}", 6, null, Children: Real.WriteRegistry([new(H, 1)]), Creation: 1, StartIntent: "v1:k");
        _rows[H] = new InstanceRow(H, "native", "h", InstanceStatus.Running, "{}", 2, K, Creation: 1, StartIntent: "v1:h", ParentCreation: 1);
        _report.Line($"setup: R = keyed root {K} {Show(K)}, with no parent (a keyed start carries none, KeyAdmission.cs 31-32); its live child H {Show(H)}");
        Retire(K, "R's built delete (an ended root, no drain)");
        _report.Expect("RM-1: R's marker obligation (a root records no parent)", Ob(K, 1), "none");
        var spec = new StartSpec("native", "k", Creation: new CreationStart(2, 1, "successor-k"));
        var startRefusal = StartSuccessor(K, spec);
        _report.Expect("B's start for creation 2", startRefusal ?? "accepted", "accepted");
        if (startRefusal is not null) { _report.Line("B is not created, so the run stops here"); return _report; }
        _report.Expect("B inherits no registry", _rows[K].Children is null, true);
        _rows[H2] = new InstanceRow(H2, "native", "h2", InstanceStatus.Running, "{}", 2, K, Creation: 1, StartIntent: "v1:h2", ParentCreation: 2);
        var b = _rows[K];
        var registry = Real.ReadRegistry(b.Children);
        registry.Add(new(H2, 1));
        _rows[K] = b with { Revision = b.Revision + 1, Children = Real.WriteRegistry(registry) };
        _report.Line($"B starts child H2 {Show(H2)}; B {Show(K)}");
        Deliver(new Message("op-b", new InstanceRef(K), Kinds.Terminate, Sender: Operator, TargetCreation: 2));
        Settle();
        // H ends on its own later; its notice names R's creation 1.
        var h = _rows[H] with { Status = InstanceStatus.Finished, Revision = _rows[H].Revision + 1 };
        _rows[H] = h;
        var notice = Real.Finished(h);
        _report.Line($"H finishes on its own: notice {notice.Id} to {K}, TargetCreation {notice.TargetCreation}");
        Deliver(notice);
        Settle();
        _report.Line($"end: B {Show(K)}; H {Show(H)}; H2 {Show(H2)}");
        _report.Line($"refusals: {(_refusals.Count == 0 ? "none" : string.Join("; ", _refusals))}");
        _report.Expect("B", _rows[K].Status, InstanceStatus.Cancelled);
        _report.Expect("H2", _rows[H2].Status, InstanceStatus.Cancelled);
        _report.Expect("terminates sent to H", _sentTo.Count(x => x == H), 0);
        _report.Expect("H ran to its own end (delete detaches, as built)", _rows[H].Status, InstanceStatus.Finished);
        _report.Expect("H's notice to R's creation, at B", Refusal(notice.Id), "WrongCreation");
        return _report;
    }

    private void Settle()
    {
        for (var guard = 0; _inFlight.Count > 0; guard++)
        {
            if (guard > 100) throw new InvalidOperationException("the cascade did not settle");
            var next = _inFlight[0];
            _inFlight.RemoveAt(0);
            Deliver(next);
        }
    }

    private static bool ParentBound(Message m, string? parent, long parentCreation, long creation) =>
        m.InboxKey == InboxKey.Issued(m.Id) && m.Sender == parent && m.SenderCreation == parentCreation && m.TargetCreation == creation && parent is not null;

    private void Refuse(Message m, string reason) { _refusals.Add($"{m.Id}: {reason}"); _report.Line($"deliver {m.Id} ({m.Kind}) to {m.Target.Id}: refused, {reason}"); }

    private readonly Dictionary<(string Sender, string Target), string> _noticeRefusals = [];

    private void Deliver(Message m)
    {
        if (m.Kind == WorkflowKinds.ChildFinished && m.Target.Id == P && m.Sender == A) _noticesToP++;
        if (m.Kind == WorkflowKinds.ChildFinished && _rows.TryGetValue(m.Target.Id, out var target) && Real.TargetError(target, m) is { } refused)
            _noticeRefusals[(m.Sender!, m.Target.Id)] = refused;
        if (!_rows.TryGetValue(m.Target.Id, out var row)) { _report.Line($"deliver {m.Id}: no target, dropped"); return; }
        var error = Real.TargetError(row, m);
        if (error is null && m.Target.Id == A && _aDrainOpen && m.Kind == Kinds.Terminate) { OpenDrainRefusal(m, row); return; }
        if (error is not null)
        {
            Refuse(m, error);
            if (error == "RetiredCreation" && m.Kind == Kinds.Terminate) MarkerCascade(m, row);
            return;
        }
        switch (m.Kind)
        {
            case Kinds.Terminate when m.Sender == Operator || ParentBound(m, row.Parent, row.ParentCreation, row.Creation):
                BuiltTerminate(row, m);
                break;
            case Kinds.Terminate:
                Refuse(m, "parent binding");
                break;
            case WorkflowKinds.ChildFinished:
                ChildNotice(row, m);
                break;
            default:
                throw new InvalidOperationException("unexpected kind " + m.Kind);
        }
    }

    /// <summary>#4756 (main): a terminate while a delete drain is open is refused as "resolution open"; with authority it cascades.</summary>
    private void OpenDrainRefusal(Message m, InstanceRow row)
    {
        if (!ParentBound(m, row.Parent, row.ParentCreation, row.Creation)) { Refuse(m, "resolution open (no cascade)"); return; }
        Refuse(m, "resolution open (with cascade)");
        var next = row with { Revision = row.Revision + 1, Children = null, Jobs = null };
        Cascade(row.Id, row.Creation, next.Revision, Real.ReadRegistry(row.Children), Real.ReadJobs(row.Jobs), "the refusing commit");
        _rows[row.Id] = next;
    }

    /// <summary>
    /// The proposed marker rule: a parent-bound terminate refused with RetiredCreation cascades over the retained
    /// obligation, and writes the marker at its next revision with the obligation cleared (RM-1).
    /// </summary>
    private void MarkerCascade(Message m, InstanceRow marker)
    {
        if (rules == RtRules.Reviewed || !_obligations.TryGetValue((marker.Id, marker.Creation), out var ob) || !ob.Outstanding) return;
        if (rules != RtRules.CascadeAny && !ParentBound(m, ob.Parent, ob.ParentCreation, marker.Creation))
        { _report.Line($"  {m.Id}: not a parent-bound terminate from the recorded parent {ob.Parent}/{ob.ParentCreation}: plain refusal, no cascade"); return; }
        _markerCascades.Add(m.Id);
        var next = marker with { Revision = marker.Revision + 1 };
        Cascade(marker.Id, marker.Creation, next.Revision, ob.Children, ob.Jobs, "the refusing commit at the marker");
        _obligations[(marker.Id, marker.Creation)] = ob with { Children = [], Jobs = [] };
        _rows[marker.Id] = next;
        _report.Line($"  marker written at revision {next.Revision}, every other field unchanged; obligation {Ob(marker.Id, marker.Creation)}");
        _report.Line($"  built CreationProtocol.Validate on that marker write: {Real.Validate(new TurnCommit(marker.Id, next, marker.Revision, [], [], [], [], [], null), marker)} (the release widens it for the obligation write)");
    }

    private void Cascade(string id, long creation, long revision, List<CreationAddress> children, List<string> jobs, string where)
    {
        var origin = Real.Origin(id, creation);
        var sent = children.Select((child, i) => new Message($"{origin}#{revision}.{i:D4}", new InstanceRef(child.Id), Kinds.Terminate, Sender: id,
            TargetCreation: child.Creation > 0 ? child.Creation : null, SenderCreation: creation).AsIssued()).ToList();
        var tickets = jobs.Select(key => Real.Ticket(id, key, creation)).ToList();
        foreach (var ticket in tickets) { _liveJobs.Remove(ticket); _cancelledJobs.Add(ticket); }
        _inFlight.AddRange(sent);
        _sentTo.AddRange(sent.Select(s => s.Target.Id));
        _report.Line($"  {where} sends terminate to [{string.Join(", ", sent.Select(s => $"{s.Target.Id}/{s.TargetCreation} as {s.Id}"))}], cancels [{string.Join(", ", tickets.Select(t => t.Id))}]");
    }

    /// <summary>The built terminate of a live row, mirrored from WorkflowInstance.cs 179–203 (Terminate).</summary>
    private void BuiltTerminate(InstanceRow row, Message m)
    {
        var running = row.Status == InstanceStatus.Running;
        var next = row with { Status = running ? InstanceStatus.Cancelled : row.Status, Revision = row.Revision + 1, Jobs = null, Children = null, Suspended = false };
        _report.Line($"deliver {m.Id} (terminate from {m.Sender}) to {row.Id}: accepted; built terminate, {row.Status} -> {next.Status}");
        Cascade(row.Id, row.Creation, next.Revision, Real.ReadRegistry(row.Children), Real.ReadJobs(row.Jobs), "  its commit");
        if (running && row.Parent is not null) _inFlight.Add(Real.Finished(next));
        _rows[row.Id] = next;
    }

    /// <summary>A child's notice, mirrored from WorkflowInstance.cs 36–61: an ended parent closes the entry, a running one hands it to its model.</summary>
    private void ChildNotice(InstanceRow row, Message m)
    {
        var finished = m.Read<ChildFinished>();
        var children = Real.ReadRegistry(row.Children);
        if (!children.Remove(new(finished.Child, finished.Creation))) { Refuse(m, "UnknownChildCreation"); return; }
        _rows[row.Id] = row with { Revision = row.Revision + 1, Children = Real.WriteRegistry(children) };
        _report.Line($"deliver {m.Id} (ChildFinished {finished.Child} {finished.Status}) to {row.Id}: registry entry closed{(row.Status == InstanceStatus.Running ? ", handed to its model (not modelled)" : "")}");
    }
}
