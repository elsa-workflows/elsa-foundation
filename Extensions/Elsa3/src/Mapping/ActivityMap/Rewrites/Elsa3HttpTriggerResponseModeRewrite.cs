using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
using Elsa3.Models;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// HttpEndpoint request projections (plan D-11 to D-13, D-17). Elsa 3 exposes the request headers and query string as
/// separate outputs whose values are a string, or an array when the key repeats. Elsa 4 has one <c>Request</c> output
/// whose headers and query map each key to a list, and a binding cannot project part of an output. For each captured
/// <c>Headers</c>/<c>QueryStringData</c>, the rewrite inserts a <c>set</c> node right after the endpoint that rebuilds
/// the Elsa 3 shape from <c>Request</c> into the variable Elsa 3 captured into: that workflow variable when it is one,
/// else a synthesized workflow variable the captured memory slot is aliased to. (<c>ResponseMode = Sync</c> is a
/// synthetic member default of the map entry.)
/// </summary>
internal sealed class Elsa3HttpTriggerResponseModeRewrite : IElsa3NodeRewrite
{
    private const string RequestOutput = "Request";

    private static readonly (string Member, string Suffix, string RequestProperty)[] Projections =
    [
        ("Headers", "headers", "headers"),
        ("QueryStringData", "query", "query"),
    ];

    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.HttpTriggerResponseMode;

    public ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        foreach (var (member, suffix, requestProperty) in Projections)
        {
            if (!context.TryGetProperty(member, out var property, out var value) || !Elsa3TypedValues.IsCaptured(value))
                continue;

            var memoryId = value.GetProperty("memoryReference").GetProperty("id").GetString()!;
            if (Target(context, memoryId, suffix, property) is { } variableKey)
                Insert(context, suffix, requestProperty, variableKey, property);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>The workflow variable the set node writes, or null when the capture cannot be expressed (reported).</summary>
    private static string? Target(Elsa3RewriteContext context, string memoryId, string suffix, string property)
    {
        var definition = context.Plan.Definition;
        if (definition.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.Id, memoryId)))
            return memoryId;

        if (context.Ancestors().Any(ancestor => ancestor.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, memoryId))))
        {
            context.Error(Elsa3ActivityDiagnosticCodes.OutputNotRepresentable,
                $"Output '{property}' is captured into container variable '{memoryId}'; Elsa 4 writes workflow variables only.",
                "Declare the variable on the workflow instead of the container before converting.",
                property);
            return null;
        }

        // A bare memory slot: a synthesized workflow variable stands in for it (D-13).
        var synthesized = $"elsa3.http.{Elsa3ConversionDiagnostics.NodeId(context.Activity)}.{suffix}";
        if (!definition.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.Id, synthesized)))
            definition.Variables.Add(new Elsa3Variable { Id = synthesized, Name = synthesized, TypeName = "Object" });
        context.Plan.MemoryAliases[memoryId] = synthesized;
        return synthesized;
    }

    private static void Insert(Elsa3RewriteContext context, string suffix, string requestProperty, string variableKey, string property)
    {
        var endpoint = context.Activity;
        var parent = context.Node.Parent;
        var parentPlan = parent is null ? null : context.Plan.Find(parent);
        if (parentPlan is { Resolved: null })
            return; // the container itself is unconvertible and already reported

        var slotIndex = parentPlan?.Slots.FindIndex(slot => ReferenceEquals(slot.Child, endpoint)) ?? -1;
        var slotName = slotIndex < 0 ? null : parentPlan!.Slots[slotIndex].SlotName;
        var slot = parentPlan?.Resolved?.Structure?.Payload.Slots.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Name, slotName));
        if (slot is null || slot.CollectionProperty is not null || !string.Equals(slot.Cardinality, "Many", StringComparison.OrdinalIgnoreCase))
        {
            context.Error(Elsa3ActivityDiagnosticCodes.OutputNotRepresentable,
                $"Output '{property}' is captured, but the endpoint is not in a list of activities, so no node can follow it to rebuild the value.",
                "Wrap the endpoint in a Sequence before converting.",
                property);
            return;
        }

        var nodeId = $"{Elsa3ConversionDiagnostics.NodeId(endpoint)}.elsa3-http-{suffix}";
        var source =
            "(function (values) { var shaped = {}; for (var key in values) { var list = values[key]; " +
            "shaped[key] = list.length === 0 ? null : list.length === 1 ? list[0] : list; } return shaped; })" +
            // The request reaches the script as JSON; read either casing of the property so neither serializer
            // convention yields a silently empty value.
            $"(args.request.{requestProperty} || args.request.{char.ToUpperInvariant(requestProperty[0])}{requestProperty[1..]} || {{}})";
        var expression = new ExpressionDefinition(
            "JavaScript",
            source,
            new TypeReference("Object"),
            new Dictionary<string, ExpressionParameterBinding>(StringComparer.Ordinal)
            {
                ["request"] = new ActivityResultExpressionParameterBinding(Elsa3ConversionDiagnostics.NodeId(endpoint), RequestOutput)
            },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var setNode = new Elsa3Activity
        {
            Id = nodeId,
            NodeId = nodeId,
            Name = nodeId,
            Type = "Elsa.SetVariable",
            AdditionalProperties = new Dictionary<string, JsonElement>
            {
                [Elsa3IntrinsicContracts.ValueKey] = JsonSerializer.SerializeToElement(new
                {
                    typeName = "Object",
                    expression = new { type = "JavaScript", value = expression }
                })
            }
        };

        var setPlan = new Elsa3NodePlan(setNode, $"{context.Node.JsonPath}{Elsa3ConversionDiagnostics.FormatProperty(property)}", parent)
        {
            Resolved = Elsa3IntrinsicContracts.Resolve(Elsa3IntrinsicContracts.Set, null),
            SynthesizedFrom = (endpoint, suffix == "query" ? "QueryStringData" : "Headers"),
            Intrinsic = new AuthoredWorkflowIntrinsic(
                AuthoredWorkflowIntrinsicKind.Set,
                new TypeReference("Object"),
                new VariableReference(variableKey, VariableReference.WorkflowScopeId))
        };
        context.Plan.Nodes[setNode] = setPlan;

        var siblings = parent!.Activities!;
        siblings.Insert(siblings.IndexOf(endpoint) + 1, setNode);
        parentPlan!.Slots.Insert(slotIndex + 1, new Elsa3SlotChild(slotName!, null, setNode));
        // In a list the position orders it; in a flowchart graph conversion connects it after the endpoint (spec 239 R-10).
        if (Elsa3FlowchartGraphBuilder.IsFlowchart(parentPlan))
            context.Node.FollowedBy.Insert(0, setNode);
    }
}
