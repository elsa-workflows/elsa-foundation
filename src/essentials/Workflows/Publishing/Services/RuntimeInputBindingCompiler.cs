using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using ActivityValuePolicy = Elsa.Activities.Runtime.Core.Models.ActivityValuePolicy;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using RuntimeActivityDescriptor = Elsa.Activities.Runtime.Core.Models.RuntimeActivityDescriptor;
using RuntimeActivityInputContract = Elsa.Activities.Runtime.Core.Models.ActivityInputContract;
using WellKnownRuntimeActivityConsumers = Elsa.Activities.Runtime.Core.Models.WellKnownRuntimeActivityConsumers;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Compiles a single authored activity input into its durable <see cref="RuntimeInputBinding"/>. Owns the
/// closed role-owned sources, authored-type resolution against the well-known type registry, and literal
/// value conversion. Extracted from
/// <see cref="WorkflowExecutableCompiler"/> (#418) so binding compilation is independently
/// unit-testable and can evolve without touching activity-tree compilation.
/// </summary>
public sealed class RuntimeInputBindingCompiler(
    IWellKnownTypeRegistry wellKnownTypeRegistry,
    ValueConversionPlanResolver? conversionPlanResolver = null)
{
    private const string LiteralExpressionType = "Literal";
    private const string ObjectExpressionType = "Object";
    private const string VariableExpressionType = "Variable";
    private const string WorkflowRequestExpressionType = "WorkflowRequest";
    private const string ActivityResultExpressionType = "ActivityResult";
    private const string DefaultExpressionType = "Default";
    private const string ReferenceKeyMetadataKey = "referenceKey";

    /// <summary>
    /// The authored expression type of a secret reference. It duplicates the Secrets module's expression type name
    /// on purpose, so this project needs no reference to that module.
    /// </summary>
    public const string SecretExpressionType = "Secret";

    // A resolved secret is text: the conversion plan of a secret read starts from a text value.
    private static readonly ValueTypeDescriptor SecretSourceType = new("String");

    // A secret read is always sensitive and requires encryption, whatever the input declares.
    private static readonly ValueProtectionPolicy SecretPolicyMinimum = new(
        DurableValueLifecycle.None,
        DurableValueStorage.None,
        isSensitive: true,
        requiresEncryption: true);

    private readonly ValueConversionPlanResolver resolvedConversionPlanResolver = conversionPlanResolver ?? new(wellKnownTypeRegistry: wellKnownTypeRegistry);

    public IReadOnlyDictionary<string, RuntimeInputBinding> CompileAll(
        string nodeId,
        IEnumerable<InputDefinition> inputDefinitions,
        IEnumerable<ArgumentState> inputStates)
    {
        var definitions = inputDefinitions.ToArray();
        var states = inputStates.ToArray();
        var definitionsByKey = definitions.ToDictionary(x => x.ReferenceKey, StringComparer.Ordinal);
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            if (!definitionsByKey.TryGetValue(state.ReferenceKey, out var definition))
                throw new ArgumentException($"Activity node '{nodeId}' input '{state.ReferenceKey}' does not match any declared input.");

            AddBinding(bindings, Compile(nodeId, definition, state), nodeId, state.ReferenceKey);
        }

        foreach (var definition in definitions.Where(x => !bindings.ContainsKey(x.ReferenceKey)))
            AddBinding(bindings, CompileUnbound(nodeId, definition, DeclaredPolicy(definition)), nodeId, definition.ReferenceKey);

        return bindings;
    }

    public IReadOnlyDictionary<string, RuntimeInputBinding> CompileAll(
        string nodeId,
        IEnumerable<RuntimeActivityInputContract> inputContracts,
        IEnumerable<ArgumentState> inputStates)
    {
        var contracts = inputContracts.ToArray();
        var states = inputStates.ToArray();
        var contractsByKey = contracts.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            if (!contractsByKey.TryGetValue(state.ReferenceKey, out var contract))
                throw new ArgumentException($"Activity node '{nodeId}' input '{state.ReferenceKey}' does not match any pinned activity input contract.");

            AddBinding(bindings, CompileAuthored(nodeId, ToInputDefinition(contract), state, DeclaredPolicy(contract)), nodeId, contract.Key);
        }

        foreach (var contract in contracts.Where(x => !bindings.ContainsKey(x.Key)))
            AddBinding(bindings, CompileUnbound(nodeId, ToInputDefinition(contract), DeclaredPolicy(contract)), nodeId, contract.Key);

        return bindings;
    }

    public RuntimeInputBinding Compile(string nodeId, InputDefinition inputDefinition, ArgumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return CompileAuthored(nodeId, inputDefinition, state, DeclaredPolicy(inputDefinition));
    }

    public RuntimeInputBinding Compile(string nodeId, InputDefinition inputDefinition, ArgumentValue value) =>
        CompileBound(nodeId, inputDefinition, value, DeclaredPolicy(inputDefinition), conversion: null);

    /// <summary>
    /// The effective policy of an activity input (spec 188, FR-007): its storage policy with the activity's
    /// sensitivity declaration applied, combined with the author's per-binding choice when the input is bound
    /// (<paramref name="state"/> is null for an unbound input). Publication pins this same policy into the activity's
    /// input contract, so the contract and the binding of an input cannot disagree.
    /// </summary>
    public static ActivityValuePolicy EffectivePolicy(string nodeId, InputDefinition inputDefinition, ArgumentState? state)
    {
        ArgumentNullException.ThrowIfNull(inputDefinition);
        var declared = DeclaredPolicy(inputDefinition);
        return state is null
            ? declared
            : ValuePolicyCombiner.CombineAuthoredInput(declared, state.StorageDriverType, state.IsSensitive, InputRole(nodeId, inputDefinition.ReferenceKey));
    }

    private static ActivityValuePolicy DeclaredPolicy(InputDefinition inputDefinition) =>
        ValuePolicyCombiner.ApplyInputDeclaration(
            ValuePolicyCombiner.FromAuthoredStorage(inputDefinition.StorageDriverType),
            inputDefinition.IsSensitive == true,
            inputDefinition.IsCredential == true);

    // A pinned contract's policy already carries what publication pinned. Its credential flag is applied again from
    // the flag itself, which is read explicitly and never inferred from the policy's RequiresEncryption.
    private static ActivityValuePolicy DeclaredPolicy(RuntimeActivityInputContract contract) =>
        ValuePolicyCombiner.ApplyInputDeclaration(contract.Policy, isSensitive: false, contract.IsCredential);

    /// <summary>
    /// Compiles an authored binding under its effective policy. On an input whose effective policy requires encryption,
    /// a binding that carries no value (<see cref="ArgumentState.IsBound"/>, the definition the design-time required-input
    /// check uses) compiles exactly as an unbound input does (spec 188 edge case, FR-010), so an empty credential field
    /// does not fail publication. A secret reference and a request for the declared default keep their own handling.
    /// </summary>
    private RuntimeInputBinding CompileAuthored(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentState state,
        ActivityValuePolicy declaredPolicy)
    {
        var effective = ValuePolicyCombiner.CombineAuthoredInput(
            declaredPolicy,
            state.StorageDriverType,
            state.IsSensitive,
            InputRole(nodeId, inputDefinition.ReferenceKey));
        var compilesAsUnbound = effective.RequiresEncryption
                                && !state.IsBound()
                                && !IsSecretBinding(state.Value)
                                && !IsDefaultRequest(state.Value);
        return compilesAsUnbound
            ? CompileUnbound(nodeId, inputDefinition, declaredPolicy)
            : CompileBound(nodeId, inputDefinition, state.Value, effective, state.Conversion);
    }

    private static bool IsDefaultRequest(ArgumentValue? value) =>
        string.Equals(value?.ExpressionType, DefaultExpressionType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compiles an input the author left unbound: its declared default, else an omitted optional input, else the
    /// missing-required-input diagnostic.
    /// </summary>
    private RuntimeInputBinding CompileUnbound(string nodeId, InputDefinition inputDefinition, ActivityValuePolicy declaredPolicy) =>
        inputDefinition.DefaultValue.HasValue
            ? CompileDefault(nodeId, inputDefinition, declaredPolicy, new ArgumentValue(null, DefaultExpressionType))
            : !inputDefinition.IsRequired
                ? CompileOmitted(inputDefinition, ValuePolicyCombiner.ToProtectionPolicy(declaredPolicy))
                : throw MissingRequiredInput(nodeId, inputDefinition.ReferenceKey);

    /// <summary>
    /// Compiles an authored value under its effective policy. An input whose effective policy requires encryption
    /// accepts only a secret reference (<c>VF-ACT-011</c>). That is decided before the value is compiled, so a refused
    /// binding never reaches a conversion diagnostic, some of which describe the authored value.
    /// </summary>
    private RuntimeInputBinding CompileBound(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ActivityValuePolicy effectivePolicy,
        AuthoredValueConversionRequest? conversion)
    {
        if (effectivePolicy.RequiresEncryption && !IsSecretBinding(value))
            throw SecretBindingDiagnostics.EncryptionRequiredBindingRefused(nodeId, inputDefinition.ReferenceKey);

        return Compile(nodeId, inputDefinition, value, ValuePolicyCombiner.ToProtectionPolicy(effectivePolicy), conversion);
    }

    /// <summary>
    /// Compiles the declared default of an unbound input. A default is a literal, so an input whose policy requires
    /// encryption refuses it (<c>VF-ACT-011</c>) just as it refuses an authored literal.
    /// </summary>
    private RuntimeInputBinding CompileDefault(
        string nodeId,
        InputDefinition inputDefinition,
        ActivityValuePolicy policy,
        ArgumentValue defaultValue)
    {
        if (policy.RequiresEncryption)
            throw SecretBindingDiagnostics.EncryptionRequiredDefaultRefused(nodeId, inputDefinition.ReferenceKey);

        return Compile(nodeId, inputDefinition, defaultValue, ValuePolicyCombiner.ToProtectionPolicy(policy), conversion: null);
    }

    private RuntimeInputBinding CompileOmitted(InputDefinition inputDefinition, ValueProtectionPolicy policy)
    {
        var inputType = ResolveInputType(inputDefinition);
        var targetType = ToValueTypeDescriptor(inputDefinition);
        if (!AcceptsNull(inputDefinition))
        {
            // An optional input the author didn't bind, whose target cannot represent omission via null, is pinned to
            // its CLR default when the type has a well-defined one (value types: bool → false, enum → its zero member,
            // TimeSpan → 00:00:00). This resolves the VF-ACT-003 contradiction where the design contract marks an input
            // optional but the non-nullable CLR type refuses omission, so a UI that pins no default can still publish (#925).
            // A non-nullable reference type has no fabricable non-null default, so it remains a hard contract error.
            if (!TryCreateClrDefault(inputType, out var defaultValue))
            {
                throw new ArgumentException(
                    $"VF-ACT-003: Optional input '{inputDefinition.ReferenceKey}' with non-nullable type alias '{inputDefinition.Type.Alias}' " +
                    "requires an authored binding or pinned default.",
                    nameof(inputDefinition));
            }

            var pinnedLiteral = JsonSerializer.SerializeToElement(defaultValue, inputType);
            return new RuntimeInputBinding(
                inputKey: inputDefinition.ReferenceKey,
                targetType: targetType,
                effectivePolicy: policy,
                source: RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(targetType, pinnedLiteral, policy),
                metadata: BuildInputMetadata(inputDefinition));
        }

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: targetType,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Absent(targetType, policy),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static InputDefinition ToInputDefinition(RuntimeActivityInputContract contract) => new(
        contract.Key,
        contract.Name,
        contract.Type.ToTypeReference(),
        contract.Policy.StorageProfile,
        contract.Name,
        Category: null,
        IsNullable: contract.IsNullable,
        IsRequired: contract.IsRequired,
        DefaultValue: contract.DefaultValue,
        DefaultSyntax: contract.HasDefault ? LiteralExpressionType : null);

    private static void AddBinding(
        IDictionary<string, RuntimeInputBinding> bindings,
        RuntimeInputBinding binding,
        string nodeId,
        string referenceKey)
    {
        if (!bindings.TryAdd(binding.InputName, binding))
        {
            throw new ArgumentException(
                $"VF-ACT-003: Activity node '{nodeId}' declares duplicate input '{referenceKey}'. " +
                "Every published input must lower to exactly one canonical binding.");
        }
    }

    private static ArgumentException MissingRequiredInput(string nodeId, string referenceKey) => new(
        $"VF-ACT-003: Activity node '{nodeId}' omits required input '{referenceKey}', which has no pinned default. " +
        "Every published input must lower to exactly one canonical binding.");

    /// <summary>
    /// Produces the CLR default for a non-nullable value type (the natural pinned default for an omitted optional
    /// input). Returns false for reference types and for the unresolved <see cref="object"/> fallback, where no
    /// non-null default can be fabricated.
    /// </summary>
    private static bool TryCreateClrDefault(Type inputType, out object? defaultValue)
    {
        if (inputType.IsValueType && inputType != typeof(void))
        {
            defaultValue = Activator.CreateInstance(inputType);
            return true;
        }

        defaultValue = null;
        return false;
    }

    private RuntimeInputBinding Compile(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy effectivePolicy,
        AuthoredValueConversionRequest? conversion)
    {
        if (string.Equals(value.ExpressionType, LiteralExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileLiteralInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

        // Studio's object editor serializes arrays and objects as JSON under the "Object" syntax. The value is
        // authored data, not an executable expression, so preserve it as a durable literal. This is particularly
        // important for publish-time trigger metadata such as HttpEndpoint.SupportedMethods, which must be known
        // before a workflow instance exists.
        if (string.Equals(value.ExpressionType, ObjectExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileObjectInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

        if (string.Equals(value.ExpressionType, VariableExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileVariableInput(nodeId, inputDefinition, value, effectivePolicy);

        if (string.Equals(value.ExpressionType, WorkflowRequestExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileWorkflowRequestInput(nodeId, inputDefinition, value, effectivePolicy);

        if (string.Equals(value.ExpressionType, ActivityResultExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileActivityResultInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

        if (string.Equals(value.ExpressionType, DefaultExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileDefaultInput(nodeId, inputDefinition, effectivePolicy);

        if (IsSecretBinding(value))
            return CompileSecretInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

        return CompileExpressionInput(nodeId, inputDefinition, value, effectivePolicy);
    }

    /// <summary>True when the authored value is a secret reference.</summary>
    public static bool IsSecretBinding(ArgumentValue? value) =>
        string.Equals(value?.ExpressionType, SecretExpressionType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Refuses (<c>VF-ACT-012</c>) a secret reference on a node that would read the value anywhere but CLR
    /// activation, or on an input the activity type declares it persists, returns or reads at publish. Every caller
    /// passes all of the node's inputs at once, before any of them is compiled, so the refusal wins over a conversion
    /// refusal of any input of the node and names the ordinally first secret input.
    /// </summary>
    public void EnsureSecretBindingsAdmissible(
        string nodeId,
        RuntimeActivityDescriptor descriptor,
        IEnumerable<ArgumentState> inputStates)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(inputStates);
        var secretInputKeys = SecretInputKeys(inputStates);
        if (secretInputKeys.Length == 0)
            return;

        var firstKey = secretInputKeys[0];
        if (!StringComparer.Ordinal.Equals(descriptor.ConsumerKey, WellKnownRuntimeActivityConsumers.ClrActivity))
            throw SecretBindingDiagnostics.NonClrConsumerRefused(nodeId, firstKey, descriptor.ConsumerKey);

        var activityType = ClrActivityTypeResolver.Resolve(wellKnownTypeRegistry, descriptor)
            ?? throw SecretBindingDiagnostics.UnresolvedActivityTypeRefused(nodeId, firstKey);
        if (typeof(IRuntimeActivityCheckpointParticipant).IsAssignableFrom(activityType))
            throw SecretBindingDiagnostics.CheckpointParticipantRefused(nodeId, firstKey);

        var refusals = activityType.GetCustomAttributes<RefusesSecretBindingAttribute>(inherit: true)
            .GroupBy(attribute => attribute.InputKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Reason, StringComparer.Ordinal);
        var valueOutcomesInputKey = activityType.GetCustomAttribute<ActivityValueOutcomesAttribute>(inherit: true)?.InputKey;
        foreach (var inputKey in secretInputKeys)
        {
            if (refusals.TryGetValue(inputKey, out var reason))
                throw SecretBindingDiagnostics.SecretBindingRefused(nodeId, inputKey, reason);
            if (StringComparer.Ordinal.Equals(inputKey, valueOutcomesInputKey))
                throw SecretBindingDiagnostics.ValueOutcomesInputRefused(nodeId, inputKey);
        }
    }

    /// <summary>
    /// The keys of the inputs bound to a secret reference, in ordinal order, so a refusal names the same input
    /// whatever order the inputs were authored in.
    /// </summary>
    public static string[] SecretInputKeys(IEnumerable<ArgumentState> inputStates) =>
        inputStates
            .Where(state => IsSecretBinding(state.Value))
            .Select(state => state.ReferenceKey)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Compiles a secret reference into a <see cref="RuntimeInputBindingSource.SecretRead"/> binding. Nothing is
    /// resolved here. The conversion plan is resolved from a text value to the input's type with the same resolver the
    /// literal paths use, so an input that cannot hold text is refused with its <c>VF-COER-001</c>, for every secret
    /// type and in every host.
    /// </summary>
    private RuntimeInputBinding CompileSecretInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy effectivePolicy,
        AuthoredValueConversionRequest? conversion)
    {
        var reference = ParseSecretReference(nodeId, inputDefinition, value);
        var targetType = ToValueTypeDescriptor(inputDefinition);
        var conversionPlan = resolvedConversionPlanResolver.Resolve(
            SecretSourceType,
            ValueRepresentation.TextValue,
            targetType,
            AuthoredValueConversionMapper.Mode(conversion),
            AuthoredValueConversionMapper.Profile(conversion),
            AuthoredValueConversionMapper.Limits(conversion),
            AuthoredValueConversionMapper.Options(conversion),
            InputBindingContext(nodeId, inputDefinition));

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: targetType,
            effectivePolicy: ValuePolicyCombiner.Combine(
                effectivePolicy,
                SecretPolicyMinimum,
                InputRole(nodeId, inputDefinition.ReferenceKey)),
            source: RuntimeInputBindingSource.SecretRead,
            metadata: BuildInputMetadata(inputDefinition),
            conversionPlan: conversionPlan,
            secret: reference);
    }

    // The messages name the node, the input and the missing member, never the authored payload.
    private static RuntimeSecretReference ParseSecretReference(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
        var payload = RequireObjectPayload(nodeId, inputDefinition, value, SecretExpressionType);
        return new RuntimeSecretReference(
            RequireStringProperty(nodeId, inputDefinition, payload, SecretExpressionType, "name"),
            ReadOptionalStringProperty(payload, "typeName", NonText),
            ReadOptionalStringProperty(payload, "scope", NonText));

        ArgumentException NonText(string propertyName) => new(
            $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{SecretExpressionType}' but carries a non-text '{propertyName}'.");
    }

    /// <summary>
    /// Compiles the existing authored <c>Variable</c> syntax into the canonical variable-read role.
    /// A missing declaring scope retains the historical workflow-scope meaning.
    /// </summary>
    private RuntimeInputBinding CompileVariableInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value, ValueProtectionPolicy effectivePolicy)
    {
        var reference = ParseVariableReference(nodeId, inputDefinition, value.Value);

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference(
                reference.ReferenceKey,
                reference.DeclaringScopeId ?? VariableReference.WorkflowScopeId),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static RuntimeInputBinding CompileWorkflowRequestInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value, ValueProtectionPolicy effectivePolicy)
    {
        var payload = RequireObjectPayload(nodeId, inputDefinition, value, WorkflowRequestExpressionType);
        var memberKey = RequireStringProperty(nodeId, inputDefinition, payload, WorkflowRequestExpressionType, "memberKey");
        var path = ReadOptionalStringProperty(payload, "path");

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference(memberKey, path),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static RuntimeInputBinding CompileActivityResultInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy effectivePolicy,
        AuthoredValueConversionRequest? conversion)
    {
        var payload = RequireObjectPayload(nodeId, inputDefinition, value, ActivityResultExpressionType);
        var producerNodeId = RequireStringProperty(nodeId, inputDefinition, payload, ActivityResultExpressionType, "producerNodeId");
        var projectionKey = RequireStringProperty(nodeId, inputDefinition, payload, ActivityResultExpressionType, "projectionKey");
        var producerScopeId = RequireStringProperty(nodeId, inputDefinition, payload, ActivityResultExpressionType, "producerScopeId");
        var isOptional = payload.TryGetProperty("isOptional", out var optionalElement) &&
                         optionalElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                         optionalElement.GetBoolean();

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference(producerNodeId, projectionKey, producerScopeId, isOptional),
            metadata: BuildInputMetadata(inputDefinition),
            conversionRequest: AuthoredValueConversionMapper.ToRuntimeRequest(conversion));
    }

    private RuntimeInputBinding CompileDefaultInput(string nodeId, InputDefinition inputDefinition, ValueProtectionPolicy effectivePolicy)
    {
        if (!inputDefinition.DefaultValue.HasValue)
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' requests its contract default, but the pinned input contract declares no default.");

        var defaultSyntax = inputDefinition.DefaultSyntax;
        if (string.IsNullOrWhiteSpace(defaultSyntax) ||
            string.Equals(defaultSyntax, LiteralExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileLiteralInput(nodeId, inputDefinition, new ArgumentValue(inputDefinition.DefaultValue.Value, LiteralExpressionType), effectivePolicy, conversion: null);

        if (string.Equals(defaultSyntax, ObjectExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileObjectInput(nodeId, inputDefinition, new ArgumentValue(inputDefinition.DefaultValue.Value, ObjectExpressionType), effectivePolicy, conversion: null);

        throw new ArgumentException(
            $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' default syntax '{defaultSyntax}' is executable. Canonical activity defaults must be pinned literals.");
    }

    private static VariableReference ParseVariableReference(string nodeId, InputDefinition inputDefinition, object? value)
    {
        var unwrapped = value is JsonElement jsonElement ? jsonElement : JsonSerializer.SerializeToElement(value);
        if (!VariableReference.TryParse(unwrapped, out var reference) || reference is null)
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type 'Variable' but carries no resolvable variable reference (a reference key is required).");

        return reference;
    }

    private RuntimeInputBinding CompileLiteralInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy policy,
        AuthoredValueConversionRequest? conversion)
    {
        if (AuthoredValueConversionMapper.IsExplicitFormattedContent(conversion))
            return CompileFormattedLiteralInput(nodeId, inputDefinition, value, policy, conversion!);

        var inputType = ResolveInputType(inputDefinition);
        object? converted;
        try
        {
            converted = ConvertLiteral(value.Value, inputType);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException or OverflowException or JsonException or NotSupportedException)
        {
            // A literal that cannot be coerced to the pinned target contract is an authoring problem, not a server
            // fault: surface it as the structured VF-COER-001 diagnostic (with node id + reference key) so preflight
            // returns a validation result instead of letting the raw JsonException/FormatException escape as a 500 (#924).
            throw ValueConversionPublicationException.LiteralCoercionFailed(
                $"VF-COER-001: Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' value '{DescribeAuthoredValue(value.Value)}' cannot be converted to alias '{inputDefinition.Type.Alias}'.",
                ToValueTypeDescriptor(inputDefinition),
                InputBindingContext(nodeId, inputDefinition),
                exception);
        }

        ValidateNull(inputDefinition, inputType, converted, nodeId);
        var literal = JsonSerializer.SerializeToElement(converted, inputType);
        var targetType = ToValueTypeDescriptor(inputDefinition);
        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: targetType,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: converted is null
                ? ValueEnvelope.Null(targetType, policy)
                : ValueEnvelope.Inline(targetType, literal, policy),
            metadata: BuildInputMetadata(inputDefinition),
            conversionPlan: resolvedConversionPlanResolver.Resolve(
                targetType,
                ValueRepresentationDefaults.Infer(targetType),
                targetType,
                AuthoredValueConversionMapper.Mode(conversion),
                AuthoredValueConversionMapper.Profile(conversion),
                AuthoredValueConversionMapper.Limits(conversion),
                AuthoredValueConversionMapper.Options(conversion),
                InputBindingContext(nodeId, inputDefinition)));
    }

    private RuntimeInputBinding CompileFormattedLiteralInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy policy,
        AuthoredValueConversionRequest conversion)
    {
        var inputType = ResolveInputType(inputDefinition);
        var raw = ExtractLiteralContent(value.Value);
        ValidateNull(inputDefinition, inputType, raw, nodeId);
        var sourceType = new ValueTypeDescriptor("String");
        var targetType = ToValueTypeDescriptor(inputDefinition);
        var literal = raw is null
            ? ValueEnvelope.Null(sourceType, policy)
            : ValueEnvelope.Inline(sourceType, JsonSerializer.SerializeToElement(raw), policy);

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: targetType,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: literal,
            metadata: BuildInputMetadata(inputDefinition),
            conversionPlan: resolvedConversionPlanResolver.Resolve(
                sourceType,
                ValueRepresentation.FormattedContent,
                targetType,
                AuthoredValueConversionMapper.Mode(conversion),
                AuthoredValueConversionMapper.Profile(conversion),
                AuthoredValueConversionMapper.Limits(conversion),
                AuthoredValueConversionMapper.Options(conversion),
                InputBindingContext(nodeId, inputDefinition)));
    }

    private RuntimeInputBinding CompileObjectInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy policy,
        AuthoredValueConversionRequest? conversion)
    {
        var inputType = ResolveInputType(inputDefinition);
        object? converted;
        try
        {
            var authored = value.Value is JsonElement jsonElement
                ? jsonElement
                : JsonSerializer.SerializeToElement(value.Value);
            var structured = authored.ValueKind == JsonValueKind.String
                ? JsonSerializer.Deserialize<JsonElement>(authored.GetString()!)
                : authored;
            converted = structured.Deserialize(inputType);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' object value cannot be converted to alias '{inputDefinition.Type.Alias}'.",
                exception);
        }

        // Keep the contract violation outside the conversion catch so VF-ACT-004 remains the direct,
        // stable publication diagnostic instead of being wrapped as malformed object syntax.
        ValidateNull(inputDefinition, inputType, converted, nodeId);
        try
        {
            var literal = JsonSerializer.SerializeToElement(converted, inputType);
            var targetType = ToValueTypeDescriptor(inputDefinition);
            return new RuntimeInputBinding(
                inputKey: inputDefinition.ReferenceKey,
                targetType: targetType,
                effectivePolicy: policy,
                source: RuntimeInputBindingSource.Literal,
                literal: converted is null
                    ? ValueEnvelope.Null(targetType, policy)
                    : ValueEnvelope.Inline(targetType, literal, policy),
                metadata: BuildInputMetadata(inputDefinition),
                conversionPlan: resolvedConversionPlanResolver.Resolve(
                    targetType,
                    ValueRepresentationDefaults.Infer(targetType),
                    targetType,
                    AuthoredValueConversionMapper.Mode(conversion),
                    AuthoredValueConversionMapper.Profile(conversion),
                    AuthoredValueConversionMapper.Limits(conversion),
                    AuthoredValueConversionMapper.Options(conversion),
                    InputBindingContext(nodeId, inputDefinition)));
        }
        catch (ValueConversionPublicationException)
        {
            // A structured conversion rejection already carries the binding context and must not be
            // masked as a malformed-object syntax error; surface it to publication as-is.
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' object value cannot be converted to alias '{inputDefinition.Type.Alias}'.",
                exception);
        }
    }

    private RuntimeInputBinding CompileExpressionInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value, ValueProtectionPolicy effectivePolicy)
    {
        if (string.IsNullOrWhiteSpace(value.ExpressionType))
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' does not declare an expression type.");

        if (TryReadPortableExpressionDefinition(value.Value, out var definition))
            return CompilePortableExpressionInput(nodeId, inputDefinition, value.ExpressionType, definition, effectivePolicy);

        var expressionText = ExtractExpressionText(value.Value);
        if (string.IsNullOrWhiteSpace(expressionText))
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{value.ExpressionType}' but carries no expression text.");

        var inputType = ResolveInputType(inputDefinition);
        var resultType = new RuntimeValueTypeDescriptor("alias", inputDefinition.Type.Alias, null);

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(value.ExpressionType, expressionText, resultType),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static RuntimeInputBinding CompilePortableExpressionInput(
        string nodeId,
        InputDefinition inputDefinition,
        string authoredExpressionType,
        ExpressionDefinition definition,
        ValueProtectionPolicy effectivePolicy)
    {
        if (!string.Equals(authoredExpressionType, definition.Language, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' declares expression type '{authoredExpressionType}', but its portable definition declares language '{definition.Language}'.");
        }

        if (!string.Equals(inputDefinition.Type.Alias, definition.ResultType.Alias, StringComparison.Ordinal) ||
            inputDefinition.Type.CollectionKind != definition.ResultType.CollectionKind)
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' expects '{inputDefinition.Type.Alias}', but its portable expression declares result type '{definition.ResultType.Alias}'.");
        }

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                definition.Language,
                definition.Source,
                new RuntimeValueTypeDescriptor("alias", definition.ResultType.Alias, null),
                definition.Metadata,
                definition.Parameters,
                definition.Options,
                definition.CapabilityProfile),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static bool TryReadPortableExpressionDefinition(object? value, out ExpressionDefinition definition)
    {
        if (value is ExpressionDefinition instance)
        {
            definition = instance;
            return true;
        }

        var element = value as JsonElement? ?? (value is null ? null : JsonSerializer.SerializeToElement(value));
        if (element is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("language", out _) ||
            !payload.TryGetProperty("source", out _) ||
            !payload.TryGetProperty("capabilityProfile", out _))
        {
            definition = null!;
            return false;
        }

        definition = payload.Deserialize<ExpressionDefinition>()
            ?? throw new ArgumentException("Portable expression definition payload could not be deserialized.");
        return true;
    }

    // Closes the authored TypeReference (alias + collection kind) into a concrete CLR type via the
    // well-known type registry, mirroring VariableMapper's resolution (FR-007). Unknown alias → object.
    private Type ResolveInputType(InputDefinition inputDefinition) =>
        TypeReferenceFactory.Resolve(
            inputDefinition.Type,
            alias => wellKnownTypeRegistry.TryGetTypeOrDefault(alias, out var type) ? type : typeof(object));

    private static string? ExtractExpressionText(object? value)
    {
        if (value is null)
            return null;

        if (value is JsonElement jsonElement)
            return jsonElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() : jsonElement.ToString();

        return value.ToString();
    }

    private static JsonElement RequireObjectPayload(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        string expressionType)
    {
        var payload = value.Value is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(value.Value);
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{expressionType}' but carries no object reference payload.");

        return payload;
    }

    private static string RequireStringProperty(
        string nodeId,
        InputDefinition inputDefinition,
        JsonElement payload,
        string expressionType,
        string propertyName)
    {
        if (payload.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
            return property.GetString()!;

        throw new ArgumentException(
            $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{expressionType}' but carries no '{propertyName}'.");
    }

    /// <summary>
    /// Reads an optional string property: a missing or null property reads as null. Any other non-string value reads
    /// as null too, unless <paramref name="nonText"/> is given, in which case its exception is thrown.
    /// </summary>
    private static string? ReadOptionalStringProperty(JsonElement payload, string propertyName, Func<string, ArgumentException>? nonText = null)
    {
        if (!payload.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (property.ValueKind == JsonValueKind.String)
            return property.GetString();

        if (nonText is not null)
            throw nonText(propertyName);
        return null;
    }

    private static string InputRole(string nodeId, string inputKey) => $"Input '{inputKey}' on activity node '{nodeId}'";

    private static ValueConversionBindingContext InputBindingContext(string nodeId, InputDefinition inputDefinition) =>
        new(nodeId, inputDefinition.ReferenceKey, ValueConversionBindingKind.Input);

    private static Dictionary<string, string> BuildInputMetadata(InputDefinition inputDefinition) =>
        new()
        {
            [ReferenceKeyMetadataKey] = inputDefinition.ReferenceKey
        };

    private static ValueTypeDescriptor ToValueTypeDescriptor(InputDefinition inputDefinition) =>
        new(inputDefinition.Type.Alias, inputDefinition.Type.CollectionKind);

    private static bool AcceptsNull(InputDefinition inputDefinition) =>
        inputDefinition.IsNullable;

    private static void ValidateNull(InputDefinition inputDefinition, Type inputType, object? value, string nodeId)
    {
        if (value is null && !AcceptsNull(inputDefinition))
        {
            throw new ArgumentException(
                $"VF-ACT-004: Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' does not accept null.");
        }
    }

    private static object? ConvertLiteral(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var nullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return null;

            if (jsonElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                return jsonElement.Deserialize(nullableTargetType);

            value = jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() : jsonElement.ToString();
        }

        if (nullableTargetType.IsInstanceOfType(value))
            return value;

        if (nullableTargetType == typeof(string))
            return $"{value}";

        if (nullableTargetType.IsEnum)
            return Enum.Parse(nullableTargetType, $"{value}", ignoreCase: true);

        // A scalar authored into a collection-typed input is coerced into a single-element collection, and a
        // comma-separated scalar into one element per trimmed item ("GET" → ["GET"], "200, 404" → [200, 404]).
        // The Studio surfaces collection inputs with a list editor now (#924), but users still type a bare value or
        // a comma list into the scalar box — accepting both keeps that authoring gesture publishable.
        if (value is string scalarText && TryGetCollectionElementType(nullableTargetType, out var elementType) &&
            TryCoerceScalarToCollection(scalarText, nullableTargetType, elementType, out var coercedCollection))
            return coercedCollection;

        if (!typeof(IConvertible).IsAssignableFrom(nullableTargetType) || value is not IConvertible)
            return JsonSerializer.SerializeToElement(value).Deserialize(nullableTargetType);

        return Convert.ChangeType(value, nullableTargetType, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Resolves the element type of a supported collection target (array, or a single-argument generic collection:
    /// <c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c> and friends). Dictionaries are not
    /// scalar-coercible (no key), so they are excluded.
    /// </summary>
    private static bool TryGetCollectionElementType(Type collectionType, out Type elementType)
    {
        if (collectionType.IsArray && collectionType.GetElementType() is { } arrayElement)
        {
            elementType = arrayElement;
            return true;
        }

        if (collectionType is { IsGenericType: true } && collectionType.GetGenericArguments() is [var single])
        {
            var definition = collectionType.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) ||
                definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IReadOnlyCollection<>) || definition == typeof(HashSet<>) || definition == typeof(ISet<>))
            {
                elementType = single;
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    /// <summary>
    /// Coerces a scalar (single value or comma-separated list) into the target collection, converting each trimmed,
    /// non-empty item to the element type. Returns false when the element conversion is unsupported so the caller can
    /// fall through to the ordinary JSON path (and, ultimately, a structured coercion diagnostic).
    /// </summary>
    private static bool TryCoerceScalarToCollection(string scalarText, Type collectionType, Type elementType, out object? result)
    {
        var items = scalarText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var elements = Array.CreateInstance(elementType, items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var element = ConvertLiteral(items[i], elementType);
            if (element is null && elementType.IsValueType && Nullable.GetUnderlyingType(elementType) is null)
            {
                result = null;
                return false;
            }

            elements.SetValue(element, i);
        }

        if (collectionType.IsArray)
        {
            result = elements;
            return true;
        }

        // The generic collection targets published here are always closed to List<T>/HashSet<T> by
        // TypeReferenceFactory.Close, both of which expose an IEnumerable<T> constructor.
        var concreteType = collectionType.GetGenericTypeDefinition() == typeof(HashSet<>)
            ? typeof(HashSet<>).MakeGenericType(elementType)
            : typeof(List<>).MakeGenericType(elementType);
        result = Activator.CreateInstance(concreteType, elements)!;
        return true;
    }

    private static string DescribeAuthoredValue(object? value) =>
        value switch
        {
            null => "null",
            JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() ?? "null" : element.GetRawText(),
            _ => value.ToString() ?? "null"
        };

    private static string? ExtractLiteralContent(object? value)
    {
        if (value is null)
            return null;

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return null;

            return jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() : jsonElement.GetRawText();
        }

        return value is string text ? text : JsonSerializer.Serialize(value);
    }
}
