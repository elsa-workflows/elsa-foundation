using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Kernel.Review6;

/// <summary>
/// PI-1 (R6-B2): a continuing wait W on key K receives two outside admissions of event id E, with different payloads,
/// across a sweep. Every step runs on the real <see cref="InMemoryStore"/>: the event insert (<c>PublishAsync</c>,
/// InMemoryStore.cs 263–267, a no-op when (K, E) exists), the match read (<c>PublishedMatchesAsync</c>, 281–285), the
/// catch-up read (<c>KeptMatchesAsync</c>, 287–298), the delivery insert that refuses an existing full key
/// (<c>AddEventAsync</c>, 235–246), the consumer's commit (<c>CommitAsync</c>) and the built sweep (<c>SweepAsync</c>,
/// 348–359). Held by the harness (proposed): the delivery's full key (the R4-M1 digests, and under the repaired rules
/// the incarnation inside them), the incarnation itself, and the sweep in separate event and message batches with the
/// round-5 predicate (a Done delivery goes only when no event row has its <c>Match</c>).
/// </summary>
sealed class Pi1(string variant, Rules rules)
{
    public static readonly string[] Variants = ["gap", "event-first", "slow-consumer", "duplicate"];

    private const string K = "order-7", E = "evt-1", I = "1/i", W = "w";
    private const long Window = 1_000;
    private readonly Report _r = new($"PI-1/{variant}", rules.Name,
        "W, continuously subscribed, receives each accepted publication of (K, E) exactly once, with that publication's payload; a duplicate within the window is answered with the existing publication and adds nothing");
    private Kernel _k = null!;
    private readonly Dictionary<EventRow, byte[]> _incarnations = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _received = [];
    private long _serial;
    private SubscriptionRow _wait = null!;

    private sealed record Receipt(string Status, string Identity);

    private bool Incarnations => !rules.BuiltKey && (rules.IncarnationInId || rules.IncarnationInMatch);

    public Report Run()
    {
        _k = new Kernel(_r, [new StubModel()]);
        _k.Store.Instances[I] = new InstanceRow(I, "stub", "i", InstanceStatus.Running, "{}", 1, null, Creation: 1);
        _wait = new SubscriptionRow(K, I, W, Creation: 1);
        Real.Subscribe(_k.Store, _wait);
        _r.Line($"setup: I {I} (Running, creation 1) waits on {K} as {W}, registered once and never unsubscribed; window {Window} ms; " +
            (rules.BuiltKey ? "key: the built delivery key through the built Events.PublishAsync (existing prototype behaviour)"
                : $"key: delivery id and Match as R4-M1's digests{(rules.IncarnationInId ? ", the id with the incarnation" : "")}{(rules.IncarnationInMatch ? ", the Match with the incarnation" : "")} (proposed)"));

        var first = Publish("""{"n":1}""", "first admission");
        if (variant != "slow-consumer") { _k.Now = 1_100; Consume("W's turn"); }
        else _r.Line("W's consumer is slow: the first delivery stays pending");

        _k.Now = 2_001;
        _r.Line($"t={_k.Now}: the first publication's window has passed");
        if (variant == "event-first") EventSweep();
        else BuiltSweep();

        _k.Now = 2_050;
        var second = Publish("""{"n":2}""", "second admission, after the sweep");
        _r.Expect("second-new", "the second admission's receipt", second.Status, "New");
        _r.Line($"  receipts: first names {first.Identity}, second names {second.Identity}");
        if (variant == "event-first")
        {
            MessageSweep();
            var oldDone = _k.Store.Messages.Values.Count(m => m.Done && m.Message.Kind == Kinds.Event && m.Message.KeepUntil < _k.Now && m.Message.Payload == """{"n":1}""");
            _r.Expect("old-done-retired", "the first publication's Done deliveries left after the message-sweep batch (its own event row is gone)", oldDone, 0);
        }
        if (variant == "duplicate")
        {
            _k.Now = 2_100;
            var dup = Publish("""{"n":3}""", "a retry within the second publication's window, another payload");
            _r.Check("duplicate", dup.Status == "Duplicate" && dup.Identity == second.Identity,
                $"the retry is answered as a duplicate of the second publication: {dup.Status}, names {dup.Identity} (expected Duplicate, {second.Identity})");
        }
        CatchUp();
        _k.Now = 2_200;
        Consume("W's turn");
        _r.ExpectSequence("payloads", "payloads W received, in order", _received, ["""{"n":1}""", """{"n":2}"""]);
        return _r;
    }

    // ---- the publication ----------------------------------------------------------------------------------------

    private byte[] Mint()
    {
        // Store-generated, opaque, unique, never reused, 16 bytes (proposed). The harness's serial stands in for the
        // provider's generated id, so the output is deterministic.
        var inc = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(inc.AsSpan(8), ++_serial);
        return inc;
    }

    private string Identity(EventRow stored) => Incarnations
        ? $"({stored.Key}, {stored.Id}, incarnation {Convert.ToHexString(_incarnations[stored]).ToLowerInvariant()})"
        : $"({stored.Key}, {stored.Id})";

    /// <summary>
    /// The outside publish. Built key: the built <c>Events.PublishAsync</c> (Store.cs 287–296). Otherwise the proposed
    /// entry point: the real insert, then insert-or-read by reference (the stored row is the one written now, or an
    /// earlier one), a new incarnation only for a new row, and the match step on the stored row. The match step runs
    /// inline, as Store.cs 290–294 runs it; the proposed publish ticket moves it to the owner and leaves the key as is.
    /// </summary>
    private Receipt Publish(string payload, string what)
    {
        var row = new EventRow(K, E, payload, _k.Now + Window);
        if (rules.BuiltKey)
        {
            var matched = Real.EventsPublish(_k.Store, row, Kernel.Partitions);
            var builtStored = _k.Store.EventRows[(K, E)];
            var fresh = ReferenceEquals(builtStored, row);
            _r.Line($"t={_k.Now}: {what}: publish ({K}, {E}) payload {payload}, KeepUntil {row.KeepUntil}: event row {(fresh ? "inserted" : "exists, kept")}; " +
                string.Join("; ", matched.Select(m => $"delivery {m.Route.Delivery(Kernel.Partitions).Message.Id} to {m.Route.Wait.Wait}: {(m.Accepted.Added ? "added" : "refused, the key exists")}")));
            return new(fresh ? "New" : "Duplicate", Identity(builtStored));
        }
        Real.Publish(_k.Store, row);
        var stored = _k.Store.EventRows[(K, E)];
        string status;
        if (ReferenceEquals(stored, row)) { _incarnations[stored] = Mint(); status = "New"; }
        else if (rules.FreshIncarnationOnDuplicate) { _incarnations[stored] = Mint(); status = "New"; }
        else status = "Duplicate";
        _r.Line($"t={_k.Now}: {what}: publish ({K}, {E}) payload {payload}, KeepUntil {row.KeepUntil}: event row {(ReferenceEquals(stored, row) ? "inserted" : $"exists (payload {stored.Payload}), kept")}; receipt {status}, names {Identity(stored)}");
        if (status == "New") Match(stored);
        else _r.Line("  a duplicate writes nothing and runs no match step");
        return new(status, Identity(stored));
    }

    private void Match(EventRow stored)
    {
        foreach (var route in Real.PublishedMatches(_k.Store, stored))
            Add(route, "  match step");
    }

    private void CatchUp()
    {
        _r.Line($"t={_k.Now}: a later catch-up page of W (InMemoryStore.KeptMatchesAsync), as a keeping wait's replay reads it");
        foreach (var route in Real.KeptMatches(_k.Store, [_wait]))
            Add(route, "  catch-up");
    }

    private void Add(EventRoute route, string what)
    {
        var delivery = Key(route);
        var accepted = Real.AddEvent(_k.Store, delivery);
        _r.Line($"{what}: event payload {route.Event.Payload} to {route.Wait.Wait}: delivery {Short(delivery.Message.Id)} Match {Short(delivery.Message.EventMatch!)}: {(accepted.Added ? "added" : "refused, the full key exists")}");
    }

    private static string Short(string s) => s.Length > 24 ? s[..24] + "…" : s;

    // ---- the key (proposed, held by the harness) ------------------------------------------------------------------

    private EventDelivery Key(EventRoute route)
    {
        var built = Real.RouteDelivery(route, Kernel.Partitions);
        if (rules.BuiltKey) return built;
        var inc = _incarnations[route.Event];
        var id = "ev:" + Digest('D', rules.IncarnationInId ? inc : null, route.Event.Id, route.Wait.Instance, route.Wait.Creation, route.Wait.Wait);
        return Real.Keyed(built, id, MatchOf(route.Event));
    }

    private string MatchOf(EventRow e) => Digest('M', rules.IncarnationInMatch ? _incarnations[e] : null, e.Key, e.Id);

    /// <summary>
    /// The canonical encoding (observability, "The event match is a fixed digest"): a domain byte and a version byte,
    /// then each part, a string as a 4-byte big-endian count of UTF-16 code units and the units big-endian, a creation
    /// as 8 bytes (<c>DurableKeyJson.CodeUnits</c>, which keeps every unpaired surrogate). Version 2 (proposed, R6-B2) puts the 16-byte incarnation after the event id. The key stands for
    /// Q(t, key): the prototype has no tenant.
    /// </summary>
    private static string Digest(char domain, byte[]? incarnation, string first, params object[] rest)
    {
        var bytes = new List<byte> { (byte)domain, (byte)(incarnation is null ? 1 : 2) };
        void Text(string s)
        {
            var count = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(count, s.Length);
            bytes.AddRange(count);
            bytes.AddRange(DurableKeyJson.CodeUnits(s));
        }
        if (domain == 'M') { Text(first); Text((string)rest[0]); if (incarnation is not null) bytes.AddRange(incarnation); }
        else
        {
            Text(first);
            if (incarnation is not null) bytes.AddRange(incarnation);
            Text((string)rest[0]);
            var creation = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(creation, (long)rest[1]);
            bytes.AddRange(creation);
            Text((string)rest[2]);
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }

    // ---- the consumer and the sweeps ----------------------------------------------------------------------------

    /// <summary>W's turn: consume every pending delivery to I, in seq order, through the real commit (kept rows become Done).</summary>
    private void Consume(string what)
    {
        var due = _k.Store.Messages.Values.Where(m => m.Message.Target.Id == I && m.Message.Kind == Kinds.Event && !m.Done).OrderBy(m => m.Seq).ToList();
        if (due.Count == 0) { _r.Line($"t={_k.Now}: {what}: nothing pending"); return; }
        var commit = new TurnCommit(I, null, null, [], due.Select(m => m.Message.InboxKey).ToList(), [], [], [], null);
        var (landed, error) = _k.Commit(I, [commit]);
        _r.Line($"t={_k.Now}: {what}: consumes [{string.Join(", ", due.Select(m => m.Message.Payload))}]: {(landed ? "landed, the rows are kept Done" : "not landed: " + error)}");
        if (landed) _received.AddRange(due.Select(m => m.Message.Payload!));
    }

    /// <summary>The built sweep (InMemoryStore.cs 348–359): Done rows and event rows past their window, in one call.</summary>
    private void BuiltSweep()
    {
        var removed = Real.Sweep(_k.Store);
        _r.Line($"  the built sweep (SweepAsync) removes {removed} rows; left: {Left()}");
    }

    /// <summary>The event-sweep batch (proposed batching; the built predicate of InMemoryStore.cs 354).</summary>
    private void EventSweep()
    {
        var gone = _k.Store.EventRows.Values.Where(e => e.KeepUntil < _k.Now).Select(e => (e.Key, e.Id)).ToList();
        gone.ForEach(key => _k.Store.EventRows.Remove(key));
        _r.Line($"  the event-sweep batch removes {gone.Count} event rows; the message-sweep batch runs later; left: {Left()}");
    }

    /// <summary>The message-sweep batch: the built predicate (InMemoryStore.cs 353) and round 5's NOT EXISTS over the event rows' Match (proposed).</summary>
    private void MessageSweep()
    {
        var gone = _k.Store.Messages.Values.Where(m => m.Done && m.Message.KeepUntil < _k.Now && !(m.Message.Kind == Kinds.Event && Protected(m.Message)))
            .Select(m => m.Message.InboxKey).ToList();
        gone.ForEach(key => _k.Store.Messages.Remove(key));
        _r.Line($"t={_k.Now}: the message-sweep batch removes {gone.Count} Done rows; left: {Left()}");
    }

    private bool Protected(Message delivery) => rules.BuiltKey
        ? EventMatch.Decode(delivery.EventMatch!) is var m && _k.Store.EventRows.ContainsKey((m.Key, m.Event))
        : _k.Store.EventRows.Values.Any(e => MatchOf(e) == delivery.EventMatch);

    private string Left() =>
        $"event rows [{string.Join(", ", _k.Store.EventRows.Values.Select(e => $"{e.Id} {e.Payload}"))}], deliveries [{string.Join(", ", _k.Store.Messages.Values.Where(m => m.Message.Kind == Kinds.Event).Select(m => $"{m.Message.Payload} {(m.Done ? "Done" : "pending")}"))}]";
}
