using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Models;

namespace Elsa.Agent.Workflows.Services;

/// <summary>The workflow module's bounded, value-free admission schema for an open editor.</summary>
internal sealed record WorkflowAgentSnapshot(AgentResourceTarget Target, AgentClientBinding Client, string DraftId,
    WorkflowAgentGraph Graph, JsonElement Content)
{
    internal const string ModuleId = "Elsa.Studio.Workflows";
    internal const string ResourceType = "workflow-definition";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static bool SameTarget(AgentResourceTarget left, AgentResourceTarget right) =>
        left.ModuleId == right.ModuleId && left.ResourceType == right.ResourceType &&
        left.ResourceId == right.ResourceId && left.Revision == right.Revision;

    internal static bool TryRead(AgentContextAttachment attachment, [NotNullWhen(true)] out WorkflowAgentSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = "The workflow snapshot requires an exact definition, draft, revision and client generation.";
        if (attachment.ModuleId != ModuleId || attachment.ContentType != "workflow.definition" || attachment.SchemaVersion != "1" ||
            attachment.Target is not { ModuleId: ModuleId, ResourceType: ResourceType } target ||
            !ValidString(target.ResourceId) || target.ResourceId == "active-draft" || !ValidString(target.Revision) ||
            attachment.Client is not { } client || !ValidString(client.ClientId) || !ValidString(client.GenerationId))
            return false;
        JsonElement body;
        try { body = attachment.Content is JsonElement json ? json : JsonSerializer.SerializeToElement(attachment.Content, JsonOptions); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException) { return false; }
        if (body.ValueKind != JsonValueKind.Object || body.GetRawText().Length > 65536 ||
            Text(body, "workflowDefinitionId") != target.ResourceId || Text(body, "revision") != target.Revision ||
            !ValidString(Text(body, "draftId")))
            return false;
        if (!body.TryGetProperty("activities", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() > 256 ||
            !body.TryGetProperty("connections", out var edges) || edges.ValueKind != JsonValueKind.Array || edges.GetArrayLength() > 512 ||
            !body.TryGetProperty("positions", out var positions) || positions.ValueKind != JsonValueKind.Array || positions.GetArrayLength() > 256)
            return false;
        var activities = new List<WorkflowAgentActivitySummary>();
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object || !ValidString(Text(node, "id")) || !ValidString(Text(node, "type")) ||
                !nodeIds.Add(Text(node, "id")!)) return false;
            activities.Add(new(Text(node, "id")!, Text(node, "type")!, Bounded(Text(node, "displayName"))));
        }
        var connections = new List<WorkflowAgentConnectionSummary>();
        foreach (var edge in edges.EnumerateArray())
        {
            if (edge.ValueKind != JsonValueKind.Object || !nodeIds.Contains(Text(edge, "source") ?? "") ||
                !nodeIds.Contains(Text(edge, "target") ?? "")) return false;
            connections.Add(new(Text(edge, "source")!, Text(edge, "target")!, Bounded(Text(edge, "sourcePort")), Bounded(Text(edge, "targetPort"))));
        }
        var admittedPositions = new List<object>();
        var positioned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var position in positions.EnumerateArray())
        {
            if (position.ValueKind != JsonValueKind.Object || !nodeIds.Contains(Text(position, "nodeId") ?? "") ||
                !positioned.Add(Text(position, "nodeId")!) || !Number(position, "x", out var x) || !Number(position, "y", out var y)) return false;
            admittedPositions.Add(new { nodeId = Text(position, "nodeId"), x, y });
        }
        var selectedNodeId = Text(body, "selectedNodeId");
        if (selectedNodeId is not null && !nodeIds.Contains(selectedNodeId)) return false;
        // Only declared graph metadata is copied. Caller summaries, permission claims, guidance,
        // property values, diagnostics with authored payloads and arbitrary extra fields are discarded.
        var admitted = JsonSerializer.SerializeToElement(new
        {
            workflowDefinitionId = target.ResourceId, draftId = Text(body, "draftId"), revision = target.Revision,
            selectedNodeId, activities, connections, positions = admittedPositions
        }, JsonOptions);
        snapshot = new(new(ModuleId, ResourceType, target.ResourceId, target.Revision), client, Text(body, "draftId")!,
            new(target.Revision, activities, connections), admitted);
        error = string.Empty;
        return true;
    }

    internal static string? Text(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    internal static bool Number(JsonElement body, string property, out double number)
    {
        number = 0;
        return body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out number) && double.IsFinite(number);
    }
    private static bool ValidString(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
    private static string Bounded(string? value) => value is null ? string.Empty : value[..Math.Min(value.Length, 256)];
}
