using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Tests.Support;

/// <summary>
/// Test-owned Elsa 3 flowchart definitions (spec 239 SC-002): each reproduces the shape of a JTest corpus workflow, with
/// invented names and values, in Elsa 3's serialized JSON form.
/// </summary>
internal static class Elsa3FlowchartFixtures
{
    private static readonly JsonSerializerOptions Elsa3Json = new(JsonSerializerDefaults.Web);

    public static readonly string[] Converting =
    [
        "decision-tree", "if-switch-done", "nested-single-child", "fork-wait-all", "fork-wait-all-active",
        "exclusive-wait-any", "implicit-join", "start-activity", "two-http-triggers", "two-triggers-into-wait-any",
        "terminal-outbound"
    ];

    public static Elsa3WorkflowDefinition Load(string name) =>
        JsonSerializer.Deserialize<Elsa3WorkflowDefinition>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Flowcharts", $"{name}.json")), Elsa3Json)
        ?? throw new InvalidOperationException($"Fixture '{name}' is empty.");

    /// <summary>A converted flowchart's connections as <c>source:port->target</c>, in stored order.</summary>
    public static string[] Connections(ActivityNode flowchart) =>
        flowchart.Structure!.Payload.GetProperty("connections").EnumerateArray()
            .Select(connection =>
                $"{connection.GetProperty("source").GetProperty("nodeId").GetString()}:{connection.GetProperty("source").GetProperty("port").GetString()}" +
                $"->{connection.GetProperty("target").GetProperty("nodeId").GetString()}")
            .ToArray();

    /// <summary>A converted flowchart's direct children by node id, in stored order.</summary>
    public static string[] Children(ActivityNode flowchart) =>
        flowchart.Structure!.Payload.GetProperty("activities").EnumerateArray()
            .Select(child => child.GetProperty("nodeId").GetString()!)
            .ToArray();

    public static string? StartNodeId(ActivityNode flowchart) =>
        flowchart.Structure!.Payload.TryGetProperty("startNodeId", out var start) && start.ValueKind == JsonValueKind.String
            ? start.GetString()
            : null;
}
