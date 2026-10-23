using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Resolution;
using Elsa3.Models;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Normalizes a private clone of an Elsa 3 definition node by node: resolves each node, applies the member rules of
/// its map entry (R1–R9, W), runs the entry's rewrite strategy, pins intrinsic contracts, checks required inputs and
/// routes port children into slots. Properties are renamed to their exact Elsa 4 keys, so value-flow lowering and
/// mapping see Elsa 4 member names (research R-8). Every cause is a diagnostic; nothing throws for input.
/// </summary>
internal sealed class Elsa3ActivityNormalizer(
    Elsa3ActivityResolver resolver,
    IEnumerable<IElsa3NodeRewrite> rewrites,
    IWellKnownTypeRegistry wellKnownTypeRegistry,
    IObjectConverter objectConverter)
{
    /// <summary>
    /// The deepest JSON level an activity node may sit at. The state is limited to 64 JSON levels (System.Text.Json's
    /// default); a node's own arguments need up to six more levels. The root node sits at level 2, and each container
    /// adds the levels its native payload needs (<see cref="ContainerLevels"/>).
    /// </summary>
    internal const int MaxNodeJsonLevel = 58;

    private const int RootJsonLevel = 2;

    /// <summary>The deepest an activity may sit below the root in nested Sequence containers (4 levels each): 14.</summary>
    internal const int MaxSequenceNestingDepth = (MaxNodeJsonLevel - RootJsonLevel) / 4;
    private const string ParallelTypeKey = "Elsa.Activities.Parallel.Activities.Parallel";

    private static readonly JsonSerializerOptions Elsa3JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement JsonNull = JsonSerializer.SerializeToElement<object?>(null);
    private readonly IReadOnlyDictionary<Elsa3NodeRewriteKind, IElsa3NodeRewrite> _rewrites =
        rewrites.ToDictionary(rewrite => rewrite.Kind);

    public async ValueTask<Elsa3ConversionPlan> NormalizeAsync(
        Elsa3ConversionRequest request,
        Elsa3WorkflowDefinition clone,
        CancellationToken cancellationToken)
    {
        var plan = new Elsa3ConversionPlan(request, clone);
        if (clone.Root is null)
            return plan;

        CheckWorkflowVariables(plan);

        var pending = new Dictionary<Elsa3Activity, string>(ReferenceEqualityComparer.Instance);
        await VisitAsync(plan, clone.Root, "$.root", null, RootJsonLevel, pending, cancellationToken);

        // Graph conversion needs every child's Elsa 4 outcomes, so it runs once the tree is normalized, innermost
        // flowchart first (spec 239 R-1). Plans are added in visiting order, so reversing it puts children first.
        foreach (var flowchart in plan.Nodes.Values.Reverse().Where(Elsa3FlowchartGraphBuilder.IsFlowchart).ToArray())
            Elsa3FlowchartGraphBuilder.Build(plan, flowchart);
        return plan;
    }

    /// <summary>
    /// A workflow variable's starting value converts by Elsa 3's rule or is refused here, before mapping, so a converted
    /// definition never carries a starting value that fails at publish (spec 264 D-4).
    /// </summary>
    private void CheckWorkflowVariables(Elsa3ConversionPlan plan)
    {
        var variables = plan.Definition.Variables;
        for (var index = 0; index < variables.Count; index++)
        {
            if (Elsa3VariableMapper.TryMap(wellKnownTypeRegistry, objectConverter, variables[index], out _, out var refusal))
                continue;
            plan.Diagnostics.Add(Elsa3ConversionDiagnostics.ForDefinition(
                plan,
                Elsa3MigrationDiagnosticSeverity.Error,
                VariableValueUnconvertible,
                refusal!.Message,
                refusal.Guidance,
                $"$.variables[{index}]",
                new Dictionary<string, string> { ["VariableName"] = variables[index].Name }));
        }
    }

    /// <summary>
    /// A FlowFork, FlowJoin or Start: graph conversion consumes it, so its setting (fork branches, join mode) is kept
    /// before structure members are removed. Outside a flowchart Elsa 3 gives it no behaviour, so it is refused.
    /// </summary>
    private static void ReadGraphConstruct(Elsa3RewriteContext context)
    {
        if (context.Node.Resolved?.Entry?.Target is not Elsa3ActivityTarget.GraphConstruct construct)
            return;

        context.Node.GraphConstruct = construct.Kind;
        var setting = construct.Kind switch
        {
            Elsa3GraphConstructKind.Fork => "Branches",
            Elsa3GraphConstructKind.Join => "Mode",
            _ => null
        };
        if (setting is not null && context.TryGetProperty(setting, out _, out var value))
            context.Node.GraphConstructSetting = value.Clone();

        if (context.Node.Parent is not { } parent || !Elsa3FlowchartGraphBuilder.IsFlowchart(context.Plan.Find(parent)))
            context.Error(UnsupportedMode,
                $"Elsa 3 '{context.Activity.Type}' only has meaning inside a Flowchart, where it becomes connections or the start node.",
                "Remove it, or move it into a Flowchart, before converting.");
    }

    /// <summary>Connections belong to a flowchart; anywhere else Elsa 3 never follows them (FR-012).</summary>
    private static void CheckConnectionsOwner(Elsa3RewriteContext context)
    {
        if (context.Activity.Connections is not { Count: > 0 } || Elsa3FlowchartGraphBuilder.IsFlowchart(context.Node))
            return;

        context.Error(GraphConnectionInvalid,
            $"Elsa 3 activity '{context.Activity.Type}' has connections, but only a Flowchart routes connections.",
            "Remove the connections, or make the container a Flowchart, before converting.",
            "connections");
    }

    private async ValueTask VisitAsync(
        Elsa3ConversionPlan plan,
        Elsa3Activity node,
        string jsonPath,
        Elsa3Activity? parent,
        int level,
        Dictionary<Elsa3Activity, string> childPaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A node a rewrite synthesized (for example an HttpEndpoint projection) arrives with its plan already made.
        if (plan.Nodes.ContainsKey(node))
            return;

        var nodePlan = new Elsa3NodePlan(node, jsonPath, parent);
        plan.Nodes[node] = nodePlan;
        node.AdditionalProperties ??= new Dictionary<string, JsonElement>();

        if (level > MaxNodeJsonLevel)
        {
            Error(plan, nodePlan, NestingTooDeep,
                $"Elsa 3 activity '{Elsa3ConversionDiagnostics.NodeId(node)}' would sit at JSON level {level} of the converted workflow; the most is {MaxNodeJsonLevel}. " +
                "Each Sequence or Flowchart adds 4 levels, each If or loop 3, each Parallel or Switch 5 " +
                $"(at most {MaxSequenceNestingDepth} nested Sequences, 18 Ifs or loops, 11 Parallels or Switches).",
                "Flatten the workflow: move part of the nested structure into a separate workflow.");
            return;
        }

        var resolution = await resolver.ResolveAsync(node, plan.Request.Replacements, cancellationToken);
        var descend = true;
        if (resolution.Failure is { } failure)
        {
            // An unresolved node has no structure to route children into; its own error stands for them.
            Error(plan, nodePlan, failure.Code, failure.Message, failure.Guidance);
            descend = false;
        }
        else
        {
            nodePlan.Resolved = resolution.Activity;
            var context = new Elsa3RewriteContext(plan, nodePlan, resolver, ResolveType, (json, path) => ReadChild(plan, nodePlan, json, path, childPaths));
            ApplyMembers(context);
            ReadGraphConstruct(context);
            CheckConnectionsOwner(context);
            ReadContainerVariables(context);
            if (nodePlan.Resolved!.Entry?.Rewrite is { } kind && _rewrites.TryGetValue(kind, out var rewrite))
                await rewrite.ApplyAsync(context, cancellationToken);
            RemoveConsumedStructure(context);
            PinIntrinsic(context);
            CheckRequiredInputs(context);
            if (nodePlan.Resolved?.Structure is null && nodePlan.Slots.Any(slot => slot.Child is not null))
                context.Error(UnknownMember,
                    $"Elsa 3 activity '{node.Type}' has child activities, but Elsa 4 activity '{nodePlan.Resolved?.ActivityTypeKey ?? nodePlan.Resolved?.ActivityVersionId}' declares no structure to hold them.",
                    "Install the Elsa 4 module that provides the container, or restructure the workflow.",
                    "activities");
        }

        if (!descend)
            return;

        var children = node.Activities ?? [];
        var childLevel = level + ContainerLevels(nodePlan);
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var childPath = childPaths.GetValueOrDefault(child) ?? $"{jsonPath}.activities[{index}]";
            await VisitAsync(plan, child, childPath, node, childLevel, childPaths, cancellationToken);
        }
    }

    /// <summary>The JSON levels a container's native payload puts between it and its children.</summary>
    private static int ContainerLevels(Elsa3NodePlan node)
    {
        var slots = node.Resolved?.Structure?.Payload.Slots;
        if (slots is null || slots.Count == 0)
            return 4; // the generic activities-list shape, as Sequence and Flowchart store it
        return slots.Max(slot =>
            slot.CollectionProperty is not null ? 5 // structure > payload > items > [i] > child
            : string.Equals(slot.Cardinality, "Many", StringComparison.OrdinalIgnoreCase) ? 4 // structure > payload > list > [i]
            : 3); // structure > payload > child
    }

    private void ApplyMembers(Elsa3RewriteContext context)
    {
        var node = context.Node;
        var resolved = node.Resolved!;
        var entry = resolved.Entry;
        var properties = context.Activity.AdditionalProperties.ToArray();
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var boundBy = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (propertyName, source) in properties)
        {
            // Spec 267 (research R-5): a literal holding null converts as a JSON null does.
            var value = Elsa3TypedValues.IsNullLiteral(source) ? JsonNull : source;
            if (entry is null)
            {
                BindByName(context, propertyName, value, result, boundBy);
                continue;
            }

            var rule = entry.FindMember(propertyName);
            if (rule is null || rule.Kind is Elsa3MemberKind.CustomProperty or Elsa3MemberKind.Synthetic)
            {
                context.Error(UnknownMember,
                    $"Property '{propertyName}' of Elsa 3 activity '{context.Activity.Type}' is not a member the conversion knows.",
                    "Remove the property, or report it so the activity map can classify it.",
                    propertyName);
                continue;
            }

            ApplyRule(context, rule, propertyName, value, result, boundBy);
        }

        if (entry is not null)
        {
            ApplySlotForChildren(context, entry);
            ApplyDefaults(context, entry, properties, result);
            ApplyCustomProperties(context, entry, result);
            ApplyEntryWarnings(context, entry);
        }
        else if (context.Activity.Activities is { Count: > 0 })
        {
            context.Error(UnknownMember,
                $"Elsa 3 activity '{context.Activity.Type}' has child activities, but its Elsa 4 activity declares no slot for them.",
                "Restructure the workflow so the activity holds no children.",
                "activities");
        }

        // Structure members stay for the rewrite strategy to consume.
        foreach (var (propertyName, value) in properties)
        {
            if (entry?.FindMember(propertyName) is { Rule: Elsa3MemberRuleKind.Structure })
                result[propertyName] = value;
        }

        context.Activity.AdditionalProperties = result;
    }

    private static void ApplyRule(
        Elsa3RewriteContext context,
        Elsa3MemberRule rule,
        string propertyName,
        JsonElement value,
        Dictionary<string, JsonElement> result,
        Dictionary<string, string> boundBy)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            // Spec 267: a null output is captured by nothing, as in Elsa 3; a null input converts to what Elsa 3 ran it
            // with. A rule without OnNull (an R2 or R5 default) falls through, and ApplyDefaults emits its default.
            if (rule is { Kind: Elsa3MemberKind.Output, Rule: Elsa3MemberRuleKind.Carry })
                return;
            if (rule.OnNull is { } onNull)
            {
                ApplyNull(context, rule, onNull, propertyName, result, boundBy);
                return;
            }
        }

        var bound = Elsa3TypedValues.IsBound(value);
        var captured = Elsa3TypedValues.IsCaptured(value);
        switch (rule.Rule)
        {
            case Elsa3MemberRuleKind.Carry when rule.Kind == Elsa3MemberKind.Output:
                Add(context, rule.Elsa4Key!, propertyName, value, result, boundBy);
                if (captured && rule.BehaviourWarning is { } outputWarning)
                    Warn(context, outputWarning, propertyName);
                break;
            case Elsa3MemberRuleKind.Carry:
            case Elsa3MemberRuleKind.DefaultFlip:
                Add(context, rule.Elsa4Key!, propertyName, value, result, boundBy);
                if (bound && rule.BehaviourWarning is { } warning)
                    Warn(context, warning, propertyName);
                break;
            case Elsa3MemberRuleKind.NarrowedContract:
                if (rule.Contract == Elsa3ValueContract.ScriptBody)
                    context.Node.ScriptProperty = rule.Elsa4Key;
                if (CheckContract(context, rule, propertyName, value) is { } fitted)
                    Add(context, rule.Elsa4Key!, propertyName, fitted, result, boundBy);
                break;
            case Elsa3MemberRuleKind.ChildSlot:
                if (value.ValueKind is JsonValueKind.Object)
                    context.AddChild(rule.Elsa4Key!, null, value, $"{context.Node.JsonPath}{Elsa3ConversionDiagnostics.FormatProperty(propertyName)}");
                else if (value.ValueKind is JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                            context.AddChild(rule.Elsa4Key!, null, item, $"{context.Node.JsonPath}{Elsa3ConversionDiagnostics.FormatProperty(propertyName)}[{index}]");
                        index++;
                    }
                }
                break;
            case Elsa3MemberRuleKind.RemovedInput:
                CheckRemoved(context, rule, propertyName, value);
                break;
            case Elsa3MemberRuleKind.LostOutput:
                ApplyLostOutput(context, rule, propertyName, value, captured, result, boundBy);
                break;
            case Elsa3MemberRuleKind.SemanticsChanged:
                if (captured)
                    context.Error(MemberSemanticsChanged,
                        $"Output '{propertyName}' is captured, but its Elsa 4 counterpart means something else.",
                        "Read the raw response body and parse it explicitly, then remove the capture before converting.",
                        propertyName);
                break;
            case Elsa3MemberRuleKind.UseRestriction:
                CheckUseRestriction(context, rule, propertyName, value);
                break;
            case Elsa3MemberRuleKind.Structure:
            case Elsa3MemberRuleKind.GraphLevel:
            case Elsa3MemberRuleKind.DisplayOnly:
                break;
        }
    }

    /// <summary>
    /// For an alias, a verbatim catalog key or a plan replacement: properties bind to Elsa 4 members by name,
    /// case-insensitively (FR-010). A leftover property is an unknown member, except on a replacement, whose Elsa 3
    /// workflow-as-activity node carries properties the reusable contract does not declare.
    /// </summary>
    private static void BindByName(
        Elsa3RewriteContext context,
        string propertyName,
        JsonElement value,
        Dictionary<string, JsonElement> result,
        Dictionary<string, string> boundBy)
    {
        var resolved = context.Node.Resolved!;
        var exact = resolved.Inputs.Where(input => StringComparer.Ordinal.Equals(input.Name, propertyName)).ToArray();
        var inputs = exact.Length > 0
            ? exact
            : resolved.Inputs.Where(input => StringComparer.OrdinalIgnoreCase.Equals(input.Name, propertyName) ||
                                             StringComparer.OrdinalIgnoreCase.Equals(input.ReferenceKey, propertyName)).ToArray();
        if (inputs.Length > 1)
        {
            context.Error(DuplicateInputBinding,
                $"Property '{propertyName}' matches more than one declared input: {string.Join(", ", inputs.Select(input => $"'{input.Name}'"))}.",
                "Rename the property so it names exactly one input.",
                propertyName);
            return;
        }

        // Spec 267 (research R-10): a null member gets no binding. Elsa 3 skipped a null workflow-as-activity member, so
        // the reusable ran with its own default, as the Elsa 4 contract's default applies now.
        var isNull = value.ValueKind == JsonValueKind.Null;
        if (inputs.Length == 1)
        {
            if (!isNull)
                Add(context, inputs[0].ReferenceKey, propertyName, value, result, boundBy);
            return;
        }

        if (resolved.FindOutput(propertyName) is { } output)
        {
            if (!isNull)
                Add(context, output.ReferenceKey, propertyName, value, result, boundBy);
            return;
        }

        if (resolved.Source == Elsa3ResolvedSource.Replacement)
            return;

        context.Error(UnknownMember,
            $"Property '{propertyName}' is neither a mapped Elsa 3 member nor a member of Elsa 4 activity '{resolved.ActivityTypeKey}'.",
            "Rename the property to an Elsa 4 member of the activity, or remove it.",
            propertyName);
    }

    /// <summary>Spec 267: converts a JSON null input to what Elsa 3 ran it with, by the member's recorded conversion.</summary>
    private static void ApplyNull(
        Elsa3RewriteContext context,
        Elsa3MemberRule rule,
        Elsa3NullConversion onNull,
        string propertyName,
        Dictionary<string, JsonElement> result,
        Dictionary<string, string> boundBy)
    {
        var key = rule.Elsa4Key!;
        var alias = context.Node.Resolved!.FindInput(key)?.Type.Alias ?? "Object";
        switch (onNull.Rule)
        {
            case Elsa3NullRule.NoBinding:
                return;
            case Elsa3NullRule.TypedNull:
                Add(context, key, propertyName, Elsa3TypedValues.CreateNull(alias), result, boundBy);
                return;
            case Elsa3NullRule.Emit:
                Add(context, key, propertyName, Elsa3TypedValues.CreateLiteral(alias, LiteralNode(onNull.Value ?? string.Empty)), result, boundBy);
                context.Node.NullEmits.Add(key);
                return;
            case Elsa3NullRule.Refuse when onNull.WhenBound is { } other && !HasValue(context, other):
                Add(context, key, propertyName, Elsa3TypedValues.CreateNull(alias), result, boundBy);
                return;
            case Elsa3NullRule.Refuse:
                context.Error(NullUnconvertible,
                    $"Member '{propertyName}' is null. {onNull.Reason}",
                    $"Set a value for '{propertyName}' in the Elsa 3 workflow, or remove the activity.",
                    propertyName);
                return;
        }
    }

    /// <summary>A JSON array or object stays JSON, so publication and graph conversion read it as a collection; anything else is text.</summary>
    private static JsonNode? LiteralNode(string value) =>
        value.TrimStart() is ['[' or '{', ..] ? JsonNode.Parse(value) : JsonValue.Create(value);

    /// <summary>
    /// Whether Elsa 3 ran the member with a value: anything but null, a literal holding null or an empty string. Unlike
    /// <see cref="Elsa3TypedValues.IsBound"/>, a literal <c>{}</c>, <c>[]</c> or <c>null</c> text is a value, as Elsa 3
    /// sent it.
    /// </summary>
    private static bool HasValue(Elsa3RewriteContext context, string elsa3Name) =>
        context.Activity.AdditionalProperties.Any(property =>
            StringComparer.OrdinalIgnoreCase.Equals(property.Key, elsa3Name) &&
            property.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
            !Elsa3TypedValues.IsNullLiteral(property.Value) &&
            !(Elsa3TypedValues.TryGetLiteral(property.Value, out var literal) && string.IsNullOrEmpty(literal)));

    private static void Add(
        Elsa3RewriteContext context,
        string key,
        string propertyName,
        JsonElement value,
        Dictionary<string, JsonElement> result,
        Dictionary<string, string> boundBy)
    {
        if (boundBy.TryGetValue(key, out var other))
        {
            context.Error(DuplicateInputBinding,
                $"Properties '{other}' and '{propertyName}' both bind Elsa 4 member '{key}'.",
                "Keep one of the two properties.",
                propertyName);
            return;
        }

        boundBy[key] = propertyName;
        result[key] = value;
        if (!StringComparer.Ordinal.Equals(key, propertyName))
            context.Node.SourceNames[key] = propertyName;
    }

    private static void ApplySlotForChildren(Elsa3RewriteContext context, Elsa3ActivityMapEntry entry)
    {
        var children = context.Activity.Activities;
        if (children is null || children.Count == 0)
            return;

        // Only the modelled `activities` list is here; port children were appended by AddChild and already have a slot.
        var assigned = context.Node.Slots.Select(slot => slot.Child).OfType<Elsa3Activity>().ToHashSet(ReferenceEqualityComparer.Instance);
        var modelled = children.Where(child => !assigned.Contains(child)).ToArray();
        if (modelled.Length == 0)
            return;

        var rule = entry.FindMember("Activities");
        if (rule is not { Rule: Elsa3MemberRuleKind.ChildSlot })
        {
            context.Error(UnknownMember,
                $"Elsa 3 activity '{context.Activity.Type}' has child activities, but the activity map routes none.",
                "Restructure the workflow so the activity holds no children.",
                "activities");
            return;
        }

        var insertAt = 0;
        foreach (var child in modelled)
            context.Node.Slots.Insert(insertAt++, new Elsa3SlotChild(rule.Elsa4Key!, null, child));
    }

    private static void ApplyDefaults(
        Elsa3RewriteContext context,
        Elsa3ActivityMapEntry entry,
        IReadOnlyCollection<KeyValuePair<string, JsonElement>> properties,
        Dictionary<string, JsonElement> result)
    {
        foreach (var rule in entry.Members)
        {
            // A removed input whose Elsa 4 behaviour differs from Elsa 3's default warns when absent too (for example
            // DisableAuthorizationHeaderValidation: Elsa 3 validated by default, Elsa 4 never does).
            if (rule is { Rule: Elsa3MemberRuleKind.RemovedInput, BehaviourWarning: not null } &&
                !properties.Any(property => StringComparer.OrdinalIgnoreCase.Equals(property.Key, rule.Elsa3Name)))
            {
                CheckRemoved(context, rule, Elsa3ConversionDiagnostics.JsonName(rule.Elsa3Name), default);
                continue;
            }

            if (rule.Elsa3Default is not { ValueKind: JsonValueKind.String } elsa3Default || rule.Elsa4Key is null)
                continue;
            if (rule.Rule is not (Elsa3MemberRuleKind.DefaultFlip or Elsa3MemberRuleKind.NarrowedContract))
                continue;
            if (rule.Kind == Elsa3MemberKind.CustomProperty)
                continue;
            // A typed null is the conversion of a null member (spec 267); only the member pass writes one.
            if (result.TryGetValue(rule.Elsa4Key, out var existing) &&
                (Elsa3TypedValues.IsBound(existing) || Elsa3TypedValues.IsNullLiteral(existing)))
                continue;
            if (rule.WhenBound is { } other &&
                !properties.Any(property => StringComparer.OrdinalIgnoreCase.Equals(property.Key, other) && Elsa3TypedValues.IsBound(property.Value)))
                continue;

            var input = context.Node.Resolved!.FindInput(rule.Elsa4Key);
            result[rule.Elsa4Key] = Elsa3TypedValues.CreateLiteral(input?.Type.Alias ?? "String", elsa3Default.GetString()!);
        }
    }

    private static void ApplyCustomProperties(Elsa3RewriteContext context, Elsa3ActivityMapEntry entry, Dictionary<string, JsonElement> result)
    {
        var custom = context.Activity.CustomProperties;
        var canStartWorkflow = custom?.CanStartWorkflow ?? false;
        var rule = CanStartWorkflowRule(entry);
        var literal = canStartWorkflow ? "true" : "false";
        switch (rule?.Rule)
        {
            case Elsa3MemberRuleKind.Carry:
                if (canStartWorkflow)
                    result[rule.Elsa4Key!] = Elsa3TypedValues.CreateLiteral("Boolean", literal);
                break;
            case Elsa3MemberRuleKind.DefaultFlip:
                result[rule.Elsa4Key!] = Elsa3TypedValues.CreateLiteral("Boolean", literal);
                break;
            case Elsa3MemberRuleKind.UseRestriction:
                if (!string.Equals(rule.Elsa3Default?.GetString(), literal, StringComparison.OrdinalIgnoreCase))
                    context.Error(NoCounterpart,
                        $"Elsa 3 activity '{context.Activity.Type}' is used mid-flow (canStartWorkflow is {literal}); in Elsa 4 it can only start a workflow.",
                        "Use a Delay for a mid-flow wait, or make the activity start the workflow.",
                        "customProperties.canStartWorkflow");
                break;
            // A non-trigger: Elsa 3 only uses the flag to pick a flowchart's start when no trigger fired. Graph
            // conversion reads it for the start node; the Elsa 4 activity gets nothing.
        }

        if (custom?.RunAsynchronously == true)
            context.Error(RemovedInputBound,
                $"Elsa 3 activity '{context.Activity.Type}' runs asynchronously, which Elsa 4 has no setting for.",
                "Clear runAsynchronously before converting.",
                "customProperties.runAsynchronously");
    }

    /// <summary>The entry's <c>customProperties.canStartWorkflow</c> rule; only Elsa 3 triggers declare one.</summary>
    private static Elsa3MemberRule? CanStartWorkflowRule(Elsa3ActivityMapEntry entry) =>
        entry.Members.FirstOrDefault(member =>
            member.Kind == Elsa3MemberKind.CustomProperty &&
            StringComparer.OrdinalIgnoreCase.Equals(member.Elsa3Name, "CanStartWorkflow"));

    /// <summary>
    /// Whether the node's map entry says it is not an Elsa 3 trigger, so its <c>canStartWorkflow</c> flag can only pick
    /// a flowchart's start. A node without an entry (alias, catalog key, replacement) is not known to be a non-trigger.
    /// </summary>
    internal static bool IsNonTrigger(Elsa3NodePlan? node) => IsNonTrigger(node?.Resolved?.Entry);

    /// <summary>Whether a map entry is for a non-trigger: it declares no <c>canStartWorkflow</c> rule.</summary>
    internal static bool IsNonTrigger(Elsa3ActivityMapEntry? entry) =>
        entry is not null && CanStartWorkflowRule(entry) is null;

    private static void ApplyEntryWarnings(Elsa3RewriteContext context, Elsa3ActivityMapEntry entry)
    {
        foreach (var code in entry.BehaviourWarnings)
        {
            if (code == BreakStopsAtParallel &&
                !context.Ancestors().Any(ancestor => ancestor.Resolved?.ActivityTypeKey == ParallelTypeKey))
                continue;
            Warn(context, code, null);
        }
    }

    private static void ApplyLostOutput(
        Elsa3RewriteContext context,
        Elsa3MemberRule rule,
        string propertyName,
        JsonElement value,
        bool captured,
        Dictionary<string, JsonElement> result,
        Dictionary<string, string> boundBy)
    {
        if (!captured)
            return;

        if (context.Node.LoopVariables.TryGetValue(propertyName, out _) ||
            context.Node.Resolved!.Entry?.Rewrite == Elsa3NodeRewriteKind.LoopVariableReferences &&
            StringComparer.OrdinalIgnoreCase.Equals(rule.Elsa3Name, "CurrentValue"))
        {
            // Lowered by the loop-variable rewrite: references to it become the loop's scoped variable.
            result[propertyName] = value;
            return;
        }

        if (rule.Projection is { } projection && !projection.Contains('.'))
        {
            Add(context, projection, propertyName, value, result, boundBy);
            return;
        }

        context.Error(OutputNotRepresentable,
            rule.Projection is null
                ? $"Output '{propertyName}' is captured, but Elsa 4 activity '{context.Node.Resolved!.ActivityTypeKey}' has no such output."
                : $"Output '{propertyName}' is captured; in Elsa 4 it is part of output '{rule.Projection.Split('.')[0]}' ({rule.Projection}), which a binding cannot project yet.",
            rule.Projection is null
                ? "Remove the capture, or rebuild the value from the outputs Elsa 4 provides."
                : $"Capture '{rule.Projection.Split('.')[0]}' and read {rule.Projection} from it in the consuming expression.",
            propertyName);
    }

    private static void CheckRemoved(Elsa3RewriteContext context, Elsa3MemberRule rule, string propertyName, JsonElement value)
    {
        var expected = rule.Elsa3Default is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
        var matches = !Elsa3TypedValues.IsBound(value)
            ? expected is null || string.Equals(expected, "false", StringComparison.OrdinalIgnoreCase)
            : Elsa3TypedValues.TryGetLiteral(value, out var literal) &&
              (expected is null ? Elsa3TypedValues.IsEmptyLiteral(literal) : string.Equals(literal?.Trim(), expected, StringComparison.OrdinalIgnoreCase));

        if (rule.BehaviourWarning is { } warning)
        {
            // Removed whatever its value; Elsa 4 behaves as the default, so only a difference is worth a warning.
            if (!matches)
                Warn(context, warning, propertyName);
            return;
        }

        if (!matches)
            context.Error(RemovedInputBound,
                Elsa3TypedValues.IsExpression(value)
                    ? $"Input '{propertyName}' has no Elsa 4 counterpart and is bound to an expression; the conversion does not evaluate expressions."
                    : $"Input '{propertyName}' has no Elsa 4 counterpart and is bound to a non-default value.",
                expected is null ? "Clear the input before converting." : $"Set the input to '{expected}' or clear it before converting.",
                propertyName);
    }

    private static void CheckUseRestriction(Elsa3RewriteContext context, Elsa3MemberRule rule, string propertyName, JsonElement value)
    {
        var required = rule.Elsa3Default is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
        var holdsActivity = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("type", out _) ||
                            value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(item =>
                                item.ValueKind == JsonValueKind.Object &&
                                (item.TryGetProperty("activity", out var activity) && activity.ValueKind == JsonValueKind.Object ||
                                 item.TryGetProperty("type", out _)));
        var fits = required is null
            ? !holdsActivity
            : Elsa3TypedValues.TryGetLiteral(value, out var literal) && string.Equals(literal, required, StringComparison.OrdinalIgnoreCase);
        if (!fits)
            context.Error(NoCounterpart,
                required is null
                    ? $"Port '{propertyName}' holds an activity; the Elsa 4 activity is a leaf and runs no port activities."
                    : $"Member '{propertyName}' must be '{required}' for an Elsa 4 counterpart to exist.",
                required is null
                    ? "Use the flow variant (FlowSendHttpRequest) and connect its outcomes instead."
                    : "Change the workflow so this use matches what Elsa 4 supports.",
                propertyName);
    }

    /// <summary>R5: returns the value to store when it provably fits the narrower contract, else reports and returns null.</summary>
    private static JsonElement? CheckContract(Elsa3RewriteContext context, Elsa3MemberRule rule, string propertyName, JsonElement value)
    {
        if (!Elsa3TypedValues.IsBound(value))
            return value;

        var isLiteral = Elsa3TypedValues.TryGetLiteral(value, out var literal);
        var alias = Elsa3TypedValues.TypeAlias(value);
        switch (rule.Contract)
        {
            case Elsa3ValueContract.LiteralOnly:
                if (isLiteral)
                    return value;
                return Outside("must be a literal: Elsa 4 fixes it when the workflow is published", "Replace the expression with a literal value.");
            case Elsa3ValueContract.StringOnly:
                if (isLiteral || string.Equals(alias, "String", StringComparison.OrdinalIgnoreCase))
                    return value;
                return Outside($"must be a string, but is declared as '{alias}'", "Convert the value to a string in the expression and declare its type as String.");
            case Elsa3ValueContract.JsonOnly:
                if (isLiteral)
                    return IsJson(literal) ? value : Outside("must be JSON", "Use a JSON literal.");
                if (alias is null || alias is "Object" or "JsonElement" or "JsonNode" or "JsonObject" or "ExpandoObject" or "IDictionary" or "Dictionary" or "ObjectDictionary")
                    return value;
                return Outside($"must be JSON, but is declared as '{alias}'", "Produce a JSON object in the expression and declare its type as Object.");
            case Elsa3ValueContract.EnumNameToInt:
                if (isLiteral)
                {
                    if (int.TryParse(literal, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        return value;
                    if (Enum.TryParse<HttpStatusCode>(literal, ignoreCase: true, out var status))
                        return Elsa3TypedValues.WithLiteral(value, ((int)status).ToString(CultureInfo.InvariantCulture));
                    return Outside($"must be an HTTP status code, but is '{literal}'", "Use a numeric status code.");
                }
                if (alias is "Int32" or "Int64" or "Int16")
                    return value;
                return Outside($"must be an integer status code, but is declared as '{alias}'", "Return the numeric status code from the expression and declare its type as Int32.");
            case Elsa3ValueContract.SecretReferenceOnly:
                return Outside("must be a secret reference in Elsa 4; a literal or expression would expose the credential", "Move the credential to a secret and bind the input to it after conversion.");
            case Elsa3ValueContract.SingleHeaderValue:
                if (isLiteral && TrySingleValueHeaders(literal, out var headers))
                    return Elsa3TypedValues.WithLiteral(value, headers);
                return Outside("must be a literal header map with one value per header", "Give each header a single value as a JSON object literal.");
            case Elsa3ValueContract.LiteralTimeSpanToString:
                if (isLiteral && TimeSpan.TryParse(literal, CultureInfo.InvariantCulture, out var span))
                    return Elsa3TypedValues.CreateLiteral("String", span.ToString("c", CultureInfo.InvariantCulture));
                return Outside("must be a literal time span", "Use a literal interval such as 00:05:00.");
            case Elsa3ValueContract.HttpMethodSupported:
                if (isLiteral && literal?.Trim().ToUpperInvariant() is "PATCH" or "OPTIONS")
                {
                    context.Error(NoCounterpart,
                        $"HTTP method '{literal}' has no Elsa 4 counterpart; Elsa 4 sends GET, POST, PUT, DELETE and HEAD.",
                        "Use a supported method.",
                        propertyName);
                    return null;
                }
                return value;
            case Elsa3ValueContract.HttpResponseBody:
                return CheckHttpResponseBody(context, propertyName, value, isLiteral, alias);
            case Elsa3ValueContract.HttpResponseContentType:
                return IsBlankExpression(value) ? null : value;
            default:
                return value;
        }

        JsonElement? Outside(string reason, string guidance)
        {
            context.Error(ValueOutsideContract, $"Input '{propertyName}' {reason}.", guidance, propertyName);
            return null;
        }
    }

    /// <summary>
    /// Elsa 3 sent text as is and serialized any other value as JSON when the content type was unset (inferred
    /// <c>application/json</c>) or exactly a type its JSON writer handled; otherwise it sent the value's type name.
    /// Elsa 4 sends text as is and any other value as JSON, so only the JSON cases convert faithfully. A script with a
    /// typed declaration (<c>String</c>, <c>Int32</c>, …) passes here and is refused by value-flow lowering (FR-013),
    /// because Elsa 3 converted its result to that type.
    /// </summary>
    private static JsonElement? CheckHttpResponseBody(
        Elsa3RewriteContext context, string propertyName, JsonElement value, bool isLiteral, string? alias)
    {
        if (isLiteral || string.Equals(alias, "String", StringComparison.OrdinalIgnoreCase))
            return value;

        if (alias is not null && (alias.Equals("Byte[]", StringComparison.OrdinalIgnoreCase) ||
                                  alias.EndsWith("Stream", StringComparison.OrdinalIgnoreCase)))
            return Outside($"is binary content ('{alias}'), which Elsa 3 sent as application/octet-stream; Elsa 4 sends text or JSON only",
                "Return text or a JSON value instead.");

        var contentType = context.Activity.AdditionalProperties
            .FirstOrDefault(property => property.Key.Equals("contentType", StringComparison.OrdinalIgnoreCase)).Value;
        if (!Elsa3TypedValues.IsBound(contentType) || IsBlankExpression(contentType))
            return value;
        if (Elsa3TypedValues.TryGetLiteral(contentType, out var mediaType))
        {
            if (mediaType is "application/json" or "text/json")
                return value;
            if (IsJsonMediaType(mediaType))
            {
                // Elsa 3 matched its JSON writer by exact string, so it sent the value's type name; Elsa 4 sends the JSON.
                Warn(context, HttpResponseJsonVariant, propertyName);
                return value;
            }
            return Outside($"is declared as '{alias}', but content type '{mediaType}' is not JSON; Elsa 3 sent the value's type name as text",
                "Return a string, or set ContentType to application/json.");
        }
        return Outside($"is declared as '{alias}', and its content type is computed by an expression; Elsa 3 sent JSON only when the run-time content type was exactly a JSON type",
            "Set ContentType to the literal application/json, or return a string.");

        JsonElement? Outside(string reason, string guidance)
        {
            context.Error(ValueOutsideContract, $"Input '{propertyName}' {reason}.", guidance, propertyName);
            return null;
        }
    }

    /// <summary>A JSON media type, ignoring letter case and parameters: <c>application/json</c>, <c>text/json</c> or a <c>+json</c> type.</summary>
    private static bool IsJsonMediaType(string? mediaType)
    {
        var type = mediaType?.Split(';', 2)[0].Trim() ?? string.Empty;
        return type.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("text/json", StringComparison.OrdinalIgnoreCase) ||
               type.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the property is bound to a non-literal expression whose script is blank (Elsa 3 evaluates it to null).</summary>
    private static bool IsBlankExpression(JsonElement value) =>
        Elsa3TypedValues.IsExpression(value) &&
        Elsa3TypedValues.TryGetExpression(value, out _, out var script) &&
        script.ValueKind == JsonValueKind.String &&
        string.IsNullOrWhiteSpace(script.GetString());

    private static bool IsJson(string? literal)
    {
        if (string.IsNullOrWhiteSpace(literal))
            return true;
        try
        {
            using var _ = JsonDocument.Parse(literal);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TrySingleValueHeaders(string? literal, out string headers)
    {
        headers = "{}";
        if (string.IsNullOrWhiteSpace(literal))
            return true;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(literal);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject map)
            return false;
        var single = new JsonObject();
        foreach (var (name, header) in map)
        {
            switch (header)
            {
                case JsonValue scalar:
                    single[name] = scalar.ToString();
                    break;
                case JsonArray { Count: 1 } array when array[0] is JsonValue only:
                    single[name] = only.ToString();
                    break;
                default:
                    return false;
            }
        }

        headers = single.ToJsonString();
        return true;
    }

    /// <summary>
    /// A container's own variables (Sequence, Flowchart) go into its native structure, scoped to the container (R7).
    /// </summary>
    private void ReadContainerVariables(Elsa3RewriteContext context)
    {
        if (context.Node.Resolved?.Entry?.FindMember("Variables") is not { Rule: Elsa3MemberRuleKind.Structure } ||
            !context.TryGetProperty("Variables", out var property, out var variables) ||
            variables.ValueKind != JsonValueKind.Array)
            return;

        try
        {
            foreach (var item in variables.EnumerateArray())
            {
                var variable = item.Deserialize<Elsa3Variable>(Elsa3JsonOptions);
                if (variable is null || string.IsNullOrWhiteSpace(variable.Id))
                    continue;
                if (Elsa3VariableMapper.TryMap(wellKnownTypeRegistry, objectConverter, variable, out var mapped, out var refusal))
                    context.Node.Variables.Add(mapped);
                else
                    context.Error(VariableValueUnconvertible, refusal!.Message, refusal.Guidance, property);
            }
        }
        catch (JsonException exception)
        {
            context.Error(UnknownMember, $"The container's variables could not be read: {exception.Message}", "Fix the variable declarations.", property);
        }
    }

    private static void RemoveConsumedStructure(Elsa3RewriteContext context)
    {
        var entry = context.Node.Resolved!.Entry;
        if (entry is null)
            return;
        foreach (var rule in entry.Members.Where(rule => rule.Rule == Elsa3MemberRuleKind.Structure))
            context.RemoveProperty(rule.Elsa3Name);
    }

    private void PinIntrinsic(Elsa3RewriteContext context)
    {
        var node = context.Node;
        if (node.Resolved?.IntrinsicKind is not { } kind || node.Intrinsic is not null)
            return;

        switch (kind)
        {
            case AuthoredWorkflowIntrinsicKind.SetCorrelationId:
            case AuthoredWorkflowIntrinsicKind.SetInstanceName:
                node.Intrinsic = new AuthoredWorkflowIntrinsic(kind, new TypeReference("String"));
                break;
            case AuthoredWorkflowIntrinsicKind.SetOutput:
                node.Intrinsic = new AuthoredWorkflowIntrinsic(kind, OutputType(context));
                break;
            case AuthoredWorkflowIntrinsicKind.Finish:
                node.Intrinsic = new AuthoredWorkflowIntrinsic(kind);
                break;
            case AuthoredWorkflowIntrinsicKind.Set:
                if (!node.Failed)
                    context.Error(UnsupportedMode,
                        "SetVariable has no variable target.",
                        "Select the variable the activity sets.",
                        "variable");
                break;
        }
    }

    private TypeReference OutputType(Elsa3RewriteContext context)
    {
        if (context.Activity.AdditionalProperties.TryGetValue(Elsa3IntrinsicContracts.NameKey, out var name) &&
            Elsa3TypedValues.TryGetLiteral(name, out var outputName) &&
            context.Plan.Definition.Outputs?.FirstOrDefault(output => StringComparer.OrdinalIgnoreCase.Equals(output.Name, outputName)) is { } declared &&
            !string.IsNullOrWhiteSpace(declared.Type))
            return ResolveType(declared.Type);
        return new TypeReference("Object");
    }

    private static void CheckRequiredInputs(Elsa3RewriteContext context)
    {
        var node = context.Node;
        if (node.Failed || node.Resolved is null)
            return;
        foreach (var input in node.Resolved.Inputs.Where(input => input.IsRequired))
        {
            if (context.Activity.AdditionalProperties.TryGetValue(input.ReferenceKey, out var value) &&
                (Elsa3TypedValues.IsBound(value) || IsEmittedDefault(node, input.ReferenceKey, value)))
                continue;
            context.Error(RequiredInputUnbound,
                $"Required Elsa 4 input '{input.ReferenceKey}' of '{node.Resolved.ActivityTypeKey ?? node.Resolved.ActivityVersionId}' would stay unbound.",
                "Bind the Elsa 3 member that feeds this input before converting.",
                input.ReferenceKey);
        }
    }

    /// <summary>
    /// Whether the value is the literal the entry's R2 rule, or a null member's recorded conversion (spec 267), emits for
    /// the member. A blank value (WriteLine's empty line, a cleared correlation id) is bound although
    /// <see cref="Elsa3TypedValues.IsBound"/> counts a blank literal as unbound. A null conversion counts only where the
    /// member pass wrote it: an Elsa 3 blank literal is not null, and keeps its own rule (#3281).
    /// </summary>
    private static bool IsEmittedDefault(Elsa3NodePlan node, string elsa4Key, JsonElement value)
    {
        var rule = node.Resolved?.Entry?.Members.FirstOrDefault(member => member.Kind == Elsa3MemberKind.Input &&
                                                                          StringComparer.Ordinal.Equals(member.Elsa4Key, elsa4Key));
        var emitted = rule switch
        {
            { OnNull: { Rule: Elsa3NullRule.Emit, Value: { } onNull } } when node.NullEmits.Contains(elsa4Key) => onNull,
            { Rule: Elsa3MemberRuleKind.DefaultFlip, Elsa3Default: { ValueKind: JsonValueKind.String } elsa3Default } => elsa3Default.GetString(),
            _ => null
        };
        return emitted is not null &&
               Elsa3TypedValues.TryGetLiteral(value, out var literal) &&
               StringComparer.Ordinal.Equals(literal, emitted);
    }

    private Elsa3Activity? ReadChild(
        Elsa3ConversionPlan plan,
        Elsa3NodePlan parent,
        JsonElement json,
        string jsonPath,
        Dictionary<Elsa3Activity, string> childPaths)
    {
        try
        {
            var child = json.Deserialize<Elsa3Activity>(Elsa3JsonOptions);
            if (child is null)
                return null;
            child.AdditionalProperties ??= new Dictionary<string, JsonElement>();
            childPaths[child] = jsonPath;
            return child;
        }
        catch (JsonException exception)
        {
            plan.Diagnostics.Add(Elsa3ConversionDiagnostics.Error(plan, parent, UnknownMember,
                $"A child activity at '{jsonPath}' could not be read: {exception.Message}",
                "Fix the child activity's JSON before converting."));
            parent.Failed = true;
            return null;
        }
    }

    private TypeReference ResolveType(string elsa3TypeToken) =>
        TypeReferenceFactory.FromClrType(
            LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, elsa3TypeToken),
            wellKnownTypeRegistry.GetAliasOrDefault);

    private static void Warn(Elsa3RewriteContext context, string code, string? property) =>
        context.Warning(code, WarningMessages.GetValueOrDefault(code, code), "Review the converted workflow's behaviour for this difference.", property);

    private static void Error(Elsa3ConversionPlan plan, Elsa3NodePlan node, string code, string message, string guidance)
    {
        node.Failed = true;
        plan.Diagnostics.Add(Elsa3ConversionDiagnostics.Error(plan, node, code, message, guidance));
    }

    private static readonly IReadOnlyDictionary<string, string> WarningMessages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [FormPostNotParsed] = "Elsa 4 does not parse form posts into ParsedContent, and a text body becomes a string.",
        [RequestSizeNoOutcome] = "A request over the size limit has no outcome in Elsa 4; the request is refused.",
        [HttpOutcomeSingle] = "Elsa 4 emits one outcome per response: Done no longer fires together with a status-code outcome, and a non-2xx response goes to Failed.",
        [AuthHeaderUnvalidated] = "Elsa 4 does not validate the Authorization header, as if validation were disabled.",
        [HttpAuthorizationUseConnection] = "Create an http connection and bind it to Connection. SendHttpRequest.Authorization was removed.",
        [WhilePersistedCondition] = "Elsa 4 re-reads the While condition only from persisted state.",
        [ForZeroStepFaults] = "A For step of 0 faults in Elsa 4.",
        [BreakStopsAtParallel] = "In Elsa 4, Break does not cross a Parallel branch.",
        [ForkWaitAnyCancellation] = "Fork WaitAny becomes a Parallel with threshold 1; cancellation of the other branches may differ.",
        [ExecuteWorkflowDurable] = "ExecuteWorkflow becomes a durable dispatch that waits for completion, not an in-process call.",
        [ScriptOutcomeRouting] = "Elsa 4 routes RunJavaScript by its return value; setOutcomes with several outcomes is refused.",
        [OutcomeUndeclared] = "The reusable workflow completes with an outcome its definition does not declare; it is declared in the converted contract.",
        [OutcomeUnreached] = "The reusable workflow declares an outcome that nothing completes with; it is left out of the converted contract.",
        [HttpResponseJsonVariant] = "Elsa 3 matched this JSON content type only by exact string and sent the object's type name; Elsa 4 sends the object as JSON.",
    };
}
