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
    internal const string ExpectedNodePayloadHash = "sha256:129bea357b2a4221859042918b2a001d79e09327fa98744c0897290c560502e9";
    internal const string ExpectedExecutableHash = "sha256:850078a0861e1f58bd6be3d6408e2b3dd38f697315091a492210790a906069af";
    internal const string ExpectedTemplateHash = "sha256:76bdb7e5e8f3ecb8c1ab37c86b3644356c77125d5fbd6fe3e6443a7594f6c0e5";
    internal const string ExpectedBindingFingerprint = "sha256:b15fdde5265ce2a2fdf22c4f439d7320ea5b9b33164ca97059baa41223ffabe0";

    [Fact]
    public void Every_whole_value_kind_keeps_its_node_payload_hash() =>
        Assert.Equal(ExpectedNodePayloadHash, new WorkflowExecutableHasher().ComputeHash(Node()));

    [Fact]
    public void Every_whole_value_kind_keeps_its_behavioural_executable_hash() =>
        Assert.Equal(ExpectedExecutableHash, new WorkflowExecutableHasher().ComputeHash(Node(),
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []), []));

    [Fact]
    public void Every_whole_value_kind_keeps_its_template_behaviour_hash() =>
        Assert.Equal(ExpectedTemplateHash, ScopedActivityResultHashTests.TemplateHash(Node()));

    [Fact]
    public void Every_whole_value_kind_keeps_its_input_snapshot_binding_fingerprint() =>
        Assert.Equal(ExpectedBindingFingerprint, BindingFingerprint(Node()));

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
