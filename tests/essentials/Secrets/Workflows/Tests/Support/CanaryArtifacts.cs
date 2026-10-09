using System.Text.Json;
using System.Text.Json.Nodes;
using InputDefinition = Elsa.Activities.Design.Core.Models.InputDefinition;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Canary.Fixtures;
using Elsa.Primitives.Models;
using Elsa.Secrets.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>
/// The canary's hand-built runtime artifacts, imported without publish (spec 188, research R13): the structural run
/// shape of S4, whose child slot no published design can carry for a test activity, and the literal of S5 and S6 on an
/// input whose pinned policy requires encryption, which publish refuses.
/// </summary>
public static class CanaryArtifacts
{
    public const string ChildNodeId = "canary-child";
    private const string ChildSlotName = "Canary.Children";

    /// <summary>
    /// S4: a <see cref="CanaryStructuralActivity"/> root over one <see cref="CanaryChildActivity"/>, whose credential input
    /// is bound to <paramref name="secretName"/> as publish compiles a secret reference.
    /// </summary>
    public static WorkflowExecutable Structural(string artifactId, string nodeId, string secretName, IWellKnownTypeRegistry types)
    {
        var compiler = new RuntimeInputBindingCompiler(types);
        var reference = new JsonObject { ["name"] = secretName, ["typeName"] = SecretTypeNames.Text };
        var binding = compiler.Compile(
            nodeId,
            Input(typeof(CanaryStructuralActivity), nameof(CanaryStructuralActivity.Primary)),
            new ArgumentValue(JsonSerializer.SerializeToElement(reference), SecretExpressionTypes.Secret));
        var child = ClrNode(ChildNodeId, typeof(CanaryChildActivity), new Dictionary<string, RuntimeInputBinding>(), null);
        var root = ClrNode(
            nodeId,
            typeof(CanaryStructuralActivity),
            new Dictionary<string, RuntimeInputBinding>
            {
                [binding.InputKey] = binding,
                [nameof(CanaryStructuralActivity.Companion)] = Literal(nameof(CanaryStructuralActivity.Companion), "structural", ValueProtectionPolicy.InstanceInline)
            },
            [new ExecutableChildSlot(ChildSlotName, [child])]);
        return Executable(artifactId, root);
    }

    /// <summary>
    /// S5 and S6: a <see cref="CanaryActivity"/> root whose <c>Primary</c> input, pinned as the credential it declares
    /// (sensitive, requiring encryption), is bound to the literal <paramref name="value"/>, and whose companion is bound
    /// to <paramref name="mode"/>. Publish refuses such a binding; an imported artifact can carry it.
    /// </summary>
    public static WorkflowExecutable EncryptionRequiredLiteral(string artifactId, string nodeId, string value, string mode)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(CanaryActivity));
        // The pinned policy publish gives an input the activity declares a credential: sensitive, requiring encryption.
        var credential = ValuePolicyCombiner.ApplyInputDeclaration(ActivityValuePolicy.Default, isSensitive: false, isCredential: true);
        var inputs = contract.Inputs.Values
            .Select(input => input.Key == nameof(CanaryActivity.Primary)
                ? new ActivityInputContract(input.Key, input.Name, input.Type, input.IsRequired, input.IsNullable, input.HasDefault, input.DefaultValue, credential, input.EditorMetadata, isCredential: true)
                : input)
            .ToArray();
        var pinned = new ActivityContract(
            contract.ActivityTypeKey,
            contract.ContractVersion,
            contract.DescriptorKind,
            contract.DescriptorPayload,
            inputs,
            contract.Result,
            contract.Outcomes,
            contract.Activation,
            contract.SideEffectProfile);
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(CanaryActivity.Primary)] = Literal(nameof(CanaryActivity.Primary), value, ValuePolicyCombiner.ToProtectionPolicy(credential)),
            [nameof(CanaryActivity.Companion)] = Literal(nameof(CanaryActivity.Companion), mode, ValueProtectionPolicy.InstanceInline)
        };
        var root = new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: nodeId,
            activityType: typeof(CanaryActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: pinned.DescriptorPayload,
            inputBindings: ClrActivityContractTestBuilder.CompleteInputBindings(pinned, bindings),
            metadata: new Dictionary<string, string>(),
            activityContract: pinned);
        return Executable(artifactId, root);
    }

    /// <summary>
    /// S8c: a <see cref="CanaryActivity"/> root in <paramref name="mode"/>, and a workflow variable
    /// <paramref name="variable"/> declared sensitive whose initial value is the literal <paramref name="value"/>. No
    /// published design declares a sensitive workflow variable (an author's sensitivity on a value written into one is
    /// not carried into the variable frame); an imported artifact can.
    /// </summary>
    public static WorkflowExecutable SensitiveVariable(string artifactId, string nodeId, string variable, string value, string mode)
    {
        var sensitive = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var root = ClrNode(
            nodeId,
            typeof(CanaryActivity),
            new Dictionary<string, RuntimeInputBinding> { [nameof(CanaryActivity.Companion)] = Literal(nameof(CanaryActivity.Companion), mode, ValueProtectionPolicy.InstanceInline) },
            null);
        return Executable(artifactId, root, [new RuntimeVariableDeclaration(variable, variable, Text, sensitive, Literal(variable, value, sensitive))]);
    }

    private static readonly ValueTypeDescriptor Text = new("String");

    private static RuntimeInputBinding Literal(string inputKey, string value, ValueProtectionPolicy policy) =>
        new(inputKey, Text, policy, RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(Text, JsonSerializer.SerializeToElement(value), policy));

    private static WorkflowExecutable Executable(string artifactId, ExecutableNode root, IReadOnlyCollection<RuntimeVariableDeclaration>? variables = null) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, $"{artifactId}-definition", $"{artifactId}-version", "1.0.0", $"sha256:{artifactId}"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            workflowVariables: variables);

    /// <summary>A node of the CLR <paramref name="activityType"/> with its contract pinned, as publish pins it.</summary>
    private static ExecutableNode ClrNode(string nodeId, Type activityType, IReadOnlyDictionary<string, RuntimeInputBinding> bindings, IReadOnlyCollection<ExecutableChildSlot>? childSlots)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(activityType);
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: nodeId,
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: ClrActivityContractTestBuilder.CompleteInputBindings(contract, bindings),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            activityContract: contract);
    }

    /// <summary>The input as the catalog declares the CLR property <paramref name="name"/> of <paramref name="activityType"/>.</summary>
    private static InputDefinition Input(Type activityType, string name) =>
        new(name, name, TypeReferenceFactory.FromClrType(activityType.GetProperty(name)!.PropertyType, TypeAliasConvention.CanonicalAlias),
            StorageDriverType: null, DisplayName: name, Category: null, IsNullable: true, IsSensitive: true, IsCredential: true);
}
