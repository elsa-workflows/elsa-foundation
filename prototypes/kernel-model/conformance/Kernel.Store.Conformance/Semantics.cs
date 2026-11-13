using System.Diagnostics;

namespace Kernel.Store.Conformance;

/// <summary>
/// Required: the kernel depends on it. Reference: what <see cref="InMemoryStore"/> does where the contract is silent
/// and the kernel never relies on it; a provider that differs is reported, not failed.
/// </summary>
public enum Level { Required, Reference }

public sealed record Case(string Name, Level Level, Func<Context, Task> Body);

public sealed record Outcome(Case Case, bool Passed, bool Skipped, string Detail, TimeSpan Took);

public sealed class ConformanceException(string message) : Exception(message);

public sealed class SkipException(string message) : Exception(message);

/// <summary>A fresh store for one case, with helpers.</summary>
public sealed class Context(ProvidedStore provided)
{
    public ProvidedStore Provided => provided;
    public IKernelStore S => provided.Store;
    public List<string> Notes { get; } = [];

    public static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    private readonly Dictionary<(int, long), TimeSpan> _leases = [];

    /// <summary>A commit renews the lease for as long as the take that started the epoch asked for.</summary>
    private TimeSpan LeaseOf(int partition, long epoch) => _leases.GetValueOrDefault((partition, epoch), Long);

    public async Task<Lease> Take(int partition, string node = "a", TimeSpan? lease = null)
    {
        var taken = await S.TakeAsync(partition, node, lease ?? Long);
        That(taken is not null, $"take of partition {partition} by {node} returned null");
        _leases[(partition, taken!.Epoch)] = lease ?? Long;
        return taken!;
    }

    /// <summary>A take as the kernel's host does it: a failure is tried again.</summary>
    public async Task<Lease> TakeRetrying(int partition, string node)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (await S.TakeAsync(partition, node, Long) is { } lease) return lease;
                That(attempt < 100, $"{node} could not take partition {partition} in 100 attempts");
            }
            catch (Exception e) when (attempt < 100) { Notes.Add("take retried: " + (e is StoreException s ? s.Failure.ToString() : e.GetType().Name)); }
            await Task.Delay(20);
        }
    }

    /// <summary>The store's time, read with a poll that can return nothing.</summary>
    public async Task<long> Now(int partition = 0) => (await S.PollAsync(partition, 0, TimeSpan.Zero, 1)).Now;

    /// <summary>Every row of the partition that is not done, whatever its mark, due or not.</summary>
    public async Task<IReadOnlyList<Stored>> All(int partition) => (await S.PollAsync(partition, long.MaxValue, TimeSpan.FromDays(3650), 100_000)).Messages;

    public async Task<Stored?> Row(int partition, string id) => (await All(partition)).FirstOrDefault(m => m.Message.Id == id);

    public Task<Committed> Commit(Lease lease, int partition, IReadOnlyList<TurnCommit> turns, IReadOnlyList<string>? seen = null, IReadOnlyList<string>? trial = null, bool handOver = false, TimeSpan? leaseFor = null) =>
        S.CommitAsync(new CommitBatch(partition, lease.Epoch, lease.Position, leaseFor ?? LeaseOf(partition, lease.Epoch), handOver, turns, seen ?? [], trial ?? [])).AsTask();

    public Task<Committed> Commit(int partition, long epoch, long position, IReadOnlyList<TurnCommit> turns, IReadOnlyList<string>? seen = null, IReadOnlyList<string>? trial = null) =>
        S.CommitAsync(new CommitBatch(partition, epoch, position, LeaseOf(partition, epoch), false, turns, seen ?? [], trial ?? [])).AsTask();

    /// <summary>A commit that must land. Transient failures are tried again, as the kernel does.</summary>
    public async Task<Committed> Lands(Func<Task<Committed>> commit, string what = "the commit")
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var result = await commit();
                That(result.Landed, $"{what} did not land");
                return result;
            }
            catch (StoreException e) when (e.Failure == Failure.Transient && attempt < 20) { await Task.Delay(20); }
        }
    }

    /// <summary>
    /// A commit that must not land: the fence matched nothing, or the store refused it. Returns how it was refused
    /// ("fence", "unknown", "definite"), since the contract does not say which a count that does not match is.
    /// </summary>
    public async Task<string> Refused(Func<Task<Committed>> commit, string what = "the commit")
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var result = await commit();
                That(!result.Landed, $"{what} landed");
                return "not landed";
            }
            catch (StoreException e) when (e.Failure == Failure.Transient && attempt < 20) { await Task.Delay(20); }
            catch (StoreException e) { return e.Failure.ToString().ToLowerInvariant(); }
        }
    }

    public static void That(bool condition, string message)
    {
        if (!condition) throw new ConformanceException(message);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new ConformanceException($"{what}: expected {expected}, got {actual}");
    }
}

/// <summary>Builders for the records of a commit.</summary>
public static class Make
{
    public static Message M(string id, int partition, string kind = "k", string? payload = null, long? due = null, long? keep = null, string target = "x") =>
        new(id, InstanceRef.In(partition, target), kind, payload, Due: due, KeepUntil: keep);

    public static InstanceRow Row(string id, long revision, string state = "{}", InstanceStatus status = InstanceStatus.Running) =>
        new(id, "m", "d", status, state, revision, null);

    public static TurnCommit Turn(string instance, InstanceRow? row = null, long? expected = null, IReadOnlyList<string>? consumed = null, IReadOnlyList<string>? done = null,
        IReadOnlyList<Message>? sent = null, IReadOnlyList<SubscriptionRow>? subscribed = null, IReadOnlyList<SubscriptionRow>? unsubscribed = null, string? journal = "[]",
        bool unsubscribe = false, IReadOnlyList<string>? held = null) =>
        new(instance, row, expected, consumed ?? [], done ?? [], sent ?? [], subscribed ?? [], unsubscribed ?? [], journal, unsubscribe, held);

    /// <summary>A delete of an ended instance at its revision, as the kernel writes it: its waits go too, and its own journal row says so.</summary>
    public static TurnCommit Delete(string instance, long expected, IReadOnlyList<string>? consumed = null) =>
        new(instance, null, expected, consumed ?? [], [], [], [], [], "[\"deleted\"]", Unsubscribe: true, Delete: true);

    /// <summary>A new instance, created with a journal row: the commonest turn.</summary>
    public static TurnCommit New(string instance, IReadOnlyList<Message>? sent = null) => Turn(instance, Row(instance, 1), sent: sent);
}

/// <summary>The semantic tests of each <see cref="IKernelStore"/> operation, as the contract and the in-memory store define them.</summary>
public static class Semantics
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PastShort = TimeSpan.FromMilliseconds(700);

    public static IReadOnlyList<Case> Cases { get; } =
    [
        // ---- take and release ----------------------------------------------------------------------------------------
        new("take: a free partition", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            Context.That(lease.Epoch > 0, $"epoch {lease.Epoch} after the first take");
            Context.Equal(0L, lease.Position, "position of a new partition");
            var now = await c.Now();
            Context.That(Math.Abs(now - lease.Now) < 5_000, $"the take's time {lease.Now} and a poll's time {now} are far apart");
        }),
        new("take: by its owner again bumps the epoch", Level.Required, async c =>
        {
            var first = await c.Take(0);
            var second = await c.Take(0);
            Context.Equal(first.Epoch + 1, second.Epoch, "epoch after a second take by the owner");
        }),
        new("take: refused while another owner's lease is live", Level.Required, async c =>
        {
            await c.Take(0, "a");
            Context.That(await c.S.TakeAsync(0, "b", Context.Long) is null, "b took a partition whose lease is live");
            Context.That(await c.S.TakeAsync(1, "b", Context.Long) is not null, "another partition is not free");
        }),
        new("take: allowed once the lease expired; position carries over", Level.Required, async c =>
        {
            var a = await c.Take(0, "a", Short);
            await c.Lands(() => c.Commit(a, 0, [Make.New("0/i")]));
            await Task.Delay(PastShort);
            var b = await c.Take(0, "b");
            Context.Equal(a.Epoch + 1, b.Epoch, "epoch after the takeover");
            Context.Equal(1L, b.Position, "position after the takeover");
        }),
        new("take: a death adds one attempt to exactly what the lost owner had on trial", Level.Required, async c =>
        {
            foreach (var id in new[] { "t1", "t2", "s1", "n1" }) await c.S.AddAsync(Make.M(id, 0));
            await c.S.AddAsync(Make.M("o1", 1));
            var a = await c.Take(0, "a", Short);
            await c.Lands(() => c.Commit(a, 0, [], seen: ["s1"], trial: ["t1", "t2"]));
            // Partition 1 has an epoch with the same number, and its row is on trial there: it must not be counted.
            var other = await c.Take(1, "c");
            Context.Equal(a.Epoch, other.Epoch, "partition 1's first epoch (this case needs it equal to partition 0's)");
            await c.Lands(() => c.Commit(other, 1, [], trial: ["o1"]));
            await Task.Delay(PastShort);
            await c.Take(0, "b");
            var rows = (await c.All(0)).ToDictionary(r => r.Message.Id);
            Context.Equal(1, rows["t1"].Attempts, "attempts of t1, on trial");
            Context.Equal(1, rows["t2"].Attempts, "attempts of t2, on trial");
            Context.Equal(0, rows["s1"].Attempts, "attempts of s1, only seen");
            Context.Equal(0, rows["n1"].Attempts, "attempts of n1, never held");
            Context.Equal(0, (await c.Row(1, "o1"))!.Attempts, "attempts of o1, on trial in another partition");
        }),
        new("take: only rows on trial at the lost owner's last epoch are counted", Level.Required, async c =>
        {
            await c.S.AddAsync(Make.M("old", 0));
            await c.S.AddAsync(Make.M("new", 0));
            var first = await c.Take(0, "a");
            await c.Lands(() => c.Commit(first, 0, [], trial: ["old"]));
            var second = await c.Take(0, "a", Short);
            await c.Lands(() => c.Commit(second, 0, [], trial: ["new"]));
            await Task.Delay(PastShort);
            await c.Take(0, "b");
            Context.Equal(0, (await c.Row(0, "old"))!.Attempts, "attempts of a row on trial at an earlier epoch of the lost owner");
            Context.Equal(1, (await c.Row(0, "new"))!.Attempts, "attempts of the row on trial at its last epoch");
        }),
        new("take: by the same node after its lease expired is not a death", Level.Required, async c =>
        {
            await c.S.AddAsync(Make.M("t", 0));
            var a = await c.Take(0, "a", Short);
            await c.Lands(() => c.Commit(a, 0, [], trial: ["t"]));
            await Task.Delay(PastShort);
            await c.Take(0, "a");
            Context.Equal(0, (await c.Row(0, "t"))!.Attempts, "attempts after the same node took again");
        }),
        new("release: frees the partition at once and is not a death", Level.Required, async c =>
        {
            await c.S.AddAsync(Make.M("t", 0));
            var a = await c.Take(0, "a");
            await c.Lands(() => c.Commit(a, 0, [], trial: ["t"]));
            await c.S.ReleaseAsync(0, a.Epoch);
            var b = await c.S.TakeAsync(0, "b", Context.Long);
            Context.That(b is not null, "b could not take a released partition");
            Context.Equal(0, (await c.Row(0, "t"))!.Attempts, "attempts after a release");
        }),
        new("release: with an old epoch changes nothing", Level.Required, async c =>
        {
            var first = await c.Take(0, "a");
            await c.Take(0, "a");
            await c.S.ReleaseAsync(0, first.Epoch);
            Context.That(await c.S.TakeAsync(0, "b", Context.Long) is null, "a release with an old epoch freed the partition");
        }),

        // ---- the fenced commit --------------------------------------------------------------------------------------
        new("commit: lands and writes every record kind", Level.Required, async c =>
        {
            await c.S.AddAsync(Make.M("in", 0));
            await c.S.AddAsync(Make.M("cmd", 0, keep: await c.Now() + 60_000));
            await c.S.AddAsync(Make.M("held", 0));
            await c.S.AddAsync(Make.M("tried", 0));
            var lease = await c.Take(0);
            var sent = new[] { Make.M("0/i#1.0000", 0, target: "j"), Make.M("0/i#1.0001", 1, target: "k") };
            var sub = new SubscriptionRow("key", "0/i", "w");
            var result = await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/i", Make.Row("0/i", 1, "{\"n\":1}"), consumed: ["in"], done: ["cmd"], sent: sent, subscribed: [sub], journal: "[\"j\"]")],
                seen: ["held"], trial: ["tried"]));
            Context.Equal(2, result.Seqs.Count, "seqs returned");
            Context.That(result.Seqs[0] < result.Seqs[1], "seqs are not increasing in the order sent");
            var row = (await c.S.LoadAsync(["0/i"])).Single();
            Context.Equal(Make.Row("0/i", 1, "{\"n\":1}"), row, "the instance row");
            var rows = (await c.All(0)).ToDictionary(r => r.Message.Id);
            Context.That(!rows.ContainsKey("in"), "the consumed message is still there");
            Context.That(!rows.ContainsKey("cmd"), "the done command is still polled");
            Context.Equal(lease.Epoch, rows["held"].Seen, "seen of the held message");
            Context.Equal((lease.Epoch, lease.Epoch), (rows["tried"].Seen, rows["tried"].Trial), "seen and trial of the tried message");
            Context.Equal(result.Seqs[0], rows["0/i#1.0000"].Seq, "seq of the sent message as stored");
            Context.Equal(sent[1], (await c.Row(1, "0/i#1.0001"))!.Message, "the message sent to partition 1");
            Context.Equal(sub, (await c.S.FindSubscriptionsAsync("key")).Single(), "the subscription");
            var journal = (await c.S.ReadJournalAsync(0, 0, 10)).Single();
            Context.Equal(new JournalRow(0, 1, "0/i", 1, "[\"j\"]"), journal, "the journal row");
            await c.Lands(() => c.Commit(0, lease.Epoch, 1, [Make.New("0/next")]), "a commit at the moved position");
        }),
        new("commit: refused for an old epoch, and writes nothing", Level.Required, async c =>
        {
            await c.S.AddAsync(Make.M("in", 0));
            await c.S.AddAsync(Make.M("held", 0));
            var old = await c.Take(0);
            await c.Take(0);
            await c.Refused(() => c.Commit(old, 0, [Make.Turn("0/i", Make.Row("0/i", 1), consumed: ["in"], sent: [Make.M("0/i#1.0000", 1)], subscribed: [new("key", "0/i", "w")])], seen: ["held"]));
            await NothingWritten(c);
        }),
        new("commit: refused for a wrong position, and writes nothing", Level.Required, async c =>
        {
            await c.S.AddAsync(Make.M("in", 0));
            await c.S.AddAsync(Make.M("held", 0));
            var lease = await c.Take(0);
            await c.Refused(() => c.Commit(0, lease.Epoch, lease.Position + 5, [Make.Turn("0/i", Make.Row("0/i", 1), consumed: ["in"], sent: [Make.M("0/i#1.0000", 1)], subscribed: [new("key", "0/i", "w")])], seen: ["held"]));
            await NothingWritten(c);
        }),
        Atomic("an update at the wrong revision", async (c, _) =>
        {
            var taken = await c.Take(0);
            await c.Lands(() => c.Commit(0, taken.Epoch, 0, [Make.New("0/b")]));
            return Make.Turn("0/b", Make.Row("0/b", 3), expected: 2);
        }),
        Atomic("a create of a row that exists", async (c, _) =>
        {
            var taken = await c.Take(0);
            await c.Lands(() => c.Commit(0, taken.Epoch, 0, [Make.New("0/b")]));
            return Make.Turn("0/b", Make.Row("0/b", 1));
        }),
        Atomic("a consume of a message that is not there", (_, _) => Task.FromResult(Make.Turn("0/b", Make.Row("0/b", 1), consumed: ["ghost"]))),
        Atomic("a consume of a message that is done", async (c, now) =>
        {
            await c.S.AddAsync(Make.M("cmd", 0, keep: now + 60_000));
            var taken = await c.Take(0);
            await c.Lands(() => c.Commit(0, taken.Epoch, 0, [Make.Turn("0/b", Make.Row("0/b", 1), done: ["cmd"])]));
            return Make.Turn("0/c", Make.Row("0/c", 1), done: ["cmd"]);
        }),
        Atomic("a send of a message id that exists", async (c, _) =>
        {
            await c.S.AddAsync(Make.M("dup", 1));
            return Make.Turn("0/b", Make.Row("0/b", 1), sent: [Make.M("dup", 1)]);
        }),
        new("commit: revision checks", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/i")]));
            await c.Lands(() => c.Commit(0, lease.Epoch, 1, [Make.Turn("0/i", Make.Row("0/i", 2, "{\"v\":2}"), expected: 1)]), "an update at the right revision");
            await c.Refused(() => c.Commit(0, lease.Epoch, 2, [Make.Turn("0/i", Make.Row("0/i", 3), expected: 1)]), "an update at an old revision");
            Context.Equal(2L, (await c.S.LoadAsync(["0/i"])).Single().Revision, "revision after a refused update");
            // A turn may commit without a journal row (a history level that records nothing): the position stays.
            await c.Lands(() => c.Commit(0, lease.Epoch, 2, [Make.Turn("0/i", Make.Row("0/i", 3), expected: 2, journal: null)]), "an update without a journal row");
            await c.Lands(() => c.Commit(0, lease.Epoch, 2, []), "a commit at the unchanged position");
        }),
        new("commit: consumed is deleted, done stays as a marker that refuses a second add", Level.Required, async c =>
        {
            var keep = await c.Now() + 60_000;
            await c.S.AddAsync(Make.M("eat", 0));
            await c.S.AddAsync(Make.M("cmd", 0, keep: keep));
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/i", Make.Row("0/i", 1), consumed: ["eat"], done: ["cmd"])]));
            Context.Equal(0, (await c.All(0)).Count, "rows polled after consuming both");
            Context.That(await c.S.AddAsync(Make.M("eat", 0)), "a consumed message's id cannot be added again");
            Context.That(!await c.S.AddAsync(Make.M("cmd", 0, keep: keep)), "a done command was added again inside its window");
            await c.Refused(() => c.Commit(0, lease.Epoch, 1, [Make.Turn("0/j", Make.Row("0/j", 1), done: ["cmd"])]), "marking a done command done again");
            await c.Lands(() => c.Commit(0, lease.Epoch, 1, [Make.Turn("0/j", Make.Row("0/j", 1), consumed: ["eat"])]), "consuming the re-added message");
            await c.Refused(() => c.Commit(0, lease.Epoch, 2, [Make.Turn("0/k", Make.Row("0/k", 1), consumed: ["eat"])]), "consuming a message twice");
        }),
        new("commit: seen and trial marks", Level.Required, async c =>
        {
            foreach (var id in new[] { "m1", "m2", "m3" }) await c.S.AddAsync(Make.M(id, 0));
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [], seen: ["m1"], trial: ["m2"]));
            var now = (await c.S.PollAsync(0, lease.Epoch, TimeSpan.Zero, 10)).Messages;
            Context.Equal("m3", string.Join(",", now.Select(m => m.Message.Id)), "a poll at the marking epoch");
            var later = (await c.S.PollAsync(0, lease.Epoch + 1, TimeSpan.Zero, 10)).Messages.ToDictionary(m => m.Message.Id);
            Context.Equal((lease.Epoch, 0L), (later["m1"].Seen, later["m1"].Trial), "m1 seen, trial");
            Context.Equal((lease.Epoch, lease.Epoch), (later["m2"].Seen, later["m2"].Trial), "m2 seen, trial");
            Context.Equal((0L, 0L), (later["m3"].Seen, later["m3"].Trial), "m3 seen, trial");
        }),
        new("commit: a hand-over writes the partition's own messages as seen", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/i", [Make.M("0/i#1.0000", 0), Make.M("0/i#1.0001", 1)])], handOver: true));
            await c.Lands(() => c.Commit(0, lease.Epoch, 1, [Make.New("0/j", [Make.M("0/j#1.0000", 0)])]));
            Context.Equal(lease.Epoch, (await c.Row(0, "0/i#1.0000"))!.Seen, "seen of a message handed over in its own partition");
            Context.Equal(0L, (await c.Row(1, "0/i#1.0001"))!.Seen, "seen of a message to another partition");
            Context.Equal(0L, (await c.Row(0, "0/j#1.0000"))!.Seen, "seen of an own message without hand-over");
        }),
        new("commit: an empty commit renews the lease", Level.Required, async c =>
        {
            var lease = await c.Take(0, "a", TimeSpan.FromMilliseconds(600));
            await Task.Delay(350);
            await c.Lands(() => c.Commit(lease, 0, [], leaseFor: TimeSpan.FromMilliseconds(600)), "the beat");
            await Task.Delay(350);
            Context.That(await c.S.TakeAsync(0, "b", Context.Long) is null, "b took the partition although the owner's beat renewed the lease");
            await Task.Delay(500);
            Context.That(await c.S.TakeAsync(0, "b", Context.Long) is not null, "b could not take after the renewed lease expired");
        }),
        new("commit: an empty commit tells a stale owner", Level.Required, async c =>
        {
            var old = await c.Take(0);
            await c.Take(0);
            await c.Refused(() => c.Commit(old, 0, []), "a beat at an old epoch");
        }),
        new("commit: seqs of one commit follow the order sent (the kernel's fixed-width ids)", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            var sent = Enumerable.Range(0, 12).Select(i => Make.M($"0/a#1.{i:D4}", 1, target: "b")).ToList();
            var result = await c.Lands(() => c.Commit(lease, 0, [Make.New("0/a", sent)]));
            await SentInOrder(c, sent, result);
        }),
        new("commit: seqs of one commit follow the order sent, whatever the ids", Level.Reference, async c =>
        {
            var lease = await c.Take(0);
            var sent = new[] { "z", "b", "y", "a", "x", "c", "w", "d", "v", "e", "u", "f" }.Select(id => Make.M(id, 1, target: "b")).ToList();
            var result = await c.Lands(() => c.Commit(lease, 0, [Make.New("0/a", sent)]));
            await SentInOrder(c, sent, result);
        }),
        new("order: a later send from the same sender has a larger key, across a takeover by another node", Level.Required, async c =>
        {
            var a = await c.Take(0, "a", Short);
            await c.Lands(() => c.Commit(a, 0, [Make.New("0/s", [Make.M("0/s#1.0000", 1)])]));
            await Task.Delay(PastShort);
            var b = await c.Take(0, "b");
            await c.Lands(() => c.Commit(b, 0, [Make.Turn("0/s", Make.Row("0/s", 2), expected: 1, sent: [Make.M("0/s#2.0000", 1)])]));
            var rows = (await c.All(1)).ToDictionary(r => r.Message.Id);
            Context.That(rows["0/s#1.0000"].Seq < rows["0/s#2.0000"].Seq, $"the later send has key {rows["0/s#2.0000"].Seq}, the earlier {rows["0/s#1.0000"].Seq}");
        }),
        new("order: outside adds one after another, from different clients, get increasing keys", Level.Required, async c =>
        {
            var clients = new[] { c.S, c.Provided.NewClient(), c.Provided.NewClient() };
            for (var i = 0; i < 12; i++) await clients[i % 3].AddAsync(Make.M($"o{i:D2}", 0));
            var seqs = (await c.All(0)).OrderBy(r => r.Message.Id, StringComparer.Ordinal).Select(r => r.Seq).ToList();
            Context.That(seqs.Zip(seqs.Skip(1)).All(p => p.First < p.Second), $"keys in the order added: {string.Join(" ", seqs)}");
        }),
        new("order: an add after a commit that sent to the same partition, from another client, gets a larger key", Level.Reference, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/s", [Make.M("0/s#1.0000", 1)])]));
            await c.Provided.NewClient().AddAsync(Make.M("after", 1));
            var rows = (await c.All(1)).ToDictionary(r => r.Message.Id);
            Context.That(rows["0/s#1.0000"].Seq < rows["after"].Seq, $"the add has key {rows["after"].Seq}, the commit's send {rows["0/s#1.0000"].Seq}");
        }),

        // ---- concurrency of the fence --------------------------------------------------------------------------------
        new("fence: of many concurrent commits at one position, exactly one lands", Level.Required, async c =>
        {
            for (var round = 0; round < 5; round++)
            {
                var lease = await c.Take(0);
                var outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
                {
                    for (var attempt = 0; ; attempt++)
                        try { return (await c.Commit(lease, 0, [Make.New($"0/r{round}-{i}")])).Landed; }
                        catch (StoreException e) when (e.Failure == Failure.Transient && attempt < 50) { await Task.Delay(Random.Shared.Next(5)); }
                })));
                Context.Equal(1, outcomes.Count(o => o), $"commits that landed at position {lease.Position}");
                var journal = await c.S.ReadJournalAsync(0, lease.Position, 100);
                Context.Equal(1, journal.Count, $"journal rows after position {lease.Position}");
                var rows = await c.S.LoadAsync(Enumerable.Range(0, 12).Select(i => $"0/r{round}-{i}").ToArray());
                Context.Equal(journal[0].Instance, rows.Single().Id, "the one instance written");
            }
        }),
        new("fence: a take beside a commit in flight reads the true position", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            for (var round = 0; round < 20; round++)
            {
                var commit = Task.Run(async () =>
                {
                    try { return (await c.Commit(lease, 0, [Make.New($"0/r{round}")])).Landed; }
                    catch (StoreException e) when (e.Failure == Failure.Transient) { return false; }
                });
                var taken = await c.TakeRetrying(0, "a");
                var landed = await commit;
                var journal = await c.S.ReadJournalAsync(0, 0, 1000);
                Context.Equal((long)journal.Count, taken.Position, $"round {round}: the commit {(landed ? "landed" : "did not land")}; position read by the take against journal rows");
                c.Notes.Add(landed ? "commit first" : "take first");
                lease = taken;
            }
        }),

        // ---- poll ------------------------------------------------------------------------------------------------------
        new("poll: seq order and the limit", Level.Required, async c =>
        {
            for (var i = 0; i < 5; i++) await c.S.AddAsync(Make.M($"p{4 - i}", 0));
            var three = (await c.S.PollAsync(0, 1, TimeSpan.Zero, 3)).Messages;
            Context.Equal("p4,p3,p2", string.Join(",", three.Select(m => m.Message.Id)), "a poll with limit 3");
            var all = (await c.S.PollAsync(0, 1, TimeSpan.Zero, 10)).Messages;
            Context.Equal(5, all.Count, "a poll with limit 10");
            Context.That(all.Zip(all.Skip(1)).All(p => p.First.Seq < p.Second.Seq), "rows are not in seq order");
        }),
        new("poll: due within the horizon", Level.Required, async c =>
        {
            var now = await c.Now();
            await c.S.AddAsync(Make.M("past", 0, due: now - 1_000));
            await c.S.AddAsync(Make.M("soon", 0, due: now + 5_000));
            await c.S.AddAsync(Make.M("far", 0, due: now + 600_000));
            await c.S.AddAsync(Make.M("none", 0));
            string Ids(Polled p) => string.Join(",", p.Messages.Select(m => m.Message.Id).Order(StringComparer.Ordinal));
            Context.Equal("none,past", Ids(await c.S.PollAsync(0, 1, TimeSpan.Zero, 10)), "horizon 0");
            Context.Equal("none,past,soon", Ids(await c.S.PollAsync(0, 1, TimeSpan.FromSeconds(60), 10)), "horizon 60 s");
            Context.Equal("far,none,past,soon", Ids(await c.S.PollAsync(0, 1, TimeSpan.FromHours(1), 10)), "horizon 1 h");
            Context.Equal(now + 5_000, (await c.Row(0, "soon"))!.Message.Due, "due as stored");
            Context.Equal((long?)null, (await c.Row(0, "none"))!.Message.Due, "due of a message due at once");
        }),
        new("poll: only seen < epoch, never done, only its partition, and the store's time when empty", Level.Required, async c =>
        {
            await c.S.AddAsync(Make.M("mine", 0));
            await c.S.AddAsync(Make.M("other", 1));
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [], seen: ["mine"]));
            var polled = await c.S.PollAsync(0, lease.Epoch, TimeSpan.Zero, 10);
            Context.Equal(0, polled.Messages.Count, "rows polled at the epoch they were seen at");
            Context.That(Math.Abs(polled.Now - lease.Now) < 5_000, $"an empty poll's time {polled.Now} is far from the take's {lease.Now}");
            Context.Equal("mine", (await c.S.PollAsync(0, lease.Epoch + 1, TimeSpan.Zero, 10)).Messages.Single().Message.Id, "a poll at the next epoch");
            Context.Equal("other", (await c.S.PollAsync(1, 1, TimeSpan.Zero, 10)).Messages.Single().Message.Id, "partition 1");
        }),
        new("poll: the store's time moves on", Level.Required, async c =>
        {
            var first = await c.Now();
            await Task.Delay(250);
            var second = await c.Now();
            Context.That(second - first is >= 150 and < 5_000, $"250 ms of waiting moved the store's time by {second - first} ms");
        }),

        // ---- add, load ---------------------------------------------------------------------------------------------
        new("add: create-only, and every field comes back", Level.Required, async c =>
        {
            var now = await c.Now();
            var message = new Message("full", InstanceRef.In(0, "t"), "kind", "{\"a\":1}", "1/sender", new StartSpec("model", "def", "{\"in\":2}", "0/parent", true, History.State), now + 1_000, now + 60_000, "node-b|request-7");
            Context.That(await c.S.AddAsync(message), "the first add");
            Context.That(!await c.S.AddAsync(message with { Payload = "changed" }), "a second add with the same id");
            var row = (await c.Row(0, "full"))!;
            Context.Equal(message, row.Message, "the message as polled");
            Context.Equal((0L, 0, 0L), (row.Seen, row.Attempts, row.Trial), "seen, attempts, trial of a new row");
        }),
        new("load: rows by id, every field, missing ids skipped", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            var faulted = new InstanceRow("0/f", "model", "def", InstanceStatus.Faulted, "{\"s\":[1,2]}", 1, "0/p", "it broke", "[\"k1\"]");
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/f", faulted), Make.New("0/g")]));
            var rows = (await c.S.LoadAsync(["0/f", "0/g", "0/missing"])).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            Context.Equal(2, rows.Count, "rows loaded");
            Context.Equal(faulted, rows[0], "the faulted row");
            Context.Equal(Make.Row("0/g", 1), rows[1], "the plain row");
            Context.Equal(0, (await c.S.LoadAsync([])).Count, "rows loaded for no ids");
        }),

        // ---- events and subscriptions --------------------------------------------------------------------------------
        new("subscriptions: added and removed by commits, found by key", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            SubscriptionRow s1 = new("k1", "0/a", "w"), s2 = new("k1", "0/b", "w"), s3 = new("k2", "0/a", "w");
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/a", Make.Row("0/a", 1), subscribed: [s1, s3]), Make.Turn("0/b", Make.Row("0/b", 1), subscribed: [s2])]));
            Context.Equal("0/a,0/b", string.Join(",", (await c.S.FindSubscriptionsAsync("k1")).Select(s => s.Instance).Order(StringComparer.Ordinal)), "subscriptions of k1");
            await c.Lands(() => c.Commit(0, lease.Epoch, 2, [Make.Turn("0/a", Make.Row("0/a", 2), expected: 1, unsubscribed: [s1])]));
            Context.Equal(s2, (await c.S.FindSubscriptionsAsync("k1")).Single(), "k1 after unsubscribing 0/a");
            Context.Equal(s3, (await c.S.FindSubscriptionsAsync("k2")).Single(), "k2");
            Context.Equal(0, (await c.S.FindSubscriptionsAsync("k3")).Count, "a key nobody waits for");
        }),
        new("subscriptions: subscribing twice keeps one", Level.Reference, async c =>
        {
            var lease = await c.Take(0);
            SubscriptionRow s = new("k", "0/a", "w");
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/a", Make.Row("0/a", 1), subscribed: [s])]));
            await c.Lands(() => c.Commit(0, lease.Epoch, 1, [Make.Turn("0/a", Make.Row("0/a", 2), expected: 1, subscribed: [s])]));
            Context.Equal(1, (await c.S.FindSubscriptionsAsync("k")).Count, "subscriptions after subscribing twice");
        }),
        new("subscriptions: unsubscribing what is not there changes nothing", Level.Reference, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/a", Make.Row("0/a", 1), unsubscribed: [new("k", "0/a", "w")])]));
        }),
        new("events: publish is create-only, find honours keep_until", Level.Required, async c =>
        {
            var now = await c.Now();
            await c.S.PublishAsync(new EventRow("k1", "e1", "{\"p\":1}", now + 60_000));
            await c.S.PublishAsync(new EventRow("k1", "e1", "changed", now + 60_000));
            await c.S.PublishAsync(new EventRow("k1", "e2", null, now + 60_000));
            await c.S.PublishAsync(new EventRow("k2", "e3", null, now - 1));
            var found = (await c.S.FindEventsAsync(["k1", "k2", "k3"])).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            Context.Equal("e1,e2", string.Join(",", found.Select(e => e.Id)), "events found");
            Context.Equal(new EventRow("k1", "e1", "{\"p\":1}", now + 60_000), found[0], "the first event as published");
            Context.Equal(0, (await c.S.FindEventsAsync([])).Count, "events for no keys");
        }),
        new("events: the publisher's delivery and the subscriber's are one message", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            var wait = new SubscriptionRow("k", "0/w", "w");
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/w", Make.Row("0/w", 1), subscribed: [wait])]));
            var e = new EventRow("k", "e1", "p", await c.Now() + 60_000);
            await Events.PublishAsync(c.S, e);
            Context.That(!await c.S.AddAsync(Events.Deliver(e, wait)), "the subscriber's delivery was added beside the publisher's");
            Context.Equal(Events.Deliver(e, wait), (await c.Row(0, Events.Deliver(e, wait).Id))!.Message, "the delivered message");
        }),

        // ---- journal and sweep ---------------------------------------------------------------------------------------
        new("journal: rows at positions n+1..n+k, read after a position", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/a"), Make.Turn("0/b", Make.Row("0/b", 1), journal: null), Make.Turn("0/c", null, journal: "[\"dropped\"]"), Make.New("0/d")]));
            var rows = await c.S.ReadJournalAsync(0, 0, 10);
            Context.Equal("1:0/a:1,2:0/c:0,3:0/d:1", string.Join(",", rows.Select(r => $"{r.Position}:{r.Instance}:{r.Revision}")), "journal rows");
            Context.Equal("2", string.Join(",", (await c.S.ReadJournalAsync(0, 1, 1)).Select(r => r.Position)), "one row after position 1");
            Context.Equal(0, (await c.S.ReadJournalAsync(1, 0, 10)).Count, "journal rows of partition 1");
            await c.Lands(() => c.Commit(0, lease.Epoch, 3, []), "a commit at position 3");
        }),
        new("sweep: deletes done commands and events whose window passed, nothing else", Level.Required, async c =>
        {
            var now = await c.Now();
            await c.S.AddAsync(Make.M("old-done", 0, keep: now + 300));
            await c.S.AddAsync(Make.M("old-open", 0, keep: now + 300));
            await c.S.AddAsync(Make.M("new-done", 0, keep: now + 600_000));
            await c.S.AddAsync(Make.M("plain", 0));
            await c.S.PublishAsync(new EventRow("k", "old", null, now + 300));
            await c.S.PublishAsync(new EventRow("k", "new", null, now + 600_000));
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/a", Make.Row("0/a", 1), done: ["old-done", "new-done"])]));
            await Task.Delay(PastShort);
            Context.Equal(2, await c.S.SweepAsync(), "rows swept");
            Context.Equal("old-open,plain", string.Join(",", (await c.All(0)).Select(m => m.Message.Id).Order(StringComparer.Ordinal)), "rows left");
            Context.That(await c.S.AddAsync(Make.M("old-done", 0)), "a swept command's id could not be added again");
            Context.That(!await c.S.AddAsync(Make.M("new-done", 0)), "a command inside its window was swept");
            Context.Equal("new", (await c.S.FindEventsAsync(["k"])).Single().Id, "events left");
        }),

        // ---- the three failure kinds ---------------------------------------------------------------------------------
        new("failure: a row the store refuses is definite, and the partition goes on", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            if (c.Provided.Refused(Make.Row("0/bad", 1)) is not { } bad) throw new SkipException("the provider cannot make a row its store refuses");
            try
            {
                await c.Commit(lease, 0, [Make.Turn("0/bad", bad)]);
                throw new ConformanceException("the refused row was not refused");
            }
            catch (StoreException e) { Context.Equal(Failure.Definite, e.Failure, "failure of a refused row"); }
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/good")]), "the next commit at the same position");
        }),
        new("failure: a commit beside another writer of the partition record waits or is transient, never anything else", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            var hold = await c.Provided.HoldPartitionAsync(0);
            if (hold is null) throw new SkipException("the provider cannot hold the partition record");
            var commit = c.Commit(lease, 0, [Make.New("0/a")]);
            if (await Task.WhenAny(commit, Task.Delay(500)) == commit)
            {
                await hold.DisposeAsync();
                try
                {
                    var result = await commit;
                    throw new ConformanceException($"beside a held partition record the commit returned at once, landed: {result.Landed}");
                }
                catch (StoreException e) { Context.Equal(Failure.Transient, e.Failure, "failure beside a held record"); }
                c.Notes.Add("transient at once");
                await c.Lands(() => c.Commit(lease, 0, [Make.New("0/a")]), "the commit tried again after the other writer let go");
            }
            else
            {
                c.Notes.Add("waits");
                await hold.DisposeAsync();
                Context.That((await commit).Landed, "the commit that waited did not land");
            }
            Context.Equal(1, (await c.S.ReadJournalAsync(0, 0, 10)).Count, "journal rows");
        }),
        new("failure: after an unknown outcome a take by the owner reads the true position", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            for (var round = 0; round < 10; round++)
            {
                using var cancel = new CancellationTokenSource();
                var commit = c.S.CommitAsync(new CommitBatch(0, lease.Epoch, lease.Position, Context.Long, false, [Make.New($"0/u{round}")], [], []), cancel.Token).AsTask();
                cancel.CancelAfter(round);
                string how;
                try { how = (await commit).Landed ? "landed" : "not landed"; }
                catch (Exception e) { how = e is StoreException s ? s.Failure.ToString() : "unknown"; }
                lease = await c.TakeRetrying(0, "a");
                var journal = await c.S.ReadJournalAsync(0, 0, 100);
                Context.Equal((long)journal.Count, lease.Position, $"round {round} ({how}): journal rows against the position");
                Context.Equal(journal.Count, (await c.S.LoadAsync(Enumerable.Range(0, round + 1).Select(i => $"0/u{i}").ToArray())).Count, $"round {round}: rows against journal rows");
                c.Notes.Add(how);
            }
        }),

        // ---- round 3's operations (merge) ------------------------------------------------------------------------------
        new("load: the kernel's columns of round 3 come back (history, suspended, children, state version)", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            var row = new InstanceRow("0/r", "model", "def", InstanceStatus.Running, "{}", 1, "0/p", null, "[\"k\"]", History.State, Suspended: true, Children: "[\"0/c\",\"1/d\"]", StateVersion: 3);
            var minimal = new InstanceRow("0/m", "model", "def", InstanceStatus.Running, "{}", 1, null, History: History.Minimal);
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/r", row), Make.Turn("0/m", minimal, journal: null)]));
            var rows = (await c.S.LoadAsync(["0/r", "0/m"])).ToDictionary(r => r.Id);
            Context.Equal(row, rows["0/r"], "the row with every column set");
            Context.Equal(minimal, rows["0/m"], "a row at history Minimal");
        }),
        new("heartbeat: the nodes heard from within the window, and the partitions nobody holds", Level.Required, async c =>
        {
            var first = await c.S.HeartbeatAsync("a", Context.Long);
            Context.Equal(1, first.Nodes, "nodes after the first beat");
            Context.Equal("0,1", string.Join(",", first.Free.Order()), "free partitions of a new store");
            Context.That(Math.Abs(first.Now - await c.Now()) < 5_000, $"the beat's time {first.Now} is far from a poll's");
            Context.Equal(2, (await c.S.HeartbeatAsync("b", Context.Long)).Nodes, "nodes after a second node beat");
            Context.Equal(2, (await c.S.HeartbeatAsync("a", Context.Long)).Nodes, "nodes after a node beat again");
            await c.Take(0, "a");
            await c.Take(1, "b", Short);
            Context.Equal("", string.Join(",", (await c.S.HeartbeatAsync("a", Context.Long)).Free), "free partitions while both leases live");
            await Task.Delay(PastShort);
            var late = await c.S.HeartbeatAsync("c", Short);
            Context.Equal(1, late.Nodes, "nodes heard from within a short window, after the others went quiet");
            Context.Equal("1", string.Join(",", late.Free), "free partitions once a lease expired");
            Context.Equal(3, (await c.S.HeartbeatAsync("a", Context.Long)).Nodes, "nodes within a long window");
        }),
        new("subscribe: a start subscription is kept once, and found by key with its spec, beside the waits", Level.Required, async c =>
        {
            var start = new SubscriptionRow("k", "", "on-k", new StartSpec("model", "def", "{\"i\":1}", null, true, History.State));
            await c.S.SubscribeAsync(start);
            await c.S.SubscribeAsync(start);
            Context.Equal(start, (await c.S.FindSubscriptionsAsync("k")).Single(), "the start subscription after two registrations");
            var lease = await c.Take(0);
            var wait = new SubscriptionRow("k", "0/w", "w");
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/w", Make.Row("0/w", 1), subscribed: [wait])]));
            var found = (await c.S.FindSubscriptionsAsync("k")).OrderBy(x => x.Instance, StringComparer.Ordinal).ToList();
            Context.Equal(2, found.Count, "subscriptions of the key");
            Context.Equal(start, found[0], "the start subscription");
            Context.Equal(wait, found[1], "the wait, which has no spec");
        }),
        new("commit: a delete removes the row at its revision and its instance's journal rows, and writes its own", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/d"), Make.New("0/o")]));
            await c.Lands(() => c.Commit(0, lease.Epoch, 2, [Make.Turn("0/d", Make.Row("0/d", 2, status: InstanceStatus.Finished), expected: 1)]));
            await c.Lands(() => c.Commit(0, lease.Epoch, 3, [Make.Delete("0/d", 2)]), "the delete");
            Context.Equal(0, (await c.S.LoadAsync(["0/d"])).Count, "rows of the deleted instance");
            Context.Equal(1, (await c.S.LoadAsync(["0/o"])).Count, "rows of the other instance");
            var journal = await c.S.ReadJournalAsync(0, 0, 10);
            Context.Equal("2:0/o:1,4:0/d:-1", string.Join(",", journal.Select(r => $"{r.Position}:{r.Instance}:{r.Revision}")), "journal rows after the delete");
            await c.Lands(() => c.Commit(0, lease.Epoch, 4, [Make.New("0/d")]), "a new instance of the same id at the moved position");
        }),
        Atomic("a delete at the wrong revision", async (c, _) =>
        {
            var taken = await c.Take(0);
            await c.Lands(() => c.Commit(0, taken.Epoch, 0, [Make.New("0/b")]));
            return Make.Delete("0/b", 2);
        }),
        Atomic("a hold of a message that is not there", (_, _) => Task.FromResult(Make.Turn("0/b", Make.Row("0/b", 1), held: ["ghost"]))),
        new("commit: unsubscribing all removes every wait of the instance, and only its", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            SubscriptionRow a1 = new("k1", "0/a", "w1"), a2 = new("k2", "0/a", "w2"), b1 = new("k1", "0/b", "w1");
            await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/a", Make.Row("0/a", 1), subscribed: [a1, a2]), Make.Turn("0/b", Make.Row("0/b", 1), subscribed: [b1])]));
            await c.Lands(() => c.Commit(0, lease.Epoch, 2, [Make.Turn("0/a", Make.Row("0/a", 2, status: InstanceStatus.Cancelled), expected: 1, unsubscribe: true)]));
            Context.Equal(b1, (await c.S.FindSubscriptionsAsync("k1")).Single(), "k1 after 0/a was ended");
            Context.Equal(0, (await c.S.FindSubscriptionsAsync("k2")).Count, "k2 after 0/a was ended");
        }),
        new("commit: a held message stays, keeps its mark, is no longer on trial, and a death does not count it", Level.Required, async c =>
        {
            await c.S.AddAsync(Make.M("h", 0));
            await c.S.AddAsync(Make.M("t", 0));
            var a = await c.Take(0, "a", Short);
            await c.Lands(() => c.Commit(a, 0, [], trial: ["h", "t"]));
            await c.Lands(() => c.Commit(a, 0, [Make.Turn("0/i", Make.Row("0/i", 1), held: ["h"])]), "the turn that held it");
            var held = (await c.Row(0, "h"))!;
            Context.Equal((a.Epoch, 0L), (held.Seen, held.Trial), "seen and trial of the held message");
            await Task.Delay(PastShort);
            await c.Take(0, "b");
            Context.Equal(0, (await c.Row(0, "h"))!.Attempts, "attempts of the held message after the owner died");
            Context.Equal(1, (await c.Row(0, "t"))!.Attempts, "attempts of the message still on trial");
        }),
        new("projection: both positions, a write that moves only from where it read, and the view rows", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/a"), Make.New("0/b")]));
            await c.Lands(() => c.Commit(0, lease.Epoch, 2, [Make.Turn("0/a", Make.Row("0/a", 2), expected: 1)]));
            Context.Equal(new Projection(0, 3), await c.S.ProjectedAsync(0), "projection of partition 0 before a round");
            Context.Equal(new Projection(0, 0), await c.S.ProjectedAsync(1), "projection of partition 1");
            var rows = await c.S.ReadJournalAsync(0, 0, 10);
            Context.That(await c.S.ProjectAsync(0, 0, 3, rows), "the first projection did not move");
            Context.That(!await c.S.ProjectAsync(0, 0, 3, rows), "a second projection from the same position moved");
            Context.Equal(new Projection(3, 3), await c.S.ProjectedAsync(0), "projection after the round");
            var views = (await c.S.ReadViewsAsync(["0/a", "0/b", "0/none"])).OrderBy(v => v.Instance, StringComparer.Ordinal).ToList();
            Context.Equal("0/a:2:2,0/b:1:1", string.Join(",", views.Select(v => $"{v.Instance}:{v.Revision}:{v.Commits}")), "view rows");
            await c.Lands(() => c.Commit(0, lease.Epoch, 3, [Make.Turn("0/a", Make.Row("0/a", 3), expected: 2), Make.Delete("0/b", 1)]));
            var next = await c.S.ReadJournalAsync(0, 3, 10);
            Context.That(await c.S.ProjectAsync(0, 3, 5, next), "the second round did not move");
            views = (await c.S.ReadViewsAsync(["0/a", "0/b"])).ToList();
            Context.Equal("0/a:3:3", string.Join(",", views.Select(v => $"{v.Instance}:{v.Revision}:{v.Commits}")), "view rows after a delete");
        }),
        new("projection: rounds larger than a statement's chunk: 850 views in one, then 600 deletes alone", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            var ids = Enumerable.Range(0, 850).Select(i => $"0/v{i:D3}").ToList();
            // In commits of 170 and 200 (the kernel flushes at most 500 rows per commit; MySQL's provider failed one commit of 850).
            for (var at = 0; at < 850; at += 170)
            {
                var (from, chunk) = (at, ids.Skip(at).Take(170).Select(id => Make.New(id)).ToList());
                await c.Lands(() => c.Commit(0, lease.Epoch, from, chunk));
            }
            Context.That(await c.S.ProjectAsync(0, 0, 850, await c.S.ReadJournalAsync(0, 0, 1000)), "the first round did not move");
            Context.Equal(850, (await c.S.ReadViewsAsync(ids)).Count(v => v is { Revision: 1, Commits: 1 }), "views after a round of 850 new instances");
            for (var at = 0; at < 600; at += 200)
            {
                var (from, chunk) = (850 + at, ids.Skip(at).Take(200).Select(id => Make.Delete(id, 1)).ToList());
                await c.Lands(() => c.Commit(0, lease.Epoch, from, chunk));
            }
            Context.That(await c.S.ProjectAsync(0, 850, 1450, await c.S.ReadJournalAsync(0, 850, 1000)), "the round of deletes did not move");
            var left = await c.S.ReadViewsAsync(ids);
            Context.Equal("250 from 0/v600", $"{left.Count} from {left.Select(v => v.Instance).Order(StringComparer.Ordinal).FirstOrDefault()}", "views after a round of 600 deletes");
        }),
        new("journal: one instance's rows past a position, in order", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/a"), Make.New("0/b")]));
            await c.Lands(() => c.Commit(0, lease.Epoch, 2, [Make.Turn("0/a", Make.Row("0/a", 2), expected: 1)]));
            await c.Lands(() => c.Commit(0, lease.Epoch, 3, [Make.Turn("0/a", Make.Row("0/a", 3), expected: 2), Make.Turn("0/b", Make.Row("0/b", 2), expected: 1)]));
            var rows = await c.S.ReadJournalOfAsync(0, "0/a", 1);
            Context.Equal("3:2,4:3", string.Join(",", rows.Select(r => $"{r.Position}:{r.Revision}")), "0/a's rows past position 1");
            Context.Equal(0, (await c.S.ReadJournalOfAsync(0, "0/none", 0)).Count, "rows of an instance that has none");
        }),
        new("viewer: the tail read is current when a round commits between its reads (decision 11)", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/a"), Make.New("0/b")]));
            Context.That(await c.S.ProjectAsync(0, 0, 2, await c.S.ReadJournalAsync(0, 0, 10)), "the first round did not move");
            await c.Lands(() => c.Commit(0, lease.Epoch, 2, [Make.Turn("0/a", Make.Row("0/a", 2), expected: 1), Make.Delete("0/b", 1)]));
            await c.Lands(() => c.Commit(0, lease.Epoch, 4, [Make.Turn("0/a", Make.Row("0/a", 3), expected: 2)]));
            var cursor = (await c.S.ProjectedAsync(0)).Position;       // the viewer reads the cursor first
            Context.That(await c.S.ProjectAsync(0, 2, 4, (await c.S.ReadJournalAsync(0, 2, 10)).Where(r => r.Position <= 4).ToList()), "the round between the reads did not move");
            string Show(ViewRow? v) => v is null ? "none" : $"{v.Revision}:{v.Commits}";
            foreach (var (id, expected) in new[] { ("0/a", "3:3"), ("0/b", "none") })
            {
                var view = Viewer.Merge(id, (await c.S.ReadViewsAsync([id])).SingleOrDefault(), await c.S.ReadJournalOfAsync(0, id, cursor));
                Context.Equal(expected, Show(view), $"the tail read of {id}");
            }
            Context.Equal("3:3", Show(await Viewer.ReadAsync(c.S, 0, "0/a")), "a tail read with nothing between its reads");
        }),
        new("projector: catch-up requests are coalesced and leave the projection current (decision 11)", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/a"), Make.New("0/b"), Make.New("0/c")]));
            var projector = new Projector(c.S, 0);
            var (first, second) = (projector.CatchUpAsync(), projector.CatchUpAsync());
            Context.That(first.IsCompleted || ReferenceEquals(first, second), "a request while another waits got a round of its own");
            await Task.WhenAll(first, second);
            Context.Equal(new Projection(3, 3), await c.S.ProjectedAsync(0), "the projection after the catch-up");
        }),
        new("projection: of many concurrent writes from one position, exactly one moves it", Level.Required, async c =>
        {
            var lease = await c.Take(0);
            await c.Lands(() => c.Commit(lease, 0, [Make.New("0/a")]));
            var rows = await c.S.ReadJournalAsync(0, 0, 10);
            var moved = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                for (var attempt = 0; ; attempt++)
                    try { return await c.S.ProjectAsync(0, 0, 1, rows); }
                    catch (StoreException e) when (e.Failure == Failure.Transient && attempt < 50) { await Task.Delay(Random.Shared.Next(5)); }
            })));
            Context.Equal(1, moved.Count(m => m), "writes that moved the projection");
            Context.Equal(1, (await c.S.ReadViewsAsync(["0/a"])).Single().Commits, "commits counted in the view");
        }),
        new("fence: a stale owner's delete, unsubscribe and hold after its successor's commit answer 'not landed' and write nothing", Level.Required, async c =>
        {
            // ENGINES.md E1, MONGO.md M5, for the writes round 3 added: with the fence in the batch the stale journal row
            // meets the successor's first, and the store must still answer as the fence, not with a key violation.
            await c.S.AddAsync(Make.M("m", 0));
            var old = await c.Take(0);
            var wait = new SubscriptionRow("k", "0/d", "w");
            await c.Lands(() => c.Commit(old, 0, [Make.Turn("0/d", Make.Row("0/d", 1, status: InstanceStatus.Finished), subscribed: [wait])], trial: ["m"]));
            var now = await c.Take(0);
            await c.Lands(() => c.Commit(now, 0, [Make.New("0/x")]), "the successor's commit");
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var result = await c.Commit(0, old.Epoch, 1, [Make.Delete("0/d", 1), Make.Turn("0/h", Make.Row("0/h", 1), held: ["m"])]);
                    Context.That(!result.Landed, "the stale owner's commit landed");
                    break;
                }
                catch (StoreException e) when (e.Failure == Failure.Transient && attempt < 20) { await Task.Delay(20); }
                catch (StoreException e) { throw new ConformanceException($"the stale owner's commit answered {e.Failure.ToString().ToLowerInvariant()}, not 'not landed' ({e.Message.Split('\n')[0]})"); }
            }
            Context.Equal(1, (await c.S.LoadAsync(["0/d"])).Count, "the instance the stale owner deleted");
            Context.Equal(wait, (await c.S.FindSubscriptionsAsync("k")).Single(), "the wait the stale owner removed");
            Context.Equal(old.Epoch, (await c.Row(0, "m"))!.Trial, "trial of the message the stale owner held");
            Context.Equal("1:0/d,2:0/x", string.Join(",", (await c.S.ReadJournalAsync(0, 0, 10)).Select(r => $"{r.Position}:{r.Instance}")), "journal rows");
        }),
    ];

    private static Case Atomic(string bad, Func<Context, long, Task<TurnCommit>> arrange) =>
        new($"commit: atomic, refused whole for {bad}", Level.Required, async c =>
        {
            var now = await c.Now();
            await c.S.AddAsync(Make.M("in", 0));
            await c.S.AddAsync(Make.M("cmd", 0, keep: now + 60_000));
            await c.S.AddAsync(Make.M("held", 0));
            await c.S.AddAsync(Make.M("tried", 0));
            var badTurn = await arrange(c, now);
            var lease = await c.Take(0);
            var before = await c.S.ReadJournalAsync(0, 0, 100);
            var good = Make.Turn("0/a", Make.Row("0/a", 1), consumed: ["in"], done: ["cmd"], sent: [Make.M("0/a#1.0000", 1)], subscribed: [new("key", "0/a", "w")]);
            c.Notes.Add(await c.Refused(() => c.Commit(lease, 0, [good, badTurn], seen: ["held"], trial: ["tried"])));
            Context.Equal(0, (await c.S.LoadAsync(["0/a"])).Count, "the good turn's instance row");
            var rows = (await c.All(0)).ToDictionary(r => r.Message.Id);
            Context.That(rows.ContainsKey("in"), "the good turn's consumed message is gone");
            Context.That(rows.ContainsKey("cmd") || badTurn.Done.Contains("cmd"), "the good turn's command is done");
            Context.Equal((0L, 0L), (rows["held"].Seen, rows["held"].Trial), "the seen mark");
            Context.Equal((0L, 0L), (rows["tried"].Seen, rows["tried"].Trial), "the trial mark");
            Context.That(await c.Row(1, "0/a#1.0000") is null, "the good turn's sent message");
            Context.Equal(0, (await c.S.FindSubscriptionsAsync("key")).Count, "the good turn's subscription");
            Context.Equal(before.Count, (await c.S.ReadJournalAsync(0, 0, 100)).Count, "journal rows");
            await c.Lands(() => c.Commit(0, lease.Epoch, before.Count, [Make.New("0/z")]), "a commit at the unchanged position");
        });

    private static async Task NothingWritten(Context c)
    {
        Context.Equal(0, (await c.S.LoadAsync(["0/i"])).Count, "instance rows");
        Context.That(await c.Row(0, "in") is not null, "the consumed message is gone");
        Context.Equal(0L, (await c.Row(0, "held"))!.Seen, "the seen mark");
        Context.That(await c.Row(1, "0/i#1.0000") is null, "the sent message is there");
        Context.Equal(0, (await c.S.FindSubscriptionsAsync("key")).Count, "subscriptions");
        Context.Equal(0, (await c.S.ReadJournalAsync(0, 0, 10)).Count, "journal rows");
    }

    private static async Task SentInOrder(Context c, IReadOnlyList<Message> sent, Committed result)
    {
        Context.Equal(sent.Count, result.Seqs.Count, "seqs returned");
        Context.That(result.Seqs.Zip(result.Seqs.Skip(1)).All(p => p.First < p.Second), "seqs returned are not increasing");
        var polled = (await c.S.PollAsync(1, 1, TimeSpan.Zero, 100)).Messages;
        Context.Equal(string.Join(" ", sent.Select(m => m.Id)), string.Join(" ", polled.Select(m => m.Message.Id)), "polled in seq order");
        Context.Equal(string.Join(" ", result.Seqs), string.Join(" ", polled.Select(m => m.Seq)), "seqs as stored");
    }

    /// <summary>Run every case on a fresh store. Prints one line per case.</summary>
    public static async Task<IReadOnlyList<Outcome>> RunAsync(IStoreProvider provider, StoreSetup? setup = null, Func<Case, bool>? filter = null, TextWriter? output = null)
    {
        output ??= Console.Out;
        var outcomes = new List<Outcome>();
        foreach (var @case in Cases.Where(filter ?? (_ => true)))
        {
            var watch = Stopwatch.StartNew();
            Outcome outcome;
            await using (var provided = await provider.CreateAsync(2, setup ?? new StoreSetup()))
            {
                var context = new Context(provided);
                try
                {
                    await @case.Body(context).WaitAsync(TimeSpan.FromSeconds(60));
                    outcome = new(@case, true, false, string.Join(", ", context.Notes.Distinct()), watch.Elapsed);
                }
                catch (SkipException e) { outcome = new(@case, false, true, e.Message, watch.Elapsed); }
                catch (ConformanceException e) { outcome = new(@case, false, false, e.Message, watch.Elapsed); }
                catch (Exception e) { outcome = new(@case, false, false, $"{e.GetType().Name}: {e.Message}", watch.Elapsed); }
            }
            outcomes.Add(outcome);
            var mark = outcome.Skipped ? "SKIP" : outcome.Passed ? "pass" : @case.Level == Level.Reference ? "DIFF" : "FAIL";
            output.WriteLine($"{mark}  {@case.Name}{(outcome.Detail.Length > 0 ? "  [" + outcome.Detail + "]" : "")}  ({outcome.Took.TotalMilliseconds:0} ms)");
        }
        var failed = outcomes.Count(o => !o.Passed && !o.Skipped && o.Case.Level == Level.Required);
        var differs = outcomes.Count(o => !o.Passed && !o.Skipped && o.Case.Level == Level.Reference);
        output.WriteLine($"{provider.Name}: {outcomes.Count(o => o.Passed)} passed, {failed} required failed, {differs} reference differ, {outcomes.Count(o => o.Skipped)} skipped");
        return outcomes;
    }
}
