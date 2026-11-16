namespace Kernel.Sim;

/// <summary>
/// Round 5 A: deterministic witnesses of virtual-partition addressing and range placement (<c>--placement-check</c>).
/// Placement is a pure function of the name and the count; ranges cover, never overlap, split and merge exactly; a
/// rebalance moves virtual partitions, never renumbers one; the hash spreads names evenly enough at both counts.
/// </summary>
public static class PlacementChecks
{
    public static int Run()
    {
        var failures = 0;
        void Check(bool ok, string name) { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); if (!ok) failures++; }

        foreach (var count in new[] { VirtualPartitions.Small, VirtualPartitions.Large })
        {
            // Stable placement: the virtual partition of a name does not depend on who owns what, and the id carries it.
            var vp = VirtualPartitions.Of("order-17", count);
            Check(vp >= 0 && vp < count && new InstanceRef(InstanceRef.In(vp, "order-17").Id).Partition == vp, $"{count}: an address embeds its virtual partition ({vp})");
            Check(Enumerable.Range(0, 1000).All(i => VirtualPartitions.Of($"name{i}", count) == VirtualPartitions.Of($"name{i}", count)), $"{count}: placement is a function of the name");

            // Fair ranges cover the count exactly, in order, differing by at most one.
            foreach (var nodes in new[] { 1, 2, 3, 4, 7, 16, 100 })
            {
                var ranges = VirtualPartitions.FairRanges(count, nodes);
                var covers = ranges[0].Lo == 0 && ranges[^1].Hi == count && ranges.Zip(ranges.Skip(1)).All(p => p.First.Hi == p.Second.Lo);
                var even = ranges.Max(r => r.Count) - ranges.Min(r => r.Count) <= 1;
                Check(covers && even && ranges.Length == Math.Min(nodes, count), $"{count}: {nodes} nodes get {ranges.Length} fair ranges, {ranges.Min(r => r.Count)} to {ranges.Max(r => r.Count)} wide");
            }

            // The map: fresh, then three nodes join one at a time; every step covers and moves nothing twice.
            var map = RangeMap.Fresh(count);
            string[] all = ["n0", "n1", "n2", "n3"];
            var names = Enumerable.Range(0, 2000).Select(i => $"w{i}").ToList();
            var before = names.ToDictionary(n => n, n => VirtualPartitions.Of(n, count));
            long moved = 0;
            for (var joined = 1; joined <= all.Length; joined++)
            {
                var nodes = all.Take(joined).ToList();
                var moves = map.Moves(nodes);
                foreach (var move in moves) { map = map.Apply(move); moved += move.Range.Count; }
                var fair = RangeMap.Fair(count, nodes);
                Check(map.Entries.SequenceEqual(fair.Entries), $"{count}: after {joined} node(s) joined, the moves reach the fair share ({map.Entries.Length} ranges)");
            }
            Check(names.All(n => VirtualPartitions.Of(n, count) == before[n]), $"{count}: no name changed its virtual partition while {moved} virtual partitions moved");
            Check(moved > 0, $"{count}: rebalances moved {moved} virtual partitions in all");

            // The same joins with minimal moves: a joiner's share moves and no more, at the price of fragmented ranges.
            var (stolen, fragments) = (0L, RangeMap.Fresh(count));
            for (var joined = 1; joined <= all.Length; joined++)
            {
                var nodes = all.Take(joined).ToList();
                foreach (var move in fragments.StealMoves(nodes)) { fragments = fragments.Apply(move); stolen += move.Range.Count; }
                var fair = VirtualPartitions.FairRanges(count, joined).Select(r => r.Count).Order().ToList();
                Check(nodes.Select(fragments.Owned).Order().SequenceEqual(fair) && fragments.Entries.All(e => e.Owner is not null),
                    $"{count}: minimal moves after {joined} node(s) joined reach the fair share in {fragments.Entries.Length} ranges");
            }
            var expectedStolen = Enumerable.Range(1, all.Length).Sum(n => VirtualPartitions.FairRanges(count, n).Last().Count);
            Check(stolen == expectedStolen, $"{count}: minimal rebalances moved {stolen} virtual partitions (contiguous ones moved {moved}); {fragments.Entries.Length} ranges for {all.Length} nodes");
            Check(names.All(n => VirtualPartitions.Of(n, count) == before[n]), $"{count}: no name changed its virtual partition under minimal moves either");

            // Split and merge are exact inverses; merging across owners is refused.
            var split = map.Split(1).Split(count - 1);
            Check(split.Entries.Length == map.Entries.Length + 2 && split.Compact().Entries.SequenceEqual(map.Entries), $"{count}: split then compact gives the map back");
            var refused = false;
            try { RangeMap.Fair(count, ["a", "b"]).Merge(0); } catch (InvalidOperationException) { refused = true; }
            Check(refused, $"{count}: merging two owners' ranges is refused");

            // One node leaves: its ranges go to the others, and each other node keeps what it had or grows.
            var three = all.Take(3).ToList();
            var held = three.ToDictionary(n => n, n => map.RangesOf(n).ToList());
            var after = map;
            foreach (var move in map.Moves(three)) after = after.Apply(move);
            Check(after.Entries.All(e => e.Owner is not null && three.Contains(e.Owner)) && after.Owned("n3") == 0, $"{count}: a node that left owns nothing");
            Check(three.All(n => after.Owned(n) >= map.Owned(n)), $"{count}: the nodes that stay only grow when one leaves");

            // Spread: 100,000 names over the count; the fullest virtual partition against the mean.
            var load = new int[count];
            for (var i = 0; i < 100_000; i++) load[VirtualPartitions.Of($"instance-{i}", count)]++;
            var mean = 100_000.0 / count;
            Check(load.Max() < mean * 2 && load.Min() > 0, $"{count}: 100,000 names, mean {mean:F1} per virtual partition, min {load.Min()}, max {load.Max()}");
        }
        Console.WriteLine(failures == 0 ? "placement: every check passed" : $"placement: {failures} check(s) FAILED");
        return failures == 0 ? 0 : 1;
    }
}
