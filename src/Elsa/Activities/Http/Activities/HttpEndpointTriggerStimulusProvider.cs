using System.Globalization;
using System.Text.Json;
using Elsa.Http.Core;
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
    public string ProviderId => "Elsa.HttpEndpoint";
    public TriggerCardinality Cardinality => TriggerCardinality.Exclusive;
    private const string PathInput = nameof(HttpEndpoint.Path);
    private const string MethodsInput = nameof(HttpEndpoint.SupportedMethods);
    private const string CanStartWorkflowInput = nameof(HttpEndpoint.CanStartWorkflow);
    private const string AuthorizeInput = nameof(HttpEndpoint.Authorize);
    private const string PolicyInput = nameof(HttpEndpoint.Policy);
    private const string RequestTimeoutInput = nameof(HttpEndpoint.RequestTimeout);
    private const string RequestSizeLimitInput = nameof(HttpEndpoint.RequestSizeLimit);
    private const string ResponseModeInput = nameof(HttpEndpoint.ResponseMode);
    private static readonly string[] DefaultMethods = ["GET"];

    public ActivityTriggerStimulusResult Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, HttpEndpoint.ActivityType))
            return ActivityTriggerStimulusResult.NotRecognized;

        // A mid-flow endpoint (CanStartWorkflow = false or unauthored) is recognized but declares itself a non-start:
        // no bindings, and — because the result is Recognized rather than NotRecognized — no publish failure (spec 089 D).
        // Starting is opt-in: only an explicit true proceeds to validate and describe the routing literals.
        if (ReadLiteralBool(node, CanStartWorkflowInput) is not true)
            return ActivityTriggerStimulusResult.Recognized([]);

        // Read every routing literal and endpoint option in a single pass, collecting all problems rather than
        // throwing on the first. Absent optional inputs fall back to the activity's declared defaults
        // (SupportedMethods → ["GET"], Authorize → false, ResponseMode → Async, Policy → null,
        // RequestTimeout/RequestSizeLimit → null), so a UI that authors none of them still publishes; only genuinely
        // required-but-missing or invalid values are reported — and all of them together, in one preflight pass (#925).
        var problems = new List<string>();

        var path = TryReadOption(problems, () => ReadLiteralString(node, PathInput));
        if (path is null)
            problems.Add(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has no literal '{PathInput}'. A start " +
                "trigger's path must be an authored literal so its stimulus is fixed at publish time.");

        var methods = TryReadOption(problems, () => ReadLiteralStringCollection(node, MethodsInput));
        var effectiveMethods = methods is { Count: > 0 } ? methods : DefaultMethods;

        var authorize = TryReadOption(problems, () => (bool?)(ReadLiteralBool(node, AuthorizeInput) ?? false));
        var policy = TryReadOption(problems, () => ReadLiteralStringOption(node, PolicyInput));
        var requestTimeout = TryReadOption(problems, () => ReadLiteralTimeSpan(node, RequestTimeoutInput));
        var requestSizeLimit = TryReadOption(problems, () => ReadLiteralLong(node, RequestSizeLimitInput));
        var responseMode = TryReadOption(problems, () => (ResponseMode?)(ReadLiteralEnum<ResponseMode>(node, ResponseModeInput) ?? ResponseMode.Async));

        if (problems.Count > 0)
            throw new ArgumentException(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' cannot be published:{Environment.NewLine}" +
                string.Join(Environment.NewLine, problems.Select(problem => $" - {problem}")));

        var options = new HttpEndpointStimulusOptions(
            Authorize: authorize ?? false,
            Policy: policy,
            RequestTimeout: requestTimeout,
            RequestSizeLimit: requestSizeLimit,
            ResponseMode: responseMode ?? ResponseMode.Async);

        return ActivityTriggerStimulusResult.Recognized(HttpEndpointStimulus.Describe(path!, effectiveMethods, options));
    }

    /// <summary>
    /// Runs a single option reader, recording any publish-time <see cref="ArgumentException"/>/<see cref="FormatException"/>
    /// into <paramref name="problems"/> and returning the type default so the remaining options are still evaluated. This
    /// turns the per-option throws into one aggregated diagnostic so preflight reports every problem in one pass (#925).
    /// </summary>
    private static T? TryReadOption<T>(List<string> problems, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            problems.Add(exception.Message);
            return default;
        }
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

        var text = LiteralText(literal);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// The scalar text of a literal JSON element: the raw string for a JSON string, else its <c>ToString()</c>
    /// form (numbers, booleans authored as bare literals). Shared by the string-shaped option readers so the
    /// coercion idiom lives in one place (#592 item 19).
    /// </summary>
    private static string? LiteralText(JsonElement literal) =>
        literal.ValueKind == JsonValueKind.String ? literal.GetString() : literal.ToString();

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

    /// <summary>
    /// Reads an optional literal enum option (the endpoint's response mode), mirroring <see cref="ReadLiteralBool"/>.
    /// Accepts a JSON string authored as the enum member name (case-sensitive, e.g. <c>"Sync"</c>) or a JSON number
    /// authored as a defined member's underlying value. Returns null when the input is unauthored (caller applies the
    /// default and the option is omitted from metadata); a non-literal, or a literal that is not a defined member of
    /// <typeparamref name="T"/>, throws the same publish-time failure as the other options — an unroutable/undecodable
    /// mode must not persist.
    /// </summary>
    private static T? ReadLiteralEnum<T>(ExecutableNode node, string inputName) where T : struct, Enum =>
        ReadLiteralOption(node, inputName, literal => literal.ValueKind switch
        {
            JsonValueKind.String when Enum.TryParse<T>(literal.GetString(), ignoreCase: false, out var parsed) && Enum.IsDefined(parsed) => parsed,
            JsonValueKind.Number when literal.TryGetInt32(out var number) && Enum.IsDefined(typeof(T), number) => (T)Enum.ToObject(typeof(T), number),
            _ => throw new ArgumentException(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has a literal '{inputName}' that is not a defined {typeof(T).Name} value.")
        });

    private static TimeSpan? ReadLiteralTimeSpan(ExecutableNode node, string inputName) =>
        ReadLiteralOption(node, inputName, literal =>
        {
            var text = LiteralText(literal);
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
