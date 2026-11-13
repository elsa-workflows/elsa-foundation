using System.Collections.Concurrent;
using Kernel.Sim;

namespace Kernel.Store.Conformance;

/// <summary>
/// The stress test's outside world and its end state, apart from whatever drives the workers: an outside producer that
/// retries its adds until they answer, and the check of FINDINGS section 4, reading only through
/// <see cref="IKernelStore"/>. The store suite's <see cref="Stress"/> drives the workers by hand; the runtime suite
/// drives them through a runtime.
/// </summary>
public sealed class Workload(int partitions, int messages, int seconds)
{
    private readonly ConcurrentDictionary<string, List<string>> _sent = new();
    private readonly List<string> _parents = [], _waiters = [];
    private readonly List<(string Id, int Count)> _sequences = [];
    private readonly List<string> _lifecycles = [], _onEvents = [];
    private readonly HashSet<string> _deleted = [];

    public int Lifecycles => _lifecycles.Count;
    public int Deleted => _deleted.Count;
    public int OnEvents => _onEvents.Count;

    /// <summary>The keys of the waiters' events: their subscriptions must be gone at the end.</summary>
    public IReadOnlyList<string> Keys => _waiters.Select(w => "k" + w[(w.IndexOf("/w", StringComparison.Ordinal) + 2)..]).ToList();

    /// <summary>
    /// The outside world: commands with start specs, retried until the insert answers; the id makes that safe. Adds
    /// come from two clients in turn, as from two nodes.
    /// </summary>
    public async Task ProduceAsync(IKernelStore outside, IKernelStore other)
    {
        var keep = (await outside.PollAsync(0, 0, TimeSpan.Zero, 1)).Now + 3_600_000;
        var (sent, parents, waiters, sequences) = (_sent, _parents, _waiters, _sequences);
        var (lifecycles, deleted, onEvents) = (_lifecycles, _deleted, _onEvents);
        var random = new Random(11);
        var clients = new[] { outside, other };
        async Task Retry(Func<IKernelStore, ValueTask> call, int n)
        {
            for (var attempt = 0; ; attempt++)
                try
                {
                    await call(clients[(n + attempt) % 2]);
                    return;
                }
                catch (Exception) { await Task.Delay(5); }
        }
        Task Add(Message message, int n) => Retry(async s => await s.AddAsync(message with { KeepUntil = keep }), n);
        for (var n = 0; n < messages; n++)
        {
            var p = random.Next(partitions);
            switch (random.Next(12))
            {
                case < 6:
                    var target = InstanceRef.In(p, "c" + random.Next(3));
                    var forward = random.Next(4) == 0 ? InstanceRef.In(random.Next(partitions), "c" + random.Next(3)).Id : null;
                    sent.GetOrAdd(target.Id, _ => []).Add("t" + n);
                    await Add(new Message("x" + n, target, "add", Json.Write(new Add("t" + n, forward)), Start: Counter.Spec), n);
                    break;
                case 6:
                    parents.Add(InstanceRef.In(p, "par" + n).Id);
                    await Add(new Message("x" + n, InstanceRef.In(p, "par" + n), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(random.Next(1, 4), random.Next(300))))), n);
                    break;
                case 7:
                    waiters.Add(InstanceRef.In(p, "w" + n).Id);
                    await Add(new Message("x" + n, InstanceRef.In(p, "w" + n), Kinds.Start, Start: new StartSpec("waiter", "w", Json.Write(new List<string> { "k" + n }))), n);
                    // The event may be published before or after the subscription commits: the two must still meet.
                    await Retry(s => Events.PublishAsync(s, new EventRow("k" + n, "e" + n, "e" + n, keep)), n);
                    break;
                case 10:
                    // Round 3: a tree that waits until it is ended, suspended and resumed or not, terminated, and deleted or not.
                    var life = InstanceRef.In(p, "life" + n);
                    lifecycles.Add(life.Id);
                    await Add(new Message("x" + n, life, Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(random.Next(1, 4), -1, random.Next(2) == 0)))), n);
                    if (random.Next(2) == 0)
                    {
                        await Add(new Message("x" + n + ".s", life, Kinds.Suspend), n);
                        await Add(new Message("x" + n + ".r", life, Kinds.Resume), n);
                    }
                    await Add(new Message("x" + n + ".t", life, Kinds.Terminate), n);
                    if (random.Next(2) == 0)
                    {
                        deleted.Add(life.Id);
                        await Add(new Message("x" + n + ".d", life, Kinds.Delete), n);
                    }
                    break;
                case 11:
                    // Round 3: an instance started by an event, the subscription registered before or after the publish.
                    var start = new SubscriptionRow("on" + n, "", "s", OnEvent.Spec("o"));
                    var e = new EventRow("on" + n, "f" + n, "f" + n, keep);
                    onEvents.Add(Events.Deliver(e, start, partitions).Target.Id);
                    var first = random.Next(2) == 0;
                    if (first) await Retry(s => Events.SubscribeAsync(s, start, partitions), n);
                    await Retry(s => Events.PublishAsync(s, e, partitions), n);
                    if (!first) await Retry(s => Events.SubscribeAsync(s, start, partitions), n);
                    break;
                default:
                    var count = random.Next(1, 6);
                    sequences.Add((InstanceRef.In(p, "s" + n).Id, count));
                    await Add(new Message("x" + n, InstanceRef.In(p, "s" + n), Kinds.Start, Start: Sequence.Spec(count, random.Next(2) == 0 ? Sequence.Balanced : Sequence.Durable)), n);
                    break;
            }
            await Task.Delay(random.Next(0, 2 * seconds * 1000 / messages));
        }
    }

    /// <summary>Wait until no message and no waiter's subscription is left, or the deadline passes.</summary>
    public async Task SettleAsync(IKernelStore outside, TimeSpan settle)
    {
        var deadline = DateTime.UtcNow + settle;
        var keys = Keys;
        for (var left = 1; left > 0 && DateTime.UtcNow < deadline; await Task.Delay(500))
        {
            try { left = await Left(outside, partitions, keys); }
            catch (Exception) { left = 1; }
        }
    }

    /// <summary>What the end state showed, besides its violations.</summary>
    public sealed record Checked(List<string> Wrong, int Instances, int Journal, int Tokens);

    /// <summary>
    /// The end state, read through the contract once every worker has stopped. <paramref name="first"/> (the recorded
    /// commits' violations of invariant 1) leads the list; <paramref name="afterJournal"/> (what the store itself saw
    /// go wrong) follows the journal's.
    /// </summary>
    public async Task<Checked> CheckAsync(IKernelStore outside, TimeSpan lease, IReadOnlyList<string> first, IEnumerable<string> afterJournal, TextWriter output)
    {
        var (sent, parents, waiters, sequences) = (_sent, _parents, _waiters, _sequences);
        var (lifecycles, deleted, onEvents) = (_lifecycles, _deleted, _onEvents);
        var keys = Keys;
        var wrong = new List<string>();
        await Task.Delay(lease * 3);     // every lease has run out: a fresh node can take, and the take returns the position
        var journal = new List<JournalRow>();
        for (var p = 0; p < partitions; p++)
        {
            var own = new List<JournalRow>();
            for (long after = 0; ;)
            {
                var page = await outside.ReadJournalAsync(p, after, 1000);
                own.AddRange(page);
                if (page.Count < 1000) break;
                after = page[^1].Position;
            }
            journal.AddRange(own);
            Lease? taken = null;
            for (var attempt = 0; taken is null && attempt < 50; attempt++)
                try { taken = await outside.TakeAsync(p, "checker-" + Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(30)); }
                catch (Exception) { await Task.Delay(100); }
            if (taken is null)
            {
                wrong.Add($"END partition {p} could not be taken after the workers stopped");
                continue;
            }
            output.WriteLine($"partition {p}: epoch {taken.Epoch} ({taken.Epoch - 2} takes before the checker's), position {taken.Position}, {own.Count} journal rows");
            // A delete removes its instance's journal rows (round 3), so a partition with a deleted instance has gaps; the
            // last position is always there, since a delete's own row is never removed.
            var gaps = deleted.Any(d => new InstanceRef(d).Partition == p);
            if (own.Count > taken.Position || (own.Count < taken.Position && !gaps) || (own.Count > 0 && (own[^1].Position != taken.Position || own.Select(j => j.Position).Distinct().Count() != own.Count)))
                wrong.Add($"I6 partition {p}: {own.Count} journal rows, position {taken.Position}");
        }
        wrong.InsertRange(0, first);
        wrong.AddRange(afterJournal);

        for (var p = 0; p < partitions; p++)
            foreach (var message in (await outside.PollAsync(p, long.MaxValue, TimeSpan.FromDays(3650), 100_000)).Messages.Take(5))
                wrong.Add($"END message {message.Message.Id} ({message.Message.Kind} to {message.Message.Target}) left behind");
        foreach (var key in keys)
            if ((await outside.FindSubscriptionsAsync(key)).Count > 0) wrong.Add($"END a subscription of {key} is left behind");

        var ids = journal.Select(j => j.Instance).Concat(parents).Concat(waiters).Concat(sequences.Select(s => s.Id)).Concat(sent.Keys).Concat(lifecycles).Concat(onEvents).Distinct().ToList();
        var rows = new Dictionary<string, InstanceRow>();
        foreach (var chunk in ids.Chunk(500))
            foreach (var row in await outside.LoadAsync(chunk)) rows[row.Id] = row;
        foreach (var group in journal.Where(j => j.Revision > 0).GroupBy(j => j.Instance))
            if (!rows.TryGetValue(group.Key, out var row) || row.Revision != group.Count() || group.Max(j => j.Revision) != row.Revision)
                wrong.Add($"I2 {group.Key}: row revision {rows.GetValueOrDefault(group.Key)?.Revision}, journal commits {group.Count()}");
        var counters = rows.Values.Where(r => r.Model == "counter").ToDictionary(r => r.Id, r => Json.Read<CounterState>(r.State));
        var expected = sent.ToDictionary(s => s.Key, s => s.Value.ToList());
        foreach (var forward in counters.Values.SelectMany(c => c.Forwarded)) (expected.TryGetValue(forward.Target, out var list) ? list : expected[forward.Target] = []).Add(forward.Token);
        foreach (var (id, tokens) in expected)
        {
            var applied = counters.GetValueOrDefault(id)?.Tokens ?? [];
            if (applied.Count != applied.Distinct().Count()) wrong.Add($"I4 {id} applied a message twice");
            if (!applied.Order().SequenceEqual(tokens.Order())) wrong.Add($"I3/I4 {id}: {applied.Count} applied, {tokens.Count} sent; missing [{string.Join(", ", tokens.Except(applied).Take(5))}], extra [{string.Join(", ", applied.Except(tokens).Take(5))}]");
            var outsideOrder = sent.GetValueOrDefault(id) ?? [];
            if (!applied.Where(outsideOrder.Contains).SequenceEqual(outsideOrder.Where(applied.Contains))) wrong.Add($"I11 {id} applied outside messages out of order");
        }
        foreach (var (sender, state) in counters)
            foreach (var group in state.Forwarded.GroupBy(f => f.Target))
            {
                var (order, applied) = (group.Select(f => f.Token).ToList(), counters.GetValueOrDefault(group.Key)?.Tokens ?? []);
                if (!applied.Where(order.Contains).SequenceEqual(order.Where(applied.Contains))) wrong.Add($"I11 {group.Key} applied the messages of {sender} out of order");
            }
        foreach (var id in parents.Concat(waiters).Concat(sequences.Select(s => s.Id)))
            if (rows.GetValueOrDefault(id)?.Status != InstanceStatus.Finished) wrong.Add($"END {id} ended as {rows.GetValueOrDefault(id)?.Status.ToString() ?? "no row"}");
        foreach (var id in waiters)
            if (rows.TryGetValue(id, out var row) && Json.Read<WaiterState>(row.State).Got.Count != 1) wrong.Add($"I13 {id} was given {Json.Read<WaiterState>(row.State).Got.Count} events");
        foreach (var (id, count) in sequences)
            if (rows.TryGetValue(id, out var row) && !Json.Read<SequenceState>(row.State).Done.SequenceEqual(Enumerable.Range(1, count))) wrong.Add($"I4 {id} did [{string.Join(", ", Json.Read<SequenceState>(row.State).Done)}] of {count}");
        foreach (var row in rows.Values.Where(r => r.Status == InstanceStatus.Faulted).Take(5)) wrong.Add($"I14 {row.Id} was parked: {row.Fault}");
        wrong.AddRange(RoundThree(rows, lifecycles, deleted, onEvents));
        return new Checked(wrong, rows.Count, journal.Count, expected.Sum(e => e.Value.Count));
    }

    /// <summary>
    /// Round 3's end state: a terminated tree ended Cancelled, root and every child it started, wherever they were placed;
    /// a deleted root has no row and no journal rows; an instance started by an event exists once and got it once.
    /// </summary>
    public static IEnumerable<string> RoundThree(IReadOnlyDictionary<string, InstanceRow> rows, IReadOnlyList<string> lifecycles, IReadOnlySet<string> deleted, IReadOnlyList<string> onEvents)
    {
        string Name(string id) => id[(id.IndexOf('/') + 1)..];
        foreach (var id in lifecycles)
        {
            if (deleted.Contains(id)) { if (rows.ContainsKey(id)) yield return $"END {id} was deleted but has a row"; }
            else if (rows.GetValueOrDefault(id)?.Status != InstanceStatus.Cancelled) yield return $"END {id} was terminated but ended as {rows.GetValueOrDefault(id)?.Status.ToString() ?? "no row"}";
            var prefix = Name(id) + "~";
            foreach (var child in rows.Values.Where(r => Name(r.Id).StartsWith(prefix, StringComparison.Ordinal) && r.Status != InstanceStatus.Cancelled))
                yield return $"END {child.Id}, a child of terminated {id}, ended as {child.Status}";
        }
        foreach (var id in onEvents)
            if (rows.GetValueOrDefault(id) is not { Status: InstanceStatus.Finished } row) yield return $"END {id}, started by an event, is {rows.GetValueOrDefault(id)?.Status.ToString() ?? "missing"}";
            else if (Json.Read<OnEventState>(row.State).Got.Count != 1) yield return $"I13 {id} was started with {Json.Read<OnEventState>(row.State).Got.Count} events";
    }

    private static async Task<int> Left(IKernelStore store, int partitions, IReadOnlyList<string> keys)
    {
        var left = 0;
        for (var p = 0; p < partitions; p++) left += (await store.PollAsync(p, long.MaxValue, TimeSpan.FromDays(3650), 100_000)).Messages.Count;
        if (left > 0) return left;
        foreach (var key in keys) left += (await store.FindSubscriptionsAsync(key)).Count;
        return left;
    }
}
