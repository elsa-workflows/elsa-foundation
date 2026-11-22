using Elsa.Activities.Design.Core.Contracts;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Services;

/// <summary>
/// Publish-time checks of a definition's step writes (spec 1231 FR-024, FR-026, FR-029) and the facts the
/// concurrent-write rule counts (FR-028):
/// <list type="bullet">
/// <item>A variable-reference input must be bound, by a <c>Variable</c> expression, to a variable declared in a scope
/// visible to the node. A loop item or index is not a declared variable yet, so it cannot be bound.</item>
/// <item>A node's may-write set is its writable bound references; an activity type marked
/// <c>[AccessesUndeclaredVariables]</c> may write every visible variable.</item>
/// <item>A script activity's may-write set is what its script body writes or lets escape, resolved as publish resolves it;
/// a computed or opaque write means it may write every visible variable (FR-019, FR-020a).</item>
/// <item>A may-write set that overlaps one of the node's own output captures is a warning: the capture applies last.</item>
/// </list>
/// </summary>
internal static class StepVariableWriteValidation
{
    public const string ReferenceRuleId = "ValueFlow/VariableReference";
    public const string OverlapRuleId = "ValueFlow/StepWriteOverlapsCapture";

    public static async Task<(StepVariableWriteFacts Facts, IReadOnlyList<ValidationError> Errors)> AnalyzeAsync(
        ActivityNode root,
        IReadOnlyCollection<VariableDefinition> workflowVariables,
        IActivityStructureService structures,
        IActivityDefinitionLookup catalog,
        CancellationToken cancellationToken,
        ScriptVariableReadResolver? scriptReads = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(catalog);
        var errors = new List<ValidationError>();
        var byNode = new Dictionary<string, StepVariableWriters>(StringComparer.Ordinal);
        var versions = new Dictionary<string, IActivityDefinitionVersion?>(StringComparer.Ordinal);
        var workflowKeys = workflowVariables.Select(variable => variable.ReferenceKey).ToHashSet(StringComparer.Ordinal);
        ScopedVariableVisibility? visibility = null;

        var pending = new Stack<(ActivityNode Node, IReadOnlyList<ActivityNode> Ancestors)>();
        pending.Push((root, []));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (node, ancestors) = pending.Pop();
            var below = ancestors.Append(node).ToArray();
            foreach (var child in structures.ProjectChildren(node).SelectMany(slot => slot.Activities))
                pending.Push((child, below));

            if (node.Intrinsic is not null || string.IsNullOrWhiteSpace(node.ActivityVersionId) || string.IsNullOrWhiteSpace(node.NodeId))
                continue;
            if (!versions.TryGetValue(node.ActivityVersionId, out var version))
                versions[node.ActivityVersionId] = version = await catalog.FindVersion(node.ActivityVersionId, cancellationToken);
            if (version is null)
                continue;

            var everyVisible = VariableAccessDesignFacet.AccessesUndeclaredVariables(version.DesignFacets);
            var stateFacet = version.DesignFacets.FirstOrDefault(facet => facet.Kind == WorkflowStateDesignFacet.Kind);
            var usesState = stateFacet is not null;
            var targets = new List<StepVariableWriteTarget>();
            foreach (var input in version.Inputs.Where(input => !string.IsNullOrWhiteSpace(input.VariableAccess)))
            {
                var writable = StringComparer.Ordinal.Equals(input.VariableAccess, InputVariableAccess.ReadWrite);
                var path = $"{node.NodeId}/inputs/{input.ReferenceKey}";
                var state = node.Inputs?.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ReferenceKey, input.ReferenceKey));
                if (state is null || !state.IsBound())
                {
                    errors.Add(new ValidationError(path, ReferenceRuleId,
                        $"Input '{input.Name}' of node '{node.NodeId}' is a variable reference and must be bound to a declared variable."));
                    continue;
                }

                if (!StringComparer.OrdinalIgnoreCase.Equals(state.Value!.ExpressionType, InputVariableAccess.ExpressionType) ||
                    !VariableReference.TryParse(state.Value.Value, out var reference) || reference is null)
                {
                    errors.Add(new ValidationError(path, ReferenceRuleId,
                        $"Input '{input.Name}' of node '{node.NodeId}' is a variable reference and must be bound to a declared variable, not to a '{state.Value.ExpressionType}' expression."));
                    continue;
                }

                var scopeId = UnorderedVariableWriteAnalyzer.NormalizeScopeId(reference.DeclaringScopeId);
                var declared = Find(scopeId, reference.ReferenceKey, ancestors, workflowVariables, structures);
                if (declared is null)
                {
                    errors.Add(new ValidationError(path, ReferenceRuleId,
                        $"Input '{input.Name}' of node '{node.NodeId}' is bound to variable '{reference.ReferenceKey}' in scope '{scopeId}', which is not declared in a scope visible to the node (a loop item or index is not a declared variable, and is read-only). A step never creates a variable."));
                    continue;
                }

                // The activity reads and writes the variable as the input's value type; a variable of another type would fault
                // the step when it runs, so publication refuses it here.
                if (!TypeFits(declared.Type, input.Type))
                {
                    errors.Add(new ValidationError(path, ReferenceRuleId,
                        $"Input '{input.Name}' of node '{node.NodeId}' reads and writes a {Describe(input.Type)}, but variable '{declared.Name}' is a {Describe(declared.Type)}. Bind a variable of a fitting type."));
                    continue;
                }

                if (writable && !targets.Any(target => target.ScopeId == scopeId && target.ReferenceKey == reference.ReferenceKey))
                    targets.Add(new StepVariableWriteTarget(scopeId, reference.ReferenceKey));
            }

            if (scriptReads is not null && ScriptInputKey(version) is { } scriptInput &&
                ScriptBodyWriteAnalyzer.LiteralScript(node, scriptInput) is { Length: > 0 } script)
            {
                visibility ??= new ScopedVariableResolver(structures)
                    .Resolve(workflowVariables, root, ScopedVariablePicker.DefaultMaxDepth, includeIterationScopes: true);
                if (scriptReads.Resolve("JavaScript", script, visibility.GetVisibleScopes(node.NodeId), scriptBody: true) is { } plan)
                {
                    if (plan.WorkflowState.HasInvalidLiteralCall)
                        errors.Add(new ValidationError(node.NodeId, "VF-STATE-001",
                            "A workflow-state call has an invalid literal output name, identity kind or argument count. Use a nonblank string output name, string/null identity and the method's exact arguments."));
                    // An engine script activity carries the type-level marker (it can reach any visible variable by name);
                    // its analyzed body says which it may write: every visible variable only for a computed or opaque write.
                    // A type that declares the marker itself may write any visible variable from its C# code too (FR-028).
                    everyVisible = VariableAccessDesignFacet.DeclaresAccessesUndeclaredVariables(version.DesignFacets) || plan.HasComputedWrite;
                    var declaredState = stateFacet?.Payload is { ValueKind: JsonValueKind.Object } statePayload &&
                        statePayload.TryGetProperty("declared", out var declared) && declared.ValueKind == JsonValueKind.True;
                    usesState = declaredState || plan.WorkflowState.AccessesState;
                    foreach (var write in plan.MayWrite())
                    {
                        var scopeId = UnorderedVariableWriteAnalyzer.NormalizeScopeId(write.ScopeId);
                        if (!targets.Any(target => target.ScopeId == scopeId && target.ReferenceKey == write.Variable.ReferenceKey))
                            targets.Add(new StepVariableWriteTarget(scopeId, write.Variable.ReferenceKey));
                    }
                }
            }

            if (usesState && (node.ExecutionPlacement == Elsa.Primitives.Models.ActivityExecutionPlacement.Background ||
                              structures.ProjectChildren(node).Count > 0 || structures.CompileExecutableStructure(node) is not null))
                errors.Add(new ValidationError(node.NodeId, "VF-STATE-002",
                    "Workflow output and identity access requires an inline leaf body outside a closed reusable graph."));

            if (targets.Count == 0 && !everyVisible)
                continue;
            byNode[node.NodeId] = new StepVariableWriters(targets, everyVisible);
            AddOverlapWarnings(node, targets, everyVisible, errors);
        }

        return (new StepVariableWriteFacts(byNode, workflowKeys.ToArray()), errors);
    }

    // The script input of a script activity: the elsa.outcomes facet's scriptInput, as the read validator finds it.
    private static string? ScriptInputKey(IActivityDefinitionVersion version)
    {
        var facet = version.DesignFacets.FirstOrDefault(item => item.Kind == "elsa.outcomes")?.Payload;
        return facet is { ValueKind: JsonValueKind.Object } payload &&
               payload.TryGetProperty("scriptInput", out var scriptInput) &&
               scriptInput.ValueKind == JsonValueKind.String &&
               scriptInput.GetString() is { Length: > 0 } key
            ? key
            : null;
    }

    // FR-029: a step that writes a variable in its session and also captures an output into it. Both commit; the capture
    // applies last and wins (ADR 0090 decision 6), which is rarely what the author meant.
    private static void AddOverlapWarnings(ActivityNode node, IReadOnlyCollection<StepVariableWriteTarget> targets, bool everyVisible, ICollection<ValidationError> errors)
    {
        foreach (var capture in UnorderedVariableWriteAnalyzer.DeclaredWrites(node, includeIntrinsics: false))
        {
            if (!everyVisible && !targets.Any(target => target.ScopeId == capture.ScopeId && target.ReferenceKey == capture.ReferenceKey))
                continue;
            errors.Add(new ValidationError(
                $"{node.NodeId}/outputs/{capture.OutputKey}",
                OverlapRuleId,
                $"Node '{node.NodeId}' may write variable '{capture.ReferenceKey}' in its step and also captures output '{capture.OutputKey}' into it. " +
                "Both commit with the step: the step's own write applies first and the capture last, so the capture wins. Write the variable in one place.")
            {
                Severity = ValidationSeverities.Warning
            });
        }
    }

    // The variable a binding names, when a scope visible to the node declares it. Only an enclosing scope's own declared
    // variables can be bound: loop items and indexes are not declared variables yet (spec 1222 Q1 makes them so).
    private static VariableDefinition? Find(
        string scopeId,
        string referenceKey,
        IReadOnlyList<ActivityNode> ancestors,
        IReadOnlyCollection<VariableDefinition> workflowVariables,
        IActivityStructureService structures)
    {
        if (StringComparer.Ordinal.Equals(scopeId, VariableReference.WorkflowScopeId))
            return workflowVariables.FirstOrDefault(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, referenceKey));

        var owner = ancestors.LastOrDefault(ancestor => StringComparer.Ordinal.Equals(ancestor.NodeId, scopeId));
        return owner is null
            ? null
            : structures.ProjectScopedVariables(owner).FirstOrDefault(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, referenceKey));
    }

    // A variable fits when it has the input's value type and collection shape. An Any or Object variable, and an input
    // that reads the value as JSON or Object, take any value.
    private static bool TypeFits(TypeReference variable, TypeReference input)
    {
        if (IsOpen(variable.Alias) || IsOpen(input.Alias))
            return true;
        return string.Equals(Normalize(variable.Alias), Normalize(input.Alias), StringComparison.OrdinalIgnoreCase) &&
               variable.CollectionKind == input.CollectionKind;
    }

    private static bool IsOpen(string? alias) =>
        Normalize(alias) is "elsa.any" or "object" or "system.object" or "jsonnode" or "system.text.json.nodes.jsonnode" or "jsonelement" or "system.text.json.jsonelement";

    private static string Normalize(string? alias) => (alias ?? string.Empty).Trim().ToLowerInvariant();

    private static string Describe(TypeReference type) =>
        type.CollectionKind == CollectionKind.Single ? type.Alias : $"{type.CollectionKind} of {type.Alias}";

}
