namespace Kernel.Store.Conformance;

/// <summary>
/// K1b: the node lease (ownership-and-fencing.md, "The partition record", "The node's heartbeat" and the acceptance
/// cases NL-1 to NL-10), the store-level parts. Each case asks for its store with the node lease on, and is skipped on a
/// store without <see cref="IKernelStore.NodeLeaseAvailable"/>. Process p owns partitions; q and r are other processes.
/// A case whose acceptance names a store mutation fails under it (<c>semantic-flaw</c>); the others carry a negative control.
/// </summary>
internal static class NodeLeaseChecks
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PastShort = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    internal static IReadOnlyList<Case> Cases { get; } =
    [
        new("node lease: the heartbeat renews the node record and lists its partitions with their epochs; membership frees an expired owner's partitions; the fence renews no lease", Level.Required, Heartbeat, 4, NodeLease: true),
        new("node lease: a partition whose owner has no node record is taken, and the take counts one death", Level.Required, MissingRecord, 3, NodeLease: true),
        new("node lease NL-2: a paused owner's flush is refused whole, its heartbeat lists none of its partitions, one death per trial message; no take from a live process", Level.Required, PausedOwner, 4, NodeLease: true),
        new("node lease NL-4: a renewal racing a take keeps only the partitions the take left alone", Level.Required, RenewalRacingTake, 4, NodeLease: true),
        new("node lease NL-5: a flush of seen marks only is refused whole when a take lands while it is on the wire", Level.Required, MarksOnly, 5, NodeLease: true),
        new("node lease NL-6: a restarted process under a new name takes nothing until its predecessor's node lease expires, then counts the deaths", Level.Required, Restart, 4, NodeLease: true),
        new("node lease NL-7: a take renews its taker's node record, whatever it takes", Level.Required, TakeRenews, 5, NodeLease: true),
        new("node lease NL-8: after a lost release or retake, the heartbeat lists the row at its epoch, and a release or retake at that epoch settles it", Level.Required, LostRelease, 6, NodeLease: true),
    ];

    private static void Require(Context c)
    {
        if (!c.S.NodeLeaseAvailable) throw new SkipException("the store keeps the per-partition lease");
    }

    private static string Ids(OwnedLease lease) => string.Join(" ", lease.Taken.Select(f => f.Partition));

    private static string Listed(NodeBeat beat) => string.Join(" ", beat.Owned.Select(o => $"{o.Partition}@{o.Epoch}"));

    private static string At(params PartitionFence[] fences) => string.Join(" ", fences.Select(f => $"{f.Partition}@{f.Epoch}"));

    private static Task<NodeBeat> Beat(Context c, string node, TimeSpan? lease = null) => c.S.NodeHeartbeatAsync(node, lease ?? Context.Long, Window).AsTask();

    private static async Task Attempts(Context c, string when, params (string Id, int Partition, int Expected)[] rows)
    {
        foreach (var (id, partition, expected) in rows) Context.Equal(expected, (await c.Row(partition, id))!.Attempts, $"attempts of {id} {when}");
    }

    private static async Task Heartbeat(Context c)
    {
        Require(c);
        var p = await c.S.TakeAsync([1, 3], "p", Short);
        var r = await c.S.TakeAsync([0], "r", Short);       // the control: nothing renews r
        var f = await c.S.TakeAsync([2], "f", Short);
        // f's commit asks for a long lease; under the node lease the fence renews nothing.
        await c.Lands(() => c.S.CommitAsync(CommitBatch.Owned([f.Taken[0]], Context.Long, false, [Make.New("2/i")], [], [])).AsTask(), "f's commit in 2");
        var beat = await Beat(c, "p");
        Context.Equal(At([.. p.Taken]), Listed(beat), "p's heartbeat answer");
        Context.That(Math.Abs(await c.Now() - beat.Now) < 5_000, "the heartbeat's time and a poll's time are far apart");
        Context.Equal("", string.Join(" ", beat.Cluster.Free), "free partitions while every owner is live");
        await Task.Delay(PastShort);
        beat = await Beat(c, "p");
        Context.Equal(At([.. p.Taken]), Listed(beat), "p's heartbeat answer after r's and f's leases ran out");
        Context.Equal("0 2", string.Join(" ", beat.Cluster.Free), "free partitions after r's and f's leases ran out");
        Context.Equal("0 2", Ids(await c.S.TakeAsync([0, 1, 2, 3], "q", Context.Long)), "partitions q took: r's and f's, and none of the renewed p's");
        Context.Equal(r.Taken[0].Epoch + 1, (await Beat(c, "q")).Owned[0].Epoch, "epoch of 0 after q's take");
    }

    /// <summary>A missing node record counts as expired. The negative control: with p's record in place, q takes nothing.</summary>
    private static async Task MissingRecord(Context c)
    {
        Require(c);
        if (c.Provided.DeleteNodeRecord is not { } delete) throw new SkipException("the provider cannot delete a node record");
        await c.S.AddAsync(Make.M("t1", 1));
        var p = await c.S.TakeAsync([1], "p", Context.Long);
        await c.Lands(() => c.S.CommitAsync(CommitBatch.Owned([.. p.Taken], Context.Long, false, [], [], ["t1"])).AsTask(), "p's flush putting t1 on trial");
        Context.Equal("", Ids(await c.S.TakeAsync([1], "q", Context.Long)), "partitions q took from the live p");
        await delete("p");
        Context.That((await Beat(c, "q")).Cluster.Free.Contains(1), "partition 1 is not free once its owner has no node record");
        Context.Equal(At(p.Taken[0] with { Epoch = p.Taken[0].Epoch + 1 }), At([.. (await c.S.TakeAsync([1], "q", Context.Long)).Taken]), "q's take of 1, whose owner has no node record");
        await Attempts(c, "after q's take", ("t1", 1, 1));
    }

    private static async Task PausedOwner(Context c)
    {
        Require(c);
        int[] set = [1, 2, 3];
        foreach (var n in set)
        {
            await c.S.AddAsync(Make.M($"m{n}", n));
            await c.S.AddAsync(Make.M($"t{n}", n));
        }
        var p = await c.S.TakeAsync(set, "p", Short);
        await c.Lands(() => c.S.CommitAsync(CommitBatch.Owned([.. p.Taken], Short, false, [], [], [.. set.Select(n => (InboxKey)$"t{n}")])).AsTask(), "p's flush putting a message of each partition on trial");
        // The invariant the mutation breaks: no take from a live process except a release or a forced take.
        Context.Equal("", Ids(await c.S.TakeAsync(set, "q", Context.Long)), "partitions q took from the live p");

        // p pauses past its lease, holding a flush of a turn and seen marks over all three.
        var flush = CommitBatch.Owned([.. p.Taken], Short, false, [Make.New("1/x")], [.. set.Select(n => (InboxKey)$"m{n}")], []);
        await Task.Delay(PastShort);
        var q = await c.S.TakeAsync(set, "q", Context.Long);
        Context.Equal("1 2 3", Ids(q), "partitions q took from the expired p");
        Context.That(q.Taken.Zip(p.Taken).All(t => t.First.Epoch == t.Second.Epoch + 1), "q's take did not move each epoch by one");
        await Attempts(c, "after q's take", ("t1", 1, 1), ("t2", 2, 1), ("t3", 3, 1), ("m1", 1, 0), ("m2", 2, 0), ("m3", 3, 0));
        Context.Equal("not landed", await c.Refused(() => c.S.CommitAsync(flush).AsTask(), "p's paused flush"), "outcome of p's paused flush");
        Context.Equal("", Listed(await Beat(c, "p")), "p's heartbeat answer after q's take");

        // Nothing of p's flush, and q's journals start at 1 with no stranded mark.
        Context.Equal(0, (await c.S.LoadAsync(["1/x"])).Count, "rows written by p's refused flush");
        foreach (var fence in q.Taken)
        {
            Context.Equal(0L, (await c.Row(fence.Partition, $"m{fence.Partition}"))!.Seen, $"seen mark of m{fence.Partition} after p's refused flush");
            await c.Lands(() => c.S.CommitAsync(CommitBatch.Owned([fence], Context.Long, false, [Make.New($"{fence.Partition}/y")], [$"m{fence.Partition}"], [])).AsTask(), $"q's commit in {fence.Partition}");
            Context.Equal("1", string.Join(" ", (await c.S.ReadJournalAsync(fence.Partition, 0, 10)).Select(j => j.Position)), $"journal positions of {fence.Partition}");
            Context.Equal(fence.Epoch, (await c.Row(fence.Partition, $"m{fence.Partition}"))!.Seen, $"seen mark of m{fence.Partition} at q's epoch");
        }
    }

    /// <summary>
    /// q's set take of {1, 2} reads p's node record as expired; p's heartbeat commits before q's take locks a partition
    /// row (the in-memory store's take hook stages it). The renewal is too late for 1 and 2, which the take decided by its
    /// read, and keeps 3. The negative control first: a heartbeat before the take's read keeps all three.
    /// </summary>
    private static async Task RenewalRacingTake(Context c)
    {
        Require(c);
        if (c.S is not InMemoryStore store) throw new SkipException("the race is staged with the in-memory store's take hook");
        foreach (var n in new[] { 1, 2, 3 }) await c.S.AddAsync(Make.M($"t{n}", n));
        var p = await c.S.TakeAsync([1, 2, 3], "p", Short);
        await c.Lands(() => c.S.CommitAsync(CommitBatch.Owned([.. p.Taken], Short, false, [], [], ["t1", "t2", "t3"])).AsTask(), "p's flush putting t1, t2 and t3 on trial");

        await Task.Delay(PastShort);
        await Beat(c, "p", Short);
        Context.Equal("", Ids(await c.S.TakeAsync([1, 2], "q", Context.Long)), "partitions q took after p's heartbeat renewed it");
        await Attempts(c, "after q's refused take", ("t1", 1, 0), ("t2", 2, 0));

        await Task.Delay(PastShort);
        NodeBeat? renewed = null;
        store.TakeRead = taker =>
        {
            if (taker != "q") return;
            store.TakeRead = null;
            renewed = store.NodeHeartbeatAsync("p", Context.Long, Window).AsTask().GetAwaiter().GetResult();
        };
        var q = await c.S.TakeAsync([1, 2], "q", Context.Long);
        Context.That(renewed is not null, "p's heartbeat did not run inside q's take");
        Context.Equal(At([.. p.Taken]), Listed(renewed!), "p's heartbeat answer, landed between q's read and q's locks");
        Context.Equal(At(p.Taken[0] with { Epoch = p.Taken[0].Epoch + 1 }, p.Taken[1] with { Epoch = p.Taken[1].Epoch + 1 }), At([.. q.Taken]), "q's take, decided by its read");
        Context.Equal(At(p.Taken[2]), Listed(await Beat(c, "p")), "p's next heartbeat answer");
        // The take counts the deaths by its read: the race costs one count on each partition it moved.
        await Attempts(c, "after q's take", ("t1", 1, 1), ("t2", 2, 1), ("t3", 3, 0));
        await c.Lands(() => c.S.CommitAsync(CommitBatch.Owned([p.Taken[2]], Context.Long, false, [Make.New("3/i")], [], [])).AsTask(), "p's commit in 3");
        Context.Equal("not landed", await c.Refused(() => c.S.CommitAsync(CommitBatch.Owned([p.Taken[0]], Context.Long, false, [Make.New("1/i")], [], [])).AsTask(), "p's commit in 1"), "outcome of p's commit in 1");
    }

    /// <summary>
    /// A batch with no journal rows still checks and locks every fenced row. Sequentially: q's take of 2 lands before p's
    /// batch over 1 and 2 is sent. Interleaved, on the in-memory store: q's take of 4 lands after p's batch over 3 and 4
    /// locked 3 and before it reaches 4. Mutation <see cref="Flaw.PlainReadEmptyFence"/>: the fence reads the rows without
    /// locking them, so the interleaved batch lands with p's stale marks in 4.
    /// </summary>
    private static async Task MarksOnly(Context c)
    {
        Require(c);
        int[] set = [1, 2, 3, 4];
        foreach (var n in set)
        {
            await c.S.AddAsync(Make.M($"s{n}", n));
            await c.S.AddAsync(Make.M($"t{n}", n));
        }
        var at = (await c.S.TakeAsync(set, "p", Short)).Taken.ToDictionary(f => f.Partition);
        CommitBatch Marks(int a, int b) => CommitBatch.Owned([at[a], at[b]], Context.Long, false, [], [$"s{a}", $"s{b}"], [$"t{a}", $"t{b}"]);
        async Task Unmarked(string when, params int[] partitions)
        {
            foreach (var n in partitions)
                foreach (var id in new[] { $"s{n}", $"t{n}" })
                    Context.That((await c.Row(n, id))! is { Seen: 0, Trial: 0 }, $"{id} is marked {when}");
        }

        var first = Marks(1, 2);
        await Task.Delay(PastShort);
        Context.Equal("2", Ids(await c.S.TakeAsync([2], "q", Context.Long)), "q's take of 2");
        Context.Equal("not landed", await c.Refused(() => c.S.CommitAsync(first).AsTask(), "p's batch of marks over 1 and 2"), "outcome of p's batch of marks over 1 and 2");
        await Unmarked("after p's refused batch over 1 and 2", 1, 2);

        if (c.S is not InMemoryStore store) return;
        Lease? q4 = null;
        store.Locking = partition =>
        {
            if (partition == 3 && q4 is null) q4 = store.TakeAsync(4, "q", Context.Long).AsTask().GetAwaiter().GetResult();
        };
        try
        {
            Context.Equal("not landed", await c.Refused(() => c.S.CommitAsync(Marks(3, 4)).AsTask(), "p's batch of marks over 3 and 4, with 4 taken on the wire"),
                "outcome of p's batch over 3 and 4");
        }
        finally { store.Locking = null; }
        Context.That(q4 is not null, "q could not take 4 while p's batch was on the wire");
        await Unmarked("after p's refused batch over 3 and 4", 3, 4);
    }

    /// <summary>Mutation <see cref="Flaw.StableNodeName"/>: the restarted process keeps its predecessor's name, so it retakes at once with no death.</summary>
    private static async Task Restart(Context c)
    {
        Require(c);
        var (before, after) = ("p.1", c.Flaws.HasFlag(Flaw.StableNodeName) ? "p.1" : "p.2");
        int[] set = [1, 2, 3];
        foreach (var n in set) await c.S.AddAsync(Make.M($"t{n}", n));
        var p = await c.S.TakeAsync(set, before, Short);
        await c.Lands(() => c.S.CommitAsync(CommitBatch.Owned([.. p.Taken], Short, false, [], [], ["t1", "t2", "t3"])).AsTask(), "p's flush putting t1, t2 and t3 on trial");
        Context.Equal("", Ids(await c.S.TakeAsync([1], after, Context.Long)), "partitions the restarted process took while its predecessor's node lease was live");
        await Task.Delay(PastShort);
        var again = await c.S.TakeAsync(set, after, Context.Long);
        Context.Equal(At([.. p.Taken.Select(f => f with { Epoch = f.Epoch + 1 })]), At([.. again.Taken]), "the restarted process's take after its predecessor's node lease expired");
        await Attempts(c, "after the restarted process's take", ("t1", 1, 1), ("t2", 2, 1), ("t3", 3, 1));
    }

    /// <summary>
    /// q's lease is near expiry when it takes 2; z's take of 3, which p holds, takes nothing. Both takes renew their taker.
    /// The negative control: p, renewed by nothing, loses 3.
    /// </summary>
    private static async Task TakeRenews(Context c)
    {
        Require(c);
        var q = await c.S.TakeAsync([1], "q", Short);
        await c.S.TakeAsync([3], "p", Short);
        await c.S.TakeAsync([4], "z", Short);
        await Task.Delay(Short / 3);
        var renewing = await c.S.TakeAsync([2], "q", Context.Long);
        Context.Equal("2", Ids(renewing), "q's take of 2");
        var empty = await c.S.TakeAsync([3], "z", Context.Long);
        Context.Equal("", Ids(empty), "z's take of 3 from the live p");
        Context.Equal(0L, empty.Now, "the time of z's empty take, which a caller must not read");
        if (c.S is InMemoryStore store)
            Context.Equal(renewing.Now + (long)Context.Long.TotalMilliseconds, store.Nodes["q"].LeaseUntil, "q's node lease after its take of 2");
        await Task.Delay(PastShort);
        Context.Equal("3", Ids(await c.S.TakeAsync([1, 2, 3, 4], "r", Context.Long)), "partitions r took: p's only");
        Context.Equal($"1@{q.Taken[0].Epoch} {At(renewing.Taken[0])}", Listed(await Beat(c, "q")), "q's heartbeat answer");
    }

    /// <summary>
    /// The store-level parts of NL-8 (the reconciliation itself is the Worker's, K1b slice 4). A release that landed, a
    /// release that did not, a retake that landed with its answer lost, and retakes one at a time with heartbeats between.
    /// The negative control: while p lives, a row listed as p's is not q's to take, so only p can settle it.
    /// </summary>
    private static async Task LostRelease(Context c)
    {
        Require(c);
        var at = (await c.S.TakeAsync([1, 2, 3, 4, 5], "p", Context.Long)).Taken.ToDictionary(f => f.Partition);
        string Next(params int[] partitions) => At([.. partitions.Select(n => at[n] with { Epoch = at[n].Epoch + 1 })]);

        await c.S.ReleaseAsync([at[2]]);                                       // landed, its answer lost
        Context.Equal(At(at[1], at[3], at[4], at[5]), Listed(await Beat(c, "p")), "p's heartbeat answer after the release of 2 landed");
        Context.Equal("2", Ids(await c.S.TakeAsync([2, 3], "q", Context.Long)), "q's take of 2 and 3 while p lives: the released 2, and not the listed 3");
        await c.S.ReleaseAsync([at[3]]);                                       // the release of 3, which did not land before, at the listed epoch
        Context.Equal("3", Ids(await c.S.TakeAsync([3], "q", Context.Long)), "q's take of 3 after p's release at the listed epoch");

        await c.S.RetakeAsync(1, "p", at[1].Epoch, Context.Long);              // landed, its answer lost
        Context.Equal($"{Next(1)} {At(at[4], at[5])}", Listed(await Beat(c, "p")), "p's heartbeat answer after the retake of 1 landed");
        await c.S.ReleaseAsync([at[1]]);
        Context.Equal("", Ids(await c.S.TakeAsync([1], "q", Context.Long)), "q's take of 1 after p's release at the epoch before the retake");
        await c.S.ReleaseAsync([at[1] with { Epoch = at[1].Epoch + 1 }]);
        Context.Equal("1", Ids(await c.S.TakeAsync([1], "q", Context.Long)), "q's take of 1 after p's release at the listed epoch");

        // p resets and retakes 4 and 5 one at a time: each answer lists the epochs at its own time.
        await c.S.RetakeAsync(4, "p", at[4].Epoch, Context.Long);
        Context.Equal($"{Next(4)} {At(at[5])}", Listed(await Beat(c, "p")), "p's heartbeat answer between its retakes");
        await c.S.RetakeAsync(5, "p", at[5].Epoch, Context.Long);
        Context.Equal(Next(4, 5), Listed(await Beat(c, "p")), "p's heartbeat answer after its retakes");

        // A retake renews its taker: s, near the end of a short lease, retakes 0, and is still live after that lease.
        var s = await c.S.TakeAsync([0], "s", Short);
        await Task.Delay(Short / 3);
        Context.That(await c.S.RetakeAsync(0, "s", s.Taken[0].Epoch, Context.Long) is not null, "s's retake of 0 was refused");
        await Task.Delay(PastShort);
        Context.That(!(await Beat(c, "q")).Cluster.Free.Contains(0), "partition 0 is free after s's retake renewed s");
        Context.Equal("", Ids(await c.S.TakeAsync([0], "q", Context.Long)), "q's take of 0 from s, renewed by its retake");
    }
}
