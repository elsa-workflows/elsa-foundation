namespace Kernel.Review5;

/// <summary>
/// RT-4 (R5-M5): the parent binding at every recipient state. P → A → G is the genuine tree; Q → H → HK is unrelated,
/// with H at the right creation in the same tenant. A is parked with its delete drain open, and RM-1's negative fixture
/// has A's retirement list H beside G. P's delayed terminate then discharges A's marker, which sends terminate to G
/// and to H.
/// </summary>
sealed class Rt4(string variant, Rules rules)
{
    public const string P = "0/p", A = "g1/child/0.1.1", G = "g2/child/1.1.1", Q = "0/q", H = "g2/child/7.1.1", HK = "g2/child/7.1.2";
    private const long PCreation = 2;

    public static readonly string[] Variants = ["running-unrelated", "ended-registry", "genuine-child", "operator"];

    private static readonly Dictionary<string, string> Invariants = new()
    {
        ["running-unrelated"] = "a terminate from an instance is parent-bound at a running recipient: H (parent Q) refuses A's terminate, and H and its child keep running",
        ["ended-registry"] = "a terminate from an instance is parent-bound at an ended recipient that still owns a registry: H refuses A's terminate, and its child keeps running",
        ["genuine-child"] = "a genuine child accepts its parent's terminate through the actual command path: P's terminate ends A, and A's ends G",
        ["operator"] = "an operator's terminate is not parent-bound: it ends a running H and its child",
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
        if (!aLive) _h.DrainOpen.Add(A);
        _h.Line($"setup: P → A → G; Q → H → HK; H {_h.Status(H)} at creation 1 with parent Q/1 and registry [HK/1]; A {(aLive ? "running" : "parked, its delete drain open")}");

        if (variant == "operator")
        {
            _h.AddOutside(new Message("op-h", new InstanceRef(H), Kinds.Terminate, Sender: Harness.Operator, TargetCreation: 1));
            _h.Settle();
            _report.Expect("H", _h.Status(H), "Cancelled");
            _report.Expect("HK", _h.Status(HK), "Cancelled");
            _report.Expect("refusals by the parent binding", _h.Refusals.Count(r => r.EndsWith("parent binding", StringComparison.Ordinal)), 0);
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
            _report.Expect("H's refusal of A's terminate", _h.Refusals.Any(r => toH.Count == 1 && r == $"{toH[0]}: parent binding") ? "parent binding" : "accepted", "parent binding");
            _report.Expect("H", _h.Status(H), variant == "ended-registry" ? "Finished" : "Running");
            _report.Expect("HK", _h.Status(HK), "Running");
            _report.Expect("terminates sent to HK", _h.SentTo(HK, Kinds.Terminate), 0);
            _report.Expect("notices Q received from H", _h.SentTo(Q, WorkflowKinds.ChildFinished, H), 0);
        }
        _h.CommonChecks();
        return _report;
    }
}
