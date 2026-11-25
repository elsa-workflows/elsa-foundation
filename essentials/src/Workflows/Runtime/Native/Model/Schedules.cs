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

    /// <summary>The run each occurrence starts: the workflow, begun at its trigger.</summary>
    public Models.WorkflowStart Start(string mode = Modes.Balanced) =>
        NativeExecutionModel.Start(Workflow, mode, triggerNode: Node, trigger: JsonValue.Create(Key));
}

/// <summary>A recurring start trigger the model does not accept.</summary>
public sealed class NativeScheduleException(string message) : ArgumentException(message);

/// <summary>
/// The recurring start triggers of the registered definitions, as <see cref="HttpRoutes"/> holds their routes. A Timer or
/// Cron where a run begins (the root, the first child of a root Sequence, or an entry of a root Flowchart) is a schedule;
/// elsewhere it is no trigger and completes at once when reached, as on the old core. The interval or expression is a
/// literal, fixed when the definition is registered; one that does not read is refused. The host's schedule actor fires
/// each one.
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
            var starts = HttpRoutes.StartsOf(workflow.Root).ToHashSet();
            foreach (var node in workflow.Nodes.Values.Where(n => starts.Contains(n)).OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                if (node.Type == typeof(Timer))
                {
                    var interval = Literal(workflow, node, "Interval");
                    if (!ScheduleIntervals.TryParse(interval, out _))
                        throw new NativeScheduleException($"Workflow '{workflow.Id}', Timer '{node.Id}': '{interval}' is no positive ISO 8601 duration or time span.");
                    _schedules.Add(new(workflow.Id, node.Id, NativeSchedule.Interval, interval));
                }
                else if (node.Type == typeof(Cron))
                {
                    var expression = Literal(workflow, node, "Expression");
                    if ((cronError is null ? "the host reads no cron expressions" : cronError(expression)) is { } error)
                        throw new NativeScheduleException($"Workflow '{workflow.Id}', Cron '{node.Id}': '{expression}' is refused: {error}.");
                    _schedules.Add(new(workflow.Id, node.Id, NativeSchedule.Cron, expression));
                }
            }
        }
    }

    public IReadOnlyList<NativeSchedule> All => _schedules;

    private static string Literal(Workflow workflow, Node node, string input) =>
        node.Inputs.GetValueOrDefault(input) is LiteralBinding { Value: JsonValue value } && value.TryGetValue<string>(out var text)
            ? text
            : throw new NativeScheduleException($"Workflow '{workflow.Id}', {node.Type.Name} '{node.Id}': its {input} must be a literal, fixed when the definition is registered.");
}
