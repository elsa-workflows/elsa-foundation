namespace Kernel.Review5;

/// <summary>
/// RT-3 (R5-B2): a termination cascade over a registry larger than one commit may send. P → A → N children, the
/// children at generated ids in partition 2, A in partition 1. The registry is written by the real
/// <c>ChildRegistry.Write</c>; at 10,001 children it is the reviewer's 398,936-byte registry.
/// </summary>
sealed class Rt3(string variant, Rules rules)
{
    public const string P = "0/p", A = "g1/child/0.1.1";
    private const long PCreation = 2;

    public static readonly string[] Variants =
        ["live-p", "live-p-plus-1", "live-10001", "marker-10001", "open-drain-10001", "jobs-marker", "takeover", "crash-landed", "crash-lost", "delete-discharging"];

    private Harness _h = null!;
    private Report _report = null!;
    private int _hooked;

    public Report Run()
    {
        _report = new Report($"RT-3/{variant}", rules.Name,
            "termination is feasible for every admitted registry: every descendant ends Cancelled, each receives exactly one terminate, no commit exceeds the send cap, the obligation clears, and P hears exactly one notice from A");
        _h = new Harness(rules, _report);
        var p = Program.PCascade;
        var (children, live, jobs) = variant switch
        {
            "live-p" => (p, true, 0),
            "live-p-plus-1" => (p + 1, true, 0),
            "jobs-marker" => (p - 1, false, 2),
            _ => (10_001, !(variant is "marker-10001" or "open-drain-10001"), 0),
        };
        var ids = Enumerable.Range(1, children).Select(n => FormattableString.Invariant($"g2/child/1.1.{n}")).ToList();
        var registry = Real.WriteRegistry([.. ids.Select(id => new CreationAddress(id, 1))]);
        _h.Seed(new InstanceRow(P, "rt", "p", InstanceStatus.Running, "{}", 4, null, Children: Real.WriteRegistry([new(A, 1)]), Creation: PCreation, StartIntent: "v1:p"), "P");
        var jobKeys = Enumerable.Range(1, jobs).Select(n => $"j{n}").ToList();
        _h.Seed(new InstanceRow(A, "rt", "a", live ? InstanceStatus.Running : InstanceStatus.Faulted, "{}", 7, P, Fault: live ? null : "parked",
            Jobs: Real.WriteJobs(jobKeys), Children: registry, Creation: 1, StartIntent: "v1:a", ParentCreation: PCreation), "A");
        foreach (var id in ids) _h.Seed(new InstanceRow(id, "rt", "c", InstanceStatus.Running, "{}", 2, A, Creation: 1, StartIntent: "v1:c", ParentCreation: 1));
        foreach (var key in jobKeys) { _h.JobGeneration[(A, key)] = 1; _h.HeldBack.Add(_h.SeedTicket(A, 1, key, 1)); }
        if (!live) _h.DrainOpen.Add(A);
        // The schedule's pages, kept inside the cascade for any P_cascade that gives 10,001 children three pages or more:
        // at the default 1,000 (11 pages) a takeover after page 3, a crash at page 5, the delete after page 2, Complete after page 3.
        var pages = (children + jobs + rules.PCascade - 1) / rules.PCascade;
        _h.Line($"setup: A {(live ? "running" : "parked, its delete drain open")}, {children} children at g2/child/1.1.1..{children} (each Running, parent A/1), {jobs} jobs; " +
            $"A's registry (ChildRegistry.Write) is {System.Text.Encoding.UTF8.GetByteCount(registry ?? "")} UTF-8 bytes; P_cascade = {rules.PCascade}, send cap = {Harness.SendCap}, {pages} page(s)");

        int Within(int page, int last) => Math.Max(1, Math.Min(page, last));
        var (takeoverAfter, crashAt, deleteAfter, completeAfter) = (Within(3, pages - 1), Math.Max(2, Within(5, pages - 1)), Within(2, pages - 2), Within(3, pages - 1));
        switch (variant)
        {
            case "takeover": _h.Hook = (actor, page) => actor == A && page == takeoverAfter && _hooked++ == 0 ? PageHook.TakeoverAfter : PageHook.None; break;
            case "crash-landed": _h.Hook = (actor, page) => actor == A && page == crashAt && _hooked++ == 0 ? PageHook.CrashLanded : PageHook.None; break;
            case "crash-lost": _h.Hook = (actor, page) => actor == A && page == crashAt && _hooked++ == 0 ? PageHook.CrashLost : PageHook.None; break;
            case "delete-discharging":
                _h.AfterPage = (actor, page) =>
                {
                    if (actor != A || page != deleteAfter || _hooked++ != 0) return;
                    _h.Line("an operator's qualified delete of the ended A arrives while its obligation is Discharging");
                    _h.AddOutside(new Message("op-delete-a", new InstanceRef(A), Kinds.Delete, Sender: Harness.Operator, TargetCreation: 1));
                };
                break;
            case "open-drain-10001":
                _h.AfterPage = (actor, page) => { if (actor == A && page == completeAfter && _hooked++ == 0) _h.Complete(A); };
                break;
        }

        _h.AddOutside(new Message("op-p", new InstanceRef(P), Kinds.Terminate, Sender: Harness.Operator, TargetCreation: PCreation));
        _h.Step();
        var t = _h.Sent.Single(m => m.Target.Id == A && m.Kind == Kinds.Terminate);
        _h.Line($"T = {t.Id} from P");
        if (variant is "marker-10001" or "jobs-marker")
        {
            _h.HeldBack.Add(t.InboxKey);
            _h.Line("T is committed while A's delete drain is open; its delivery is delayed past A's Complete (the reviewer's R5-B2 trace)");
            _h.Complete(A);
            _h.Settle();
            if (variant == "jobs-marker")
                _h.DeliverNow(Harness.Derive(A, "j2", 1, 1), "a fresh owner's poll refuses and consumes j2 at the retired A (Worker.cs 1050-1055); j1 is still pending");
            _h.HeldBack.Remove(t.InboxKey);
        }
        else if (variant == "open-drain-10001") _h.Line($"T is delivered while A's delete drain is open (J-1); Complete runs after page {completeAfter}");
        _h.Settle();
        _h.HeldBack.Clear();
        _h.Settle();

        var notCancelled = ids.Count(id => _h.Status(id) != "Cancelled");
        var deliveries = ids.Select(id => _h.TerminatesDelivered.GetValueOrDefault(id)).ToList();
        _h.Line($"end: A {_h.Status(A)}; children Cancelled {children - notCancelled} of {children}; terminates delivered per child: min {deliveries.DefaultIfEmpty().Min()}, max {deliveries.DefaultIfEmpty().Max()}; " +
            $"obligation {(_h.Obligations.TryGetValue(A, out var ob) ? ob.ToString() : "none")}; T pending {_h.Pending(t.InboxKey)}");
        _report.Expect("children not Cancelled", notCancelled, 0);
        _report.Expect("children that received no terminate", deliveries.Count(d => d == 0), 0);
        _report.Expect("children that received more than one terminate", deliveries.Count(d => d > 1), 0);
        _report.Expect("obligations still open", _h.OpenObligations, 0);
        _report.Expect("A's child terminates stored without the cascade origin mark", _h.Sent.Count(m => m.Kind == Kinds.Terminate && m.Sender == A && !_h.Marks.Contains(m.InboxKey)), 0);
        _report.Expect("notices P received from A", _h.SentTo(P, WorkflowKinds.ChildFinished, A), 1);
        _report.Expect("A", _h.Status(A), variant is "marker-10001" or "jobs-marker" or "open-drain-10001" or "delete-discharging" ? "retired marker" : "Cancelled");
        if (variant is "live-p" or "live-p-plus-1" && rules.Paged && rules.PCascade == Program.PCascade)
            _report.Expect("cascade commits for A's registry (pages)", _h.CascadeCommits.Count(c => c.Actor == A), variant == "live-p" ? 1 : 2);
        if (variant == "crash-landed") _report.Expect("the dead owner's replay of the landed page landed", string.Join(",", _h.StaleReplays), "False");
        if (variant == "jobs-marker")
        {
            _report.Expect("j1 (pending) consumed by the cascade", _h.CascadeCommits.Where(c => c.Actor == A).SelectMany(c => c.Consumed).Count(k => k == Harness.Derive(A, "j1", 1, 1)), 1);
            _report.Expect("j2 (absent) named by no cascade input", _h.CascadeCommits.Where(c => c.Actor == A).SelectMany(c => c.Consumed).Count(k => k == Harness.Derive(A, "j2", 1, 1)), 0);
        }
        _h.CommonChecks();
        return _report;
    }
}
