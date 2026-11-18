using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Reports how each script reads workflow variables (spec 1222), from the same resolution publish pins with:
/// <list type="bullet">
/// <item><c>VF-READ-002</c> (error): a script names a variable literally that no visible declaration has (FR-012,
/// FR-023). Publication refuses it too.</item>
/// <item><c>VF-READ-004</c> (warning): a script reads variables by a computed or opaque name; the message lists the
/// variables it could read, nearest scope first (FR-009). Each actual read is looked up, checked and recorded at run
/// time.</item>
/// <item><c>VF-READ-006</c> (warning): a script activity's body reads workflow variables, which it cannot do until its
/// variable session lands (spec 1222 FR-033).</item>
/// </list>
/// Input expressions and the script of a script activity (the <c>elsa.outcomes</c> facet's <c>scriptInput</c>) are
/// checked. Without a read resolver (no expression language that reads variables by name) nothing is checked.
/// </summary>
public sealed class ScriptVariableReadValidator(
    CatalogVersionResolver catalogResolver,
    IOptions<WorkflowDesignValidatorOptions> options,
    ActivityTreeWalker activityTreeWalker,
    IActivityStructureService activityStructureService,
    ScriptVariableReadResolver? readResolver = null) : IDraftValidator
{
    public const string UnresolvedReadType = "Scripts/UnresolvedVariableRead";
    public const string ComputedReadType = "Scripts/ComputedVariableRead";
    public const string BodyReadType = "Scripts/BodyVariableRead";

    public async ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        if (readResolver is null)
            return [];

        var state = draft.State;
        var visibility = new ScopedVariableResolver(activityStructureService)
            .Resolve(state.Variables, state.RootActivity, options.Value.MaxRecursionDepth, includeIterationScopes: true);
        var errors = new List<ValidationError>();
        foreach (var node in activityTreeWalker.Walk(state.RootActivity, options.Value.MaxRecursionDepth))
        {
            var scopes = visibility.GetVisibleScopes(node.NodeId);
            var scriptInput = await ScriptInputKeyAsync(node, cancellationToken);
            // The node's own inputs and those its structure owns (a Switch case condition), as publish compiles them.
            var structureInputs = activityStructureService.ProjectInputs(node);
            var inputs = node.Inputs
                .Where(input => !structureInputs.ConsumedInputKeys.Contains(input.ReferenceKey, StringComparer.Ordinal))
                .Select(input => (Bag: "inputs", State: input))
                .Concat(structureInputs.Inputs.Where(input => input.State is not null)
                    .Select(input => (Bag: "structure", State: input.State! with { ReferenceKey = input.ReferenceKey })));
            foreach (var (bag, input) in inputs)
            {
                var path = $"{node.NodeId}/{bag}/{input.ReferenceKey}";
                ScriptVariableReadPlan? plan;
                var isBody = scriptInput is not null && StringComparer.Ordinal.Equals(input.ReferenceKey, scriptInput);
                if (isBody)
                    plan = LiteralText(input) is { Length: > 0 } script ? readResolver.Resolve("JavaScript", script, scopes, scriptBody: true) : null;
                else if (input.Value?.ExpressionType is { } language && readResolver.Supports(language) && TryGetSource(input.Value.Value, out var source))
                    plan = readResolver.Resolve(language, source, scopes);
                else
                    plan = null;

                if (plan is null)
                    continue;

                foreach (var name in plan.Unresolved)
                {
                    errors.Add(new ValidationError(path, UnresolvedReadType,
                        $"{ScriptVariableReadDiagnostics.UnresolvedLiteralRead}: The script of input '{input.ReferenceKey}' on activity '{node.NodeId}' reads variable '{name}', " +
                        "but no variable with that name is visible from the activity. Declare it on the workflow or an enclosing container, or correct the name. " +
                        "A loop index is visible only inside the loop's body, and ForEach declares currentIndex only while ExposeIndex is not the literal false."));
                }

                if (isBody && (plan.Pinned.Count > 0 || plan.Unresolved.Count > 0 || plan.NeedsFallback))
                {
                    // Spec 1222 FR-033: the body reads variables through its variable session, which is not built yet.
                    errors.Add(new ValidationError(path, BodyReadType,
                        $"{ScriptVariableReadDiagnostics.BodyReadNotYetServed}: The script of activity '{node.NodeId}' reads workflow variables, but a script activity's body " +
                        "cannot read them yet and fails when it runs. Pass the values in through the activity's arguments for now.")
                    { Severity = ValidationSeverities.Warning });
                    continue;
                }

                if (plan.Candidates is { } candidates)
                {
                    var listed = candidates.Count == 0
                        ? "none"
                        : string.Join(", ", candidates.Select(candidate => $"'{candidate.Name}'"));
                    errors.Add(new ValidationError(path, ComputedReadType,
                        $"{ScriptVariableReadDiagnostics.ComputedReadReported}: The script of input '{input.ReferenceKey}' on activity '{node.NodeId}' reads variables by a computed name. " +
                        $"It could read: {listed}. Each read is looked up nearest scope first, checked and recorded when the script runs; a name that matches nothing reads undefined.")
                    { Severity = ValidationSeverities.Warning });
                }
            }
        }

        return errors;
    }

    private async ValueTask<string?> ScriptInputKeyAsync(ActivityNode node, CancellationToken cancellationToken)
    {
        if (node.Intrinsic is not null || string.IsNullOrWhiteSpace(node.ActivityVersionId))
            return null;
        if (await catalogResolver.Find(node.ActivityVersionId, cancellationToken) is not { } version)
            return null;
        var facet = version.DesignFacets.FirstOrDefault(item => item.Kind == "elsa.outcomes")?.Payload;
        return facet is { ValueKind: JsonValueKind.Object } payload &&
               payload.TryGetProperty("scriptInput", out var scriptInput) &&
               scriptInput.GetString() is { Length: > 0 } key
            ? key
            : null;
    }

    private static string? LiteralText(ArgumentState input)
    {
        var value = input.Value;
        if (value is null || value.ExpressionType is { } type && !string.Equals(type, "Literal", StringComparison.OrdinalIgnoreCase))
            return null;
        return value.Value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
    }

    private static bool TryGetSource(object? value, out string source)
    {
        source = value switch
        {
            string text => text,
            ExpressionDefinition definition => definition.Source,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? "",
            JsonElement { ValueKind: JsonValueKind.Object } element when element.TryGetProperty("source", out var text) && text.ValueKind == JsonValueKind.String => text.GetString() ?? "",
            _ => ""
        };
        return source.Length > 0;
    }
}
