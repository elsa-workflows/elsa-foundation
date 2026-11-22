using JsonValueConversion = Elsa.Serialization.Core.JsonConversionContract;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Models;
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
    private const string ActivityResultMemberExpressionType = "ActivityResultMember";
    private const string VariableMemberExpressionType = "VariableMember";
    private const string DefaultExpressionType = "Default";
    private const string ReferenceKeyMetadataKey = "referenceKey";

    /// <summary>
    /// The authored expression type of a secret reference. It duplicates the Secrets module's expression type name
    /// on purpose, so this project needs no reference to that module.
    /// </summary>
    public const string SecretExpressionType = "Secret";

    /// <summary>
    /// The authored expression type of a connection reference. Duplicates the Connections module's expression
    /// type name so this project needs no reference to that module.
    /// </summary>
    public const string ConnectionExpressionType = "Connection";

    // A resolved secret is text: the conversion plan of a secret read starts from a text value.
    private static readonly ValueTypeDescriptor SecretSourceType = new("String");

    // A secret read is always sensitive and requires encryption, whatever the input declares.
    private static readonly ValueProtectionPolicy SecretPolicyMinimum = new(
        DurableValueLifecycle.None,
        DurableValueStorage.None,
        isSensitive: true,
        requiresEncryption: true);

    private readonly ValueConversionPlanResolver resolvedConversionPlanResolver = conversionPlanResolver ?? new(wellKnownTypeRegistry: wellKnownTypeRegistry);
    private Type? ConversionActivityType { get; init; }
    private Elsa.Expressions.Core.Contracts.IExecutionWarningSink? ConversionWarnings { get; init; }
    private string? ConversionNodeId { get; init; }

    internal RuntimeInputBindingCompiler WithConversionBoundary(Type? activityType, Elsa.Expressions.Core.Contracts.IExecutionWarningSink? warnings, string nodeId) =>
        new(wellKnownTypeRegistry, resolvedConversionPlanResolver) { ConversionActivityType = activityType, ConversionWarnings = warnings, ConversionNodeId = nodeId };

    private JsonConversionContract ConversionFor(Type type, InputDefinition? input)
    {
        var member = input is null ? null : ConversionActivityType?.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(property =>
            property.GetCustomAttribute<ActivityInputAttribute>(inherit: true) is { } attribute &&
            (attribute.Key ?? property.Name) == input.ReferenceKey);
        return JsonConversionContract.For(type, member);
    }

    private void ReportConversion(JsonConversionDiagnostic diagnostic, InputDefinition? input)
    {
        var warning = ExecutionWarning.FromJsonConversion(diagnostic.Code, diagnostic.Path, diagnostic.TargetType, diagnostic.Converter, $"nodes.{ConversionNodeId}.inputs.{input?.ReferenceKey ?? "default"}");
        ConversionWarnings?.Report(input?.IsSensitive == true ? warning.ProtectedForm! : warning);
    }

    /// <summary>
    /// Compiles every input of a node against its catalog input definitions. The credential-literal rule
    /// is applied to the whole node first, so its refusal wins over <c>VF-ACT-011</c> and over any other input's
    /// compile error.
    /// </summary>
    public IReadOnlyDictionary<string, RuntimeInputBinding> CompileAll(
        string nodeId,
        IEnumerable<InputDefinition> inputDefinitions,
        IEnumerable<ArgumentState> inputStates)
    {
        var definitions = inputDefinitions.ToArray();
        var states = inputStates.ToArray();
        var definitionsByKey = definitions.ToDictionary(x => x.ReferenceKey, StringComparer.Ordinal);
        EnsureNoCredentialLiterals(nodeId, states, key =>
            definitionsByKey.TryGetValue(key, out var definition) && definition.IsCredential == true ? definition.Name : null);
        EnsureConnectionBindings(nodeId, states, key =>
            definitionsByKey.TryGetValue(key, out var definition) && IsConnectionInput(definition));
        EnsureVariableReferenceBindings(nodeId, definitions, states);
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

    /// <summary>
    /// Compiles every input of a node against its pinned activity input contracts. The credential-literal rule
    /// reads each contract's explicit <c>IsCredential</c> flag and is applied to the whole node first, as in the
    /// catalog overload.
    /// </summary>
    public IReadOnlyDictionary<string, RuntimeInputBinding> CompileAll(
        string nodeId,
        IEnumerable<RuntimeActivityInputContract> inputContracts,
        IEnumerable<ArgumentState> inputStates)
    {
        var contracts = inputContracts.ToArray();
        var states = inputStates.ToArray();
        var contractsByKey = contracts.ToDictionary(x => x.Key, StringComparer.Ordinal);
        EnsureNoCredentialLiterals(nodeId, states, key =>
            contractsByKey.TryGetValue(key, out var contract) && contract.IsCredential ? contract.Name : null);
        EnsureConnectionBindings(nodeId, states, key =>
            contractsByKey.TryGetValue(key, out var contract) && IsConnectionInput(contract));
        EnsureVariableReferenceBindings(nodeId, contracts.Select(ToInputDefinition).ToArray(), states);
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

    /// <summary>
    /// Refuses (<see cref="CredentialLiteralRefusedException"/>) every binding of the node that the credential-literal
    /// rule refuses, in ordinal order of the input key. <paramref name="credentialInputName"/> returns the name of the
    /// input with that key when it is declared a credential, and null otherwise.
    /// </summary>
    private static void EnsureNoCredentialLiterals(string nodeId, IEnumerable<ArgumentState> states, Func<string, string?> credentialInputName)
    {
        var findings = states
            .Select(state => (State: state, Name: credentialInputName(state.ReferenceKey)))
            .Where(input => input.Name is not null && !CredentialInputBinding.IsAccepted(isCredential: true, input.State))
            .OrderBy(input => input.State.ReferenceKey, StringComparer.Ordinal)
            .Select(input => CredentialLiteralFinding.For(nodeId, input.State.ReferenceKey, input.Name!))
            .ToArray();
        if (findings.Length > 0)
            throw new CredentialLiteralRefusedException(findings);
    }

    /// <summary>
    /// A connection-typed input may only carry a Connection expression (name and type). A string
    /// literal of a token is refused so credentials never enter workflow JSON (issue #2729).
    /// </summary>
    private static void EnsureConnectionBindings(
        string nodeId,
        IEnumerable<ArgumentState> states,
        Func<string, bool> isConnectionInput)
    {
        foreach (var state in states.OrderBy(candidate => candidate.ReferenceKey, StringComparer.Ordinal))
        {
            if (!isConnectionInput(state.ReferenceKey))
                continue;
            if (ConnectionInputBinding.IsAccepted(state))
                continue;

            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{state.ReferenceKey}' must be a Connection reference (name and type), not a literal credential.");
        }
    }

    /// <summary>
    /// Spec 1231 FR-024: a variable-reference input (<c>VariableReference&lt;T&gt;</c> or
    /// <c>ReadOnlyVariableReference&lt;T&gt;</c>) must be bound, and only to a declared variable through the
    /// <c>Variable</c> expression. An unbound one, or one bound to any other expression, is refused, naming the input.
    /// </summary>
    private static void EnsureVariableReferenceBindings(string nodeId, IReadOnlyCollection<InputDefinition> definitions, IReadOnlyCollection<ArgumentState> states)
    {
        foreach (var definition in definitions.Where(IsVariableReferenceInput).OrderBy(definition => definition.ReferenceKey, StringComparer.Ordinal))
        {
            var state = states.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ReferenceKey, definition.ReferenceKey));
            if (state is null || !state.IsBound())
                throw new ArgumentException(
                    $"VF-VAR-001: Activity node '{nodeId}' input '{definition.ReferenceKey}' is a variable reference and must be bound to a declared variable.");
            if (!string.Equals(state.Value!.ExpressionType, VariableExpressionType, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"VF-VAR-001: Activity node '{nodeId}' input '{definition.ReferenceKey}' is a variable reference and must be bound to a declared variable, not to a '{state.Value.ExpressionType}' expression.");
        }
    }

    internal static bool IsVariableReferenceInput(InputDefinition definition) => !string.IsNullOrWhiteSpace(definition.VariableAccess);

    private static bool IsConnectionInput(InputDefinition definition) =>
        !string.IsNullOrWhiteSpace(definition.ConnectionType) || IsConnectionTyped(definition.Type.Alias);

    private static bool IsConnectionInput(RuntimeActivityInputContract contract) =>
        !string.IsNullOrWhiteSpace(contract.ConnectionType) || IsConnectionTyped(contract.Type);

    private static bool IsConnectionTyped(Elsa.Primitives.Models.ValueTypeDescriptor type) =>
        IsConnectionTyped(type.Alias);

    private static bool IsConnectionTyped(string? alias) =>
        string.Equals(alias, typeof(Elsa.Activities.Runtime.Core.Models.ConnectionReference).FullName, StringComparison.Ordinal);

    public RuntimeInputBinding Compile(string nodeId, InputDefinition inputDefinition, ArgumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return CompileAuthored(nodeId, inputDefinition, state, DeclaredPolicy(inputDefinition));
    }

    /// <summary>
    /// Compiles an authored input on a published activity boundary. The boundary contract's storage-driver key
    /// selects its value codec; it is not an external payload-storage profile. Authored per-binding storage policy
    /// still flows through <see cref="CompileAuthored"/> and can strengthen the inline default.
    /// </summary>
    internal RuntimeInputBinding CompilePublishedBoundaryInput(
        string nodeId,
        ActivityInputContract contract,
        ArgumentState state)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(state);

        return CompileAuthored(
            nodeId,
            ToInputDefinition(contract),
            state,
            ActivityValuePolicy.Default);
    }

    /// <summary>Compiles a published boundary input that has no authored state or declared default.</summary>
    internal RuntimeInputBinding CompileUnboundPublishedBoundaryInput(
        string nodeId,
        ActivityInputContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return CompileUnbound(nodeId, ToInputDefinition(contract), ActivityValuePolicy.Default);
    }

    public RuntimeInputBinding Compile(string nodeId, InputDefinition inputDefinition, ArgumentValue value) =>
        CompileBound(nodeId, inputDefinition, value, DeclaredPolicy(inputDefinition), conversion: null);

    /// <summary>
    /// The effective policy of an activity input: its storage policy with the activity's
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
    /// check uses) compiles exactly as an unbound input does, so an empty credential field
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

            var pinnedLiteral = ConversionFor(inputType, inputDefinition).Write(defaultValue, inputType);
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
        DefaultSyntax: contract.HasDefault ? LiteralExpressionType : null,
        IsCredential: contract.IsCredential ? true : null,
        ConnectionType: contract.ConnectionType,
        VariableAccess: contract.VariableReference is { } reference
            ? reference.Access == Elsa.Activities.Runtime.Core.Models.ActivityVariableAccess.ReadWrite ? InputVariableAccess.ReadWrite : InputVariableAccess.Read
            : null);

    private static InputDefinition ToInputDefinition(ActivityInputContract contract) => new(
        contract.ReferenceKey,
        contract.Name,
        contract.Type,
        contract.StorageDriverKey,
        contract.DisplayName ?? contract.Name,
        contract.Category,
        contract.IsNullable,
        Description: contract.Description,
        Order: contract.Order,
        UiHint: contract.UiHint,
        UISpecifications: contract.UiSpecifications,
        IsRequired: contract.IsRequired,
        DefaultValue: contract.Default?.Value,
        DefaultSyntax: contract.Default?.Syntax);

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
        if (IsVariableReferenceInput(inputDefinition))
            return CompileVariableReferenceInput(nodeId, inputDefinition, value);

        if (string.Equals(value.ExpressionType, LiteralExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileLiteralInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

        RefuseMemberSelectionOnWholeValueRead(nodeId, inputDefinition, value);

        if (string.Equals(value.ExpressionType, ActivityResultMemberExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileActivityResultMemberInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

        if (string.Equals(value.ExpressionType, VariableMemberExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileVariableMemberInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

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

        if (IsConnectionBinding(value))
            return CompileConnectionInput(nodeId, inputDefinition, value, effectivePolicy);

        return CompileExpressionInput(nodeId, inputDefinition, value, effectivePolicy);
    }

    /// <summary>True when the authored value is a secret reference.</summary>
    public static bool IsSecretBinding(ArgumentValue? value) =>
        string.Equals(value?.ExpressionType, SecretExpressionType, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the authored value is a connection reference.</summary>
    public static bool IsConnectionBinding(ArgumentValue? value) =>
        string.Equals(value?.ExpressionType, ConnectionExpressionType, StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Compiles a connection reference into an inline literal of name and type only. Credentials stay
    /// on the connection and are resolved at send time.
    /// </summary>
    private RuntimeInputBinding CompileConnectionInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy effectivePolicy)
    {
        var reading = ConnectionReferencePayload.Read(value.Value);
        if (reading.Reference is not { } members)
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{ConnectionExpressionType}' but {reading.Defect}.");
        }

        var requiredType = inputDefinition.ConnectionType;
        if (requiredType is { Length: > 0 } &&
            members.Type is { Length: > 0 } &&
            !string.Equals(members.Type, requiredType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' requires connection type '{requiredType}' but the binding names '{members.Type}'.");
        }

        var reference = new Elsa.Activities.Runtime.Core.Models.ConnectionReference(members.Name, members.Type ?? requiredType);
        var targetType = ToValueTypeDescriptor(inputDefinition);
        var literal = JsonValueConversion.SerializeToElement(reference);
        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: targetType,
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(targetType, literal, effectivePolicy),
            metadata: BuildInputMetadata(inputDefinition));
    }

    // Read through the one definition of a well-formed reference, which the credential-literal rule applies at save.
    // The messages name the node, the input and the defect, never the authored payload.
    private static RuntimeSecretReference ParseSecretReference(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
        var reading = SecretReferencePayload.Read(value.Value);
        return reading.Reference is { } reference
            ? new RuntimeSecretReference(reference.Name, reference.TypeName, reference.Scope)
            : throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{SecretExpressionType}' but {reading.Defect}.");
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
                reference.IsWorkflowScope ? VariableReference.WorkflowScopeId : reference.DeclaringScopeId!),
            metadata: BuildInputMetadata(inputDefinition));
    }

    /// <summary>
    /// Compiles a variable-reference input (spec 1231 FR-022): the pinned binding is the variable it names, its declaring
    /// scope and key, as an inline literal of <see cref="Elsa.Activities.Runtime.Core.Models.VariableReferenceTypes.Alias"/>.
    /// It is never a value: the activity reads and writes the variable through its step's session.
    /// </summary>
    private static RuntimeInputBinding CompileVariableReferenceInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
        if (!string.Equals(value.ExpressionType, VariableExpressionType, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"VF-VAR-001: Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' is a variable reference and must be bound to a declared variable, not to a '{value.ExpressionType}' expression.");

        var reference = ParseVariableReference(nodeId, inputDefinition, value.Value);
        var scopeId = reference.IsWorkflowScope ? VariableReference.WorkflowScopeId : reference.DeclaringScopeId!;
        var name = ReadVariableName(value.Value) ?? reference.ReferenceKey;
        var type = new ValueTypeDescriptor(Elsa.Activities.Runtime.Core.Models.VariableReferenceTypes.Alias);
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: type,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonValueConversion.SerializeToElement(new { name, scopeId, variableKey = reference.ReferenceKey }), policy),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static string? ReadVariableName(object? value)
    {
        var element = value is JsonElement json ? json : JsonValueConversion.SerializeToElement(value);
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var member in new[] { "name", "Name" })
        {
            if (element.TryGetProperty(member, out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                return name.GetString();
        }

        return null;
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
        // A binding the server supplied declares no scope. One that an existing document declares must still be usable.
        var producerScopeId = payload.TryGetProperty("producerScopeId", out var scope) && scope.ValueKind != JsonValueKind.Null
            ? RequireStringProperty(nodeId, inputDefinition, payload, ActivityResultExpressionType, "producerScopeId")
            : null;
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

    /// <summary>
    /// Compiles a read of one member of an activity result. The selection is compiled as deferred: the selected
    /// member's contract is pinned only after the data selection analysis has typed it.
    /// </summary>
    private static RuntimeInputBinding CompileActivityResultMemberInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy effectivePolicy,
        AuthoredValueConversionRequest? conversion)
    {
        var payload = value.Value is JsonElement element ? element : default;
        if (payload.ValueKind != JsonValueKind.Object || payload.TryGetProperty("path", out _) ||
            payload.TryGetProperty("producerScopeId", out var scope) && scope.ValueKind != JsonValueKind.Null)
            throw Refusal(nodeId, inputDefinition, "ActivityData/InvalidSource",
                "A member read of an activity result names its producer and projection only, and declares no scope.");
        var producerNodeId = ReadOptionalStringProperty(payload, "producerNodeId");
        var projectionKey = ReadOptionalStringProperty(payload, "projectionKey");
        if (string.IsNullOrWhiteSpace(producerNodeId) || string.IsNullOrWhiteSpace(projectionKey))
            throw Refusal(nodeId, inputDefinition, "ActivityData/InvalidSource",
                "A member read of an activity result names its producer and projection only, and declares no scope.");
        var segments = ReadMemberPath(nodeId, inputDefinition, payload);
        var isOptional = payload.TryGetProperty("isOptional", out var optionalElement) && optionalElement.ValueKind == JsonValueKind.True;

        return new RuntimeInputBinding(
            inputDefinition.ReferenceKey,
            ToValueTypeDescriptor(inputDefinition),
            effectivePolicy,
            RuntimeInputBindingSource.ActivityResultMember,
            new RuntimeReadSelection(RuntimeReadSelection.CurrentSchemaVersion, segments, RuntimeReadSelectionMode.DeferredFit),
            activityResult: new RuntimeActivityResultReference(producerNodeId, projectionKey, producerScopeId: null, isOptional),
            metadata: BuildInputMetadata(inputDefinition),
            conversionRequest: AuthoredValueConversionMapper.ToRuntimeRequest(conversion));
    }

    /// <summary>
    /// Compiles a read of one member of a variable. Its declaring scope is explicit, unlike a whole-value read's. The
    /// selection is always deferred: a variable can hold JSON from any writer, so its member is checked when it is read,
    /// and with nothing known to convert from, only the default conversion can be honoured.
    /// </summary>
    private static RuntimeInputBinding CompileVariableMemberInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy effectivePolicy,
        AuthoredValueConversionRequest? conversion)
    {
        var payload = value.Value is JsonElement element ? element : default;
        var variableKey = payload.ValueKind == JsonValueKind.Object ? ReadOptionalStringProperty(payload, "referenceKey") : null;
        var scopeId = payload.ValueKind == JsonValueKind.Object ? ReadOptionalStringProperty(payload, "declaringScopeId") : null;
        if (payload.ValueKind != JsonValueKind.Object || payload.TryGetProperty("path", out _) ||
            string.IsNullOrWhiteSpace(variableKey) || string.IsNullOrWhiteSpace(scopeId))
            throw Refusal(nodeId, inputDefinition, "ActivityData/InvalidSource",
                "A member read of a variable names the variable and its declaring scope.");
        var segments = ReadMemberPath(nodeId, inputDefinition, payload);
        var targetType = ToValueTypeDescriptor(inputDefinition);
        if (conversion is { Mode: not AuthoredValueConversionMode.Auto })
            throw ValueConversionPublicationException.SourceContractUnavailable(
                $"VF-COER-001: Variable member input '{inputDefinition.ReferenceKey}' on node '{nodeId}' requests a '{conversion.Mode}' conversion, " +
                "but the selected member's contract is not known at publication.",
                ValueConversionRejectionReason.SelectedMemberContractUnknown,
                targetType,
                AuthoredValueConversionMapper.Mode(conversion),
                AuthoredValueConversionMapper.Profile(conversion),
                InputBindingContext(nodeId, inputDefinition));

        return new RuntimeInputBinding(
            inputDefinition.ReferenceKey,
            targetType,
            effectivePolicy,
            RuntimeInputBindingSource.VariableMember,
            new RuntimeReadSelection(RuntimeReadSelection.CurrentSchemaVersion, segments, RuntimeReadSelectionMode.DeferredFit),
            variable: new RuntimeVariableReference(variableKey, scopeId),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static string[] ReadMemberPath(string nodeId, InputDefinition inputDefinition, JsonElement payload) =>
        ActivityDataMemberPath.TryRead(payload, out var segments)
            ? [.. segments]
            : throw Refusal(nodeId, inputDefinition, "ActivityData/InvalidMemberPath",
                $"A member path is a nonempty array of at most {ActivityDataMemberPath.MaximumSegments} strings of at most {ActivityDataMemberPath.MaximumLength} characters in total.");

    // A member path, or a legacy path anywhere but a workflow request, on a whole-value read would otherwise be dropped
    // and the whole value read instead; refuse it at the input, as the data selection analysis does.
    private static void RefuseMemberSelectionOnWholeValueRead(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
        var wholeValueRole = value.ExpressionType switch
        {
            _ when string.Equals(value.ExpressionType, ActivityResultExpressionType, StringComparison.OrdinalIgnoreCase) => ActivityResultExpressionType,
            _ when string.Equals(value.ExpressionType, VariableExpressionType, StringComparison.OrdinalIgnoreCase) => VariableExpressionType,
            _ when string.Equals(value.ExpressionType, WorkflowRequestExpressionType, StringComparison.OrdinalIgnoreCase) => WorkflowRequestExpressionType,
            _ => null
        };
        if (wholeValueRole is null || value.Value is not JsonElement { ValueKind: JsonValueKind.Object } payload)
            return;
        if (payload.TryGetProperty("memberPath", out _) ||
            wholeValueRole != WorkflowRequestExpressionType && payload.TryGetProperty("path", out _))
            throw Refusal(nodeId, inputDefinition, "ActivityData/UnsupportedMemberSelection",
                "A whole-value read cannot select a member; use the member read of its role.");
    }

    private static ActivityDataSelectionRejectedException Refusal(string nodeId, InputDefinition inputDefinition, string code, string message) =>
        new([new ValidationError($"{nodeId}/inputs/{inputDefinition.ReferenceKey}", code, message)]);

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
        var unwrapped = value is JsonElement jsonElement ? jsonElement : JsonValueConversion.SerializeToElement(value);
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
            converted = ConvertLiteral(value.Value, inputType, inputDefinition);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException or OverflowException or JsonException or NotSupportedException)
        {
            // A literal that cannot be coerced to the pinned target contract is an authoring problem, not a server
            // fault: surface it as the structured VF-COER-001 diagnostic (with node id + reference key) so preflight
            // returns a validation result instead of letting the raw JsonException/FormatException escape as a 500 (#924).
            throw ValueConversionPublicationException.LiteralCoercionFailed(
                $"VF-COER-001: Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' value cannot be converted to alias '{inputDefinition.Type.ToDisplayString()}'.",
                ToValueTypeDescriptor(inputDefinition),
                InputBindingContext(nodeId, inputDefinition),
                exception);
        }

        ValidateNull(inputDefinition, inputType, converted, nodeId);
        // Validate a typed copy; the executable carries the original data, including unmatched members and raw tokens.
        var literal = value.Value is JsonElement original ? original.Clone() : ConversionFor(inputType, inputDefinition).Write(converted, inputType);
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
            : ValueEnvelope.Inline(sourceType, JsonValueConversion.SerializeToElement(raw), policy);

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
        JsonElement structured;
        try
        {
            var authored = value.Value is JsonElement jsonElement
                ? jsonElement
                : value.Value is not null && inputType.IsInstanceOfType(value.Value)
                    ? ConversionFor(inputType, inputDefinition).Write(value.Value, inputType)
                    : JsonValueConversion.SerializeToElement(value.Value);
            structured = authored.ValueKind == JsonValueKind.String
                ? JsonValueConversion.Deserialize<JsonElement>(authored.GetString()!)
                : authored;
            converted = ConversionFor(inputType, inputDefinition).Read(structured, inputType, diagnostic => ReportConversion(diagnostic, inputDefinition));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' object value cannot be converted to alias '{inputDefinition.Type.ToDisplayString()}'.",
                exception);
        }

        // Keep the contract violation outside the conversion catch so VF-ACT-004 remains the direct,
        // stable publication diagnostic instead of being wrapped as malformed object syntax.
        ValidateNull(inputDefinition, inputType, converted, nodeId);
        try
        {
            var literal = structured.Clone();
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
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' object value cannot be converted to alias '{inputDefinition.Type.ToDisplayString()}'.",
                exception);
        }
    }

    private RuntimeInputBinding CompileExpressionInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value, ValueProtectionPolicy effectivePolicy)
    {
        if (string.IsNullOrWhiteSpace(value.ExpressionType))
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' does not declare an expression type.");

        if (value.Value is not null and not string and not JsonElement and not ExpressionDefinition)
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' has an unsupported CLR expression payload. Supply plain text, ExpressionDefinition or JsonElement.");

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

        if (!DeclaredTypeFits(definition.ResultType, inputDefinition.Type))
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' expects '{inputDefinition.Type.ToDisplayString()}', but its portable expression declares result type '{definition.ResultType.ToDisplayString()}'.");
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
                definition.CapabilityProfile,
                variableReadFallback: definition.VariableReadFallback),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static bool TryReadPortableExpressionDefinition(object? value, out ExpressionDefinition definition)
    {
        if (value is ExpressionDefinition instance)
        {
            definition = instance;
            return true;
        }

        if (value is not JsonElement { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("language", out _) ||
            !payload.TryGetProperty("source", out _) ||
            !payload.TryGetProperty("capabilityProfile", out _))
        {
            definition = null!;
            return false;
        }

        definition = payload.Deserialize<ExpressionDefinition>(DescriptorPayloadSerializer.Options)
            ?? throw new ArgumentException("Portable expression definition payload could not be deserialized.");
        return true;
    }

    // A portable expression's declared result type fits an input when the two name the same type. The dynamic kind
    // has several names (Any, JsonElement, JsonElement?, JsonNode, their full .NET names): they are one kind here, so
    // an Any expression binds to a JsonElement input with no conversion step. A JsonObject or JsonArray input accepts
    // the dynamic kind too, and the run time checks the shape (spec 1228 FR-021).
    private static bool DeclaredTypeFits(TypeReference resultType, TypeReference inputType)
    {
        if (resultType.CollectionKind != inputType.CollectionKind)
            return false;

        if (string.Equals(resultType.Alias, inputType.Alias, StringComparison.Ordinal))
            return Equals(resultType.Element, inputType.Element);

        if (resultType.Element is not null || inputType.Element is not null)
            return false;

        return TypeAliasConvention.IsDynamicKind(resultType.Alias) &&
               (TypeAliasConvention.IsDynamicKind(inputType.Alias) || TypeAliasConvention.IsShapedJson(inputType.Alias));
    }

    // Closes the authored TypeReference (alias + collection kind) into a concrete CLR type via the
    // well-known type registry, mirroring VariableMapper's resolution. Unknown alias → object.
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
            : JsonValueConversion.SerializeToElement(value.Value);
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

    /// <summary>Reads an optional string property: a missing, null or non-string property reads as null.</summary>
    private static string? ReadOptionalStringProperty(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string InputRole(string nodeId, string inputKey) => $"Input '{inputKey}' on activity node '{nodeId}'";

    private static ValueConversionBindingContext InputBindingContext(string nodeId, InputDefinition inputDefinition) =>
        new(nodeId, inputDefinition.ReferenceKey, ValueConversionBindingKind.Input);

    private static Dictionary<string, string> BuildInputMetadata(InputDefinition inputDefinition) =>
        new()
        {
            [ReferenceKeyMetadataKey] = inputDefinition.ReferenceKey
        };

    private static ValueTypeDescriptor ToValueTypeDescriptor(InputDefinition inputDefinition) =>
        ValueTypeDescriptor.FromTypeReference(inputDefinition.Type);

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

    private object? ConvertLiteral(object? value, Type targetType, InputDefinition? input = null)
    {
        if (value is null)
            return null;
        var contract = ConversionFor(targetType, input);
        var element = value is JsonElement json ? json
            : targetType.IsInstanceOfType(value) ? contract.Write(value, targetType)
            : JsonValueConversion.SerializeToElement(value);
        // Presence/admission policy stays with S-2. This boundary only changes typed conversion.
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        return ConversionFor(targetType, input).Read(element, targetType, diagnostic => ReportConversion(diagnostic, input));
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

        return value is string text ? text : JsonValueConversion.Serialize(value);
    }
}
