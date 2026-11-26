using System.Text.Json.Nodes;
using Kernel.Graph;
using Models = Kernel.Model.Contracts;

namespace Kernel.Native;

/// <summary>
/// A recurring start trigger of a registered definition: its Timer's interval or its Cron's expression. It is keyed by the
/// definition and node, not by the workflow (the artifact), so a republished definition keeps its schedule actor and points
/// its run at the new workflow.
/// </summary>
public sealed record NativeSchedule(string Workflow, string Node, string Kind, string Value)
{
    public const string Interval = "interval";
    public const string Cron = "cron";

    /// <summary>The definition the workflow is a version of (<see cref="Native.Workflow.Definition"/>); the workflow itself unless set.</summary>
    public string Definition { get => _definition ?? Workflow; init => _definition = value; }
    private readonly string? _definition;

    /// <summary>The caller key of the schedule's actor: one per definition and trigger.</summary>
    public string Key => $"schedule:{Definition}:{Node}";

    /// <summary>The start key each occurrence publishes to; the host registers <see cref="Start"/> on it.</summary>
    public string StartKey => $"schedule-start:{Definition}:{Node}";

    /// <summary>The run each occurrence starts: the workflow, begun at its trigger, under its definition.</summary>
    public Models.WorkflowStart Start(string mode = Modes.Balanced) =>
        NativeExecutionModel.Start(Workflow, mode, triggerNode: Node, trigger: JsonValue.Create(Key), definition: Definition);
}

/// <summary>A recurring start trigger the model does not accept.</summary>
public sealed class NativeScheduleException(string message) : ArgumentException(message);

/// <summary>
/// The recurring start triggers of the registered definitions, as <see cref="HttpRoutes"/> holds their routes. A Timer or
/// Cron must be where a run begins: the root, or, following the start path through containers, the first child of a
/// Sequence, a direct child of a Flowchart that no connection leads to, or deeper through a Flowchart's start node. One elsewhere is refused, as the old core refuses to
/// publish it ("use Delay instead"). The interval or expression is a literal, fixed when the definition is registered; one
/// that does not read is refused. The host's schedule actor fires each one.
/// </summary>
public sealed class NativeSchedules
{
    private readonly List<NativeSchedule> _schedules = [];

    /// <param name="registry">
    /// The workflows that serve: one version per scheduled definition (release 1 serves one slot of each), not every
    /// workflow instances still run, or a republished definition's two versions would both claim its schedule.
    /// </param>
    /// <param name="cronError">The host's check of a cron expression: null when it reads, else why not. Without one, every expression is refused.</param>
    public NativeSchedules(WorkflowRegistry registry, Func<string, string?>? cronError = null)
    {
        foreach (var workflow in registry.All.OrderBy(w => w.Id, StringComparer.Ordinal))
        {
            var starts = StartPath(workflow.Root).ToHashSet();
            foreach (var node in workflow.Nodes.Values.Where(n => n.Type == typeof(Timer) || n.Type == typeof(Cron)).OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                if (!starts.Contains(node))
                    throw new NativeScheduleException($"Definition '{workflow.Definition}', {node.Type.Name} '{node.Id}': a recurring start trigger must be where a run begins; use a Delay to wait in the middle of a workflow.");
                if (node.Type == typeof(Timer))
                {
                    var interval = Literal(workflow, node, "Interval");
                    if (!ScheduleIntervals.TryParse(interval, out _))
                        throw new NativeScheduleException($"Definition '{workflow.Definition}', Timer '{node.Id}': '{interval}' is no ISO 8601 duration or time span of at least {ScheduleIntervals.MinInterval.TotalSeconds:0} second.");
                    _schedules.Add(new(workflow.Id, node.Id, NativeSchedule.Interval, interval.Trim()) { Definition = workflow.Definition });
                }
                else if (node.Type == typeof(Cron))
                {
                    var expression = Literal(workflow, node, "Expression");
                    if ((cronError is null ? "the host reads no cron expressions" : cronError(expression)) is { } error)
                        throw new NativeScheduleException($"Definition '{workflow.Definition}', Cron '{node.Id}': '{expression}' is refused: {error}.");
                    _schedules.Add(new(workflow.Id, node.Id, NativeSchedule.Cron, expression.Trim()) { Definition = workflow.Definition });
                }
            }
        }
        Claim();
    }

    public IReadOnlyList<NativeSchedule> All => _schedules;

    private void Claim()
    {
        // Schedules are built over the workflows that serve, one per definition: two versions of one definition would
        // fire one schedule twice.
        foreach (var clash in _schedules.GroupBy(s => s.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
            throw new NativeScheduleException($"Definition '{clash.First().Definition}' would schedule '{clash.First().Node}' from {clash.Count()} versions at once. A definition's schedule runs one version: publish the scheduled definition to one channel.");
    }

    // Where a run can begin, as the graph engine begins it: the node itself; through a Sequence, its first child; through a
    // Flowchart, a direct child no connection leads to (the run begins at its trigger), and deeper only through the start
    // node, since a Flowchart whose trigger is deeper begins there (the old core's start-path rule, SelectTriggerStartChild).
    private static IEnumerable<Node> StartPath(Node node)
    {
        yield return node;
        if (node.Type == typeof(Sequence))
        {
            foreach (var child in node.Children.Take(1))
                foreach (var start in StartPath(child)) yield return start;
            yield break;
        }
        if (node.Type != typeof(Flowchart)) yield break;
        foreach (var child in node.Children.Where(child => !node.Connections.Any(c => c.Target == child.Id))) yield return child;
        FlowchartNode? entry;
        try { entry = Flowchart.GraphOf(node).SelectStartNode(); }
        catch (Exception refused) when (refused is Kernel.Graph.Exceptions.FlowchartExecutionException or ArgumentException) { yield break; }
        if (entry is not null && node.Children.FirstOrDefault(c => c.Id == entry.NodeId) is { } first)
            foreach (var start in StartPath(first)) yield return start;
    }

    private static string Literal(Workflow workflow, Node node, string input) =>
        node.Inputs.GetValueOrDefault(input) is LiteralBinding { Value: JsonValue value } && value.TryGetValue<string>(out var text)
            ? text
            : throw new NativeScheduleException($"Definition '{workflow.Definition}', {node.Type.Name} '{node.Id}': its {input} must be a literal, fixed when the definition is registered.");
}
