namespace Kernel.Store.Conformance;

/// <summary>
/// K1: ownership by a set of virtual partitions, each with its own fence row, epoch, position and journal. A commit
/// over several of them fences k rows, locked in ascending partition id, and lands all or nothing. Skipped on a store
/// without <see cref="IKernelStore.OwnedSetsAvailable"/>.
/// </summary>
internal static class OwnedSetChecks
{
    private const int Partitions = 8;
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PastShort = TimeSpan.FromMilliseconds(700);

    internal static IReadOnlyList<Case> Cases { get; } =
    [
        new("owned sets: a one-element set is today's one-partition take, poll, commit and release", Level.Required, OneElement),
        new("owned sets: one stale fence rolls the whole flush back; the kept partitions have no hole and no stranded mark", Level.Required, PartialStaleness, Partitions),
        new("owned sets: a release of a subset leaves the kept rows untouched", Level.Required, SubsetRelease, Partitions),
        new("owned sets: a take of a scattered set, and one poll over it", Level.Required, ScatteredTake, Partitions),
        new("owned sets: the k-row fence locks in ascending partition id", Level.Required, LockOrder, Partitions),
    ];

    private static void Require(Context c)
    {
        if (!c.S.OwnedSetsAvailable) throw new SkipException("the store fences one partition per commit");
    }

    private static CommitBatch Flush(IEnumerable<PartitionFence> fences, IReadOnlyList<TurnCommit> turns, IReadOnlyList<InboxKey>? seen = null, IReadOnlyList<InboxKey>? trial = null, bool handOver = false) =>
        CommitBatch.Owned([.. fences], Context.Long, handOver, turns, seen ?? [], trial ?? []);

    private static IReadOnlyList<int> Ids(OwnedLease lease) => [.. lease.Taken.Select(f => f.Partition)];

    /// <summary>Every store: the set-valued operations over one partition, through the store's own or the default adapters.</summary>
    private static async Task OneElement(Context c)
    {
        await c.S.AddAsync(Make.M("m", 0));
        var single = await c.Take(0);
        var set = await c.S.TakeAsync([0], "a", Context.Long);
        Context.Equal(new PartitionFence(0, single.Epoch + 1, 0), set.Taken.Single(), "a take of the one-element set");
        Context.That(Math.Abs(set.Now - single.Now) < 5_000, "the set take's time and the single take's time are far apart");
        var fence = set.Taken[0];
        Context.Equal("m", string.Join(" ", (await c.S.PollAsync([fence], TimeSpan.Zero, 10)).Messages.Select(m => m.Message.Id)), "a poll of the one-element set");
        Context.Equal("not landed", await c.Refused(() => c.Commit(single, 0, [Make.New("0/old")])), "a commit at the epoch before the set take");
        await c.Lands(() => c.S.CommitAsync(Flush([fence], [Make.New("0/i"), Make.New("0/j")], seen: ["m"])).AsTask(), "a one-element owned flush");
        Context.Equal(2L, (await c.S.ProjectedAsync(0)).Written, "position after the one-element owned flush");
        Context.Equal(fence.Epoch, (await c.Row(0, "m"))!.Seen, "seen mark of the one-element owned flush");
        await c.Lands(() => c.Commit(0, fence.Epoch, 2, [Make.New("0/k")]), "a one-partition commit after it");
        Context.Equal("1 2 3", string.Join(" ", (await c.S.ReadJournalAsync(0, 0, 10)).Select(j => j.Position)), "journal positions");
        Context.Equal("definite", await c.Refused(() => c.S.CommitAsync(Flush([fence with { Position = 3 }], [Make.New("1/x")])).AsTask(), "a one-element flush with a turn outside its partition"),
            "outcome of a one-element flush with a turn outside its partition");
        if (c.S.OwnedSetsAvailable)
        {
            // An owned store binds a mark to its message's own partition, so one outside even a one-element set is refused.
            await c.S.AddAsync(Make.M("o1", 1));
            Context.Equal("definite", await c.Refused(() => c.S.CommitAsync(Flush([fence with { Position = 3 }], [], seen: ["o1"])).AsTask(), "a one-element flush marking a message of another partition"),
                "outcome of a one-element flush marking a message of another partition");
            Context.Equal(0L, (await c.Row(1, "o1"))!.Seen, "seen mark of a message outside the one-element set");
        }
        var misnamed = Flush([fence with { Position = 3 }], [Make.New("0/l")]) with { Position = 2 };
        Context.Equal("definite", await c.Refused(() => c.S.CommitAsync(misnamed).AsTask(), "an owned batch that misnames its lowest fence"), "outcome of a misnamed owned batch");
        if (!c.S.OwnedSetsAvailable)
            Context.Equal("definite", await c.Refused(() => c.S.CommitAsync(Flush([fence with { Position = 3 }, new(1, 1, 0)], [Make.New("0/l")])).AsTask(),
                "a batch over two partitions on a store that fences one"), "outcome of a two-partition batch on a store that fences one");
        await c.S.ReleaseAsync([fence]);
        Context.That(await c.S.TakeAsync(0, "b", Context.Long) is not null, "b could not take the partition after a one-element release");
    }

    private static async Task PartialStaleness(Context c)
    {
        Require(c);
        foreach (var p in new[] { 1, 3, 5 }) await c.S.AddAsync(Make.M($"s{p}", p));
        foreach (var p in new[] { 1, 3, 5 }) await c.S.AddAsync(Make.M($"t{p}", p));
        var a = await c.S.TakeAsync([1, 3, 5], "a", Short);
        Context.Equal("1 3 5", string.Join(" ", Ids(a)), "partitions a took");
        // Held outstanding while partition 3 moves to b: a's lease runs out and b takes 3 alone.
        var flush = Flush(a.Taken, [Make.New("1/x"), Make.New("3/y"), Make.New("5/z")], seen: ["s1", "s3", "s5"], trial: ["t1", "t3", "t5"]);
        await Task.Delay(PastShort);
        var b = await c.S.TakeAsync(3, "b", Context.Long);
        Context.That(b is not null, "b could not take partition 3 after a's lease ran out");
        Context.Equal("not landed", await c.Refused(() => c.S.CommitAsync(flush).AsTask(), "the flush with one stale fence"), "outcome of the flush");

        Context.Equal(0, (await c.S.LoadAsync(["1/x", "3/y", "5/z"])).Count, "rows written by the refused flush");
        foreach (var p in new[] { 1, 3, 5 })
        {
            Context.Equal(0, (await c.S.ReadJournalAsync(p, 0, 10)).Count, $"journal rows of partition {p} after the refused flush");
            Context.Equal(0L, (await c.S.ProjectedAsync(p)).Written, $"position of partition {p} after the refused flush");
        }
        foreach (var p in new[] { 1, 5 })
        {
            var (seen, tried) = ((await c.Row(p, $"s{p}"))!, (await c.Row(p, $"t{p}"))!);
            Context.That(seen.Seen == 0 && seen.Trial == 0 && tried.Seen == 0 && tried.Trial == 0, $"the refused flush left a mark in kept partition {p}");
        }

        // a resets from the store and continues on the partitions it keeps; b writes 3.
        var kept = a.Taken.Where(f => f.Partition != 3).ToList();
        await c.Lands(() => c.S.CommitAsync(Flush(kept, [Make.New("1/x"), Make.New("5/z")], seen: ["s1", "s5"], trial: ["t1", "t5"])).AsTask(), "the flush on the kept partitions");
        await c.Lands(() => c.Commit(b!, 3, [Make.New("3/y")], seen: ["s3"]), "b's commit in partition 3");
        foreach (var fence in kept)
        {
            var journal = await c.S.ReadJournalAsync(fence.Partition, 0, 10);
            Context.Equal("1", string.Join(" ", journal.Select(j => j.Position)), $"journal positions of kept partition {fence.Partition}");
            Context.Equal(1L, (await c.S.ProjectedAsync(fence.Partition)).Written, $"position of kept partition {fence.Partition}");
            var tried = (await c.Row(fence.Partition, $"t{fence.Partition}"))!;
            Context.That(tried.Trial == fence.Epoch && tried.Seen == fence.Epoch, $"trial mark in partition {fence.Partition} is not at that partition's epoch");
        }
        Context.Equal(1L, (await c.S.ProjectedAsync(3)).Written, "position of partition 3 after b's commit");
        Context.Equal(b!.Epoch, (await c.Row(3, "s3"))!.Seen, "seen mark in partition 3 at b's epoch");
    }

    private static async Task SubsetRelease(Context c)
    {
        Require(c);
        var a = await c.S.TakeAsync([2, 3, 4, 5], "a", Context.Long);
        Context.Equal("2 3 4 5", string.Join(" ", Ids(a)), "partitions a took");
        await c.Lands(() => c.S.CommitAsync(Flush(a.Taken, [Make.New("2/i"), Make.New("3/i"), Make.New("4/i"), Make.New("5/i")])).AsTask(), "a's first flush");
        var held = a.Taken.Select(f => f with { Position = 1 }).ToList();

        await c.S.ReleaseAsync([.. held.Where(f => f.Partition is 3 or 5)]);
        var b = await c.S.TakeAsync([2, 3, 4, 5], "b", Context.Long);
        Context.Equal("3 5", string.Join(" ", Ids(b)), "partitions b could take after the release");
        foreach (var fence in b.Taken)
        {
            Context.Equal(held.Single(f => f.Partition == fence.Partition).Epoch + 1, fence.Epoch, $"epoch of released partition {fence.Partition} at b's take");
            Context.Equal(1L, fence.Position, $"position of released partition {fence.Partition} at b's take");
        }

        // The kept rows are as they were: a's picture of them still lands, and a stale release of a moved row frees nothing.
        var kept = held.Where(f => f.Partition is 2 or 4).ToList();
        await c.Lands(() => c.S.CommitAsync(Flush(kept, [Make.Turn("2/i", Make.Row("2/i", 2), expected: 1), Make.Turn("4/i", Make.Row("4/i", 2), expected: 1)])).AsTask(), "a's flush on its kept partitions");
        await c.S.ReleaseAsync([.. held.Where(f => f.Partition == 3)]);
        Context.Equal(0, (await c.S.TakeAsync([3], "c", Context.Long)).Taken.Count, "a release at a moved partition's old epoch freed it");
        await c.Lands(() => c.S.CommitAsync(Flush(b.Taken, [Make.Turn("3/i", Make.Row("3/i", 2), expected: 1), Make.Turn("5/i", Make.Row("5/i", 2), expected: 1)])).AsTask(), "b's flush");
        // a's flush over its kept 2, current, and the released 3, stale: nothing of it lands.
        IReadOnlyList<PartitionFence> across = [held.Single(f => f.Partition == 2) with { Position = 2 }, held.Single(f => f.Partition == 3)];
        Context.Equal("not landed", await c.Refused(() => c.S.CommitAsync(Flush(across, [Make.New("2/j")])).AsTask(),
            "a's flush across a kept and a released partition"), "a's flush across a kept and a released partition");
        Context.Equal(0, (await c.S.LoadAsync(["2/j"])).Count, "row written in the kept partition by the refused flush");
        foreach (var p in new[] { 2, 3, 4, 5 })
            Context.Equal("1 2", string.Join(" ", (await c.S.ReadJournalAsync(p, 0, 10)).Select(j => j.Position)), $"journal positions of partition {p}");
    }

    private static async Task ScatteredTake(Context c)
    {
        Require(c);
        // Partition 1 is taken once before, so that its epoch differs from 4's and 6's, and p1 is seen at that first epoch.
        var first = await c.Take(1, "a");
        await c.S.AddAsync(Make.M("p1", 1));
        await c.Lands(() => c.Commit(first, 1, [], seen: ["p1"]), "a commit in partition 1 at its first epoch");
        var a = await c.S.TakeAsync([6, 1, 4], "a", Context.Long);
        Context.Equal("1 4 6", string.Join(" ", Ids(a)), "a scattered take, in ascending partition id");
        Context.That(a.Taken.All(f => f.Epoch > 0 && f.Position == 0), "a take's epochs and positions");
        var epoch = a.Taken.ToDictionary(f => f.Partition, f => f.Epoch);
        Context.That(epoch[1] != epoch[6], "this case needs partition 1's epoch to differ from partition 6's");
        Context.That(Math.Abs(await c.Now() - a.Now) < 5_000, "the take's time and a poll's time are far apart");
        var b = await c.S.TakeAsync([0, 1, 2, 4, 7], "b", Context.Long);
        Context.Equal("0 2 7", string.Join(" ", Ids(b)), "b's take leaves out a's partitions");

        foreach (var (id, p) in new[] { ("m6", 6), ("m2", 2), ("m1", 1), ("m4", 4), ("n1", 1) }) await c.S.AddAsync(Make.M(id, p));
        var polled = await c.S.PollAsync(a.Taken, TimeSpan.Zero, 100);
        // Each row is compared with its own partition's epoch: p1, seen at 1's earlier epoch, is due again.
        Context.Equal("p1 m6 m1 m4 n1", string.Join(" ", polled.Messages.Select(m => m.Message.Id)), "one poll over a's set, in seq order");
        Context.Equal("p1 m6", string.Join(" ", (await c.S.PollAsync(a.Taken, TimeSpan.Zero, 2)).Messages.Select(m => m.Message.Id)), "the poll's limit over the set");
        PartitionFence At(int partition, long position) => new(partition, epoch[partition], position);

        // A flush may touch part of the set: only those partitions move, and each mark takes its own partition's epoch.
        await c.Lands(() => c.S.CommitAsync(Flush([At(1, 0), At(6, 0)], [Make.New("6/x"), Make.New("1/x"), Make.New("1/y")], seen: ["m1", "p1"], trial: ["m6"])).AsTask(), "a flush over 1 and 6");
        Context.Equal(2L, (await c.S.ProjectedAsync(1)).Written, "position of partition 1");
        Context.Equal(0L, (await c.S.ProjectedAsync(4)).Written, "position of partition 4, not in the flush");
        Context.Equal(1L, (await c.S.ProjectedAsync(6)).Written, "position of partition 6");
        Context.Equal(epoch[1], (await c.Row(1, "m1"))!.Seen, "seen mark in partition 1");
        var m6 = (await c.Row(6, "m6"))!;
        Context.That(m6.Seen == epoch[6] && m6.Trial == epoch[6], $"trial mark in partition 6 at seen {m6.Seen}, trial {m6.Trial}, not at its own epoch {epoch[6]}");
        Context.Equal("m4 n1", string.Join(" ", (await c.S.PollAsync(a.Taken, TimeSpan.Zero, 100)).Messages.Select(m => m.Message.Id)), "poll after the marks");

        // A handover writes a message to a fenced partition as seen at that partition's epoch, and one to another unseen.
        await c.Lands(() => c.S.CommitAsync(Flush([At(1, 2), At(6, 1)], [Make.Turn("1/x", Make.Row("1/x", 2), expected: 1, sent: [Make.M("h6", 6), Make.M("h4", 4)])], handOver: true)).AsTask(), "a handover flush over 1 and 6");
        Context.Equal(epoch[6], (await c.Row(6, "h6"))!.Seen, "handover seen mark of a message to partition 6");
        Context.Equal(0L, (await c.Row(4, "h4"))!.Seen, "handover seen mark of a message to partition 4, outside the flush");
        Context.Equal("definite", await c.Refused(() => c.S.CommitAsync(Flush([At(1, 3), At(6, 1)], [], seen: ["m4"])).AsTask(), "a flush over 1 and 6 marking a message of 4"),
            "outcome of a flush marking a message outside its fenced set");
        Context.Equal("definite", await c.Refused(() => c.S.CommitAsync(Flush([At(1, 3), At(6, 1)], [Make.Turn("1/x", Make.Row("1/x", 3), expected: 2, consumed: ["m4"])])).AsTask(), "a turn in 1 consuming a message of 4"),
            "outcome of a flush consuming a message outside its fenced set");
        Context.Equal(2L, (await c.S.LoadAsync(["1/x"])).Single().Revision, "revision of 1/x after the refused consume");
        Context.Equal("not landed", await c.Refused(() => c.S.CommitAsync(Flush([At(1, 3), At(6, 0)], [], seen: ["n1"])).AsTask(), "a flush whose higher fence alone is stale"),
            "outcome of a flush whose higher fence alone is stale");
        Context.Equal(0L, (await c.Row(1, "n1"))!.Seen, "seen mark written by the refused flush");
        Context.Equal("definite", await c.Refused(() => c.S.CommitAsync(Flush([At(1, 3), At(6, 1)], [Make.New("1/z"), Make.New("2/z")])).AsTask(), "a turn outside the fenced set"),
            "outcome of a flush with a turn outside its fenced set");
        Context.Equal(0, (await c.S.LoadAsync(["1/z", "2/z"])).Count, "rows written by the refused flush");

        // Journal rows a delete removes are counted in their own partition (observable on the in-memory store).
        await c.Lands(() => c.S.CommitAsync(Flush([At(1, 3), At(6, 1)], [Make.Delete("6/x", 1)])).AsTask(), "a delete in partition 6 fenced with partition 1");
        Context.Equal("2", string.Join(" ", (await c.S.ReadJournalAsync(6, 0, 10)).Select(j => j.Position)), "journal positions of partition 6 after the delete");
        if (c.S is InMemoryStore store) Context.That(store.Deleted[6] == 1 && store.Deleted[1] == 0, "journal rows removed by the delete are not counted in their own partition");

        // A landed flush renews the lease of every fenced row.
        var brief = await c.S.TakeAsync([3, 5], "a", Short);
        await c.Lands(() => c.S.CommitAsync(Flush(brief.Taken, [])).AsTask(), "a flush over 3 and 5 under a long lease");
        await Task.Delay(PastShort);
        Context.Equal("", string.Join(" ", Ids(await c.S.TakeAsync([3, 5], "b", Context.Long))), "partitions b took after a's flush renewed their leases");
    }

    private static async Task LockOrder(Context c)
    {
        Require(c);
        if (c.S is not InMemoryStore store) throw new SkipException("the lock order is observable on the in-memory store only");
        var a = await c.S.TakeAsync([7, 2, 5, 0], "a", Context.Long);
        var locked = new List<int>();
        store.Locking = p => { lock (locked) locked.Add(p); };
        IReadOnlyList<PartitionFence> scrambled = [.. a.Taken.Reverse().Skip(1), a.Taken[^1]];
        await c.Lands(() => c.S.CommitAsync(Flush(scrambled, [Make.New("7/x"), Make.New("0/x"), Make.New("5/x")])).AsTask(), "the scrambled flush");
        Context.Equal("0 2 5 7", string.Join(" ", locked), "fence rows locked by the landed flush");
        locked.Clear();
        Context.Equal("not landed", await c.Refused(() => c.S.CommitAsync(Flush(scrambled, [Make.New("2/y")])).AsTask(), "the same flush again"), "outcome of the same flush again");
        // A store may stop at the first stale fence; whatever it locks is an ascending prefix.
        Context.That(locked.Count > 0 && locked.SequenceEqual(new[] { 0, 2, 5, 7 }.Take(locked.Count)), $"fence rows locked by the refused flush: {string.Join(" ", locked)}");
    }
}
