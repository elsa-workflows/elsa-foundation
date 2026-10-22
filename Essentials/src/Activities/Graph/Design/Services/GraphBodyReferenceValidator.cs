using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Activities.Graph.Design.Models;
using Elsa.Expressions.Core.Models;
using DesignActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Activities.Graph.Design.Services;

/// <summary>
/// Refuses, at activity publication, a graph body reference to anything the closed scope does not hold: an input the activity does not declare, a variable it does not declare, a container that does not
/// enclose the reading node, a producer that is not a body node, and a capture into anything but its own variables.
/// Inside a body the root scope is the graph, so <c>"workflow"</c> (and Studio's legacy <c>"activity-graph"</c>) name
/// the graph's variables and a request read names one of its inputs.
/// </summary>
/// <remarks>
/// Ambient JavaScript reads (<c>variables.x</c>, <c>getVariable('x')</c>) are not checked, as for workflows: they
/// resolve at run time only. Potentially concurrent captures into one variable are not detected here; no concurrency
/// analysis exists for captures yet, for workflows either.
/// </remarks>
internal static class GraphBodyReferenceValidator
{
    public const string InputUnknown = "activity.graph.body-input-unknown";
    public const string VariableUnknown = "activity.graph.body-variable-unknown";
    public const string VariableScopeInvisible = "activity.graph.body-variable-scope-invisible";
    public const string ProducerUnknown = "activity.graph.body-producer-unknown";
    public const string CaptureTargetUnknown = "activity.graph.body-capture-target-unknown";
    public const string CaptureTypeIncompatible = "activity.graph.body-capture-type-incompatible";

    private const string WorkflowScopeId = "workflow";
    // The whole result of an activity, which every activity with a result produces.
    private const string WholeResultProjectionKey = "$result";
    private const string LegacyGraphScopeId = "activity-graph";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>One body node as the validator needs it.</summary>
    public sealed record BodyNode(string NodeId, string JsonPointer, string? ParentOccurrenceId, JsonElement Element);

    /// <summary>A refusal, located at the body node's input or output that holds the reference.</summary>
    public sealed record Finding(string Code, string Message, string JsonPointer, string ReferenceKey);

    /// <param name="dependencyContracts">Each body node's resolved dependency contract, when known (compile, not draft validation).</param>
    public static IReadOnlyList<Finding> Validate(
        ActivityGraphManifest graph,
        DesignActivityContract contract,
        IReadOnlyList<BodyNode> nodes,
        IReadOnlyDictionary<string, DesignActivityContract>? dependencyContracts)
    {
        var findings = new List<Finding>();
        // Duplicate variable reference keys or node ids are reported by the graph's own checks; a body cannot be
        // resolved against them, so its references are not checked until they are fixed.
        if (HasDuplicates(graph.Variables.Select(x => x.ReferenceKey)) || HasDuplicates(nodes.Select(x => x.NodeId)))
            return findings;

        var inputs = contract.Inputs.Select(x => x.ReferenceKey).ToHashSet(StringComparer.Ordinal);
        var variables = graph.Variables.ToDictionary(x => x.ReferenceKey, StringComparer.Ordinal);
        var nodesById = nodes.ToDictionary(x => x.NodeId, StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            foreach (var (argument, index) in Arguments(node.Element, "inputs"))
            {
                var pointer = $"{node.JsonPointer}/inputs/{index}";
                var key = argument.ReferenceKey;
                foreach (var reference in References(argument))
                {
                    switch (reference)
                    {
                        case RequestRead request when !inputs.Contains(request.MemberKey):
                            findings.Add(new(InputUnknown,
                                $"Body node '{node.NodeId}' input '{key}' reads graph input '{request.MemberKey}', which this activity does not declare.",
                                pointer, key));
                            break;
                        case VariableRead variable when IsGraphScope(variable.ScopeId):
                            if (!variables.ContainsKey(variable.VariableKey))
                                findings.Add(new(VariableUnknown,
                                    $"Body node '{node.NodeId}' input '{key}' reads graph variable '{variable.VariableKey}', which this activity does not declare.",
                                    pointer, key));
                            break;
                        case VariableRead variable when !IsAncestor(variable.ScopeId!, node, nodesById):
                            findings.Add(new(VariableScopeInvisible,
                                $"Body node '{node.NodeId}' input '{key}' reads variable '{variable.VariableKey}' of scope '{variable.ScopeId}', which is not a container around this node.",
                                pointer, key));
                            break;
                        case ResultRead result when !nodesById.ContainsKey(result.ProducerNodeId) ||
                                                    (!StringComparer.Ordinal.Equals(result.ProjectionKey, WholeResultProjectionKey) &&
                                                     dependencyContracts?.TryGetValue(result.ProducerNodeId, out var producer) == true &&
                                                     producer.Outputs.All(output => !StringComparer.Ordinal.Equals(output.ReferenceKey, result.ProjectionKey))):
                            findings.Add(new(ProducerUnknown,
                                $"Body node '{node.NodeId}' input '{key}' reads the result '{result.ProjectionKey}' of '{result.ProducerNodeId}', which is not a node of this activity's body or has no such result.",
                                pointer, key));
                            break;
                    }
                }
            }

            foreach (var (output, index) in Arguments(node.Element, "outputs"))
                ValidateCapture(node, output, $"{node.JsonPointer}/outputs/{index}", variables, dependencyContracts, findings);
        }

        return findings;
    }

    private static void ValidateCapture(
        BodyNode node,
        ArgumentState output,
        string pointer,
        IReadOnlyDictionary<string, ActivityGraphVariable> variables,
        IReadOnlyDictionary<string, DesignActivityContract>? dependencyContracts,
        List<Finding> findings)
    {
        if (References(output).OfType<VariableRead>().FirstOrDefault() is not { } target ||
            !IsGraphScope(target.ScopeId) ||
            !variables.TryGetValue(target.VariableKey, out var variable))
        {
            var described = References(output).OfType<VariableRead>().FirstOrDefault() is { } read
                ? $"'{read.VariableKey}' of scope '{read.ScopeId ?? WorkflowScopeId}'"
                : "a value that is not a variable";
            findings.Add(new(CaptureTargetUnknown,
                $"Body node '{node.NodeId}' output '{output.ReferenceKey}' captures into {described}. A body can capture only into this activity's declared variables.",
                pointer, output.ReferenceKey));
            return;
        }

        if (output.Conversion is not null ||
            dependencyContracts?.TryGetValue(node.NodeId, out var producer) != true ||
            producer.Outputs.FirstOrDefault(x => StringComparer.Ordinal.Equals(x.ReferenceKey, output.ReferenceKey)) is not { } source)
            return;

        if (!IsCaptureCompatible(source.Type, variable.Type))
            findings.Add(new(CaptureTypeIncompatible,
                $"Body node '{node.NodeId}' output '{output.ReferenceKey}' of type '{source.Type.Alias}' cannot be captured into graph variable '{variable.Name}' of type '{variable.Type.Alias}' without a conversion.",
                pointer, output.ReferenceKey));
    }

    // An exact alias and collection kind, or a target that holds any value. Anything else needs an authored conversion,
    // which placement resolves into a conversion plan.
    private static bool IsCaptureCompatible(TypeReference source, TypeReference target) =>
        IsAnyType(target.Alias) ||
        (StringComparer.OrdinalIgnoreCase.Equals(source.Alias, target.Alias) && source.CollectionKind == target.CollectionKind);

    private static bool IsAnyType(string alias) =>
        alias.Equals("object", StringComparison.OrdinalIgnoreCase) ||
        alias.Equals("any", StringComparison.OrdinalIgnoreCase) ||
        alias.Equals("elsa.any", StringComparison.OrdinalIgnoreCase) ||
        alias.Equals("jsonnode", StringComparison.OrdinalIgnoreCase);

    private static bool HasDuplicates(IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return keys.Any(key => !seen.Add(key));
    }

    // An absent or empty scope is the root scope, as the compiler reads it.
    private static bool IsGraphScope(string? scopeId) =>
        string.IsNullOrEmpty(scopeId) || scopeId is WorkflowScopeId or LegacyGraphScopeId;

    private static bool IsAncestor(string scopeId, BodyNode node, IReadOnlyDictionary<string, BodyNode> nodesById)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var parentId = node.ParentOccurrenceId;
        while (parentId is not null && visited.Add(parentId))
        {
            if (StringComparer.Ordinal.Equals(parentId, scopeId))
                return true;
            parentId = nodesById.TryGetValue(parentId, out var parent) ? parent.ParentOccurrenceId : null;
        }

        return false;
    }

    // Nodes arrive only after the whole body deserialized as activity nodes, so every argument has this shape.
    private static IEnumerable<(ArgumentState Argument, int Index)> Arguments(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            yield break;

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.Deserialize<ArgumentState>(SerializerOptions) is { } argument)
                yield return (argument, index);
            index++;
        }
    }

    private abstract record Reference;
    private sealed record RequestRead(string MemberKey) : Reference;
    private sealed record VariableRead(string VariableKey, string? ScopeId) : Reference;
    private sealed record ResultRead(string ProducerNodeId, string ProjectionKey) : Reference;

    // The reference shapes an authored argument can hold: the role-owned binding syntaxes, Studio's Input pick, and the
    // parameters of a portable expression. These are the shapes placement remaps (PlacedReferenceRemapper).
    private static IEnumerable<Reference> References(ArgumentState argument)
    {
        // Syntax names match case-insensitively, as the binding compiler and the placer read them.
        var expressionType = argument.Value.ExpressionType?.ToLowerInvariant();
        // A variable reference is read exactly as the compiler reads it, in its string or object form.
        if (expressionType == "variable")
        {
            if (VariableReference.TryParse(argument.Value.Value, out var reference) && reference is not null)
                yield return new VariableRead(reference.ReferenceKey, reference.DeclaringScopeId);
            yield break;
        }

        var value = argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value);
        if (value.ValueKind != JsonValueKind.Object)
            yield break;

        switch (expressionType)
        {
            case "workflowrequest" when Text(value, "memberKey") is { } memberKey:
                yield return new RequestRead(memberKey);
                yield break;
            case "input" when Text(value, "referenceKey") is { } inputKey:
                yield return new RequestRead(inputKey);
                yield break;
            case "activityresult" when Text(value, "producerNodeId") is { } producer && Text(value, "projectionKey") is { } projection:
                yield return new ResultRead(producer, projection);
                yield break;
        }

        if (!value.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            yield break;
        foreach (var parameter in parameters.EnumerateObject().Select(x => x.Value).Where(x => x.ValueKind == JsonValueKind.Object))
        {
            switch (Text(parameter, "kind"))
            {
                case "WorkflowRequest" when Text(parameter, "memberKey") is { } memberKey:
                    yield return new RequestRead(memberKey);
                    break;
                case "Variable" when Text(parameter, "variableKey") is { } variableKey:
                    yield return new VariableRead(variableKey, Text(parameter, "declaringScopeNodeId"));
                    break;
                case "ActivityResult" when Text(parameter, "producerNodeId") is { } producer && Text(parameter, "projectionKey") is { } projection:
                    yield return new ResultRead(producer, projection);
                    break;
            }
        }
    }

    private static string? Text(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
