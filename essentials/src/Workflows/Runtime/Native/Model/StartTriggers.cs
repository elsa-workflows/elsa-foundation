using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>
/// Which trigger activities start a run: the old core's <c>CanStartWorkflow</c> on HttpEndpoint and Event. A trigger whose
/// <see cref="Input"/> is a literal false only waits, also where a run begins: it is a wait route, never a start route, and
/// a start delivered to it is not taken. Any other binding, or none (a workflow built in code), lets it start a run.
/// </summary>
public static class NativeStartTriggers
{
    public const string Input = "CanStartWorkflow";

    /// <summary>Whether <paramref name="node"/> may start a run where one begins.</summary>
    public static bool CanStart(Node node) =>
        !(node.Inputs.GetValueOrDefault(Input) is LiteralBinding { Value: JsonValue value } && value.GetValueKind() == JsonValueKind.False);

    /// <summary>The Events a run of <paramref name="workflow"/> can begin at, by their event: where a run begins, and allowed to start one.</summary>
    public static IEnumerable<Node> Events(Workflow workflow) =>
        HttpRoutes.StartsOf(workflow.Root).Where(node => node.Type == typeof(Event) && CanStart(node));
}
