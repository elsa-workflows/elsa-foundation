namespace Kernel.Review4;

/// <summary>The rule sets of RT-1 (R4-B2). <c>CascadeAny</c> is a mutation of the repaired set: any terminate at a marker cascades.</summary>
enum RtRules { Reviewed, Repaired, Negative, CascadeAny }

/// <summary>
/// The proposed extension of the retirement marker: the recorded parent, the child registry and the job keys as they
/// were at retirement. It lives in the harness, keyed by the retired creation, beside the real
/// <see cref="CreationProtocol.Retire"/> output, which Creation.cs keeps unchanged.
/// </summary>
sealed record Obligation(string? Parent, long ParentCreation, List<CreationAddress> Children, List<string> Jobs)
{
    public override string ToString() =>
        $"parent {Parent}/{ParentCreation}, children [{string.Join(", ", Children.Select(c => $"{c.Id}/{c.Creation}"))}], jobs [{string.Join(", ", Jobs)}]";
}

/// <summary>
/// RT-1: P → A → G → G2, live G and G2, a job J on A, and A in an unprotected parked-delete drain. The rows are real
/// <see cref="InstanceRow"/> records; the refusal, the marker, the registry, the job keys and the parent's notice are
/// the real prototype functions (<see cref="Real"/>). The drain's Complete, the open-drain refusal of #4756 and the
/// marker's cascade are the proposed transitions. The built terminate of a live row is mirrored from
/// WorkflowInstance.cs 179–203, because its TurnInput needs a real Worker.
/// </summary>
sealed class Rt1(string variant, RtRules rules)
{
    private const string P = "0/p", A = "1/a", G = "2/g", G2 = "3/g2", X = "4/x", Operator = "operator";
    private readonly Dictionary<string, InstanceRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, long Creation), Obligation> _obligations = [];
    private readonly List<Message> _inFlight = [];
    private readonly HashSet<InboxKey> _liveJobs = [], _cancelledJobs = [];
    private readonly List<string> _refusals = [];
    private readonly List<string> _markerCascades = [];
    private bool _aDrainOpen = true;
    private int _aKept = 3;
    private int _noticesToP;
    private Report _report = null!;

    public static readonly string[] Variants = ["a", "b", "nonparent", "successor", "successor-gap"];

    public Report Run()
    {
        _report = new Report($"RT-1/{variant}", Program.Name(rules), variant switch
        {
            "nonparent" => "a terminate without authority over the retired creation is refused with no cascade; the obligation stays for the parent",
            "successor" => "a terminate of a successor creation never reaches the old creation's obligation",
            _ => "invariant 17: after P's tree terminate no instance of P's tree runs, none still lists children or jobs, J is cancelled, and P hears exactly one notice from A",
        });

        _rows[P] = new InstanceRow(P, "native", "p", InstanceStatus.Running, "{}", 4, null,
            Children: Real.WriteRegistry([new(A, 1)]), Creation: 1, StartIntent: "v1:p");
        _rows[A] = new InstanceRow(A, "native", "a", InstanceStatus.Faulted, "{}", 7, P, Fault: "parked",
            Jobs: Real.WriteJobs(["j"]), Children: Real.WriteRegistry([new(G, 1)]), Creation: 1, StartIntent: "v1:a", ParentCreation: 1);
        _rows[G] = new InstanceRow(G, "native", "g", InstanceStatus.Running, "{}", 3, A,
            Children: Real.WriteRegistry([new(G2, 1)]), Creation: 1, StartIntent: "v1:g", ParentCreation: 1);
        _rows[G2] = new InstanceRow(G2, "native", "g2", InstanceStatus.Running, "{}", 2, G, Creation: 1, StartIntent: "v1:g2", ParentCreation: 1);
        _liveJobs.Add(Real.Ticket(A, "j", 1));
        _report.Line($"setup: P {Show(P)}; A {Show(A)}; G {Show(G)}; G2 {Show(G2)}; job J = ticket {Real.Ticket(A, "j", 1).Id} live");
        _report.Line($"A: unprotected parked-delete drain open, {_aKept} kept rows; Park left G, G2 and J alone");

        Message? t = null;
        switch (variant)
        {
            case "a":
                t = TerminateP();
                _report.Line($"T committed while A exists and its delete drain is open; delivery delayed past A's Complete");
                PageAndComplete();
                Deliver(t);
                break;
            case "b":
                t = TerminateP();
                _report.Line("T delivered before A's Complete, while the delete drain is open");
                Deliver(t);
                PageAndComplete();
                break;
            case "nonparent":
                PageAndComplete();
                var before = Ob();
                Settle();
                var x = new Message($"{CreationProtocol.Origin(X, 1)}#5.0000", new InstanceRef(A), Kinds.Terminate, Sender: X, TargetCreation: 1, SenderCreation: 1).AsIssued();
                _report.Line($"X (not A's parent) sends terminate {x.Id} to A's creation 1");
                Deliver(x);
                Settle();
                _report.Expect("obligation after the non-parent's terminate", Ob(), before);
                break;
            case "successor":
            case "successor-gap":
                if (variant == "successor-gap") { t = TerminateP(); _report.Line("T committed while A's creation 1 exists; delivery delayed past A's Complete and the successor's creation"); }
                PageAndComplete();
                Settle();
                var marker = _rows[A];
                var spec = new StartSpec("native", "a", Creation: new CreationStart(2, 1, "successor-a"));
                var error = Real.StartError(marker, spec, Kinds.Start, null, InboxKey.Issued("start-a2"));
                _report.Expect("CreationProtocol.StartError for creation 2 over the marker", error ?? "accepted", "accepted");
                _rows[A] = new InstanceRow(A, "native", "a", InstanceStatus.Running, "{}", marker.Revision + 1, null, Creation: 2, StartIntent: Real.Fingerprint(spec));
                _report.Line($"successor: A's id now holds creation 2 {Show(A)}; the creation-1 obligation stays keyed by (1/a, 1): {Ob()}");
                if (variant == "successor")
                {
                    var before2 = Ob();
                    Deliver(new Message("op-a2", new InstanceRef(A), Kinds.Terminate, Sender: Operator, TargetCreation: 2));
                    Settle();
                    _report.Expect("obligation of creation 1 after the successor's terminate", Ob(), before2);
                    _report.Expect("terminates sent to G", _sentTo.Count(m => m == G), 0);
                }
                else Deliver(t!);
                break;
        }
        Settle();

        _report.Line($"end: P {Show(P)}; A {Show(A)}; G {Show(G)}; G2 {Show(G2)}");
        _report.Line($"jobs: live [{string.Join(", ", _liveJobs.Select(j => j.Id))}], cancelled [{string.Join(", ", _cancelledJobs.Select(j => j.Id))}]");
        _report.Line($"obligation on the creation-1 marker: {Ob()}");
        _report.Line($"refusals: {(_refusals.Count == 0 ? "none" : string.Join("; ", _refusals))}");

        if (variant is "a" or "b" or "successor-gap")
        {
            _report.Expect("P", _rows[P].Status, InstanceStatus.Cancelled);
            _report.Expect("G", _rows[G].Status, InstanceStatus.Cancelled);
            _report.Expect("G2", _rows[G2].Status, InstanceStatus.Cancelled);
            _report.Expect("J cancelled", _cancelledJobs.Contains(Real.Ticket(A, "j", 1)) && _liveJobs.Count == 0, true);
            _report.Expect("live tree rows still listing children or jobs", _rows.Values.Count(r => !r.Retired && (r.Children is not null || r.Jobs is not null)), 0);
            _report.Expect("obligations outstanding on the marker", _obligations.Values.Sum(o => o.Children.Count + o.Jobs.Count), 0);
            _report.Expect("notices P received from A", _noticesToP, 1);
            if (variant == "a") _report.Expect("T's refusal at the marker (CreationProtocol.TargetError)", _refusals.FirstOrDefault(r => r.StartsWith(t!.Id, StringComparison.Ordinal)), $"{t!.Id}: RetiredCreation");
            if (variant == "b") _report.Expect("T's refusal during the open drain", _refusals.FirstOrDefault(r => r.StartsWith(t!.Id, StringComparison.Ordinal)), $"{t!.Id}: resolution open (with cascade)");
        }
        if (variant == "nonparent")
        {
            _report.Expect("cascades from the marker", _markerCascades.Count, 0);
            _report.Expect("G", _rows[G].Status, InstanceStatus.Running);
            _report.Expect("G2", _rows[G2].Status, InstanceStatus.Running);
            _report.Expect("J live", _liveJobs.Contains(Real.Ticket(A, "j", 1)), true);
            _report.Expect("notices P received from A", _noticesToP, 1);
        }
        if (variant == "successor")
        {
            _report.Expect("successor", _rows[A].Status, InstanceStatus.Cancelled);
            _report.Expect("G", _rows[G].Status, InstanceStatus.Running);
            _report.Expect("J live", _liveJobs.Contains(Real.Ticket(A, "j", 1)), true);
            _report.Expect("cascades from the marker", _markerCascades.Count, 0);
        }
        return _report;
    }

    private readonly List<string> _sentTo = [];

    private string Ob() => _obligations.TryGetValue((A, 1), out var o) ? o.ToString() : "none";

    private string Show(string id)
    {
        var r = _rows[id];
        if (r.Retired) return $"[retired marker, creation {r.Creation}, revision {r.Revision}, parent {r.Parent ?? "null"}, children {r.Children ?? "null"}, jobs {r.Jobs ?? "null"}]";
        return $"[{r.Status}, creation {r.Creation}, revision {r.Revision}, parent {r.Parent ?? "null"}/{r.ParentCreation}, children {r.Children ?? "null"}, jobs {r.Jobs ?? "null"}]";
    }

    /// <summary>An operator terminates P. P's built terminate sends the parent-bound terminate T to A.</summary>
    private Message TerminateP()
    {
        Deliver(new Message("op-p", new InstanceRef(P), Kinds.Terminate, Sender: Operator, TargetCreation: 1));
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
        var row = _rows[A];
        var notice = Real.Finished(row);
        _inFlight.Add(notice);
        var marker = Real.Retire(row);
        if (rules != RtRules.Reviewed)
        {
            var children = rules == RtRules.Negative ? [] : Real.ReadRegistry(row.Children);
            _obligations[(A, row.Creation)] = new Obligation(row.Parent, row.ParentCreation, children, Real.ReadJobs(row.Jobs));
        }
        _rows[A] = marker;
        _aDrainOpen = false;
        _report.Line($"A Complete: notice {notice.Id} (ChildFinished {row.Status}) to P; retirement marker {Show(A)}; obligation {Ob()}");
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

    private void Deliver(Message m)
    {
        if (m.Kind == WorkflowKinds.ChildFinished && m.Target.Id == P && m.Sender == A) _noticesToP++;
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

    /// <summary>The proposed marker rule: a parent-bound terminate refused with RetiredCreation cascades over the retained obligation.</summary>
    private void MarkerCascade(Message m, InstanceRow marker)
    {
        if (rules == RtRules.Reviewed || !_obligations.TryGetValue((marker.Id, marker.Creation), out var ob)) return;
        if (rules != RtRules.CascadeAny && !ParentBound(m, ob.Parent, ob.ParentCreation, marker.Creation))
        { _report.Line($"  {m.Id}: sender {m.Sender}/{m.SenderCreation} is not the recorded parent {ob.Parent}/{ob.ParentCreation}: plain refusal, no cascade"); return; }
        _markerCascades.Add(m.Id);
        Cascade(marker.Id, marker.Creation, marker.Revision, ob.Children, ob.Jobs, "the refusing commit at the marker");
        _obligations[(marker.Id, marker.Creation)] = ob with { Children = [], Jobs = [] };
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
