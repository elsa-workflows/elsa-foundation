using System.Reflection;

namespace Kernel.Review6;

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
    private static readonly Type Lifetime = typeof(NativeModel).Assembly.GetType("Kernel.Native.InvocationLifetime", throwOnError: true)!;
    private static readonly MethodInfo LifetimeTerminate = Find(Lifetime, "Terminate");
    private static readonly Type SlotType = typeof(Delivery).Assembly.GetType("Kernel.Durable.Slot", throwOnError: true)!;
    private static readonly FieldInfo SlotRow = SlotType.GetField("Row")!;
    private static readonly FieldInfo SlotLoaded = SlotType.GetField("Loaded")!;
    private static readonly FieldInfo DeliveryDurable = Field(typeof(Delivery), "Durable");
    private static readonly ConstructorInfo TurnInputCtor = typeof(TurnInput).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
        [typeof(Worker), SlotType, typeof(string), typeof(List<Delivery>), typeof(int), typeof(StartSpec)], null)
        ?? throw new MissingMethodException(nameof(TurnInput), ".ctor");
    private static readonly ConstructorInfo EventDeliveryCtor = typeof(EventDelivery).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
        [typeof(Message), typeof(EventMatch)], null) ?? throw new MissingMethodException(nameof(EventDelivery), ".ctor");
    private static readonly FieldInfo EventDeliveryMessage = Field(typeof(EventDelivery), "<Message>k__BackingField");

    public static readonly SortedDictionary<string, int> Calls = new(StringComparer.Ordinal);

    private static MethodInfo Find(Type type, string name) =>
        type.GetMethod(name, Internal) ?? throw new MissingMethodException(type.FullName, name);

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingFieldException(type.FullName, name);

    private static void Count(string name) => Calls[name] = Calls.GetValueOrDefault(name) + 1;

    public static void Reset() => Calls.Clear();

    public static string Summary() => string.Join(", ", Calls.Select(c => $"{c.Key} x{c.Value}"));

    private static T Sync<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();

    // ---- the store ---------------------------------------------------------------------------------------------------

    /// <summary>InMemoryStore.cs 87–194, <c>InMemoryStore.CommitAsync</c>: the fenced, count-enforcing commit.</summary>
    public static Committed Commit(InMemoryStore store, CommitBatch batch) { Count("InMemoryStore.CommitAsync"); return Sync(store.CommitAsync(batch)); }

    public static Lease? Take(InMemoryStore store, int partition, string node, TimeSpan lease) { Count("InMemoryStore.TakeAsync"); return Sync(store.TakeAsync(partition, node, lease)); }

    /// <summary>InMemoryStore.cs 227–232, <c>InMemoryStore.AddAsync</c>: an outside add, stored under its ordinary key.</summary>
    public static bool Add(InMemoryStore store, Message message) { Count("InMemoryStore.AddAsync"); return Sync(store.AddAsync(message)); }

    /// <summary>InMemoryStore.cs 263–267, <c>InMemoryStore.PublishAsync</c>: the event row's insert, a no-op when (key, id) exists.</summary>
    public static void Publish(InMemoryStore store, EventRow row) { Count("InMemoryStore.PublishAsync"); store.PublishAsync(row).AsTask().GetAwaiter().GetResult(); }

    /// <summary>InMemoryStore.cs 269–274, <c>InMemoryStore.SubscribeAsync</c>.</summary>
    public static void Subscribe(InMemoryStore store, SubscriptionRow row) { Count("InMemoryStore.SubscribeAsync"); store.SubscribeAsync(row).AsTask().GetAwaiter().GetResult(); }

    /// <summary>InMemoryStore.cs 281–285, <c>InMemoryStore.PublishedMatchesAsync</c>: routes built from the stored event row.</summary>
    public static IReadOnlyList<EventRoute> PublishedMatches(InMemoryStore store, EventRow row) { Count("InMemoryStore.PublishedMatchesAsync"); return Sync(store.PublishedMatchesAsync(row)); }

    /// <summary>InMemoryStore.cs 287–298, <c>InMemoryStore.KeptMatchesAsync</c>: a catch-up's routes over the kept events.</summary>
    public static IReadOnlyList<EventRoute> KeptMatches(InMemoryStore store, IReadOnlyList<SubscriptionRow> waits) { Count("InMemoryStore.KeptMatchesAsync"); return Sync(store.KeptMatchesAsync(waits)); }

    /// <summary>InMemoryStore.cs 235–246, <c>InMemoryStore.AddEventAsync</c>: insert the delivery unless its full key exists.</summary>
    public static EventAcceptance AddEvent(InMemoryStore store, EventDelivery delivery) { Count("InMemoryStore.AddEventAsync"); return Sync(store.AddEventAsync(delivery)); }

    /// <summary>InMemoryStore.cs 348–359, <c>InMemoryStore.SweepAsync</c>: Done rows and events past their window.</summary>
    public static int Sweep(InMemoryStore store) { Count("InMemoryStore.SweepAsync"); return Sync(store.SweepAsync()); }

    /// <summary>Store.cs 287–296, <c>Events.PublishAsync</c>: the built caller-side publish, insert then match.</summary>
    public static List<(EventRoute Route, EventAcceptance Accepted)> EventsPublish(InMemoryStore store, EventRow row, int partitions)
    {
        Count("Events.PublishAsync");
        var matched = new List<(EventRoute, EventAcceptance)>();
        Events.PublishAsync(store, row, partitions, matched: (route, accepted) => matched.Add((route, accepted))).AsTask().GetAwaiter().GetResult();
        return matched;
    }

    /// <summary>Store.cs 221–222 and 247–251, <c>EventRoute.Delivery</c> over <c>Events.Deliver</c>: the built delivery envelope.</summary>
    public static EventDelivery RouteDelivery(EventRoute route, int partitions) { Count("EventRoute.Delivery"); return route.Delivery(partitions); }

    /// <summary>
    /// The built envelope (<see cref="RouteDelivery"/>) under a proposed full key: the id and <c>Match</c> are replaced and
    /// everything else (target, kind, payload, <c>KeepUntil</c>, creation, the negative scope) is the built one. The
    /// envelope's constructor is internal (Store.cs 214), so the harness builds it by reflection.
    /// </summary>
    public static EventDelivery Keyed(EventDelivery built, string id, string match)
    {
        var message = (built.Message with { Id = id }).WithInboxScope(built.Message.InboxScope, match);
        var delivery = (EventDelivery)EventDeliveryCtor.Invoke([message, new EventMatch("", "", "", "")]);
        EventDeliveryMessage.SetValue(delivery, message);
        return delivery;
    }

    // ---- the actor type and its turns ---------------------------------------------------------------------------------

    /// <summary>WorkflowInstance.cs 31–93, <c>WorkflowInstance.Command</c>: the actor type's own turn (terminate at 155–203).</summary>
    public static TurnOutcome? Command(WorkflowInstance type, Worker worker, InstanceRow row, Message message, long seq)
    {
        Count("WorkflowInstance.Command");
        return type.Command(Input(worker, row, [Delivery(message, seq)]));
    }

    /// <summary>WorkflowInstance.cs 120–132, <c>WorkflowInstance.Park</c>: the fault a failed turn takes.</summary>
    public static TurnOutcome Park(WorkflowInstance type, Worker worker, InstanceRow row, Message message, long seq, string error)
    {
        Count("WorkflowInstance.Park");
        var delivery = Delivery(message, seq);
        var input = Input(worker, row, [delivery]);
        return type.Park(row, input.Actor, [delivery], null, error);
    }

    /// <summary>Turns.cs 249–260, a real <see cref="DurableTurn"/> over a reflected input holding <paramref name="taken"/>.</summary>
    public static DurableTurn ModelTurn(Worker worker, InstanceRow row, List<Delivery> taken)
    {
        Count("DurableTurn");
        return new DurableTurn(Input(worker, row, taken));
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

    // ---- the Native model -------------------------------------------------------------------------------------------

    /// <summary>NativeModel.cs 171–179, <c>NativeModel.TerminateMetadata</c>: InvocationLifetime.Terminate, then LexicalScopes.Terminate.</summary>
    public static void TerminateMetadata(NativeModel model, NativeState state) { Count("NativeModel.TerminateMetadata"); model.TerminateMetadata(state); }

    /// <summary>InvocationLifetime.cs 42–45, <c>InvocationLifetime.Terminate</c>, the end paths' close (InvocationInbox.cs 74–76 passes "completed" or "faulted").</summary>
    public static void CloseAll(NativeState state, string reason)
    { Count("InvocationLifetime.Terminate"); LifetimeTerminate.Invoke(null, [state, WorkflowCommitClock.Terminal, reason, false, false]); }

    /// <summary>NativeModel.cs 180–183, <c>NativeModel.ProtectsDeletion</c>.</summary>
    public static bool ProtectsDeletion(NativeModel model, NativeState state, long now, ICommitClock clock)
    { Count("NativeModel.ProtectsDeletion"); return model.ProtectsDeletion(state, now, clock); }
}
