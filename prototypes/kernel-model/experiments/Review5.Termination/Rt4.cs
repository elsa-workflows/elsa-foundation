namespace Kernel.Review5;

/// <summary>
/// RT-4 (R5-M5): the parent binding of kernel-composed (marked) terminates at every recipient state, and no narrowing
/// of a model's application terminate. P → A → G is the genuine tree; Q → H → HK is unrelated,
/// with H at the right creation in the same tenant. A is parked with its delete drain open, and RM-1's negative fixture
/// has A's retirement list H beside G. P's delayed terminate then discharges A's marker, which sends terminate to G
/// and to H.
/// </summary>
sealed class Rt4(string variant, Rules rules)
{
    public const string P = "0/p", A = "g1/child/0.1.1", G = "g2/child/1.1.1", Q = "0/q", H = "g2/child/7.1.1", HK = "g2/child/7.1.2", X = "0/x";
    private const long PCreation = 2;

    public static readonly string[] Variants = ["running-unrelated", "ended-registry", "genuine-child", "operator", "model-send", "forgery"];

    private static readonly Dictionary<string, string> Invariants = new()
    {
        ["running-unrelated"] = "a kernel-composed terminate carries the cascade origin mark and is parent-bound at a running recipient: H (parent Q) refuses A's marked terminate with reason parent, and H and its child keep running",
        ["ended-registry"] = "a marked terminate is parent-bound at an ended recipient that still owns a registry: H refuses A's marked terminate with reason parent, and its child keeps running",
        ["genuine-child"] = "a genuine child accepts its parent's terminate through the actual command path: P's terminate ends A, and A's ends G",
        ["operator"] = "an operator's terminate is not parent-bound: it ends a running H and its child",
        ["model-send"] = "a model's application terminate carries no cascade origin mark and keeps its built authorization: X's ITurn.Send ends the running non-child H (no narrowing)",
        ["forgery"] = "only kernel composition sets the cascade origin mark: a model turn's marked send, and a composed terminate naming another sender, are refused at the commit, and H keeps running",
    };

    private Harness _h = null!;
    private Report _report = null!;

    public Report Run()
    {
        _report = new Report($"RT-4/{variant}", rules.Name, Invariants[variant]);
        _h = new Harness(rules, _report);
        var aLive = variant == "genuine-child";
        _h.Seed(new InstanceRow(P, "rt", "p", InstanceStatus.Running, "{}", 4, null, Children: Real.WriteRegistry([new(A, 1)]), Creation: PCreation, StartIntent: "v1:p"), "P");
        _h.Seed(new InstanceRow(A, "rt", "a", aLive ? InstanceStatus.Running : InstanceStatus.Faulted, "{}", 7, P, Fault: aLive ? null : "parked",
            Children: Real.WriteRegistry([new(G, 1)]), Creation: 1, StartIntent: "v1:a", ParentCreation: PCreation), "A");
        _h.Seed(new InstanceRow(G, "rt", "g", InstanceStatus.Running, "{}", 3, A, Creation: 1, StartIntent: "v1:g", ParentCreation: 1), "G");
        _h.Seed(new InstanceRow(Q, "rt", "q", InstanceStatus.Running, "{}", 5, null, Children: Real.WriteRegistry([new(H, 1)]), Creation: 1, StartIntent: "v1:q"), "Q");
        _h.Seed(new InstanceRow(H, "rt", "h", variant == "ended-registry" ? InstanceStatus.Finished : InstanceStatus.Running, "{}", 3, Q,
            Children: Real.WriteRegistry([new(HK, 1)]), Creation: 1, StartIntent: "v1:h", ParentCreation: 1), "H");
        _h.Seed(new InstanceRow(HK, "rt", "hk", InstanceStatus.Running, "{}", 2, H, Creation: 1, StartIntent: "v1:hk", ParentCreation: 1), "HK");
        _h.Seed(new InstanceRow(X, "rt", "x", InstanceStatus.Running, "{}", 3, null, Creation: 1, StartIntent: "v1:x"), "X");
        if (!aLive) _h.DrainOpen.Add(A);
        _h.Line($"setup: P → A → G; Q → H → HK; X a running root unrelated to H; H {_h.Status(H)} at creation 1 with parent Q/1 and registry [HK/1]; A {(aLive ? "running" : "parked, its delete drain open")}");

        if (variant == "model-send")
        {
            _h.ModelTurn(X, turn => turn.Send(new InstanceRef(H), Kinds.Terminate, null, new SendOptions(TargetCreation: 1)), null, "X's model turn sends kernel.terminate to H through DurableTurn.Send (ITurn.Send)");
            _h.Settle();
            _h.Line($"end: H {_h.Status(H)}; HK {_h.Status(HK)}; refusals: {(_h.Refusals.Count == 0 ? "none" : string.Join("; ", _h.Refusals))}");
            _report.Expect("terminates from X to H stored", _h.SentTo(H, Kinds.Terminate, X), 1);
            _report.Expect("marked messages among them", _h.Sent.Count(m => m.Sender == X && _h.Marks.Contains(m.InboxKey)), 0);
            _report.Expect("H (WorkflowInstance.cs 63-64, 155-158, 177-203, as built)", _h.Status(H), "Cancelled");
            _report.Expect("HK", _h.Status(HK), "Cancelled");
            _report.Expect("refusals with reason parent", _h.Refusals.Count(r => r.EndsWith(": parent", StringComparison.Ordinal)), 0);
            _h.CommonChecks();
            return _report;
        }
        if (variant == "forgery")
        {
            var revision = _h.Store.Instances[X].Revision;
            var results = new List<CommitResult>
            {
                _h.ModelTurn(X, turn => turn.Send(new InstanceRef(H), Kinds.Terminate, null, new SendOptions(TargetCreation: 1)), m => true,
                    "(a) X's model turn sends kernel.terminate to H and sets the mark on it"),
                _h.ModelTurn(X, turn => turn.Send(new InstanceRef(H), "app.ping", null, new SendOptions(TargetCreation: 1)), m => true,
                    "(b) X's model turn sets the mark on an application message"),
                _h.ModelTurn(X, turn => turn.Add(new Message(FormattableString.Invariant($"{Real.Origin(X, 1)}#{revision + 1}.0007"), new InstanceRef(H), Kinds.Terminate,
                    Sender: Q, TargetCreation: 1, SenderCreation: 1)), m => true, "(c) a composed kernel.terminate from X's commit names Q, H's parent, as Sender, marked"),
            };
            _h.Settle();
            _h.Line($"end: X revision {_h.Store.Instances[X].Revision}; H {_h.Status(H)}; HK {_h.Status(HK)}; marks on X's sends {_h.Sent.Count(m => m.Sender == X && _h.Marks.Contains(m.InboxKey))}");
            _report.ExpectSequence("the three commits landed", [.. results.Select(r => r.Landed.ToString())], ["False", "False", "False"]);
            _report.Expect("(c) is refused by the sender rule", results[2].Error?.Contains("names Sender Q/1, not the committing X/1", StringComparison.Ordinal) == true, true);
            _report.Expect("X's row revision", _h.Store.Instances[X].Revision, revision);
            _report.Expect("messages from X's commits stored", _h.Sent.Count(m => m.Sender == X), 0);
            _report.Expect("stored messages carrying a mark no kernel composition set (structural check of the mark rule)", _h.Sent.Count(m => m.Sender == X && _h.Marks.Contains(m.InboxKey)), 0);
            _report.Expect("H", _h.Status(H), "Running");
            _report.Expect("HK", _h.Status(HK), "Running");
            _h.CommonChecks();
            return _report;
        }
        if (variant == "operator")
        {
            _h.AddOutside(new Message("op-h", new InstanceRef(H), Kinds.Terminate, Sender: Harness.Operator, TargetCreation: 1));
            _h.Settle();
            _report.Expect("H", _h.Status(H), "Cancelled");
            _report.Expect("HK", _h.Status(HK), "Cancelled");
            _report.Expect("refusals with reason parent", _h.Refusals.Count(r => r.EndsWith(": parent", StringComparison.Ordinal)), 0);
            _h.CommonChecks();
            return _report;
        }

        _h.AddOutside(new Message("op-p", new InstanceRef(P), Kinds.Terminate, Sender: Harness.Operator, TargetCreation: PCreation));
        _h.Step();
        var t = _h.Sent.Single(m => m.Target.Id == A && m.Kind == Kinds.Terminate);
        if (!aLive)
        {
            _h.HeldBack.Add(t.InboxKey);
            _h.Line($"T = {t.Id}, held back past A's Complete");
            _h.Complete(A, extraChildren: [new(H, 1)]);
            _h.Settle();
            _h.HeldBack.Remove(t.InboxKey);
        }
        _h.Settle();

        var toH = _h.Sent.Where(m => m.Target.Id == H && m.Kind == Kinds.Terminate).Select(m => m.Id).ToList();
        _h.Line($"end: A {_h.Status(A)}; G {_h.Status(G)}; H {_h.Status(H)}; HK {_h.Status(HK)}; terminates to H [{string.Join(", ", toH)}]; refusals: {(_h.Refusals.Count == 0 ? "none" : string.Join("; ", _h.Refusals))}");
        _report.Expect("G", _h.Status(G), "Cancelled");
        _report.Expect("obligations still open", _h.OpenObligations, 0);
        _report.Expect("notices P received from A", _h.SentTo(P, WorkflowKinds.ChildFinished, A), 1);
        if (variant == "genuine-child")
        {
            _report.Expect("A", _h.Status(A), "Cancelled");
            _report.Expect("terminates sent to H", toH.Count, 0);
        }
        else
        {
            _report.Expect("terminates from A's marker to H", toH.Count, 1);
            _report.Expect("A's cascade terminate to H carries the mark (structural: every page terminate is marked)", toH.Count == 1 && _h.Marks.Contains(InboxKey.Issued(toH[0])), true);
            _report.Expect("H's refusal of A's terminate", _h.Refusals.Any(r => toH.Count == 1 && r == $"{toH[0]}: parent") ? "parent" : "accepted", "parent");
            _report.Expect("H", _h.Status(H), variant == "ended-registry" ? "Finished" : "Running");
            _report.Expect("HK", _h.Status(HK), "Running");
            _report.Expect("terminates sent to HK", _h.SentTo(HK, Kinds.Terminate), 0);
            _report.Expect("notices Q received from H", _h.SentTo(Q, WorkflowKinds.ChildFinished, H), 0);
        }
        _h.CommonChecks();
        return _report;
    }
}
