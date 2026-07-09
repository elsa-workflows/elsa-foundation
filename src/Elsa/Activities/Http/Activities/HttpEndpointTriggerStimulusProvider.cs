using System.Globalization;
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
    private const string AuthorizeInput = nameof(HttpEndpoint.Authorize);
    private const string PolicyInput = nameof(HttpEndpoint.Policy);
    private const string RequestTimeoutInput = nameof(HttpEndpoint.RequestTimeout);
    private const string RequestSizeLimitInput = nameof(HttpEndpoint.RequestSizeLimit);
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

        var options = new HttpEndpointStimulusOptions(
            Authorize: ReadLiteralBool(node, AuthorizeInput) ?? false,
            Policy: ReadLiteralStringOption(node, PolicyInput),
            RequestTimeout: ReadLiteralTimeSpan(node, RequestTimeoutInput),
            RequestSizeLimit: ReadLiteralLong(node, RequestSizeLimitInput));

        return HttpEndpointStimulus.Describe(path, effectiveMethods, options);
    }

    /// <summary>
    /// Reads an optional literal option value via <paramref name="convert"/>. Returns null when the input is
    /// unauthored (so the caller applies the default and the option is omitted from metadata). A non-literal
    /// (expression-bound) value throws with the same publish-time rule as the path and supported methods: a
    /// binding-metadata option cannot be resolved from an expression, so the publish fails rather than persisting
    /// an unresolved option.
    /// </summary>
    private static T? ReadLiteralOption<T>(ExecutableNode node, string inputName, Func<JsonElement, T> convert) where T : struct =>
        ReadRequiredLiteralOption(node, inputName) is { } literal ? convert(literal) : null;

    /// <summary>
    /// Reads an optional literal string option (the endpoint's policy). Returns null when unauthored or blank so
    /// the option is omitted from metadata; a non-literal throws with the same publish-time rule as the other
    /// options.
    /// </summary>
    private static string? ReadLiteralStringOption(ExecutableNode node, string inputName)
    {
        if (ReadRequiredLiteralOption(node, inputName) is not { } literal)
            return null;

        var text = literal.ValueKind == JsonValueKind.String ? literal.GetString() : literal.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Resolves an optional option input to its literal <see cref="JsonElement"/>, or null when the input is
    /// unauthored or explicitly null. A non-literal (expression-bound) value throws — an option that rides the
    /// binding metadata cannot be resolved from an expression, so the publish fails rather than persisting an
    /// unresolved option (same rule as the path and supported methods).
    /// </summary>
    private static JsonElement? ReadRequiredLiteralOption(ExecutableNode node, string inputName)
    {
        var binding = FindBinding(node, inputName);
        if (binding is null)
            return null;

        if (binding.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { } literal)
            throw new ArgumentException(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a non-literal '{inputName}'. Endpoint " +
                "options must be authored literals so they are fixed at publish time.");

        return literal.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : literal;
    }

    private static bool? ReadLiteralBool(ExecutableNode node, string inputName) =>
        ReadLiteralOption(node, inputName, literal => literal.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(literal.GetString(), out var parsed) => parsed,
            _ => throw new ArgumentException(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a literal '{inputName}' that is not a boolean.")
        });

    private static TimeSpan? ReadLiteralTimeSpan(ExecutableNode node, string inputName) =>
        ReadLiteralOption(node, inputName, literal =>
        {
            var text = literal.ValueKind == JsonValueKind.String ? literal.GetString() : literal.ToString();
            if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var parsed))
                throw new ArgumentException(
                    $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a literal '{inputName}' that is not a TimeSpan.");

            // Review C2: a non-positive timeout would arm CancelAfter with an invalid value at request time
            // (or cancel every request instantly) — an authoring error, so the publish fails here.
            if (parsed <= TimeSpan.Zero)
                throw new ArgumentException(
                    $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a non-positive '{inputName}' ({parsed:c}); the request timeout must be greater than zero.");

            return parsed;
        });

    private static long? ReadLiteralLong(ExecutableNode node, string inputName) =>
        ReadLiteralOption(node, inputName, literal =>
        {
            long? value = literal.ValueKind switch
            {
                JsonValueKind.Number when literal.TryGetInt64(out var number) => number,
                JsonValueKind.String when long.TryParse(literal.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            };

            if (value is null)
                throw new ArgumentException(
                    $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a literal '{inputName}' that is not an integer.");

            // Review C2 (companion): a non-positive size limit is meaningless and would otherwise be silently
            // dropped by the read side's strict parse — fail the publish where the authoring error is made.
            if (value <= 0)
                throw new ArgumentException(
                    $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a non-positive '{inputName}' ({value}); the request size limit must be greater than zero.");

            return value.Value;
        });

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
