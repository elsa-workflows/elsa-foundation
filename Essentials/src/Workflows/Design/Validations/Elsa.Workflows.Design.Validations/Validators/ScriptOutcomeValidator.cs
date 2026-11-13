using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Checks the scripts of activities that select outcomes with <c>return complete(result, outcomes)</c> (spec 1208), read
/// from the <c>elsa.outcomes</c> facet's <c>scriptInput</c>:
/// <list type="bullet">
/// <item><c>VF-AUTH-010</c> (error): a string literal, or an array of string literals, passed as the outcomes of a
/// <c>complete(...)</c> call, wherever the call is, names an outcome outside the node's outcome set (FR-015). A computed
/// name or an aliased <c>complete</c> is checked at run time instead (<c>VF-ACT-006</c>).</item>
/// <item><c>VF-AUTH-012</c> (warning): the script returns, at its top level, a string literal equal to an outcome
/// connected on the node; a returned value is data and selects nothing (FR-008a).</item>
/// <item>A script that declares <c>complete</c>, a reserved built-in name (FR-007a).</item>
/// </list>
/// The outcome set follows the publish compiler's rule: static ports, plus literal configured items with the
/// catch-all and the <c>Done</c> rule. A set decided by an expression is not checked here.
/// </summary>
public sealed class ScriptOutcomeValidator(
    CatalogVersionResolver catalogResolver,
    IOptions<WorkflowDesignValidatorOptions> options,
    ActivityTreeWalker activityTreeWalker,
    IActivityStructureService activityStructureService) : IDraftValidator
{
    public const string UnknownOutcomeType = "Scripts/UnknownOutcome";
    public const string ReturnedOutcomeNameType = "Scripts/ReturnedOutcomeName";
    public const string ReservedNameType = "Scripts/ReservedName";
    private const string Complete = "complete";
    private const string Done = "Done";

    public async ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var nodes = activityTreeWalker.Walk(draft.State.RootActivity, options.Value.MaxRecursionDepth).ToArray();
        var connectedPorts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var owner in nodes)
        {
            foreach (var usage in activityStructureService.ProjectChildContractMemberUsage(owner))
            {
                if (!connectedPorts.TryGetValue(usage.NodeId, out var ports))
                    connectedPorts[usage.NodeId] = ports = new HashSet<string>(StringComparer.Ordinal);
                ports.UnionWith(usage.MemberUsage.Where(member => member.MemberKind == "Outcome").Select(member => member.ReferenceKey));
            }
        }

        var errors = new List<ValidationError>();
        foreach (var node in nodes)
        {
            if (node.Intrinsic is not null || string.IsNullOrWhiteSpace(node.ActivityVersionId))
                continue;
            if (await catalogResolver.Find(node.ActivityVersionId, cancellationToken) is not { } version)
                continue;
            var facet = version.DesignFacets.FirstOrDefault(item => item.Kind == "elsa.outcomes")?.Payload;
            if (facet is not { ValueKind: JsonValueKind.Object } payload ||
                !payload.TryGetProperty("scriptInput", out var scriptInputElement) ||
                scriptInputElement.GetString() is not { Length: > 0 } scriptInput ||
                LiteralText(node, scriptInput) is not { Length: > 0 } script)
                continue;

            Script parsed;
            try
            {
                parsed = new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(script);
            }
            catch (ParseErrorException)
            {
                continue; // The expression tooling reports syntax errors; nothing to check here.
            }

            var analysis = new ScriptAnalysis();
            analysis.Visit(parsed, insideFunction: false);
            if (analysis.DeclaresComplete)
                errors.Add(new ValidationError($"{node.NodeId}/inputs/{scriptInput}", ReservedNameType,
                    $"The script of activity '{node.NodeId}' declares '{Complete}', a reserved built-in name: scripts select outcomes by returning {Complete}(result, outcomes). Rename it."));

            if (OutcomeSet(node, payload) is { } outcomes)
            {
                foreach (var name in analysis.LiteralOutcomes.Distinct(StringComparer.Ordinal).Where(name => !outcomes.Contains(name)))
                {
                    errors.Add(new ValidationError($"{node.NodeId}/inputs/{scriptInput}", UnknownOutcomeType,
                        $"VF-AUTH-010: The script of activity '{node.NodeId}' selects outcome '{name}', which is not in its outcome set " +
                        $"[{string.Join(", ", outcomes.Order(StringComparer.Ordinal).Select(outcome => $"'{outcome}'"))}]. Add it to the possible outcomes, or correct the name."));
                }
            }

            if (connectedPorts.TryGetValue(node.NodeId, out var connected))
            {
                foreach (var returned in analysis.ReturnedStrings.Distinct(StringComparer.Ordinal).Where(connected.Contains))
                {
                    errors.Add(new ValidationError($"{node.NodeId}/inputs/{scriptInput}", ReturnedOutcomeNameType,
                        $"VF-AUTH-012: The script of activity '{node.NodeId}' returns '{returned}', the name of its connected outcome '{returned}'. A returned value is data " +
                        $"and selects no outcome; to route there, return {Complete}(result, '{returned}').")
                    { Severity = ValidationSeverities.Warning });
                }
            }
        }

        return errors;
    }

    private static string? LiteralText(ActivityNode node, string inputKey)
    {
        var value = node.Inputs.FirstOrDefault(input => StringComparer.Ordinal.Equals(input.ReferenceKey, inputKey))?.Value;
        if (value is null || value.ExpressionType is { } type && !string.Equals(type, "Literal", StringComparison.OrdinalIgnoreCase))
            return null;
        return value.Value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
    }

    /// <summary>The node's outcome set as the publish compiler pins it, or null when an expression decides its items.</summary>
    private static HashSet<string>? OutcomeSet(ActivityNode node, JsonElement payload)
    {
        var outcomes = new HashSet<string>(StringComparer.Ordinal);
        if (payload.TryGetProperty("ports", out var ports) && ports.ValueKind == JsonValueKind.Array)
            foreach (var port in ports.EnumerateArray())
                if (port.ValueKind == JsonValueKind.Object && port.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } text)
                    outcomes.Add(text);

        if (payload.TryGetProperty("dynamicOutcomes", out var dynamic) && dynamic.ValueKind == JsonValueKind.Object &&
            dynamic.TryGetProperty("source", out var source) && source.GetString() is { Length: > 0 } sourceKey)
        {
            if (Items(node, sourceKey) is not { } items)
                return null;
            if (items.Count > 0)
            {
                outcomes.UnionWith(items);
                if (dynamic.TryGetProperty("unmatched", out var unmatched) && unmatched.GetString() is { Length: > 0 } catchAll)
                    outcomes.Add(catchAll);
                if (!dynamic.TryGetProperty("selectsOnEveryCompletion", out var selects) || selects.ValueKind != JsonValueKind.False)
                    outcomes.Remove(Done);
                else
                    outcomes.Add(Done);
            }
        }

        if (outcomes.Count == 0)
            outcomes.Add(Done);
        return outcomes;
    }

    private static IReadOnlyList<string>? Items(ActivityNode node, string inputKey)
    {
        var value = node.Inputs.FirstOrDefault(input => StringComparer.Ordinal.Equals(input.ReferenceKey, inputKey))?.Value;
        if (value?.Value is null)
            return [];
        if (value.ExpressionType is { } type && !string.Equals(type, "Literal", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, "Object", StringComparison.OrdinalIgnoreCase))
            return null;

        JsonElement array;
        try
        {
            array = value.Value switch
            {
                JsonElement { ValueKind: JsonValueKind.String } text => JsonDocument.Parse(text.GetString()!).RootElement,
                JsonElement element => element,
                string text => JsonDocument.Parse(text).RootElement,
                _ => JsonSerializer.SerializeToElement(value.Value)
            };
        }
        catch (JsonException)
        {
            return null;
        }

        return array.ValueKind != JsonValueKind.Array
            ? null
            : array.EnumerateArray()
                .Select(item => item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Number => item.GetRawText(),
                    _ => null
                })
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
    }

    private sealed class ScriptAnalysis
    {
        public List<string> LiteralOutcomes { get; } = [];
        public List<string> ReturnedStrings { get; } = [];
        public bool DeclaresComplete { get; private set; }

        public void Visit(Node node, bool insideFunction)
        {
            switch (node)
            {
                case CallExpression { Callee: Identifier { Name: Complete } } call when call.Arguments.Count >= 2:
                    CollectLiteralOutcomes(call.Arguments[1]);
                    break;
                case VariableDeclarator { Id: Identifier { Name: Complete } }:
                case FunctionDeclaration { Id.Name: Complete }:
                case ClassDeclaration { Id.Name: Complete }:
                    DeclaresComplete = true;
                    break;
                case ReturnStatement { Argument: Literal { Value: string returned } } when !insideFunction:
                    ReturnedStrings.Add(returned);
                    break;
            }

            if (node is IFunction function && function.Params.Any(parameter => parameter is Identifier { Name: Complete }))
                DeclaresComplete = true;

            var nested = insideFunction || node is IFunction;
            foreach (var child in node.ChildNodes)
                Visit(child, nested);
        }

        private void CollectLiteralOutcomes(Node outcomes)
        {
            switch (outcomes)
            {
                case Literal { Value: string name }:
                    LiteralOutcomes.Add(name);
                    break;
                case ArrayExpression array:
                    LiteralOutcomes.AddRange(array.Elements.OfType<Literal>().Select(element => element.Value).OfType<string>());
                    break;
            }
        }
    }
}
