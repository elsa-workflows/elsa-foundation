using System.Text.Json;

namespace Elsa.Weaver.Workflows.Services;

/// <summary>
/// The explanation additions to <c>workflow.definition</c> schema <c>1</c> (spec 1193 slice D): the designer's current
/// validation diagnostics, the selected activity's declared property context, and the graph activities the draft references.
/// Each is optional; when present it is read strictly, and a malformed one refuses the whole snapshot. Everything here is
/// resource content the editor reports: names, messages and expression text are data, never instructions.
/// </summary>
internal static class WorkflowWeaverExplainContext
{
    internal const int MaxDiagnostics = 64;
    internal const int MaxDiagnosticMessage = 1024;
    internal const int MaxProperties = 64;
    /// <summary>The longest expression or literal content one property discloses; longer content is cut and marked truncated.</summary>
    internal const int MaxPropertyContent = 2048;
    /// <summary>The total content all disclosed properties may carry together, so property context stays a bounded share of the snapshot.</summary>
    internal const int PropertyContentBudget = 16384;
    internal const int MaxGraphActivities = 64;
    /// <summary>
    /// The interpretation status the server stamps on each non-literal expression. Interpretation, semantic diagnostics and
    /// edits belong to the owning Expression Module's Weaver Tools (Studio ADR 0002); no Expression Module registers any, so
    /// they are unavailable. The editor cannot claim otherwise: an incoming value other than this one is refused.
    /// </summary>
    internal const string InterpretationUnavailable = "unavailable";
    internal const string LiteralExpressionType = "Literal";
    internal const string SecretExpressionType = "Secret";

    internal static bool TryReadDiagnostics(JsonElement body, out IReadOnlyList<WorkflowWeaverDiagnostic> diagnostics,
        out int? omitted)
    {
        diagnostics = [];
        omitted = null;
        if (body.TryGetProperty("diagnosticsOmitted", out var count))
        {
            if (!body.TryGetProperty("diagnostics", out _) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var value) || value is < 1 or > 100_000)
                return false;
            omitted = value;
        }
        if (!body.TryGetProperty("diagnostics", out var items)) return true;
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > MaxDiagnostics) return false;
        var read = new List<WorkflowWeaverDiagnostic>();
        foreach (var item in items.EnumerateArray())
        {
            if (!Only(item, ["severity", "type", "nodeId", "property", "message"], ["severity", "message"])) return false;
            var severity = WorkflowWeaverSnapshot.Text(item, "severity");
            var message = WorkflowWeaverSnapshot.Text(item, "message");
            if (severity is not ("error" or "warning") || string.IsNullOrWhiteSpace(message) || message.Length > MaxDiagnosticMessage
                || !Optional(item, "type", 128, out var type) || !Optional(item, "nodeId", 256, out var nodeId) || !Optional(item, "property", 256, out var property))
                return false;
            // A property names an input of an activity, so it needs the activity. The activity may be one the designer still
            // reports while the working state no longer holds it (a persisted validation of an older revision), so it is only
            // bounded here; the designer decides what it shows.
            if (property is not null && nodeId is null) return false;
            read.Add(new(severity, type, nodeId, property, message));
        }
        diagnostics = read;
        return true;
    }

    internal static bool TryReadSelectedActivity(JsonElement body, string? selectedNodeId, IReadOnlyDictionary<string, WorkflowWeaverNode>? structure,
        out WorkflowWeaverSelectedActivity? selected)
    {
        selected = null;
        if (!body.TryGetProperty("selectedActivity", out var value)) return true;
        if (!Only(value, ["nodeId", "activityVersionId", "truncated", "properties"], ["nodeId", "activityVersionId", "truncated", "properties"])) return false;
        var nodeId = WorkflowWeaverSnapshot.Text(value, "nodeId");
        var versionId = WorkflowWeaverSnapshot.Text(value, "activityVersionId");
        // Property context describes the selected activity only, in the exact version the structure binds it to. Without the
        // structure that version is unbound, so the property context is refused (Studio drops it before the structure).
        if (nodeId is null || nodeId != selectedNodeId || !Bounded(versionId, 256)
            || structure is null || !structure.TryGetValue(nodeId, out var node) || node.ActivityVersionId != versionId
            || !Boolean(value, "truncated", out var truncated))
            return false;
        var items = value.GetProperty("properties");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > MaxProperties) return false;
        var properties = new List<WorkflowWeaverProperty>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var budget = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (!Only(item, ["name", "referenceKey", "typeName", "isCredential", "isSensitive", "value"], ["name", "referenceKey", "typeName", "isCredential", "isSensitive", "value"]))
                return false;
            var name = WorkflowWeaverSnapshot.Text(item, "name");
            var referenceKey = WorkflowWeaverSnapshot.Text(item, "referenceKey");
            var typeName = WorkflowWeaverSnapshot.Text(item, "typeName");
            if (!Bounded(name, 256) || !Bounded(referenceKey, 256) || typeName is null || typeName.Length > 256 || !keys.Add(referenceKey!)
                || !Boolean(item, "isCredential", out var credential) || !Boolean(item, "isSensitive", out var sensitive)
                // A credential input is sensitive by declaration (the catalog reports it so).
                || credential && !sensitive
                || !TryReadValue(item.GetProperty("value"), sensitive, out var property))
                return false;
            budget += property.Content?.Length ?? 0;
            if (budget > PropertyContentBudget) return false;
            properties.Add(property with { Name = name!, ReferenceKey = referenceKey!, TypeName = typeName, IsCredential = credential, IsSensitive = sensitive });
        }
        selected = new(nodeId, versionId!, truncated, properties);
        return true;
    }

    /// <summary>
    /// One property's value. A sensitive or credential input never carries content: it is unset, present and withheld, or
    /// bound to a Secret Reference (which names a secret and holds no value). A Secret syntax value is never content either.
    /// Anything else is expression content, bounded.
    /// </summary>
    private static bool TryReadValue(JsonElement value, bool sensitive, out WorkflowWeaverProperty property)
    {
        property = new(string.Empty, string.Empty, string.Empty, false, false, "unset", null, null, false, 0, null);
        var kind = WorkflowWeaverSnapshot.Text(value, "kind");
        switch (kind)
        {
            case "unset":
                return Only(value, ["kind"], ["kind"]);
            case "withheld":
            {
                if (!Only(value, ["kind", "expressionType"], ["kind", "expressionType"])) return false;
                var type = WorkflowWeaverSnapshot.Text(value, "expressionType");
                // Withheld is a masked input's value, or a Secret syntax that names no Secret (its text may be the secret itself).
                if (!Bounded(type, 128) || !sensitive && type != SecretExpressionType) return false;
                property = property with { Kind = kind, ExpressionType = type };
                return true;
            }
            case "secretReference":
            {
                if (!Only(value, ["kind", "secretReference"], ["kind", "secretReference"])) return false;
                var reference = value.GetProperty("secretReference");
                if (!Only(reference, ["name", "typeName", "scope"], ["name"])) return false;
                var name = WorkflowWeaverSnapshot.Text(reference, "name");
                if (!Bounded(name, 256) || !Optional(reference, "typeName", 256, out var typeName) || !Optional(reference, "scope", 256, out var scope))
                    return false;
                property = property with { Kind = kind, ExpressionType = SecretExpressionType, SecretReference = new(name!, typeName, scope) };
                return true;
            }
            case "expression":
            {
                if (sensitive || !Only(value, ["kind", "expressionType", "content", "truncated", "length", "interpretation"], ["kind", "expressionType", "content", "truncated", "length"]))
                    return false;
                var type = WorkflowWeaverSnapshot.Text(value, "expressionType");
                var content = WorkflowWeaverSnapshot.Text(value, "content");
                if (!Bounded(type, 128) || type == SecretExpressionType || content is null || content.Length > MaxPropertyContent
                    || !Boolean(value, "truncated", out var truncated)
                    || value.GetProperty("length") is not { ValueKind: JsonValueKind.Number } measured || !measured.TryGetInt32(out var length) || length < content.Length
                    // Truncation is marked exactly: content is cut only when the original was longer.
                    || truncated != length > content.Length)
                    return false;
                // The editor cannot claim an Expression Module's interpretation; the server stamps it again on admission.
                if (value.TryGetProperty("interpretation", out var interpretation)
                    && WorkflowWeaverSnapshot.String(interpretation) != InterpretationUnavailable)
                    return false;
                property = property with { Kind = kind, ExpressionType = type, Content = content, Truncated = truncated, Length = length };
                return true;
            }
            default:
                return false;
        }
    }

    internal static bool TryReadGraphActivities(JsonElement body, int activityCount, IReadOnlyList<string> activityIds,
        IReadOnlyDictionary<string, WorkflowWeaverNode>? structure, out IReadOnlyList<WorkflowWeaverGraphActivity> graphActivities)
    {
        graphActivities = [];
        if (!body.TryGetProperty("graphActivities", out var items)) return true;
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > MaxGraphActivities) return false;
        var claimed = new HashSet<int>();
        var read = new List<WorkflowWeaverGraphActivity>();
        foreach (var item in items.EnumerateArray())
        {
            if (!Only(item, ["activityDefinitionId", "activityVersionId", "version", "nodes"], ["activityDefinitionId", "activityVersionId", "version", "nodes"]))
                return false;
            var definitionId = WorkflowWeaverSnapshot.Text(item, "activityDefinitionId");
            var versionId = WorkflowWeaverSnapshot.Text(item, "activityVersionId");
            var version = WorkflowWeaverSnapshot.Text(item, "version");
            var nodes = item.GetProperty("nodes");
            if (!Bounded(definitionId, 256) || !Bounded(versionId, 256) || !Bounded(version, 256)
                || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() is 0 || nodes.GetArrayLength() > activityCount)
                return false;
            var members = new List<int>();
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Number || !node.TryGetInt32(out var index) || index < 0 || index >= activityCount || !claimed.Add(index))
                    return false;
                // A graph activity is referenced by its exact version; the structure must agree on the node's version.
                if (structure is not null && structure[activityIds[index]].ActivityVersionId != versionId) return false;
                members.Add(index);
            }
            read.Add(new(definitionId!, versionId!, version!, members));
        }
        graphActivities = read;
        return true;
    }

    /// <summary>The admitted projection of the diagnostics, in the editor's order and shape: absent parts stay absent.</summary>
    internal static object[] Project(IReadOnlyList<WorkflowWeaverDiagnostic> diagnostics) =>
        diagnostics.Select(item =>
        {
            var projected = new Dictionary<string, string> { ["severity"] = item.Severity, ["message"] = item.Message };
            if (item.Type is not null) projected["type"] = item.Type;
            if (item.NodeId is not null) projected["nodeId"] = item.NodeId;
            if (item.Property is not null) projected["property"] = item.Property;
            return (object)projected;
        }).ToArray();

    /// <summary>The admitted projection of the selected activity, with the server's interpretation status on each non-literal expression.</summary>
    internal static object Project(WorkflowWeaverSelectedActivity selected) => new
    {
        nodeId = selected.NodeId, activityVersionId = selected.ActivityVersionId, truncated = selected.Truncated,
        properties = selected.Properties.Select(property => new
        {
            name = property.Name, referenceKey = property.ReferenceKey, typeName = property.TypeName,
            isCredential = property.IsCredential, isSensitive = property.IsSensitive, value = ProjectValue(property)
        }).ToArray()
    };

    private static object ProjectValue(WorkflowWeaverProperty property) => property.Kind switch
    {
        "withheld" => new { kind = property.Kind, expressionType = property.ExpressionType },
        "secretReference" => new
        {
            kind = property.Kind,
            secretReference = new { name = property.SecretReference!.Name, typeName = property.SecretReference.TypeName, scope = property.SecretReference.Scope }
        },
        "expression" when property.ExpressionType != LiteralExpressionType => new
        {
            kind = property.Kind, expressionType = property.ExpressionType, content = property.Content, truncated = property.Truncated,
            length = property.Length, interpretation = InterpretationUnavailable
        },
        "expression" => new
        {
            kind = property.Kind, expressionType = property.ExpressionType, content = property.Content, truncated = property.Truncated, length = property.Length
        },
        _ => (object)new { kind = property.Kind }
    };

    internal static object[] Project(IReadOnlyList<WorkflowWeaverGraphActivity> graphActivities) =>
        graphActivities.Select(item => new
        {
            activityDefinitionId = item.ActivityDefinitionId, activityVersionId = item.ActivityVersionId, version = item.Version, nodes = item.Nodes
        }).ToArray<object>();

    /// <summary>The serialized size the read tool's scope-path addresses may take; past it the result says they were cut.</summary>
    internal const int AddressBudget = 24 * 1024;

    /// <summary>
    /// Each activity's address as an Operation Batch names it (FR-014): the container ids from the root to the container that
    /// holds it, and its slot there. The root's scope path is empty. Bounded; <c>Truncated</c> says when the budget ran out.
    /// </summary>
    internal static (IReadOnlyList<object> Addresses, bool Truncated) Addresses(IReadOnlyList<string> activityIds, IReadOnlyDictionary<string, WorkflowWeaverNode> structure)
    {
        var addresses = new List<object>();
        var size = 0;
        foreach (var id in activityIds)
        {
            var node = structure[id];
            var path = new List<string>();
            for (var parent = node.ParentId; parent is not null; parent = structure[parent].ParentId) path.Insert(0, parent);
            var address = new { id, scopePath = path, slot = node.Slot };
            size += JsonSerializer.SerializeToUtf8Bytes(address).Length + 1;
            if (size > AddressBudget) return (addresses, true);
            addresses.Add(address);
        }
        return (addresses, false);
    }

    /// <summary>True when the object has only these fields and every required one, each once.</summary>
    private static bool Only(JsonElement value, IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> required)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        return names.All(name => allowed.Contains(name, StringComparer.Ordinal)) && names.Length == names.Distinct(StringComparer.Ordinal).Count()
            && required.All(name => names.Contains(name, StringComparer.Ordinal));
    }

    private static bool Bounded(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;

    private static bool Boolean(JsonElement value, string property, out bool result)
    {
        result = false;
        if (!value.TryGetProperty(property, out var element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        result = element.GetBoolean();
        return true;
    }

    /// <summary>True when the property is absent or null, or a non-blank string within the limit.</summary>
    private static bool Optional(JsonElement value, string property, int limit, out string? text)
    {
        text = null;
        if (!value.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        text = WorkflowWeaverSnapshot.String(element);
        return Bounded(text, limit);
    }
}

/// <summary>One validation diagnostic the designer shows, by activity and input where it names them. The message is resource content.</summary>
internal sealed record WorkflowWeaverDiagnostic(string Severity, string? Type, string? NodeId, string? Property, string Message);

/// <summary>The selected activity's declared properties as the editor discloses them (FR-016, FR-017).</summary>
internal sealed record WorkflowWeaverSelectedActivity(string NodeId, string ActivityVersionId, bool Truncated, IReadOnlyList<WorkflowWeaverProperty> Properties);

/// <summary>
/// One declared property. <c>Kind</c> is <c>unset</c>, <c>expression</c> (type and bounded content), <c>withheld</c> (a
/// sensitive or credential value that is present and never disclosed) or <c>secretReference</c> (name, type and scope).
/// </summary>
internal sealed record WorkflowWeaverProperty(string Name, string ReferenceKey, string TypeName, bool IsCredential, bool IsSensitive, string Kind,
    string? ExpressionType, string? Content, bool Truncated, int Length, WorkflowWeaverSecretReference? SecretReference);

/// <summary>A graph activity the draft references: its definition identity and exact version, and the activity indexes that use it. Never its body.</summary>
internal sealed record WorkflowWeaverGraphActivity(string ActivityDefinitionId, string ActivityVersionId, string Version, IReadOnlyList<int> Nodes);
