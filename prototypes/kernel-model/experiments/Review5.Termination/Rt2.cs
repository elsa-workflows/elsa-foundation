namespace Kernel.Review5;

/// <summary>
/// RT-2 (R5-B1, R5-m3): a retained or listed job ticket that is pending, done and kept, absent, or a mix, when the
/// parent's terminate reaches the marker (or the open drain, or the live row). The tree is P → A → G → G2. P is at
/// creation 2; A, G and G2 sit at generated child ids. A is parked with an unprotected delete drain open, except in
/// <c>live-pending</c>. Every write goes through the real <c>InMemoryStore.CommitAsync</c>.
/// </summary>
sealed class Rt2(string variant, Rules rules)
{
    public const string P = "0/p", A = "g1/child/0.1.1", G = "g2/child/1.1.1", G2 = "g2/child/1.1.2", B = "g1/child/0.1.9";
    private const long PCreation = 2;

    public static readonly string[] Variants =
        ["witness", "pending", "done-kept", "absent", "mixed", "settle-after", "duplicate", "takeover", "open-drain-mixed", "live-pending", "generations", "off-registry"];

    private static readonly Dictionary<string, string> Invariants = new()
    {
        ["witness"] = "the reviewer's store witness, replayed: the cascade's commit lands with the ticket pending and with it absent, storing the child send and consuming the parent command",
        ["generations"] = "the cancel names exactly the listed (origin, creation, key, generation): a newer same-origin generation and another instance's ticket are untouched",
        ["off-registry"] = "retirement refuses a job pair the registry does not hold, so the marker lists only registry pairs, and the cascade cancels the current generation",
    };

    private enum Ticket { Pending, DoneKept, Absent }

    private Harness _h = null!;
    private Report _report = null!;
    private readonly List<InboxKey> _heldTickets = [];   // pending tickets of A: the Worker's poll reaches them after T (the schedule's choice)

    public Report Run()
    {
        _report = new Report($"RT-2/{variant}", rules.Name, Invariants.GetValueOrDefault(variant,
            "after P's tree terminate, G and G2 end Cancelled, the cascade's commit lands once with exactly its present inputs counted, no obligation stays open, and P hears exactly one notice from A"));
        if (variant == "witness") return Witness();
        _h = new Harness(rules, _report);
        (List<(string, long, Ticket)> jobs, bool live) = variant switch
        {
            "pending" or "settle-after" => (new List<(string, long, Ticket)> { ("j", 1L, Ticket.Pending) }, false),
            "done-kept" => ([("j", 1L, Ticket.DoneKept)], false),
            "absent" or "duplicate" or "takeover" => ([("j", 1L, Ticket.Absent)], false),
            "mixed" => ([("j1", 1L, Ticket.Pending), ("j2", 1L, Ticket.Absent), ("j3", 1L, Ticket.DoneKept)], false),
            "open-drain-mixed" => ([("j1", 1L, Ticket.Pending), ("j2", 1L, Ticket.DoneKept)], false),
            "live-pending" => ([("j", 1L, Ticket.Pending)], true),
            "generations" => ([("j", 1L, Ticket.Pending)], false),
            "off-registry" => ([("j", 2L, Ticket.Pending)], false),
            _ => throw new ArgumentException(variant),
        };
        List<(string Key, long Generation, Ticket State)> tickets = [.. jobs];
        Tree(live, tickets);

        var t = TerminateP();
        switch (variant)
        {
            case "live-pending":
                _h.Line("A is running; T is delivered at once and A's own built terminate cancels J (the live-actor path, WorkflowInstance.cs 196-198)");
                _h.HeldBack.Remove(t.InboxKey);
                _h.Settle();
                break;
            case "open-drain-mixed":
                _h.Line("T is delivered while A's delete drain is open (J-1); then the drain completes");
                _h.HeldBack.Remove(t.InboxKey);
                _h.Settle();
                _h.Complete(A);
                _h.Settle();
                break;
            case "settle-after":
                _h.Line("T arrives before the ticket's settle: the other arrival order");
                _h.Complete(A);
                _h.Settle();
                _h.HeldBack.Remove(t.InboxKey);
                _h.Settle();
                _h.Takeover(1, "(a fresh owner, as in the reviewer's trace)");
                _h.DeliverNow(Harness.Derive(A, "j", 1, 1), "the fresh owner polls J after the cascade");
                _h.Settle();
                break;
            case "off-registry":
                _h.Line("A's registry holds j at generation 2 (restarted once); two faulty retirement lists are tried before the correct one");
                _h.Complete(A, alterJobs: js => [.. js, ("x", 1)]);
                if (_h.Status(A) != "retired marker") _h.Complete(A, alterJobs: js => [.. js.Select(j => (j.Key, 1L))]);
                _report.Expect("A after the two faulty retirement lists", _h.Status(A), rules.AcceptOffRegistry ? "retired marker" : "Faulted");
                if (_h.Status(A) != "retired marker") _h.Complete(A);
                _report.Expect("the marker lists only registry pairs", _h.Obligations.TryGetValue(A, out var listed) ? string.Join(", ", listed.Jobs.Select(j => $"{j.Key}@g{j.Generation}")) : "none", "j@g2");
                _h.Settle();
                _h.HeldBack.Remove(t.InboxKey);
                _h.Settle();
                break;
            default:
                _h.Line("T is committed while A's delete drain is open; its delivery is delayed past A's Complete");
                _h.Complete(A);
                _h.Settle();
                if (tickets.Any(x => x.State == Ticket.Absent))
                {
                    _h.Takeover(1, "(a fresh owner)");
                    foreach (var (key, generation, _) in tickets.Where(x => x.State == Ticket.Absent))
                        _h.DeliverNow(Harness.Derive(A, key, 1, generation), $"the fresh owner polls {key}: A is retired, so the Worker refuses and consumes the ticket (Worker.cs 1166-1177, 1050-1055); the marker still lists {key}");
                }
                if (variant == "takeover") _h.Takeover(1, "between the ticket's settle and P's terminate");
                if (variant == "duplicate") _h.Hook = (actor, page) => actor == A && page == 1 && _h.CascadeCommits.All(c => c.Actor != A) ? PageHook.CrashLanded : PageHook.None;
                _h.HeldBack.Remove(t.InboxKey);
                _h.Settle();
                break;
        }
        _h.Settle();
        foreach (var key in _heldTickets) _h.HeldBack.Remove(key);
        _h.Settle();
        Assertions(t, tickets);
        _h.CommonChecks();
        return _report;
    }

    private void Tree(bool live, List<(string Key, long Generation, Ticket State)> tickets)
    {
        _h.Seed(new InstanceRow(P, "rt", "p", InstanceStatus.Running, "{}", 4, null, Children: Real.WriteRegistry([new(A, 1)]), Creation: PCreation, StartIntent: "v1:p"), "P");
        _h.Seed(new InstanceRow(A, "rt", "a", live ? InstanceStatus.Running : InstanceStatus.Faulted, "{}", 7, P, Fault: live ? null : "parked",
            Jobs: Real.WriteJobs([.. tickets.Select(x => x.Key)]), Children: Real.WriteRegistry([new(G, 1)]), Creation: 1, StartIntent: "v1:a", ParentCreation: PCreation), "A");
        _h.Seed(new InstanceRow(G, "rt", "g", InstanceStatus.Running, "{}", 3, A, Children: Real.WriteRegistry([new(G2, 1)]), Creation: 1, StartIntent: "v1:g", ParentCreation: 1), "G");
        _h.Seed(new InstanceRow(G2, "rt", "g2", InstanceStatus.Running, "{}", 2, G, Creation: 1, StartIntent: "v1:g2", ParentCreation: 1), "G2");
        if (!live) _h.DrainOpen.Add(A);
        foreach (var (key, generation, state) in tickets)
        {
            _h.JobGeneration[(A, key)] = generation;
            var id = _h.SeedTicket(A, 1, key, generation);
            _heldTickets.Add(id);
            _h.HeldBack.Add(id);
            if (state == Ticket.DoneKept)
            {
                _h.Store.Messages[id].Done = true;
                _h.KeptBy[id] = "Consumed";
            }
        }
        if (variant == "generations")
        {
            // Fixtures placed directly, labelled: a newer same-origin generation of j, and an unrelated instance B with its own j.
            _heldTickets.Add(_h.SeedTicket(A, 1, "j", 2));
            _h.HeldBack.Add(_heldTickets[^1]);
            _h.Seed(new InstanceRow(B, "rt", "b", InstanceStatus.Running, "{}", 2, "0/q", Jobs: Real.WriteJobs(["j"]), Creation: 1, StartIntent: "v1:b", ParentCreation: 1), "B");
            _h.JobGeneration[(B, "j")] = 1;
            _h.SeedTicket(B, 1, "j", 1);
            _h.ForeignActor = B;
            _h.Line($"fixture (below admission, it tests the derivation): a same-origin ticket {Harness.Derive(A, "j", 1, 2).Id} of a newer generation, and B's own {Harness.Derive(B, "j", 1, 1).Id}");
        }
        _h.Line($"setup: P {Show(P)}; A {Show(A)}; G {Show(G)}; G2 {Show(G2)}");
        _h.Line($"A's tickets: {string.Join("; ", tickets.Select(x => $"{x.Key}@g{x.Generation} = {Harness.Derive(A, x.Key, 1, x.Generation).Id} ({x.State})"))}" +
            $"{(tickets.Any(x => x.State == Ticket.DoneKept) ? "; done and kept is a Done row with KeptBy Consumed, the proposed column the harness holds" : "")}");
    }

    private string Show(string id)
    {
        var r = _h.Store.Instances[id];
        return r.Retired ? $"[retired marker, creation {r.Creation}, revision {r.Revision}]"
            : $"[{r.Status}, creation {r.Creation}, revision {r.Revision}, parent {(r.Parent is null ? "null" : _h.Name(r.Parent))}/{r.ParentCreation}, children {r.Children ?? "null"}, jobs {r.Jobs ?? "null"}]";
    }

    /// <summary>An operator terminates P (an outside add); P's built terminate sends the parent-bound terminate T to A, held back.</summary>
    private Message TerminateP()
    {
        _h.AddOutside(new Message("op-p", new InstanceRef(P), Kinds.Terminate, Sender: Harness.Operator, TargetCreation: PCreation));
        _h.Step();
        var t = _h.Sent.Single(m => m.Target.Id == A && m.Kind == Kinds.Terminate);
        _h.HeldBack.Add(t.InboxKey);
        _h.Line($"T = {t.Id} (Sender P/{t.SenderCreation}, TargetCreation {t.TargetCreation}, issued), stored and held back");
        return t;
    }

    private void Assertions(Message t, List<(string Key, long Generation, Ticket State)> tickets)
    {
        _h.Line($"end: P {Show(P)}; A {Show(A)}; G {Show(G)}; G2 {Show(G2)}");
        _h.Line($"obligations: {string.Join("; ", _h.Obligations.Where(o => o.Value.Entries > 0).Select(o => $"{_h.Name(o.Key)} {o.Value}"))}");
        _h.Line($"refusals: {string.Join("; ", _h.Refusals)}");
        var cascades = _h.CascadeCommits.Where(c => c.Actor == A && c.Page == 1).ToList();
        _report.Expect("T consumed (no longer pending)", _h.Pending(t.InboxKey), false);
        _report.Expect("terminates to G stored", _h.SentTo(G, Kinds.Terminate), 1);
        _report.Expect("G", _h.Status(G), "Cancelled");
        _report.Expect("G2", _h.Status(G2), "Cancelled");
        _report.Expect("obligations still open", _h.OpenObligations, 0);
        _report.Expect("notices P received from A", _h.SentTo(P, WorkflowKinds.ChildFinished, A), 1);
        _report.Expect("A's cascade commits that landed", cascades.Count, 1);
        if (variant == "duplicate") _report.Expect("the dead owner's replay of the landed cascade batch landed", string.Join(",", _h.StaleReplays), "False");
        if (cascades.Count == 1)
        {
            var c = cascades[0];
            var expected = new List<InboxKey> { t.InboxKey };
            expected.AddRange(tickets.Where(x => x.State == Ticket.Pending).Select(x => Harness.Derive(A, x.Key, 1, x.Generation)));
            _report.ExpectSequence("the cascade commit's counted inputs", [.. c.Consumed.Select(k => k.Id)], [.. expected.Select(k => k.Id)]);
            if (variant == "takeover") _report.Expect("the cascade committed under the owner after the takeover", c.Epoch, _h.Owners[1].Epoch);
        }
        foreach (var (key, generation, state) in tickets)
        {
            var id = Harness.Derive(A, key, 1, generation);
            switch (state)
            {
                case Ticket.Pending: _report.Expect($"{key}@g{generation} (pending): row at the end", _h.Store.Messages.ContainsKey(id), false); break;
                case Ticket.DoneKept:
                    _report.Expect($"{key}@g{generation} (done and kept): row kept, Done", _h.Store.Messages.GetValueOrDefault(id)?.Done, true);
                    _report.Expect($"{key}@g{generation} (done and kept): kept state cleared", _h.KeptBy.ContainsKey(id), false);
                    break;
                case Ticket.Absent: _report.Expect($"{key}@g{generation} (absent): row at the end", _h.Store.Messages.ContainsKey(id), false); break;
            }
        }
        if (variant == "generations")
        {
            _report.Expect("A's newer same-origin generation j@g2 among the cascade's inputs", cascades.SelectMany(c => c.Consumed).Any(k => k == Harness.Derive(A, "j", 1, 2)), false);
            _report.Expect("B's ticket j@g1, at B's origin, still pending", _h.Pending(Harness.Derive(B, "j", 1, 1)), true);
        }
        if (variant == "off-registry")
            _report.Expect("retirement refusals of the faulty lists", _h.RetirementRefusals.Count, rules.AcceptOffRegistry ? 0 : 2);
    }

    /// <summary>
    /// The reviewer's store witness (logs/store-witness.out, event/Program.cs), as it was built: one partition, a
    /// parent command, the ticket placed in the store or not, and one cascade turn. The rule set decides only the
    /// turn's consumed inputs.
    /// </summary>
    private Report Witness()
    {
        var landed = 0;
        foreach (var pending in new[] { true, false })
        {
            var store = new InMemoryStore(1, () => 10);
            var lease = Real.Take(store, 0, "owner", TimeSpan.FromMinutes(1))!;
            var key = Real.Ticket("0/a", "j", 1);
            var command = new Message("parent-command", new InstanceRef("0/a"), Kinds.Terminate);
            Real.Add(store, command);
            if (pending) store.Messages[key] = new InMemoryStore.MessageRow(new Message(key.Id, command.Target, Kinds.Job), 2, 0);
            var send = new Message("child-command", new InstanceRef("0/g"), Kinds.Terminate);
            var present = store.Messages.TryGetValue(key, out var row);
            List<InboxKey> consumed = [command.InboxKey];
            if (rules.CountedCancel || present && !row!.Done) consumed.Add(key);
            var turn = new TurnCommit("0/a", null, null, consumed, [], [send], [], [], null);
            try
            {
                var result = Real.Commit(store, new CommitBatch(0, lease.Epoch, lease.Position, TimeSpan.FromMinutes(1), false, [turn], [], []));
                _report.Line($"cascade input-count probe: ticket pending={pending}, inputs [{string.Join(", ", consumed.Select(k => k.Id))}], landed={result.Landed}, child send stored={store.Messages.ContainsKey(send.InboxKey)}, parent command pending={store.Messages.ContainsKey(command.InboxKey)}");
                if (result.Landed && store.Messages.ContainsKey(send.InboxKey) && !store.Messages.ContainsKey(command.InboxKey)) landed++;
            }
            catch (StoreException ex)
            {
                _report.Line($"cascade input-count probe: ticket pending={pending}, inputs [{string.Join(", ", consumed.Select(k => k.Id))}], {ex.GetType().Name}: {ex.Message}, child send stored={store.Messages.ContainsKey(send.InboxKey)}, parent command pending={store.Messages.ContainsKey(command.InboxKey)}");
            }
            _report.Expect($"ticket pending={pending}: the cascade landed, stored the child send and consumed the parent command", landed == (pending ? 1 : 2), true);
        }
        return _report;
    }
}
