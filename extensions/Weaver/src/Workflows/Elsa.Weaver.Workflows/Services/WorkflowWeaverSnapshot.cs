using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Models;

namespace Elsa.Weaver.Workflows.Services;

/// <summary>The workflow module's bounded, value-free admission schema for an open editor.</summary>
/// <remarks>
/// The compact containment structure (each activity's exact version, parent, slot and its own slots), the working-state
/// kind and the visible Secret References are optional additions to schema <c>1</c>. A snapshot that carries structure
/// must describe every activity and one tree; the Operation Batch tool requires it. The designer's diagnostics, the selected
/// activity's property context and the graph activity references are further optional additions, read strictly by
/// <see cref="WorkflowWeaverExplainContext"/>.
/// </remarks>
internal sealed record WorkflowWeaverSnapshot(WeaverResourceTarget Target, WeaverClientBinding Client, string DraftId,
    WorkflowWeaverGraph Graph, JsonElement Content)
{
    /// <summary>The containment structure by node id, or null when the editor did not disclose it.</summary>
    internal IReadOnlyDictionary<string, WorkflowWeaverNode>? Structure { get; init; }
    /// <summary><c>draft</c> for the editable working draft; anything else (a historical snapshot) is read-only.</summary>
    internal string? WorkingState { get; init; }
    /// <summary>The Secret References the editor reports as visible to the user: names, types and scopes only.</summary>
    internal IReadOnlyList<WorkflowWeaverSecretReference> SecretReferences { get; init; } = [];
    /// <summary>Ids the document holds outside its tree (layout and presentation records); a new node never reuses one.</summary>
    internal IReadOnlyList<string> ReservedIds { get; init; } = [];
    internal string? SelectedNodeId { get; init; }
    /// <summary>The validation diagnostics the designer shows for this working state (FR-018); empty when none were disclosed.</summary>
    internal IReadOnlyList<WorkflowWeaverDiagnostic> Diagnostics { get; init; } = [];
    /// <summary>The selected activity's declared property context (FR-016, FR-017), or null when none was disclosed.</summary>
    internal WorkflowWeaverSelectedActivity? SelectedActivity { get; init; }
    /// <summary>The graph activities the draft references by definition identity and version (FR-014).</summary>
    internal IReadOnlyList<WorkflowWeaverGraphActivity> GraphActivities { get; init; } = [];

    /// <summary>The template editor's persisted template revision (FR-005), the expected revision of its explicit save; null for a definition.</summary>
    internal string? TemplateRevision { get; init; }
    /// <summary>Whether this is a Reusable Workflow Template's editor working state rather than a workflow definition draft.</summary>
    internal bool IsTemplate => Target.ResourceType == TemplateResourceType;

    internal const string ModuleId = "Elsa.Studio.Workflows";
    internal const string ResourceType = "workflow-definition";
    /// <summary>The Reusable Workflow Template editor's working state (spec 1193 FR-024): its own resource, never a definition.</summary>
    internal const string TemplateContentType = "workflows.template-editor";
    internal const string TemplateResourceType = "workflow-template";

    /// <summary>Whether the target is one of this module's editor working states: a definition draft or a template.</summary>
    internal static bool IsEditorTarget(WeaverResourceTarget? target) =>
        target is { ModuleId: ModuleId } && target.ResourceType is ResourceType or TemplateResourceType;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The same snapshot without the selected activity's property context, for when the owning catalog cannot confirm it.</summary>
    internal WorkflowWeaverSnapshot WithoutSelectedActivity()
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse(Content.GetRawText())!.AsObject();
        body.Remove("selectedActivity");
        return this with { Content = JsonSerializer.SerializeToElement(body, JsonOptions), SelectedActivity = null };
    }

    internal static bool SameTarget(WeaverResourceTarget left, WeaverResourceTarget right) =>
        left.ModuleId == right.ModuleId && left.ResourceType == right.ResourceType &&
        left.ResourceId == right.ResourceId && left.Revision == right.Revision;

    internal static bool TryRead(WeaverContextAttachment attachment, [NotNullWhen(true)] out WorkflowWeaverSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = "The workflow snapshot requires an exact definition, draft, revision and client generation.";
        // A template editor discloses the same working-state shape under its own kind, resource type and identity field.
        var template = attachment.ContentType == TemplateContentType;
        var resourceType = template ? TemplateResourceType : ResourceType;
        var idField = template ? "templateId" : "workflowDefinitionId";
        if (attachment.ModuleId != ModuleId || attachment.ContentType != (template ? TemplateContentType : "workflow.definition") || attachment.SchemaVersion != "1" ||
            attachment.Target is not { ModuleId: ModuleId } target || target.ResourceType != resourceType ||
            !ValidString(target.ResourceId) || target.ResourceId == "active-draft" || !ValidString(target.Revision) ||
            attachment.Client is not { } client || !ValidString(client.ClientId) || !ValidString(client.GenerationId))
            return false;
        JsonElement body;
        try { body = attachment.Content is JsonElement json ? json : JsonSerializer.SerializeToElement(attachment.Content, JsonOptions); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException) { return false; }
        // Malformed UTF-16 (a lone surrogate escape) throws wherever a name or string is read (JsonProperty.Name,
        // GetString), so it is refused once, up front, like any other malformed snapshot; every later read is then safe.
        if (body.ValueKind != JsonValueKind.Object || body.GetRawText().Length > 65536 || !WellFormedText(body)) return false;
        if (Text(body, idField) != target.ResourceId || Text(body, "revision") != target.Revision ||
            (template ? !ValidString(Text(body, "templateRevision")) || body.TryGetProperty("draftId", out _) : !ValidString(Text(body, "draftId"))))
            return false;
        if (!body.TryGetProperty("activities", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() > 256 ||
            !body.TryGetProperty("connections", out var edges) || edges.ValueKind != JsonValueKind.Array || edges.GetArrayLength() > 512 ||
            !body.TryGetProperty("positions", out var positions) || positions.ValueKind != JsonValueKind.Array || positions.GetArrayLength() > 256)
            return false;
        var activities = new List<WorkflowWeaverActivitySummary>();
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object || !ValidString(Text(node, "id")) || !ValidString(Text(node, "type")) ||
                !nodeIds.Add(Text(node, "id")!)) return false;
            activities.Add(new(Text(node, "id")!, Text(node, "type")!, Bounded(Text(node, "displayName"))));
        }
        if (!TryReadStructure(body, activities, out var structure, out var admittedStructure, out var reservedIds)) return false;
        var connections = new List<WorkflowWeaverConnectionSummary>();
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
        // Slice D: the designer's diagnostics, the selected activity's property context and the graph activity references.
        if (!WorkflowWeaverExplainContext.TryReadDiagnostics(body, out var diagnostics, out var diagnosticsOmitted)
            || !WorkflowWeaverExplainContext.TryReadSelectedActivity(body, selectedNodeId, structure, out var selectedActivity)
            || !WorkflowWeaverExplainContext.TryReadGraphActivities(body, activities.Count, activities.Select(item => item.Id).ToArray(), structure, out var graphActivities))
            return false;
        // Only declared graph metadata is copied. Caller summaries, permission claims, guidance, property values outside the
        // selected activity's bounded property context, and arbitrary extra fields are discarded.
        // The optional additions are copied only when the editor disclosed them, so an older snapshot admits unchanged.
        var admittedBody = new Dictionary<string, object?> { [idField] = target.ResourceId };
        if (template) admittedBody["templateRevision"] = Text(body, "templateRevision");
        else admittedBody["draftId"] = Text(body, "draftId");
        admittedBody["revision"] = target.Revision;
        admittedBody["selectedNodeId"] = selectedNodeId;
        if (workingState is not null) admittedBody["workingState"] = workingState;
        admittedBody["activities"] = activities;
        if (admittedStructure is not null) admittedBody["structure"] = admittedStructure;
        admittedBody["connections"] = connections;
        admittedBody["positions"] = admittedPositions;
        if (body.TryGetProperty("secretReferences", out _))
            admittedBody["secretReferences"] = secretReferences.Select(item => new { name = item.Name, typeName = item.TypeName, scope = item.Scope }).ToArray();
        if (body.TryGetProperty("diagnostics", out _)) admittedBody["diagnostics"] = WorkflowWeaverExplainContext.Project(diagnostics);
        if (diagnosticsOmitted is not null) admittedBody["diagnosticsOmitted"] = diagnosticsOmitted;
        if (selectedActivity is not null) admittedBody["selectedActivity"] = WorkflowWeaverExplainContext.Project(selectedActivity);
        if (body.TryGetProperty("graphActivities", out _)) admittedBody["graphActivities"] = WorkflowWeaverExplainContext.Project(graphActivities);
        var admitted = JsonSerializer.SerializeToElement(admittedBody, JsonOptions);
        snapshot = new(new(ModuleId, resourceType, target.ResourceId, target.Revision), client, template ? target.ResourceId! : Text(body, "draftId")!,
            new(target.Revision, activities, connections), admitted)
        {
            TemplateRevision = template ? Text(body, "templateRevision") : null,
            // An empty draft has no structure to withhold, so it is structured (and empty) by definition.
            Structure = structure ?? (activities.Count == 0 ? new Dictionary<string, WorkflowWeaverNode>() : null), ReservedIds = reservedIds, WorkingState = workingState, SecretReferences = secretReferences,
            SelectedNodeId = selectedNodeId, Diagnostics = diagnostics, SelectedActivity = selectedActivity, GraphActivities = graphActivities
        };
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether every property name and string value in the (already size-bounded) body is well-formed UTF-16. Reading one
    /// that holds a lone surrogate escape throws <see cref="InvalidOperationException"/>; only that read is guarded here.
    /// </summary>
    private static bool WellFormedText(JsonElement body)
    {
        var pending = new Stack<JsonElement>();
        pending.Push(body);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            switch (current.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in current.EnumerateObject())
                    {
                        try { _ = property.Name; }
                        catch (InvalidOperationException) { return false; }
                        pending.Push(property.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in current.EnumerateArray()) pending.Push(item);
                    break;
                case JsonValueKind.String:
                    try { _ = current.GetString(); }
                    catch (InvalidOperationException) { return false; }
                    break;
            }
        }
        return true;
    }

    internal static string? Text(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) ? String(value) : null;

    /// <summary>
    /// The element's string, or null when it is not a string or holds malformed UTF-16 (a lone surrogate escape), which
    /// <see cref="JsonElement.GetString"/> would throw on: such a value is refused like any other malformed field.
    /// </summary>
    internal static string? String(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return null;
        try { return value.GetString(); }
        catch (InvalidOperationException) { return null; }
    }
    internal static bool Number(JsonElement body, string property, out double number)
    {
        number = 0;
        return body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out number) && double.IsFinite(number);
    }
    private static bool ValidString(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;

    /// <summary>
    /// Reads the optional compact containment structure: <c>versions</c> groups activity indexes by exact activity version,
    /// optional <c>reservedIds</c> lists ids the document holds only in its layout or presentation records,
    /// and <c>containers</c> lists the root and every activity that declares slots, each with its parent, its slot in the
    /// parent and its slots, whose <c>nodes</c> are indexes into <c>activities</c>. Every activity has exactly one version and,
    /// except the root, exactly one slot; the result must be one tree. False when present but malformed.
    /// </summary>
    /// <remarks>Indexes keep the structure small: a draft that fits the 64 KiB bound without structure still fits with it.</remarks>
    private static bool TryReadStructure(JsonElement body, IReadOnlyList<WorkflowWeaverActivitySummary> activities,
        out Dictionary<string, WorkflowWeaverNode>? structure, out object? admitted, out IReadOnlyList<string> reservedIds)
    {
        structure = null;
        admitted = null;
        reservedIds = [];
        if (!body.TryGetProperty("structure", out var value)) return true;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(property => property.Name is not ("versions" or "containers" or "reservedIds"))
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
        var slotsOf = new Dictionary<int, IReadOnlyList<WorkflowWeaverSlot>>();
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
            var declared = new List<WorkflowWeaverSlot>();
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
        var reserved = new List<string>();
        if (value.TryGetProperty("reservedIds", out var reservedItems))
        {
            if (reservedItems.ValueKind != JsonValueKind.Array || reservedItems.GetArrayLength() > 256) return false;
            foreach (var item in reservedItems.EnumerateArray())
            {
                var id = String(item);
                if (!ValidString(id) || indexOf.ContainsKey(id!) || reserved.Contains(id!)) return false;
                reserved.Add(id!);
            }
        }
        reservedIds = reserved;
        admitted = value.TryGetProperty("reservedIds", out _)
            ? new { versions = admittedVersions, containers = admittedContainers, reservedIds = reserved }
            : (object)new { versions = admittedVersions, containers = admittedContainers };
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

    private static bool TryReadSecretReferences(JsonElement body, out IReadOnlyList<WorkflowWeaverSecretReference> references)
    {
        references = [];
        if (!body.TryGetProperty("secretReferences", out var items)) return true;
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 64) return false;
        var read = new List<WorkflowWeaverSecretReference>();
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
        value = String(element);
        return value is not null && value.Length <= 256;
    }
    private static string Bounded(string? value) => value is null ? string.Empty : value[..Math.Min(value.Length, 256)];
}

/// <summary>One slot an activity declares for its children, as the designer resolves it.</summary>
internal sealed record WorkflowWeaverSlot(string Id, string Mode, string Cardinality);

/// <summary>One activity's place in the tree: its exact version, its parent and the parent's slot, and its own slots.</summary>
internal sealed record WorkflowWeaverNode(string Id, string Type, string DisplayName, string ActivityVersionId, string? ParentId,
    string? Slot, IReadOnlyList<WorkflowWeaverSlot> Slots);

/// <summary>A Secret Reference by name, type and scope. It names a secret; it never holds a value.</summary>
internal sealed record WorkflowWeaverSecretReference(string Name, string? TypeName, string? Scope);
