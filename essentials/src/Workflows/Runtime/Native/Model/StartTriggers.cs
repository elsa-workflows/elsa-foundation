using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>
/// Which trigger activities start a run: the old core's <c>CanStartWorkflow</c> on HttpEndpoint and Event. A trigger whose
/// <see cref="Input"/> is a literal false only waits, also where a run begins: it is a wait route, never a start route, and
/// a start delivered to it is not taken. Unbound (a workflow built in code), it may start a run. Bind it to a literal
/// boolean: the route table and the event starts are fixed when a definition is registered, so a computed value is read
/// there as no value, and the trigger may start a run.
/// </summary>
public static class NativeStartTriggers
{
    public const string Input = "CanStartWorkflow";

    /// <summary>Whether <paramref name="node"/> may start a run where one begins: false only for a literal false.</summary>
    public static bool CanStart(Node node) =>
        !(node.Inputs.GetValueOrDefault(Input) is LiteralBinding { Value: JsonValue value } && value.GetValueKind() == JsonValueKind.False);

    /// <summary>The Events a run of <paramref name="workflow"/> can begin at, by their event: where a run begins, and allowed to start one.</summary>
    public static IEnumerable<Node> StartEvents(Workflow workflow) =>
        HttpRoutes.StartsOf(workflow.Root).Where(node => node.Type == typeof(Event) && CanStart(node));
}
