using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Expressions.Core.Models;

/// <summary>
/// A portable, immutable, pure expression definition. Every workflow value visible to the expression
/// is declared as a named parameter; the definition contains no execution context, delegate, or service.
/// </summary>
public sealed class ExpressionDefinition : IEquatable<ExpressionDefinition>
{
    private static readonly JsonSerializerOptions FingerprintSerializerOptions = new(JsonSerializerDefaults.Web);

    [JsonConstructor]
    public ExpressionDefinition(
        string language,
        string source,
        TypeReference resultType,
        IReadOnlyDictionary<string, ExpressionParameterBinding> parameters,
        JsonElement options,
        string capabilityProfile,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityProfile);
        if (options.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expression evaluator options must be a JSON object.", nameof(options));
        if (parameters.Keys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Expression parameter names cannot be blank.", nameof(parameters));
        if (parameters.Values.Any(binding => binding is null))
            throw new ArgumentException("Expression parameter bindings cannot be null.", nameof(parameters));

        Language = language;
        Source = source;
        ResultType = resultType;
        Parameters = new ReadOnlyDictionary<string, ExpressionParameterBinding>(
            parameters.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        Options = options.Clone();
        CapabilityProfile = capabilityProfile;
        Metadata = new ReadOnlyDictionary<string, string>(
            (metadata ?? new Dictionary<string, string>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    [JsonPropertyName("language")]
    public string Language { get; }

    [JsonPropertyName("source")]
    public string Source { get; }

    [JsonPropertyName("resultType")]
    public TypeReference ResultType { get; }

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, ExpressionParameterBinding> Parameters { get; }

    [JsonPropertyName("options")]
    public JsonElement Options { get; }

    [JsonPropertyName("capabilityProfile")]
    public string CapabilityProfile { get; }

    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string> Metadata { get; }

    [JsonIgnore]
    public string Fingerprint => ComputeFingerprint();

    public bool Equals(ExpressionDefinition? other) =>
        other is not null && StringComparer.Ordinal.Equals(Fingerprint, other.Fingerprint);

    public override bool Equals(object? obj) => obj is ExpressionDefinition other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Fingerprint);

    private string ComputeFingerprint()
    {
        var node = JsonSerializer.SerializeToNode(this, FingerprintSerializerOptions)
            ?? throw new InvalidOperationException("Expression definition serialization produced no canonical payload.");
        var canonical = SortNode(node).ToJsonString(FingerprintSerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static JsonNode SortNode(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => KeyValuePair.Create(item.Key, item.Value is null ? null : SortNode(item.Value)))),
        JsonArray array => new JsonArray(array.Select(item => item is null ? null : SortNode(item)).ToArray()),
        _ => node.DeepClone()
    };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LiteralExpressionParameterBinding), "Literal")]
[JsonDerivedType(typeof(WorkflowRequestExpressionParameterBinding), "WorkflowRequest")]
[JsonDerivedType(typeof(VariableExpressionParameterBinding), "Variable")]
[JsonDerivedType(typeof(ActivityResultExpressionParameterBinding), "ActivityResult")]
public abstract record ExpressionParameterBinding;

public sealed record LiteralExpressionParameterBinding : ExpressionParameterBinding
{
    [JsonConstructor]
    public LiteralExpressionParameterBinding(JsonElement value) => Value = value.Clone();

    [JsonPropertyName("value")]
    public JsonElement Value { get; }
}

public sealed record WorkflowRequestExpressionParameterBinding : ExpressionParameterBinding
{
    public WorkflowRequestExpressionParameterBinding(string memberKey, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberKey);
        MemberKey = memberKey;
        Path = path;
    }

    [JsonPropertyName("memberKey")]
    public string MemberKey { get; }

    [JsonPropertyName("path")]
    public string? Path { get; }
}

public sealed record VariableExpressionParameterBinding : ExpressionParameterBinding
{
    public VariableExpressionParameterBinding(string declaringScopeNodeId, string variableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringScopeNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        DeclaringScopeNodeId = declaringScopeNodeId;
        VariableKey = variableKey;
    }

    [JsonPropertyName("declaringScopeNodeId")]
    public string DeclaringScopeNodeId { get; }

    [JsonPropertyName("variableKey")]
    public string VariableKey { get; }
}

public sealed record ActivityResultExpressionParameterBinding : ExpressionParameterBinding
{
    public ActivityResultExpressionParameterBinding(string producerNodeId, string projectionKey, bool isOptional = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionKey);
        ProducerNodeId = producerNodeId;
        ProjectionKey = projectionKey;
        IsOptional = isOptional;
    }

    [JsonPropertyName("producerNodeId")]
    public string ProducerNodeId { get; }

    [JsonPropertyName("projectionKey")]
    public string ProjectionKey { get; }

    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; }
}

public static class ExpressionCapabilityProfiles
{
    public const string BindingPureV1 = "binding-pure-v1";
    public const string LegacyAmbientV1 = "legacy-ambient-v1";
}

/// <summary>A complete immutable parameter snapshot supplied to a pure evaluator.</summary>
public sealed class ExpressionEvaluationRequest
{
    public ExpressionEvaluationRequest(
        ExpressionDefinition definition,
        IReadOnlyDictionary<string, JsonElement> parameterValues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(parameterValues);
        var missing = definition.Parameters.Keys.Except(parameterValues.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var unknown = parameterValues.Keys.Except(definition.Parameters.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            throw new ArgumentException(
                $"Expression parameter values must exactly match the definition. Missing: [{string.Join(", ", missing)}]; unknown: [{string.Join(", ", unknown)}].",
                nameof(parameterValues));
        }

        Definition = definition;
        ParameterValues = new ReadOnlyDictionary<string, JsonElement>(
            parameterValues.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal));
        CancellationToken = cancellationToken;
    }

    public ExpressionDefinition Definition { get; }
    public IReadOnlyDictionary<string, JsonElement> ParameterValues { get; }
    public CancellationToken CancellationToken { get; }
}
