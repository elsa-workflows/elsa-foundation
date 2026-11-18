namespace Kernel.VpExperiment;

/// <summary>
/// Deliberate defects of the ownership protocol, one rule each, for <see cref="OwnershipSim.Mutations"/>. Some rules
/// are each other's backstop and are caught only together: the position check and the reset after a lost answer
/// (as in the kernel's gate, where FenceWithoutPosition is a documented uncaught mutation); in (ii) the bound in the
/// fence and the new epoch at a split.
/// </summary>
[Flags]
public enum Mutation
{
    None = 0,
    FenceWithoutEpoch = 1,         // both: the fence does not check the epoch
    FenceWithoutPosition = 2,      // both: (i) the fence row's position, (ii) the position row, is not checked
    RetryAfterLostAnswer = 4,      // both: a commit whose answer was lost is sent again from the same picture instead of a reset
    FenceWithoutHi = 8,            // (ii): the range fence does not check the bound a split may have moved
    SplitKeepsEpoch = 16,          // (ii): a split leaves the kept row's epoch as it was
    SplitWithoutNewRow = 32,       // (ii): a split frees the tail without writing its row
    MergeKeepsSecondRow = 64,      // (ii): a merge grows the first row and leaves the second
    BeatRenewsTouchedOnly = 128,   // (i): a beat renews only the rows the last flush touched: availability, not safety
    SplitDuringFlush = 256,        // both: an owner releases a tail or merges while a commit of its is still in flight
    FenceSubset = 512,             // (i): the k-row fence covers only the rows still the owner's, and the batch lands those
    SeenAtNodeEpoch = 1024,        // (i): a mark records one epoch for the node's picture (its highest), not its partition's
    ResetAll = 2048,               // (i): a refused flush resets the whole picture (a take), dropping what is still held
    ResetPollsSeenBelow = 4096,    // (i): the poll after a keeping reset still reads Seen < epoch, as a steady poll does
    ProjectorBlindWrite = 8192,    // (i): a projector round moves its partition's cursor without checking it is where it read it
    ProjectorJumps = 16384,        // (i): a projector round moves the cursor to the partition's position, past rows it did not read
}

/// <summary>
/// The ownership protocol of both variants as an in-memory model, driven by a seed: nodes take ranges, commit flushes
/// over the virtual partitions they believe they hold, beat, rebalance (release a tail, carve, take, merge), lose
/// answers, crash and restart, while leases run out. After every action the store's view is checked: one writer per
/// virtual partition (no row landed under a stale epoch or bound, no two rows at one position), a gapless journal per
/// virtual partition, and in (ii) ranges that cover without overlap. Then faults stop and every node must be able to
/// commit on everything it holds. The EF experiment measures the same operations as rows; this shows the rules hold
/// under interleavings at both counts, and <see cref="Mutations"/> shows each rule is load-bearing.
/// (i) also models K1's protocol (slice 0): messages with Seen and Trial marks at their partition's epoch (D4), the
/// takeover of part of an outstanding multi-partition flush, the reset that keeps what is still held, and the
/// projector's rounds per virtual partition.
/// </summary>
public sealed class OwnershipSim(int count, int nodes, Fence fence, Mutation mutation, Random random)
{
    private sealed class VpRow { public string? Owner; public long Epoch, Lease, Position; }
    private sealed class RangeRow { public int Lo, Hi; public string? Owner; public long Epoch, Lease; }
    private sealed class Node(string name)
    {
        public string Name = name;
        public bool Alive = true;
        public int Incarnation;
        public List<Ownership> Held = [];
        public HashSet<Ownership> Doubtful = [];    // pictures whose last commit answer was lost: each range doubts on its own
        public HashSet<int> Touched = [];
        public int InFlight;           // commits sent and not yet executed
        public Dictionary<Msg, Memo> Memory = [];          // (i) messages polled, under their partition's epoch then
        public HashSet<(int Vp, long Epoch)> Mine = [];   // (i) what this process took: the suspect test's set
    }
    /// <summary>(i) A message: the store's marks, and the model's ghost of who marked it and the true epoch of its trial.</summary>
    private sealed class Msg { public int Vp; public long Seen, Trial, TrialAt; public int Attempts, Lost; public string? MarkedBy; public bool Done; }
    private sealed class Memo(long epoch, bool suspect) { public long Epoch = epoch; public bool Suspect = suspect, Marked; }
    /// <summary>What a batch does to a message in memory: mark it seen (on trial, if a suspect), or consume it once marked.</summary>
    private sealed record Mark(Msg M, bool Trial, bool Consume);
    private sealed record InFlight(Node Node, Ownership Picture, List<int> Vps, List<Mark> Marks);
    private readonly List<InFlight> _inFlight = [];
    private readonly HashSet<Ownership> _busy = [];     // pictures with a commit in flight: one flush at a time per range, as the worker does

    private readonly VpRow[] _vps = Enumerable.Range(0, count).Select(_ => new VpRow()).ToArray();
    private readonly List<RangeRow> _ranges = [new RangeRow { Lo = 0, Hi = count }];
    private readonly long[] _position = new long[count];
    private readonly HashSet<(int Vp, long Position)> _journal = [];
    private readonly Node[] _nodes = Enumerable.Range(0, nodes).Select(i => new Node($"n{i}.0")).ToArray();
    private readonly List<Msg> _msgs = [];
    private readonly long[] _projected = new long[count];                   // (i) the projector's cursor per virtual partition
    private readonly HashSet<(int Vp, long Position)> _view = [];
    private readonly List<(int Vp, long From, long To)> _rounds = [];       // projector rounds read and not yet written
    private long _time = 1000, _epochs;
    private const long Lease = 10_000;
    public List<string> Faults { get; } = [];
    public Action<string>? Log { get; init; }
    public long Landed, Refused, Resets, Takes, Splits, Merges, Moved, FenceRowsTouched, RowsTaken;
    public long KeepResets, Witnesses, Marks, Trials, Attempts, Consumed, Rounds, SettleRounds;

    private bool Has(Mutation m) => (mutation & m) != 0;
    private static string Process(Node n) => n.Name;

    // ---- the store's side ------------------------------------------------------------------------------------------

    private Ownership? Take(VpRange range, Node node, IReadOnlySet<int>? only = null)
    {
        var o = new Ownership(Process(node), range);
        if (fence == Fence.PerVirtualPartition)
        {
            // A take keeps what it got: it skips the rows another owner holds live.
            bool Free(int vp) => (only is null || only.Contains(vp)) && _vps[vp] is var row && (row.Owner is null || row.Owner == o.Node || row.Lease < _time);
            // A take from another owner counts an attempt on what was on trial at that partition's epoch (D4). T1 checks
            // the epoch: the same test on the epoch the trial really ran under, whatever the mark recorded.
            foreach (var m in _msgs)
                if (!m.Done && range.Contains(m.Vp) && Free(m.Vp) && _vps[m.Vp] is { Owner: { } before } row && before != o.Node)
                {
                    if (m.Trial == row.Epoch) { m.Attempts++; Attempts++; }
                    if (m.TrialAt == row.Epoch) m.Lost++;
                }
            for (var vp = range.Lo; vp < range.Hi; vp++)
            {
                var row = _vps[vp];
                if (!Free(vp)) continue;
                (row.Owner, row.Epoch, row.Lease) = (o.Node, row.Epoch + 1, _time + Lease);
                (o.VpEpoch[vp], o.Position[vp]) = (row.Epoch, row.Position);
                node.Mine.Add((vp, row.Epoch));
            }
        }
        else
        {
            var row = _ranges.SingleOrDefault(r => r.Lo == range.Lo && r.Hi == range.Hi);
            if (row is null || (row.Owner is not null && row.Owner != o.Node && row.Lease >= _time)) return null;
            (row.Owner, row.Epoch, row.Lease) = (o.Node, ++_epochs, _time + Lease);
            o.Epoch = row.Epoch;
            for (var vp = range.Lo; vp < range.Hi; vp++) o.Position[vp] = _position[vp];
        }
        if (o.Position.Count == 0) return null;
        Takes++;
        RowsTaken += o.Position.Count;
        return o;
    }

    /// <summary>The fenced commit. Returns landed; writes the journal rows when it lands, and a fault when it should not have.</summary>
    private bool Commit(Ownership o, IReadOnlyList<int> vps, IReadOnlyList<Mark>? marks = null)
    {
        var byVp = vps.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        foreach (var vp in byVp.Keys)
            if (!o.Owns(vp) || (fence == Fence.PerVirtualPartition && !o.VpEpoch.ContainsKey(vp)))
                throw new InvalidOperationException($"the picture of {o.Node} for {o.Range} lacks virtual partition {vp}: positions {o.Position.Count}, epochs {o.VpEpoch.Count}, range count {o.Range.Count}");
        bool fenced, rightful;
        var write = byVp.Keys.ToList();
        if (fence == Fence.PerVirtualPartition)
        {
            if (Has(Mutation.FenceSubset)) write = write.Where(vp => _vps[vp].Owner == o.Node).ToList();
            fenced = write.Count > 0 && write.All(vp => (Has(Mutation.FenceWithoutEpoch) || _vps[vp].Epoch == o.VpEpoch[vp]) && (Has(Mutation.FenceWithoutPosition) || _vps[vp].Position == o.Position[vp]));
            rightful = write.All(vp => (_vps[vp].Owner == o.Node || _vps[vp].Owner is null) && _vps[vp].Epoch == o.VpEpoch[vp]);
            FenceRowsTouched += write.Count;
        }
        else
        {
            var row = _ranges.SingleOrDefault(r => r.Lo == o.Range.Lo);
            fenced = row is not null && (Has(Mutation.FenceWithoutEpoch) || row.Epoch == o.Epoch) && (Has(Mutation.FenceWithoutHi) || row.Hi == o.Range.Hi)
                && byVp.Keys.All(vp => Has(Mutation.FenceWithoutPosition) || _position[vp] == o.Position[vp]);
            rightful = row is not null && row.Owner == o.Node && row.Epoch == o.Epoch && row.Hi == o.Range.Hi && byVp.Keys.All(vp => row.Lo <= vp && vp < row.Hi);
            FenceRowsTouched += 1;
        }
        if (!fenced) { Refused++; return false; }
        if (!rightful) Faults.Add($"I1 {o.Node} landed a commit on {string.Join(",", byVp.Keys)} although it is not the owner at that epoch or bound");
        if (write.Count < byVp.Count) Faults.Add($"W1 a flush of {o.Node} over {string.Join(",", byVp.Keys)} landed on {string.Join(",", write)} only: one stale fence row must roll the whole flush back");
        foreach (var vp in write)
        {
            var k = byVp[vp];
            // The batch carries its journal rows at the positions the owner believes, as the store's batch does.
            var at = o.Position[vp];
            for (var i = 1; i <= k; i++)
                if (!_journal.Add((vp, at + i))) Faults.Add($"I1 two commits landed at position {at + i} of virtual partition {vp}");
            if (fence == Fence.PerVirtualPartition) { _vps[vp].Position = at + k; _vps[vp].Lease = _time + Lease; }
            else _position[vp] = at + k;
        }
        if (fence == Fence.PerRange) _ranges.Single(r => r.Lo == o.Range.Lo).Lease = _time + Lease;
        foreach (var (m, trial, consume) in marks ?? [])
        {
            if (!write.Contains(m.Vp)) continue;
            if (consume)
            {
                if (m.Done) Faults.Add($"C1 the message to {m.Vp} was consumed twice");
                (m.Done, m.Trial, m.TrialAt) = (true, 0, 0);
                Consumed++;
                continue;
            }
            // D4: a mark records the epoch the message's virtual partition had at the take.
            var epoch = Has(Mutation.SeenAtNodeEpoch) ? o.VpEpoch.Values.Max() : o.VpEpoch[m.Vp];
            (m.Seen, m.MarkedBy) = (epoch, o.Node);
            if (trial) { (m.Trial, m.TrialAt) = (epoch, o.VpEpoch[m.Vp]); Trials++; }
            Marks++;
        }
        Landed++;
        return true;
    }

    /// <summary>(i) A projector round's write: moves the partition's cursor only if it is still where the round read it.</summary>
    private void Project(int vp, long from, long to)
    {
        if (_projected[vp] != from && !Has(Mutation.ProjectorBlindWrite)) return;     // lost the race: lost whole, read again
        for (var p = from + 1; p <= to; p++)
            if (!_journal.Contains((vp, p)) || !_view.Add((vp, p))) Faults.Add($"P1 row {p} of virtual partition {vp} was projected {(_journal.Contains((vp, p)) ? "twice" : "without a commit")}");
        _projected[vp] = Has(Mutation.ProjectorJumps) ? _vps[vp].Position : to;
    }

    private void Beat(Ownership o, Node node)
    {
        if (fence == Fence.PerVirtualPartition)
            for (var vp = o.Range.Lo; vp < o.Range.Hi; vp++)
                if (_vps[vp].Owner == o.Node && (!Has(Mutation.BeatRenewsTouchedOnly) || node.Touched.Contains(vp))) _vps[vp].Lease = _time + Lease;
        else if (_ranges.SingleOrDefault(r => r.Lo == o.Range.Lo) is { } row && row.Epoch == o.Epoch && row.Hi == o.Range.Hi) row.Lease = _time + Lease;
    }

    private bool ReleaseTail(Ownership o, int at)
    {
        if (at <= o.Range.Lo || at >= o.Range.Hi) throw new ArgumentOutOfRangeException(nameof(at), $"{at} is not inside {o.Range}");
        if (fence == Fence.PerVirtualPartition)
        {
            for (var vp = at; vp < o.Range.Hi; vp++) if (o.Owns(vp) && _vps[vp].Owner != o.Node) return false;
            for (var vp = at; vp < o.Range.Hi; vp++) if (o.Owns(vp)) _vps[vp].Owner = null;
        }
        else
        {
            var row = _ranges.SingleOrDefault(r => r.Lo == o.Range.Lo && r.Hi == o.Range.Hi && r.Epoch == o.Epoch);
            if (row is null) return false;
            var hi = row.Hi;
            row.Hi = at;
            if (!Has(Mutation.SplitKeepsEpoch)) row.Epoch = ++_epochs;
            if (!Has(Mutation.SplitWithoutNewRow)) _ranges.Add(new RangeRow { Lo = at, Hi = hi, Epoch = ++_epochs });
            o.Epoch = row.Epoch;
            Splits++;
        }
        Moved += o.Range.Hi - at;
        for (var vp = at; vp < o.Range.Hi; vp++) { o.Position.Remove(vp); o.VpEpoch.Remove(vp); }
        o.Range = new VpRange(o.Range.Lo, at);
        return true;
    }

    private void Carve(VpRange want)
    {
        if (fence == Fence.PerVirtualPartition) return;
        var row = _ranges.SingleOrDefault(r => r.Lo <= want.Lo && r.Hi >= want.Hi && (r.Owner is null || r.Lease < _time));
        if (row is null) return;
        if (row.Owner is not null) row.Owner = null;      // an expired owner's row is free to carve
        foreach (var at in new[] { want.Lo, want.Hi })
        {
            if (at <= row.Lo || at >= row.Hi) continue;
            var rest = new RangeRow { Lo = at, Hi = row.Hi, Epoch = ++_epochs };
            row.Hi = at;
            row.Epoch = ++_epochs;
            _ranges.Add(rest);
            Splits++;
            if (at == want.Lo) row = rest;
        }
    }

    private bool Merge(Ownership first, Ownership second)
    {
        if (fence == Fence.PerRange)
        {
            var a = _ranges.SingleOrDefault(r => r.Lo == first.Range.Lo && r.Hi == first.Range.Hi && r.Epoch == first.Epoch);
            var b = _ranges.SingleOrDefault(r => r.Lo == second.Range.Lo && r.Hi == second.Range.Hi && r.Epoch == second.Epoch);
            if (a is null || b is null) return false;
            a.Hi = b.Hi;
            a.Epoch = ++_epochs;
            if (!Has(Mutation.MergeKeepsSecondRow)) _ranges.Remove(b);
            first.Epoch = a.Epoch;
            Merges++;
        }
        foreach (var (vp, p) in second.Position) first.Position[vp] = p;
        foreach (var (vp, e) in second.VpEpoch) first.VpEpoch[vp] = e;
        first.Range = first.Range.Merge(second.Range);
        return true;
    }

    /// <summary>What the heartbeat shows: who holds what, by the rows.</summary>
    private RangeMap Map()
    {
        var map = RangeMap.Fresh(count);
        if (fence == Fence.PerVirtualPartition)
        {
            for (var vp = 1; vp < count; vp++) if (Live(_vps[vp]) != Live(_vps[vp - 1])) map = map.Split(vp);
            foreach (var e in map.Entries) map = map.Assign(e.Range.Lo, Live(_vps[e.Range.Lo]));
            return map;
        }
        foreach (var r in _ranges.OrderBy(r => r.Lo)) { if (r.Lo > 0) map = map.Split(r.Lo); }
        foreach (var r in _ranges) map = map.Assign(r.Lo, r.Owner is not null && r.Lease >= _time ? r.Owner : null);
        return map;
        string? Live(VpRow row) => row.Owner is not null && row.Lease >= _time ? row.Owner : null;
    }

    // ---- the nodes' side ---------------------------------------------------------------------------------------------

    /// <summary>A node takes a range: whatever it still pictured over those virtual partitions is stale once the take bumps their epochs.</summary>
    private void Acquire(Node node, VpRange range)
    {
        if (fence == Fence.PerVirtualPartition)
        {
            // (i) The take re-reads only the partitions it takes: they leave the pictures that hold them, the rest stays,
            // and what the take got fills a hole of the picture whose range contains it, or is a picture of its own.
            foreach (var p in node.Held.ToList())
            {
                foreach (var vp in p.Position.Keys.Where(range.Contains).ToList()) { p.Position.Remove(vp); p.VpEpoch.Remove(vp); }
                if (p.Position.Count == 0) { node.Held.Remove(p); node.Doubtful.Remove(p); }
            }
            if (Take(range, node) is not { } got) return;
            if (node.Held.FirstOrDefault(p => p.Range.Lo <= range.Lo && range.Hi <= p.Range.Hi) is { } holder)
                foreach (var (vp, e) in got.VpEpoch) (holder.VpEpoch[vp], holder.Position[vp]) = (e, got.Position[vp]);
            else node.Held.Add(got);
            return;
        }
        node.Held.RemoveAll(o => o.Range.Lo < range.Hi && range.Lo < o.Range.Hi);
        node.Doubtful.RemoveWhere(d => d.Range.Lo < range.Hi && range.Lo < d.Range.Hi);
        if (Take(range, node) is { } taken) node.Held.Add(taken);
    }

    private void Reset(Node node, Ownership o)
    {
        node.Held.Remove(o);
        node.Doubtful.Remove(o);
        Resets++;
        if (fence == Fence.PerRange) { Acquire(node, o.Range); return; }
        // (i) Re-take, one partition at a time and each at a new epoch, those of the picture still mine or free; drop the rest.
        if (Take(o.Range, node, o.Position.Keys.ToHashSet()) is { } again) node.Held.Add(again);
        Served(node, "a reset");
    }

    /// <summary>(i) L1: what a live process holds live in the store, it serves in a picture at the store's epoch.</summary>
    private int Unserved(Node node) => !node.Alive ? 0 : Enumerable.Range(0, count).Count(vp => _vps[vp] is var row && row.Owner == node.Name
        && row.Lease >= _time && !node.Held.Any(p => p.VpEpoch.TryGetValue(vp, out var e) && e == row.Epoch));

    private void Served(Node node, string after)
    {
        if (Unserved(node) is var n and > 0) Faults.Add($"L1 liveness: after {after} {node.Name} holds {n} virtual partitions live in the store that it serves in no picture");
    }

    /// <summary>
    /// (i) After a refused flush: keep from the store what is still mine at the epochs I hold (one statement, as a beat that
    /// returns id, owner, epoch and position, renewing them), drop the rest, take no row. The kept partitions' memory is dropped and polled again,
    /// reading what I marked at their epoch too: the poll's Seen &lt; epoch would leave it for no one.
    /// </summary>
    private void KeepReset(Node node, Ownership o)
    {
        if (fence == Fence.PerRange || Has(Mutation.ResetAll)) { Reset(node, o); return; }
        node.Doubtful.Remove(o);
        KeepResets++;
        foreach (var vp in o.Position.Keys.ToList())
            if (_vps[vp] is var row && row.Owner == o.Node && row.Epoch == o.VpEpoch[vp]) (o.Position[vp], row.Lease) = (row.Position, _time + Lease);
            else { o.Position.Remove(vp); o.VpEpoch.Remove(vp); }
        if (o.Position.Count == 0) node.Held.Remove(o);
        foreach (var m in node.Memory.Keys.Where(m => o.Owns(m.Vp)).ToList()) node.Memory.Remove(m);
        Poll(node, o, again: !Has(Mutation.ResetPollsSeenBelow));
        Served(node, "a keeping reset");
    }

    /// <summary>(i) The poll (D4): what is not done and was not seen at its partition's epoch; <paramref name="again"/>: or was.</summary>
    private void Poll(Node node, Ownership o, bool again = false)
    {
        foreach (var m in _msgs)
        {
            if (m.Done || !o.Owns(m.Vp) || !o.VpEpoch.TryGetValue(m.Vp, out var epoch) || m.Seen > epoch || (m.Seen == epoch && !again)) continue;
            if (node.Memory.TryGetValue(m, out var memo) && memo.Epoch == epoch) continue;
            var suspect = m.Trial > 0 || (m.Seen > 0 && !node.Mine.Contains((m.Vp, m.Seen)));
            var truth = m.TrialAt > 0 || (m.MarkedBy is not null && m.MarkedBy != node.Name);
            if (suspect != truth) Faults.Add($"S2 {node.Name} polled the message to {m.Vp} as {(suspect ? "" : "not ")}suspect: seen at {m.Seen}, on trial at {m.Trial}, marked by {m.MarkedBy}");
            node.Memory[m] = new Memo(epoch, suspect);
        }
    }

    /// <summary>(i) The marks a flush over <paramref name="vps"/> carries for what is in memory under the picture's epochs.</summary>
    private List<Mark> MarksOf(Node node, Ownership o, List<int> vps) =>
        [.. node.Memory.Where(e => vps.Contains(e.Key.Vp) && o.VpEpoch.TryGetValue(e.Key.Vp, out var ep) && e.Value.Epoch == ep).Select(e => new Mark(e.Key, e.Value.Suspect, e.Value.Marked))];

    private static Ownership Snapshot(Ownership o)
    {
        var copy = new Ownership(o.Node, o.Range) { Epoch = o.Epoch };
        foreach (var (vp, p) in o.Position) copy.Position[vp] = p;
        foreach (var (vp, e) in o.VpEpoch) copy.VpEpoch[vp] = e;
        return copy;
    }

    private bool SameEpochs(Ownership a, Ownership b, IEnumerable<int> vps) =>
        fence == Fence.PerRange ? a.Epoch == b.Epoch && a.Range == b.Range : vps.Where(a.VpEpoch.ContainsKey).ToList() is { Count: > 0 } held && held.All(vp => b.VpEpoch.TryGetValue(vp, out var f) && a.VpEpoch[vp] == f);

    /// <summary>What the node does with a commit's answer: moves its positions, or doubts (a lost answer), or resets.</summary>
    private void Answer(Node node, Ownership current, Ownership sent, bool landed, List<int> vps, List<Mark> marks)
    {
        var lost = landed && random.Next(8) == 0;
        Log?.Invoke($"    answer: {(landed ? lost ? "landed, answer lost" : "landed" : "refused")}");
        if (landed && !lost)
        {
            foreach (var (vp, k) in vps.GroupBy(v => v).Select(g => (g.Key, g.Count()))) if (current.Position.ContainsKey(vp)) current.Position[vp] = sent.Position[vp] + k;
            node.Touched = [.. vps];
            foreach (var (m, _, consume) in marks)
                if (node.Memory.TryGetValue(m, out var memo) && memo.Epoch == sent.VpEpoch[m.Vp]) { if (consume) node.Memory.Remove(m); else memo.Marked = true; }
        }
        else if (lost) node.Doubtful.Add(current);
        else if (!Has(Mutation.RetryAfterLostAnswer) || !node.Doubtful.Contains(current)) KeepReset(node, current);
    }

    /// <summary>A commit sent earlier executes now, and its answer reaches the node if it still has that picture.</summary>
    private (bool Landed, Ownership? Current) Land(InFlight sent)
    {
        _inFlight.Remove(sent);
        sent.Node.InFlight--;
        var landed = Commit(sent.Picture, sent.Vps, sent.Marks);
        Log?.Invoke($"    in-flight commit of {sent.Node.Name} [{string.Join(",", sent.Vps)}] from {sent.Picture.Range} e{sent.Picture.Epoch} at {string.Join(",", sent.Vps.Distinct().Select(v => $"{v}@{sent.Picture.Position[v]}"))}: {(landed ? "landed" : "refused")}");
        // The node's current picture of that range, if it still has one at the same epoch(s); a crashed node's is gone.
        var current = sent.Node.Alive ? sent.Node.Held.FirstOrDefault(p => p.Range.Lo == sent.Picture.Range.Lo && SameEpochs(p, sent.Picture, sent.Vps)) : null;
        _busy.RemoveWhere(p => p.Node == sent.Picture.Node && p.Range.Lo == sent.Picture.Range.Lo);
        if (current is not null) Answer(sent.Node, current, sent.Picture, landed, sent.Vps, sent.Marks);
        return (landed, current);
    }

    private void Act(int kind)
    {
        var node = _nodes[random.Next(nodes)];
        Log?.Invoke($"{kind} {node.Name}{(node.Alive ? "" : " (dead)")} held [{string.Join(" ", node.Held.Select(o => $"{o.Range}{(_busy.Contains(o) ? "*" : "")}{(node.Doubtful.Contains(o) ? "?" : "")}"))}] t={_time}");
        switch (kind)
        {
            case 0: _time += random.Next(1, 4000); break;
            case 1 when node.Alive && node.Held.Count > 0:
            {
                var o = node.Held[random.Next(node.Held.Count)];
                if (_busy.Contains(o)) break;
                if (node.Doubtful.Contains(o) && !Has(Mutation.RetryAfterLostAnswer)) { Reset(node, o); break; }
                var k = new[] { 1, 3, 10 }[random.Next(3)];
                var vps = Enumerable.Range(0, k).Select(_ => o.Range.Lo + random.Next(o.Range.Count)).Where(o.Owns).ToList();
                // (i) The partitions with messages in memory are dirty too: their turns mark or consume them.
                if (fence == Fence.PerVirtualPartition) vps.AddRange(node.Memory.Where(e => o.Owns(e.Key.Vp) && e.Value.Epoch == o.VpEpoch[e.Key.Vp]).Select(e => e.Key.Vp));
                if (vps.Count == 0) break;
                var marks = MarksOf(node, o, vps);
                if (random.Next(4) == 0)
                {
                    // Sent, executed later (case 8): the store sees the picture as it was when the batch was sent.
                    _inFlight.Add(new InFlight(node, Snapshot(o), vps, marks));
                    _busy.Add(o);
                    node.InFlight++;
                    break;
                }
                var landedNow = Commit(o, vps, marks);
                Log?.Invoke($"    commit [{string.Join(",", vps)}] from {o.Range} e{o.Epoch} at {string.Join(",", vps.Distinct().Select(v => $"{v}@{o.Position[v]}"))}: {(landedNow ? "landed" : "refused")}");
                Answer(node, o, o, landedNow, vps, marks);
                break;
            }
            case 8 when _inFlight.Count > 0: Land(_inFlight[0]); break;
            case 2 when node.Alive: foreach (var o in node.Held) Beat(o, node); break;
            case 3 when node.Alive:
            {
                var mayReshape = node.InFlight == 0 || Has(Mutation.SplitDuringFlush);
                // Balance toward the fair share of the live nodes: release a tail of a range that is not all mine to keep,
                // or take a free range after carving it.
                var live = _nodes.Where(n => n.Alive).Select(Process).ToList();
                var map = Map();
                var moves = random.Next(2) == 0 ? map.Moves(live) : map.StealMoves(live);
                bool Tail(Ownership o, RangeMap.Move m) => m.Range.Hi == o.Range.Hi && m.Range.Lo > o.Range.Lo && m.Range.Lo < o.Range.Hi;
                var mine = moves.FirstOrDefault(m => m.From == Process(node) && node.Held.Any(o => Tail(o, m)));
                if (mine is not null) { if (!mayReshape) break; var o = node.Held.First(o => Tail(o, mine)); if (!ReleaseTail(o, mine.Range.Lo)) Reset(node, o); else if (o.Position.Count == 0) node.Held.Remove(o); break; }
                var take = moves.FirstOrDefault(m => m.To == Process(node) && m.From is null) ?? moves.FirstOrDefault(m => m.To == Process(node) && m.From is not null && !live.Contains(m.From));
                if (take is not null) { Carve(take.Range); Acquire(node, take.Range); }
                break;
            }
            case 4 when node.Alive && node.Held.Count > 1 && (node.InFlight == 0 || Has(Mutation.SplitDuringFlush)):
            {
                // A doubtful picture resets before it takes part in anything: a merge would carry its stale positions along.
                var sorted = node.Held.OrderBy(o => o.Range.Lo).ToList();
                for (var i = 0; i + 1 < sorted.Count; i++)
                    if (sorted[i].Range.Hi == sorted[i + 1].Range.Lo)
                    {
                        if (node.Doubtful.Contains(sorted[i])) { Reset(node, sorted[i]); break; }
                        if (node.Doubtful.Contains(sorted[i + 1])) { Reset(node, sorted[i + 1]); break; }
                        if (Merge(sorted[i], sorted[i + 1])) node.Held.Remove(sorted[i + 1]);
                        break;
                    }
                break;
            }
            case 5 when node.Alive && random.Next(6) == 0: node.Alive = false; node.Held.Clear(); node.Doubtful.Clear(); node.Memory.Clear(); node.Mine.Clear(); break;
            case 6 when !node.Alive: node.Alive = true; node.Name = $"n{Array.IndexOf(_nodes, node)}.{++node.Incarnation}"; break;
            case 7 when node.Alive:
            {
                // Take over what expired, as a whole range row (ii) or as the maximal run of expired rows (i).
                var map = Map();
                var free = map.Entries.Where(e => e.Owner is null).ToList();
                if (free.Count == 0) break;
                var pick = free[random.Next(free.Count)].Range;
                Carve(pick);
                Acquire(node, pick);
                break;
            }
            case 9 when fence == Fence.PerVirtualPartition:
                _msgs.Add(new Msg { Vp = random.Next(count) });
                if (node.Alive && node.Held.Count > 0) Poll(node, node.Held[random.Next(node.Held.Count)]);
                break;
            case 10 when fence == Fence.PerVirtualPartition:
            {
                // K1's witness: part of an outstanding multi-partition flush is taken over, then the flush tries to land.
                var flushes = _inFlight.Where(f => f.Node.Alive && f.Node.Name == f.Picture.Node).ToList();
                if (flushes.Count == 0) break;
                var sent = flushes[random.Next(flushes.Count)];
                var spanned = sent.Vps.Distinct().Where(vp => _vps[vp].Owner == sent.Picture.Node && _vps[vp].Epoch == sent.Picture.VpEpoch[vp]).Order().ToList();
                var takers = _nodes.Where(n => n.Alive && n.Name != sent.Picture.Node).ToList();
                if (spanned.Count < 2 || takers.Count == 0) break;
                // Some, never all, move: by lease expiry (a late beat; commits renewed the rest), or by a cooperative release.
                var moved = spanned.OrderBy(_ => random.Next()).Take(1 + random.Next(spanned.Count - 1)).ToList();
                var release = random.Next(2) == 0;
                foreach (var vp in moved)
                {
                    if (!release) _vps[vp].Lease = _time - 1;
                    else if (sent.Node.Held.FirstOrDefault(p => p.VpEpoch.TryGetValue(vp, out var e) && e == _vps[vp].Epoch) is { } p)
                    {
                        (_vps[vp].Owner, Moved) = (null, Moved + 1);
                        p.Position.Remove(vp);
                        p.VpEpoch.Remove(vp);
                        if (p.Position.Count == 0) sent.Node.Held.Remove(p);
                    }
                    Acquire(takers[random.Next(takers.Count)], new VpRange(vp, vp + 1));
                }
                Witnesses++;
                var (landed, current) = Land(sent);
                if (landed) Faults.Add($"W1 the flush of {sent.Node.Name} over {string.Join(",", spanned)} landed after {string.Join(",", moved)} moved");
                // W3: what the old owner still holds it serves, at the store's epoch and position. W4 (cost): the reset took
                // none of it again. (A picture still in doubt under RetryAfterLostAnswer did not reset; the position check is its backstop.)
                if (current is not null && sent.Node.Alive && !(Has(Mutation.RetryAfterLostAnswer) && sent.Node.Doubtful.Contains(current)))
                    foreach (var vp in spanned.Except(moved).Where(vp => _vps[vp].Owner == sent.Node.Name))
                        if (!sent.Node.Held.Any(p => p.VpEpoch.TryGetValue(vp, out var e) && e == _vps[vp].Epoch && p.Position[vp] == _vps[vp].Position))
                            Faults.Add($"W3 liveness: after the partial fence failure {sent.Node.Name} does not serve virtual partition {vp}, which it still holds");
                        else if (_vps[vp].Epoch != sent.Picture.VpEpoch[vp])
                            Faults.Add($"W4 cost: after the partial fence failure {sent.Node.Name} took virtual partition {vp} again (epoch {sent.Picture.VpEpoch[vp]} to {_vps[vp].Epoch}) where it could keep it");
                break;
            }
            case 11 when fence == Fence.PerVirtualPartition:
            {
                // A projector round on one virtual partition, on any node: read cursor and position, write now or later.
                if (_rounds.Count > 0 && random.Next(2) == 0)
                {
                    var (vp, from, to) = _rounds[0];
                    _rounds.RemoveAt(0);
                    if (random.Next(8) != 0) Project(vp, from, to);   // else its process died between the read and the write
                    break;
                }
                var dirty = Enumerable.Range(0, count).Where(v => _projected[v] < _vps[v].Position).ToList();
                if (dirty.Count == 0) break;
                var at = dirty[random.Next(dirty.Count)];
                var round = (at, _projected[at], Math.Min(_vps[at].Position, _projected[at] + 4));
                Rounds++;
                if (random.Next(2) == 0) _rounds.Add(round);
                else Project(round.at, round.Item2, round.Item3);
                break;
            }
        }
    }

    private string? Check()
    {
        if (Faults.Count > 0) return Faults[0];
        for (var vp = 0; vp < count; vp++)
        {
            var position = fence == Fence.PerVirtualPartition ? _vps[vp].Position : _position[vp];
            for (var p = 1; p <= position; p++) if (!_journal.Contains((vp, p))) return $"I6 virtual partition {vp} has no journal row at {p} of {position}";
        }
        foreach (var m in _msgs)
        {
            if (m.Attempts != m.Lost) return $"T1 the message to {m.Vp} counts {m.Attempts} attempts; the takes that found it on trial at the epoch the trial really ran under number {m.Lost}";
            if (m.Done) continue;
            var row = _vps[m.Vp];
            if (m.Seen > row.Epoch) return $"S1 the message to {m.Vp} is marked seen at {m.Seen}, above its partition's epoch {row.Epoch}: no owner polls it";
            if (m.Seen == row.Epoch && m.Seen > 0 && _nodes.FirstOrDefault(n => n.Alive && n.Name == row.Owner) is { } owner
                && owner.Held.Any(p => p.VpEpoch.TryGetValue(m.Vp, out var e) && e == row.Epoch)
                && !(owner.Memory.TryGetValue(m, out var memo) && memo.Epoch == row.Epoch))
                return $"S1 the message to {m.Vp} is marked seen at {m.Seen}, its partition's epoch, and {owner.Name}, which holds it there, does not have it in memory: it is stranded";
        }
        if (fence == Fence.PerRange)
        {
            var sorted = _ranges.OrderBy(r => r.Lo).ToList();
            if (sorted[0].Lo != 0 || sorted[^1].Hi != count) return "cover: the ranges do not cover the virtual partitions";
            for (var i = 0; i + 1 < sorted.Count; i++) if (sorted[i].Hi != sorted[i + 1].Lo) return $"cover: ranges [{sorted[i].Lo},{sorted[i].Hi}) and [{sorted[i + 1].Lo},{sorted[i + 1].Hi}) overlap or leave a gap";
        }
        return null;
    }

    /// <summary>One seed: <paramref name="actions"/> actions, then the system settles; null when every check held.</summary>
    public string? Run(int actions)
    {
        foreach (var node in _nodes) Act(3);
        for (var i = 0; i < actions; i++)
        {
            Act(random.Next(fence == Fence.PerVirtualPartition ? 12 : 9));
            if (Check() is { } violation) return $"{violation} (after action {i + 1})";
        }
        // Settle: what is in flight executes, every node is alive, whatever is free or expired is taken, every held range commits once more.
        while (_inFlight.Count > 0) Act(8);
        foreach (var node in _nodes) if (!node.Alive) Act(6);
        for (var round = 0; round < 50; round++) foreach (var node in _nodes) Act(7);
        // A picture may be stale (its lease ran out and another node took the range): a refused commit resets it, as the
        // kernel does, and the take drops it when another node holds the range now. What is held after that must commit.
        foreach (var node in _nodes)
            foreach (var o in node.Held.ToList())
                if (!node.Held.Contains(o)) continue;       // (i) a take of another picture's range emptied it
                else if (node.Doubtful.Contains(o)) Reset(node, o);
                else if (!Commit(o, [First(o)])) { if (fence == Fence.PerVirtualPartition) KeepReset(node, o); else Reset(node, o); }
                else o.Position[First(o)]++;
        foreach (var node in _nodes)
            foreach (var o in node.Held)
            {
                if (!Commit(o, [First(o)])) return $"liveness: {node.Name} cannot commit on {o.Range} it holds";
                o.Position[First(o)]++;
            }
        var covered = Map().Entries.Where(e => e.Owner is null).Sum(e => e.Range.Count);
        // (i) A row a live process owns and does not serve is as uncovered as a free one (a dead process's rows wait for their lease).
        if (fence == Fence.PerVirtualPartition)
            covered += _nodes.Sum(Unserved);
        // (i) The projector catches up: what was read lands, then rounds per virtual partition until every cursor is at its position.
        foreach (var (vp, from, to) in _rounds) Project(vp, from, to);
        if (fence == Fence.PerVirtualPartition)
            for (var vp = 0; vp < count; vp++)
                for (; _projected[vp] < _vps[vp].Position; SettleRounds++) Project(vp, _projected[vp], Math.Min(_vps[vp].Position, _projected[vp] + 4));
        return Check() ?? (covered > 0 ? $"liveness: {covered} virtual partitions have no owner that serves them after settling" : null)
            ?? (_view.Count < (fence == Fence.PerVirtualPartition ? _journal.Count : 0) ? $"P3 liveness: {_journal.Count - _view.Count} journal rows were never projected" : null);
    }

    /// <summary>A virtual partition the picture holds: its lowest bound, unless a reset that kept the rest dropped it.</summary>
    private static int First(Ownership o) => o.Owns(o.Range.Lo) ? o.Range.Lo : o.Position.Keys.Min();

    public static int Sweep(int seeds, int actions, int count, int nodes, Fence fence, Mutation mutation = Mutation.None, bool quiet = false)
    {
        long landed = 0, refused = 0, resets = 0, takes = 0, splits = 0, merges = 0, moved = 0, fenceRows = 0, rowsTaken = 0;
        long keepResets = 0, witnesses = 0, marks = 0, trials = 0, attempts = 0, consumed = 0, rounds = 0, settleRounds = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var seed = 1; seed <= seeds; seed++)
        {
            var sim = new OwnershipSim(count, nodes, fence, mutation, new Random(seed));
            if (sim.Run(actions) is { } violation)
            {
                if (!quiet) Console.WriteLine($"{Name(fence)}, {count}: seed {seed} FAILED: {violation}");
                return seed;
            }
            landed += sim.Landed; refused += sim.Refused; resets += sim.Resets; takes += sim.Takes; splits += sim.Splits; merges += sim.Merges; moved += sim.Moved; fenceRows += sim.FenceRowsTouched; rowsTaken += sim.RowsTaken;
            keepResets += sim.KeepResets; witnesses += sim.Witnesses; marks += sim.Marks; trials += sim.Trials; attempts += sim.Attempts; consumed += sim.Consumed; rounds += sim.Rounds; settleRounds += sim.SettleRounds;
        }
        if (!quiet)
            Console.WriteLine($"{Name(fence)}, {count} vps, {nodes} nodes: {seeds} seeds of {actions} actions, {watch.Elapsed.TotalSeconds:F1} s, 0 failed; per seed: {landed / (double)seeds:F1} commits landed, {refused / (double)seeds:F1} refused, {resets / (double)seeds:F1} resets, {takes / (double)seeds:F1} takes of {rowsTaken / Math.Max(1.0, takes):F0} rows, {splits / (double)seeds:F1} splits, {merges / (double)seeds:F1} merges, {moved / (double)seeds:F0} vps moved; fence rows per commit {fenceRows / Math.Max(1.0, landed + refused):F2}"
                + (fence == Fence.PerRange ? "" : $"; {keepResets / (double)seeds:F1} resets keeping the rest, {witnesses / (double)seeds:F1} partial takeovers of a flush in flight, {marks / (double)seeds:F1} marks ({trials / (double)seeds:F2} trials, {attempts / (double)seeds:F2} attempts counted), {consumed / (double)seeds:F1} consumed, projector rounds {rounds / (double)seeds:F1} and {settleRounds / (double)seeds:F1} to catch up"));
        return 0;
    }

    public static int Replay(int seed, int actions, int count, int nodes, Fence fence, Mutation mutation)
    {
        var sim = new OwnershipSim(count, nodes, fence, mutation, new Random(seed)) { Log = Console.WriteLine };
        var violation = sim.Run(actions);
        Console.WriteLine(violation ?? "no violation");
        return violation is null ? 0 : 1;
    }

    public static string Name(Fence fence) => fence == Fence.PerVirtualPartition ? "(i) per virtual partition" : "(ii) per range";

    /// <summary>The mutations each variant runs, and whether each must be caught (its backstop is listed where it is not).</summary>
    public static IEnumerable<(Mutation Mutation, bool MustBeCaught, string Note)> MutationsOf(Fence fence)
    {
        yield return (Mutation.FenceWithoutEpoch, true, "");
        yield return (Mutation.FenceWithoutPosition, false, "a lost answer is a reset, so the epoch catches the retry");
        yield return (Mutation.RetryAfterLostAnswer, false, "the position check refuses the retry");
        yield return (Mutation.FenceWithoutPosition | Mutation.RetryAfterLostAnswer, true, "");
        if (fence == Fence.PerRange)
        {
            yield return (Mutation.SplitDuringFlush, false, "the split's new epoch refuses the commit in flight; the owner resets");
            yield return (Mutation.SplitDuringFlush | Mutation.SplitKeepsEpoch, false, "the bound in the fence refuses the commit in flight");
            yield return (Mutation.SplitDuringFlush | Mutation.FenceWithoutHi, false, "the split's new epoch refuses the commit in flight");
            yield return (Mutation.SplitDuringFlush | Mutation.FenceWithoutHi | Mutation.SplitKeepsEpoch, true, "");
            yield return (Mutation.FenceWithoutHi | Mutation.SplitKeepsEpoch, false, "an owner that splits between flushes knows its own split");
            yield return (Mutation.SplitWithoutNewRow, true, "");
            yield return (Mutation.MergeKeepsSecondRow, true, "");
        }
        else
        {
            yield return (Mutation.SplitDuringFlush, false, "a release changes no kept row; a commit landing on a freed row before its take moves the position the take reads");
            yield return (Mutation.BeatRenewsTouchedOnly, false, "availability only: idle rows expire and are taken, the owner resets");
            yield return (Mutation.FenceSubset, true, "");
            yield return (Mutation.SeenAtNodeEpoch, true, "");
            yield return (Mutation.ResetAll, true, "");       // safe; caught as a cost (W4): the kept partitions are taken again
            yield return (Mutation.ResetPollsSeenBelow, true, "");
            yield return (Mutation.ProjectorBlindWrite, true, "");
            yield return (Mutation.ProjectorJumps, true, "");
        }
    }

    /// <summary>Every mutation at both counts: which seed first catches it, or that none did; the verdict against the expectation.</summary>
    public static int Mutations(int seeds, int actions, int nodes)
    {
        var wrong = 0;
        foreach (var fence in new[] { Fence.PerVirtualPartition, Fence.PerRange })
            foreach (var (mutation, mustBeCaught, note) in MutationsOf(fence))
                foreach (var count in new[] { VirtualPartitions.Small, VirtualPartitions.Large })
                {
                    var seed = Sweep(seeds, actions, count, nodes, fence, mutation, quiet: true);
                    var asExpected = (seed > 0) == mustBeCaught;
                    Console.WriteLine($"{Name(fence)}, {count}: {mutation}: {(seed > 0 ? $"CAUGHT at seed {seed}" : "not caught")}{(mustBeCaught ? "" : $" (expected: {note})")}{(asExpected ? "" : "  <-- NOT AS EXPECTED")}");
                    if (!asExpected) wrong++;
                }
        Console.WriteLine(wrong == 0 ? "every mutation run gave the expected verdict" : $"{wrong} mutation run(s) did not give the expected verdict");
        return wrong;
    }
}
