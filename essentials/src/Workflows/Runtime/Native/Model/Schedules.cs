using System.Text.Json.Nodes;
using Models = Kernel.Model.Contracts;

namespace Kernel.Native;

/// <summary>A recurring start trigger of a registered definition: its Timer's interval or its Cron's expression.</summary>
public sealed record NativeSchedule(string Workflow, string Node, string Kind, string Value)
{
    public const string Interval = "interval";
    public const string Cron = "cron";

    /// <summary>The caller key of the schedule's actor: one per trigger.</summary>
    public string Key => $"schedule:{Workflow}:{Node}";

    /// <summary>The start key each occurrence publishes to; the host registers <see cref="Start"/> on it.</summary>
    public string StartKey => $"schedule-start:{Workflow}:{Node}";

    /// <summary>The run each occurrence starts: the workflow, begun at its trigger.</summary>
    public Models.WorkflowStart Start(string mode = Modes.Balanced) =>
        NativeExecutionModel.Start(Workflow, mode, triggerNode: Node, trigger: JsonValue.Create(Key));
}

/// <summary>A recurring start trigger the model does not accept.</summary>
public sealed class NativeScheduleException(string message) : ArgumentException(message);

/// <summary>
/// The recurring start triggers of the registered definitions, as <see cref="HttpRoutes"/> holds their routes. A Timer or
/// Cron must be where a run begins: the root, or, following the start path through containers, the first child of a
/// Sequence or an entry of a Flowchart (a child no connection leads to). One elsewhere is refused, as the old core refuses to
/// publish it ("use Delay instead"). The interval or expression is a literal, fixed when the definition is registered; one
/// that does not read is refused. The host's schedule actor fires each one.
/// </summary>
public sealed class NativeSchedules
{
    private readonly List<NativeSchedule> _schedules = [];

    /// <param name="registry">The registered definitions.</param>
    /// <param name="cronError">The host's check of a cron expression: null when it reads, else why not. Without one, every expression is refused.</param>
    public NativeSchedules(WorkflowRegistry registry, Func<string, string?>? cronError = null)
    {
        foreach (var workflow in registry.All.OrderBy(w => w.Id, StringComparer.Ordinal))
        {
            var starts = StartPath(workflow.Root).ToHashSet();
            foreach (var node in workflow.Nodes.Values.Where(n => n.Type == typeof(Timer) || n.Type == typeof(Cron)).OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                if (!starts.Contains(node))
                    throw new NativeScheduleException($"Workflow '{workflow.Id}', {node.Type.Name} '{node.Id}': a recurring start trigger must be where a run begins; use a Delay to wait in the middle of a workflow.");
                if (node.Type == typeof(Timer))
                {
                    var interval = Literal(workflow, node, "Interval");
                    if (!ScheduleIntervals.TryParse(interval, out _))
                        throw new NativeScheduleException($"Workflow '{workflow.Id}', Timer '{node.Id}': '{interval}' is no ISO 8601 duration or time span of at least {ScheduleIntervals.MinInterval.TotalSeconds:0} second.");
                    _schedules.Add(new(workflow.Id, node.Id, NativeSchedule.Interval, interval.Trim()));
                }
                else if (node.Type == typeof(Cron))
                {
                    var expression = Literal(workflow, node, "Expression");
                    if ((cronError is null ? "the host reads no cron expressions" : cronError(expression)) is { } error)
                        throw new NativeScheduleException($"Workflow '{workflow.Id}', Cron '{node.Id}': '{expression}' is refused: {error}.");
                    _schedules.Add(new(workflow.Id, node.Id, NativeSchedule.Cron, expression.Trim()));
                }
            }
        }
    }

    public IReadOnlyList<NativeSchedule> All => _schedules;

    // Where a run can begin: the node itself, and through a Sequence its first child, through a Flowchart its entries.
    private static IEnumerable<Node> StartPath(Node node)
    {
        yield return node;
        IEnumerable<Node> next = node.Type == typeof(Sequence) ? node.Children.Take(1)
            : node.Type == typeof(Flowchart) ? node.Children.Where(child => !node.Connections.Any(c => c.Target == child.Id))
            : [];
        foreach (var child in next)
            foreach (var start in StartPath(child)) yield return start;
    }

    private static string Literal(Workflow workflow, Node node, string input) =>
        node.Inputs.GetValueOrDefault(input) is LiteralBinding { Value: JsonValue value } && value.TryGetValue<string>(out var text)
            ? text
            : throw new NativeScheduleException($"Workflow '{workflow.Id}', {node.Type.Name} '{node.Id}': its {input} must be a literal, fixed when the definition is registered.");
}
