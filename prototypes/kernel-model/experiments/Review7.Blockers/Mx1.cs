namespace Kernel.Review7;

/// <summary>
/// MX-1 (R7-M1): a workflow turn's publication and an outside publication on one key, where the outside caller uses
/// the turn's event id. Every step runs on real prototype code: the turn's publish is the real
/// <c>DurableTurn.Publish</c> (Turns.cs 429–434), sealed by the real <c>DurableTurn.Seal</c> and committed through the
/// real <c>InMemoryStore.CommitAsync</c>; the outside publish and the owner's step of the turn's publish ticket are
/// the real <c>Events.PublishAsync</c> (Store.cs 288–296: <c>InMemoryStore.PublishAsync</c> 263–267, a no-op when
/// (key, id) exists, then <c>PublishedMatchesAsync</c> 281–285 and <c>AddEventAsync</c> 235–246), as Worker.cs
/// 1323–1325 runs the step. Held by the harness (proposed): the reserved first code unit <c>~</c> on a turn's event
/// id, and the outside publish's refusal of a caller id that starts with it.
/// </summary>
sealed class Mx1(string variant, Rules rules)
{
    public static readonly string[] Variants = ["ticket-first", "outside-first", "owner-change", "distinct"];

    /// <summary>The reserved first code unit of a turn's event id (proposed, R7-M1).</summary>
    public const char Reserved = '~';
    private const string K = "acme_topic", P = "0/acme_p", W = "0/acme_w", Wait = "W", Distinct = "source-42";
    private const string FromTurn = "from-turn", FromOutside = "from-outside";
    private static readonly TimeSpan Keep = TimeSpan.FromHours(1);

    private readonly Report _r = new($"MX-1/{variant}", rules.Name,
        "a turn's committed publication is delivered to W with its own payload; an outside publication either has its own event row and is delivered, or is refused and writes nothing");
    private Kernel _k = null!;
    private readonly List<string> _received = [];

    private sealed record Receipt(string Status, int EventsBefore, int EventsAfter, int MessagesBefore, int MessagesAfter);

    public Report Run()
    {
        _k = new Kernel(_r, [new StubModel()]);
        _k.Store.Instances[P] = new InstanceRow(P, "stub", "p", InstanceStatus.Running, "{}", 1, null, Creation: 1);
        _k.Store.Instances[W] = new InstanceRow(W, "stub", "w", InstanceStatus.Running, "{}", 1, null, Creation: 1);
        Real.Subscribe(_k.Store, new SubscriptionRow(K, W, Wait, Creation: 1));
        _r.Line($"setup: P {P} and W {W} (Running, creation 1, one tenant); W waits on {K} as {Wait}; the delivery key is the built one (Store.cs 247–251); " +
            $"{(rules.Prefix ? "a turn's event id carries the reserved first code unit '~'" : "a turn's event id is the built one")}, " +
            $"{(rules.Refusal ? "the outside publish refuses an id that starts with '~'" : "the outside publish refuses no id")} (proposed)");

        Receipt outside;
        string turnId;
        InboxKey ticket;
        switch (variant)
        {
            case "outside-first":
            {
                var predicted = Predict();
                outside = Outside(predicted, "the outside caller publishes first, under the id the turn will return");
                (turnId, ticket) = TurnPublish();
                _r.Line($"  the turn's id equals the id the caller used: {turnId == predicted}");
                Step(ticket, "the owner's first step of the ticket");
                break;
            }
            case "owner-change":
                (turnId, ticket) = TurnPublish();
                _k.TakeOver(new InstanceRef(P).Partition);
                outside = Outside(turnId, "after the owner change, before the ticket's first step, the outside caller publishes under the turn's returned id");
                Step(ticket, "the new owner's first step of the ticket");
                break;
            case "ticket-first":
                (turnId, ticket) = TurnPublish();
                outside = Outside(turnId, "before the ticket's first step, the outside caller publishes under the turn's returned id");
                Step(ticket, "the owner's first step of the ticket");
                break;
            case "distinct":
                (turnId, ticket) = TurnPublish();
                outside = Outside(Distinct, "before the ticket's first step, the outside caller publishes under its own id");
                Step(ticket, "the owner's first step of the ticket");
                break;
            default: throw new ArgumentException(variant);
        }
        Consume();
        _r.Line($"stored event rows: {_k.Store.EventRows.Count} [{string.Join(", ", _k.Store.EventRows.Values.Select(e => $"({e.Key}, {e.Id}) {e.Payload}"))}]");

        if (variant == "distinct")
        {
            var stored = _k.Store.EventRows.GetValueOrDefault((K, Distinct));
            _r.Check("outside-accepted", outside.Status == "New" && stored?.Payload == FromOutside,
                $"the outside publish under {Distinct} is accepted as New and its event row carries {FromOutside}: {outside.Status}, {stored?.Payload ?? "no row"}");
        }
        else
            _r.Check("outside-refused", outside.Status == "InvalidEventId" && outside.EventsAfter == outside.EventsBefore && outside.MessagesAfter == outside.MessagesBefore,
                $"the outside publish under the turn's id is refused with InvalidEventId and writes nothing: {outside.Status}, event rows {outside.EventsBefore} -> {outside.EventsAfter}, " +
                $"message rows {outside.MessagesBefore} -> {outside.MessagesAfter}");
        var turnEvent = _k.Store.EventRows.GetValueOrDefault((K, turnId));
        _r.Check("turn-event", turnEvent?.Payload == FromTurn, $"the event row under the turn's id {turnId} carries the turn's payload: {turnEvent?.Payload ?? "no row"} (expected {FromTurn})");
        _r.ExpectSequence("payloads", "payloads W received, in order", _received, variant == "distinct" ? [FromOutside, FromTurn] : [FromTurn]);
        return _r;
    }

    // ---- the two producers ------------------------------------------------------------------------------------------

    /// <summary>The id a turn of P would return now, from a real <c>DurableTurn.Publish</c> on P's current row in a turn that is never sealed or committed.</summary>
    private string Predict()
    {
        var row = _k.Store.Instances[P];
        var built = Real.Publish(Real.DurableTurn(_k.OwnerOf(P).Worker, row, []), K, null, Keep);
        var id = rules.Prefix ? Reserved + built : built;
        _r.Line($"the caller predicts the id of P's next publish: a real DurableTurn.Publish on P's row at revision {row.Revision}, never committed, returns {built}; the id the turn will return: {id}");
        return id;
    }

    /// <summary>P's turn publishes, and its commit lands with the publish ticket (the real DurableTurn, Seal and CommitAsync).</summary>
    private (string Id, InboxKey Ticket) TurnPublish()
    {
        var trigger = new Message("go", new InstanceRef(P), "app.go", Sender: "operator", TargetCreation: 1);
        var (_, seq) = _k.AddOutside(trigger);
        var row = _k.Store.Instances[P];
        var turn = Real.DurableTurn(_k.OwnerOf(P).Worker, row, [Real.Delivery(trigger, seq)]);
        var built = Real.Publish(turn, K, FromTurn, Keep);
        var commit = Kernel.ToTurnCommit(Real.Seal(turn, row));
        var id = built;
        if (rules.Prefix)
        {
            // Proposed (R7-M1): the turn's event id is stored and returned with the reserved first code unit. The harness
            // writes it into the sealed ticket's EventRow; the ticket's own id and key are unchanged.
            id = Reserved + built;
            commit = commit with
            {
                Sent = commit.Sent.Select(m => m.Kind == Kinds.Publish
                    ? ModeledTicket.Issue(m with { Payload = Json.Write(m.Read<EventRow>() with { Id = id }) })
                    : m).ToList()
            };
        }
        var ticket = commit.Sent.Single(m => m.Kind == Kinds.Publish);
        var (landed, error) = _k.Commit(P, [commit]);
        if (!landed) throw new InvalidOperationException("P's publishing turn did not land: " + error);
        var queued = ticket.Read<EventRow>();
        _r.Line($"t={_k.Now}: P's turn publishes on {K} with payload {FromTurn}: DurableTurn.Publish returns {built}{(rules.Prefix ? $", stored and returned as {id} (proposed)" : "")}; " +
            $"the commit lands with the publish ticket {ticket.Id} (event ({queued.Key}, {queued.Id}), payload {queued.Payload}) on {_k.OwnerOf(P).Node}");
        return (id, ticket.InboxKey);
    }

    /// <summary>The outside publish: the proposed refusal, then the real <c>Events.PublishAsync</c>.</summary>
    private Receipt Outside(string id, string what)
    {
        var (events, messages) = (_k.Store.EventRows.Count, _k.Store.Messages.Count);
        if (rules.Refusal && id.Length > 0 && id[0] == Reserved)
        {
            _r.Line($"t={_k.Now}: {what}: publish ({K}, {id}) payload {FromOutside}: refused with InvalidEventId (proposed), not retryable; nothing is written and no receipt is issued");
            return new("InvalidEventId", events, _k.Store.EventRows.Count, messages, _k.Store.Messages.Count);
        }
        var row = new EventRow(K, id, FromOutside, _k.Now + (long)Keep.TotalMilliseconds);
        var matched = Real.EventsPublish(_k.Store, row, Kernel.Partitions);
        var stored = _k.Store.EventRows[(K, id)];
        var status = ReferenceEquals(stored, row) ? "New" : "Duplicate";
        _r.Line($"t={_k.Now}: {what}: publish ({K}, {id}) payload {FromOutside}: event row {(status == "New" ? "inserted" : $"exists (payload {stored.Payload}), kept")}, receipt {status}; " +
            string.Join("; ", matched.Select(m => $"delivery of {m.Route.Event.Payload} to {m.Route.Wait.Wait}: {(m.Accepted.Added ? "added" : "refused, the key exists")}")));
        return new(status, events, _k.Store.EventRows.Count, messages, _k.Store.Messages.Count);
    }

    /// <summary>The owner's step of the publish ticket, as Worker.cs 1323–1325 runs it, then the commit that consumes the ticket (as MeetAsync enqueues it, 1339).</summary>
    private void Step(InboxKey ticket, string what)
    {
        var stored = _k.Store.Messages[ticket].Message;
        var e = stored.Read<EventRow>();
        var matched = Real.EventsPublish(_k.Store, e, Kernel.Partitions);
        var row = _k.Store.EventRows[(e.Key, e.Id)];
        var inserted = ReferenceEquals(row, e);
        _r.Line($"t={_k.Now}: {what} ({_k.OwnerOf(P).Node}): Events.PublishAsync of ({e.Key}, {e.Id}) payload {e.Payload}: event row {(inserted ? "inserted" : $"exists (payload {row.Payload}), kept; the turn's insert writes nothing")}; " +
            (matched.Count == 0 ? "no route" : string.Join("; ", matched.Select(m => $"route from the stored row, payload {m.Route.Event.Payload}, to {m.Route.Wait.Wait}: {(m.Accepted.Added ? "added" : "refused, the key exists")}"))));
        var (landed, error) = _k.Commit(P, [new TurnCommit(P, null, null, [ticket], [], [], [], [], null)]);
        _r.Line($"  the ticket's consumption: {(landed ? "landed" : "not landed: " + error)}");
    }

    /// <summary>W's turn: consume every pending delivery to W, in seq order, through the real commit (kept rows become Done).</summary>
    private void Consume()
    {
        var due = _k.Store.Messages.Values.Where(m => m.Message.Target.Id == W && m.Message.Kind == Kinds.Event && !m.Done).OrderBy(m => m.Seq).ToList();
        if (due.Count == 0) { _r.Line($"t={_k.Now}: W's turn: nothing pending"); return; }
        var commit = new TurnCommit(W, null, null, [], due.Select(m => m.Message.InboxKey).ToList(), [], [], [], null);
        var (landed, error) = _k.Commit(W, [commit]);
        _r.Line($"t={_k.Now}: W's turn consumes [{string.Join(", ", due.Select(m => m.Message.Payload))}]: {(landed ? "landed, the rows are kept Done" : "not landed: " + error)}");
        if (landed) _received.AddRange(due.Select(m => m.Message.Payload!));
    }
}
