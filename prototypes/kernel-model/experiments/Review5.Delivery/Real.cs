using System.Reflection;

namespace Kernel.Review5;

/// <summary>
/// The prototype functions the outcomes depend on, called as built (after Review4.Blockers' Real.cs). Internal ones are
/// reached by reflection, so no prototype project changes. Every call is counted and every report prints the counts,
/// so a reader can see which real code a run went through.
/// </summary>
static class Real
{
    private static readonly Type SlotType = typeof(Delivery).Assembly.GetType("Kernel.Durable.Slot", throwOnError: true)!;
    private static readonly MethodInfo DiscardedAtMethod = typeof(Delivery).GetMethod("DiscardedAt", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingMethodException(nameof(Delivery), "DiscardedAt");

    public static readonly SortedDictionary<string, int> Calls = new(StringComparer.Ordinal);

    public static void Count(string name) => Calls[name] = Calls.GetValueOrDefault(name) + 1;

    /// <summary>
    /// Turns.cs 109–112, <c>Delivery.DiscardedAt</c>, over a real <c>Slot</c> whose committed row is <paramref name="row"/>,
    /// for a delivery built from a durable row as the Worker's poll builds it (Worker.cs 633–638): no in-memory
    /// <c>CompletedJob</c> provenance. True when the Worker consumes it with no turn (Worker.cs 1030–1037).
    /// </summary>
    public static bool DiscardedAt(Message message, InstanceRow row)
    {
        Count("Delivery.DiscardedAt");
        var slot = Activator.CreateInstance(SlotType, nonPublic: true)!;
        SlotType.GetField("Row")!.SetValue(slot, row);
        return (bool)DiscardedAtMethod.Invoke(new Delivery(message, 0), [slot])!;
    }

    public static InboxKey Ticket(string actor, string key, long creation) { Count("InboxKey.Ticket"); return InboxKey.Ticket(actor, key, creation); }

    public static string Origin(string actor, long creation) { Count("CreationProtocol.Origin"); return CreationProtocol.Origin(actor, creation); }

    public static void CountModelTurn() => Count("NativeModel.TurnAsync");

    // ---- the real in-memory store, counted --------------------------------------------------------------------------

    public static async ValueTask<Committed> CommitAsync(InMemoryStore store, CommitBatch batch)
    { Count("InMemoryStore.CommitAsync"); return await store.CommitAsync(batch); }

    public static async ValueTask<Lease?> TakeAsync(InMemoryStore store, int partition, string node, TimeSpan lease)
    { Count("InMemoryStore.TakeAsync"); return await store.TakeAsync(partition, node, lease); }

    public static async ValueTask<Polled> PollAsync(InMemoryStore store, int partition, long epoch, int limit)
    { Count("InMemoryStore.PollAsync"); return await store.PollAsync(partition, epoch, TimeSpan.Zero, limit); }

    public static async ValueTask PublishAsync(InMemoryStore store, EventRow e) { Count("InMemoryStore.PublishAsync"); await store.PublishAsync(e); }

    public static async ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(InMemoryStore store, EventRow e)
    { Count("InMemoryStore.PublishedMatchesAsync"); return await store.PublishedMatchesAsync(e); }

    public static async ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(InMemoryStore store, IReadOnlyList<SubscriptionRow> waits)
    { Count("InMemoryStore.KeptMatchesAsync"); return await store.KeptMatchesAsync(waits); }

    public static async ValueTask<EventAcceptance> AddEventAsync(InMemoryStore store, EventRoute route)
    {
        Count("EventRoute.Delivery"); var delivery = route.Delivery(1);
        Count("InMemoryStore.AddEventAsync"); return await store.AddEventAsync(delivery);
    }

    public static async ValueTask<int> SweepAsync(InMemoryStore store) { Count("InMemoryStore.SweepAsync"); return await store.SweepAsync(); }

    public static async ValueTask<bool> AddAsync(InMemoryStore store, Message message) { Count("InMemoryStore.AddAsync"); return await store.AddAsync(message); }

    public static void Reset() => Calls.Clear();

    public static string Summary() => string.Join(", ", Calls.Select(c => $"{c.Key} x{c.Value}"));
}
