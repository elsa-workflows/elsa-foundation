using System.Text.Json;
using Elsa3.Mapping.Conversion;
using System.Text.Json.Nodes;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Internal;
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
        IReadOnlyDictionary<string, string> diagnosticMetadata,
        IReadOnlyDictionary<Elsa3Activity, string>? jsonPaths = null,
        Func<Elsa3Activity, string, string>? sourceNameOf = null,
        Func<Elsa3Activity, string, bool>? isScriptBody = null,
        IReadOnlySet<string>? containerVariableIds = null,
        IReadOnlyDictionary<string, string>? memoryAliases = null,
        bool isReusable = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(diagnosticMetadata);
        var input = new DiagnosticScope(diagnosticMetadata, definition.DefinitionId);

        var diagnostics = new List<Elsa3MigrationDiagnostic>();
        var properties = CollectProperties(definition.Root, inventory, jsonPaths, sourceNameOf ?? ((_, name) => name)).ToArray();
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

        var containerVariables = containerVariableIds ?? new HashSet<string>(StringComparer.Ordinal);
        var accessors = new AccessorScope(isReusable, ActivityAccessorNames(definition.Root));
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
                !ValueFits(property, property.InputContract.Type))
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
                if (isScriptBody?.Invoke(property.Activity, property.PropertyName) == true)
                    LowerScript(definition, input, property, outputs, nodeOrder, accessors, diagnostics);
                else
                    LowerExpression(definition, input, property, outputs, nodeOrder, accessors, diagnostics);
                continue;
            }

            if (property.MemoryReferenceId is null)
                continue;

            if (isReusable)
            {
                // A captured output writes the value; anything else bound to a memory reference reads it.
                var captures = directions.Contains(Elsa3PropertyDirection.Output);
                diagnostics.Add(CreateDiagnostic(
                    input,
                    property,
                    Elsa3ActivityDiagnosticCodes.ReusableValueReadUnsupported,
                    captures
                        ? $"Output '{property.PropertyName}' of reusable workflow node '{property.ActivityNodeId}' is captured into a variable, which nothing in an Elsa 4 graph activity body can read yet."
                        : $"Property '{property.PropertyName}' of reusable workflow node '{property.ActivityNodeId}' reads a variable or another activity's result, which an Elsa 4 graph activity body cannot reach yet.",
                    captures ? Elsa3ReusableGuidance.Capture : ReusableValueReadGuidance,
                    $"{property.JsonPath}.memoryReference",
                    property.MemoryReferenceId));
                continue;
            }

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
                LowerOutputCapture(input, property, variablesById, containerVariables, diagnostics);
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

            // A memory slot a synthesized workflow variable stands in for is read through that variable (plan D-13).
            var variableMatches = variablesById.GetValueOrDefault(
                memoryAliases?.GetValueOrDefault(property.MemoryReferenceId) ?? property.MemoryReferenceId) ?? [];
            var producerMatches = outputsByMemory.GetValueOrDefault(property.MemoryReferenceId) ?? [];
            // A captured output writes its variable (plan D-17), so a variable read is unambiguous however many
            // activities write it; without a variable, the memory slot must have one producer.
            if (variableMatches.Length > 1 || variableMatches.Length == 0 && producerMatches.Length > 1)
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
                if (producerMatches.FirstOrDefault(IsLoopVariable) is { } loop)
                {
                    LowerLoopVariableRead(input, property, loop, diagnostics);
                    continue;
                }

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
                !TypesMatch(producerContract.Type, property.InputContract.Type))
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
        DiagnosticScope input,
        ActivityProperty property,
        IReadOnlyList<Elsa3MemoryOccurrence> outputs,
        IReadOnlyDictionary<string, int> nodeOrder,
        AccessorScope accessors,
        List<Elsa3MigrationDiagnostic> diagnostics)
    {
        var expression = property.Expression!;
        // Literal, Object and Secret values are data, not code: they carry no Elsa 3 reads to rewrite. A Secret value is
        // judged by the credential-literal rule as it stands.
        if (expression.Type.Equals("Literal", StringComparison.OrdinalIgnoreCase) ||
            expression.Type.Equals("Object", StringComparison.OrdinalIgnoreCase) ||
            expression.Type.Equals("Secret", StringComparison.OrdinalIgnoreCase))
        {
            property.Activity.AdditionalProperties[property.PropertyName] = StripMemoryReference(property.Value);
            return;
        }

        var resultType = ExpressionResultType(property);
        var rewrite = expressionRewriter.Rewrite(
            expression.Type,
            expression.Value,
            resultType,
            ambient => ResolveAmbient(definition, property, outputs, nodeOrder, accessors, ambient));

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

    /// <summary>
    /// An output captured into a variable (plan D-17). Elsa 3 output targets are variables; Elsa 4 captures a leaf
    /// result into a workflow-scope variable through a <c>Variable</c> output binding. A capture into a container-scope
    /// variable cannot be expressed; a loop's current value has no Elsa 4 output (its readers bind to the loop
    /// variable); a memory slot that is no declared variable is read through the producer's result.
    /// </summary>
    private static void LowerOutputCapture(
        DiagnosticScope input,
        ActivityProperty property,
        IReadOnlyDictionary<string, Elsa3Variable[]> variablesById,
        IReadOnlySet<string> containerVariableIds,
        List<Elsa3MigrationDiagnostic> diagnostics)
    {
        var memoryId = property.MemoryReferenceId!;
        var isLoopVariable = property.Occurrences.Any(IsLoopVariable);
        if (!isLoopVariable && variablesById.ContainsKey(memoryId))
        {
            property.Activity.AdditionalProperties[property.PropertyName] = CreateArgument(
                property.Value,
                "Variable",
                JsonSerializer.SerializeToElement(new
                {
                    referenceKey = memoryId,
                    declaringScopeId = VariableReference.WorkflowScopeId,
                }));
            return;
        }

        if (!isLoopVariable && containerVariableIds.Contains(memoryId))
        {
            diagnostics.Add(CreateDiagnostic(
                input,
                property,
                Elsa3ActivityDiagnosticCodes.OutputNotRepresentable,
                $"Output '{property.PropertyName}' is captured into container variable '{memoryId}'; Elsa 4 captures outputs into workflow variables only.",
                "Declare the variable on the workflow instead of the container before converting.",
                $"{property.JsonPath}.memoryReference",
                memoryId));
            return;
        }

        property.Activity.AdditionalProperties.Remove(property.PropertyName);
    }

    /// <summary>
    /// A read of a workflow variable a loop captures its current value into (plan D-17): inside the loop body it is
    /// the loop's scoped variable; outside, nothing writes it in Elsa 4.
    /// </summary>
    private static void LowerLoopVariableRead(
        DiagnosticScope input,
        ActivityProperty property,
        Elsa3MemoryOccurrence loop,
        List<Elsa3MigrationDiagnostic> diagnostics)
    {
        if (IsCarriedValue(loop, property))
        {
            property.Activity.AdditionalProperties[property.PropertyName] = CreateArgument(
                property.Value,
                "Variable",
                JsonSerializer.SerializeToElement(new
                {
                    declaringScopeId = loop.ActivityNodeId,
                    referenceKey = loop.StablePropertyKey,
                }));
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            input,
            property,
            Elsa3ActivityDiagnosticCodes.OutputNotRepresentable,
            $"Variable '{property.MemoryReferenceId}' holds loop '{loop.ActivityNodeId}''s current value, which Elsa 4 keeps only inside the loop body.",
            "Read the value inside the loop, or copy it into another variable there.",
            $"{property.JsonPath}.memoryReference",
            property.MemoryReferenceId));
    }

    private static bool IsLoopVariable(Elsa3MemoryOccurrence occurrence) =>
        occurrence.Direction == Elsa3PropertyDirection.Output &&
        Elsa3LoopVariableReferencesRewrite.LoopVariableKeys.Contains(occurrence.StablePropertyKey);

    /// <summary>The Elsa 4 RunJavaScript input that carries the values a converted script reads.</summary>
    internal const string ScriptArgumentsKey = "arguments";

    /// <summary>
    /// A RunJavaScript body (plan D-16). Elsa 3 stores it as a literal and runs it with its host functions; Elsa 4 runs
    /// it as a function body that sees only <c>args</c>. The body is rewritten in script mode: its reads become
    /// <c>args.&lt;name&gt;</c>, bound by an <c>arguments</c> object expression, and its completion value is returned.
    /// </summary>
    private void LowerScript(
        Elsa3WorkflowDefinition definition,
        DiagnosticScope input,
        ActivityProperty property,
        IReadOnlyList<Elsa3MemoryOccurrence> outputs,
        IReadOnlyDictionary<string, int> nodeOrder,
        AccessorScope accessors,
        List<Elsa3MigrationDiagnostic> diagnostics)
    {
        var expression = property.Expression!;
        if (!expression.Type.Equals("Literal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(expression.Value))
        {
            LowerExpression(definition, input, property, outputs, nodeOrder, accessors, diagnostics);
            return;
        }

        var rewrite = expressionRewriter.Rewrite(
            "JavaScript",
            expression.Value,
            new TypeReference("Object"),
            ambient => ResolveAmbient(definition, property, outputs, nodeOrder, accessors, ambient),
            Elsa3JavaScriptMode.Script);
        if (!rewrite.Succeeded)
        {
            foreach (var issue in rewrite.Issues)
                diagnostics.Add(CreateDiagnostic(input, property, issue.Code, issue.Message, issue.Guidance, $"{property.JsonPath}.expression.value"));
            return;
        }

        var resultCaptured = outputs.Any(output => output.ActivityNodeId.Equals(property.ActivityNodeId, StringComparison.Ordinal));
        if (rewrite.CompletionValueLost && resultCaptured)
        {
            diagnostics.Add(CreateDiagnostic(
                input,
                property,
                Elsa3ImportDiagnosticCodes.DynamicAccessor,
                "The script's result is captured, but the script ends with a statement, so its Elsa 3 completion value has no Elsa 4 return.",
                "End the script with the expression whose value it produces.",
                $"{property.JsonPath}.expression.value"));
            return;
        }

        var definitionResult = rewrite.Definition!;
        property.Activity.AdditionalProperties[property.PropertyName] = StripMemoryReference(
            Elsa3Literal(property.Value, definitionResult.Source));
        if (definitionResult.Parameters.Count == 0)
            return;

        var members = definitionResult.Parameters.Keys
            .Order(StringComparer.Ordinal)
            .Select(name => $"{JsonSerializer.Serialize(name)}: {ArgumentAccess(name)}");
        var arguments = new ExpressionDefinition(
            "JavaScript",
            $"({{ {string.Join(", ", members)} }})",
            new TypeReference("Object"),
            definitionResult.Parameters,
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        property.Activity.AdditionalProperties[ScriptArgumentsKey] = JsonSerializer.SerializeToElement(new JsonObject
        {
            ["typeName"] = "Object",
            ["expression"] = new JsonObject
            {
                ["type"] = "JavaScript",
                ["value"] = JsonNode.Parse(JsonSerializer.Serialize(arguments)),
            },
        });
    }

    private static JsonElement Elsa3Literal(JsonElement original, string value)
    {
        var node = JsonNode.Parse(original.GetRawText())?.AsObject() ?? new JsonObject();
        node["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = value };
        return JsonSerializer.SerializeToElement(node);
    }

    private static string ArgumentAccess(string name) =>
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] is '_' or '$') &&
        name.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$')
            ? $"args.{name}"
            : $"args[{JsonSerializer.Serialize(name)}]";

    private static Elsa3ExpressionParameterResolution ResolveAmbient(
        Elsa3WorkflowDefinition definition,
        ActivityProperty consumer,
        IReadOnlyList<Elsa3MemoryOccurrence> outputs,
        IReadOnlyDictionary<string, int> nodeOrder,
        AccessorScope accessors,
        Elsa3AmbientReference ambient)
    {
        // A graph body node's bindings resolve against the started workflow, never the graph (spec 246 D-8). Generated
        // get{Name}() accessors resolve through here too, so they are refused alike.
        if (accessors.IsReusable && ambient.Kind is Elsa3AmbientReferenceKind.WorkflowRequest or Elsa3AmbientReferenceKind.Variable or Elsa3AmbientReferenceKind.ActivityResult)
            return Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ActivityDiagnosticCodes.ReusableValueReadUnsupported,
                $"The reusable workflow body reads {ambient.Kind} '{ambient.Name}', which an Elsa 4 graph activity body cannot reach yet.",
                ReusableValueReadGuidance));

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
                if (matches.Length == 1 &&
                    outputs.FirstOrDefault(output => IsLoopVariable(output) &&
                                                     output.MemoryReferenceId.Equals(matches[0].Id, StringComparison.Ordinal)) is { } loop)
                {
                    return IsCarriedValue(loop, consumer)
                        ? Elsa3ExpressionParameterResolution.Success(new VariableExpressionParameterBinding(loop.ActivityNodeId, loop.StablePropertyKey))
                        : Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue(
                            Elsa3ActivityDiagnosticCodes.OutputNotRepresentable,
                            $"Variable '{ambient.Name}' holds loop '{loop.ActivityNodeId}''s current value, which Elsa 4 keeps only inside the loop body.",
                            "Read the value inside the loop, or copy it into another variable there."));
                }

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
            case Elsa3AmbientReferenceKind.GeneratedAccessor:
                return ResolveGeneratedAccessor(definition, consumer, outputs, nodeOrder, accessors, ambient.Name);
            default:
                throw new ArgumentOutOfRangeException(nameof(ambient), ambient.Kind, "Unknown ambient reference kind.");
        }
    }

    /// <summary>
    /// Resolves <c>get{name}()</c> to the declaration Elsa 3 generated it for, in Elsa 3's registration order: an
    /// activity output accessor shadows everything (and is not lowered), then a workflow input (verbatim name; in a
    /// reusable run Elsa 3 declares inputs as variables instead), then a variable (Pascal-cased name). Spec 247 R-2.
    /// </summary>
    private static Elsa3ExpressionParameterResolution ResolveGeneratedAccessor(
        Elsa3WorkflowDefinition definition,
        ActivityProperty consumer,
        IReadOnlyList<Elsa3MemoryOccurrence> outputs,
        IReadOnlyDictionary<string, int> nodeOrder,
        AccessorScope accessors,
        string name)
    {
        Elsa3ExpressionParameterResolution Resolve(Elsa3AmbientReferenceKind kind, string declared) =>
            ResolveAmbient(definition, consumer, outputs, nodeOrder, accessors, new Elsa3AmbientReference(kind, declared));

        if (accessors.ActivityNames.Any(activity =>
                name.Length > activity.Length + "From".Length && name.EndsWith($"From{activity}", StringComparison.Ordinal)))
        {
            return Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                $"JavaScript calls 'get{name}', which Elsa 3 generated for an activity output and which shadows any variable or input of that name.",
                "Capture the activity output into a variable and read it with get{Variable}() before import."));
        }

        if (!accessors.IsReusable)
        {
            var inputs = definition.Inputs.Where(value => value.Name.Equals(name, StringComparison.Ordinal)).ToArray();
            if (inputs.Length > 1)
                return AmbiguousAmbient(new Elsa3AmbientReference(Elsa3AmbientReferenceKind.GeneratedAccessor, name));
            if (inputs.Length == 1)
                return Resolve(Elsa3AmbientReferenceKind.WorkflowRequest, inputs[0].Name);
        }

        var variables = definition.Variables.Where(value => HasAccessor(value.Name, name)).ToArray();
        var inputVariables = accessors.IsReusable
            ? definition.Inputs.Where(value => HasAccessor(value.Name, name)).ToArray()
            : [];
        if (variables.Length + inputVariables.Length > 1)
            return AmbiguousAmbient(new Elsa3AmbientReference(Elsa3AmbientReferenceKind.GeneratedAccessor, name));
        if (variables.Length == 1)
            return Resolve(Elsa3AmbientReferenceKind.Variable, variables[0].Id);
        if (inputVariables.Length == 1)
            return Resolve(Elsa3AmbientReferenceKind.WorkflowRequest, inputVariables[0].Name);

        return Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue(
            Elsa3ImportDiagnosticCodes.ForbiddenCapability,
            $"JavaScript calls unknown or unapproved host function 'get{name}'.",
            "Declare the variable or workflow input it reads, or replace the call with deterministic language syntax, before import."));

        static bool HasAccessor(string declared, string accessor) =>
            Elsa3JavaScriptNames.IsAccessorName(declared) &&
            Elsa3JavaScriptNames.Pascalize(declared).Equals(accessor, StringComparison.Ordinal);
    }

    /// <summary>The Pascal-cased names of the definition's named activities, as Elsa 3 used them in output accessors.</summary>
    private static IReadOnlyList<string> ActivityAccessorNames(Elsa3Activity? root)
    {
        var names = new List<string>();
        Collect(root);
        return names;

        void Collect(Elsa3Activity? activity)
        {
            if (activity is null)
                return;
            if (!string.IsNullOrWhiteSpace(activity.Name))
                names.Add(Elsa3JavaScriptNames.Pascalize(activity.Name));
            foreach (var child in activity.Activities ?? [])
                Collect(child);
        }
    }

    private const string ReusableValueReadGuidance = Elsa3ReusableGuidance.ValueRead;

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
        DiagnosticScope input)
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

    /// <summary>
    /// A loop's current value read inside its body: Elsa 4 exposes it as the loop's scoped variable (<c>index</c>,
    /// <c>currentItem</c>, <c>currentIndex</c>) declared by the loop node.
    /// </summary>
    private static bool IsCarriedValue(Elsa3MemoryOccurrence producer, ActivityProperty consumer) =>
        (producer.StablePropertyKey.Equals("key:current-value", StringComparison.Ordinal) ||
         Elsa3LoopVariableReferencesRewrite.LoopVariableKeys.Contains(producer.StablePropertyKey)) &&
        consumer.ActivityPath.StartsWith($"{producer.ActivityPath}/", StringComparison.Ordinal);

    private static IEnumerable<ActivityProperty> CollectProperties(
        Elsa3Activity root,
        Elsa3MemoryReferenceInventory inventory,
        IReadOnlyDictionary<Elsa3Activity, string>? jsonPaths,
        Func<Elsa3Activity, string, string> sourceNameOf)
    {
        var nodes = inventory.Nodes.ToDictionary(node => node.ActivityNodeId, StringComparer.Ordinal);
        return Visit(root, "$.root");

        IEnumerable<ActivityProperty> Visit(Elsa3Activity activity, string jsonPath)
        {
            var nodeId = !string.IsNullOrWhiteSpace(activity.NodeId) ? activity.NodeId : activity.Id;
            var node = nodes[nodeId];
            foreach (var (propertyName, value) in activity.AdditionalProperties.ToArray())
            {
                var propertyPath = $"{jsonPath}{FormatJsonProperty(sourceNameOf(activity, propertyName))}";
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
                    ?? inputContract?.StablePropertyKey
                    ?? ToStableKey(sourceNameOf(activity, propertyName));
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
                foreach (var property in Visit(children[index], jsonPaths?.GetValueOrDefault(children[index]) ?? $"{jsonPath}.activities[{index}]"))
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

    /// <summary>
    /// The result type a lowered expression declares. Publication requires it to equal the member's type verbatim. A JSON
    /// member (<c>JsonElement</c>, <c>Object</c>, <c>Any</c>) accepts any expression value, so the expression declares
    /// the member's own type (spec 256: an Elsa 3 <c>Object</c> response body binds Elsa 4's <c>JsonElement?</c>). A typed
    /// member keeps the declared Elsa 3 type, so publication still refuses a real mismatch.
    /// </summary>
    private static TypeReference ExpressionResultType(ActivityProperty property) =>
        property.InputContract?.Type is { } member && IsUntyped(member, SimpleName(member.Alias))
            ? member
            : ReadType(property.Value);

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

    /// <summary>
    /// The declared Elsa 3 type in the Elsa 4 shape normalization has already put the value in (spec 248 R-4): Elsa 3's
    /// generic list syntax (<c>List&lt;String&gt;</c>) is a list of its element, and the Elsa 3 names below are what
    /// their Elsa 4 member holds. <c>HttpStatusCode</c> is an integer because the narrowed contract (R5) turned a status
    /// name into its number and refused an expression declared as the enum.
    /// </summary>
    private static TypeReference ReadNormalizedType(JsonElement value)
    {
        if (value.TryGetProperty("typeName", out var typeName) && typeName.ValueKind == JsonValueKind.String &&
            typeName.GetString() is { } token && token.IndexOf('<') is var open and > 0 && token.EndsWith('>') &&
            SimpleName(token[..open]) is "List" or "IList" or "ICollection" or "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" &&
            token[(open + 1)..^1] is var element && element.IndexOfAny([',', '<']) < 0)
            return new TypeReference(SimpleName(element), CollectionKind.List);

        var declared = ReadType(value);
        return Elsa3TypeShapes.GetValueOrDefault(declared.Alias) ?? declared;
    }

    private static readonly IReadOnlyDictionary<string, TypeReference> Elsa3TypeShapes = new Dictionary<string, TypeReference>(StringComparer.Ordinal)
    {
        ["HttpStatusCode"] = new("Int32"),
        ["HttpHeaders"] = new("String[]", CollectionKind.Dictionary),
        ["ObjectDictionary"] = new("Object", CollectionKind.Dictionary),
    };

    /// <summary>
    /// Whether a value of type <paramref name="value"/> may bind a member of type <paramref name="member"/>. Aliases are
    /// compared by simple name (Elsa 3 writes <c>String</c>, the catalog <c>System.String</c> or
    /// <c>System.Text.Json.JsonElement?</c>). An untyped side (<c>Object</c>, <c>JsonElement</c>, <c>Any</c>) matches
    /// anything, or any element of the same collection kind, and a String value may bind a scalar member, which Elsa 4
    /// converts from text (a URI, a number, an enum).
    /// </summary>
    private static bool TypesMatch(TypeReference value, TypeReference member)
    {
        var valueName = SimpleName(value.Alias);
        var memberName = SimpleName(member.Alias);
        if (IsUntyped(value, valueName) || IsUntyped(member, memberName))
            return true;
        if ((IsUntypedName(valueName) || IsUntypedName(memberName)) && value.CollectionKind == member.CollectionKind)
            return true;
        if (valueName.Equals("String", StringComparison.OrdinalIgnoreCase) &&
            value.CollectionKind == CollectionKind.Single &&
            member.CollectionKind == CollectionKind.Single)
            return true;
        return valueName.Equals(memberName, StringComparison.OrdinalIgnoreCase) && value.CollectionKind == member.CollectionKind;
    }

    /// <summary>
    /// Whether the property's declared value may bind the member. A literal must also parse as the member's scalar type
    /// when that type is a number, boolean, time span, GUID or date; other literals (an enum name, a URI) are text
    /// Elsa 4 converts.
    /// </summary>
    private static bool ValueFits(ActivityProperty property, TypeReference member)
    {
        var declared = ReadNormalizedType(property.Value);
        if (property.Expression is null ||
            !property.Expression.Type.Equals("Literal", StringComparison.OrdinalIgnoreCase) ||
            !SimpleName(declared.Alias).Equals("String", StringComparison.OrdinalIgnoreCase) ||
            member.CollectionKind != CollectionKind.Single)
            return TypesMatch(declared, member);

        var text = property.Expression.Value;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return SimpleName(member.Alias) switch
        {
            "Int16" or "Int32" or "Int64" or "Byte" => long.TryParse(text, System.Globalization.NumberStyles.Integer, invariant, out _),
            "Single" or "Double" or "Decimal" => decimal.TryParse(text, System.Globalization.NumberStyles.Number, invariant, out _),
            "Boolean" => bool.TryParse(text, out _),
            "TimeSpan" => TimeSpan.TryParse(text, invariant, out _),
            "Guid" => Guid.TryParse(text, out _),
            "DateTime" or "DateTimeOffset" => DateTimeOffset.TryParse(text, invariant, System.Globalization.DateTimeStyles.None, out _),
            _ => TypesMatch(declared, member)
        };
    }

    private static bool IsUntyped(TypeReference type, string simpleName) =>
        type.CollectionKind == CollectionKind.Single && IsUntypedName(simpleName);

    private static bool IsUntypedName(string simpleName) =>
        simpleName is "Object" or "object" or "JsonElement" or "JsonNode" or "Any" or "ExpandoObject";

    private static string SimpleName(string alias)
    {
        var name = alias.Trim().TrimEnd('?');
        var generic = name.IndexOfAny(['`', '<']);
        if (generic >= 0)
            name = name[..generic];
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

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
        DiagnosticScope input,
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
        DiagnosticScope input,
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
        DiagnosticScope input,
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
        metadata["DefinitionId"] = input.DefinitionId;
        metadata["ActivityNodeId"] = activityNodeId;
        metadata["ActivityPath"] = activityPath;
        metadata["StablePropertyKey"] = stablePropertyKey;
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

    /// <summary>The caller's diagnostic metadata (input kind, source name, plan item, …) and the definition id.</summary>
    private sealed record DiagnosticScope(IReadOnlyDictionary<string, string> Metadata, string DefinitionId);

    private sealed record ReferenceEdge(Elsa3MemoryOccurrence Producer, Elsa3MemoryOccurrence Consumer);

    /// <summary>What decides which Elsa 3 generated accessors exist: the run mode and the named activities.</summary>
    private sealed record AccessorScope(bool IsReusable, IReadOnlyList<string> ActivityNames);

}
