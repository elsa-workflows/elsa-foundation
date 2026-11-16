using System.Collections.Immutable;

namespace Kernel.Runtime;

/// <summary>
/// Round 5 A (owner, 15 November): virtual partitions. The database has a fixed number of them, 1,024 or 4,096 (4,096
/// provisionally), and an actor's address embeds its virtual partition exactly as it embeds a partition today
/// ("3071/order-17"): the same <see cref="InstanceRef.Spread"/>, so placement is stable, and the count is fixed when
/// the database is created, so nothing is ever rehashed. Nodes own contiguous <em>ranges</em> of virtual partitions
/// (<see cref="VpRange"/>, <see cref="RangeMap"/>): a range moves between nodes as a whole or is split; a virtual
/// partition never changes its number, so its journal keeps its identity across every move. Which row carries the
/// fence, one per virtual partition or one per owned range, is the experiment of round 5 A
/// (<c>experiments/VirtualPartitions</c>); this file is only addressing and placement, and nothing in the kernel uses
/// it yet: the built-in runtime still hosts <see cref="Defaults.ServerPartitions"/> partitions.
/// </summary>
public static class VirtualPartitions
{
    /// <summary>The two counts under comparison.</summary>
    public const int Small = 1024, Large = 4096;

    /// <summary>The owner's provisional choice (15 November), pending the measurements.</summary>
    public const int Provisional = Large;

    /// <summary>The virtual partition of a name: a pure function of the name and the count, never of the nodes.</summary>
    public static int Of(string name, int count) => InstanceRef.Spread(name, count);

    /// <summary>
    /// The fair share as ranges: <paramref name="nodes"/> contiguous ranges that cover <c>[0, count)</c>, the first
    /// <c>count mod nodes</c> one longer. The same on every node for the same count and node count.
    /// </summary>
    public static ImmutableArray<VpRange> FairRanges(int count, int nodes)
    {
        if (count <= 0 || nodes <= 0) throw new ArgumentOutOfRangeException(nameof(nodes));
        var (share, extra) = Math.DivRem(count, nodes);
        var ranges = ImmutableArray.CreateBuilder<VpRange>(nodes);
        for (int node = 0, lo = 0; node < nodes && lo < count; node++)
        {
            var hi = lo + share + (node < extra ? 1 : 0);
            ranges.Add(new VpRange(lo, hi));
            lo = hi;
        }
        return ranges.ToImmutable();
    }
}

/// <summary>A closed-open range of virtual partitions <c>[Lo, Hi)</c>.</summary>
public readonly record struct VpRange(int Lo, int Hi)
{
    public int Count => Hi - Lo;
    public bool IsEmpty => Hi <= Lo;
    public bool Contains(int vp) => vp >= Lo && vp < Hi;
    public bool Adjacent(VpRange other) => Hi == other.Lo || other.Hi == Lo;

    /// <summary>Two ranges, <c>[Lo, at)</c> and <c>[at, Hi)</c>; <paramref name="at"/> strictly inside.</summary>
    public (VpRange Left, VpRange Right) Split(int at) =>
        at > Lo && at < Hi ? (new(Lo, at), new(at, Hi)) : throw new ArgumentOutOfRangeException(nameof(at), $"{at} is not strictly inside {this}");

    /// <summary>The union of two adjacent ranges.</summary>
    public VpRange Merge(VpRange other) =>
        Adjacent(other) ? new(Math.Min(Lo, other.Lo), Math.Max(Hi, other.Hi)) : throw new ArgumentException($"{this} and {other} are not adjacent", nameof(other));

    public override string ToString() => $"[{Lo}, {Hi})";
}

/// <summary>
/// Who owns which range: the placement table of a cluster, as any node computes it from the same facts. Ranges are
/// sorted, do not overlap and cover <c>[0, Count)</c>; an owner of null is a range nobody holds. The operations are the
/// ones a rebalance needs, each returning the new map: <see cref="Split"/>, <see cref="Merge"/> (adjacent ranges of one
/// owner), <see cref="Assign"/>, and <see cref="Moves"/>, the splits and reassignments that take the map to a fair
/// share. Pure and immutable: the experiment's two stores apply the same moves as rows.
/// </summary>
public sealed class RangeMap
{
    public sealed record Entry(VpRange Range, string? Owner);

    public ImmutableArray<Entry> Entries { get; }
    public int Count { get; }

    private RangeMap(ImmutableArray<Entry> entries, int count)
    {
        Entries = entries;
        Count = count;
        Check();
    }

    /// <summary>One range, nobody's: a fresh database.</summary>
    public static RangeMap Fresh(int count) => new([new(new VpRange(0, count), null)], count);

    /// <summary>The fair share of <paramref name="nodes"/>, each named by its index.</summary>
    public static RangeMap Fair(int count, IReadOnlyList<string> nodes) =>
        new(VirtualPartitions.FairRanges(count, nodes.Count).Select((r, i) => new Entry(r, nodes[i])).ToImmutableArray(), count);

    public Entry EntryOf(int vp) => Entries[IndexOf(vp)];
    public string? OwnerOf(int vp) => EntryOf(vp).Owner;
    public IEnumerable<VpRange> RangesOf(string owner) => Entries.Where(e => e.Owner == owner).Select(e => e.Range);
    public int Owned(string owner) => RangesOf(owner).Sum(r => r.Count);

    private int IndexOf(int vp)
    {
        if (vp < 0 || vp >= Count) throw new ArgumentOutOfRangeException(nameof(vp));
        int lo = 0, hi = Entries.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (Entries[mid].Range.Hi <= vp) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>Split the range that contains <paramref name="at"/> into two with the same owner. A boundary already there changes nothing.</summary>
    public RangeMap Split(int at)
    {
        var i = IndexOf(at);
        var entry = Entries[i];
        if (entry.Range.Lo == at) return this;
        var (left, right) = entry.Range.Split(at);
        return new(Entries.RemoveAt(i).Insert(i, new Entry(left, entry.Owner)).Insert(i + 1, new Entry(right, entry.Owner)), Count);
    }

    /// <summary>Merge the range starting at <paramref name="lo"/> with the next one; both must have the same owner.</summary>
    public RangeMap Merge(int lo)
    {
        var i = IndexOf(lo);
        if (Entries[i].Range.Lo != lo || i + 1 >= Entries.Length) throw new ArgumentException($"no range starts at {lo} with a successor", nameof(lo));
        var (a, b) = (Entries[i], Entries[i + 1]);
        if (a.Owner != b.Owner) throw new InvalidOperationException($"{a.Range} ({a.Owner ?? "free"}) and {b.Range} ({b.Owner ?? "free"}) have different owners");
        return new(Entries.RemoveAt(i + 1).SetItem(i, new Entry(a.Range.Merge(b.Range), a.Owner)), Count);
    }

    /// <summary>Give the range starting at <paramref name="lo"/> to <paramref name="owner"/> (null: free it).</summary>
    public RangeMap Assign(int lo, string? owner)
    {
        var i = IndexOf(lo);
        if (Entries[i].Range.Lo != lo) throw new ArgumentException($"no range starts at {lo}", nameof(lo));
        return new(Entries.SetItem(i, Entries[i] with { Owner = owner }), Count);
    }

    /// <summary>Merge every pair of adjacent ranges with one owner: the shape a node keeps after a rebalance.</summary>
    public RangeMap Compact()
    {
        var map = this;
        for (var i = 0; i + 1 < map.Entries.Length;)
            if (map.Entries[i].Owner == map.Entries[i + 1].Owner) map = map.Merge(map.Entries[i].Range.Lo);
            else i++;
        return map;
    }

    /// <summary>A rebalance step: split where a boundary is missing, then reassign, then compact.</summary>
    public sealed record Move(VpRange Range, string? From, string? To);

    /// <summary>
    /// The moves that take this map to the fair share of <paramref name="nodes"/>: for every virtual partition whose
    /// fair owner differs from its current one, the maximal run of them. Applying them (<see cref="Apply"/>) gives a map
    /// equal to <see cref="Fair"/> after <see cref="Compact"/>.
    /// </summary>
    public IReadOnlyList<Move> Moves(IReadOnlyList<string> nodes)
    {
        var fair = Fair(Count, nodes);
        var moves = new List<Move>();
        var boundaries = Entries.Select(e => e.Range.Lo).Concat(fair.Entries.Select(e => e.Range.Lo)).Distinct().Order().ToList();
        for (var b = 0; b < boundaries.Count; b++)
        {
            var (lo, hi) = (boundaries[b], b + 1 < boundaries.Count ? boundaries[b + 1] : Count);
            var (from, to) = (OwnerOf(lo), fair.OwnerOf(lo));
            if (from != to) moves.Add(new Move(new VpRange(lo, hi), from, to));
        }
        // Adjacent moves with the same from and to are one move.
        for (var i = 0; i + 1 < moves.Count;)
        {
            var (a, b) = (moves[i], moves[i + 1]);
            if (a.From == b.From && a.To == b.To && a.Range.Hi == b.Range.Lo)
            {
                moves[i] = a with { Range = new VpRange(a.Range.Lo, b.Range.Hi) };
                moves.RemoveAt(i + 1);
            }
            else i++;
        }
        return moves;
    }

    /// <summary>
    /// The other way to rebalance: move as few virtual partitions as possible. Nodes above their share give the tail
    /// of their widest range to nodes below it, free ranges first. A joiner ends up with a slice from every node, so the
    /// map fragments (more range rows per node) where <see cref="Moves"/> keeps one range per node but moves far more.
    /// </summary>
    public IReadOnlyList<Move> StealMoves(IReadOnlyList<string> nodes)
    {
        var share = VirtualPartitions.FairRanges(Count, nodes.Count).Select((r, i) => (nodes[i], r.Count)).ToDictionary(StringComparer.Ordinal);
        var map = this;
        var moves = new List<Move>();
        var owned = nodes.ToDictionary(n => n, map.Owned, StringComparer.Ordinal);
        // Donors: free ranges, then the nodes with a surplus, widest range first. Takers: nodes below their share.
        for (var guard = 0; guard < Count; guard++)
        {
            var taker = nodes.Select(n => (n, want: share[n] - owned[n])).Where(t => t.want > 0).OrderByDescending(t => t.want).ThenBy(t => t.n, StringComparer.Ordinal).FirstOrDefault();
            if (taker.n is null) break;
            var donor = map.Entries.Where(e => e.Owner is null).OrderByDescending(e => e.Range.Count).ThenBy(e => e.Range.Lo).FirstOrDefault()
                ?? map.Entries.Where(e => e.Owner is not null && (!share.ContainsKey(e.Owner) || owned[e.Owner] > share[e.Owner]))
                    .OrderByDescending(e => e.Range.Count).ThenBy(e => e.Range.Lo).FirstOrDefault();
            if (donor is null) break;
            var surplus = donor.Owner is null || !share.ContainsKey(donor.Owner) ? donor.Range.Count : owned[donor.Owner] - share[donor.Owner];
            var width = Math.Min(Math.Min(taker.want, surplus), donor.Range.Count);
            var move = new Move(new VpRange(donor.Range.Hi - width, donor.Range.Hi), donor.Owner, taker.n);
            moves.Add(move);
            map = map.Apply(move);
            owned[taker.n] += width;
            if (donor.Owner is not null && share.ContainsKey(donor.Owner)) owned[donor.Owner] -= width;
        }
        return moves;
    }

    /// <summary>Apply a move: the splits its boundaries need, the reassignment, then compaction.</summary>
    public RangeMap Apply(Move move)
    {
        var map = Split(move.Range.Lo);
        if (move.Range.Hi < Count) map = map.Split(move.Range.Hi);
        for (var i = 0; i < map.Entries.Length; i++)
        {
            var entry = map.Entries[i];
            if (entry.Range.Lo >= move.Range.Lo && entry.Range.Hi <= move.Range.Hi)
            {
                if (entry.Owner != move.From) throw new InvalidOperationException($"{entry.Range} is {entry.Owner ?? "free"}, the move expected {move.From ?? "free"}");
                map = map.Assign(entry.Range.Lo, move.To);
            }
        }
        return map.Compact();
    }

    private void Check()
    {
        if (Entries.Length == 0 || Entries[0].Range.Lo != 0 || Entries[^1].Range.Hi != Count) throw new InvalidOperationException("the ranges do not cover the virtual partitions");
        for (var i = 0; i + 1 < Entries.Length; i++)
            if (Entries[i].Range.Hi != Entries[i + 1].Range.Lo || Entries[i].Range.IsEmpty) throw new InvalidOperationException($"the ranges overlap or leave a gap at {Entries[i].Range}");
        if (Entries[^1].Range.IsEmpty) throw new InvalidOperationException("an empty range");
    }

    public override string ToString() => string.Join(" ", Entries.Select(e => $"{e.Range}={e.Owner ?? "-"}"));
}
