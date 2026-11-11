using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Durable compiled declaration describing how an activity input is evaluated at execution time.
/// </summary>
public sealed class RuntimeInputBinding
{
    public RuntimeInputBinding(
        string inputKey,
        ValueTypeDescriptor targetType,
        ValueProtectionPolicy effectivePolicy,
        RuntimeInputBindingSource source,
        ValueEnvelope? literal = null,
        RuntimeWorkflowRequestReference? workflowRequest = null,
        RuntimeVariableReference? variable = null,
        RuntimeActivityResultReference? activityResult = null,
        RuntimeExpressionBinding? expression = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        ValueConversionPlan? conversionPlan = null,
        RuntimeValueConversionRequest? conversionRequest = null,
        RuntimeSecretReference? secret = null)
        : this(inputKey, targetType, effectivePolicy, source, selection: null, literal, workflowRequest, variable, activityResult,
            expression, metadata, conversionPlan, conversionRequest, secret)
    {
    }

    /// <summary>
    /// Creates a binding that may read one member inside its source: <paramref name="selection"/> is required for a
    /// member source (<see cref="RuntimeInputBindingSource.WorkflowRequestMember"/>,
    /// <see cref="RuntimeInputBindingSource.VariableMember"/>, <see cref="RuntimeInputBindingSource.ActivityResultMember"/>)
    /// and refused for every other source.
    /// </summary>
    [JsonConstructor]
    public RuntimeInputBinding(
        string inputKey,
        ValueTypeDescriptor targetType,
        ValueProtectionPolicy effectivePolicy,
        RuntimeInputBindingSource source,
        RuntimeReadSelection? selection,
        ValueEnvelope? literal = null,
        RuntimeWorkflowRequestReference? workflowRequest = null,
        RuntimeVariableReference? variable = null,
        RuntimeActivityResultReference? activityResult = null,
        RuntimeExpressionBinding? expression = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        ValueConversionPlan? conversionPlan = null,
        RuntimeValueConversionRequest? conversionRequest = null,
        RuntimeSecretReference? secret = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputKey);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(effectivePolicy);
        ValidateCanonical(source, selection, literal, workflowRequest, variable, activityResult, expression, secret);

        InputName = inputKey;
        Selection = selection;
        TargetType = targetType;
        EffectivePolicy = effectivePolicy;
        Source = source;
        Literal = literal;
        LiteralValue = literal switch
        {
            { Presence: ValuePresence.Present } => literal.InlineValue,
            { Presence: ValuePresence.ExplicitNull } => JsonSerializer.SerializeToElement<object?>(null),
            _ => null
        };
        WorkflowRequest = workflowRequest;
        Variable = variable;
        ActivityResult = activityResult;
        Expression = expression;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        ConversionPlan = conversionPlan;
        ConversionRequest = conversionRequest;
        Secret = secret;
    }

    public string InputName { get; }
    public string InputKey => InputName;
    public ValueTypeDescriptor TargetType { get; }
    public ValueProtectionPolicy EffectivePolicy { get; }
    public RuntimeInputBindingSource Source { get; }
    public ValueEnvelope? Literal { get; }
    public RuntimeWorkflowRequestReference? WorkflowRequest { get; }
    public RuntimeVariableReference? Variable { get; }
    public RuntimeActivityResultReference? ActivityResult { get; }
    public JsonElement? LiteralValue { get; }
    public RuntimeExpressionBinding? Expression { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    /// <summary>Null preserves the legacy identity/retyping behavior of artifacts published before conversion plans.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValueConversionPlan? ConversionPlan { get; }
    /// <summary>
    /// Authored conversion intent retained only until a later publication linker can resolve a source contract.
    /// Published executable bindings should prefer <see cref="ConversionPlan"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeValueConversionRequest? ConversionRequest { get; }

    /// <summary>
    /// The secret reference of a <see cref="RuntimeInputBindingSource.SecretRead"/> binding. Omitted when null so
    /// bindings of every other source serialize and hash exactly as before secret reads existed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeSecretReference? Secret { get; }

    /// <summary>
    /// The member a member source reads inside its source value, or <see langword="null"/> for every other source.
    /// Omitted when null so bindings of every other source serialize and hash exactly as before member reads existed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeReadSelection? Selection { get; }

    private static void ValidateCanonical(
        RuntimeInputBindingSource source,
        RuntimeReadSelection? selection,
        ValueEnvelope? literal,
        RuntimeWorkflowRequestReference? workflowRequest,
        RuntimeVariableReference? variable,
        RuntimeActivityResultReference? activityResult,
        RuntimeExpressionBinding? expression,
        RuntimeSecretReference? secret)
    {
        var payloadCount =
            (literal is not null ? 1 : 0) +
            (workflowRequest is not null ? 1 : 0) +
            (variable is not null ? 1 : 0) +
            (activityResult is not null ? 1 : 0) +
            (expression is not null ? 1 : 0) +
            (secret is not null ? 1 : 0);

        if (payloadCount != 1)
            throw new ArgumentException("A canonical runtime input binding must carry exactly one role-owned source payload.");

        var valid = source switch
        {
            RuntimeInputBindingSource.Literal => literal is not null,
            RuntimeInputBindingSource.WorkflowRequest => workflowRequest is not null,
            RuntimeInputBindingSource.VariableRead => variable is not null,
            RuntimeInputBindingSource.ActivityResult => activityResult is not null,
            RuntimeInputBindingSource.Expression => expression is not null,
            RuntimeInputBindingSource.SecretRead => secret is not null,
            RuntimeInputBindingSource.WorkflowRequestMember => workflowRequest is not null,
            RuntimeInputBindingSource.VariableMember => variable is not null,
            RuntimeInputBindingSource.ActivityResultMember => activityResult is not null,
            _ => false
        };

        if (!valid)
            throw new ArgumentException($"Runtime input binding source '{source}' does not match its canonical payload.");

        var readsMember = IsMemberSource(source);
        if (readsMember && selection is null)
            throw new ArgumentException($"Runtime input binding source '{source}' reads a member and requires a read selection.");
        if (!readsMember && selection is not null)
            throw new ArgumentException($"Runtime input binding source '{source}' reads a whole value and cannot carry a read selection.");
        if (source == RuntimeInputBindingSource.WorkflowRequestMember && workflowRequest!.Path is not null)
            throw new ArgumentException("A workflow request member read selects its member through its read selection and cannot carry a legacy request path.");
        if (source == RuntimeInputBindingSource.ActivityResultMember && activityResult!.ProducerScopeId is not null)
            throw new ArgumentException("An activity result member read is identified by causal lineage and cannot declare a producer scope.");
    }

    internal static bool IsMemberSource(RuntimeInputBindingSource source) =>
        source is RuntimeInputBindingSource.WorkflowRequestMember
            or RuntimeInputBindingSource.VariableMember
            or RuntimeInputBindingSource.ActivityResultMember;

}

public sealed record RuntimeValueConversionRequest(
    ValueConversionMode Mode,
    ValueConversionProfileReference? Profile = null,
    ValueConversionLimits? Limits = null,
    JsonElement? Options = null);

public enum RuntimeInputBindingSource
{
    Literal,
    Expression,
    WorkflowRequest,
    VariableRead,
    ActivityResult,

    /// <summary>
    /// A stored secret, by reference. Only activation may resolve it: the persisted input snapshot records a withheld
    /// envelope (<see cref="ValuePresence.Withheld"/>) instead of the value.
    /// </summary>
    SecretRead,

    /// <summary>One member inside a workflow request member; carries the request payload and a read selection.</summary>
    WorkflowRequestMember = 6,

    /// <summary>One member inside a variable's value; carries the variable payload and a read selection.</summary>
    VariableMember = 7,

    /// <summary>One member inside an activity result projection; carries the result payload and a read selection.</summary>
    ActivityResultMember = 8
}

/// <summary>
/// A reference to a stored secret, as a <see cref="RuntimeInputBindingSource.SecretRead"/> binding carries it. It
/// mirrors the Secrets module's reference shape without referencing that module, and deliberately has no tenant:
/// the tenant always comes from the executing workflow instance, never from a binding.
/// </summary>
public sealed record RuntimeSecretReference
{
    public RuntimeSecretReference(string name, string? typeName = null, string? scope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName;
        Scope = string.IsNullOrWhiteSpace(scope) ? null : scope;
    }

    public string Name { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeName { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; }
}

public sealed class RuntimeWorkflowRequestReference
{
    public RuntimeWorkflowRequestReference(string memberKey, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberKey);

        MemberKey = memberKey;
        Path = path;
    }

    public string MemberKey { get; }
    public string? Path { get; }
}

public sealed class RuntimeVariableReference
{
    public RuntimeVariableReference(string variableKey, string declaringScopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringScopeId);

        VariableKey = variableKey;
        DeclaringScopeId = declaringScopeId;
    }

    public string VariableKey { get; }
    public string DeclaringScopeId { get; }
}

public sealed class RuntimeActivityResultReference
{
    /// <param name="producerExecutableNodeId">The producer node.</param>
    /// <param name="projectionKey">The declared result projection, or <c>$result</c>.</param>
    /// <param name="producerScopeId">
    /// The execution scope an existing document declares for the producer, or <see langword="null"/> for a reference
    /// that declares none. A declared scope must not be blank.
    /// </param>
    /// <param name="isOptional">Whether an unavailable producer resolves to no value instead of failing.</param>
    public RuntimeActivityResultReference(
        string producerExecutableNodeId,
        string projectionKey,
        string? producerScopeId,
        bool isOptional = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionKey);
        if (producerScopeId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(producerScopeId);

        ProducerExecutableNodeId = producerExecutableNodeId;
        ProjectionKey = projectionKey;
        ProducerScopeId = producerScopeId;
        IsOptional = isOptional;
    }

    public string ProducerExecutableNodeId { get; }
    public string ProjectionKey { get; }

    /// <summary>
    /// The producer's declared execution scope, kept for documents that declare one. When set, a producer that ran in
    /// another scope is not a candidate. <see langword="null"/> declares none: the producer is identified by causal
    /// lineage and branch and iteration frames alone, which publication admits only where the owning structure says
    /// that no earlier run of the producer can lead to the consumer. A null scope is left out of the stored reference.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProducerScopeId { get; }

    public bool IsOptional { get; }
}

public sealed class RuntimeExpressionBinding
{
    public RuntimeExpressionBinding(
        string language,
        string expression,
        RuntimeValueTypeDescriptor? resultType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyDictionary<string, ExpressionParameterBinding>? parameters = null,
        JsonElement? options = null,
        string? capabilityProfile = null,
        IReadOnlyDictionary<string, ValueConversionPlan>? parameterConversionPlans = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        Language = language;
        Expression = expression;
        ResultType = resultType;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        Parameters = new ReadOnlyDictionary<string, ExpressionParameterBinding>(
            (parameters ?? new Dictionary<string, ExpressionParameterBinding>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        ParameterConversionPlans = new ReadOnlyDictionary<string, ValueConversionPlan>(
            (parameterConversionPlans ?? new Dictionary<string, ValueConversionPlan>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        var unknownPlans = ParameterConversionPlans.Keys.Except(Parameters.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (unknownPlans.Length > 0)
            throw new ArgumentException($"Runtime expression conversion plans reference unknown parameters: [{string.Join(", ", unknownPlans)}].", nameof(parameterConversionPlans));
        Options = options?.Clone() ?? JsonSerializer.SerializeToElement(new { });
        if (Options.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Runtime expression evaluator options must be a JSON object.", nameof(options));
        CapabilityProfile = string.IsNullOrWhiteSpace(capabilityProfile)
            ? ExpressionCapabilityProfiles.BindingPureV1
            : capabilityProfile;
    }

    // System.Text.Json binds a constructor parameter to a property only when their declared types match
    // exactly, and Nullable<JsonElement> is not JsonElement. The public ctor's nullable 'options' therefore
    // cannot serve as the deserialization constructor. This private ctor's 'options' is exactly JsonElement
    // so every parameter binds; it chains through the public ctor to keep all invariants in one place.
    [JsonConstructor]
    private RuntimeExpressionBinding(
        string language,
        string expression,
        RuntimeValueTypeDescriptor? resultType,
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyDictionary<string, ExpressionParameterBinding>? parameters,
        JsonElement options,
        string? capabilityProfile,
        IReadOnlyDictionary<string, ValueConversionPlan>? parameterConversionPlans)
        : this(language, expression, resultType, metadata, parameters, (JsonElement?)options, capabilityProfile, parameterConversionPlans)
    {
    }

    public string Language { get; }
    public string Expression { get; }
    public RuntimeValueTypeDescriptor? ResultType { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public IReadOnlyDictionary<string, ExpressionParameterBinding> Parameters { get; }
    public IReadOnlyDictionary<string, ValueConversionPlan> ParameterConversionPlans { get; }
    public JsonElement Options { get; }
    public string CapabilityProfile { get; }
}

public sealed record RuntimeResolvedInput(
    string InputName,
    RuntimeInputBindingSource Source,
    RuntimeExpressionBinding? Expression)
{
    /// <summary>Canonical materialized source including its effective protection policy and payload location.</summary>
    public ValueEnvelope? Envelope { get; init; }
}
