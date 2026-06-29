using System.Text.Json;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Models;

namespace Elsa.Agent.Workflows.Services;

/// <summary>
/// Parses the live workflow graph out of a <c>workflow.definition</c> context attachment. Studio sends the
/// graph inline as the attachment <see cref="AgentContextAttachment.Content"/> (a JSON object); the server
/// builds the same shape. This reader normalizes either representation into a <see cref="WorkflowAgentGraph"/>
/// so the risk classifier and authoring tools can validate operations against real activity node ids.
/// </summary>
public static class WorkflowAgentGraphReader
{
    public const string WorkflowDefinitionContentType = "workflow.definition";

    private static readonly WorkflowAgentGraph Empty = new(null, [], []);

    /// <summary>Finds the first <c>workflow.definition</c> attachment in a context set and reads its graph.</summary>
    public static WorkflowAgentGraph Read(IEnumerable<AgentContextAttachment> context)
    {
        var attachment = context.FirstOrDefault(x =>
            string.Equals(x.ContentType, WorkflowDefinitionContentType, StringComparison.OrdinalIgnoreCase));
        return attachment is null ? Empty : Read(attachment);
    }

    public static WorkflowAgentGraph Read(AgentContextAttachment attachment)
    {
        var element = TryNormalize(attachment.Content);
        var revision = ReadRevision(element)
            ?? (attachment.References.TryGetValue("revision", out var r) && !string.IsNullOrWhiteSpace(r) ? r : null);

        if (element is not { ValueKind: JsonValueKind.Object } body)
            return Empty with { Revision = revision };

        return new(revision, ReadActivities(body), ReadConnections(body));
    }

    private static string? ReadRevision(JsonElement? element)
        => element is { ValueKind: JsonValueKind.Object } body
           && TryGetProperty(body, "revision", out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyCollection<WorkflowAgentActivitySummary> ReadActivities(JsonElement body)
    {
        if (!TryGetProperty(body, "activities", out var activities) || activities.ValueKind != JsonValueKind.Array)
            return [];

        return activities.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new WorkflowAgentActivitySummary(
                GetString(x, "id") ?? GetString(x, "nodeId") ?? string.Empty,
                GetString(x, "type") ?? GetString(x, "activityType") ?? string.Empty,
                GetString(x, "displayName") ?? GetString(x, "name") ?? string.Empty))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToList();
    }

    private static IReadOnlyCollection<WorkflowAgentConnectionSummary> ReadConnections(JsonElement body)
    {
        if (!TryGetProperty(body, "connections", out var connections) || connections.ValueKind != JsonValueKind.Array)
            return [];

        return connections.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new WorkflowAgentConnectionSummary(
                GetString(x, "source") ?? GetString(x, "sourceActivityId") ?? string.Empty,
                GetString(x, "target") ?? GetString(x, "targetActivityId") ?? string.Empty,
                GetString(x, "sourcePort"),
                GetString(x, "targetPort")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Source) && !string.IsNullOrWhiteSpace(x.Target))
            .ToList();
    }

    private static JsonElement? TryNormalize(object? content)
    {
        switch (content)
        {
            case null:
                return null;
            case JsonElement element:
                return element;
            case string:
                return null; // A plain summary string carries no graph.
            default:
                try
                {
                    return JsonSerializer.SerializeToElement(content);
                }
                catch (NotSupportedException)
                {
                    return null;
                }
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
