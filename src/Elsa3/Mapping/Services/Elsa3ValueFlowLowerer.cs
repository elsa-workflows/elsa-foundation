using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Services;

/// <summary>
/// Importer-local pass-two implementation. It validates the Elsa 3 reference graph and rewrites a
/// private definition clone into ordinary Elsa 4 authored bindings before the design mapper sees it.
/// </summary>
public sealed class Elsa3ValueFlowLowerer(Elsa3ExpressionRewriter expressionRewriter)
{
    public IReadOnlyList<Elsa3MigrationDiagnostic> Lower(
        Elsa3WorkflowDefinition definition,
        Elsa3MemoryReferenceInventory inventory,
        Elsa3WorkflowDefinitionImportInput input)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(input);

        var diagnostics = new List<Elsa3MigrationDiagnostic>();
        var properties = CollectProperties(definition.Root, inventory).ToArray();
        var nodeOrder = inventory.Nodes
            .Select((node, index) => (node.ActivityNodeId, index))
            .ToDictionary(item => item.ActivityNodeId, item => item.index, StringComparer.Ordinal);
        var outputs = inventory.Occurrences
            .Where(occurrence => occurrence.Direction == Elsa3PropertyDirection.Output)
            .ToArray();
        var outputsByMemory = outputs
            .GroupBy(occurrence => occurrence.MemoryReferenceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var variablesById = definition.Variables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Id))
            .GroupBy(variable => variable.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var cyclicMemoryIds = FindCycleMemoryIds(inventory, variablesById, outputsByMemory, diagnostics, input);

        foreach (var property in properties)
        {
            var directions = property.Occurrences.Select(occurrence => occurrence.Direction).Distinct().ToArray();
            if (directions.Contains(Elsa3PropertyDirection.Unknown))
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ImportDiagnosticCodes.UnsupportedShape,
                    $"Property '{property.PropertyName}' carries an Elsa 3 memory reference but is not a declared input or output.",
                    "Install a lowering provider for the custom property or remove its memory reference before import.",
                    property.MemoryReferenceId is null ? property.JsonPath : $"{property.JsonPath}.memoryReference",
                    property.MemoryReferenceId));
                continue;
            }

            if ((property.Expression is not null || property.MemoryReferenceId is not null) &&
                property.InputContract is not null &&
                !TypesMatch(ReadType(property.Value), property.InputContract.Type))
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ImportDiagnosticCodes.IncompatibleValue,
                    $"Property '{property.PropertyName}' declares type '{ReadType(property.Value).Alias}', but the target member requires '{property.InputContract.Type.Alias}'.",
                    "Change the Elsa 3 typed value to the target activity member type before import.",
                    $"{property.JsonPath}.typeName",
                    property.MemoryReferenceId));
                continue;
            }

            if (property.Expression is not null)
            {
                LowerExpression(definition, input, property, outputs, nodeOrder, diagnostics);
                continue;
            }

            if (property.MemoryReferenceId is null)
                continue;

            if (string.IsNullOrWhiteSpace(property.MemoryReferenceId))
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ImportDiagnosticCodes.UnresolvableReferenceShape,
                    "Memory reference does not contain a non-empty id.",
                    "Assign a valid Elsa 3 memory id or replace the property with an explicit literal/reference binding.",
                    $"{property.JsonPath}.memoryReference",
                    property.MemoryReferenceId));
                continue;
            }

            var isInput = directions.Contains(Elsa3PropertyDirection.Input);
            var isOutput = directions.Contains(Elsa3PropertyDirection.Output);
            if (isOutput && !isInput)
            {
                // Output projection identity belongs to the activity contract in Elsa 4. Removing the property
                // drops only the Elsa 3 memory id; the producer relationship remains available to consumers
                // through the validated inventory while this pass lowers them.
                property.Activity.AdditionalProperties.Remove(property.PropertyName);
                continue;
            }

            if (!isInput || isOutput)
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ImportDiagnosticCodes.UnresolvableReferenceShape,
                    "Output-only or combined memory-reference shape cannot be represented without an input expression.",
                    "Split the property into an explicit input binding and the activity's declared result projection.",
                    $"{property.JsonPath}.memoryReference",
                    property.MemoryReferenceId));
                continue;
            }

            if (cyclicMemoryIds.Contains(property.MemoryReferenceId))
                continue;

            var variableMatches = variablesById.GetValueOrDefault(property.MemoryReferenceId) ?? [];
            var producerMatches = outputsByMemory.GetValueOrDefault(property.MemoryReferenceId) ?? [];
            if (variableMatches.Length + producerMatches.Length > 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ImportDiagnosticCodes.AmbiguousReference,
                    $"Memory reference '{property.MemoryReferenceId}' has multiple possible declarations or producers.",
                    "Give every variable and activity result a unique Elsa 3 memory id before import.",
                    $"{property.JsonPath}.memoryReference",
                    property.MemoryReferenceId));
                continue;
            }

            if (variableMatches.Length == 1)
            {
                property.Activity.AdditionalProperties[property.PropertyName] = CreateArgument(
                    property.Value,
                    "Variable",
                    JsonSerializer.SerializeToElement(new
                    {
                        referenceKey = variableMatches[0].Id,
                        declaringScopeId = VariableReference.WorkflowScopeId,
                    }));
                continue;
            }

            if (producerMatches.Length == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ImportDiagnosticCodes.DanglingReference,
                    $"Memory reference '{property.MemoryReferenceId}' has no variable declaration or activity result producer.",
                    "Restore the missing producer/declaration or replace the reference with an explicit binding.",
                    $"{property.JsonPath}.memoryReference",
                    property.MemoryReferenceId));
                continue;
            }

            var producer = producerMatches[0];
            var producerContract = inventory.PropertyContracts.FirstOrDefault(contract =>
                contract.Direction == Elsa3PropertyDirection.Output &&
                contract.ActivityNodeId.Equals(producer.ActivityNodeId, StringComparison.Ordinal) &&
                contract.StablePropertyKey.Equals(producer.StablePropertyKey, StringComparison.Ordinal));
            if (property.InputContract is not null && producerContract is not null &&
                !TypesMatch(property.InputContract.Type, producerContract.Type))
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ImportDiagnosticCodes.IncompatibleValue,
                    $"Memory reference '{property.MemoryReferenceId}' connects result type '{producerContract.Type.Alias}' to incompatible input type '{property.InputContract.Type.Alias}'.",
                    "Insert an explicit conversion activity or align the producer and consumer member types.",
                    $"{property.JsonPath}.memoryReference",
                    property.MemoryReferenceId));
                continue;
            }
            if (IsCarriedValue(producer, property))
            {
                property.Activity.AdditionalProperties[property.PropertyName] = CreateArgument(
                    property.Value,
                    "Variable",
                    JsonSerializer.SerializeToElement(new
                    {
                        declaringScopeId = producer.ActivityNodeId,
                        referenceKey = producer.StablePropertyKey,
                    }));
                continue;
            }

            if (!producer.StructuralFrameId.Equals(property.StructuralFrameId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ImportDiagnosticCodes.CrossFrameReference,
                    $"Memory reference '{property.MemoryReferenceId}' crosses structural frame '{producer.StructuralFrameId}' to '{property.StructuralFrameId}'.",
                    "Introduce an explicit container result or carried-value transfer at the subtree boundary.",
                    $"{property.JsonPath}.memoryReference",
                    property.MemoryReferenceId));
                continue;
            }

            if (nodeOrder[producer.ActivityNodeId] >= nodeOrder[property.ActivityNodeId])
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ImportDiagnosticCodes.UnresolvableReferenceShape,
                    $"Memory reference '{property.MemoryReferenceId}' does not identify a prior causal activity result.",
                    "Reorder the producer before the consumer or introduce explicit loop/carried state.",
                    $"{property.JsonPath}.memoryReference",
                    property.MemoryReferenceId));
                continue;
            }

            property.Activity.AdditionalProperties[property.PropertyName] = CreateArgument(
                property.Value,
                "ActivityResult",
                JsonSerializer.SerializeToElement(new
                {
                    producerNodeId = producer.ActivityNodeId,
                    projectionKey = producer.StablePropertyKey,
                    producerScopeId = producer.StructuralFrameId,
                }));
        }

        return diagnostics;
    }

    private void LowerExpression(
        Elsa3WorkflowDefinition definition,
        Elsa3WorkflowDefinitionImportInput input,
        ActivityProperty property,
        IReadOnlyList<Elsa3MemoryOccurrence> outputs,
        IReadOnlyDictionary<string, int> nodeOrder,
        List<Elsa3MigrationDiagnostic> diagnostics)
    {
        var expression = property.Expression!;
        if (expression.Type.Equals("Literal", StringComparison.OrdinalIgnoreCase) ||
            expression.Type.Equals("Object", StringComparison.OrdinalIgnoreCase))
        {
            property.Activity.AdditionalProperties[property.PropertyName] = StripMemoryReference(property.Value);
            return;
        }

        var resultType = ReadType(property.Value);
        var rewrite = expressionRewriter.Rewrite(
            expression.Type,
            expression.Value,
            resultType,
            ambient => ResolveAmbient(definition, property, outputs, nodeOrder, ambient));

        if (!rewrite.Succeeded)
        {
            foreach (var issue in rewrite.Issues)
            {
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    issue.Code,
                    issue.Message,
                    issue.Guidance,
                    $"{property.JsonPath}.expression.value"));
            }
            return;
        }

        property.Activity.AdditionalProperties[property.PropertyName] = CreateArgument(
            property.Value,
            rewrite.Definition!.Language,
            JsonSerializer.SerializeToElement(rewrite.Definition));
    }

    private static Elsa3ExpressionParameterResolution ResolveAmbient(
        Elsa3WorkflowDefinition definition,
        ActivityProperty consumer,
        IReadOnlyList<Elsa3MemoryOccurrence> outputs,
        IReadOnlyDictionary<string, int> nodeOrder,
        Elsa3AmbientReference ambient)
    {
        switch (ambient.Kind)
        {
            case Elsa3AmbientReferenceKind.WorkflowRequest:
            {
                var matches = definition.Inputs
                    .Where(value => value.Name.Equals(ambient.Name, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return matches.Length switch
                {
                    1 => Elsa3ExpressionParameterResolution.Success(
                        new WorkflowRequestExpressionParameterBinding(matches[0].Name)),
                    0 => MissingAmbient(ambient),
                    _ => AmbiguousAmbient(ambient),
                };
            }
            case Elsa3AmbientReferenceKind.Variable:
            {
                var matches = definition.Variables
                    .Where(value => value.Name.Equals(ambient.Name, StringComparison.OrdinalIgnoreCase) ||
                                    value.Id.Equals(ambient.Name, StringComparison.Ordinal))
                    .ToArray();
                return matches.Length switch
                {
                    1 => Elsa3ExpressionParameterResolution.Success(
                        new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, matches[0].Id)),
                    0 => MissingAmbient(ambient),
                    _ => AmbiguousAmbient(ambient),
                };
            }
            case Elsa3AmbientReferenceKind.ActivityResult:
            {
                var stableKey = ToStableKey(ambient.Name);
                var matches = outputs
                    .Where(output => output.StablePropertyKey.Equals(stableKey, StringComparison.Ordinal) &&
                                     !output.ActivityNodeId.Equals(consumer.ActivityNodeId, StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length == 0)
                    return MissingAmbient(ambient);
                if (matches.Length > 1)
                    return AmbiguousAmbient(ambient);

                var producer = matches[0];
                if (IsCarriedValue(producer, consumer))
                {
                    return Elsa3ExpressionParameterResolution.Success(
                        new VariableExpressionParameterBinding(producer.ActivityNodeId, producer.StablePropertyKey));
                }
                if (!producer.StructuralFrameId.Equals(consumer.StructuralFrameId, StringComparison.Ordinal))
                {
                    return Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.CrossFrameReference,
                        $"Activity result '{ambient.Name}' is produced outside the consumer's visible structural frame.",
                        "Introduce an explicit container result at the subtree boundary."));
                }
                if (nodeOrder[producer.ActivityNodeId] >= nodeOrder[consumer.ActivityNodeId])
                {
                    return Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.UnresolvableReferenceShape,
                        $"Activity result '{ambient.Name}' is not produced before the consumer.",
                        "Reorder the producer before the consumer or introduce explicit loop/carried state."));
                }
                return Elsa3ExpressionParameterResolution.Success(
                    new ActivityResultExpressionParameterBinding(producer.ActivityNodeId, producer.StablePropertyKey));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(ambient), ambient.Kind, "Unknown ambient reference kind.");
        }
    }

    private static Elsa3ExpressionParameterResolution MissingAmbient(Elsa3AmbientReference ambient) =>
        Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue(
            Elsa3ImportDiagnosticCodes.DanglingReference,
            $"Ambient {ambient.Kind} reference '{ambient.Name}' has no declaration or producer.",
            "Restore the missing declaration/producer or replace the read with an explicit portable parameter."));

    private static Elsa3ExpressionParameterResolution AmbiguousAmbient(Elsa3AmbientReference ambient) =>
        Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue(
            Elsa3ImportDiagnosticCodes.AmbiguousReference,
            $"Ambient {ambient.Kind} reference '{ambient.Name}' has multiple possible sources.",
            "Rename the declarations/results so the ambient read has exactly one source."));

    private static HashSet<string> FindCycleMemoryIds(
        Elsa3MemoryReferenceInventory inventory,
        IReadOnlyDictionary<string, Elsa3Variable[]> variablesById,
        IReadOnlyDictionary<string, Elsa3MemoryOccurrence[]> outputsByMemory,
        List<Elsa3MigrationDiagnostic> diagnostics,
        Elsa3WorkflowDefinitionImportInput input)
    {
        var candidates = inventory.Occurrences
            .Where(occurrence => occurrence.Direction == Elsa3PropertyDirection.Input && occurrence.Expression is null)
            .Select(consumer =>
            {
                var producers = outputsByMemory.GetValueOrDefault(consumer.MemoryReferenceId) ?? [];
                return variablesById.ContainsKey(consumer.MemoryReferenceId) || producers.Length != 1
                    ? null
                    : new ReferenceEdge(producers[0], consumer);
            })
            .OfType<ReferenceEdge>()
            .Where(edge => edge.Producer.StructuralFrameId.Equals(edge.Consumer.StructuralFrameId, StringComparison.Ordinal))
            .ToArray();

        var cyclicIds = new HashSet<string>(StringComparer.Ordinal);
        ReferenceEdge? firstCycle = null;
        foreach (var edge in candidates)
        {
            if (!HasPath(edge.Consumer.ActivityNodeId, edge.Producer.ActivityNodeId, candidates))
                continue;
            cyclicIds.Add(edge.Consumer.MemoryReferenceId);
            firstCycle ??= edge;
        }

        if (firstCycle is not null)
        {
            var occurrence = firstCycle.Consumer;
            diagnostics.Add(CreateDiagnostic(
                input,
                occurrence,
                Elsa3ImportDiagnosticCodes.UnresolvableReferenceShape,
                $"Memory reference '{occurrence.MemoryReferenceId}' participates in a cyclic activity-result dependency.",
                "Break the cycle with explicit loop/carried state or reorder the value-producing activities.",
                occurrence.JsonPath,
                occurrence.MemoryReferenceId));
        }

        return cyclicIds;
    }

    private static bool HasPath(string start, string target, IReadOnlyCollection<ReferenceEdge> edges)
    {
        var pending = new Stack<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(start);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node))
                continue;
            foreach (var edge in edges.Where(edge => edge.Producer.ActivityNodeId.Equals(node, StringComparison.Ordinal)))
            {
                if (edge.Consumer.ActivityNodeId.Equals(target, StringComparison.Ordinal))
                    return true;
                pending.Push(edge.Consumer.ActivityNodeId);
            }
        }
        return false;
    }

    private static bool IsCarriedValue(Elsa3MemoryOccurrence producer, ActivityProperty consumer) =>
        producer.StablePropertyKey.Equals("key:current-value", StringComparison.Ordinal) &&
        consumer.ActivityPath.StartsWith($"{producer.ActivityPath}/", StringComparison.Ordinal);

    private static IEnumerable<ActivityProperty> CollectProperties(
        Elsa3Activity root,
        Elsa3MemoryReferenceInventory inventory)
    {
        var nodes = inventory.Nodes.ToDictionary(node => node.ActivityNodeId, StringComparer.Ordinal);
        return Visit(root, "$.root");

        IEnumerable<ActivityProperty> Visit(Elsa3Activity activity, string jsonPath)
        {
            var nodeId = !string.IsNullOrWhiteSpace(activity.NodeId) ? activity.NodeId : activity.Id;
            var node = nodes[nodeId];
            foreach (var (propertyName, value) in activity.AdditionalProperties.ToArray())
            {
                var propertyPath = $"{jsonPath}{FormatJsonProperty(propertyName)}";
                var occurrences = inventory.Occurrences
                    .Where(occurrence => occurrence.ActivityNodeId.Equals(nodeId, StringComparison.Ordinal) &&
                                         occurrence.JsonPath.Equals($"{propertyPath}.memoryReference", StringComparison.Ordinal))
                    .ToArray();
                var inputContract = inventory.PropertyContracts.FirstOrDefault(contract =>
                    contract.Direction == Elsa3PropertyDirection.Input &&
                    contract.ActivityNodeId.Equals(nodeId, StringComparison.Ordinal) &&
                    contract.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
                TryReadTypedValue(value, out var memoryId, out var expression);
                var stablePropertyKey = occurrences
                    .FirstOrDefault(occurrence => occurrence.Direction == Elsa3PropertyDirection.Input)?.StablePropertyKey
                    ?? occurrences.FirstOrDefault()?.StablePropertyKey
                    ?? ToStableKey(propertyName);
                yield return new ActivityProperty(
                    activity,
                    nodeId,
                    node.ActivityPath,
                    node.StructuralFrameId,
                    propertyName,
                    stablePropertyKey,
                    propertyPath,
                    value,
                    memoryId,
                    expression,
                    inputContract,
                    occurrences);
            }

            var children = activity.Activities ?? [];
            for (var index = 0; index < children.Count; index++)
            {
                foreach (var property in Visit(children[index], $"{jsonPath}.activities[{index}]"))
                    yield return property;
            }
        }
    }

    private static void TryReadTypedValue(JsonElement value, out string? memoryId, out Elsa3Expression? expression)
    {
        memoryId = null;
        expression = null;
        if (value.ValueKind != JsonValueKind.Object)
            return;
        if (value.TryGetProperty("memoryReference", out var memory) &&
            memory.ValueKind == JsonValueKind.Object &&
            memory.TryGetProperty("id", out var id))
            memoryId = id.ValueKind == JsonValueKind.String ? id.GetString() : string.Empty;
        if (value.TryGetProperty("expression", out var expressionElement) &&
            expressionElement.ValueKind == JsonValueKind.Object &&
            expressionElement.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            expressionElement.TryGetProperty("value", out var expressionValue) &&
            expressionValue.ValueKind == JsonValueKind.String)
        {
            expression = new Elsa3Expression
            {
                Type = type.GetString() ?? string.Empty,
                Value = expressionValue.GetString() ?? string.Empty,
            };
        }
    }

    private static TypeReference ReadType(JsonElement value)
    {
        if (!value.TryGetProperty("typeName", out var typeName) || typeName.ValueKind != JsonValueKind.String)
            return new TypeReference("Object");
        var alias = typeName.GetString() ?? "Object";
        var comma = alias.IndexOf(',');
        if (comma >= 0)
            alias = alias[..comma];
        var dot = alias.LastIndexOf('.');
        if (dot >= 0)
            alias = alias[(dot + 1)..];
        return new TypeReference(alias);
    }

    private static bool TypesMatch(TypeReference left, TypeReference right) =>
        left.Alias.Equals(right.Alias, StringComparison.OrdinalIgnoreCase) &&
        left.CollectionKind == right.CollectionKind;

    private static JsonElement StripMemoryReference(JsonElement value)
    {
        var node = JsonNode.Parse(value.GetRawText())?.AsObject()
            ?? throw new ArgumentException("Elsa 3 typed value must be a JSON object.", nameof(value));
        node.Remove("memoryReference");
        return JsonSerializer.SerializeToElement(node);
    }

    private static JsonElement CreateArgument(JsonElement original, string expressionType, JsonElement expressionValue)
    {
        var node = JsonNode.Parse(original.GetRawText())?.AsObject()
            ?? throw new ArgumentException("Elsa 3 typed value must be a JSON object.", nameof(original));
        node.Remove("memoryReference");
        node["expression"] = new JsonObject
        {
            ["type"] = expressionType,
            ["value"] = JsonNode.Parse(expressionValue.GetRawText()),
        };
        return JsonSerializer.SerializeToElement(node);
    }

    private static Elsa3MigrationDiagnostic CreateDiagnostic(
        Elsa3WorkflowDefinitionImportInput input,
        ActivityProperty property,
        string code,
        string message,
        string guidance,
        string path,
        string? memoryReferenceId = null) =>
        CreateDiagnostic(
            input,
            property.ActivityNodeId,
            property.ActivityPath,
            property.StablePropertyKey,
            code,
            message,
            guidance,
            path,
            memoryReferenceId);

    private static Elsa3MigrationDiagnostic CreateDiagnostic(
        Elsa3WorkflowDefinitionImportInput input,
        Elsa3MemoryOccurrence occurrence,
        string code,
        string message,
        string guidance,
        string path,
        string? memoryReferenceId = null) =>
        CreateDiagnostic(
            input,
            occurrence.ActivityNodeId,
            occurrence.ActivityPath,
            occurrence.StablePropertyKey,
            code,
            message,
            guidance,
            path,
            memoryReferenceId);

    private static Elsa3MigrationDiagnostic CreateDiagnostic(
        Elsa3WorkflowDefinitionImportInput input,
        string activityNodeId,
        string activityPath,
        string stablePropertyKey,
        string code,
        string message,
        string guidance,
        string path,
        string? memoryReferenceId)
    {
        var metadata = input.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["InputKind"] = input.InputKind.ToString();
        metadata["DefinitionId"] = input.Definition.DefinitionId;
        metadata["ActivityNodeId"] = activityNodeId;
        metadata["ActivityPath"] = activityPath;
        metadata["StablePropertyKey"] = stablePropertyKey;
        if (input.SourceName is not null)
            metadata["SourceName"] = input.SourceName;
        if (memoryReferenceId is not null)
            metadata["MemoryReferenceId"] = memoryReferenceId;

        return new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            code,
            message,
            path,
            guidance,
            metadata);
    }

    private static string ToStableKey(string name)
    {
        var kebab = string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $"-{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));
        return $"key:{kebab}";
    }

    private static string FormatJsonProperty(string propertyName)
    {
        if (propertyName.Length > 0 &&
            (char.IsLetter(propertyName[0]) || propertyName[0] is '_' or '$') &&
            propertyName.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$'))
            return $".{propertyName}";
        var escaped = propertyName.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
        return $"['{escaped}']";
    }

    private sealed record ActivityProperty(
        Elsa3Activity Activity,
        string ActivityNodeId,
        string ActivityPath,
        string StructuralFrameId,
        string PropertyName,
        string StablePropertyKey,
        string JsonPath,
        JsonElement Value,
        string? MemoryReferenceId,
        Elsa3Expression? Expression,
        Elsa3PropertyContract? InputContract,
        IReadOnlyList<Elsa3MemoryOccurrence> Occurrences);

    private sealed record ReferenceEdge(Elsa3MemoryOccurrence Producer, Elsa3MemoryOccurrence Consumer);

}
