namespace Kernel.Review5;

/// <summary>
/// The rule sets of EV-1 (R5-M2). <c>Negative</c> removes the one rule the variant tests: the stamp check for the
/// non-keeping traces (<c>a</c>, <c>a-band</c>, <c>a-after</c>), the sweep's owed-from mark for the others. Mutations of the
/// repaired set: <c>MatchAtNow</c> keeps the mark but runs the keeping catch-up through the real <c>KeptMatchesAsync</c>
/// at the step's clock; <c>FreshEventId</c> lets a replayed publish ticket derive a new event id; <c>OwedFromAtCommit</c>
/// stamps each later meet page's ticket with its own commit time instead of carrying its predecessor's.
/// </summary>
enum EvRules { Reviewed, Repaired, Negative, MatchAtNow, FreshEventId, OwedFromAtCommit }

/// <summary>
/// EV-1: event eligibility across a recovery Open, on the real in-memory publisher path. The publisher's side is
/// <c>Events.PublishAsync</c> (Store.cs 287–295) split at the reviewer's pause point: <c>PublishAsync</c> (InMemoryStore.cs
/// 263–267), then <c>PublishedMatchesAsync</c> (281–285), then <c>AddEventAsync</c> (235–246). The subscriber's side is the
/// meet step (Worker.cs 1326–1329) over <c>KeptMatchesAsync</c> (287–298), and the sweep is <c>SweepAsync</c> (348–359).
/// Turn commits, the restore's Open and ticket consumption go through the real fenced <c>CommitAsync</c>; its
/// <c>Committed.Now</c> is the commit clock (InMemoryStore.cs 94, 143), the one every stamp is taken from.
/// Proposed (R5-M2, the coordinator's rule of 18 November) and kept beside the real rows by the harness:
/// <c>AcceptedAt</c> on the event, <c>RegisteredAt</c> and the keep flag on the subscription, <c>OwedFrom</c> on each
/// meet and publish ticket, the stamp check with epsilon = the configured lease (10 s, <c>Defaults.Lease</c>), the
/// paged meet ticket, and the sweep's owed-from mark.
/// </summary>
sealed class Ev1(string variant, EvRules rules)
{
    public static readonly string[] Variants = ["a", "a-band", "a-after", "b", "b-paged", "b-band", "replay", "replay-consumed"];

    private const int PageLimit = 1;                               // P_page, scaled from 170 to 1 so two events make two pages
    private static readonly TimeSpan LeaseFor = Defaults.Lease;    // the configured lease
    private static readonly long Epsilon = (long)Defaults.Lease.TotalMilliseconds;  // coordinator: epsilon = the configured lease
    private static readonly InstanceRef Publisher = new("0/publisher");
    private Report _report = null!;

    private bool NonKeepingTrace => variant is "a" or "a-band" or "a-after";
    private bool StampCheck => rules is not (EvRules.Reviewed or EvRules.Negative) || rules == EvRules.Negative && !NonKeepingTrace;
    private bool OwedFromMark => rules is not (EvRules.Reviewed or EvRules.Negative) || rules == EvRules.Negative && NonKeepingTrace;
    private bool MatchByStamps => StampCheck && rules != EvRules.MatchAtNow;

    /// <summary>One schedule's durable world: the real store, the owner's lease, and the proposed stamps beside it.</summary>
    private sealed class World
    {
        public long Now;
        public InMemoryStore Store = null!;
        public Lease Lease = null!;
        public readonly Dictionary<string, long> Revisions = new(StringComparer.Ordinal);
        public readonly Dictionary<(string Key, string Id), long> AcceptedAt = [];
        public readonly Dictionary<(string Key, string Instance, string Wait), (long At, bool Keep)> Registered = [];
        public readonly Dictionary<InboxKey, long> OwedFrom = [];
    }

    /// <summary>The proposed paged meet ticket: the waits by reference and a cursor (observability.md, kept-event catch-up).</summary>
    private sealed record MeetPage(SubscriptionRow[] Waits, string? AfterEvent);

    public async Task<Report> RunAsync()
    {
        _report = new Report($"EV-1/{variant}", Program.Name(rules), variant switch
        {
            "a" => "a non-keeping wait restored more than epsilon after an event's acceptance never receives it, whenever the publisher's match runs",
            "a-band" => "inside the epsilon band either outcome is permitted for a non-keeping wait, and the event is never delivered twice",
            "a-after" => "an event accepted after a non-keeping wait committed is delivered once, whenever the publisher's match runs (invariant 13)",
            "b" => "a keeping wait registered within an event's keep window receives it once, whenever its catch-up runs",
            "b-paged" => "a catch-up paged across sweeps delivers every event the keeping wait was owed when it registered",
            "b-band" => "a keeping wait owed an event by its acceptance (within epsilon before registration) receives it once, whenever its catch-up runs",
            "replay" => "a replayed publish ticket adds no second delivery, and its event is not swept while the ticket is outstanding",
            _ => "a replayed publish ticket adds no second delivery after its waits consumed theirs and the sweep ran",
        });
        _report.Line($"epsilon = the configured lease = {Epsilon} ms (Defaults.Lease); every stamp is the commit clock's Committed.Now");
        switch (variant)
        {
            case "a": await TraceA(gap: Epsilon + 1); break;
            case "a-band": await TraceA(gap: 10); break;
            case "a-after": await TraceAAfter(); break;
            case "b": await TraceB(); break;
            case "b-paged": await TraceBPaged(); break;
            case "b-band": await TraceBBand(); break;
            default: await Replay(consumed: variant == "replay-consumed"); break;
        }
        return _report;
    }

    // ---- the traces ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Trace A (R5-M2): E is persisted during Park while S's wait is absent; the publisher pauses before its subscription
    /// lookup; S's non-keeping wait is restored <paramref name="gap"/> ms after E's acceptance; the publisher resumes. Both
    /// orders of lookup and restore. With the gap above epsilon the repaired rule never delivers; inside the band (the
    /// reviewer's constant-clock witness) it permits both outcomes.
    /// </summary>
    private async Task TraceA(long gap)
    {
        var outcomes = new List<int>();
        foreach (var delayed in new[] { false, true })
        {
            var w = await NewWorld();
            var s = new InstanceRef("0/subject");
            var wait = new SubscriptionRow("order:1", s.Id, "w-order", Creation: 1);
            w.Now = 10;
            var (e, ticket) = await PublishTurn(w, "order:1", "\"payload\"", keep: 30_000);
            await Persist(w, e);
            if (!delayed) { await Match(w, e); w.Now = 10 + gap; await Restore(w, wait, keep: false); }
            else { w.Now = 10 + gap; await Restore(w, wait, keep: false); w.Now++; await Match(w, e); }
            await ConsumeTicket(w, ticket);
            outcomes.Add(Deliveries(w, s));
            _report.Line($"order {(delayed ? "restore before the publisher's lookup" : "lookup before the restore")}: AcceptedAt {w.AcceptedAt[(e.Key, e.Id)]}, RegisteredAt {w.Registered[(wait.Key, wait.Instance, wait.Wait)].At}, deliveries to S {outcomes[^1]}");
        }
        if (gap > Epsilon)
            _report.Expect($"deliveries, lookup first / restore first (restore {gap} ms after acceptance, beyond epsilon)", $"{outcomes[0]},{outcomes[1]}", "0,0");
        else
        {
            // Inside the band the rule permits a delivery and permits none; it never permits two.
            _report.Check(outcomes.All(o => o is 0 or 1), $"deliveries, lookup first / restore first (restore {gap} ms after acceptance, inside the band): {outcomes[0]},{outcomes[1]}, each 0 or 1 (permitted)");
            _report.Line($"inside the band the outcome follows the publisher's delay: {(outcomes[0] == outcomes[1] ? "the same" : "different")} in the two orders");
        }
    }

    /// <summary>Control: the wait committed before E was accepted; the publisher pauses either way. Delivered once (invariant 13).</summary>
    private async Task TraceAAfter()
    {
        var outcomes = new List<int>();
        foreach (var delayed in new[] { false, true })
        {
            var w = await NewWorld();
            var s = new InstanceRef("0/subject");
            var wait = new SubscriptionRow("order:1", s.Id, "w-order", Creation: 1);
            w.Now = 10; await Restore(w, wait, keep: false);
            w.Now = 20;
            var (e, ticket) = await PublishTurn(w, "order:1", "\"payload\"", keep: 0);
            await Persist(w, e);
            if (delayed) w.Now = 20 + Epsilon + 1;
            await Match(w, e);
            await ConsumeTicket(w, ticket);
            outcomes.Add(Deliveries(w, s));
            _report.Line($"order {(delayed ? $"lookup {Epsilon + 1} ms after the persist" : "lookup at once")}: RegisteredAt 10, AcceptedAt 20, deliveries to S {outcomes[^1]}");
        }
        _report.Expect("deliveries, lookup at once and delayed", $"{outcomes[0]},{outcomes[1]}", "1,1");
    }

    /// <summary>
    /// Trace B (R5-M2): the whole publish completes during Park; S's keeping wait is restored at 99 for an event kept
    /// through 100; its catch-up runs at 99, or at 101 after a sweep.
    /// </summary>
    private async Task TraceB()
    {
        var outcomes = new List<string>();
        foreach (var late in new[] { false, true })
        {
            var (w, s, events, meet) = await KeptSetup([(1, 99)]);
            if (late) { w.Now = 101; await Sweep(w); }
            await Drain(w, meet);
            outcomes.Add(await Settle(w, s, events, late ? "catch-up at 101 after a sweep" : "catch-up at 99"));
        }
        _report.Expect("deliveries and the events' fate after the catch-up, catch-up at 99 / at 101", string.Join(" / ", outcomes), "1 swept / 1 swept");
    }

    /// <summary>
    /// Two events kept through 100 and a page of one. Control: both pages at 99. The other schedule: a sweep at 101, page 1,
    /// another sweep, page 2. The second page's ticket was written at 101, so its owed-from stamp decides whether the sweep
    /// before page 2 may delete E2.
    /// </summary>
    private async Task TraceBPaged()
    {
        var outcomes = new List<string>();
        foreach (var late in new[] { false, true })
        {
            var (w, s, events, meet) = await KeptSetup([(1, 99), (2, 98)]);
            if (late) { w.Now = 101; await Sweep(w); }
            meet = await MeetStep(w, meet);
            if (late) await Sweep(w);
            await Drain(w, meet);
            outcomes.Add(await Settle(w, s, events, late ? "sweep, page 1, sweep, page 2, at 101" : "both pages at 99"));
        }
        _report.Expect("deliveries and the events' fate, both pages at 99 / pages at 101 between sweeps", string.Join(" / ", outcomes), "2 swept / 2 swept");
    }

    /// <summary>
    /// An event published with no keep window (KeepUntil = its acceptance, 95) and a keeping wait restored at 99, inside
    /// epsilon: rule (a) entitles the wait. The catch-up runs at 99, or at 101 after a sweep.
    /// </summary>
    private async Task TraceBBand()
    {
        var outcomes = new List<string>();
        foreach (var late in new[] { false, true })
        {
            var (w, s, events, meet) = await KeptSetup([(95, 0)]);
            if (late) { w.Now = 101; await Sweep(w); }
            await Drain(w, meet);
            outcomes.Add(await Settle(w, s, events, late ? "catch-up at 101 after a sweep" : "catch-up at 99"));
        }
        var expected = "1 swept / 1 swept";
        var actual = string.Join(" / ", outcomes);
        _report.Expect("deliveries and the events' fate, catch-up at 99 / at 101", actual, expected);
        if (rules == EvRules.Repaired && actual != expected)
            _report.Stop("the stated rules disagree here: rule (a) owes E to the keeping wait (AcceptedAt 95 >= RegisteredAt 99 - epsilon), " +
                "but the sweep mark deletes an event when KeepUntil has passed and lies below the least owed-from stamp (95 < 99), so a sweep " +
                "between the restore and its catch-up removes an owed event and the outcome depends on when the catch-up runs. " +
                "Recommendation in the README: hold an event while AcceptedAt >= mark - epsilon as well (delete only when KeepUntil < mark and AcceptedAt < mark - epsilon).");
    }

    /// <summary>
    /// A replayed publish ticket. W1 (non-keeping, registered at 5) and W2 (keeping, restored at 10,020) are both owed E
    /// (accepted at 10, kept through 20,000). Owner 1 matches W1 and dies before its commit consumes the ticket. Owner 2
    /// takes over after the lease, restores W2 and runs its catch-up, pauses past E's keep window, sweeps, and replays the
    /// ticket. With <paramref name="consumed"/>, S1 and S2 consume their deliveries before the sweep (Done markers until
    /// E's KeepUntil, Worker.cs 1452–1453).
    /// </summary>
    private async Task Replay(bool consumed)
    {
        var w = await NewWorld();
        var s1 = new InstanceRef("0/subject-1");
        var s2 = new InstanceRef("0/subject-2");
        var w1 = new SubscriptionRow("order:3", s1.Id, "w-order", Creation: 1);
        var w2 = new SubscriptionRow("order:3", s2.Id, "w-order", Creation: 1);
        w.Now = 5; await Restore(w, w1, keep: false);
        w.Now = 10;
        var (e, ticket) = await PublishTurn(w, "order:3", "\"payload\"", keep: 19_990);
        await Persist(w, e); await Match(w, e);
        if (consumed) await ConsumeDeliveries(w, s1);
        _report.Line($"owner 1 at 10: persisted E (KeepUntil {e.KeepUntil}), matched: deliveries S1 {Deliveries(w, s1)}; it dies before the commit that consumes the publish ticket (OwedFrom {w.OwedFrom[ticket.InboxKey]})");
        w.Now = 10 + Epsilon + 10;
        w.Lease = await Real.TakeAsync(w.Store, 0, "owner-2", LeaseFor) ?? throw new InvalidOperationException("harness schedule: the takeover failed");
        var polled = await Real.PollAsync(w.Store, 0, w.Lease.Epoch, 500);
        var replayed = polled.Messages.Single(m => m.Message.Kind == Kinds.Publish).Message;
        _report.Line($"owner 2 takes at {w.Now} (epoch {w.Lease.Epoch}); its poll returns the publish ticket {replayed.Id}, still undone");
        var meet = await Restore(w, w2, keep: true);
        w.Now++;
        await Drain(w, meet!);
        if (consumed) await ConsumeDeliveries(w, s2);
        w.Now = e.KeepUntil + 1;
        await Sweep(w);
        var kept = w.Store.EventRows.ContainsKey((e.Key, e.Id));
        _report.Line($"owner 2 pauses until {w.Now}, past E's keep window, with the ticket outstanding; after the sweep E is {(kept ? "kept" : "deleted")}; delivery rows left: S1 {DeliveryRows(w, s1)}, S2 {DeliveryRows(w, s2)}");
        w.Now++;
        var again = replayed.Read<EventRow>();
        if (rules == EvRules.FreshEventId)
        {
            // Mutation: the replay derives the event id afresh, as a re-encoded ticket at a new revision would.
            again = again with { Id = $"{Real.Origin(Publisher.Id, 1)}#{Revision(w, Publisher.Id)}.0000" };
            w.AcceptedAt[(again.Key, again.Id)] = w.AcceptedAt[(e.Key, e.Id)];
        }
        await Persist(w, again);
        var added = await Match(w, again);
        await ConsumeTicket(w, replayed);
        _report.Line($"owner 2 at {w.Now} replays the ticket: {added} new deliveries");
        _report.Expect("E kept by the sweep while its publish ticket was outstanding", kept, true);
        _report.Expect("deliveries the replay added (S1 and S2 had one each)", added, 0);
        if (consumed && rules == EvRules.Repaired && added > 0)
            _report.Stop("the owed-from mark keeps the event, and the replay's lookup still owes it to both waits, but the sweep deleted their " +
                "consumed delivery rows (Done markers whose KeepUntil is the event's, InMemoryStore.cs 353), so AddEventAsync's duplicate check " +
                "(235–241) finds nothing and inserts the deliveries again. The stated mark holds events only. Recommendation in the README: " +
                "the mark also holds the Done delivery rows of an event it holds, so the replay meets its duplicates.");
    }

    // ---- the steps ---------------------------------------------------------------------------------------------------

    private static async Task<World> NewWorld()
    {
        var w = new World { Now = 1 };
        w.Store = new InMemoryStore(1, () => w.Now);
        w.Lease = await Real.TakeAsync(w.Store, 0, "owner-1", LeaseFor) ?? throw new InvalidOperationException("harness schedule: no lease");
        return w;
    }

    private static async Task<Committed> Commit(World w, TurnCommit turn)
    {
        var committed = await Real.CommitAsync(w.Store, new CommitBatch(0, w.Lease.Epoch, w.Lease.Position, LeaseFor, false, [turn], [], []));
        if (!committed.Landed) throw new InvalidOperationException("harness schedule: a commit did not land");
        return committed;
    }

    private static long Revision(World w, string instance) => w.Revisions[instance] = w.Revisions.GetValueOrDefault(instance) + 1;

    /// <summary>Publishes the events (time, keep) during Park, then restores S's keeping wait at 99 with its meet ticket.</summary>
    private async Task<(World W, InstanceRef S, List<EventRow> Events, Message Meet)> KeptSetup((long At, long Keep)[] publishes)
    {
        var w = await NewWorld();
        var s = new InstanceRef("0/subject");
        var wait = new SubscriptionRow("order:2", s.Id, "w-order", Creation: 1);
        var events = new List<EventRow>();
        foreach (var (at, keep) in publishes)
        {
            w.Now = at;
            var (e, ticket) = await PublishTurn(w, "order:2", $"\"payload-{events.Count + 1}\"", keep);
            await Persist(w, e); await Match(w, e); await ConsumeTicket(w, ticket);
            events.Add(e);
        }
        w.Now = 99;
        var meet = await Restore(w, wait, keep: true);
        _report.Line($"published during Park: {string.Join(", ", events.Select(e => $"{e.Payload} AcceptedAt {w.AcceptedAt[(e.Key, e.Id)]} KeepUntil {e.KeepUntil}"))}; keeping wait restored at 99");
        return (w, s, events, meet!);
    }

    /// <summary>After the catch-up: a sweep at 102, once nothing is owed, deletes what has expired.</summary>
    private async Task<string> Settle(World w, InstanceRef s, List<EventRow> events, string schedule)
    {
        var delivered = Deliveries(w, s);
        w.Now = 102; await Sweep(w);
        var fate = events.All(e => !w.Store.EventRows.ContainsKey((e.Key, e.Id))) ? "swept" : "kept";
        _report.Line($"{schedule}: deliveries to S {delivered}; at 102 the events are {fate}");
        return $"{delivered} {fate}";
    }

    /// <summary>
    /// The publisher's turn: DurableTurn.Publish (Turns.cs 429–434) sends the self-addressed publish ticket, whose event id is
    /// the turn's next issued id and whose KeepUntil is Now + keep. Proposed: AcceptedAt and the ticket's OwedFrom are the commit's clock.
    /// </summary>
    private async Task<(EventRow Event, Message Ticket)> PublishTurn(World w, string key, string? payload, long keep)
    {
        var id = $"{Real.Origin(Publisher.Id, 1)}#{Revision(w, Publisher.Id)}.0000";
        var e = new EventRow(key, id, payload, w.Now + keep);
        var ticket = new Message(id, Publisher, Kinds.Publish, Json.Write(e), Publisher.Id, TargetCreation: 1, SenderCreation: 1).AsIssued();
        var committed = await Commit(w, new TurnCommit(Publisher.Id, null, null, [], [], [ticket], [], [], null));
        w.AcceptedAt[(e.Key, e.Id)] = committed.Now;
        w.OwedFrom[ticket.InboxKey] = committed.Now;
        return (e, ticket);
    }

    private static async Task Persist(World w, EventRow e) => await Real.PublishAsync(w.Store, e);

    /// <summary>The publisher's lookup and insert (Store.cs 291–295), with the proposed stamp check between them.</summary>
    private async Task<int> Match(World w, EventRow e)
    {
        var added = 0;
        foreach (var match in await Real.PublishedMatchesAsync(w.Store, e))
        {
            if (StampCheck && !Owed(w, match.Event, match.Wait)) continue;
            if ((await Real.AddEventAsync(w.Store, match)).Added) added++;
        }
        return added;
    }

    /// <summary>
    /// Proposed (R5-M2, coordinator's rule): any wait is owed E when (a) E.AcceptedAt >= S.RegisteredAt - epsilon; a
    /// keeping wait also when (b) E.KeepUntil >= S.RegisteredAt.
    /// </summary>
    private static bool Owed(World w, EventRow e, SubscriptionRow s)
    {
        var (registeredAt, keep) = w.Registered[(s.Key, s.Instance, s.Wait)];
        return w.AcceptedAt[(e.Key, e.Id)] >= registeredAt - Epsilon || keep && e.KeepUntil >= registeredAt;
    }

    /// <summary>
    /// The restore's Open writes the wait (and, for a keeping wait, one meet ticket) in its commit. Proposed: the commit's
    /// clock is the wait's RegisteredAt and the meet ticket's OwedFrom, and the row records its keep flag.
    /// </summary>
    private static async Task<Message?> Restore(World w, SubscriptionRow wait, bool keep)
    {
        Message? meet = keep
            ? new Message($"{Real.Origin(wait.Instance, wait.Creation)}#{Revision(w, wait.Instance)}.0000", new InstanceRef(wait.Instance), Kinds.Meet,
                Json.Write(new MeetPage([wait], null)), wait.Instance, TargetCreation: wait.Creation, SenderCreation: wait.Creation).AsIssued()
            : null;
        var committed = await Commit(w, new TurnCommit(wait.Instance, null, null, [], [], meet is null ? [] : [meet], [wait], [], null));
        w.Registered[(wait.Key, wait.Instance, wait.Wait)] = (committed.Now, keep);
        if (meet is not null) w.OwedFrom[meet.InboxKey] = committed.Now;
        return meet;
    }

    private async Task Drain(World w, Message? meet)
    {
        while (meet is not null) meet = await MeetStep(w, meet);
    }

    /// <summary>
    /// One meet step (Worker.cs 1326–1329, paged as proposed): read at most P_page matches of the ticket's waits after its
    /// cursor, add each delivery, and commit the next ticket with the advanced cursor in the commit that consumes this one.
    /// Reviewed: the real KeptMatchesAsync at the step's clock. Repaired: the same rows, owed by rule (a) or (b). The next
    /// ticket carries its predecessor's OwedFrom (the harness's reading of "until its cursor passes"; the
    /// <c>OwedFromAtCommit</c> mutation stamps it at its own commit).
    /// </summary>
    private async Task<Message?> MeetStep(World w, Message meet)
    {
        var page = meet.Read<MeetPage>();
        IReadOnlyList<EventRoute> matches;
        if (MatchByStamps)
        {
            var selected = page.Waits.Select(s => (s.Key, s.Instance, s.Wait)).ToHashSet();
            matches = (from e in w.Store.EventRows.Values
                       join s in w.Store.Subscriptions on e.Key equals s.Key
                       where selected.Contains((s.Key, s.Instance, s.Wait)) && Owed(w, e, s)
                       select new EventRoute(e, s)).ToList();
        }
        else matches = await Real.KeptMatchesAsync(w.Store, page.Waits);
        var ordered = matches.OrderBy(m => m.Wait.Key, StringComparer.Ordinal).ThenBy(m => m.Wait.Wait, StringComparer.Ordinal)
            .ThenBy(m => m.Event.Id, StringComparer.Ordinal)
            .Where(m => page.AfterEvent is null || string.CompareOrdinal(m.Event.Id, page.AfterEvent) > 0).Take(PageLimit).ToList();
        foreach (var match in ordered) await Real.AddEventAsync(w.Store, match);
        var instance = new InstanceRef(page.Waits[0].Instance);
        Message? next = ordered.Count == PageLimit
            ? new Message($"{Real.Origin(instance.Id, 1)}#{Revision(w, instance.Id)}.0000", instance, Kinds.Meet,
                Json.Write(page with { AfterEvent = ordered[^1].Event.Id }), instance.Id, TargetCreation: 1, SenderCreation: 1).AsIssued()
            : null;
        var committed = await Commit(w, new TurnCommit(instance.Id, null, null, [meet.InboxKey], [], next is null ? [] : [next], [], [], null));
        if (next is not null) w.OwedFrom[next.InboxKey] = rules == EvRules.OwedFromAtCommit ? committed.Now : w.OwedFrom[meet.InboxKey];
        _report.Line($"  meet step at {w.Now}: {(MatchByStamps ? "owed by (a) or (b)" : "real KeptMatchesAsync (KeepUntil >= now)")} -> page [{string.Join(", ", ordered.Select(m => m.Event.Payload))}]{(next is null ? "" : $", next ticket sent (OwedFrom {w.OwedFrom[next.InboxKey]})")}");
        return next;
    }

    /// <summary>
    /// The sweep. Reviewed: the real SweepAsync. Repaired: the same two deletions (InMemoryStore.cs 353–356), except that an
    /// event is deleted only when its KeepUntil has passed and lies below the mark: the least OwedFrom of the outstanding
    /// meet and publish tickets.
    /// </summary>
    private async Task Sweep(World w)
    {
        if (!OwedFromMark) { await Real.SweepAsync(w.Store); _report.Line($"  sweep at {w.Now}: the real SweepAsync"); return; }
        var mark = w.Store.Messages.Values.Where(m => m.Message.Kind is Kinds.Meet or Kinds.Publish && !m.Done)
            .Select(m => w.OwedFrom[m.Message.InboxKey]).DefaultIfEmpty(long.MaxValue).Min();
        var messages = w.Store.Messages.Values.Where(m => m.Done && m.Message.KeepUntil < w.Now).Select(m => m.Message.InboxKey).ToList();
        var events = w.Store.EventRows.Values.Where(e => e.KeepUntil < w.Now && e.KeepUntil < mark).Select(e => (e.Key, e.Id)).ToList();
        messages.ForEach(id => w.Store.Messages.Remove(id));
        events.ForEach(key => w.Store.EventRows.Remove(key));
        _report.Line($"  sweep at {w.Now}: owed-from mark {(mark == long.MaxValue ? "none" : mark.ToString())}, {events.Count} event(s) and {messages.Count} done row(s) deleted");
    }

    private static async Task ConsumeTicket(World w, Message ticket) =>
        await Commit(w, new TurnCommit(ticket.Target.Id, null, null, [ticket.InboxKey], [], [], [], [], null));

    /// <summary>The waiting instance's turn consumes its deliveries; one with a KeepUntil stays a Done marker (Worker.cs 1452–1453).</summary>
    private static async Task ConsumeDeliveries(World w, InstanceRef target)
    {
        var keys = w.Store.Messages.Values.Where(m => m.Message.Kind == Kinds.Event && m.Message.Target.Id == target.Id && !m.Done).Select(m => m.Message.InboxKey).ToList();
        await Commit(w, new TurnCommit(target.Id, null, null, [], keys, [], [], [], null));
    }

    private static int Deliveries(World w, InstanceRef target) => DeliveryRows(w, target);

    private static int DeliveryRows(World w, InstanceRef target) =>
        w.Store.Messages.Values.Count(m => m.Message.Kind == Kinds.Event && m.Message.Target.Id == target.Id);
}
