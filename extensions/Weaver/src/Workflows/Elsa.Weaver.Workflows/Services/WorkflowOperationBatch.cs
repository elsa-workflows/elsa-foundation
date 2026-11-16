using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Elsa.Activities.Design.Core.Models;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Workflows.Services;

/// <summary>
/// The strict contract of <c>workflow_apply_operation_batch</c> schema <c>1</c>: parsing the model's operations, validating
/// them against the admitted working-state snapshot and the owning activity catalog, resolving every reference to an exact
/// node, classifying risk, and freezing the batch the originating editor applies. Nothing here guesses: an unknown field,
/// an unknown or ambiguous target, an activity the catalog does not offer, an undeclared input or a credential literal ends
/// the whole batch with a refusal or a clarification that names the failing operation.
/// </summary>
internal static partial class WorkflowOperationBatch
{
    internal const string SchemaVersion = "1";
    internal const string ResultType = "workflows.batch-result";

    /// <summary>The hard bound: a larger batch is refused whole, never split.</summary>
    internal const int MaxOperations = 64;

    /// <summary>The declared complexity limit: a larger batch is labelled high-complexity in review, never split.</summary>
    internal const int ComplexityLimit = 16;

    internal const int MaxLiteralLength = 4096;
    internal const int MaxDisplayNameLength = 200;
    internal const int MaxDescriptionLength = 2000;
    private const double MaxCoordinate = 1_000_000;

    internal const string SchemaInvalid = "weaver.workflow.batch.schema_invalid";
    internal const string ReferenceInvalid = "weaver.workflow.batch.reference_invalid";
    internal const string ClarificationRequired = "weaver.workflow.batch.clarification_required";
    internal const string CredentialLiteral = "weaver.workflow.batch.credential_literal";
    internal const string ExpressionUnsupported = "weaver.workflow.batch.expression_unsupported";
    internal const string NotEditable = "weaver.workflow.batch.target_not_editable";
    internal const string StructureRequired = "weaver.workflow.batch.structure_required";

    private const string RootSlot = "root";
    private const string NothingChanged = " The whole batch was refused and nothing changed.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record Failure(string Code, string Message, int Status);

    /// <summary>The frozen batch, its review preview and its highest operation risk.</summary>
    internal sealed record Prepared(JsonElement Arguments, JsonElement Preview, WeaverRisk Risk, IReadOnlyList<FrozenOperation> Operations);

    /// <summary>One frozen operation: its kind, its exact target node (if any), its risk and the frozen JSON the editor applies.</summary>
    internal sealed record FrozenOperation(int Index, string Kind, string? NodeId, WeaverRisk Risk, JsonObject Json, string Summary,
        IReadOnlyList<string> Reasons);

    /// <summary>The tool's input schema, as advertised to providers. Strict: every object refuses additional properties.</summary>
    internal static string InputSchema { get; } = BuildSchema();

    // ---- Parsing -------------------------------------------------------------------------------------------------

    internal abstract record Operation(int Index, string Kind);
    internal sealed record ActivityReference(string? Id, string? Ref, string? Name)
    {
        public override string ToString() => Id is not null ? $"id '{Id}'" : Ref is not null ? $"ref '{Ref}'" : $"name '{Name}'";
    }
    internal sealed record AddOperation(int Index, string Ref, string ActivityTypeKey, string Version, IReadOnlyList<string> ScopePath, string Slot,
        (double X, double Y)? Position) : Operation(Index, "add");
    internal sealed record SetRootOperation(int Index, ActivityReference Activity) : Operation(Index, "set-root");
    internal sealed record PositionOperation(int Index, ActivityReference Activity, double X, double Y) : Operation(Index, "position");
    internal sealed record SetPropertyOperation(int Index, ActivityReference Activity, string Property, JsonElement? Literal,
        WorkflowWeaverSecretReference? Secret) : Operation(Index, "set-property");
    internal sealed record UpdateOperation(int Index, ActivityReference Activity, string? DisplayName, string? Description, bool? CanStartWorkflow)
        : Operation(Index, "update");
    internal sealed record RemoveOperation(int Index, ActivityReference Activity) : Operation(Index, "remove");
    internal sealed record ConnectOperation(int Index, ActivityReference Source, ActivityReference Target, string? SourcePort, string? TargetPort)
        : Operation(Index, "connect");
    internal sealed record DisconnectOperation(int Index, ActivityReference Source, ActivityReference Target, string? SourcePort)
        : Operation(Index, "disconnect");

    [GeneratedRegex("^[a-z][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    /// <summary>Parses the arguments strictly. Any unknown field, missing field or wrong type refuses the whole batch.</summary>
    internal static (IReadOnlyList<Operation>? Operations, Failure? Failure) Parse(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return (null, Schema(null, null, "The arguments must be an object with an 'operations' array."));
        if (Fields(arguments, ["operations"], ["operations"]) is { } top) return (null, Schema(null, null, top));
        var items = arguments.GetProperty("operations");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return (null, Schema(null, null, "'operations' must be a non-empty array."));
        if (items.GetArrayLength() > MaxOperations)
            return (null, Schema(null, null, $"A batch holds at most {MaxOperations} operations; this one has {items.GetArrayLength()}. It is never split, so ask for a smaller change."));
        var operations = new List<Operation>();
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var (operation, error) = ParseOperation(index, item);
            if (error is not null) return (null, error);
            operations.Add(operation!);
            index++;
        }
        return (operations, null);
    }

    private static (Operation?, Failure?) ParseOperation(int index, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return (null, Schema(index, null, "Each operation must be an object."));
        var kind = WorkflowWeaverSnapshot.Text(item, "kind");
        if (kind is null) return (null, Schema(index, null, "The operation has no string 'kind'."));
        string? error;
        switch (kind)
        {
            case "add":
            {
                if ((error = Fields(item, ["kind", "ref", "activityTypeKey", "version", "scopePath", "slot", "position"],
                        ["kind", "ref", "activityTypeKey", "version", "scopePath", "slot"])) is not null) return (null, Schema(index, kind, error));
                var reference = Bounded(item, "ref");
                var typeKey = Bounded(item, "activityTypeKey");
                var version = Bounded(item, "version");
                var slot = Bounded(item, "slot");
                if (reference is null || typeKey is null || version is null || slot is null)
                    return (null, Schema(index, kind, "'ref', 'activityTypeKey', 'version' and 'slot' must be non-empty strings of at most 256 characters."));
                if (!ReferencePattern().IsMatch(reference))
                    return (null, Schema(index, kind, $"'ref' must match {ReferencePattern()}; it becomes the new activity's node id."));
                var path = item.GetProperty("scopePath");
                if (path.ValueKind != JsonValueKind.Array || path.GetArrayLength() > 32)
                    return (null, Schema(index, kind, "'scopePath' must be an array of at most 32 node ids or refs, from the root to the container."));
                var scope = new List<string>();
                foreach (var segment in path.EnumerateArray())
                {
                    if (segment.ValueKind != JsonValueKind.String || segment.GetString() is not { Length: > 0 and <= 256 } value)
                        return (null, Schema(index, kind, "Each 'scopePath' entry must be a non-empty node id or ref."));
                    scope.Add(value);
                }
                (double, double)? position = null;
                if (item.TryGetProperty("position", out var point))
                {
                    if (point.ValueKind != JsonValueKind.Object || Fields(point, ["x", "y"], ["x", "y"]) is not null
                        || !Coordinate(point, "x", out var x) || !Coordinate(point, "y", out var y))
                        return (null, Schema(index, kind, "'position' must be an object with exactly finite 'x' and 'y' numbers."));
                    position = (x, y);
                }
                return (new AddOperation(index, reference, typeKey, version, scope, slot, position), null);
            }
            case "set-root":
            {
                if ((error = Fields(item, ["kind", "activity"], ["kind", "activity"])) is not null) return (null, Schema(index, kind, error));
                var (activity, problem) = ParseReference(item, "activity");
                return problem is not null ? (null, Schema(index, kind, problem)) : (new SetRootOperation(index, activity!), null);
            }
            case "position":
            {
                if ((error = Fields(item, ["kind", "activity", "x", "y"], ["kind", "activity", "x", "y"])) is not null) return (null, Schema(index, kind, error));
                var (activity, problem) = ParseReference(item, "activity");
                if (problem is not null) return (null, Schema(index, kind, problem));
                if (!Coordinate(item, "x", out var x) || !Coordinate(item, "y", out var y))
                    return (null, Schema(index, kind, "'x' and 'y' must be finite numbers."));
                return (new PositionOperation(index, activity!, x, y), null);
            }
            case "set-property":
            {
                if ((error = Fields(item, ["kind", "activity", "property", "value"], ["kind", "activity", "property", "value"])) is not null)
                    return (null, Schema(index, kind, error));
                var (activity, problem) = ParseReference(item, "activity");
                if (problem is not null) return (null, Schema(index, kind, problem));
                if (Bounded(item, "property") is not { } property)
                    return (null, Schema(index, kind, "'property' must name the declared input explicitly; there is no default property."));
                var value = item.GetProperty("value");
                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("expression", out _))
                    return (null, new Failure(ExpressionUnsupported, $"Operation {index} (set-property): expression edits belong to the owning Expression Module's Weaver tools, and none is available to this batch. Only a literal value or a visible Secret Reference can be set here." + NothingChanged, 422));
                if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1)
                    return (null, Schema(index, kind, "'value' must be exactly one of {\"literal\": string|number|boolean} or {\"secretReference\": {\"name\": ...}}."));
                if (value.TryGetProperty("literal", out var literal))
                {
                    var valid = literal.ValueKind switch
                    {
                        JsonValueKind.String => literal.GetString()!.Length <= MaxLiteralLength,
                        JsonValueKind.Number => literal.TryGetDouble(out var number) && double.IsFinite(number),
                        JsonValueKind.True or JsonValueKind.False => true,
                        _ => false
                    };
                    return valid ? (new SetPropertyOperation(index, activity!, property, literal.Clone(), null), null)
                        : (null, Schema(index, kind, $"'literal' must be a string of at most {MaxLiteralLength} characters, a finite number or a boolean."));
                }
                if (value.TryGetProperty("secretReference", out var secret))
                {
                    if (secret.ValueKind != JsonValueKind.Object || Fields(secret, ["name", "typeName", "scope"], ["name"]) is not null
                        || Bounded(secret, "name") is not { } name || !OptionalBounded(secret, "typeName", out var typeName)
                        || !OptionalBounded(secret, "scope", out var scope))
                        return (null, Schema(index, kind, "'secretReference' must be an object with a 'name' and optional 'typeName' and 'scope' strings."));
                    return (new SetPropertyOperation(index, activity!, property, null, new(name, typeName, scope)), null);
                }
                return (null, Schema(index, kind, "'value' must be exactly one of {\"literal\": ...} or {\"secretReference\": ...}."));
            }
            case "update":
            {
                if ((error = Fields(item, ["kind", "activity", "fields"], ["kind", "activity", "fields"])) is not null) return (null, Schema(index, kind, error));
                var (activity, problem) = ParseReference(item, "activity");
                if (problem is not null) return (null, Schema(index, kind, problem));
                var fields = item.GetProperty("fields");
                var fieldError = fields.ValueKind == JsonValueKind.Object ? Fields(fields, ["displayName", "description", "canStartWorkflow"], []) : "'fields' must be an object.";
                if (fieldError is not null)
                    return (null, Schema(index, kind, fieldError + " 'fields' allows only 'displayName', 'description' and 'canStartWorkflow'; " +
                        "an activity's type, version, children and other fields are never rewritten."));
                if (!fields.EnumerateObject().Any()) return (null, Schema(index, kind, "'fields' must set at least one allowed field."));
                string? displayName = null, description = null;
                bool? canStart = null;
                if (fields.TryGetProperty("displayName", out var display))
                {
                    if (display.ValueKind != JsonValueKind.String || display.GetString()!.Length > MaxDisplayNameLength)
                        return (null, Schema(index, kind, $"'displayName' must be a string of at most {MaxDisplayNameLength} characters; an empty string restores the default."));
                    displayName = display.GetString();
                }
                if (fields.TryGetProperty("description", out var text))
                {
                    if (text.ValueKind != JsonValueKind.String || text.GetString()!.Length > MaxDescriptionLength)
                        return (null, Schema(index, kind, $"'description' must be a string of at most {MaxDescriptionLength} characters; an empty string clears it."));
                    description = text.GetString();
                }
                if (fields.TryGetProperty("canStartWorkflow", out var start))
                {
                    if (start.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return (null, Schema(index, kind, "'canStartWorkflow' must be a boolean."));
                    canStart = start.GetBoolean();
                }
                return (new UpdateOperation(index, activity!, displayName, description, canStart), null);
            }
            case "remove":
            {
                if ((error = Fields(item, ["kind", "activity"], ["kind", "activity"])) is not null) return (null, Schema(index, kind, error));
                var (activity, problem) = ParseReference(item, "activity");
                return problem is not null ? (null, Schema(index, kind, problem)) : (new RemoveOperation(index, activity!), null);
            }
            case "connect":
            case "disconnect":
            {
                var allowed = kind == "connect" ? new[] { "kind", "source", "target", "sourcePort", "targetPort" } : new[] { "kind", "source", "target", "sourcePort" };
                if ((error = Fields(item, allowed, ["kind", "source", "target"])) is not null) return (null, Schema(index, kind, error));
                var (source, sourceProblem) = ParseReference(item, "source");
                if (sourceProblem is not null) return (null, Schema(index, kind, sourceProblem));
                var (target, targetProblem) = ParseReference(item, "target");
                if (targetProblem is not null) return (null, Schema(index, kind, targetProblem));
                if (!OptionalBounded(item, "sourcePort", out var sourcePort) || !OptionalBounded(item, "targetPort", out var targetPort))
                    return (null, Schema(index, kind, "'sourcePort' and 'targetPort' must be non-empty strings when present."));
                return kind == "connect"
                    ? (new ConnectOperation(index, source!, target!, sourcePort, targetPort), null)
                    : (new DisconnectOperation(index, source!, target!, sourcePort), null);
            }
            default:
                return (null, Schema(index, kind, "Unknown operation kind. The kinds are add, set-root, position, set-property, update, remove, connect and disconnect."));
        }
    }

    private static (ActivityReference?, string?) ParseReference(JsonElement item, string property)
    {
        var value = item.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1
            || Fields(value, ["id", "ref", "name"], []) is not null)
            return (null, $"'{property}' must be exactly one of {{\"id\": existing node id}}, {{\"ref\": an earlier add's ref}} or {{\"name\": display name}}.");
        var single = value.EnumerateObject().Single();
        if (single.Value.ValueKind != JsonValueKind.String || single.Value.GetString() is not { Length: > 0 and <= 256 } text)
            return (null, $"'{property}.{single.Name}' must be a non-empty string of at most 256 characters.");
        return (single.Name switch
        {
            "id" => new ActivityReference(text, null, null),
            "ref" => new ActivityReference(null, text, null),
            _ => new ActivityReference(null, null, text.Trim())
        }, null);
    }

    /// <summary>Null when the object has only allowed fields and every required one; otherwise the exact problem.</summary>
    private static string? Fields(JsonElement value, IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> required)
    {
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        var unknown = names.Where(name => !allowed.Contains(name, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0) return $"Unknown field(s): {string.Join(", ", unknown.Take(8).Select(name => $"'{Clip(name, 64)}'"))}.";
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count()) return "A field is repeated.";
        var missing = required.Where(name => !names.Contains(name, StringComparer.Ordinal)).ToArray();
        return missing.Length > 0 ? $"Missing required field(s): {string.Join(", ", missing.Select(name => $"'{name}'"))}." : null;
    }

    private static string? Bounded(JsonElement value, string property)
        => WorkflowWeaverSnapshot.Text(value, property) is { Length: > 0 and <= 256 } text && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static bool OptionalBounded(JsonElement value, string property, out string? text)
    {
        text = null;
        if (!value.TryGetProperty(property, out _)) return true;
        text = Bounded(value, property);
        return text is not null;
    }

    private static bool Coordinate(JsonElement value, string property, out double number)
        => WorkflowWeaverSnapshot.Number(value, property, out number) && Math.Abs(number) <= MaxCoordinate;

    // ---- Validation, resolution and freezing ---------------------------------------------------------------------

    /// <summary>Whether any operation needs the owning catalog: a new activity, a declared input or the start setting.</summary>
    internal static bool NeedsCatalog(IEnumerable<Operation> operations) => operations.Any(operation => operation is AddOperation or SetPropertyOperation
        or UpdateOperation { CanStartWorkflow: not null });

    private sealed class Node(string id, string displayName, string activityVersionId, string? parentId, string? slot, bool added)
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public string ActivityVersionId { get; } = activityVersionId;
        public string? ParentId { get; } = parentId;
        public string? Slot { get; } = slot;
        public bool Added { get; } = added;
        public IReadOnlyList<WorkflowWeaverSlot>? Slots { get; init; }
        public ActivityAuthoringCatalogEntry? Entry { get; init; }
    }

    private sealed record Connection(string Source, string Target, string SourcePort, string? TargetPort);

    /// <summary>
    /// Validates the parsed operations in order against the snapshot and (when needed) the owning catalog, and freezes the
    /// resolved batch. <paramref name="catalog"/> is null only when <see cref="NeedsCatalog"/> is false.
    /// </summary>
    internal static (Prepared? Prepared, Failure? Failure) Validate(IReadOnlyList<Operation> operations, WorkflowWeaverSnapshot snapshot,
        WorkflowActivityCatalog.Read? catalog)
    {
        if (snapshot.WorkingState != "draft")
            return (null, new Failure(NotEditable, "The open workflow is a published or historical version, not the editable working draft, so it cannot be changed. Offer to work on the current draft instead; nothing was retargeted." + NothingChanged, 409));
        if (snapshot.Structure is null)
            return (null, new Failure(StructureRequired, "The editor did not disclose the workflow's structure (scope paths and slots), so no batch can be prepared against it." + NothingChanged, 409));
        if (catalog is { Status: not WorkflowActivityCatalog.Status.Available } && NeedsCatalog(operations))
            return (null, catalog.Status == WorkflowActivityCatalog.Status.Denied
                ? new Failure(ClarificationRequired, "The activity design catalog is not visible to you in this tenant, so no new activity or declared input can be confirmed. Ask for a change that only moves, connects, disconnects, renames or removes existing activities." + NothingChanged, 422)
                : new Failure("activities.catalog.unavailable", "The activity design catalog could not be read, so new activities and declared inputs cannot be confirmed now." + NothingChanged, 503));
        var entries = catalog?.Entries ?? [];
        var byVersion = entries.GroupBy(entry => entry.ActivityVersionId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var item in snapshot.Structure.Values)
            nodes[item.Id] = new(item.Id, item.DisplayName, item.ActivityVersionId, item.ParentId, item.Slot, false)
            {
                Slots = item.Slots, Entry = byVersion.GetValueOrDefault(item.ActivityVersionId)
            };
        var existingIds = nodes.Keys.ToHashSet(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        var refs = new Dictionary<string, AddOperation>(StringComparer.Ordinal);
        var pendingRoots = new HashSet<string>(StringComparer.Ordinal);
        var root = nodes.Values.SingleOrDefault(node => node.ParentId is null)?.Id;
        var rootInstalled = false;
        // Ids the document still holds in its side tables (layout, presentation) are never reused for a new node either.
        foreach (var id in snapshot.ReservedIds) existingIds.Add(id);
        var connections = snapshot.Graph.Connections
            .Select(edge => new Connection(edge.Source, edge.Target, Port(edge.SourcePort), string.IsNullOrEmpty(edge.TargetPort) ? null : edge.TargetPort)).ToList();
        var frozen = new List<FrozenOperation>();

        foreach (var operation in operations)
        {
            var (result, failure) = operation switch
            {
                AddOperation add => Add(add),
                SetRootOperation setRoot => SetRoot(setRoot),
                PositionOperation position => PositionOf(position),
                SetPropertyOperation property => SetProperty(property),
                UpdateOperation update => Update(update),
                RemoveOperation remove => Remove(remove),
                ConnectOperation connect => Connect(connect),
                DisconnectOperation disconnect => Disconnect(disconnect),
                _ => (null, Schema(operation.Index, operation.Kind, "Unsupported operation."))
            };
            if (failure is not null) return (null, failure);
            frozen.Add(result!);
        }
        if (pendingRoots.FirstOrDefault() is { } dangling)
            return (null, Reference(refs[dangling].Index, "add", $"'{dangling}' is added at the root scope, but no set-root installs it. Every root-scope add needs a set-root."));

        var highComplexity = frozen.Count > ComplexityLimit;
        var risk = frozen.Max(item => item.Risk);
        var arguments = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["highComplexity"] = highComplexity,
            ["risk"] = RiskName(risk),
            ["operations"] = new JsonArray(frozen.Select(item => (JsonNode)item.Json.DeepClone()).ToArray())
        };
        // A template batch names its template and the persisted template revision it is based on, never a definition.
        var preview = snapshot.IsTemplate
            ? new JsonObject { ["templateId"] = snapshot.Target.ResourceId, ["templateRevision"] = snapshot.TemplateRevision }
            : new JsonObject { ["workflowDefinitionId"] = snapshot.Target.ResourceId, ["draftId"] = snapshot.DraftId };
        foreach (var (name, value) in new JsonObject
        {
            ["revision"] = snapshot.Target.Revision,
            ["risk"] = RiskName(risk),
            ["highComplexity"] = highComplexity,
            ["complexityLimit"] = ComplexityLimit,
            ["operationCount"] = frozen.Count,
            ["operations"] = new JsonArray(frozen.Select(item => (JsonNode)new JsonObject
            {
                ["index"] = item.Index, ["kind"] = item.Kind, ["nodeId"] = item.NodeId, ["risk"] = RiskName(item.Risk),
                ["reasons"] = new JsonArray(item.Reasons.Select(reason => (JsonNode)JsonValue.Create(reason)!).ToArray()), ["summary"] = item.Summary
            }).ToArray()),
            ["clears"] = new JsonArray("testRunResult", "publishedArtifactAssociation"),
            ["summary"] = (highComplexity ? $"A high-complexity batch of {frozen.Count} operations (above the declared limit of {ComplexityLimit}); it is applied whole or not at all. " : "")
                + "Applies these operations together to the unsaved working draft as one designer-history step. The last test-run result and published-artifact association are cleared and Undo does not restore them. Saving and publishing remain explicit."
        }) preview[name] = value?.DeepClone();
        return (new(JsonSerializer.SerializeToElement(arguments, JsonOptions), JsonSerializer.SerializeToElement(preview, JsonOptions), risk, frozen), null);

        // -- Operation handlers (closures over the batch state) --

        (FrozenOperation?, Failure?) Add(AddOperation add)
        {
            if (existingIds.Contains(add.Ref) || refs.ContainsKey(add.Ref))
                return (null, Reference(add.Index, "add", $"'ref' '{add.Ref}' is already a node id or ref; a new activity never reuses an existing or removed id."));
            var matches = entries.Where(entry => entry.ActivityTypeKey == add.ActivityTypeKey && entry.Version == add.Version).ToArray();
            if (matches.Length == 0)
            {
                var versions = entries.Where(entry => entry.ActivityTypeKey == add.ActivityTypeKey).Select(entry => entry.Version).Distinct().Take(8).ToArray();
                return (null, Clarify(add.Index, "add", $"the activity type '{add.ActivityTypeKey}' version '{add.Version}' is not in the activity design catalog for this actor and tenant, so it cannot be added. "
                    + (versions.Length > 0 ? $"Installed versions of that type: {string.Join(", ", versions)}." : "No version of that type is installed or visible.")
                    + " Ask the user which installed activity to use; never substitute one."));
            }
            if (matches.Length > 1)
                return (null, Clarify(add.Index, "add", $"the activity type '{add.ActivityTypeKey}' version '{add.Version}' names {matches.Length} catalog entries ({string.Join(", ", matches.Take(4).Select(entry => entry.ActivityVersionId))}). Ask the user which one is meant."));
            var entry = matches[0];
            if (!entry.Available)
                return (null, Clarify(add.Index, "add", $"'{entry.DisplayName}' ({entry.ActivityTypeKey} {entry.Version}) is installed but cannot be added here: {(entry.AvailabilityReason ?? "the activity availability policy excludes it").TrimEnd('.')}. Existing activities of this type stay editable. Ask the user which available activity to use."));
            string? parentId = null;
            if (add.ScopePath.Count == 0)
            {
                if (add.Slot != RootSlot)
                    return (null, Schema(add.Index, "add", "An empty 'scopePath' adds a root candidate and requires 'slot': 'root'."));
                pendingRoots.Add(add.Ref);
            }
            else
            {
                var (container, problem) = ResolveScope(add);
                if (problem is not null) return (null, problem);
                parentId = container!.Id;
                // The catalog does not disclose a new activity's slots, so nothing is added into an activity this batch
                // adds: its slots could not be confirmed before review. A following batch can fill it.
                if (container.Added)
                    return (null, Reference(add.Index, "add", $"'{container.Id}' is added by this batch, so its slots cannot be confirmed yet; add activities into it in a following batch."));
                {
                    var slot = container.Slots!.FirstOrDefault(item => item.Id == add.Slot);
                    if (slot is null)
                        return (null, Clarify(add.Index, "add", $"'{Label(container)}' has no slot '{add.Slot}'. Its slots: {SlotList(container)}."));
                    if (slot.Mode == "bpmn")
                        return (null, Reference(add.Index, "add", $"the slot '{add.Slot}' of {Label(container)} is a BPMN scope, which an Operation Batch does not change."));
                    if (slot.Cardinality == "single" && nodes.Values.Any(node => node.ParentId == container.Id && node.Slot == add.Slot && !removed.Contains(node.Id)))
                        return (null, Reference(add.Index, "add", $"the slot '{add.Slot}' of '{Label(container)}' holds a single activity and is occupied. Remove that activity first."));
                }
            }
            var node = new Node(add.Ref, entry.DisplayName, entry.ActivityVersionId, parentId, parentId is null ? null : add.Slot, true) { Entry = entry };
            nodes[add.Ref] = node;
            refs[add.Ref] = add;
            var json = Op(add.Index, "add", WeaverRisk.ReviewRequired);
            json["nodeId"] = add.Ref;
            json["activityVersionId"] = entry.ActivityVersionId;
            json["activityTypeKey"] = entry.ActivityTypeKey;
            json["version"] = entry.Version;
            json["scopePath"] = new JsonArray(add.ScopePath.Select(segment => (JsonNode)JsonValue.Create(segment)!).ToArray());
            json["slot"] = add.Slot;
            json["position"] = add.Position is { } point ? new JsonObject { ["x"] = point.X, ["y"] = point.Y } : null;
            var where = parentId is null ? "as a root candidate" : $"into {ScopePath(parentId)} / {add.Slot}";
            return (Frozen(add.Index, "add", add.Ref, WeaverRisk.ReviewRequired, json, $"Add {entry.DisplayName} ({entry.ActivityTypeKey} {entry.Version}) as '{add.Ref}' {where}.", ["ordinary-edit"]), null);
        }

        (FrozenOperation?, Failure?) SetRoot(SetRootOperation setRoot)
        {
            // One root per batch: a second set-root would replace the root the first one installed.
            if (rootInstalled)
                return (null, Reference(setRoot.Index, "set-root", "a batch installs at most one root."));
            if (setRoot.Activity.Ref is not { } reference || !pendingRoots.Contains(reference))
                return (null, Reference(setRoot.Index, "set-root", "set-root takes the 'ref' of an earlier add with an empty 'scopePath' and 'slot': 'root'. An existing activity is never moved to the root."));
            pendingRoots.Remove(reference);
            rootInstalled = true;
            var replaces = root is not null && !refs.ContainsKey(root);
            if (root is not null)
                foreach (var node in nodes.Values.Where(node => node.Id != reference && !removed.Contains(node.Id)).ToArray())
                    removed.Add(node.Id);
            if (root is not null) connections.Clear();
            root = reference;
            var risk = replaces ? WeaverRisk.Destructive : WeaverRisk.ReviewRequired;
            var json = Op(setRoot.Index, "set-root", risk);
            json["nodeId"] = reference;
            json["replacesRoot"] = replaces;
            return (Frozen(setRoot.Index, "set-root", reference, risk, json,
                replaces ? $"Make '{reference}' the root, replacing the current root and every activity in it." : $"Make '{reference}' the root.",
                replaces ? ["destructive", "replaces-root"] : ["ordinary-edit"]), null);
        }

        (FrozenOperation?, Failure?) PositionOf(PositionOperation position)
        {
            var (node, problem) = Resolve(position.Index, "position", position.Activity);
            if (problem is not null) return (null, problem);
            var json = Op(position.Index, "position", WeaverRisk.ReviewRequired);
            json["nodeId"] = node!.Id;
            json["x"] = position.X;
            json["y"] = position.Y;
            return (Frozen(position.Index, "position", node.Id, WeaverRisk.ReviewRequired, json,
                $"Move {Label(node)} to ({Format(position.X)}, {Format(position.Y)}).", ["ordinary-edit"]), null);
        }

        (FrozenOperation?, Failure?) SetProperty(SetPropertyOperation property)
        {
            var (node, problem) = Resolve(property.Index, "set-property", property.Activity);
            if (problem is not null) return (null, problem);
            if (node!.Entry is not { } entry)
                return (null, Clarify(property.Index, "set-property", $"the activity version '{node.ActivityVersionId}' of {Label(node)} is not in the activity design catalog, so its declared inputs are unknown and no property can be set. Ask the user before changing it."));
            var input = entry.Inputs.FirstOrDefault(item => item.ReferenceKey == property.Property)
                ?? entry.Inputs.FirstOrDefault(item => item.Name == property.Property);
            if (input is null)
                return (null, Clarify(property.Index, "set-property", $"'{entry.DisplayName}' ({entry.ActivityTypeKey} {entry.Version}) declares no input '{property.Property}'. Declared inputs: {InputList(entry)}. Ask the user which input is meant; never guess."));
            JsonObject value;
            string summary;
            if (property.Literal is { } literal)
            {
                if (input.IsCredential)
                {
                    var visible = snapshot.SecretReferences.Take(8).Select(item => $"'{item.Name}'").ToArray();
                    return (null, new Failure(CredentialLiteral, $"Operation {property.Index} (set-property): '{input.Name}' of {Label(node)} is a credential input, so a literal value is refused. Offer to bind a Secret Reference the user can see instead"
                        + (visible.Length > 0 ? $": {string.Join(", ", visible)}." : "; none is visible in this context, so ask the user to choose one with the secret picker.") + NothingChanged, 422));
                }
                value = new JsonObject { ["literal"] = JsonNode.Parse(literal.GetRawText()) };
                summary = $"Set {input.Name} of {Label(node)} to a literal {LiteralKind(literal)}.";
            }
            else
            {
                var secret = property.Secret!;
                var match = snapshot.SecretReferences.FirstOrDefault(item => item.Name == secret.Name
                    && (secret.TypeName is null || item.TypeName == secret.TypeName) && (secret.Scope is null || item.Scope == secret.Scope));
                if (match is null)
                    return (null, Clarify(property.Index, "set-property", $"the Secret Reference '{secret.Name}' is not visible in this context. Ask the user to choose one with the secret picker; no Secret value is ever read or set."));
                value = new JsonObject { ["secretReference"] = new JsonObject { ["name"] = match.Name, ["typeName"] = match.TypeName, ["scope"] = match.Scope } };
                summary = $"Bind {input.Name} of {Label(node)} to the Secret Reference '{match.Name}'.";
            }
            var json = Op(property.Index, "set-property", WeaverRisk.ReviewRequired);
            json["nodeId"] = node.Id;
            json["property"] = input.ReferenceKey;
            json["inputName"] = input.Name;
            json["typeName"] = input.Type;
            json["value"] = value;
            return (Frozen(property.Index, "set-property", node.Id, WeaverRisk.ReviewRequired, json, summary, ["ordinary-edit"]), null);
        }

        (FrozenOperation?, Failure?) Update(UpdateOperation update)
        {
            var (node, problem) = Resolve(update.Index, "update", update.Activity);
            if (problem is not null) return (null, problem);
            string? startInput = null;
            if (update.CanStartWorkflow is not null)
            {
                if (node!.Entry is not { } entry)
                    return (null, Clarify(update.Index, "update", $"the activity version '{node.ActivityVersionId}' of {Label(node)} is not in the activity design catalog, so whether it can start a workflow is unknown."));
                var input = entry.Inputs.FirstOrDefault(item => string.Equals(item.ReferenceKey, "CanStartWorkflow", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Name, "CanStartWorkflow", StringComparison.OrdinalIgnoreCase));
                if (input is null)
                    return (null, Clarify(update.Index, "update", $"'{entry.DisplayName}' declares no CanStartWorkflow input, so it cannot be marked as able to start a workflow."));
                startInput = input.ReferenceKey;
            }
            var fields = new JsonObject();
            if (update.DisplayName is not null) fields["displayName"] = update.DisplayName;
            if (update.Description is not null) fields["description"] = update.Description;
            if (update.CanStartWorkflow is { } canStart) fields["canStartWorkflow"] = canStart;
            var json = Op(update.Index, "update", WeaverRisk.ReviewRequired);
            json["nodeId"] = node!.Id;
            json["fields"] = fields;
            json["startInput"] = startInput;
            var changed = string.Join(", ", fields.Select(field => field.Key));
            return (Frozen(update.Index, "update", node.Id, WeaverRisk.ReviewRequired, json, $"Update {changed} of {Label(node)}.", ["ordinary-edit"]), null);
        }

        (FrozenOperation?, Failure?) Remove(RemoveOperation remove)
        {
            if (remove.Activity.Ref is not null)
                return (null, Reference(remove.Index, "remove", "an activity added in the same batch cannot be removed by it; leave its add out instead."));
            var (node, problem) = Resolve(remove.Index, "remove", remove.Activity);
            if (problem is not null) return (null, problem);
            if (node!.ParentId is null)
                return (null, Reference(remove.Index, "remove", "the root cannot be removed. Add a new root candidate and install it with set-root instead."));
            if (nodes[node.ParentId].Slots?.FirstOrDefault(slot => slot.Id == node.Slot)?.Mode == "bpmn")
                return (null, Reference(remove.Index, "remove", $"{Label(node)} is in a BPMN scope, which an Operation Batch does not change."));
            var subtree = Subtree(node.Id);
            foreach (var id in subtree) removed.Add(id);
            connections.RemoveAll(edge => subtree.Contains(edge.Source) || subtree.Contains(edge.Target));
            var json = Op(remove.Index, "remove", WeaverRisk.Destructive);
            json["nodeId"] = node.Id;
            var nested = subtree.Count - 1;
            return (Frozen(remove.Index, "remove", node.Id, WeaverRisk.Destructive, json,
                $"Remove {Label(node)} from {ScopePath(node.ParentId)}" + (nested > 0 ? $", with the {nested} activit{(nested == 1 ? "y" : "ies")} inside it." : ".") + " Its connections are removed too.",
                ["destructive"]), null);
        }

        (FrozenOperation?, Failure?) Connect(ConnectOperation connect)
        {
            var (source, sourceProblem) = Resolve(connect.Index, "connect", connect.Source);
            if (sourceProblem is not null) return (null, sourceProblem);
            var (target, targetProblem) = Resolve(connect.Index, "connect", connect.Target);
            if (targetProblem is not null) return (null, targetProblem);
            if (source!.Id == target!.Id) return (null, Reference(connect.Index, "connect", "an activity cannot be connected to itself."));
            if (source.ParentId is null || source.ParentId != target.ParentId || source.Slot != target.Slot)
                return (null, Reference(connect.Index, "connect", $"{Label(source)} and {Label(target)} are not in the same flowchart scope ({ScopePath(source.ParentId)} vs {ScopePath(target.ParentId)})."));
            var owner = nodes[source.ParentId];
            if (owner.Added)
                return (null, Reference(connect.Index, "connect", $"'{owner.Id}' is added by this batch, so whether it is a flowchart cannot be confirmed yet; connect its activities in a following batch."));
            if (owner.Slots!.FirstOrDefault(slot => slot.Id == source.Slot)?.Mode != "flowchart")
                return (null, Reference(connect.Index, "connect", $"the slot '{source.Slot}' of {Label(owner)} is not a flowchart, so it has no connections."));
            var port = Port(connect.SourcePort);
            if (connections.Any(edge => edge.Source == source.Id && edge.Target == target.Id && edge.SourcePort == port))
                return (null, Reference(connect.Index, "connect", $"{Label(source)} is already connected to {Label(target)} from '{port}'."));
            connections.Add(new(source.Id, target.Id, port, connect.TargetPort));
            var json = Op(connect.Index, "connect", WeaverRisk.ReviewRequired);
            json["source"] = source.Id;
            json["target"] = target.Id;
            json["sourcePort"] = port;
            json["targetPort"] = connect.TargetPort;
            json["scopeOwnerId"] = owner.Id;
            json["slot"] = source.Slot;
            return (Frozen(connect.Index, "connect", source.Id, WeaverRisk.ReviewRequired, json, $"Connect {Label(source)} ('{port}') to {Label(target)}.", ["ordinary-edit"]), null);
        }

        (FrozenOperation?, Failure?) Disconnect(DisconnectOperation disconnect)
        {
            var (source, sourceProblem) = Resolve(disconnect.Index, "disconnect", disconnect.Source);
            if (sourceProblem is not null) return (null, sourceProblem);
            var (target, targetProblem) = Resolve(disconnect.Index, "disconnect", disconnect.Target);
            if (targetProblem is not null) return (null, targetProblem);
            var matches = connections.Where(edge => edge.Source == source!.Id && edge.Target == target!.Id
                && (disconnect.SourcePort is null || edge.SourcePort == Port(disconnect.SourcePort))).ToArray();
            if (matches.Length == 0)
                return (null, Reference(disconnect.Index, "disconnect", $"{Label(source!)} is not connected to {Label(target!)}" + (disconnect.SourcePort is null ? "." : $" from '{disconnect.SourcePort}'.")));
            if (matches.Length > 1)
                return (null, Clarify(disconnect.Index, "disconnect", $"{Label(source!)} is connected to {Label(target!)} from {matches.Length} ports ({string.Join(", ", matches.Select(edge => $"'{edge.SourcePort}'"))}). Ask which connection is meant."));
            connections.Remove(matches[0]);
            var json = Op(disconnect.Index, "disconnect", WeaverRisk.Destructive);
            json["source"] = source!.Id;
            json["target"] = target!.Id;
            json["sourcePort"] = matches[0].SourcePort;
            return (Frozen(disconnect.Index, "disconnect", source.Id, WeaverRisk.Destructive, json,
                $"Disconnect {Label(source)} ('{matches[0].SourcePort}') from {Label(target)}.", ["destructive"]), null);
        }

        (Node?, Failure?) Resolve(int index, string kind, ActivityReference reference)
        {
            if (reference.Ref is { } tempRef)
            {
                if (!refs.ContainsKey(tempRef))
                    return (null, Reference(index, kind, $"ref '{tempRef}' names no earlier add in this batch. A ref resolves only after the add that creates it."));
                if (removed.Contains(tempRef))
                    return (null, Reference(index, kind, $"ref '{tempRef}' was removed by an earlier operation."));
                if (pendingRoots.Contains(tempRef))
                    return (null, Reference(index, kind, $"ref '{tempRef}' is a root candidate; operate on it only after the set-root that installs it."));
                return (nodes[tempRef], null);
            }
            if (reference.Id is { } id)
            {
                if (refs.ContainsKey(id))
                    return (null, Reference(index, kind, $"'{id}' is a ref of this batch; address it as {{\"ref\": \"{id}\"}}."));
                if (removed.Contains(id))
                    return (null, Reference(index, kind, $"'{id}' was removed by an earlier operation of this batch."));
                return nodes.TryGetValue(id, out var node)
                    ? (node, null)
                    : (null, Reference(index, kind, $"no activity has the id '{id}' in the working draft."));
            }
            var name = reference.Name!;
            var candidates = nodes.Values.Where(node => !node.Added && !removed.Contains(node.Id)
                && string.Equals(node.DisplayName.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 1) return (candidates[0], null);
            if (candidates.Length == 0)
                return (null, Clarify(index, kind, $"no activity is named '{name}'. Ask the user which activity is meant."));
            return (null, Clarify(index, kind, $"'{name}' matches {candidates.Length} activities: " +
                string.Join("; ", candidates.Take(12).Select(candidate => $"'{candidate.Id}' at {ScopePath(candidate.Id)}")) +
                ". Ask the user which one is meant, then address it by id."));
        }

        (Node?, Failure?) ResolveScope(AddOperation add)
        {
            Node? previous = null;
            for (var position = 0; position < add.ScopePath.Count; position++)
            {
                var segment = add.ScopePath[position];
                var reference = refs.ContainsKey(segment) ? new ActivityReference(null, segment, null) : new ActivityReference(segment, null, null);
                var (node, problem) = Resolve(add.Index, "add", reference);
                if (problem is not null) return (null, problem);
                if (position == 0 && node!.Id != root)
                    return (null, Reference(add.Index, "add", $"'scopePath' must start at the root '{root ?? "(none)"}'; '{segment}' is not the root."));
                if (previous is not null && node!.ParentId != previous.Id)
                    return (null, Reference(add.Index, "add", $"'scopePath' is not a containment chain: '{segment}' is not inside '{previous.Id}'."));
                previous = node;
            }
            return (previous, null);
        }

        HashSet<string> Subtree(string id)
        {
            var result = new HashSet<string>(StringComparer.Ordinal) { id };
            bool grew;
            do
            {
                grew = false;
                foreach (var node in nodes.Values)
                    if (node.ParentId is not null && result.Contains(node.ParentId) && result.Add(node.Id)) grew = true;
            } while (grew);
            return result;
        }

        string ScopePath(string? id)
        {
            if (id is null) return "the root scope";
            var chain = new List<string>();
            for (var current = nodes.GetValueOrDefault(id); current is not null && chain.Count <= nodes.Count; current = current.ParentId is null ? null : nodes.GetValueOrDefault(current.ParentId))
                chain.Add(Label(current));
            chain.Reverse();
            return string.Join(" / ", chain);
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    private static FrozenOperation Frozen(int index, string kind, string? nodeId, WeaverRisk risk, JsonObject json, string summary, IReadOnlyList<string> reasons)
        => new(index, kind, nodeId, risk, json, summary, reasons);

    private static JsonObject Op(int index, string kind, WeaverRisk risk) => new() { ["index"] = index, ["kind"] = kind, ["risk"] = RiskName(risk) };

    internal static string RiskName(WeaverRisk risk) => risk switch
    {
        WeaverRisk.ReadOnly => "readOnly",
        WeaverRisk.ReviewRequired => "reviewRequired",
        WeaverRisk.Destructive => "destructive",
        _ => "admin"
    };

    private static string Port(string? port) => string.IsNullOrEmpty(port) ? "Done" : port;
    private static string Label(Node node) => $"'{Clip(node.DisplayName, 80)}' [{node.Id}]";
    private static string SlotList(Node node) => node.Slots is { Count: > 0 } slots ? string.Join(", ", slots.Select(slot => $"'{slot.Id}' ({slot.Mode}, {slot.Cardinality})")) : "none";
    private static string InputList(ActivityAuthoringCatalogEntry entry)
        => entry.Inputs.Count == 0 ? "none" : string.Join(", ", entry.Inputs.Take(24).Select(input => $"'{input.ReferenceKey}'" + (input.IsCredential ? " (credential)" : "")));
    private static string LiteralKind(JsonElement literal) => literal.ValueKind switch
    {
        JsonValueKind.String => "text value",
        JsonValueKind.Number => "number",
        _ => "boolean"
    };
    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Clip(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";

    private static Failure Schema(int? index, string? kind, string problem)
        => new(SchemaInvalid, (index is null ? "" : $"Operation {index}" + (kind is null ? "" : $" ({Clip(kind, 32)})") + ": ") + problem + NothingChanged, 400);
    private static Failure Reference(int index, string kind, string problem)
        => new(ReferenceInvalid, $"Operation {index} ({kind}): {problem}" + NothingChanged, 422);
    private static Failure Clarify(int index, string kind, string problem)
        => new(ClarificationRequired, $"Clarification needed for operation {index} ({kind}): {problem}" + NothingChanged, 422);

    private static string BuildSchema()
    {
        JsonObject Activity(string description) => new()
        {
            ["type"] = "object", ["additionalProperties"] = false, ["minProperties"] = 1, ["maxProperties"] = 1, ["description"] = description,
            ["properties"] = new JsonObject
            {
                ["id"] = new JsonObject { ["type"] = "string", ["description"] = "The node id of an existing activity in the working draft." },
                ["ref"] = new JsonObject { ["type"] = "string", ["description"] = "The ref of an activity added earlier in this batch." },
                ["name"] = new JsonObject { ["type"] = "string", ["description"] = "An existing activity's display name. An ambiguous name is a clarification, never a guess." }
            }
        };
        JsonObject Kind(string kind) => new() { ["type"] = "string", ["const"] = kind };
        JsonObject Number() => new() { ["type"] = "number" };
        JsonObject Text(string? description = null) => description is null
            ? new JsonObject { ["type"] = "string" } : new JsonObject { ["type"] = "string", ["description"] = description };
        JsonObject OperationSchema(string kind, string description, JsonObject properties, params string[] required)
        {
            properties["kind"] = Kind(kind);
            return new()
            {
                ["type"] = "object", ["additionalProperties"] = false, ["description"] = description, ["properties"] = properties,
                ["required"] = new JsonArray(new[] { "kind" }.Concat(required).Select(name => (JsonNode)JsonValue.Create(name)!).ToArray())
            };
        }
        var operations = new JsonArray(
            OperationSchema("add", "Add a new activity from the authorized catalog into an explicit scope and slot. Its 'ref' becomes its node id.", new JsonObject
            {
                ["ref"] = Text("A new lowercase id, unique and never an existing or removed node id (pattern ^[a-z][a-z0-9-]{0,62}$)."),
                ["activityTypeKey"] = Text("The catalog entry's activityTypeKey."),
                ["version"] = Text("The catalog entry's exact version."),
                ["scopePath"] = new JsonObject { ["type"] = "array", ["items"] = Text(), ["maxItems"] = 32,
                    ["description"] = "Node ids (or earlier refs) from the root to the container, inclusive. Empty only for a root candidate." },
                ["slot"] = Text("The container's slot id, as disclosed in its 'slots'; 'root' for a root candidate."),
                ["position"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("x", "y"),
                    ["properties"] = new JsonObject { ["x"] = Number(), ["y"] = Number() } }
            }, "ref", "activityTypeKey", "version", "scopePath", "slot"),
            OperationSchema("set-root", "Install a root candidate added in this batch as the root. Replacing an existing root removes everything in it.",
                new JsonObject { ["activity"] = Activity("The ref of the root candidate.") }, "activity"),
            OperationSchema("position", "Set an activity's designer position.", new JsonObject { ["activity"] = Activity("The activity to move."), ["x"] = Number(), ["y"] = Number() },
                "activity", "x", "y"),
            OperationSchema("set-property", "Set one declared input by its explicit name or reference key, to a literal or to a visible Secret Reference.", new JsonObject
            {
                ["activity"] = Activity("The activity whose input is set."),
                ["property"] = Text("The declared input's referenceKey or name. There is no default property."),
                ["value"] = new JsonObject
                {
                    ["type"] = "object", ["additionalProperties"] = false, ["minProperties"] = 1, ["maxProperties"] = 1,
                    ["properties"] = new JsonObject
                    {
                        ["literal"] = new JsonObject { ["type"] = new JsonArray("string", "number", "boolean") },
                        ["secretReference"] = new JsonObject
                        {
                            ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("name"),
                            ["properties"] = new JsonObject { ["name"] = Text(), ["typeName"] = Text(), ["scope"] = Text() }
                        }
                    }
                }
            }, "activity", "property", "value"),
            OperationSchema("update", "Update allowlisted activity fields only.", new JsonObject
            {
                ["activity"] = Activity("The activity to update."),
                ["fields"] = new JsonObject
                {
                    ["type"] = "object", ["additionalProperties"] = false, ["minProperties"] = 1,
                    ["properties"] = new JsonObject { ["displayName"] = Text(), ["description"] = Text(), ["canStartWorkflow"] = new JsonObject { ["type"] = "boolean" } }
                }
            }, "activity", "fields"),
            OperationSchema("remove", "Remove an existing activity and everything inside it. Destructive.", new JsonObject { ["activity"] = Activity("The activity to remove.") }, "activity"),
            OperationSchema("connect", "Connect two activities of the same flowchart scope.", new JsonObject
            {
                ["source"] = Activity("The source activity."), ["target"] = Activity("The target activity."),
                ["sourcePort"] = Text("The source outcome; 'Done' when omitted."), ["targetPort"] = Text()
            }, "source", "target"),
            OperationSchema("disconnect", "Remove one existing connection. Destructive.", new JsonObject
            {
                ["source"] = Activity("The source activity."), ["target"] = Activity("The target activity."), ["sourcePort"] = Text("Needed when several ports connect the pair.")
            }, "source", "target"));
        var schema = new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("operations"),
            ["properties"] = new JsonObject
            {
                ["operations"] = new JsonObject
                {
                    ["type"] = "array", ["minItems"] = 1, ["maxItems"] = MaxOperations, ["items"] = new JsonObject { ["anyOf"] = operations },
                    ["description"] = $"Applied in order, all together or not at all. Above {ComplexityLimit} operations the batch is labelled high-complexity."
                }
            }
        };
        return schema.ToJsonString();
    }
}
