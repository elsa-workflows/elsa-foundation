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
        ["live-p", "live-p-plus-1", "live-10001", "marker-10001", "open-drain-10001", "jobs-marker", "takeover", "crash-landed", "crash-lost", "delete-discharging",
         "thendelete-root-p-plus-1", "thendelete-root-10001", "thendelete-child-p-plus-1", "thendelete-child-10001", "thendelete-kept-p-plus-1", "thendelete-kept-10001",
         "key-reuse-discharging"];

    private Harness _h = null!;
    private Report _report = null!;
    private int _hooked;

    public Report Run()
    {
        _report = new Report($"RT-3/{variant}", rules.Name,
            "termination is feasible for every admitted registry: every descendant ends Cancelled, each receives exactly one terminate, no commit exceeds the send cap, the obligation clears, and P hears exactly one notice from A");
        _h = new Harness(rules, _report);
        if (variant.StartsWith("thendelete-", StringComparison.Ordinal)) return ThenDelete();
        if (variant == "key-reuse-discharging") return KeyReuse();
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
                _h.DeliverNow(Harness.Derive(A, "j2", 1, 1), "the owner's poll refuses and consumes j2 at the retired A (Worker.cs 1050-1055); j1 is still pending");
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
        var stored = _h.Sent.Where(m => m.Kind == Kinds.Terminate && m.Sender == A).GroupBy(m => m.Target.Id).ToDictionary(g => g.Key, g => g.Count());
        _report.Expect("children without exactly one stored terminate from A", ids.Count(id => stored.GetValueOrDefault(id) != 1), 0);
        _report.Expect("obligations still open", _h.OpenObligations, 0);
        _report.Expect("notices P received from A", _h.SentTo(P, WorkflowKinds.ChildFinished, A), 1);
        _report.Expect("A", _h.Status(A), variant is "marker-10001" or "jobs-marker" or "open-drain-10001" or "delete-discharging" ? "retired marker" : "Cancelled");
        if (variant is "live-p" or "live-p-plus-1" && rules.Paged && rules.PCascade == Program.PCascade)
            _report.Expect("cascade commits for A's registry (pages)", _h.CascadeCommits.Count(c => c.Actor == A), variant == "live-p" ? 1 : 2);
        if (variant == "crash-landed") _report.Expect("the dead owner's replay of the landed page landed", string.Join(",", _h.StaleReplays), "False");
        if (variant == "crash-lost")
        {
            var lost = _h.LostPages.FirstOrDefault(l => l.Actor == A);
            _report.Expect("pages a crash kept from the store", _h.LostPages.Count(l => l.Actor == A), 1);
            _report.Expect($"the new owner lands the lost page {lost.Page} from the same control",
                lost.Control is { } control && _h.CascadeCommits.Any(c => c.Actor == A && c.Page == lost.Page && c.Consumed.Count > 0 && c.Consumed[0] == control && c.Epoch == _h.Owners[1].Epoch), true);
        }
        if (variant == "jobs-marker")
        {
            _report.Expect("j1 (pending) consumed by the cascade", _h.CascadeCommits.Where(c => c.Actor == A).SelectMany(c => c.Consumed).Count(k => k == Harness.Derive(A, "j1", 1, 1)), 1);
            _report.Expect("j2 (absent) named by no cascade input", _h.CascadeCommits.Where(c => c.Actor == A).SelectMany(c => c.Consumed).Count(k => k == Harness.Derive(A, "j2", 1, 1)), 0);
        }
        _h.CommonChecks();
        return _report;
    }

    private List<string> Children(string parent, int count, int issuer)
    {
        var ids = Enumerable.Range(1, count).Select(n => FormattableString.Invariant($"g2/child/{issuer}.1.{n}")).ToList();
        foreach (var id in ids) _h.Seed(new InstanceRow(id, "rt", "c", InstanceStatus.Running, "{}", 2, parent, Creation: 1, StartIntent: "v1:c", ParentCreation: 1));
        return ids;
    }

    private void ExpectCascade(List<string> ids, string actor)
    {
        var deliveries = ids.Select(id => _h.TerminatesDelivered.GetValueOrDefault(id)).ToList();
        var notCancelled = ids.Count(id => _h.Status(id) != "Cancelled");
        _h.Line($"end: {_h.Name(actor)} {_h.Status(actor)}; children Cancelled {ids.Count - notCancelled} of {ids.Count}; terminates delivered per child: min {deliveries.DefaultIfEmpty().Min()}, max {deliveries.DefaultIfEmpty().Max()}; " +
            $"obligation {(_h.Obligations.TryGetValue(actor, out var ob) ? ob.ToString() : "none")}");
        _report.Expect("children not Cancelled", notCancelled, 0);
        _report.Expect("children that received no terminate", deliveries.Count(d => d == 0), 0);
        _report.Expect("children that received more than one terminate", deliveries.Count(d => d > 1), 0);
        var stored = _h.Sent.Where(m => m.Kind == Kinds.Terminate && m.Sender == actor).GroupBy(m => m.Target.Id).ToDictionary(g => g.Key, g => g.Count());
        _report.Expect($"children without exactly one stored terminate from {_h.Name(actor)}", ids.Count(id => stored.GetValueOrDefault(id) != 1), 0);
        _report.Expect("obligations still open", _h.OpenObligations, 0);
    }

    /// <summary>
    /// ThenDelete on a running instance: an operator's terminate that also deletes. Under the repaired rule one commit
    /// terminates the row, sends page 1 (both job cancels first) and retires the row with a Discharging obligation, and
    /// the pages go on at the marker. The kept variant's row is protected, so it is terminated and kept.
    /// </summary>
    private Report ThenDelete()
    {
        var kind = variant.Split('-')[1];
        var big = variant.EndsWith("10001", StringComparison.Ordinal);
        var actor = kind == "root" ? "1/r" : A;
        var children = big ? 10_001 : rules.PCascade - 1;
        if (kind != "root")
            _h.Seed(new InstanceRow(P, "rt", "p", InstanceStatus.Running, "{}", 4, null, Children: Real.WriteRegistry([new(A, 1)]), Creation: PCreation, StartIntent: "v1:p"), "P");
        var ids = Children(actor, children, 1);
        _h.Seed(new InstanceRow(actor, "rt", "a", InstanceStatus.Running, "{}", 7, kind == "root" ? null : P, Jobs: Real.WriteJobs(["j1", "j2"]),
            Children: Real.WriteRegistry([.. ids.Select(id => new CreationAddress(id, 1))]), Creation: 1, StartIntent: "v1:a", ParentCreation: kind == "root" ? 0 : PCreation), "A");
        var tickets = new[] { "j1", "j2" }.Select(k => { _h.JobGeneration[(actor, k)] = 1; return _h.SeedTicket(actor, 1, k, 1); }).ToList();
        if (kind == "kept") _h.Protects.Add(actor);
        _h.Line($"setup: A {(kind == "root" ? "a running root" : "a running child of P")}{(kind == "kept" ? ", protected by ProtectsDeletion (fixture)" : "")}, {children} children and 2 pending jobs: {children + 2} entries; P_cascade = {rules.PCascade}");
        _h.AddOutside(new Message("op-a", new InstanceRef(actor), Kinds.Terminate, Json.Write(new { ThenDelete = true, Instance = actor }), Harness.Operator, TargetCreation: 1));
        _h.Settle();
        ExpectCascade(ids, actor);
        _report.Expect("A", _h.Status(actor), kind == "kept" ? "Cancelled" : "retired marker");
        if (kind != "root") _report.Expect("notices P received from A", _h.SentTo(P, WorkflowKinds.ChildFinished, actor), 1);
        _report.Expect("job entries in the first page (jobs first)", _h.FirstPageJobEntries.GetValueOrDefault(actor, -1), 2);
        var first = _h.CascadeCommits.FirstOrDefault(c => c.Actor == actor && c.Page == 1).Consumed ?? [];
        _report.Expect("both pending job tickets consumed by the first page", tickets.All(first.Contains), true);
        if (kind != "kept")
            _report.Expect("the retiring commit's shape: list equal to the previous registry and jobs, cursor at most P_cascade",
                _h.ThenDeleteShapes.Count > 0 && _h.ThenDeleteShapes.All(x => x.Contains("jobs: True", StringComparison.Ordinal) && x.Contains(": True)", StringComparison.Ordinal)), true);
        _h.CommonChecks();
        return _report;
    }

    /// <summary>
    /// A keyed root with 2 P_cascade + 1 children is terminated, then deleted while Discharging; the caller reuses its key.
    /// The built admission (KeyAdmission.cs) admits the first start and binds a fresh physical id on reuse; the proposal
    /// keeps the binding's actor id and advances its creation (tenancy, R4-M6), mirrored here over the real request
    /// type, with the proposed cascade read held in the harness.
    /// </summary>
    private Report KeyReuse()
    {
        const string Key = "K";
        var spec = new StartSpec("rt", "r");
        // Existing prototype behaviour, on a scratch store: reuse after retirement binds a fresh physical id.
        long t = 1000;
        var scratch = new InMemoryStore(4, () => t);
        var built1 = Real.Accept(scratch, new KeyRequest(Key, "op-1", Kinds.Start, Start: spec, KeepForMillis: 5000));
        scratch.Instances[built1.RunId!] = Real.Retire(new InstanceRow(built1.RunId!, "rt", "r", InstanceStatus.Cancelled, "{}", 4, null));
        t += 10_000;
        var built2 = Real.Accept(scratch, new KeyRequest(Key, "op-2", Kinds.Start, Start: spec, KeepForMillis: 5000));
        _h.Line($"existing prototype behaviour (InMemoryStore.AcceptAsync, KeyAdmission.cs 48-84, scratch store): op-1 {built1.Outcome} at {built1.RunId}; after it retired and the window passed, op-2 {built2.Outcome} at {built2.RunId}, a fresh physical id that never meets the marker");

        var first = Real.Accept(_h.Store, new KeyRequest(Key, "op-1", Kinds.Start, Start: spec, KeepForMillis: 5000));
        var r = first.RunId!;
        _h.Names[r] = "R";
        var start = _h.Store.Messages.Values.Single(m => m.Message.Target.Id == r).Message;
        var ids = Children(r, 2 * rules.PCascade + 1, 3);
        var born = new InstanceRow(r, "rt", "r", InstanceStatus.Running, "{}", 1, null, Children: Real.WriteRegistry([.. ids.Select(id => new CreationAddress(id, 1))]),
            Creation: 1, StartIntent: Real.Fingerprint(spec with { Creation = new CreationStart(1, 0, "op-1") }, Kinds.Start, null));
        if (!_h.Commit(new InstanceRef(r).Partition, [new TurnCommit(r, born, null, [start.InboxKey], [], [], [], [], Json.Write(new[] { "started" }))], null, "R's start").Landed)
            throw new InvalidOperationException("R's start did not land");
        _h.Line($"op-1 {first.Outcome} at {r} by the real AcceptAsync; R's start commit (harness-composed, no model turn; the proposal's creation 1 in place of the built unqualified start) writes R at creation 1 with {ids.Count} children; P_cascade = {rules.PCascade}");

        var bindings = new Dictionary<string, (string Actor, long Until)> { [Key] = (r, first.KeepUntil!.Value) };
        var receipts = new Dictionary<(string, string), (KeyRequest Request, string Outcome)> { [(Key, "op-1")] = (new KeyRequest(Key, "op-1", Kinds.Start, Start: spec, KeepForMillis: 5000), "accepted") };
        var decisions = new List<string>();
        var acceptedWhileDischarging = 0;

        // The proposed admission: KeyAdmission.cs 54-82 with the binding's actor id kept and its creation advanced, and
        // (repaired) a read of the bound actor's cascade state in the same transaction.
        string Admit(string operation, bool deliverAtOnce)
        {
            var request = new KeyRequest(Key, operation, Kinds.Start, Start: spec, KeepForMillis: 5000);
            Real.ValidateKey(request);
            string outcome;
            var discharging = _h.Obligations.TryGetValue(bindings[Key].Actor, out var open) && open.Open;
            InboxKey? inserted = null;
            if (receipts.TryGetValue((Key, operation), out var receipt)) outcome = receipt.Request == request ? $"{receipt.Outcome} (stored receipt, nothing inserted)" : "conflict";
            else
            {
                var binding = bindings[Key];
                var marker = _h.Store.Instances[binding.Actor];
                if (_h.Now < binding.Until || !marker.Retired) outcome = "in-use";
                else if (rules.AdmissionReadsCascade && _h.Obligations.TryGetValue(binding.Actor, out var ob) && ob.Open) outcome = "in-progress (ObligationOutstanding: the bound actor's cascade is discharging; no receipt written)";
                else
                {
                    var next = marker.Creation + 1;
                    var message = new Message(request.WireId, new InstanceRef(binding.Actor), Kinds.Start, request.Payload,
                        Start: spec with { Creation = new CreationStart(next, marker.Creation, request.Digest) }, TargetCreation: next).WithInboxScope(long.MaxValue);
                    if (!_h.Commit(new InstanceRef(binding.Actor).Partition, [new TurnCommit(binding.Actor, null, null, [], [], [message], [], [], null)], null, $"admission of {operation}").Landed)
                        throw new InvalidOperationException("admission insert did not land");
                    receipts[(Key, operation)] = (request, "accepted");
                    bindings[Key] = (binding.Actor, _h.Now + request.KeepForMillis);
                    outcome = "accepted";
                    inserted = message.InboxKey;
                    if (discharging) acceptedWhileDischarging++;
                }
            }
            _h.Line($"key admission of {Key}/{operation}{(discharging ? " (R's cascade is discharging)" : "")}: {outcome}");
            if (inserted is { } key && deliverAtOnce) _h.DeliverNow(key, $"  R's owner delivers the start {key.Id} at once");
            decisions.Add(outcome.Split(' ')[0] + (outcome.Contains("stored receipt", StringComparison.Ordinal) ? " (stored)" : ""));
            return outcome;
        }

        _h.AfterPage = (actor, page) =>
        {
            if (actor != r || page != 1 || _hooked++ != 0) return;
            _h.Line("an operator's qualified delete of the ended R arrives while its obligation is Discharging");
            _h.AddOutside(new Message("op-delete-r", new InstanceRef(r), Kinds.Delete, Sender: Harness.Operator, TargetCreation: 1));
        };
        _h.AfterDelete = actor =>
        {
            if (actor != r) return;
            _h.Now += 10_000;   // the key's reuse window has passed
            _h.Line($"the caller reuses {Key} while R's marker is Discharging: a new operation, then the same operation again");
            Admit("op-2", deliverAtOnce: true);
            Admit("op-2", deliverAtOnce: true);
        };
        _h.AddOutside(new Message("op-r", new InstanceRef(r), Kinds.Terminate, Sender: Harness.Operator, TargetCreation: 1));
        _h.Settle();
        _h.Line("after the last page, the caller retries op-2");
        Admit("op-2", deliverAtOnce: false);
        _h.Settle();

        ExpectCascade(ids, r);
        _h.Line($"key admission's answers to op-2: [{string.Join(" ; ", decisions)}]");
        _report.Expect("starts admitted (receipt written) while R's cascade was discharging", acceptedWhileDischarging, 0);
        _report.Expect("starts refused with ObligationOutstanding", _h.StartRefusals.Count(x => x.EndsWith("ObligationOutstanding", StringComparison.Ordinal)), 0);
        _report.Expect("R's row", _h.Store.Instances[r] is { Retired: false } row ? $"{row.Status} at creation {row.Creation}" : "retired marker", "Running at creation 2");
        _report.ExpectSequence("successor creations written at R's id", _h.Births, [$"{r}@2"]);
        _report.Expect("op-2's receipt names a creation that was written", receipts.ContainsKey((Key, "op-2")) && _h.Births.Contains($"{r}@2"), true);
        _h.CommonChecks();
        return _report;
    }
}
