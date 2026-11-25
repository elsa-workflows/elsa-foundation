namespace Kernel.Native;

/// <summary>A designer activity the kernel does not run, met where a definition is mapped onto the kernel.</summary>
public sealed class NativeActivityUnsupportedException(string node, string activityType, bool hidden)
    : InvalidOperationException(hidden
        ? $"Node '{node}' is a {activityType}, which the kernel does not run in release 1; a kernel host hides it ({NativeActivityCatalog.KernelUnsupportedSet})."
        : $"Node '{node}' is a {activityType}, which the kernel does not know.")
{
    public string Node { get; } = node;
    public string ActivityType { get; } = activityType;
    /// <summary>True for one of the release 1 activities the kernel leaves out; false for a type it does not know at all.</summary>
    public bool Hidden { get; } = hidden;
}

/// <summary>
/// The release 1 activity list (owner decision of 24 November, card 3 B): each designer activity the kernel runs, by its
/// catalog type key (the old core's CLR type name), to the Native type that runs it, and the eight it leaves out. A kernel
/// host hides those eight in Studio's toolbox with an availability set (<see cref="KernelUnsupportedSet"/>):
/// <code>
/// "Elsa": { "Activities": { "Availability": {
///   "Sets": { "kernel-unsupported": [ ...NativeActivityCatalog.Unsupported ] },
///   "Exclude": { "Sets": [ "kernel-unsupported" ] } } } }
/// </code>
/// and the kernel refuses a definition that holds one (<see cref="Resolve"/>), since availability decides only what can be
/// added (ADR 0026), and an imported or older definition can still hold a hidden activity.
/// </summary>
public static class NativeActivityCatalog
{
    /// <summary>The name of the availability set that holds <see cref="Unsupported"/>.</summary>
    public const string KernelUnsupportedSet = "kernel-unsupported";

    private static readonly Dictionary<string, Type> Runs = new(StringComparer.Ordinal)
    {
        ["Elsa.Activities.Sequence.Activities.Sequence"] = typeof(Sequence),
        ["Elsa.Activities.If.Activities.If"] = typeof(If),
        ["Elsa.Activities.Switch.Activities.Switch"] = typeof(Switch),
        ["Elsa.Activities.For.Activities.For"] = typeof(For),
        ["Elsa.Activities.ForEach.Activities.ForEach"] = typeof(ForEach),
        ["Elsa.Activities.While.Activities.While"] = typeof(While),
        ["Elsa.Activities.Do.Activities.Do"] = typeof(Do),
        ["Elsa.Activities.Parallel.Activities.Parallel"] = typeof(Parallel),
        ["Elsa.Activities.Primitives.Activities.Break"] = typeof(Break),
        ["Elsa.Activities.Flowchart.Activities.Flowchart"] = typeof(Flowchart),
        ["Elsa.Activities.Graph.Runtime.Activities.GraphActivity"] = typeof(GraphActivity),
        ["Elsa.Activities.Graph.Runtime.Activities.Complete"] = typeof(Complete),
        ["Elsa.Activities.Primitives.Activities.WriteLine"] = typeof(WriteLine),
        ["Elsa.Activities.Primitives.Activities.WriteLines"] = typeof(WriteLines),
        ["Elsa.Activities.Primitives.Activities.Fault"] = typeof(Fault),
        ["Elsa.Activities.Primitives.Activities.Inline"] = typeof(Inline),
        ["Elsa.Activities.Scripting.Activities.RunJavaScript"] = typeof(RunJavaScript),
        ["Elsa.Activities.Primitives.Activities.Event"] = typeof(Event),
        ["Elsa.Activities.Primitives.Activities.PublishEvent"] = typeof(PublishEvent),
        ["Elsa.Activities.Scheduling.Activities.Delay"] = typeof(Delay),
        ["Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow"] = typeof(DispatchWorkflow),
        ["Elsa.Activities.Http.Activities.HttpEndpoint"] = typeof(HttpEndpoint),
        ["Elsa.Activities.Http.Activities.WriteHttpResponse"] = typeof(WriteHttpResponse),
        ["Elsa.Activities.Http.Activities.SendHttpRequest"] = typeof(SendHttpRequest),
        ["Elsa.Activities.Scheduling.Activities.Timer"] = typeof(Timer),
        ["Elsa.Activities.Scheduling.Activities.Cron"] = typeof(Cron),
    };

    /// <summary>The eight the kernel leaves out of release 1: compensation, BPMN, user tasks and console input.</summary>
    public static IReadOnlyList<string> Unsupported { get; } =
    [
        "Elsa.Activities.Compensation.Activities.CompensationScope",
        "Elsa.Activities.Compensation.Activities.Compensate",
        "Elsa.Activities.Compensation.Activities.Confirm",
        "Elsa.Activities.Bpmn.Activities.BpmnProcess",
        "Elsa.Activities.Bpmn.Activities.BpmnDecision",
        "Elsa.Activities.Bpmn.Activities.RunBpmn",
        "Elsa.Activities.Primitives.Activities.CreateUserTask",
        "Elsa.Activities.Primitives.Activities.ReadLine",
    ];

    /// <summary>The designer activities the kernel runs, by catalog type key, to their Native types.</summary>
    public static IReadOnlyDictionary<string, Type> Supported => Runs;

    /// <summary>The Native type that runs a designer activity; a hidden or unknown one is refused, naming the node.</summary>
    public static Type Resolve(string activityType, string node) =>
        Runs.TryGetValue(activityType, out var type) ? type : throw new NativeActivityUnsupportedException(node, activityType, Unsupported.Contains(activityType, StringComparer.Ordinal));
}
