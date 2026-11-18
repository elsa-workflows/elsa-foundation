using System.Reflection;

namespace Kernel.Review4;

/// <summary>
/// The prototype functions the outcomes depend on, called as built. Four of them are internal to their assemblies,
/// so the harness reaches them by reflection and leaves their projects unchanged. Each call is counted,
/// and every report prints the counts, so a reader can see which real code a run went through.
/// </summary>
static class Real
{
    private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly MethodInfo TargetErrorMethod = Find(typeof(CreationProtocol), "TargetError");
    private static readonly MethodInfo StartErrorMethod = Find(typeof(CreationProtocol), "StartError");
    private static readonly Type Registry = typeof(WorkflowInstance).Assembly.GetType("Kernel.Workflows.ChildRegistry", throwOnError: true)!;
    private static readonly MethodInfo RegistryRead = Find(Registry, "Read");
    private static readonly MethodInfo RegistryWrite = Find(Registry, "Write");
    private static readonly MethodInfo FinishedMethod = Find(typeof(WorkflowInstance), "Finished");

    public static readonly SortedDictionary<string, int> Calls = new(StringComparer.Ordinal);

    private static MethodInfo Find(Type type, string name) =>
        type.GetMethod(name, Internal) ?? throw new MissingMethodException(type.FullName, name);

    private static void Count(string name) => Calls[name] = Calls.GetValueOrDefault(name) + 1;

    /// <summary>Creation.cs, <c>CreationProtocol.TargetError</c>: the delivery-time creation check.</summary>
    public static string? TargetError(InstanceRow? row, Message message)
    { Count("CreationProtocol.TargetError"); return (string?)TargetErrorMethod.Invoke(null, [row, message]); }

    /// <summary>Creation.cs, <c>CreationProtocol.StartError</c>: whether a start may create the next creation.</summary>
    public static string? StartError(InstanceRow? previous, StartSpec start, string kind, string? payload, InboxKey message)
    { Count("CreationProtocol.StartError"); return (string?)StartErrorMethod.Invoke(null, [previous, start, kind, payload, message]); }

    /// <summary>Creation.cs, <c>CreationProtocol.Retire</c>: the built delete's retirement marker.</summary>
    public static InstanceRow Retire(InstanceRow row) { Count("CreationProtocol.Retire"); return CreationProtocol.Retire(row); }

    public static string Origin(string actor, long creation) { Count("CreationProtocol.Origin"); return CreationProtocol.Origin(actor, creation); }

    public static string? Fingerprint(StartSpec start) { Count("CreationProtocol.Fingerprint"); return CreationProtocol.Fingerprint(start); }

    /// <summary>ChildRegistry.cs, the registry's stored form.</summary>
    public static List<CreationAddress> ReadRegistry(string? json)
    { Count("ChildRegistry.Read"); return (List<CreationAddress>)RegistryRead.Invoke(null, [json])!; }

    public static string? WriteRegistry(List<CreationAddress> children)
    { Count("ChildRegistry.Write"); return (string?)RegistryWrite.Invoke(null, [children]); }

    /// <summary>WorkflowInstance.cs, <c>WorkflowInstance.Finished</c>: the parent's notice that a child ended.</summary>
    public static Message Finished(InstanceRow row) { Count("WorkflowInstance.Finished"); return (Message)FinishedMethod.Invoke(null, [row, null])!; }

    public static List<string> ReadJobs(string? json) { Count("DurableKeyJson.ReadKeys"); return json is null ? [] : DurableKeyJson.ReadKeys(json); }

    public static string? WriteJobs(List<string> keys) { Count("DurableKeyJson.WriteKeys"); return keys.Count == 0 ? null : DurableKeyJson.WriteKeys(keys); }

    public static InboxKey Ticket(string actor, string key, long creation) { Count("InboxKey.Ticket"); return InboxKey.Ticket(actor, key, creation); }

    public static void Reset() => Calls.Clear();

    public static void CountModelTurn() => Count("NativeModel.TurnAsync");

    public static string Summary() => string.Join(", ", Calls.Select(c => $"{c.Key} x{c.Value}"));
}
