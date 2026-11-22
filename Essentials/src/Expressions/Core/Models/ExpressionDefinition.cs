using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Contracts;
using Elsa.Primitives.Models;

namespace Elsa.Expressions.Core.Models;

/// <summary>
/// A portable, immutable, pure expression definition. Every workflow value visible to the expression
/// is declared as a named parameter; the definition contains no execution context, delegate, or service.
/// </summary>
public sealed class ExpressionDefinition : IEquatable<ExpressionDefinition>
{
    private static readonly JsonSerializerOptions FingerprintSerializerOptions = new(Elsa.Serialization.Core.JsonBaseline.Options);

    [JsonConstructor]
    public ExpressionDefinition(
        string language,
        string source,
        TypeReference resultType,
        IReadOnlyDictionary<string, ExpressionParameterBinding> parameters,
        JsonElement options,
        string capabilityProfile,
        IReadOnlyDictionary<string, string>? metadata = null,
        VariableReadFallback? variableReadFallback = null)
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
        ExpressionParameterSurfaces.Validate(parameters, nameof(parameters));

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
        VariableReadFallback = variableReadFallback;
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

    /// <summary>
    /// Present when publish found a computed or opaque variable read (spec 1222 FR-008, FR-009): the conservative read
    /// report the runtime's recorded lookup serves. Absent on an expression with only literal reads.
    /// </summary>
    [JsonPropertyName("variableReadFallback")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VariableReadFallback? VariableReadFallback { get; }

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
[JsonDerivedType(typeof(WorkflowInstanceExpressionParameterBinding), "WorkflowInstance")]
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
    [JsonConstructor]
    public VariableExpressionParameterBinding(string declaringScopeNodeId, string variableKey, string? surface = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringScopeNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        if (surface is not null && !StringComparer.Ordinal.Equals(surface, ExpressionParameterSurfaces.Variables))
            throw new ArgumentException($"Variable parameter surface '{surface}' is not '{ExpressionParameterSurfaces.Variables}'; omit it for '{ExpressionParameterSurfaces.Args}'.", nameof(surface));
        DeclaringScopeNodeId = declaringScopeNodeId;
        VariableKey = variableKey;
        Surface = surface;
    }

    [JsonPropertyName("declaringScopeNodeId")]
    public string DeclaringScopeNodeId { get; }

    [JsonPropertyName("variableKey")]
    public string VariableKey { get; }

    /// <summary>
    /// Where a script sees the parameter (spec 1222 FR-003, ADR 0089): <see langword="null"/> for the <c>args</c> root,
    /// <see cref="ExpressionParameterSurfaces.Variables"/> for a read publish pinned from <c>variables.X</c>,
    /// <c>variables['X']</c> or <c>getVariable('X')</c>. Such a parameter is keyed <c>variables.&lt;name&gt;</c>.
    /// </summary>
    [JsonPropertyName("surface")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Surface { get; }

    /// <summary>Whether a script reads this parameter through the <c>variables</c> root.</summary>
    [JsonIgnore]
    public bool IsVariablesSurface => Surface is not null;
}

/// <summary>The script roots a declared parameter can appear under (spec 1222 FR-003).</summary>
public static class ExpressionParameterSurfaces
{
    public const string Args = "args";
    public const string Variables = "variables";

    /// <summary>The prefix of the parameter key of a <see cref="Variables"/>-surface parameter.</summary>
    public const string VariablesKeyPrefix = "variables.";

    /// <summary>The parameter key a pinned read of the named variable uses.</summary>
    public static string VariablesKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return VariablesKeyPrefix + name;
    }

    /// <summary>Whether a parameter key names a <c>variables</c>-surface parameter, and the variable name it reads.</summary>
    public static bool TryGetVariableName(string parameterKey, out string name)
    {
        ArgumentNullException.ThrowIfNull(parameterKey);
        if (parameterKey.StartsWith(VariablesKeyPrefix, StringComparison.Ordinal) && parameterKey.Length > VariablesKeyPrefix.Length)
        {
            name = parameterKey[VariablesKeyPrefix.Length..];
            return true;
        }

        name = "";
        return false;
    }

    /// <summary>
    /// Refuses a parameter map in which a <c>variables.</c> key and the <c>variables</c> surface disagree: a key
    /// <c>variables.&lt;name&gt;</c> must be a <see cref="VariableExpressionParameterBinding"/> on the variables surface,
    /// and a variables-surface binding must use such a key.
    /// </summary>
    public static void Validate(IReadOnlyDictionary<string, ExpressionParameterBinding> parameters, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        foreach (var (key, binding) in parameters)
        {
            var keyed = TryGetVariableName(key, out _);
            var surfaced = binding is VariableExpressionParameterBinding { IsVariablesSurface: true };
            if (keyed != surfaced)
                throw new ArgumentException(
                    $"Expression parameter '{key}': a parameter keyed '{VariablesKeyPrefix}<name>' must be a Variable binding on the '{Variables}' surface, and a '{Variables}'-surface binding must use such a key.",
                    parameterName);
        }
    }
}

/// <summary>
/// The fallback marker of a compiled expression (spec 1222 FR-008, FR-009, research R-2): publish found a computed or
/// opaque variable read (<c>variables[expr]</c>, <c>getVariable(expr)</c>, <c>variables</c> used as a value, or a host
/// function that reads variables). It carries the conservative read report: every variable visible at that point,
/// nearest scope first. The runtime serves such reads with a recorded lookup; an expression without the marker pays no
/// fallback cost.
/// </summary>
public sealed record VariableReadFallback
{
    [JsonConstructor]
    public VariableReadFallback(IReadOnlyList<VariableReadCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Any(candidate => candidate is null))
            throw new ArgumentException("Variable read candidates cannot be null.", nameof(candidates));
        Candidates = candidates.ToArray();
    }

    /// <summary>The variables the expression could read, nearest scope first, each name once (the innermost declaration).</summary>
    [JsonPropertyName("candidates")]
    public IReadOnlyList<VariableReadCandidate> Candidates { get; }
}

/// <summary>One variable a computed or opaque read could reach.</summary>
public sealed record VariableReadCandidate
{
    [JsonConstructor]
    public VariableReadCandidate(string name, string declaringScopeNodeId, string variableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringScopeNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        Name = name;
        DeclaringScopeNodeId = declaringScopeNodeId;
        VariableKey = variableKey;
    }

    [JsonPropertyName("name")]
    public string Name { get; }

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

/// <summary>
/// Reads metadata of the running workflow instance as a declared parameter, resolved from committed runtime state:
/// its correlation ID (null when it has none) or its instance ID. ADR 0045, amendment for issue #2717.
/// </summary>
public sealed record WorkflowInstanceExpressionParameterBinding : ExpressionParameterBinding
{
    [JsonConstructor]
    public WorkflowInstanceExpressionParameterBinding(string member)
    {
        if (member is not (Members.CorrelationId or Members.InstanceId))
            throw new ArgumentException($"Workflow instance member '{member}' is not one of '{Members.CorrelationId}' or '{Members.InstanceId}'.", nameof(member));
        Member = member;
    }

    [JsonPropertyName("member")]
    public string Member { get; }

    /// <summary>The instance metadata an expression can read.</summary>
    public static class Members
    {
        public const string CorrelationId = "correlationId";
        public const string InstanceId = "instanceId";
    }
}

public static class ExpressionCapabilityProfiles
{
    public const string BindingReadonlyV1 = "binding-readonly-v1";
}

/// <summary>A complete immutable parameter snapshot supplied to a pure evaluator.</summary>
public sealed record ExpressionEvaluationRequest
{
    public ExpressionEvaluationRequest(
        ExpressionDefinition definition,
        IReadOnlyDictionary<string, JsonElement> parameterValues,
        CancellationToken cancellationToken)
        : this(definition, parameterValues, variableReader: null, cancellationToken)
    {
    }

    /// <summary>
    /// Builds an evaluation request whose script may also read variables it does not name literally (spec 1222 FR-008):
    /// <paramref name="variableReader"/> is the host-side lookup that serves computed and opaque reads. The host
    /// supplies one only for an expression that carries a <see cref="Models.VariableReadFallback"/>; it loaded the
    /// candidates' values before evaluation, so the engine's reads stay synchronous, and it records every read it serves.
    /// </summary>
    public ExpressionEvaluationRequest(
        ExpressionDefinition definition,
        IReadOnlyDictionary<string, JsonElement> parameterValues,
        IExpressionVariableReader? variableReader,
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
        Capabilities = ExpressionEvaluationCapabilities.Resolve(definition.CapabilityProfile);
        ParameterValues = new ReadOnlyDictionary<string, JsonElement>(
            parameterValues.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal));
        VariableReader = variableReader;
        CancellationToken = cancellationToken;
    }

    public ExpressionDefinition Definition { get; }
    public ExpressionEvaluationCapabilities Capabilities { get; }

    /// <summary>
    /// Every declared parameter's pinned value. Keys <c>variables.&lt;name&gt;</c> are the <c>variables</c> root
    /// (spec 1222 FR-003); the rest are <c>args</c>.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> ParameterValues { get; }

    /// <summary>
    /// The host-side lookup for variable reads publish could not pin, or <see langword="null"/> when the expression has
    /// no computed or opaque read (FR-013).
    /// </summary>
    public IExpressionVariableReader? VariableReader { get; }

    /// <summary>
    /// Where the evaluator reports warnings (spec 1228, the shared run-time warning channel); <see langword="null"/>
    /// when the caller records none. The runtime records them on the activity execution.
    /// </summary>
    public IExecutionWarningSink? Warnings { get; init; }

    public CancellationToken CancellationToken { get; }

    /// <summary>The pinned <c>variables</c>-root values, by variable name.</summary>
    public IReadOnlyDictionary<string, JsonElement> PinnedVariableValues() =>
        ParameterValues
            .Where(item => ExpressionParameterSurfaces.TryGetVariableName(item.Key, out _))
            .ToDictionary(item => item.Key[ExpressionParameterSurfaces.VariablesKeyPrefix.Length..], item => item.Value, StringComparer.Ordinal);

    /// <summary>The <c>args</c>-root values, by parameter name.</summary>
    public IReadOnlyDictionary<string, JsonElement> ArgumentValues() =>
        ParameterValues
            .Where(item => !ExpressionParameterSurfaces.TryGetVariableName(item.Key, out _))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
}
