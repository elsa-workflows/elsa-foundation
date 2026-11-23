using System.Reflection;

namespace Kernel.Review7;

/// <summary>
/// The prototype functions the outcomes depend on, called as built. Several are internal to their assemblies, so the
/// harness reaches them by reflection and leaves every prototype project unchanged. Each call is counted, and every
/// report prints the counts, so a reader can see which real code a run went through.
/// </summary>
static class Real
{
    private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type Registry = typeof(WorkflowInstance).Assembly.GetType("Kernel.Workflows.ChildRegistry", throwOnError: true)!;
    private static readonly MethodInfo RegistryWrite = Find(Registry, "Write");
    private static readonly MethodInfo RegistryRead = Find(Registry, "Read");
    private static readonly Type SlotType = typeof(Delivery).Assembly.GetType("Kernel.Durable.Slot", throwOnError: true)!;
    private static readonly FieldInfo SlotRow = SlotType.GetField("Row")!;
    private static readonly FieldInfo SlotLoaded = SlotType.GetField("Loaded")!;
    private static readonly FieldInfo DeliveryDurable = Field(typeof(Delivery), "Durable");
    private static readonly MethodInfo WorkerTake = typeof(Worker).GetMethod("TakeAsync", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(CancellationToken)])
        ?? throw new MissingMethodException(nameof(Worker), "TakeAsync");
    private static readonly ConstructorInfo TurnInputCtor = typeof(TurnInput).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
        [typeof(Worker), SlotType, typeof(string), typeof(List<Delivery>), typeof(int), typeof(StartSpec)], null)
        ?? throw new MissingMethodException(nameof(TurnInput), ".ctor");

    public static readonly SortedDictionary<string, int> Calls = new(StringComparer.Ordinal);

    private static MethodInfo Find(Type type, string name) =>
        type.GetMethod(name, Internal) ?? throw new MissingMethodException(type.FullName, name);

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingFieldException(type.FullName, name);

    private static void Count(string name) => Calls[name] = Calls.GetValueOrDefault(name) + 1;

    public static void Reset() => Calls.Clear();

    public static string Summary() => string.Join(", ", Calls.Select(c => $"{c.Key} x{c.Value}"));

    private static T Sync<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();

    // ---- the store and its owners ------------------------------------------------------------------------------------

    /// <summary>InMemoryStore.cs 87–194, <c>InMemoryStore.CommitAsync</c>: the fenced, count-enforcing commit; its <c>Refuses</c> hook (49, 110) refuses a row as a definite error.</summary>
    public static Committed Commit(InMemoryStore store, CommitBatch batch) { Count("InMemoryStore.CommitAsync"); return Sync(store.CommitAsync(batch)); }

    /// <summary>Worker.cs 411–430, <c>Worker.TakeAsync</c>: the store's <c>TakeAsync</c> (InMemoryStore.cs 70–83), then the worker's epoch, position and child issuer.</summary>
    public static void Take(Worker worker)
    {
        Count("Worker.TakeAsync");
        ((Task)WorkerTake.Invoke(worker, [CancellationToken.None])!).GetAwaiter().GetResult();
    }

    /// <summary>InMemoryStore.cs 227–232, <c>InMemoryStore.AddAsync</c>: an outside add, stored under its ordinary key.</summary>
    public static bool Add(InMemoryStore store, Message message) { Count("InMemoryStore.AddAsync"); return Sync(store.AddAsync(message)); }

    /// <summary>InMemoryStore.cs 269–274, <c>InMemoryStore.SubscribeAsync</c>.</summary>
    public static void Subscribe(InMemoryStore store, SubscriptionRow row) { Count("InMemoryStore.SubscribeAsync"); store.SubscribeAsync(row).AsTask().GetAwaiter().GetResult(); }

    /// <summary>
    /// Store.cs 288–296, <c>Events.PublishAsync</c>: the real <c>InMemoryStore.PublishAsync</c> (263–267, a no-op when
    /// (key, id) exists), then <c>PublishedMatchesAsync</c> (281–285, routes from the stored row), then
    /// <c>AddEventAsync</c> (235–246, refuses an existing delivery key) for each route. The outside path and, as
    /// Worker.cs 1323–1325 runs it, the owner's step of a turn's publish ticket.
    /// </summary>
    public static List<(EventRoute Route, EventAcceptance Accepted)> EventsPublish(InMemoryStore store, EventRow row, int partitions)
    {
        Count("Events.PublishAsync");
        Count("InMemoryStore.PublishAsync");
        Count("InMemoryStore.PublishedMatchesAsync");
        var matched = new List<(EventRoute, EventAcceptance)>();
        Events.PublishAsync(store, row, partitions, matched: (route, accepted) => { Count("InMemoryStore.AddEventAsync"); matched.Add((route, accepted)); })
            .AsTask().GetAwaiter().GetResult();
        return matched;
    }

    // ---- the actor type and its turns ---------------------------------------------------------------------------------

    /// <summary>WorkflowInstance.cs 31–93, <c>WorkflowInstance.Command</c>: the actor type's own turn (terminate at 179–203).</summary>
    public static TurnOutcome? Command(WorkflowInstance type, Worker worker, InstanceRow row, Message message, long seq)
    {
        Count("WorkflowInstance.Command");
        return type.Command(Input(worker, row, [Delivery(message, seq)]));
    }

    /// <summary>
    /// WorkflowInstance.cs 95–112, <c>WorkflowInstance.TurnAsync</c>: a model turn through the real <c>Turn</c>
    /// (Turn.cs), its <c>Seal</c> (82–139) and the real <c>DurableTurn.Seal</c> (Turns.cs 481–508).
    /// </summary>
    public static TurnOutcome ModelTurnAsync(WorkflowInstance type, Worker worker, InstanceRow row, List<Delivery> taken)
    {
        Count("WorkflowInstance.TurnAsync");
        return Sync(type.TurnAsync(Input(worker, row, taken)));
    }

    /// <summary>WorkflowInstance.cs 116–132, <c>WorkflowInstance.Park</c>: the fault a failed turn takes.</summary>
    public static TurnOutcome Park(WorkflowInstance type, Worker worker, InstanceRow row, Message message, long seq, string error)
    {
        Count("WorkflowInstance.Park");
        var delivery = Delivery(message, seq);
        var input = Input(worker, row, [delivery]);
        return type.Park(row, input.Actor, [delivery], null, error);
    }

    /// <summary>Turns.cs 249–260, a real <see cref="DurableTurn"/> over a reflected input holding <paramref name="taken"/>.</summary>
    public static DurableTurn DurableTurn(Worker worker, InstanceRow row, List<Delivery> taken)
    {
        Count("DurableTurn");
        return new DurableTurn(Input(worker, row, taken));
    }

    /// <summary>Turns.cs 429–434, <c>DurableTurn.Publish</c>: the ticket to self and the returned id, <c>{Origin}#{Revision}.{ordinal:D4}</c>.</summary>
    public static string Publish(DurableTurn turn, string key, string? payload, TimeSpan keep) { Count("DurableTurn.Publish"); return turn.Publish(key, payload, keep); }

    /// <summary>Turns.cs 481–508, <c>DurableTurn.Seal</c>.</summary>
    public static TurnOutcome Seal(DurableTurn turn, InstanceRow row)
    {
        Count("DurableTurn.Seal");
        return turn.Seal(turn.Outcome(row.Status), row with { Revision = turn.Revision }, null, null);
    }

    /// <summary>A delivery of a stored row (<c>Durable</c>).</summary>
    public static Delivery Delivery(Message message, long seq)
    {
        var delivery = new Delivery(message, seq);
        DeliveryDurable.SetValue(delivery, true);
        return delivery;
    }

    public static bool Durable(Delivery delivery) => (bool)DeliveryDurable.GetValue(delivery)!;

    private static TurnInput Input(Worker worker, InstanceRow row, List<Delivery> taken)
    {
        var slot = Activator.CreateInstance(SlotType, nonPublic: true)!;
        SlotRow.SetValue(slot, row);
        SlotLoaded.SetValue(slot, true);
        return (TurnInput)TurnInputCtor.Invoke([worker, slot, row.Id, taken, 0, null]);
    }

    /// <summary>ChildRegistry.cs 8–17, the registry's stored form.</summary>
    public static string? WriteRegistry(List<CreationAddress> children) { Count("ChildRegistry.Write"); return (string?)RegistryWrite.Invoke(null, [children]); }

    /// <summary>ChildRegistry.cs, the registry read back.</summary>
    public static List<CreationAddress> ReadRegistry(string? children) { Count("ChildRegistry.Read"); return (List<CreationAddress>)RegistryRead.Invoke(null, [children])!; }

    // ---- the Native model -------------------------------------------------------------------------------------------

    /// <summary>NativeModel.cs 180–183, <c>NativeModel.ProtectsDeletion</c>.</summary>
    public static bool ProtectsDeletion(NativeModel model, NativeState state, long now, ICommitClock clock)
    { Count("NativeModel.ProtectsDeletion"); return model.ProtectsDeletion(state, now, clock); }
}
