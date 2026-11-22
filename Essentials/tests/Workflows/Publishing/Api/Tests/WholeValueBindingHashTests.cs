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
/// One node binding every whole-value source kind hashes exactly as it did before member reads existed (#4078 slice A),
/// as a node payload, as a behavioural executable, as a template behaviour and as an input-snapshot binding
/// fingerprint. The expected values were computed on a9771131c3, the commit before the model change.
/// </summary>
public sealed class WholeValueBindingHashTests
{
    internal const string ExpectedNodePayloadHash = "sha256:dea1bc201ca5c42fd13b2093037d104681f1d98156c05f7a4317c39814ca0648";
    internal const string ExpectedExecutableHash = "sha256:7c550a97501f76c8473ac6f8090989f0a1e307108d4abe78986f656b311f5f20";
    internal const string ExpectedTemplateHash = "sha256:904f680fe682e713f776d1835d1c6d3740b8f2638c6e70b704446386c3c3a769";
    internal const string ExpectedBindingFingerprint = "sha256:8c2712110d10c68bd9b05bcfbcbdc8aec902e01773161dfb7058bceb462ec1b2";

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
