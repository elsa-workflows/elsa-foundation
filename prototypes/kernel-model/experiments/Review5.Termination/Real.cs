using System.Reflection;

namespace Kernel.Review5;

/// <summary>
/// The prototype functions the outcomes depend on, called as built. Several are internal to their assemblies, so the
/// harness reaches them by reflection and leaves every prototype project unchanged. Each call is counted, and every
/// report prints the counts, so a reader can see which real code a run went through.
/// </summary>
static class Real
{
    private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly MethodInfo TargetErrorMethod = Find(typeof(CreationProtocol), "TargetError");
    private static readonly MethodInfo ValidateMethod = Find(typeof(CreationProtocol), "Validate");
    private static readonly Type Registry = typeof(WorkflowInstance).Assembly.GetType("Kernel.Workflows.ChildRegistry", throwOnError: true)!;
    private static readonly MethodInfo RegistryRead = Find(Registry, "Read");
    private static readonly MethodInfo RegistryWrite = Find(Registry, "Write");
    private static readonly MethodInfo FinishedMethod = Find(typeof(WorkflowInstance), "Finished");
    private static readonly Type SlotType = typeof(Delivery).Assembly.GetType("Kernel.Durable.Slot", throwOnError: true)!;
    private static readonly FieldInfo SlotRow = SlotType.GetField("Row")!;
    private static readonly FieldInfo SlotLoaded = SlotType.GetField("Loaded")!;
    private static readonly FieldInfo DeliveryDurable = typeof(Delivery).GetField("Durable", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(nameof(Delivery), "Durable");
    private static readonly ConstructorInfo TurnInputCtor = typeof(TurnInput).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
        [typeof(Worker), SlotType, typeof(string), typeof(List<Delivery>), typeof(int), typeof(StartSpec)], null)
        ?? throw new MissingMethodException(nameof(TurnInput), ".ctor");

    public static readonly SortedDictionary<string, int> Calls = new(StringComparer.Ordinal);

    private static MethodInfo Find(Type type, string name) =>
        type.GetMethod(name, Internal) ?? throw new MissingMethodException(type.FullName, name);

    private static void Count(string name) => Calls[name] = Calls.GetValueOrDefault(name) + 1;

    public static void Reset() => Calls.Clear();

    public static string Summary() => string.Join(", ", Calls.Select(c => $"{c.Key} x{c.Value}"));

    /// <summary>InMemoryStore.cs 87–194, <c>InMemoryStore.CommitAsync</c>: the fenced, count-enforcing commit.</summary>
    public static Committed Commit(InMemoryStore store, CommitBatch batch)
    { Count("InMemoryStore.CommitAsync"); return store.CommitAsync(batch).AsTask().GetAwaiter().GetResult(); }

    public static Lease? Take(InMemoryStore store, int partition, string node, TimeSpan lease)
    { Count("InMemoryStore.TakeAsync"); return store.TakeAsync(partition, node, lease).AsTask().GetAwaiter().GetResult(); }

    public static bool Add(InMemoryStore store, Message message)
    { Count("InMemoryStore.AddAsync"); return store.AddAsync(message).AsTask().GetAwaiter().GetResult(); }

    /// <summary>Creation.cs 103–109, <c>CreationProtocol.TargetError</c>: the delivery-time creation check.</summary>
    public static string? TargetError(InstanceRow? row, Message message)
    { Count("CreationProtocol.TargetError"); return (string?)TargetErrorMethod.Invoke(null, [row, message]); }

    /// <summary>Creation.cs 111–117, <c>CreationProtocol.Refuse</c>: the Worker's refusal outcome (Worker.cs 1050–1055).</summary>
    public static TurnOutcome Refuse(string actor, InstanceRow? row, Delivery delivery, string reason)
    { Count("CreationProtocol.Refuse"); return CreationProtocol.Refuse(actor, row, [delivery], reason); }

    /// <summary>Creation.cs 145–176, <c>CreationProtocol.Validate</c>: the commit's creation-header check. Returns its refusal, or "accepted".</summary>
    public static string Validate(TurnCommit turn, InstanceRow? previous)
    {
        Count("CreationProtocol.Validate");
        try { ValidateMethod.Invoke(null, [turn, previous]); return "accepted"; }
        catch (TargetInvocationException e) when (e.InnerException is StoreException refused) { return "refused: " + refused.Message; }
    }

    /// <summary>Creation.cs 119–120, <c>CreationProtocol.Retire</c>: the built delete's retirement marker.</summary>
    public static InstanceRow Retire(InstanceRow row) { Count("CreationProtocol.Retire"); return CreationProtocol.Retire(row); }

    public static string Origin(string actor, long creation) { Count("CreationProtocol.Origin"); return CreationProtocol.Origin(actor, creation); }

    /// <summary>ChildRegistry.cs 8–17, the registry's stored form.</summary>
    public static List<CreationAddress> ReadRegistry(string? json)
    { Count("ChildRegistry.Read"); return (List<CreationAddress>)RegistryRead.Invoke(null, [json])!; }

    public static string? WriteRegistry(List<CreationAddress> children)
    { Count("ChildRegistry.Write"); return (string?)RegistryWrite.Invoke(null, [children]); }

    /// <summary>WorkflowInstance.cs 210–212, <c>WorkflowInstance.Finished</c>: the parent's notice that a child ended.</summary>
    public static Message Finished(InstanceRow row) { Count("WorkflowInstance.Finished"); return (Message)FinishedMethod.Invoke(null, [row, null])!; }

    public static List<string> ReadJobs(string? json) { Count("DurableKeyJson.ReadKeys"); return json is null ? [] : DurableKeyJson.ReadKeys(json); }

    public static string? WriteJobs(List<string> keys) { Count("DurableKeyJson.WriteKeys"); return keys.Count == 0 ? null : DurableKeyJson.WriteKeys(keys); }

    /// <summary>Messages.cs 31 and Worker.cs 295–296, <c>InboxKey.Ticket</c> over <c>Worker.TicketId</c>.</summary>
    public static InboxKey Ticket(string actor, string key, long creation) { Count("InboxKey.Ticket"); return InboxKey.Ticket(actor, key, creation); }

    /// <summary>
    /// WorkflowInstance.cs 31–93, <c>WorkflowInstance.Command</c>: the actor type's own turn for a control or an ended
    /// row, run on a real <see cref="TurnInput"/> over a real <c>Slot</c> holding <paramref name="row"/>. The input is
    /// built by reflection (its constructor is internal to the Worker's assembly); the Worker is constructed and never
    /// started, so it lends the turn its services and nothing else.
    /// </summary>
    public static TurnOutcome? Command(WorkflowInstance type, Worker worker, InstanceRow row, Message message, long seq)
    {
        Count("WorkflowInstance.Command");
        var input = Input(worker, row, message, seq, out _);
        return type.Command(input);
    }

    /// <summary>WorkflowInstance.cs 120–132, <c>WorkflowInstance.Park</c>: the fault a failed turn takes (the send cap's "fails and parks").</summary>
    public static TurnOutcome Park(WorkflowInstance type, Worker worker, InstanceRow row, Message message, long seq, string error)
    {
        Count("WorkflowInstance.Park");
        var input = Input(worker, row, message, seq, out var delivery);
        return type.Park(row, input.Actor, [delivery], null, error);
    }

    public static Delivery Delivery(Message message, long seq)
    {
        var delivery = new Delivery(message, seq);
        DeliveryDurable.SetValue(delivery, true);
        return delivery;
    }

    public static bool Durable(Delivery delivery) => (bool)DeliveryDurable.GetValue(delivery)!;

    private static TurnInput Input(Worker worker, InstanceRow row, Message message, long seq, out Delivery delivery)
    {
        var slot = Activator.CreateInstance(SlotType, nonPublic: true)!;
        SlotRow.SetValue(slot, row);
        SlotLoaded.SetValue(slot, true);
        delivery = Delivery(message, seq);
        return (TurnInput)TurnInputCtor.Invoke([worker, slot, row.Id, new List<Delivery> { delivery }, 0, null]);
    }
}

/// <summary>
/// A harness-authored execution model with no metadata, so <c>WorkflowInstance</c>'s terminate, delete and ended-row
/// paths run without a model state to read. No run gives it a turn.
/// </summary>
sealed class StubModel : IExecutionModel
{
    public string Key => "rt";
    public Type StateType => typeof(Dictionary<string, string>);
    public int StateVersion => 1;
    public object CreateState(StartSpec start) => new Dictionary<string, string>();
    public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken) => throw new NotSupportedException("RT harness models no model turn");
    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
}
