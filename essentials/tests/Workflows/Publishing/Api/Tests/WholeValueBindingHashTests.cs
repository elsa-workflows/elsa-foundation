using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Fixed current hashes for a node binding every whole-value source kind. Regenerated once by S-9 before the first stable release.
/// </summary>
public sealed class WholeValueBindingHashTests
{
    internal const string ExpectedNodePayloadHash = "sha256:44e2c240280a2c6ce31c662f54a85311ff7a670d024465994b191c73c9905111";
    internal const string ExpectedExecutableHash = "sha256:7d92bfc3137b805dc75dcc434370eb2023b9c0de3421e178e15aea959da07e18";
    internal const string ExpectedTemplateHash = "sha256:fa20530c5493f4159134c46b584ff782ae66fbfca47c81a088aa055272a9e993";
    internal const string ExpectedBindingFingerprint = "sha256:7dc805658de8eec17062f5fde590a520010694e86a71d84f14653ae22ec3489e";

    [Fact]
    public void Every_whole_value_kind_keeps_its_node_payload_hash() =>
        AssertHash(ExpectedNodePayloadHash, new WorkflowExecutableHasher().ComputeHash(Node()));

    [Fact]
    public void Every_whole_value_kind_keeps_its_behavioural_executable_hash() =>
        AssertHash(ExpectedExecutableHash, new WorkflowExecutableHasher().ComputeHash(Node(),
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []), []));

    [Fact]
    public void Every_whole_value_kind_keeps_its_template_behaviour_hash() =>
        AssertHash(ExpectedTemplateHash, ScopedActivityResultHashTests.TemplateHash(Node()));

    [Fact]
    public void Every_whole_value_kind_keeps_its_input_snapshot_binding_fingerprint() =>
        AssertHash(ExpectedBindingFingerprint, BindingFingerprint(Node()));

    internal static void AssertHash(string expected, string actual) => Assert.True(expected == actual,
        $"JSON hash golden: Expected={expected}; Actual={actual}. From Elsa 4's first stable release, this hash is frozen and changes require an explicit decision.");

    internal static string BindingFingerprint(ExecutableNode node) =>
        (string)typeof(RuntimeActivityInputMaterializer)
            .GetMethod("ComputeBindingFingerprint", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [node])!;

    private static readonly ValueTypeDescriptor Text = new("String");

    internal static IReadOnlyDictionary<string, RuntimeInputBinding> WholeValueBindings() => new Dictionary<string, RuntimeInputBinding>
    {
        ["literal"] = new("literal", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(Text, JsonSerializer.SerializeToElement("hello"), ValueProtectionPolicy.InstanceInline)),
        ["request"] = new("request", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("customer", "customer.id")),
        ["variable"] = new("variable", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference("customer", "workflow")),
        ["scoped-result"] = new("scoped-result", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "$result", "scope:root", isOptional: true)),
        ["result"] = new("result", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer", producerScopeId: null),
            conversionPlan: ValueConversionPlan.Identity(Text, ValueRepresentation.TextValue)),
        ["expression"] = new("expression", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "args.p", parameters: new Dictionary<string, ExpressionParameterBinding>
            {
                ["p"] = new ActivityResultExpressionParameterBinding("producer", "$result")
            })),
        ["secret"] = new("secret", Text, new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true),
            RuntimeInputBindingSource.SecretRead, secret: new RuntimeSecretReference("api-key", "text"))
    };

    internal static ExecutableNode Node(IReadOnlyDictionary<string, RuntimeInputBinding>? bindings = null) => new(
        executableNodeId: "node-consumer",
        authoredActivityId: "consumer",
        activityType: "Tests.Consumer",
        activityTypeVersion: "1.0.0",
        descriptorType: "clr",
        descriptorPayload: JsonSerializer.SerializeToElement(new { typeAlias = "Tests.Consumer" }),
        inputBindings: bindings ?? WholeValueBindings(),
        metadata: new Dictionary<string, string>());
}
