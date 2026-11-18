using System.Text;
using System.Text.Json.Nodes;

namespace Kernel.Review6;

/// <summary>
/// NT-1 (R6-B1): a Native parent's terminal state can be larger than its running state. Every case drives a real
/// <see cref="NativeModel"/> state through the real <c>WorkflowInstance</c> terminate (WorkflowInstance.cs 179–203,
/// which calls <c>NativeModel.TerminateMetadata</c> at 184–189) and commits through the real <c>InMemoryStore</c>.
/// The envelope check is the proposed one and the harness holds it: before each commit of P it computes
/// state + <c>TerminalGrowth(state)</c> (repaired only) + registries at marker sizes + the 16,384-byte header and refuses
/// the commit as a definite error above 16,457,728 bytes; the refused turn fails and parks through the real
/// <c>WorkflowInstance.Park</c>, as an over-envelope turn does.
/// </summary>
sealed class Nt1(string variant, Rules rules)
{
    public static readonly string[] Variants = ["growth", "witness", "at-bound", "thendelete-protected", "no-inboxes"];

    /// <summary>The instance envelope before <c>ParkRecord</c>: 16 MiB less 312 KiB (observability "The envelope bound and its headroom").</summary>
    public const long Cap = 16_457_728;
    /// <summary>The reserved cascade header (data lifecycle, "The bound, and when it clears", round 5).</summary>
    public const long Header = 16_384;
    /// <summary>The marker's per-entry sizes, SQL Server's UTF-16 (data lifecycle, same paragraph).</summary>
    public const long ChildEntry = 384, JobEntry = 12_416;
    /// <summary>
    /// The measured closing cost of one open invocation inbox (the <c>nt1-growth</c> case measures it): "Closed":null
    /// becomes "Closed":"cancelled" (InvocationLifetime.cs 32), 4 bytes to 11 in UTF-8. The end paths' reasons,
    /// "completed" and "faulted", cost 7 and 5.
    /// </summary>
    public const int MeasuredPerInbox = 7;
    /// <summary>
    /// The gross growth of one non-final receipt before its pending input is cleared (InvocationLifetime.cs 36–38):
    /// Status Queued to NotProcessed (+6), Outcome null to "cancelled" (+7), Clock null to "workflow:end" (+10).
    /// </summary>
    public const int ReceiptGross = 23;
    private const int Inboxes = 3_500;

    private const string Q = "0/q", P = "1/p", G = "g2/child/1.1.1";
    private readonly Report _r = new($"NT-1/{variant}", rules.Name, variant == "growth"
        ? "Native's TerminalGrowth(state) bound covers the real TerminateMetadata and end-path growth of every measured shape"
        : "every state P commits leaves room for its own terminate, so the ending commit lands, the first counted child terminate is stored, G ends Cancelled and Q hears one notice from P");
    private Kernel _k = null!;
    private readonly NativeModel _native = new(new WorkflowRegistry());
    private readonly List<string> _landedStates = [];

    public Report Run()
    {
        if (variant == "growth") { Measure(); return _r; }
        _k = new Kernel(_r, [_native, new StubModel()]);
        Seed();
        var thenDelete = variant == "thendelete-protected";
        var grown = Synthesize();
        Grow(grown);
        Terminate(thenDelete);
        DeliverChildTerminates();
        Assert(thenDelete);
        return _r;
    }

    // ---- the per-item figure ----------------------------------------------------------------------------------------

    private static long Bytes(string s) => Encoding.UTF8.GetByteCount(s);

    /// <summary>Native's <c>TerminalGrowth(state)</c> (proposed, layer 3, pure): each open inbox at its closing cost, each non-final receipt at its gross growth.</summary>
    public long Bound(NativeState state) => state.Inboxes?.Values.Where(i => i.Closed is null)
        .Sum(i => rules.GrowthPerInbox + (long)ReceiptGross * i.Receipts.Values.Count(r => r.Status is "Queued" or "Evaluating" or "Selected")) ?? 0;

    private long Growth(NativeState state, NativeModel? model = null, string? endReason = null)
    {
        var copy = Json.Read<NativeState>(Json.Write(state));
        var before = Bytes(Json.Write(copy));
        if (endReason is null) Real.TerminateMetadata(model ?? _native, copy); else Real.CloseAll(copy, endReason);
        return Bytes(Json.Write(copy)) - before;
    }

    private static NativeState One(Action<InvocationInbox, NativeState> shape)
    {
        var state = new NativeState { Started = true, Inboxes = new(StringComparer.Ordinal) };
        state.Frames["f1"] = new Frame { Id = "f1", Node = "receiver" };
        var inbox = state.Inboxes["f1"] = new InvocationInbox { Capacity = 1, ReceiptCapacity = 1 };
        shape(inbox, state);
        return state;
    }

    private static readonly InvocationInput Input = new("f1", "i1", "n");
    private static InputReceipt Receipt(string status, string? outcome = null, string? clock = null) =>
        new("v1:" + new string('0', 64), 1, status, outcome, 60_000, clock);

    /// <summary>The receipt with its internal <c>EvaluationStarted</c> flag set, through its JSON form (InvocationInbox.cs 23–24).</summary>
    private static InputReceipt Started(InputReceipt receipt) => Json.Read<InputReceipt>(Json.Write(receipt).TrimEnd('}') + ",\"EvaluationStarted\":true}");

    private void Measure()
    {
        var keep = new NativeModel(new WorkflowRegistry(), new NativeOptions { AdmissionFlaws = AdmissionFlaw.KeepPayload });
        var shapes = new List<(string Name, NativeState State, string? End)>
        {
            ("one open inbox, empty, no retention", One((_, _) => { }), null),
            ("one open inbox, empty, retention 60 s", One((i, _) => i.Retention = 60_000), null),
            ("one closed inbox", One((i, _) => i.Closed = "completed"), null),
            ("one open inbox, a Queued receipt and its pending input", One((i, _) => { i.Retention = 60_000; i.Receipts["i1"] = Receipt("Queued"); i.Pending.Add(Input); }), null),
            ("one open inbox, a Selected receipt and its pending input", One((i, _) => { i.Retention = 60_000; i.Receipts["i1"] = Receipt("Selected"); i.Pending.Add(Input); }), null),
            ("one open inbox, an Evaluating receipt and its pending input", One((i, _) => { i.Retention = 60_000; i.Receipts["i1"] = Started(Receipt("Evaluating")); i.Pending.Add(Input); }), null),
            ("one open inbox, a Completed receipt", One((i, _) => { i.Retention = 60_000; i.Receipts["i1"] = Receipt("Completed", "done", "turn:1"); }), null),
            ("a frame with a lexical scope and its resource, no inbox", Json.Read<NativeState>(
                """{"Started":true,"Frames":{"f1":{"Id":"f1","Node":"receiver","LexicalScope":"s1"}},"LexicalScopes":{"s1":{"Owner":"f1","Parent":null,"Values":{"v":1}}}}"""), null),
            ("end path: one open empty inbox closed as completed", One((_, _) => { }), "completed"),
            ("end path: one open empty inbox closed as faulted", One((_, _) => { }), "faulted"),
            ($"the witness's structure: {Inboxes:N0} open empty inboxes and frames", Witness(0), null),
        };
        _r.Line($"Native TerminalGrowth bound (proposed): {rules.GrowthPerInbox} bytes per open inbox, plus {ReceiptGross} per non-final receipt; frames 0");
        _r.Line("growth measured with the real NativeModel.TerminateMetadata (InvocationLifetime.cs 29-45, LexicalScopes.cs 28-34), UTF-8 of Json.Write:");
        long first = long.MinValue;
        var witness = (Growth: 0L, Bound: 0L);
        var allWithin = true;
        foreach (var (name, state, end) in shapes)
        {
            var growth = Growth(state, endReason: end);
            var gross = end is null ? Growth(state, keep) : growth;
            var bound = Bound(state);
            allWithin &= growth <= bound && gross <= bound;
            _r.Line($"  {name}: growth {growth:+#;-#;0} bytes{(gross != growth ? $" (gross {gross:+#;-#;0} with the pending input kept, AdmissionFlaw.KeepPayload)" : "")}; bound {bound}");
            if (first == long.MinValue) first = growth;
            if (name.StartsWith("the witness", StringComparison.Ordinal)) witness = (growth, bound);
        }
        _r.Line("  in SQL Server's UTF-16 each figure doubles (derived, unmeasured)");
        _r.Expect("the closing cost of one open empty inbox, measured", first, (long)MeasuredPerInbox);
        _r.Check(allWithin, $"every measured growth, net and gross, is within the bound: {allWithin}");
        _r.Expect($"the witness structure's growth equals its bound ({Inboxes:N0} x {rules.GrowthPerInbox})", witness.Growth, witness.Bound);
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
        _landedStates.Add(_k.Store.Instances[P].State);
        _r.Line($"setup: Q {Q} (parent), P {P} (Native, Running, creation 1, children [G@1]), G {G} (Native, Running, child of P)");
    }

    /// <summary>The reviewer's witness structure (structural-witness.log): open empty inboxes with frames, then long null-valued labels and a padding string to <paramref name="target"/> bytes.</summary>
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

    private NativeState Synthesize()
    {
        var registries = ChildEntry;
        NativeState state;
        string how;
        switch (variant)
        {
            case "witness":
                // The reviewer's figure: the cap less the header, the one child and 4,096 bytes for other fields.
                state = Witness(Cap - Header - ChildEntry - 4_096);
                how = "the reviewer's witness size, cap - header - one child - 4,096";
                break;
            case "at-bound":
            case "thendelete-protected":
            {
                var receipt = variant == "thendelete-protected";
                var growth = rules.TerminalGrowth ? Bound(Witness(0, receipt)) : 0;
                state = Witness(Cap - Header - registries - growth, receipt);
                how = $"the largest state this rule set's check admits, cap - header - one child - {growth:N0} reserved";
                break;
            }
            case "no-inboxes":
                state = new NativeState { Started = true };
                state.Variables["padding"] = "";
                state.Variables["padding"] = new string('p', (int)(Cap - Header - registries - Bytes(Json.Write(state))));
                how = "a Native state with no invocation inbox, padded to the reviewed bound";
                break;
            default: throw new ArgumentException(variant);
        }
        _r.Line($"the grown state: {Bytes(Json.Write(state)):N0} bytes ({how}); {state.Inboxes?.Count ?? 0:N0} open inboxes, {state.Variables.Count:N0} variables");
        return state;
    }

    // ---- the envelope check (proposed, held by the harness) ---------------------------------------------------------

    private sealed record Envelope(long State, long Growth, long Registries, long Other, long Total)
    {
        public bool Fits => Total <= Cap;
        public override string ToString() =>
            $"state {State:N0} + TerminalGrowth {Growth:N0} + registries {Registries:N0} + header {Header:N0} = {Total:N0} {(Fits ? "<=" : ">")} {Cap:N0} (margin {Cap - Total:N0}); the row's other fields {Other:N0} bytes, inside the header";
    }

    private Envelope EnvelopeOf(InstanceRow row, long children, long jobs)
    {
        var state = Bytes(row.State);
        var growth = rules.TerminalGrowth && row.Model == NativeModel.ModelKey && row.State != "null" ? Bound(Json.Read<NativeState>(row.State)) : 0;
        var registries = ChildEntry * children + JobEntry * jobs;
        var other = Bytes(Json.Write(row with { State = "", Children = null, Jobs = null, Fault = null }));
        return new(state, growth, registries, other, state + growth + registries + Header);
    }

    private static long Count(string? json) => json is null ? 0 : Json.Read<JsonArray>(json).Count;

    /// <summary>One commit of P's partition, behind the envelope check. A refusal is a definite error: the turn fails and parks.</summary>
    private bool Checked(string what, TurnCommit commit, InstanceRow previous, long seq, Message trigger)
    {
        var row = commit.Row!;
        // A terminate replaces the registries with its list: the list is the previous row's registries.
        var envelope = EnvelopeOf(row, Math.Max(Count(row.Children), Count(previous.Children)), Math.Max(Count(row.Jobs), Count(previous.Jobs)));
        _r.Line($"{what}: envelope check (proposed{(rules.TerminalGrowth ? ", with TerminalGrowth" : ", reviewed form")}): {envelope}");
        if (envelope.Fits)
        {
            var (landed, error) = _k.Commit(row.Id, [commit]);
            _r.Line($"  {(landed ? "landed" : "not landed: " + error)}");
            if (landed) _landedStates.Add(row.State);
            return landed;
        }
        _r.Line("  refused before the store as a definite error (over the envelope); the turn fails and parks (WorkflowInstance.Park)");
        var park = Real.Park(_k.Type, _k.OwnerOf(P).Worker, previous, trigger, seq, $"envelope: {envelope.Total} > {Cap}");
        var parkCommit = Kernel.ToTurnCommit(park);
        var parked = EnvelopeOf(parkCommit.Row!, Count(parkCommit.Row!.Children), Count(parkCommit.Row!.Jobs));
        _r.Line($"  Park: P {previous.Status} -> {park.Row!.Status}, state kept ({Bytes(park.Row.State):N0} bytes), registry kept, sends [{string.Join(", ", park.Sent.Select(Show))}]; envelope {parked}");
        var (ok, err) = _k.Commit(P, [parkCommit]);
        _r.Line($"  the Park commit {(ok ? "landed" : "did not land: " + err)}");
        if (ok) _landedStates.Add(parkCommit.Row!.State);
        return false;
    }

    private static string Show(Message m) => $"{m.Kind} {m.Id} to {m.Target.Id}";

    /// <summary>P's turn that writes the grown state (harness-composed: the synthesized state stands for a model turn's result).</summary>
    private void Grow(NativeState grown)
    {
        var trigger = new Message("grow-1", new InstanceRef(P), "app.grow", Sender: "operator", TargetCreation: 1);
        var (key, seq) = _k.AddOutside(trigger);
        var row = _k.Store.Instances[P];
        var next = row with { State = Json.Write(grown), Revision = row.Revision + 1 };
        Checked("P's growth turn (writes the grown state)", new TurnCommit(P, next, row.Revision, [key], [], [], [], [], Json.Write(new[] { "grown" })), row, seq, trigger);
    }

    private TurnCommit? _ending;

    private void Terminate(bool thenDelete)
    {
        var payload = thenDelete ? """{"ThenDelete":true}""" : null;
        var term = new Message("term-1", new InstanceRef(P), Kinds.Terminate, payload, Sender: "operator", TargetCreation: 1);
        var (_, seq) = _k.AddOutside(term);
        var row = _k.Store.Instances[P];
        var outcome = Real.Command(_k.Type, _k.OwnerOf(P).Worker, row, term, seq)
            ?? throw new InvalidOperationException("a terminate takes the actor type's own turn");
        _r.Line($"deliver {term.Id} (operator's terminate{(thenDelete ? " with ThenDelete" : "")}) to P: the built terminate (WorkflowInstance.cs 179-203); P {row.Status} -> {outcome.Row!.Status}, sends [{string.Join(", ", outcome.Sent.Select(Show))}]");
        if (row.Status == InstanceStatus.Running)
            _r.Line($"  TerminateMetadata grew P's state by {Bytes(outcome.Row.State) - Bytes(row.State):N0} bytes ({Bytes(row.State):N0} -> {Bytes(outcome.Row.State):N0})");
        if (thenDelete)
        {
            // ThenDelete (data lifecycle, "Terminate, then delete", proposed): terminate, then delete unless ProtectsDeletion protects the row.
            var state = Json.Read<NativeState>(outcome.Row.State);
            var clock = Real.ModelTurn(_k.OwnerOf(P).Worker, outcome.Row, [Real.Delivery(term, seq)]).Clock;
            var protects = Real.ProtectsDeletion(_native, state, _k.Now, clock);
            _r.Line($"  ThenDelete: NativeModel.ProtectsDeletion on the terminated state: {protects}, so P is {(protects ? "terminated and kept: the commit is the live row's write" : "deleted in the same commit")}");
            if (!protects) throw new InvalidOperationException("the protected ThenDelete fixture is not protected");
        }
        _ending = Kernel.ToTurnCommit(outcome);
        Checked("P's ending commit", _ending, row, seq, term);
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

    private void Assert(bool thenDelete)
    {
        // The invariant, measured with the real code: every P state that landed leaves room for its own terminate.
        var worst = _landedStates.Select(s => (Bytes: Bytes(s), Growth: s == "null" ? 0 : Growth(Json.Read<NativeState>(s)))).MaxBy(x => x.Bytes + x.Growth);
        var worstTotal = worst.Bytes + worst.Growth + ChildEntry + Header;
        _r.Line($"the largest landed P state: {worst.Bytes:N0} bytes, its real terminal growth {worst.Growth:N0}: with one child and the header {worstTotal:N0} (cap {Cap:N0})");
        _r.Check(worstTotal <= Cap, $"every state P committed leaves room for its own terminate: {worstTotal:N0} <= {Cap:N0}: {worstTotal <= Cap}");
        var childSends = _k.Sent.Where(s => s.Message.Target.Id == G && s.Message.Kind == Kinds.Terminate && s.Message.Sender == P).ToList();
        _r.Line($"counted terminates to G stored by a landed commit: [{string.Join(", ", childSends.Select(s => $"{s.Message.Id} (seq {s.Seq})"))}]");
        var first = childSends.Count > 0 && _k.Store.Instances[G].Status == InstanceStatus.Cancelled;
        _r.Check(first, $"the first counted child terminate was stored by a landed commit and G ended Cancelled: {childSends.Count} stored, G {_k.Store.Instances[G].Status}");
        var notices = _k.Sent.Where(s => s.Message.Target.Id == Q && s.Message.Kind == WorkflowKinds.ChildFinished && s.Message.Sender == P).ToList();
        _r.Expect("notices from P to Q", notices.Count, 1);
        _r.Line($"  P's notice: {string.Join(", ", notices.Select(n => n.Message.Payload))}");
        var p = _k.Store.Instances[P];
        if (variant != "witness")
        {
            var state = Json.Read<NativeState>(p.State);
            var open = state.Inboxes?.Values.Count(i => i.Closed is null) ?? 0;
            _r.Check(p.Status == InstanceStatus.Cancelled && open == 0, $"P ended Cancelled{(thenDelete ? " and kept" : "")} with its forensic state, every invocation inbox closed: {p.Status}, {open} open");
        }
        else _r.Line($"P ends {p.Status}; its state {Bytes(p.State):N0} bytes");
    }
}
