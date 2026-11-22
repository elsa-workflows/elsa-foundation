using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class TriggerInputMapper(IWorkflowExecutableInputValidator validator,
    IPortableExpressionEvaluator? expressions = null,
    IEnumerable<IPortableExpressionHandler>? expressionHandlers = null) : ITriggerInputMapper
{
    // The evaluator contract does not advertise languages. Even a replacement evaluator needs handler
    // metadata here; hosts with another capability-discovery contract can replace ITriggerInputMapper.
    private readonly IReadOnlySet<string> expressionLanguages = (expressionHandlers ?? [])
        .Select(handler => handler.Language).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public WorkflowExecutableInputValidation ValidateMappings(WorkflowExecutableInputContract contract,
        IReadOnlyList<TriggerInputMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(mappings);
        var findings = CheckDestinations(contract, mappings);
        if (findings.Count > 0) return WorkflowExecutableInputValidation.Failure(findings);
        foreach (var mapping in mappings)
        {
            if (mapping.Binding is TriggerExpressionBinding expression &&
                (expressions is null || !expressionLanguages.Contains(expression.Language) ||
                 !ExpressionEvaluationCapabilities.Resolve(expression.CapabilityProfile).IsBindingPure))
                findings.Add(new("triggers.mapping.expression-unavailable", "The selected pure expression language, evaluator or profile is unavailable.", mapping.InputName));
        }
        if (findings.Count > 0) return WorkflowExecutableInputValidation.Failure(findings);
        var known = validator.ValidateKnownInputs(contract, mappings.Where(mapping => mapping.Binding is TriggerLiteralBinding)
            .Select(mapping => KeyValuePair.Create(mapping.InputName, ((TriggerLiteralBinding)mapping.Binding).Value)));
        if (!known.IsValid) return known;
        var names = mappings.Select(mapping => mapping.InputName).ToHashSet(StringComparer.Ordinal);
        foreach (var input in contract.Inputs.Where(input => input.IsRequired && input.DefaultValue is null && !names.Contains(input.Name)))
            findings.Add(new(WorkflowExecutableInputValidationReasonCodes.MissingRequired,
                "A required workflow input needs a mapping or a declared default.", input.Name));
        return findings.Count == 0 ? known : WorkflowExecutableInputValidation.Failure(findings);
    }

    public async ValueTask<WorkflowExecutableInputValidation> MapAsync(WorkflowExecutableInputContract contract,
        IReadOnlyList<TriggerInputMapping> mappings, JsonElement sourceEnvelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var preflight = ValidateMappings(contract, mappings);
        if (!preflight.IsValid) return preflight;
        if (sourceEnvelope.ValueKind != JsonValueKind.Object)
            return Failure("triggers.mapping.source-envelope", "A trigger source envelope must be a detached JSON object.");
        var values = new List<KeyValuePair<string, JsonElement>>();
        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (mapping.Binding)
            {
                case TriggerLiteralBinding literal:
                    values.Add(KeyValuePair.Create(mapping.InputName, literal.Value));
                    break;
                case TriggerSourceBinding source:
                    // Omission remains omission; the ordinary target validator decides defaults/required inputs.
                    if (TryRead(sourceEnvelope, source.Path, out var selected))
                        values.Add(KeyValuePair.Create(mapping.InputName, selected));
                    break;
                case TriggerExpressionBinding expression:
                    var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var (name, selector) in expression.Parameters)
                    {
                        if (selector is TriggerLiteralBinding parameterLiteral) parameters.Add(name, parameterLiteral.Value);
                        else if (selector is TriggerSourceBinding parameterSource && TryRead(sourceEnvelope, parameterSource.Path, out var parameterValue))
                            parameters.Add(name, parameterValue);
                        else return Failure("triggers.mapping.parameter-missing", "A declared expression parameter is absent from the source.", mapping.InputName);
                    }
                    var definition = new ExpressionDefinition(expression.Language, expression.Source, expression.ResultType,
                        parameters.ToDictionary(pair => pair.Key,
                            pair => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(pair.Value), StringComparer.Ordinal),
                        expression.Options, expression.CapabilityProfile);
                    try
                    {
                        var result = await expressions!.EvaluateAsync(new(definition, parameters, cancellationToken));
                        if (result.ValueKind == JsonValueKind.Undefined)
                            return Failure("triggers.mapping.expression-undefined", "The expression produced no defined input value.", mapping.InputName);
                        values.Add(KeyValuePair.Create(mapping.InputName, result.Clone()));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (NotSupportedException)
                    {
                        return Failure("triggers.mapping.expression-unavailable", "The selected pure expression evaluator is unavailable.", mapping.InputName);
                    }
                    catch (Exception)
                    {
                        // Evaluator diagnostics may contain payload fragments. The public finding stays value-free.
                        return Failure("triggers.mapping.expression-failed", "The expression could not produce the declared workflow input.", mapping.InputName);
                    }
                    break;
                default:
                    return Failure("triggers.mapping.binding-unsupported", "The input binding is not supported.", mapping.InputName);
            }
        }
        return validator.Validate(contract, values);
    }

    private static List<WorkflowExecutableInputValidationFinding> CheckDestinations(WorkflowExecutableInputContract contract,
        IReadOnlyList<TriggerInputMapping> mappings)
    {
        var findings = new List<WorkflowExecutableInputValidationFinding>();
        var declarations = contract.Inputs.Select(input => input.Name).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            if (mapping is null)
            {
                findings.Add(new("triggers.mapping.null", "An input mapping cannot be null."));
                continue;
            }
            if (!seen.Add(mapping.InputName))
                findings.Add(new(WorkflowExecutableInputValidationReasonCodes.DuplicateName, "An input destination is mapped more than once.", mapping.InputName));
            if (!declarations.Contains(mapping.InputName))
                findings.Add(new(WorkflowExecutableInputValidationReasonCodes.UnknownName, "The destination is not a declared workflow input.", mapping.InputName));
        }
        return findings;
    }

    private static bool TryRead(JsonElement source, IReadOnlyList<string> path, out JsonElement value)
    {
        value = source;
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out var next)) return false;
            value = next;
        }
        return value.ValueKind != JsonValueKind.Undefined;
    }

    private static WorkflowExecutableInputValidation Failure(string code, string message, string? input = null) =>
        WorkflowExecutableInputValidation.Failure([new(code, message, input)]);
}
