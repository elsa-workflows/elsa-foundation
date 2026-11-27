using System.Text;
using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Elsa3.Mapping.Conversion;
using Elsa3.Models;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// BulkDispatchWorkflows (spec 1242 plan §3). The Elsa 4 activity holds one authored DispatchWorkflow in its
/// <c>Dispatch</c> slot; this rewrite generates it, with id <c>{elsa3NodeId}.dispatch</c>, from the Elsa 3 dispatch
/// members, and adds the <c>ChildCompleted</c> and <c>ChildFaulted</c> branches as slot children. The generated node is a
/// ready-made plan, so it is not normalized through the Elsa 3 DispatchWorkflow entry: that entry's member rules are
/// mirrored here (FR-011), except the wait, which defaults to true (FR-013).
/// </summary>
internal sealed class Elsa3BulkDispatchChildRewrite : IElsa3NodeRewrite
{
    private const string DispatchWorkflowTypeKey = "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow";
    private const string DispatchSlot = "BulkDispatchWorkflows.Dispatch";
    private const string ChildCompletedSlot = "BulkDispatchWorkflows.ChildCompleted";
    private const string ChildFaultedSlot = "BulkDispatchWorkflows.ChildFaulted";
    private const string CurrentItem = "currentItem";
    private const string DefaultItemKey = "Item";
    private const string ItemParameter = "item";

    private static readonly JsonElement JsonNull = JsonSerializer.SerializeToElement<object?>(null, JsonBaseline.Options);

    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.BulkDispatchChild;

    public async ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        var bulk = context.Activity;
        var bulkId = Elsa3ConversionDiagnostics.NodeId(bulk);

        CheckItems(context);
        CheckRemoved(context, "StartNewTrace", "false");
        CheckRemoved(context, "ChannelName", null);
        var targetId = TargetId(context);
        var wait = Wait(context, out var waitValue);
        var itemKey = ItemKey(context);
        var declared = targetId is null ? null : Declared(context, targetId);
        var fixedInputs = StaticInputs(context, targetId, declared);

        var resolution = await context.Resolver.ResolveCatalogTypeKeyAsync(DispatchWorkflowTypeKey, cancellationToken);
        if (resolution.Failure is { } failure)
        {
            context.Error(failure.Code, failure.Message, failure.Guidance);
            return;
        }

        var dispatchType = resolution.Activity!;
        var childId = $"{bulkId}.dispatch";
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var sourceNames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (targetId is not null && context.TryGetProperty("WorkflowDefinitionId", out var targetProperty, out var target))
            Set("WorkflowDefinitionId", targetProperty, target);
        if (context.TryGetProperty("CorrelationIdFunction", out var correlationProperty, out var correlation) &&
            Normalize(correlation) is { ValueKind: not JsonValueKind.Null } correlationValue && Elsa3TypedValues.IsBound(correlationValue))
            Set("CorrelationId", correlationProperty, correlationValue);
        if (waitValue is { } waitElement)
            Set("WaitForCompletion", Elsa3ConversionDiagnostics.JsonName("WaitForCompletion"), waitElement);
        Set("CancelChildOnParentCancellation", "cancelChildOnParentCancellation", Elsa3TypedValues.CreateLiteral("Boolean", "false"));
        if (declared is not null && fixedInputs is not null && itemKey is not null &&
            dispatchType.FindInput("Inputs") is { } inputsInput)
        {
            context.TryGetProperty("Input", out var inputProperty, out _);
            Set("Inputs", inputProperty, InputsExpression(bulkId, declared, itemKey, fixedInputs, inputsInput.Type));
        }

        var child = new Elsa3Activity
        {
            Id = childId,
            NodeId = childId,
            Name = childId,
            Type = "Elsa.DispatchWorkflow",
            AdditionalProperties = properties
        };
        var childPlan = new Elsa3NodePlan(child, context.Node.JsonPath, bulk) { Resolved = dispatchType };
        foreach (var (key, source) in sourceNames)
            childPlan.SourceNames[key] = source;
        context.Plan.Nodes[child] = childPlan;
        bulk.Activities ??= [];
        bulk.Activities.Insert(0, child);
        context.Node.Slots.Insert(0, new Elsa3SlotChild(DispatchSlot, null, child));

        var branches = new List<Elsa3Activity>();
        AddBranch(context, "ChildCompleted", ChildCompletedSlot, wait, branches);
        AddBranch(context, "ChildFaulted", ChildFaultedSlot, wait, branches);

        context.Plan.BulkDispatches[bulk] = new Elsa3BulkDispatchPlan(bulk, bulkId, child, itemKey ?? DefaultItemKey, wait ?? Elsa3BulkDispatchWait.Expression, branches);
        context.Warning(BulkDispatchItemInputs,
            "Elsa 4 passes each child only the inputs its workflow declares: map items by declared name (in any letter case), any other item under the item key, and a child-declared 'ParentInstanceId' is no longer filled. " +
            "'ChildFaulted' now also runs for cancelled children and children that could not be dispatched. Without a correlation function a child now inherits the parent's correlation id, where Elsa 3 gave it none. " +
            "An expression 'WaitForCompletion' is now evaluated once per item on the Dispatch node, where Elsa 3 evaluated it once.",
            "Check that each child workflow declares the inputs it reads, that a 'ChildFaulted' branch handles cancelled children, and that the child's correlation id is what you expect.");

        void Set(string key, string sourceName, JsonElement value)
        {
            properties[key] = value;
            sourceNames[key] = sourceName;
        }
    }

    // FR-014: Elsa 3 read a non-null literal as raw JSON text, which is not a collection, so it dispatched no child.
    private static void CheckItems(Elsa3RewriteContext context)
    {
        if (!context.Activity.AdditionalProperties.TryGetValue("Items", out var items) ||
            Elsa3TypedValues.IsNullLiteral(items) || !Elsa3TypedValues.TryGetLiteral(items, out _))
            return;

        context.Error(ValueOutsideContract,
            "Input 'items' is a literal. Elsa 3 read a literal as raw JSON text, which is not a collection, so it dispatched no child.",
            "Bind 'items' to an expression that returns the collection, such as JavaScript or a variable, before converting.",
            context.Plan.SourceName(context.Activity, "Items"));
    }

    // FR-012: removed members mirror the Elsa 3 DispatchWorkflow entry (Removed), whose check the normalizer owns.
    private static void CheckRemoved(Elsa3RewriteContext context, string member, string? elsa3Default)
    {
        if (!context.TryGetProperty(member, out var property, out var value))
            return;
        var rule = new Elsa3MemberRule(member, Elsa3MemberKind.Input, Elsa3MemberRuleKind.RemovedInput, null,
            JsonSerializer.SerializeToElement(elsa3Default, JsonBaseline.Options));
        Elsa3ActivityNormalizer.CheckRemoved(context, rule, property, Normalize(value));
    }

    // FR-012: the target is pinned when the parent publishes, so it must be a literal.
    private static string? TargetId(Elsa3RewriteContext context)
    {
        var present = context.TryGetProperty("WorkflowDefinitionId", out var property, out var value);
        value = present ? Normalize(value) : JsonNull;
        if (Elsa3TypedValues.TryGetLiteral(value, out var literal) && !string.IsNullOrWhiteSpace(literal))
            return literal.Trim();

        if (Elsa3TypedValues.IsBound(value))
            context.Error(ValueOutsideContract,
                $"Input '{property}' is bound to an expression. Elsa 4 pins the workflow a bulk dispatch starts when the parent is published, so it must be a literal.",
                $"Set '{property}' to the literal id of the workflow to start before converting.",
                property);
        else
            context.Error(RequiredInputUnbound,
                $"Input '{property}' names no workflow to start; Elsa 3 rejects the activity without it.",
                $"Set '{property}' to the literal id of the workflow to start before converting.",
                property);
        return null;
    }

    /// <summary>
    /// FR-013: the wait goes to the Dispatch node. Missing is Elsa 3's default, true; null or a valueless literal reads as
    /// false; a literal is parsed in any letter case; an expression is carried. Null when the value is refused.
    /// </summary>
    private static Elsa3BulkDispatchWait? Wait(Elsa3RewriteContext context, out JsonElement? value)
    {
        if (!context.TryGetProperty("WaitForCompletion", out var property, out var raw))
        {
            value = Elsa3TypedValues.CreateLiteral("Boolean", "true");
            return Elsa3BulkDispatchWait.Missing;
        }

        raw = Normalize(raw);
        if (raw.ValueKind == JsonValueKind.Null || !Elsa3TypedValues.IsBound(raw) && !Elsa3TypedValues.IsCaptured(raw))
        {
            value = Elsa3TypedValues.CreateLiteral("Boolean", "false");
            return Elsa3BulkDispatchWait.LiteralFalse;
        }

        if (Elsa3TypedValues.TryGetLiteral(raw, out var literal))
        {
            if (bool.TryParse(literal?.Trim(), out var parsed))
            {
                value = Elsa3TypedValues.CreateLiteral("Boolean", parsed ? "true" : "false");
                return parsed ? Elsa3BulkDispatchWait.LiteralTrue : Elsa3BulkDispatchWait.LiteralFalse;
            }

            value = null;
            context.Error(ValueOutsideContract,
                $"Input '{property}' is the literal '{literal}', which is not true or false.",
                $"Set '{property}' to true or false before converting.",
                property);
            return null;
        }

        value = raw;
        return Elsa3BulkDispatchWait.Expression;
    }

    /// <summary>FR-016: <c>DefaultItemInputKey</c> is a literal, or null or absent for <c>Item</c>. Null when refused.</summary>
    private static string? ItemKey(Elsa3RewriteContext context)
    {
        if (!context.TryGetProperty("DefaultItemInputKey", out var property, out var raw))
            return DefaultItemKey;

        raw = Normalize(raw);
        var isLiteral = Elsa3TypedValues.TryGetLiteral(raw, out var literal);
        if (raw.ValueKind == JsonValueKind.Null || isLiteral && literal is null || !isLiteral && !Elsa3TypedValues.IsBound(raw))
            return DefaultItemKey;
        if (isLiteral && !string.IsNullOrWhiteSpace(literal))
            return literal;

        context.Error(ValueOutsideContract,
            isLiteral
                ? $"Input '{property}' is blank, which names no input to pass the item under."
                : $"Input '{property}' is bound to an expression; the conversion builds the per-item inputs from a literal key.",
            $"Set '{property}' to a literal input name, or clear it for 'Item', before converting. To pass values under other names, put them in the items as members, or use a literal 'input' map.",
            property);
        return null;
    }

    /// <summary>FR-016, Q2: the target's declared inputs, from this import or from Elsa 4. Null (refused) when it is in neither.</summary>
    private static IReadOnlyList<string>? Declared(Elsa3RewriteContext context, string targetId)
    {
        if (context.Plan.Request.BulkDispatchTargetInputs.TryGetValue(targetId, out var declared))
            return declared;

        context.Error(BulkDispatchTargetUnknown,
            $"Bulk dispatch starts workflow '{targetId}', which is neither in this import nor published in Elsa 4, so the conversion cannot tell which inputs it declares.",
            "Import the target workflow first, or in the same run.",
            Elsa3ConversionDiagnostics.JsonName("WorkflowDefinitionId"));
        return null;
    }

    /// <summary>
    /// FR-016: a literal or Object <c>Input</c> map, checked by the #5808 rules and spelled as the target declares each
    /// name. Empty when unset; null when refused. An expression map is refused: scripts are lowered after this rewrite.
    /// </summary>
    private static List<(string Name, JsonElement Value)>? StaticInputs(Elsa3RewriteContext context, string? targetId, IReadOnlyList<string>? declared)
    {
        if (!context.TryGetProperty("Input", out var property, out var raw))
            return [];
        raw = Normalize(raw);
        if (raw.ValueKind == JsonValueKind.Null || !Elsa3TypedValues.IsBound(raw) && !Elsa3TypedValues.IsCaptured(raw))
            return [];

        string? json;
        var isStatic = Elsa3TypedValues.TryGetLiteral(raw, out json) || Elsa3ActivityNormalizer.TryGetObjectExpression(raw, out json);
        if (!isStatic || !Elsa3ActivityNormalizer.TryReadObject(json, out var entries))
        {
            context.Error(ValueOutsideContract,
                isStatic
                    ? $"Input '{property}' is not a JSON object of input names and values."
                    : $"Input '{property}' is bound to an expression; the conversion builds the per-item inputs before it converts scripts, so it cannot merge one in.",
                "Put those values in the items as members, or use a literal map, before converting.",
                property);
            return null;
        }

        if (declared is null)
            return entries;

        var result = new List<(string Name, JsonElement Value)>(entries.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var failed = false;
        foreach (var (key, value) in entries)
        {
            var matches = declared.Contains(key, StringComparer.Ordinal)
                ? [key]
                : declared.Where(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).ToArray();
            if (matches.Length == 0)
            {
                context.Error(DispatchInputUndeclared,
                    $"Input '{property}' passes '{key}', which workflow '{targetId}' does not declare. Elsa 3 ignored it; Elsa 4 refuses the dispatch.",
                    $"Remove '{key}' from the map, or declare it as an input of '{targetId}'. It declares {Elsa3ActivityNormalizer.Describe(declared)}.",
                    property, key);
                failed = true;
                continue;
            }

            if (matches.Length > 1 || !used.Add(matches[0]))
            {
                context.Error(LiteralKeyAmbiguous,
                    matches.Length > 1
                        ? $"Input '{property}' passes '{key}', which matches more than one input of workflow '{targetId}' in another letter case ({Elsa3ActivityNormalizer.Describe(matches)})."
                        : $"Input '{property}' passes input '{matches[0]}' of workflow '{targetId}' more than once.",
                    "Write each input once, spelled exactly as the target workflow declares it.",
                    property, key);
                failed = true;
                continue;
            }

            if (!StringComparer.Ordinal.Equals(matches[0], key))
                context.Info(LiteralKeyRewritten,
                    $"Input '{property}' key '{key}' was spelled as workflow '{targetId}' declares it, '{matches[0]}'; Elsa 3 matched input names in any letter case.",
                    "Spell input names as the target workflow declares them in new workflows.",
                    property, key);
            result.Add((matches[0], value));
        }

        return failed ? null : result;
    }

    /// <summary>
    /// FR-018: a branch runs only for an awaited child. With a literal-false wait Elsa 3 never ran it, so it is dropped.
    /// </summary>
    private static void AddBranch(Elsa3RewriteContext context, string member, string slot, Elsa3BulkDispatchWait? wait, List<Elsa3Activity> branches)
    {
        if (!context.TryGetProperty(member, out var property, out var value) || value.ValueKind != JsonValueKind.Object)
            return;

        if (wait == Elsa3BulkDispatchWait.LiteralFalse)
        {
            context.Warning(DeadConnectionRemoved,
                $"Branch '{property}' runs only for awaited children, and this bulk dispatch never waits, so Elsa 3 never ran it; it is removed.",
                "Remove the branch from the Elsa 3 workflow, or set 'waitForCompletion' to true.",
                property);
            return;
        }

        if (context.AddChild(slot, null, value, $"{context.Node.JsonPath}{Elsa3ConversionDiagnostics.FormatProperty(property)}") is { } branch)
            branches.Add(branch);
    }

    /// <summary>
    /// FR-016: the Dispatch node's per-item <c>Inputs</c>, an Elsa 4 expression bound to the bulk node's
    /// <c>currentItem</c>. In order: the static entries; then a map item's members that the target declares, matched in any
    /// letter case and passed in the declared spelling, or any other item under the item key when the target declares it.
    /// The item wins a clash. Declared names appear as literals, and item members are read only through
    /// <c>Object.entries</c>, never by computed member access, which the read analysis treats as possible state access.
    /// </summary>
    private static JsonElement InputsExpression(
        string bulkId,
        IReadOnlyList<string> declared,
        string itemKey,
        IReadOnlyList<(string Name, JsonElement Value)> fixedInputs,
        Elsa.Primitives.Models.TypeReference inputsType)
    {
        var keyMatches = declared.Where(name => name.Equals(itemKey, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).ToArray();
        var key = keyMatches.Length >= 1 ? keyMatches[0] : null;
        var source = new StringBuilder()
            .Append("(function (item) { var picked = []; ")
            .Append("if (item !== null && typeof item === 'object' && !Array.isArray(item)) { var members = Object.entries(item); ")
            .Append("picked = ").Append(Json(declared)).Append(".map(function (name) { var lower = name.toLowerCase(); ")
            .Append("return [name, members.filter(function (member) { return member[0].toLowerCase() === lower; })]; })")
            .Append(".filter(function (match) { return match[1].length > 0; })")
            .Append(".map(function (match) { if (match[1].length > 1) { throw new Error(\"The item has members '\" + ")
            .Append("match[1].map(function (member) { return member[0]; }).join(\"', '\") + ")
            .Append("\"', which differ only in letter case and all match input '\" + match[0] + \"'. Keep one of them.\"); } ")
            .Append("return [match[0], match[1][0][1]]; }); } ")
            .Append(key is null ? string.Empty : $"else {{ picked = [[{Json(key)}, item]]; }} ")
            .Append("return Object.assign({}, ").Append(FixedObject(fixedInputs)).Append(", Object.fromEntries(picked)); })")
            .Append($"(args.{ItemParameter})")
            .ToString();
        var expression = new ExpressionDefinition(
            "JavaScript",
            source,
            inputsType,
            new Dictionary<string, ExpressionParameterBinding>(StringComparer.Ordinal)
            {
                [ItemParameter] = new VariableExpressionParameterBinding(bulkId, CurrentItem)
            },
            JsonSerializer.SerializeToElement(new { }, JsonBaseline.Options),
            ExpressionCapabilityProfiles.BindingReadonlyV1);
        return JsonSerializer.SerializeToElement(new
        {
            typeName = "Object",
            expression = new { type = "JavaScript", value = expression }
        }, JsonBaseline.Options);
    }

    private static string FixedObject(IReadOnlyList<(string Name, JsonElement Value)> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JsonBaseline.Options.Encoder }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in entries)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonBaseline.Options);

    // Spec 267 (research R-5): a literal holding null converts as a JSON null does.
    private static JsonElement Normalize(JsonElement value) => Elsa3TypedValues.IsNullLiteral(value) ? JsonNull : value;
}
