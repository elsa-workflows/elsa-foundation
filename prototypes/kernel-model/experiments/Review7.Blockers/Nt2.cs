using System.Text;
using System.Text.Json.Nodes;

namespace Kernel.Review7;

/// <summary>
/// NT-2 (R7-B1): a Native parent's retained creator handles enlarge its ending row. P's detached starts run through
/// the real <c>Turn.Start</c> (Turn.cs 53–61) and the real <c>DurableTurn.StartChild</c> (Turns.cs 364–376), and the
/// real <c>DurableTurn.Seal</c> keeps every creator handle in <c>CustomHandles</c> (Turns.cs 490–498). Each commit goes
/// through the real <c>InMemoryStore.CommitAsync</c>. The terminate is the real <c>WorkflowInstance</c> terminate
/// (WorkflowInstance.cs 179–203), which runs <c>NativeModel.TerminateMetadata</c> and keeps <c>CustomHandles</c>.
/// Held by the harness (proposed): the admission projection of every commit of a live row, the row cap of every
/// commit (the store's real <c>Refuses</c> hook, InMemoryStore.cs 49 and 110, measuring the row as <see cref="Measured"/>
/// does) and Native's <c>TerminalGrowth(state)</c> bound. A turn over either fails and parks through the real
/// <c>WorkflowInstance.Park</c>, as an over-envelope turn does.
/// </summary>
sealed class Nt2(string variant, Rules rules)
{
    public static readonly string[] Variants = ["handles", "at-bound", "thendelete-protected", "no-handles"];

    /// <summary>The instance envelope before <c>ParkRecord</c>: 16 MiB less 312 KiB (observability "The envelope bound and its headroom").</summary>
    public const long Cap = 16_457_728;
    /// <summary>The fixed reserve. Repaired: it covers only the bounded fixed fields; <c>Model</c>, <c>Definition</c> and <c>CustomHandles</c> are counted at their encoded size. Reviewed: every non-state field.</summary>
    public const long Reserve = 16_384;
    /// <summary>The marker's per-entry sizes, SQL Server's UTF-16 (data lifecycle, "The bound, and when it clears").</summary>
    public const long ChildEntry = 384, JobEntry = 12_416;
    /// <summary>Native's <c>TerminalGrowth</c> bound, the sixth round's (measured by Review6.Blockers <c>nt1-growth</c>): 7 bytes per open inbox, 23 per non-final receipt.</summary>
    public const int PerInbox = 7, ReceiptGross = 23;
    private const int Inboxes = 3_500, Starts = 500, SpawnCommits = 3;

    private const string Q = "0/q", P = "1/p", G = "g2/child/1.1.1";
    private readonly Report _r = new($"NT-2/{variant}", rules.Name,
        "every row P commits leaves room for its own terminate's ending row, so the first counted child terminate is stored by a landed commit, G ends Cancelled and one notice from P to Q is stored");
    private Kernel _k = null!;
    private readonly NativeModel _native = new(new WorkflowRegistry());
    private WorkflowInstance _spawn = null!;
    private readonly List<InstanceRow> _landed = [];
    private readonly List<(string What, long Total)> _admitted = [];
    private InstanceRow? _beforeGrowth, _afterGrowth, _beforeTerminate;
    private (bool Landed, long Measured, long Projected) _ending;

    public Report Run()
    {
        _k = new Kernel(_r, [_native, new StubModel()], refuses: row => Measured(row) > Cap);
        _spawn = new WorkflowInstance([new Spawner(_native.StateVersion, Starts)]);
        var thenDelete = variant == "thendelete-protected";
        Seed();
        Spawn(variant == "no-handles" ? 0 : SpawnCommits);
        Grow(Synthesize(thenDelete));
        Terminate(thenDelete);
        DeliverChildTerminates();
        Assert(thenDelete);
        return _r;
    }

    // ---- the measures (proposed, held by the harness) ---------------------------------------------------------------

    private static long Bytes(string? s) => s is null ? 0 : Encoding.UTF8.GetByteCount(s);

    /// <summary>The row's other fields, as one JSON object in UTF-8: every field but the four JSON text columns.</summary>
    public static long Fixed(InstanceRow row) => Bytes(Json.Write(row with { State = "", Jobs = null, Children = null, CustomHandles = null }));

    /// <summary>
    /// The row as the harness measures it for the row cap: its JSON text columns <c>State</c>, <c>Jobs</c>,
    /// <c>Children</c> and <c>CustomHandles</c> at their UTF-8 size, and the other fields as one JSON object in UTF-8.
    /// The provider's stored encoding (BSON, SQL Server's UTF-16) is qualified at K4.
    /// </summary>
    public static long Measured(InstanceRow row) => Bytes(row.State) + Bytes(row.Jobs) + Bytes(row.Children) + Bytes(row.CustomHandles) + Fixed(row);

    /// <summary>Native's <c>TerminalGrowth(state)</c> (proposed, layer 3, pure): each open inbox at its closing cost, each non-final receipt at its gross growth.</summary>
    private static long Bound(NativeState state) => state.Inboxes?.Values.Where(i => i.Closed is null)
        .Sum(i => PerInbox + (long)ReceiptGross * i.Receipts.Values.Count(r => r.Status is "Queued" or "Evaluating" or "Selected")) ?? 0;

    private sealed record Projection(long State, long Growth, long Registries, long Handles, long Names, long Total)
    {
        public bool Fits => Total <= Cap;
        public override string ToString() =>
            $"state {State:N0} + TerminalGrowth {Growth:N0} + registries {Registries:N0} + CustomHandles {Handles:N0} + Model and Definition {Names:N0} + reserve {Reserve:N0} = {Total:N0} {(Fits ? "<=" : ">")} {Cap:N0} (margin {Cap - Total:N0})";
    }

    /// <summary>A string field's encoded size, as <see cref="Measured"/> counts it inside the other fields: its JSON string in UTF-8.</summary>
    private static long Encoded(string value) => Bytes(Json.Write(value));

    /// <summary>
    /// The projected terminal row of a live row. Reviewed counts the handles, <c>Model</c> and <c>Definition</c> as 0;
    /// repaired counts each at its encoded size, since no rule bounds them.
    /// </summary>
    private static Projection ProjectionOf(InstanceRow row, long children, long jobs, bool countHandles, bool countNames)
    {
        var state = Bytes(row.State);
        var growth = row.Model == NativeModel.ModelKey && row.State != "null" ? Bound(Json.Read<NativeState>(row.State)) : 0;
        var registries = ChildEntry * children + JobEntry * jobs;
        var handles = countHandles ? Bytes(row.CustomHandles) : 0;
        var names = countNames ? Encoded(row.Model) + Encoded(row.Definition) : 0;
        return new(state, growth, registries, handles, names, state + growth + registries + handles + names + Reserve);
    }

    private static long Count(string? json) => json is null ? 0 : Json.Read<JsonArray>(json).Count;
    private static int Handles(InstanceRow row) => row.CustomHandles is null ? 0 : DurableKeyJson.ReadKeys(row.CustomHandles).Count;
    private static string Registry(InstanceRow row) => string.Join(", ", Real.ReadRegistry(row.Children).Select(c => $"{c.Id}@{c.Creation}"));
    private static string Show(Message m) => $"{m.Kind} {m.Id} to {m.Target.Id}";

    // ---- the commits ------------------------------------------------------------------------------------------------

    /// <summary>
    /// One commit of P's partition. A live row's commit is first checked against the projection (proposed); every
    /// commit then meets the store's row cap. Either refusal is a definite error: the turn fails and parks.
    /// </summary>
    private bool Checked(string what, TurnCommit commit, InstanceRow previous, long seq, Message trigger)
    {
        var row = commit.Row!;
        if (row.Status == InstanceStatus.Running)
        {
            var projection = ProjectionOf(row, Count(row.Children), Count(row.Jobs), rules.CountHandles, rules.CountNames);
            _r.Line($"{what}: admission check of a live row (proposed, {(rules.CountHandles ? "the full projection" : "the sixth round's formula")}): {projection}");
            if (!projection.Fits)
            {
                _r.Line("  refused before the store as a definite error (over the envelope); the turn fails and parks (WorkflowInstance.Park)");
                Park(previous, seq, trigger, $"envelope: {projection.Total} > {Cap}");
                return false;
            }
            _admitted.Add((what, projection.Total));
        }
        else _r.Line($"{what}: the row ends {row.Status}, so no admission projection applies; the store's row cap does");
        var measured = Measured(row);
        var (landed, error) = _k.Commit(row.Id, [commit]);
        _r.Line($"  the store's row cap (proposed, the Refuses hook): the row measures {measured:N0} bytes ({Bytes(row.State):N0} state, " +
            $"{Bytes(row.CustomHandles):N0} CustomHandles, {Bytes(row.Children) + Bytes(row.Jobs):N0} registries, {Fixed(row):N0} other fields) " +
            $"{(measured <= Cap ? "<=" : ">")} {Cap:N0} (margin {Cap - measured:N0}): {(landed ? "landed" : "not landed: " + error)}");
        if (landed) { _landed.Add(row); return true; }
        if (error is not null && error.Contains("(Definite)", StringComparison.Ordinal))
        {
            _r.Line("  the definite refusal fails the turn, which parks (WorkflowInstance.Park)");
            Park(previous, seq, trigger, $"row cap: {measured} > {Cap}");
        }
        return false;
    }

    private void Park(InstanceRow previous, long seq, Message trigger, string error)
    {
        var park = Real.Park(_k.Type, _k.OwnerOf(P).Worker, previous, trigger, seq, error);
        var commit = Kernel.ToTurnCommit(park);
        var (ok, err) = _k.Commit(P, [commit]);
        _r.Line($"  Park: P {previous.Status} -> {park.Row!.Status}, state kept ({Bytes(park.Row.State):N0} bytes), {Handles(park.Row):N0} creator handles kept, " +
            $"registry [{Registry(park.Row)}] kept, sends [{string.Join(", ", park.Sent.Select(Show))}]; the row measures {Measured(park.Row):N0}: {(ok ? "landed" : "did not land: " + err)}");
        if (ok) _landed.Add(commit.Row!);
    }

    // ---- the schedule -----------------------------------------------------------------------------------------------

    private void Seed()
    {
        var child = Real.WriteRegistry([new CreationAddress(G, 1)]);
        _k.Store.Instances[Q] = new InstanceRow(Q, "stub", "q", InstanceStatus.Running, "{}", 1, null, Creation: 1);
        _k.Store.Instances[P] = new InstanceRow(P, NativeModel.ModelKey, "p", InstanceStatus.Running, Json.Write(new NativeState { Started = true }), 1, Q,
            Children: child, StateVersion: _native.StateVersion, Creation: 1, ParentCreation: 1);
        _k.Store.Instances[G] = new InstanceRow(G, NativeModel.ModelKey, "g", InstanceStatus.Running, Json.Write(new NativeState { Started = true }), 1, P,
            StateVersion: _native.StateVersion, Creation: 1, ParentCreation: 1);
        _landed.Add(_k.Store.Instances[P]);
        _r.Line($"setup: Q {Q} (parent), P {P} (Native, Running, creation 1, children [G@1]), G {G} (Native, Running, child of P)");
    }

    /// <summary>P's spawn turns: each a real model turn with <see cref="Starts"/> detached starts, committed through the real commit.</summary>
    private void Spawn(int commits)
    {
        for (var c = 1; c <= commits; c++)
        {
            var trigger = new Message($"spawn-{c}", new InstanceRef(P), "app.spawn", Sender: "operator", TargetCreation: 1);
            var (_, seq) = _k.AddOutside(trigger);
            var row = _k.Store.Instances[P];
            var outcome = Real.ModelTurnAsync(_spawn, _k.OwnerOf(P).Worker, row, [Real.Delivery(trigger, seq)]);
            var starts = outcome.Sent.Count(m => m.Kind == Kinds.Start);
            _r.Line($"P's spawn turn {c} (the real Turn.Start with detached: true, {Starts} times): {starts} start sends to " +
                $"{outcome.Sent.Where(m => m.Kind == Kinds.Start).Select(m => m.Target.Partition).Distinct().Count()} partitions; the sealed row holds " +
                $"{Handles(outcome.Row!):N0} creator handles ({Bytes(outcome.Row!.CustomHandles):N0} bytes) and registry [{Registry(outcome.Row)}]");
            Checked($"P's spawn turn {c}", Kernel.ToTurnCommit(outcome), row, seq, trigger);
        }
        var stored = _k.Store.Instances[P];
        var expected = commits * Starts;
        var registry = Real.ReadRegistry(stored.Children);
        if (commits == 0) _r.Line("no spawn turn: P makes no detached start");
        _r.Check("handles-accumulated", Handles(stored) == expected && registry.Count == 1 && registry[0].Id == G,
            $"P's stored row after the spawn commits holds {Handles(stored):N0} creator handles ({Bytes(stored.CustomHandles):N0} bytes; expected {expected:N0}) " +
            $"and its registry lists [{Registry(stored)}] (expected [{G}@1])");
    }

    private static readonly InvocationInput Input = new("f1", "i1", "n");
    private static InputReceipt Receipt(string status) => new("v1:" + new string('0', 64), 1, status, null, 60_000, null);

    /// <summary>The reviewer's boundary structure (as Review6.Blockers builds it): 3,500 open empty inboxes with frames, then long null-valued labels and a padding string to <paramref name="target"/> bytes.</summary>
    private static NativeState Witness(long target, bool receipt = false)
    {
        var state = new NativeState { Started = true, Inboxes = new(StringComparer.Ordinal) };
        for (var n = 1; n <= Inboxes; n++)
        {
            var id = "f" + n;
            state.Frames[id] = new Frame { Id = id, Node = "receiver" };
            state.Inboxes[id] = new InvocationInbox { Capacity = 1, ReceiptCapacity = 1 };
        }
        if (receipt)
        {
            // One pending invocation input, Queued: Native's ProtectsDeletion keeps the row (InvocationLifetime.cs 55-56).
            var inbox = state.Inboxes["f1"];
            inbox.Retention = 60_000;
            inbox.Receipts["i1"] = Receipt("Queued");
            inbox.Pending.Add(Input);
        }
        if (target == 0) return state;
        state.Variables["padding"] = "";
        var remaining = target - Bytes(Json.Write(state));
        for (var n = 0; remaining >= 515 + 64; n++)
        {
            var label = n.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + new string('v', 500);
            state.Variables[label] = null;
            remaining -= label.Length + 8;
        }
        state.Variables["padding"] = new string('p', (int)remaining);
        var size = Bytes(Json.Write(state));
        if (size != target) state.Variables["padding"] = new string('p', (int)(remaining + target - size));
        return state;
    }

    private NativeState Synthesize(bool receipt)
    {
        var handles = Bytes(_k.Store.Instances[P].CustomHandles);
        long target;
        string how;
        if (variant == "handles")
        {
            target = Cap - Reserve - ChildEntry - (long)Inboxes * PerInbox;
            how = "the reviewer's figure, the largest state the sixth round's formula admits with one child: cap - reserve - one child - 24,500";
        }
        else
        {
            var growth = Bound(Witness(0, receipt));
            var counted = rules.CountHandles ? handles : 0;
            var p = _k.Store.Instances[P];
            var names = rules.CountNames ? Encoded(p.Model) + Encoded(p.Definition) : 0;
            target = Cap - Reserve - ChildEntry - growth - counted - names;
            how = $"the largest state this rule set's check admits: cap - reserve - one child - {growth:N0} TerminalGrowth - {counted:N0} CustomHandles - {names:N0} Model and Definition counted";
        }
        var state = Witness(target, receipt);
        _r.Line($"the grown state: {Bytes(Json.Write(state)):N0} bytes ({how}); {state.Inboxes?.Count ?? 0:N0} open inboxes, {state.Variables.Count:N0} variables");
        return state;
    }

    /// <summary>P's turn that writes the grown state (harness-composed: the synthesized state stands for a model turn's result; the row's other fields, its handles included, are kept).</summary>
    private void Grow(NativeState grown)
    {
        var trigger = new Message("grow-1", new InstanceRef(P), "app.grow", Sender: "operator", TargetCreation: 1);
        var (key, seq) = _k.AddOutside(trigger);
        var row = _beforeGrowth = _k.Store.Instances[P];
        var next = row with { State = Json.Write(grown), Revision = row.Revision + 1 };
        Checked("P's growth turn", new TurnCommit(P, next, row.Revision, [key], [], [], [], [], Json.Write(new[] { "grown" })), row, seq, trigger);
        _afterGrowth = _k.Store.Instances[P];
    }

    private void Terminate(bool thenDelete)
    {
        var payload = thenDelete ? """{"ThenDelete":true}""" : null;
        var term = new Message("term-1", new InstanceRef(P), Kinds.Terminate, payload, Sender: "operator", TargetCreation: 1);
        var (_, seq) = _k.AddOutside(term);
        var row = _beforeTerminate = _k.Store.Instances[P];
        var outcome = Real.Command(_k.Type, _k.OwnerOf(P).Worker, row, term, seq)
            ?? throw new InvalidOperationException("a terminate takes the actor type's own turn");
        var end = outcome.Row!;
        _r.Line($"deliver {term.Id} (operator's terminate{(thenDelete ? " with ThenDelete" : "")}) to P: the built terminate (WorkflowInstance.cs 179-203); P {row.Status} -> {end.Status}, " +
            $"sends [{string.Join(", ", outcome.Sent.Select(Show))}]; {Handles(end):N0} creator handles kept ({Bytes(end.CustomHandles):N0} bytes)");
        if (row.Status == InstanceStatus.Running)
            _r.Line($"  TerminateMetadata grew P's state by {Bytes(end.State) - Bytes(row.State):N0} bytes ({Bytes(row.State):N0} -> {Bytes(end.State):N0})");
        if (thenDelete)
        {
            // ThenDelete (data lifecycle, "Terminate, then delete", proposed): terminate, then delete unless ProtectsDeletion protects the row.
            var state = Json.Read<NativeState>(end.State);
            var clock = Real.DurableTurn(_k.OwnerOf(P).Worker, end, [Real.Delivery(term, seq)]).Clock;
            if (!Real.ProtectsDeletion(_native, state, _k.Now, clock))
                throw new InvalidOperationException("fixture failure: NativeModel.ProtectsDeletion returned false for the protected ThenDelete fixture, so no commit was attempted");
            _r.Line("  ThenDelete: NativeModel.ProtectsDeletion on the terminated state: True, so P is terminated and kept; the modeled protected outcome is the ordinary terminate commit, the live row's write");
        }
        var projected = ProjectionOf(end, Count(row.Children), Count(row.Jobs), countHandles: true, countNames: true);
        _r.Line($"  for reference, the ending row against the full projection, its registries counted as the previous row's: {projected}");
        var landed = Checked("P's ending commit", Kernel.ToTurnCommit(outcome), row, seq, term);
        _ending = (landed, Measured(end), projected.Total);
    }

    /// <summary>Deliver every stored terminate for G through the real terminate, and commit it.</summary>
    private void DeliverChildTerminates()
    {
        foreach (var (key, stored) in _k.Store.Messages.Where(m => m.Value.Message.Target.Id == G && m.Value.Message.Kind == Kinds.Terminate && !m.Value.Done).OrderBy(m => m.Value.Seq).ToList())
        {
            var row = _k.Store.Instances[G];
            var outcome = Real.Command(_k.Type, _k.OwnerOf(G).Worker, row, stored.Message, stored.Seq)!;
            var (landed, error) = _k.Commit(G, [Kernel.ToTurnCommit(outcome)]);
            _r.Line($"deliver {key.Id} (terminate from {stored.Message.Sender}) to G: the built terminate; G {row.Status} -> {outcome.Row?.Status}; {(landed ? "landed" : "not landed: " + error)}");
        }
    }

    /// <summary>The measured size of the row the real terminate writes for <paramref name="row"/>; 0 when it writes none.</summary>
    private long EndingOf(InstanceRow row)
    {
        var probe = new Message("probe-term", new InstanceRef(P), Kinds.Terminate, Sender: "operator", TargetCreation: 1);
        return Real.Command(_k.Type, _k.OwnerOf(P).Worker, row, probe, 0)?.Row is { } end ? Measured(end) : 0;
    }

    private void Assert(bool thenDelete)
    {
        if (variant == "handles")
            _r.Check("growth-parked", _afterGrowth!.Status == InstanceStatus.Faulted && _afterGrowth.State == _beforeGrowth!.State,
                $"P's growth turn parked: the stored row after it is {_afterGrowth.Status} with its pre-growth state ({Bytes(_beforeGrowth!.State):N0} bytes): " +
                $"{_afterGrowth.Status == InstanceStatus.Faulted && _afterGrowth.State == _beforeGrowth.State}");
        // The invariant, measured with the real code: every row P committed leaves room for the row its own terminate writes.
        var worst = _landed.Select(row => (Row: row, Ending: EndingOf(row))).MaxBy(x => x.Ending);
        _r.Line($"the largest ending row of a landed P row (the real terminate run on a copy): {worst.Ending:N0} bytes, for P at revision {worst.Row.Revision} ({worst.Row.Status}, state {Bytes(worst.Row.State):N0} bytes, {Handles(worst.Row):N0} handles)");
        _r.Check("room-for-terminate", worst.Ending <= Cap, $"every row P committed leaves room for its own terminate's ending row: {worst.Ending:N0} <= {Cap:N0}: {worst.Ending <= Cap}");
        if (variant == "at-bound" && rules.Name == "repaired")
        {
            // The inclusive boundary: the growth commit is admitted at exactly the cap, and the ending commit lands.
            var growth = _admitted.Where(a => a.What == "P's growth turn").Select(a => a.Total).ToList();
            _r.Check("at-cap", growth.SequenceEqual([Cap]) && _ending.Landed && _ending.Projected == Cap && _ending.Measured <= Cap,
                $"the growth commit was admitted with a projected total of exactly {Cap:N0} ([{string.Join(", ", growth.Select(g => g.ToString("N0")))}]), the ending row projects to " +
                $"{_ending.Projected:N0}, and the ending commit {(_ending.Landed ? "landed" : "did not land")} with a measured row of {_ending.Measured:N0} (margin {Cap - _ending.Measured:N0})");
        }
        var childSends = _k.Sent.Where(s => s.Message.Target.Id == G && s.Message.Kind == Kinds.Terminate && s.Message.Sender == P).ToList();
        _r.Line($"counted terminates to G stored by a landed commit: [{string.Join(", ", childSends.Select(s => $"{s.Message.Id} (seq {s.Seq})"))}]");
        var g = _k.Store.Instances[G].Status;
        _r.Check("child-terminate", childSends.Count > 0 && g == InstanceStatus.Cancelled, $"the first counted child terminate was stored by a landed commit and G ended Cancelled: {childSends.Count} stored, G {g}");
        var notices = _k.Sent.Where(s => s.Message.Target.Id == Q && s.Message.Kind == WorkflowKinds.ChildFinished && s.Message.Sender == P).ToList();
        _r.Expect("one-notice", "notices from P to Q stored", notices.Count, 1);
        _r.Line($"  P's notice: {string.Join(", ", notices.Select(n => n.Message.Payload))}");
        var p = _k.Store.Instances[P];
        // Forensic state: the terminal row keeps every inbox of the pre-terminate row, and each kept inbox is closed.
        var before = Json.Read<NativeState>(_beforeTerminate!.State).Inboxes?.Keys.ToHashSet(StringComparer.Ordinal) ?? [];
        var after = Json.Read<NativeState>(p.State).Inboxes ?? new Dictionary<string, InvocationInbox>(StringComparer.Ordinal);
        var kept = before.Count(after.ContainsKey);
        var closed = before.Count(id => after.TryGetValue(id, out var inbox) && inbox.Closed is not null);
        _r.Check("inboxes-kept-closed", kept == before.Count && closed == before.Count,
            $"P's terminal row keeps every inbox of its pre-terminate row, each closed: {before.Count:N0} before, {kept:N0} kept, {closed:N0} closed");
        if (variant != "handles")
            _r.Check("p-cancelled", p.Status == InstanceStatus.Cancelled, $"P ended Cancelled{(thenDelete ? " and kept" : "")}: {p.Status}");
        else _r.Line($"P ends {p.Status}; its state {Bytes(p.State):N0} bytes, {Handles(p):N0} creator handles");
    }
}
