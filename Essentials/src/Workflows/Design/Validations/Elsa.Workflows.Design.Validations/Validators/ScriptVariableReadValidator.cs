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
/// <item><c>VF-WRITE-002</c>, <c>VF-WRITE-004</c> (error): a script body writes, by a literal name, an undeclared variable,
/// or a loop item or index (spec 1231 FR-020). Publication refuses them too, and a transient target (<c>VF-WRITE-003</c>).</item>
/// <item><c>VF-WRITE-005</c> (warning): a script body writes by a computed or opaque name, so it may write every visible
/// variable (spec 1231 FR-020a).</item>
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
    public const string RefusedWriteType = "Scripts/RefusedVariableWrite";
    public const string ComputedWriteType = "Scripts/ComputedVariableWrite";

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
                string? body = null;
                if (isBody)
                    plan = (body = LiteralText(input)) is { Length: > 0 } script ? readResolver.Resolve("JavaScript", script, scopes, scriptBody: true) : null;
                else if (input.Value?.ExpressionType is { } language && readResolver.Supports(language) && TryGetSource(input.Value.Value, out var source))
                    plan = readResolver.Resolve(language, source, scopes);
                else
                    plan = null;

                if (plan is null)
                    continue;

                if (body is not null)
                    AddWriteFindings(errors, path, input.ReferenceKey, node.NodeId, body, plan);

                foreach (var name in plan.Unresolved.Where(name => body is null || !plan.Writes.Any(write => StringComparer.Ordinal.Equals(write.Name, name))))
                {
                    errors.Add(new ValidationError(path, UnresolvedReadType,
                        $"{ScriptVariableReadDiagnostics.UnresolvedLiteralRead}: The script of input '{input.ReferenceKey}' on activity '{node.NodeId}' reads variable '{name}', " +
                        "but no variable with that name is visible from the activity. Declare it on the workflow or an enclosing container, or correct the name. " +
                        "A loop index is visible only inside the loop's body, and ForEach declares currentIndex only while ExposeIndex is not the literal false."));
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

    // Spec 1231 FR-020, FR-020a: the refusals publish makes for a body's literal writes (an undeclared target, a loop item
    // or index; a transient target is decided by the runtime storage profile, so publication alone refuses it), and the
    // warning for a computed one.
    private static void AddWriteFindings(List<ValidationError> errors, string path, string inputKey, string nodeId, string body, ScriptVariableReadPlan plan)
    {
        foreach (var (write, code) in plan.RefusedLiteralWrites())
            errors.Add(new ValidationError(path, RefusedWriteType,
                ScriptVariableReadDiagnostics.DescribeLiteralWriteRefusal(code, inputKey, nodeId, body, write)));

        if (plan.HasComputedWrite)
            errors.Add(new ValidationError(path, ComputedWriteType, ScriptVariableReadDiagnostics.DescribeComputedWrite(inputKey, nodeId))
            {
                Severity = ValidationSeverities.Warning
            });
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
