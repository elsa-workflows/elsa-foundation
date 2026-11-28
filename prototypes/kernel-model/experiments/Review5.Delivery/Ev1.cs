namespace Kernel.Review5;

/// <summary>
/// The rule sets of EV-1 (R5-M2). <c>Negative</c> removes the one repaired rule the variant tests: the sweep's meet-ticket
/// coverage (<c>b</c>, <c>b-paged</c>), its publish-ticket coverage (<c>replay</c>), the atomic match step
/// (<c>replay-atomic</c>), or the delivery-row hold (<c>replay-consumed</c>). Mutations of the repaired set:
/// <c>MatchAtNow</c> runs the catch-up through the real <c>KeptMatchesAsync</c> at the step's clock; <c>P0AtPageCommit</c>
/// recomputes the entitlement point at each later page's commit; <c>NoHorizon</c> takes P0 = RegisteredAt, ignoring the
/// sweep horizon H; <c>MeetWaitsWhileSuspended</c> holds a suspended instance's publish and meet tickets until resume;
/// <c>SplitNoRowHold</c> takes both the atomic match step and the delivery-row hold away; <c>MatchAtAcceptance</c> lets the
/// match step read the subscriptions committed at the event's acceptance in place of those committed at its own commit;
/// <c>NoPublishCoverage</c> lets the sweep delete an event whose publish ticket is outstanding.
/// </summary>
enum EvRules { Reviewed, Repaired, Negative, MatchAtNow, P0AtPageCommit, NoHorizon, MeetWaitsWhileSuspended, SplitNoRowHold, MatchAtAcceptance, NoPublishCoverage }

/// <summary>
/// EV-1: event eligibility across a recovery Open, on the real in-memory publisher path. The publisher's side is
/// <c>Events.PublishAsync</c> (Store.cs 287–295): <c>PublishAsync</c> (InMemoryStore.cs 263–267), <c>PublishedMatchesAsync</c>
/// (281–285), then the deliveries. The subscriber's side is the meet step (Worker.cs 1326–1329) over <c>KeptMatchesAsync</c>
/// (287–298); the sweep is <c>SweepAsync</c> (348–359). Turn commits, the restore's Open, ticket consumption and the
/// repaired match step go through the real fenced, count-checked <c>CommitAsync</c>.
/// The coordinator's repaired rule (18 November, second statement; the epsilon rule is withdrawn), proposed and applied by
/// the harness beside the real rows:
/// 1. the match step is the processing of a publish ticket whose one commit consumes the ticket and inserts the deliveries;
///    a non-keeping wait receives exactly the events whose match step reads its committed subscription row;
/// 2. a keeping wait's entitlement point P0 = max(RegisteredAt, H + 1 ms), with H the sweep's durable monotone horizon
///    (the greatest KeepUntil deleted), read at the restore and recorded on the meet ticket; the catch-up is owed the events
///    of its keys with KeepUntil >= P0, so no swept event, and no unswept one already expired at H;
/// 3. the sweep deletes E only when KeepUntil &lt; now, advances H to E.KeepUntil, and never while an outstanding publish
///    ticket names E or an outstanding meet ticket covers E's key with P0 &lt;= E.KeepUntil; it keeps an event's Done delivery
///    rows while the event row exists; meet and publish tickets pass suspension.
/// </summary>
sealed class Ev1(string variant, EvRules rules)
{
    public static readonly string[] Variants = ["a", "a-after", "b", "b-paged", "b-stepback", "b-suspended", "replay", "replay-atomic", "replay-consumed"];

    private const int PageLimit = 1;                               // P_page, scaled from 170 to 1 so two events make two pages
    private static readonly TimeSpan LeaseFor = Defaults.Lease;    // 10 s
    private static readonly InstanceRef Publisher = new("0/publisher");
    private Report _report = null!;

    private bool Repairedish => rules is not EvRules.Reviewed;
    /// <summary>The repaired match step: one commit consumes the ticket and inserts the deliveries. Off under reviewed and the replay negatives.</summary>
    private bool AtomicMatch => Repairedish && rules != EvRules.SplitNoRowHold && !(rules == EvRules.Negative && variant == "replay-atomic");
    /// <summary>The repaired sweep keeps an event's Done delivery rows while the event row exists (coordinator's decision, option (a)).</summary>
    private bool RowHold => Repairedish && rules != EvRules.SplitNoRowHold && !(rules == EvRules.Negative && variant == "replay-consumed");
    private bool MeetCoverage => Repairedish && !(rules == EvRules.Negative && variant is "b" or "b-paged");
    private bool PublishCoverage => Repairedish && rules != EvRules.NoPublishCoverage && !(rules == EvRules.Negative && variant == "replay");
    private bool CatchUpByP0 => Repairedish && rules != EvRules.MatchAtNow;

    /// <summary>One schedule's durable world: the real store, the owner's lease, and the proposed facts beside it.</summary>
    private sealed class World
    {
        public long Now;
        public InMemoryStore Store = null!;
        public Lease Lease = null!;
        public long Horizon = long.MinValue;           // H: the greatest KeepUntil the sweep deleted
        public readonly HashSet<string> Suspended = new(StringComparer.Ordinal);
        public readonly Dictionary<(string Key, string Id), HashSet<(string, string, string)>> AtAcceptance = [];
        public readonly List<(EventRow Event, Message Ticket)> Deferred = [];
        public readonly Dictionary<string, long> Revisions = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Inserted = new(StringComparer.Ordinal);   // deliveries ever inserted, per target
        public readonly Dictionary<(string Key, string Instance, string Wait), long> RegisteredAt = [];
    }

    /// <summary>The proposed meet ticket: the waits by reference, a cursor, and the entitlement point P0.</summary>
    private sealed record MeetPage(SubscriptionRow[] Waits, string? AfterEvent, long P0);

    public async Task<Report> RunAsync()
    {
        _report = new Report($"EV-1/{variant}", Program.Name(rules), variant switch
        {
            "a" => "a non-keeping wait receives an event accepted while it was absent exactly when the event's match step reads its committed row (the reviewed oracle: never)",
            "a-after" => "an event accepted after a non-keeping wait committed is delivered once, however long the publisher pauses (invariant 13)",
            "b" => "a keeping wait restored within an event's keep window receives it once, whenever its catch-up runs",
            "b-paged" => "a catch-up paged across sweeps delivers every event its entitlement point covers",
            "b-stepback" => "after the clock steps back below a swept event's KeepUntil, a new keeping wait is not entitled to the swept event",
            "b-suspended" => "a suspended instance's meet ticket completes its catch-up, so its hold on the event ends",
            "replay" => "a replayed publish ticket adds no second delivery, and its event is not swept while the ticket is outstanding",
            "replay-atomic" => "a match step whose commit did not land inserted nothing, so its replay delivers once after the sweep",
            _ => "a replayed publish ticket adds no second delivery after the waits consumed theirs and the sweep ran",
        });
        switch (variant)
        {
            case "a": await TraceA(); break;
            case "a-after": await TraceAAfter(); break;
            case "b": await TraceB(); break;
            case "b-paged": await TraceBPaged(); break;
            case "b-stepback": await StepBack(); break;
            case "b-suspended": await Suspended(); break;
            case "replay": await Replay(consumed: false); break;
            case "replay-atomic": await ReplayAtomic(); break;
            default: await Replay(consumed: true); break;
        }
        return _report;
    }

    // ---- the traces ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Trace A (R5-M2): E is accepted at 10 and persisted while S's wait is absent (Park); S's non-keeping wait is restored at
    /// 20. Three orders: the match step before the restore; the publisher paused before its match step until after the
    /// restore; the match step at once and then a long pause before the restore.
    /// </summary>
    private async Task TraceA()
    {
        var outcomes = new List<int>();
        foreach (var order in new[] { "match step before the restore", "publisher paused before its match step, restore, match step", "match step, a 30 s pause, restore" })
        {
            var w = await NewWorld();
            var s = new InstanceRef("0/subject");
            var wait = new SubscriptionRow("order:1", s.Id, "w-order", Creation: 1);
            w.Now = 10;
            var (e, ticket) = await PublishTurn(w, "order:1", "\"payload\"", keep: 0);
            await Persist(w, e);
            if (order.StartsWith("publisher paused", StringComparison.Ordinal))
            {
                w.Now = 20; await Restore(w, wait, keep: false); w.Now = 21; await MatchStep(w, e, ticket);
                // "Once": S consumes its delivery, the sweep runs, and a stale dispatch processes the same ticket again.
                await ConsumeDeliveries(w, s);
                w.Now = 30; await Sweep(w);
                try { await Persist(w, e); await MatchStep(w, e, ticket); }
                catch (StoreException refused) { _report.Line($"  second processing of the ticket at 30: the count-checked commit refuses it ({refused.Message})"); }
            }
            else { await MatchStep(w, e, ticket); w.Now = order.Contains("30 s") ? 30_020 : 20; await Restore(w, wait, keep: false); }
            outcomes.Add(Delivered(w, s));
            _report.Line($"{order}: deliveries to S {outcomes[^1]}");
        }
        var expected = Repairedish ? "0,1,0" : "0,0,0";
        _report.Line(Repairedish
            ? "oracle (repaired): the wait gets E exactly when E's match step reads its committed row, and once: 0, 1, 0"
            : "oracle (reviewed, observability.md 1187-1189): a wait that did not ask for kept events misses the events published while it was parked: 0, 0, 0");
        _report.Expect("deliveries in the three orders", string.Join(",", outcomes), expected);
    }

    /// <summary>Control: the wait committed at 10, E is accepted at 20; the match step runs at once or after 30 s. Delivered once.</summary>
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
            if (delayed) w.Now = 30_020;
            await MatchStep(w, e, ticket);
            outcomes.Add(Delivered(w, s));
            _report.Line($"match step {(delayed ? "30 s after the publish" : "at once")}: deliveries to S {outcomes[^1]}");
        }
        _report.Expect("deliveries, match step at once and after 30 s", $"{outcomes[0]},{outcomes[1]}", "1,1");
    }

    /// <summary>
    /// Trace B (R5-M2): the whole publish completes during Park; S's keeping wait is restored at 99 for an event kept through
    /// 100; its catch-up runs at 99, or at 101 after a sweep.
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
        _report.Expect("deliveries and the events' fate, catch-up at 99 / at 101", string.Join(" / ", outcomes), "1 swept / 1 swept");
    }

    /// <summary>
    /// Two events kept through 100 and a page of one. Control: both pages at 99. The other schedule: a sweep at 101, page 1,
    /// another sweep, page 2. Page 2's ticket is written at 101; P0 recorded on the first ticket decides its coverage.
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
    /// Clock step-back: E1 (KeepUntil 100) is published and swept at 101 with no ticket covering it (H = 100); the database
    /// clock steps back to 98; W0 (non-keeping) commits and E2 (KeepUntil 99) is published; a keeping wait registers at 99
    /// with P0 = max(99, H + 1) = 101. The observable contract: every event the entitlement promises is delivered. E2's
    /// exclusion from the new wait's catch-up is the events page's stated consequence, reported as such.
    /// </summary>
    private async Task StepBack()
    {
        var w = await NewWorld();
        var s = new InstanceRef("0/subject");
        var wait = new SubscriptionRow("order:4", s.Id, "w-order", Creation: 1);
        w.Now = 1;
        var (e1, ticket) = await PublishTurn(w, "order:4", "\"payload-1\"", keep: 99);
        await Persist(w, e1); await MatchStep(w, e1, ticket);
        w.Now = 101; await Sweep(w);
        _report.Line($"sweep at 101: E1 (KeepUntil {e1.KeepUntil}) is {(w.Store.EventRows.ContainsKey((e1.Key, e1.Id)) ? "kept" : "deleted")}; H = {(w.Horizon == long.MinValue ? "none" : w.Horizon.ToString())}");
        w.Now = 98;
        _report.Line("the database clock steps back to 98");
        // W0, a non-keeping wait of another instance, commits before E2's match step: the publisher side delivers E2 to it.
        var s0 = new InstanceRef("0/subject-0");
        await Restore(w, new SubscriptionRow("order:4", s0.Id, "w-order", Creation: 1), keep: false);
        var (e2, ticket2) = await PublishTurn(w, "order:4", "\"payload-2\"", keep: 1);
        await Persist(w, e2); await MatchStep(w, e2, ticket2);
        _report.Line($"E2 is published at 98 with KeepUntil {e2.KeepUntil}, before the wait registers; it is live");
        w.Now = 99;
        var meet = await Restore(w, wait, keep: true);
        var p0 = meet!.Read<MeetPage>().P0;
        await Drain(w, meet);
        var delivered = w.Store.Messages.Values.Where(m => m.Message.Kind == Kinds.Event && m.Message.Target.Id == s.Id).Select(m => m.Message.Payload).ToList();
        _report.Line($"keeping wait registered at 99{(Repairedish ? $", P0 = {p0}" : "")}: the catch-up delivers [{string.Join(", ", delivered)}]");
        // The observable contract: every event the entitlement promises reaches S. Repaired: P0 = max(99, H + 1) = 101 promises
        // neither E1 nor E2. no-horizon (P0 99) and reviewed (KeepUntil >= registration 99) promise the swept E1.
        var from = Repairedish ? p0 : w.RegisteredAt[(wait.Key, wait.Instance, wait.Wait)];
        var promised = new[] { e1, e2 }.Where(e => e.KeepUntil >= from).ToList();
        var missing = promised.Where(e => !delivered.Contains(e.Payload)).Select(e => e.Payload).ToList();
        _report.Line($"the entitlement (KeepUntil >= {from}) promises [{string.Join(", ", promised.Select(e => e.Payload))}]");
        if (Repairedish && !promised.Contains(e2))
            _report.Line("E2, live with KeepUntil 99 <= H, is excluded from the new wait's catch-up as expired: the events page's stated consequence of a step-back");
        _report.Expect("promised events not delivered to S", string.Join(", ", missing), "");
        _report.Expect("deliveries of E2 to W0, registered before E2's match step (the publisher side)", Delivered(w, s0), 1);
        _report.Expect("E1 re-created", w.Store.EventRows.ContainsKey((e1.Key, e1.Id)), false);
    }

    /// <summary>
    /// A suspended instance: S is suspended when its keeping wait is restored at 99 for E kept through 100; S resumes at
    /// 20,000. The meet ticket passes suspension (built: Worker.cs 640–645 and 1315–1339 take meet tickets with no
    /// withholding check), so the catch-up completes at 99 and the ticket's hold on E ends.
    /// </summary>
    private async Task Suspended()
    {
        var w = await NewWorld();
        var s = new InstanceRef("0/subject");
        var wait = new SubscriptionRow("order:2", s.Id, "w-order", Creation: 1);
        w.Now = 1;
        var (e, ticket) = await PublishTurn(w, "order:2", "\"payload\"", keep: 99);
        await Persist(w, e);
        w.Suspended.Add(Publisher.Id);
        _report.Line("the publisher is suspended after its publish turn committed, with its publish ticket outstanding");
        await MatchStep(w, e, ticket);
        w.Suspended.Add(s.Id);
        w.Now = 99;
        var meet = await Restore(w, wait, keep: true);
        _report.Line("S is suspended; its keeping wait is restored at 99 (P0 99) for E, kept through 100");
        meet = await Drain(w, meet);
        w.Now = 102; await Sweep(w);
        var swept = !w.Store.EventRows.ContainsKey((e.Key, e.Id));
        _report.Line($"at 102, with S and the publisher still suspended, E is {(swept ? "swept" : "kept")}");
        w.Now = 20_000; w.Suspended.Clear();
        _report.Line("S and the publisher resume at 20,000");
        foreach (var (de, dt) in w.Deferred.ToList()) { w.Deferred.Clear(); await MatchStep(w, de, dt); }
        await Drain(w, meet);
        _report.Expect("deliveries to S", Delivered(w, s), 1);
        _report.Expect("E swept at 102 while both are suspended (the tickets' holds end with their steps)", swept, true);
    }

    /// <summary>
    /// A replayed publish ticket. W1 (non-keeping, registered at 5) and W2 (keeping, restored at 10,020) are owed E (accepted
    /// at 10, kept through 20,000). Owner 1 dies in E's match step: the repaired step's commit does not land; the reviewed
    /// split step has inserted W1's delivery and not consumed the ticket. Owner 2 takes over after the lease, restores W2,
    /// runs its catch-up, pauses past E's keep window, sweeps, and replays the ticket. With <paramref name="consumed"/>, S1
    /// and S2 consume their deliveries before the sweep (Done rows until E's KeepUntil, Worker.cs 1452–1453).
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
        await Persist(w, e);
        await MatchStep(w, e, ticket, dies: true);
        if (consumed) await ConsumeDeliveries(w, s1);
        w.Now = 10 + (long)LeaseFor.TotalMilliseconds + 10;
        var replayed = await Takeover(w);
        var meet = await Restore(w, w2, keep: true);
        w.Now++;
        await Drain(w, meet!);
        if (consumed) await ConsumeDeliveries(w, s2);
        w.Now = e.KeepUntil + 1;
        await Sweep(w);
        var kept = w.Store.EventRows.ContainsKey((e.Key, e.Id));
        _report.Line($"owner 2 pauses until {w.Now}, past E's keep window, with the ticket outstanding; after the sweep E is {(kept ? "kept" : "deleted")}");
        w.Now++;
        var again = replayed.Read<EventRow>();
        await Persist(w, again);
        await MatchStep(w, again, replayed);
        _report.Expect("deliveries to S1 (non-keeping, registered before E)", Delivered(w, s1), 1);
        _report.Expect("deliveries to S2 (keeping, registered within the window)", Delivered(w, s2), 1);
    }

    /// <summary>
    /// The match step's atomicity: W1 alone, E kept through 110. Owner 1 dies in the match step; S1 consumes whatever it was
    /// given; owner 2 takes over after the lease, sweeps, and replays the ticket.
    /// </summary>
    private async Task ReplayAtomic()
    {
        var w = await NewWorld();
        var s1 = new InstanceRef("0/subject-1");
        var w1 = new SubscriptionRow("order:5", s1.Id, "w-order", Creation: 1);
        w.Now = 5; await Restore(w, w1, keep: false);
        w.Now = 10;
        var (e, ticket) = await PublishTurn(w, "order:5", "\"payload\"", keep: 100);
        await Persist(w, e);
        await MatchStep(w, e, ticket, dies: true);
        await ConsumeDeliveries(w, s1);
        w.Now = 10 + (long)LeaseFor.TotalMilliseconds + 10;
        var replayed = await Takeover(w);
        await Sweep(w);
        w.Now++;
        await Persist(w, replayed.Read<EventRow>());
        await MatchStep(w, replayed.Read<EventRow>(), replayed);
        _report.Expect("deliveries to S1", Delivered(w, s1), 1);
    }

    // ---- the steps ---------------------------------------------------------------------------------------------------

    private static async Task<World> NewWorld()
    {
        var w = new World { Now = 1 };
        w.Store = new InMemoryStore(1, () => w.Now);
        w.Lease = await Real.TakeAsync(w.Store, 0, "owner-1", LeaseFor) ?? throw new InvalidOperationException("harness schedule: no lease");
        return w;
    }

    private async Task<Message> Takeover(World w)
    {
        w.Lease = await Real.TakeAsync(w.Store, 0, "owner-2", LeaseFor) ?? throw new InvalidOperationException("harness schedule: the takeover failed");
        var polled = await Real.PollAsync(w.Store, 0, w.Lease.Epoch, 500);
        var replayed = polled.Messages.Single(m => m.Message.Kind == Kinds.Publish).Message;
        _report.Line($"owner 2 takes over at {w.Now} (epoch {w.Lease.Epoch}); its poll returns the publish ticket, still undone");
        return replayed;
    }

    private static async Task<Committed> Commit(World w, TurnCommit turn)
    {
        var committed = await Real.CommitAsync(w.Store, new CommitBatch(0, w.Lease.Epoch, w.Lease.Position, LeaseFor, false, [turn], [], []));
        if (!committed.Landed) throw new InvalidOperationException("harness schedule: a commit did not land");
        return committed;
    }

    private static long Revision(World w, string instance) => w.Revisions[instance] = w.Revisions.GetValueOrDefault(instance) + 1;

    /// <summary>Publishes the events (time, keep) during Park, then restores S's keeping wait at 99 with its meet ticket.</summary>
    private async Task<(World W, InstanceRef S, List<EventRow> Events, Message Meet)> KeptSetup((long At, long Keep)[] publishes, bool suspended = false)
    {
        var w = await NewWorld();
        var s = new InstanceRef("0/subject");
        var wait = new SubscriptionRow("order:2", s.Id, "w-order", Creation: 1);
        var events = new List<EventRow>();
        foreach (var (at, keep) in publishes)
        {
            w.Now = at;
            var (e, ticket) = await PublishTurn(w, "order:2", $"\"payload-{events.Count + 1}\"", keep);
            await Persist(w, e); await MatchStep(w, e, ticket);
            events.Add(e);
        }
        if (suspended) w.Suspended.Add(s.Id);
        w.Now = 99;
        var meet = await Restore(w, wait, keep: true);
        _report.Line($"published during Park: {string.Join(", ", events.Select(e => $"{e.Payload} KeepUntil {e.KeepUntil}"))}; keeping wait restored at 99{(suspended ? " while S is suspended" : "")} (P0 {meet!.Read<MeetPage>().P0})");
        return (w, s, events, meet);
    }

    /// <summary>After the catch-up: a sweep at 102, once nothing is owed, deletes what has expired.</summary>
    private async Task<string> Settle(World w, InstanceRef s, List<EventRow> events, string schedule)
    {
        var delivered = Delivered(w, s);
        w.Now = 102; await Sweep(w);
        var fate = events.All(e => !w.Store.EventRows.ContainsKey((e.Key, e.Id))) ? "swept" : "kept";
        _report.Line($"{schedule}: deliveries to S {delivered}; at 102 the events are {fate}");
        return $"{delivered} {fate}";
    }

    /// <summary>The publisher's turn: DurableTurn.Publish (Turns.cs 429–434) sends the self-addressed publish ticket; KeepUntil is Now + keep.</summary>
    private static async Task<(EventRow Event, Message Ticket)> PublishTurn(World w, string key, string? payload, long keep)
    {
        var id = $"{Real.Origin(Publisher.Id, 1)}#{Revision(w, Publisher.Id)}.0000";
        var e = new EventRow(key, id, payload, w.Now + keep);
        var ticket = ModeledTicket.Issue(new Message(id, Publisher, Kinds.Publish, Json.Write(e), Publisher.Id,
            TargetCreation: 1, SenderCreation: 1).AsIssued());
        await Commit(w, new TurnCommit(Publisher.Id, null, null, [], [], [ticket], [], [], null));
        w.AtAcceptance[(e.Key, e.Id)] = w.Store.Subscriptions.Select(r => (r.Key, r.Instance, r.Wait)).ToHashSet();
        return (e, ticket);
    }

    private static async Task Persist(World w, EventRow e) => await Real.PublishAsync(w.Store, e);

    /// <summary>
    /// The match step. Reviewed (built, Store.cs 291–295 then Worker.cs 1338–1339): the lookup, each delivery added on its
    /// own, and the ticket consumed by a later commit. Repaired: the lookup, then one fenced commit that consumes the ticket
    /// and inserts the deliveries not yet present (the in-memory CommitAsync refuses an existing key, so the step leaves out
    /// present rows, as a provider's insert-if-absent would). <paramref name="dies"/>: the owner dies before the step's
    /// commit (repaired: nothing lands) or before the ticket's consumption (reviewed: the deliveries are in).
    /// </summary>
    private async Task MatchStep(World w, EventRow e, Message ticket, bool dies = false)
    {
        if (WaitsWhileSuspended(ticket.Target.Id, w)) { w.Deferred.Add((e, ticket)); _report.Line($"  match step at {w.Now}: the publish ticket waits while the publisher is suspended (mutation)"); return; }
        var matches = await Real.PublishedMatchesAsync(w.Store, e);
        if (rules == EvRules.MatchAtAcceptance && w.AtAcceptance.TryGetValue((e.Key, e.Id), out var then))
            matches = matches.Where(m => then.Contains((m.Wait.Key, m.Wait.Instance, m.Wait.Wait))).ToList();
        if (AtomicMatch)
        {
            var deliveries = new List<Message>();
            foreach (var match in matches)
            {
                Real.Count("EventRoute.Delivery");
                var message = match.Delivery(1).Message;
                if (!w.Store.Messages.ContainsKey(message.InboxKey) && deliveries.TrueForAll(d => d.InboxKey != message.InboxKey)) deliveries.Add(message);
            }
            if (dies) { _report.Line($"  match step at {w.Now}: the owner dies before its commit; nothing lands"); return; }
            await Commit(w, new TurnCommit(Publisher.Id, null, null, [ticket.InboxKey], [], deliveries, [], [], null));
            foreach (var d in deliveries) w.Inserted[d.Target.Id] = w.Inserted.GetValueOrDefault(d.Target.Id) + 1;
            _report.Line($"  match step at {w.Now}: one commit consumes the ticket and inserts {deliveries.Count} deliveries");
            return;
        }
        var added = 0;
        foreach (var match in matches)
            if ((await Real.AddEventAsync(w.Store, match)).Added) { added++; w.Inserted[match.Wait.Instance] = w.Inserted.GetValueOrDefault(match.Wait.Instance) + 1; }
        if (dies) { _report.Line($"  match step at {w.Now}: {added} deliveries added; the owner dies before the commit that consumes the ticket"); return; }
        await Commit(w, new TurnCommit(Publisher.Id, null, null, [ticket.InboxKey], [], [], [], [], null));
        _report.Line($"  match step at {w.Now}: {added} deliveries added, then the ticket consumed");
    }

    /// <summary>
    /// The restore's Open writes the wait (and, for a keeping wait, one meet ticket) in its commit. Proposed: P0 =
    /// max(RegisteredAt, H + 1 ms), read at the restore and recorded on the meet ticket; RegisteredAt is the commit's clock.
    /// </summary>
    private async Task<Message?> Restore(World w, SubscriptionRow wait, bool keep)
    {
        var p0 = rules == EvRules.NoHorizon ? w.Now : Math.Max(w.Now, w.Horizon == long.MinValue ? long.MinValue : w.Horizon + 1);
        Message? meet = keep
            ? ModeledTicket.Issue(new Message($"{Real.Origin(wait.Instance, wait.Creation)}#{Revision(w, wait.Instance)}.0000", new InstanceRef(wait.Instance), Kinds.Meet,
                Json.Write(new MeetPage([wait], null, p0)), wait.Instance, TargetCreation: wait.Creation, SenderCreation: wait.Creation).AsIssued())
            : null;
        var committed = await Commit(w, new TurnCommit(wait.Instance, null, null, [], [], meet is null ? [] : [meet], [wait], [], null));
        if (committed.Now != w.Now) throw new InvalidOperationException("harness schedule: the commit clock differs from the harness clock");
        w.RegisteredAt[(wait.Key, wait.Instance, wait.Wait)] = committed.Now;
        return meet;
    }

    /// <summary>Runs meet steps until none is left, or until one waits (only under <c>MeetWaitsWhileSuspended</c>); returns the waiting ticket.</summary>
    private async Task<Message?> Drain(World w, Message? meet)
    {
        while (meet is not null)
        {
            var page = meet.Read<MeetPage>();
            if (WaitsWhileSuspended(page.Waits[0].Instance, w)) { _report.Line($"  meet step at {w.Now}: the ticket waits while {page.Waits[0].Instance} is suspended (mutation)"); return meet; }
            meet = await MeetStep(w, meet);
        }
        return null;
    }

    /// <summary>Meet and publish tickets pass suspension (built: Worker.cs 640–645, 1315–1339); the mutation makes them wait.</summary>
    private bool WaitsWhileSuspended(string instance, World w) => rules == EvRules.MeetWaitsWhileSuspended && w.Suspended.Contains(instance);

    /// <summary>
    /// One meet step (Worker.cs 1326–1329, paged as proposed): read at most P_page matches of the ticket's waits after its
    /// cursor, add each delivery, and commit the next ticket with the advanced cursor in the commit that consumes this one.
    /// Reviewed: the real KeptMatchesAsync at the step's clock. Repaired: the same rows with KeepUntil >= the ticket's P0, which
    /// the next ticket carries.
    /// </summary>
    private async Task<Message?> MeetStep(World w, Message meet)
    {
        var page = meet.Read<MeetPage>();
        IReadOnlyList<EventRoute> matches;
        if (CatchUpByP0)
        {
            var selected = page.Waits.Select(s => (s.Key, s.Instance, s.Wait)).ToHashSet();
            matches = (from e in w.Store.EventRows.Values
                       join s in w.Store.Subscriptions on e.Key equals s.Key
                       where selected.Contains((s.Key, s.Instance, s.Wait)) && e.KeepUntil >= page.P0
                       select new EventRoute(e, s)).ToList();
        }
        else matches = await Real.KeptMatchesAsync(w.Store, page.Waits);
        var ordered = matches.OrderBy(m => m.Wait.Key, StringComparer.Ordinal).ThenBy(m => m.Wait.Wait, StringComparer.Ordinal)
            .ThenBy(m => m.Event.Id, StringComparer.Ordinal)
            .Where(m => page.AfterEvent is null || string.CompareOrdinal(m.Event.Id, page.AfterEvent) > 0).Take(PageLimit).ToList();
        foreach (var match in ordered)
            if ((await Real.AddEventAsync(w.Store, match)).Added) w.Inserted[match.Wait.Instance] = w.Inserted.GetValueOrDefault(match.Wait.Instance) + 1;
        var instance = new InstanceRef(page.Waits[0].Instance);
        var nextP0 = rules == EvRules.P0AtPageCommit ? Math.Max(w.Now, w.Horizon == long.MinValue ? long.MinValue : w.Horizon + 1) : page.P0;
        Message? next = ordered.Count == PageLimit
            ? ModeledTicket.Issue(new Message($"{Real.Origin(instance.Id, 1)}#{Revision(w, instance.Id)}.0000", instance, Kinds.Meet,
                Json.Write(page with { AfterEvent = ordered[^1].Event.Id, P0 = nextP0 }), instance.Id, TargetCreation: 1, SenderCreation: 1).AsIssued())
            : null;
        await Commit(w, new TurnCommit(instance.Id, null, null, [meet.InboxKey], [], next is null ? [] : [next], [], [], null));
        _report.Line($"  meet step at {w.Now}: {(CatchUpByP0 ? $"KeepUntil >= P0 {page.P0}" : "real KeptMatchesAsync (KeepUntil >= now)")} -> page [{string.Join(", ", ordered.Select(m => m.Event.Payload))}]{(next is null ? "" : $", next ticket sent (P0 {nextP0})")}");
        return next;
    }

    /// <summary>
    /// The sweep. Reviewed: the real SweepAsync. Repaired: the same two deletions (InMemoryStore.cs 353–356), except that an
    /// event is kept while an outstanding publish ticket names it or an outstanding meet ticket covers its key with
    /// P0 &lt;= KeepUntil; each deleted event advances H; and a Done delivery row stays while its event row exists.
    /// </summary>
    private async Task Sweep(World w)
    {
        if (!Repairedish) { await Real.SweepAsync(w.Store); _report.Line($"  sweep at {w.Now}: the real SweepAsync"); return; }
        var outstanding = w.Store.Messages.Values.Where(m => !m.Done).Select(m => m.Message).ToList();
        var named = outstanding.Where(m => m.Kind == Kinds.Publish).Select(m => m.Read<EventRow>()).Select(e => (e.Key, e.Id)).ToHashSet();
        var covered = outstanding.Where(m => m.Kind == Kinds.Meet).Select(m => m.Read<MeetPage>())
            .SelectMany(p => p.Waits.Select(s => (s.Key, p.P0))).ToList();
        var events = w.Store.EventRows.Values.Where(e => e.KeepUntil < w.Now
            && !(PublishCoverage && named.Contains((e.Key, e.Id)))
            && !(MeetCoverage && covered.Any(c => c.Key == e.Key && c.P0 <= e.KeepUntil))).ToList();
        var surviving = w.Store.EventRows.Keys.Except(events.Select(e => (e.Key, e.Id))).ToHashSet();
        // The delivery-row hold: a Done event delivery stays while its event row exists (an indexed Events.Match read).
        var messages = w.Store.Messages.Values.Where(m => m.Done && m.Message.KeepUntil < w.Now
            && !(RowHold && m.Message.Kind == Kinds.Event && m.Message.EventMatch is { } match && EventMatch.Decode(match) is var em && surviving.Contains((em.Key, em.Event))))
            .Select(m => m.Message.InboxKey).ToList();
        messages.ForEach(id => w.Store.Messages.Remove(id));
        foreach (var e in events) { w.Store.EventRows.Remove((e.Key, e.Id)); w.Horizon = Math.Max(w.Horizon, e.KeepUntil); }
        _report.Line($"  sweep at {w.Now}: {events.Count} event(s) and {messages.Count} done row(s) deleted; H {(w.Horizon == long.MinValue ? "none" : w.Horizon.ToString())}");
    }

    /// <summary>The waiting instance's turn consumes its deliveries; one with a KeepUntil stays a Done row (Worker.cs 1452–1453).</summary>
    private static async Task ConsumeDeliveries(World w, InstanceRef target)
    {
        var keys = w.Store.Messages.Values.Where(m => m.Message.Kind == Kinds.Event && m.Message.Target.Id == target.Id && !m.Done).Select(m => m.Message.InboxKey).ToList();
        if (keys.Count > 0) await Commit(w, new TurnCommit(target.Id, null, null, [], keys, [], [], [], null));
    }

    /// <summary>Deliveries ever inserted for the target (the sweep may since have deleted consumed ones).</summary>
    private static int Delivered(World w, InstanceRef target) => w.Inserted.GetValueOrDefault(target.Id);
}
