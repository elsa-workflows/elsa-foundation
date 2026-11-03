using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Models;

namespace Elsa.Agent.Workflows.Services;

/// <summary>The workflow module's bounded, value-free admission schema for an open editor.</summary>
/// <remarks>
/// The compact containment structure (each activity's exact version, parent, slot and its own slots), the working-state
/// kind and the visible Secret References are optional additions to schema <c>1</c>. A snapshot that carries structure
/// must describe every activity and one tree; the Operation Batch tool requires it.
/// </remarks>
internal sealed record WorkflowAgentSnapshot(AgentResourceTarget Target, AgentClientBinding Client, string DraftId,
    WorkflowAgentGraph Graph, JsonElement Content)
{
    /// <summary>The containment structure by node id, or null when the editor did not disclose it.</summary>
    internal IReadOnlyDictionary<string, WorkflowAgentNode>? Structure { get; init; }
    /// <summary><c>draft</c> for the editable working draft; anything else (a historical snapshot) is read-only.</summary>
    internal string? WorkingState { get; init; }
    /// <summary>The Secret References the editor reports as visible to the user: names, types and scopes only.</summary>
    internal IReadOnlyList<WorkflowAgentSecretReference> SecretReferences { get; init; } = [];
    internal string? SelectedNodeId { get; init; }

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
        if (!TryReadStructure(body, activities, out var structure, out var admittedStructure)) return false;
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
        var workingState = Text(body, "workingState");
        if (body.TryGetProperty("workingState", out var stateValue) && (stateValue.ValueKind != JsonValueKind.String || workingState is not ("draft" or "historical")))
            return false;
        if (!TryReadSecretReferences(body, out var secretReferences)) return false;
        // Only declared graph metadata is copied. Caller summaries, permission claims, guidance,
        // property values, diagnostics with authored payloads and arbitrary extra fields are discarded.
        // The optional additions are copied only when the editor disclosed them, so an older snapshot admits unchanged.
        var admittedBody = new Dictionary<string, object?>
        {
            ["workflowDefinitionId"] = target.ResourceId, ["draftId"] = Text(body, "draftId"), ["revision"] = target.Revision,
            ["selectedNodeId"] = selectedNodeId
        };
        if (workingState is not null) admittedBody["workingState"] = workingState;
        admittedBody["activities"] = activities;
        if (admittedStructure is not null) admittedBody["structure"] = admittedStructure;
        admittedBody["connections"] = connections;
        admittedBody["positions"] = admittedPositions;
        if (body.TryGetProperty("secretReferences", out _))
            admittedBody["secretReferences"] = secretReferences.Select(item => new { name = item.Name, typeName = item.TypeName, scope = item.Scope }).ToArray();
        var admitted = JsonSerializer.SerializeToElement(admittedBody, JsonOptions);
        snapshot = new(new(ModuleId, ResourceType, target.ResourceId, target.Revision), client, Text(body, "draftId")!,
            new(target.Revision, activities, connections), admitted)
        {
            // An empty draft has no structure to withhold, so it is structured (and empty) by definition.
            Structure = structure ?? (activities.Count == 0 ? new Dictionary<string, WorkflowAgentNode>() : null), WorkingState = workingState, SecretReferences = secretReferences,
            SelectedNodeId = selectedNodeId
        };
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

    /// <summary>
    /// Reads the optional compact containment structure: <c>versions</c> groups activity indexes by exact activity version,
    /// and <c>containers</c> lists the root and every activity that declares slots, each with its parent, its slot in the
    /// parent and its slots, whose <c>nodes</c> are indexes into <c>activities</c>. Every activity has exactly one version and,
    /// except the root, exactly one slot; the result must be one tree. False when present but malformed.
    /// </summary>
    /// <remarks>Indexes keep the structure small: a draft that fits the 64 KiB bound without structure still fits with it.</remarks>
    private static bool TryReadStructure(JsonElement body, IReadOnlyList<WorkflowAgentActivitySummary> activities,
        out Dictionary<string, WorkflowAgentNode>? structure, out object? admitted)
    {
        structure = null;
        admitted = null;
        if (!body.TryGetProperty("structure", out var value)) return true;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(property => property.Name is not ("versions" or "containers"))
            || !value.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array || versions.GetArrayLength() > 256
            || !value.TryGetProperty("containers", out var containers) || containers.ValueKind != JsonValueKind.Array || containers.GetArrayLength() > 256)
            return false;
        var count = activities.Count;
        var versionOf = new string?[count];
        var admittedVersions = new List<object>();
        foreach (var group in versions.EnumerateArray())
        {
            var id = Text(group, "id");
            if (!ValidString(id) || !TryReadIndexes(group, count, out var members)) return false;
            foreach (var index in members)
            {
                if (versionOf[index] is not null) return false;
                versionOf[index] = id;
            }
            admittedVersions.Add(new { id, nodes = members });
        }
        if (versionOf.Any(item => item is null)) return false;
        var indexOf = activities.Select((activity, index) => (activity.Id, index)).ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        var slotsOf = new Dictionary<int, IReadOnlyList<WorkflowAgentSlot>>();
        var parentOf = new (int Parent, string Slot)?[count];
        var declaredParent = new Dictionary<int, (string? ParentId, string? Slot)>();
        var admittedContainers = new List<object>();
        foreach (var container in containers.EnumerateArray())
        {
            if (container.ValueKind != JsonValueKind.Object || Text(container, "id") is not { } id || !indexOf.TryGetValue(id, out var owner)
                || slotsOf.ContainsKey(owner) || !container.TryGetProperty("parentId", out var parent) || !container.TryGetProperty("slot", out var slot)
                || !container.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array || slots.GetArrayLength() > 32)
                return false;
            var parentId = parent.ValueKind == JsonValueKind.Null ? null : Text(container, "parentId");
            var slotId = slot.ValueKind == JsonValueKind.Null ? null : Text(container, "slot");
            // The root has neither parent nor slot; every other container has both.
            if (parent.ValueKind == JsonValueKind.Null != (slot.ValueKind == JsonValueKind.Null)
                || parent.ValueKind != JsonValueKind.Null && (!ValidString(parentId) || !ValidString(slotId)))
                return false;
            var declared = new List<WorkflowAgentSlot>();
            var admittedSlots = new List<object>();
            foreach (var item in slots.EnumerateArray())
            {
                var slotKey = Text(item, "id");
                var mode = Text(item, "mode");
                var cardinality = Text(item, "cardinality");
                if (!ValidString(slotKey) || declared.Any(existing => existing.Id == slotKey) || mode is not ("sequence" or "flowchart" or "bpmn" or "compensation" or "generic")
                    || cardinality is not ("single" or "many") || !TryReadIndexes(item, count, out var children) || cardinality == "single" && children.Count > 1)
                    return false;
                foreach (var child in children)
                {
                    if (child == owner || parentOf[child] is not null) return false;
                    parentOf[child] = (owner, slotKey!);
                }
                declared.Add(new(slotKey!, mode, cardinality));
                admittedSlots.Add(new { id = slotKey, mode, cardinality, nodes = children });
            }
            slotsOf[owner] = declared;
            declaredParent[owner] = (parentId, slotId);
            admittedContainers.Add(new { id, parentId, slot = slotId, slots = admittedSlots });
        }
        // Exactly one root: the one activity in no slot, which must be listed as the container without a parent.
        var roots = Enumerable.Range(0, count).Where(index => parentOf[index] is null).ToArray();
        if (count > 0 && (roots.Length != 1 || !declaredParent.TryGetValue(roots[0], out var rootDeclared) || rootDeclared.ParentId is not null))
            return false;
        foreach (var (owner, declared) in declaredParent)
        {
            if (owner == roots.FirstOrDefault()) continue;
            var actual = parentOf[owner];
            if (actual is null || activities[actual.Value.Parent].Id != declared.ParentId || actual.Value.Slot != declared.Slot) return false;
        }
        // Every chain reaches the root within the activity count, so there is no cycle.
        for (var index = 0; index < count; index++)
        {
            var current = index;
            for (var hops = 0; parentOf[current] is { } step; hops++)
            {
                if (hops > count) return false;
                current = step.Parent;
            }
        }
        structure = new(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var activity = activities[index];
            var step = parentOf[index];
            structure[activity.Id] = new(activity.Id, activity.Type, activity.DisplayName, versionOf[index]!,
                step is null ? null : activities[step.Value.Parent].Id, step?.Slot, slotsOf.GetValueOrDefault(index) ?? []);
        }
        admitted = new { versions = admittedVersions, containers = admittedContainers };
        return true;
    }

    /// <summary>Reads a <c>nodes</c> array of distinct activity indexes within bounds.</summary>
    private static bool TryReadIndexes(JsonElement value, int count, out IReadOnlyList<int> indexes)
    {
        indexes = [];
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array
            || nodes.GetArrayLength() > count) return false;
        var read = new List<int>();
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Number || !node.TryGetInt32(out var index) || index < 0 || index >= count || read.Contains(index)) return false;
            read.Add(index);
        }
        indexes = read;
        return true;
    }

    private static bool TryReadSecretReferences(JsonElement body, out IReadOnlyList<WorkflowAgentSecretReference> references)
    {
        references = [];
        if (!body.TryGetProperty("secretReferences", out var items)) return true;
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 64) return false;
        var read = new List<WorkflowAgentSecretReference>();
        foreach (var item in items.EnumerateArray())
        {
            var name = Text(item, "name");
            if (!ValidString(name) || OptionalText(item, "typeName", out var typeName) is false || OptionalText(item, "scope", out var scope) is false)
                return false;
            if (read.All(existing => existing.Name != name || existing.TypeName != typeName || existing.Scope != scope))
                read.Add(new(name!, typeName, scope));
        }
        references = read;
        return true;
    }

    /// <summary>True when the property is absent, null or a bounded string; false when it is anything else.</summary>
    private static bool OptionalText(JsonElement body, string property, out string? value)
    {
        value = null;
        if (!body.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        return value is not null && value.Length <= 256;
    }
    private static string Bounded(string? value) => value is null ? string.Empty : value[..Math.Min(value.Length, 256)];
}

/// <summary>One slot an activity declares for its children, as the designer resolves it.</summary>
internal sealed record WorkflowAgentSlot(string Id, string Mode, string Cardinality);

/// <summary>One activity's place in the tree: its exact version, its parent and the parent's slot, and its own slots.</summary>
internal sealed record WorkflowAgentNode(string Id, string Type, string DisplayName, string ActivityVersionId, string? ParentId,
    string? Slot, IReadOnlyList<WorkflowAgentSlot> Slots);

/// <summary>A Secret Reference by name, type and scope. It names a secret; it never holds a value.</summary>
internal sealed record WorkflowAgentSecretReference(string Name, string? TypeName, string? Scope);
