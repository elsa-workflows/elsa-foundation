using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// The <see cref="IActivityTriggerStimulusProvider"/> for the <see cref="HttpEndpoint"/> start trigger (W16, on
/// the W7 seam). It recognizes published <see cref="HttpEndpoint"/> nodes and derives their stimulus identity
/// from the authored <see cref="HttpEndpoint.Path"/> and <see cref="HttpEndpoint.SupportedMethods"/> literals, so
/// the trigger extractor can index one binding per <c>(template, method)</c> at publish time over the pinned
/// artifact.
/// </summary>
/// <remarks>
/// Both the path and the supported methods must be authored literals — the stimulus identity is fixed at publish
/// time, before any run exists, so a non-literal (expression-bound) value has no value to hash and throws,
/// failing the publish rather than persisting an unroutable trigger. When <see cref="HttpEndpoint.SupportedMethods"/>
/// is unauthored or empty the endpoint defaults to <c>GET</c> (elsa-core parity — this is BREAKING for the earlier
/// any-method behavior, and is called out in the PR body).
/// </remarks>
public sealed class HttpEndpointTriggerStimulusProvider : IActivityTriggerStimulusProvider
{
    private const string PathInput = nameof(HttpEndpoint.Path);
    private const string MethodsInput = nameof(HttpEndpoint.SupportedMethods);
    private static readonly string[] DefaultMethods = ["GET"];

    public IReadOnlyCollection<TriggerStimulusDescriptor> Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, HttpEndpoint.ActivityType))
            return [];

        var path = ReadLiteralString(node, PathInput)
            ?? throw new ArgumentException(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has no literal '{PathInput}'. A start " +
                "trigger's path must be an authored literal so its stimulus is fixed at publish time.");

        var methods = ReadLiteralStringCollection(node, MethodsInput);
        var effectiveMethods = methods is { Count: > 0 } ? methods : DefaultMethods;

        return HttpEndpointStimulus.Describe(path, effectiveMethods);
    }

    private static string? ReadLiteralString(ExecutableNode node, string inputName)
    {
        var literal = ReadLiteral(node, inputName);
        if (literal is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var text = RequireJsonString(value, node, inputName);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Extracts the string value of a literal JSON element that is expected to be a string. A non-string kind is a
    /// publish failure — coercing a number/bool/object into a route path or HTTP method via <c>ToString()</c> would
    /// persist a garbage stimulus — so it throws the same <see cref="ArgumentException"/> as the non-literal case,
    /// naming the input and the offending JSON kind.
    /// </summary>
    private static string? RequireJsonString(JsonElement value, ExecutableNode node, string inputName)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new ArgumentException(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a literal '{inputName}' element of kind " +
                $"'{value.ValueKind}' that is not a JSON string. Routing-significant literals must be authored as strings.");

        return value.GetString();
    }

    /// <summary>
    /// Reads an optional literal string-collection input (the endpoint's supported methods). Returns null when the
    /// input is unauthored so the caller can apply the default. A non-literal (expression-bound) value throws with
    /// the same publish-time rule as the path: a routing-significant facet cannot be resolved from an expression.
    /// </summary>
    private static IReadOnlyCollection<string>? ReadLiteralStringCollection(ExecutableNode node, string inputName)
    {
        var binding = FindBinding(node, inputName);
        if (binding is null)
            return null;

        if (binding.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { } literal)
            throw new ArgumentException(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a non-literal '{inputName}'. Supported " +
                "methods must be an authored literal so the endpoint's per-method stimuli are fixed at publish time.");

        if (literal.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (literal.ValueKind != JsonValueKind.Array)
            throw new ArgumentException(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a literal '{inputName}' that is not a JSON array of methods.");

        var methods = literal.EnumerateArray()
            .Select(element => RequireJsonString(element, node, inputName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        return methods.Length == 0 ? null : methods;
    }

    private static JsonElement? ReadLiteral(ExecutableNode node, string inputName)
    {
        var binding = FindBinding(node, inputName);
        if (binding is null || binding.Source != RuntimeInputBindingSource.Literal)
            return null;

        return binding.LiteralValue;
    }

    private static RuntimeInputBinding? FindBinding(ExecutableNode node, string inputName) =>
        node.InputBindings
            .FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, inputName))
            .Value;
}
