using System.Text.Json;
using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class WorkflowIntrinsicAssignmentConversionTests
{
    private const string RawValidationValue = """{ "known":1, "SecretBranch": { "n":1.00, "nil":null, "large":9007199254740993, "text":"\u0041 <+>" } }""";

    public static IEnumerable<object[]> TypedValidationWarningCases() =>
        from output in new[] { false, true }
        from source in new[] { "literal", "expression", "member" }
        from protection in new[] { "public", "sensitive", "redacted", "retained" }
        select new object[] { output, source, protection };

    [Theory]
    [MemberData(nameof(TypedValidationWarningCases))]
    public async Task Typed_intrinsic_writes_commit_raw_data_and_one_policy_appropriate_validation_warning(bool output, string source, string protection)
    {
        using var document = JsonDocument.Parse(RawValidationValue);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline,
            isSensitive: protection == "sensitive", redactionMode: protection == "redacted" ? "mask" : null,
            retentionPolicy: protection == "retained" ? "retain" : null);
        var node = ValidationWarningNode(output, source, document.RootElement, policy);
        var executor = new WorkflowIntrinsicExecutor(_workflowStore, _activityStore, new RuntimeInputBindingResolver(), _durableStore,
            new RuntimeActivityExecutionInspectionAccumulator(_inspectionStore, _workflowStore, new RuntimeHistoryOptions()), new FakeTimeProvider(Now),
            externalPayloadStore: _payloadStore, portableExpressionEvaluator: new RawWarningExpressionEvaluator(document.RootElement.Clone()),
            valueConversionExecutor: new RuntimeValueConversionExecutor(new IntRegistry()), inputValidator: new WorkflowExecutableInputValidator(new IntRegistry()));

        var commit = await ExecuteAsync(node, document.RootElement, external: false, executor: executor);

        var value = output
            ? Assert.Single(commit.StateChanges.DurableValues).State!.InlineValue!.Value
            : commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["counter"].InlineValue!.Value;
        Assert.Equal(RawValidationValue, value.GetRawText());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("SecretBranch").GetProperty("nil").ValueKind);
        Assert.False(value.TryGetProperty("missing", out _));
        var warning = Assert.Single(Assert.Single(commit.StateChanges.ActivityExecutions).State!.Warnings!);
        Assert.Equal("json-unmatched-member", warning.Code);
        var diagnostic = JsonSerializer.Serialize(warning, JsonBaseline.Options);
        if (protection == "public") Assert.Contains("SecretBranch", diagnostic, StringComparison.Ordinal);
        else Assert.DoesNotContain("SecretBranch", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("9007199254740993", diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_typed_intrinsic_writes_still_refuse_before_returning_a_checkpoint(bool output)
    {
        using var document = JsonDocument.Parse("{\"known\":\"not an integer\",\"SecretBranch\":1}");
        var node = ValidationWarningNode(output, "literal", document.RootElement, ValueProtectionPolicy.InstanceInline);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(node, document.RootElement, external: false).AsTask());

        var unchanged = await _workflowStore.FindAsync(WorkflowExecutionId);
        Assert.Equal(ValuePresence.ExplicitNull, unchanged!.RootVariableFrame!.Values["counter"].Presence);
        Assert.Equal(0, unchanged.RootVariableFrame.Revision);
    }

    private static ExecutableNode ValidationWarningNode(bool output, string source, JsonElement raw, ValueProtectionPolicy policy)
    {
        var type = new ValueTypeDescriptor("Test.ValidationWarningRecord");
        var binding = source switch
        {
            "literal" => new RuntimeInputBinding("value", type, policy, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, raw, policy)),
            "member" => new RuntimeInputBinding("value", type, policy, RuntimeInputBindingSource.WorkflowRequestMember,
                selection: new RuntimeReadSelection(1, ["count"], RuntimeReadSelectionMode.DeferredFit), workflowRequest: new RuntimeWorkflowRequestReference("source")),
            _ => new RuntimeInputBinding("value", type, policy, RuntimeInputBindingSource.Expression,
                expression: new RuntimeExpressionBinding("test", "raw", capabilityProfile: ExpressionCapabilityProfiles.BindingReadonlyV1))
        };
        var bindings = new Dictionary<string, RuntimeInputBinding> { ["value"] = binding };
        if (output) bindings["name"] = new("name", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new("String"), JsonSerializer.SerializeToElement("raw-result", JsonBaseline.Options), ValueProtectionPolicy.InstanceInline));
        return new ExecutableNode("set-warning", "set-warning", "test.intrinsic", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }, JsonBaseline.Options), bindings,
            new Dictionary<string, string>(), intrinsicKind: output ? WorkflowIntrinsicKind.SetOutput : WorkflowIntrinsicKind.Set,
            intrinsicVariable: output ? null : new("counter", "workflow"));
    }

    private sealed class RawWarningExpressionEvaluator(JsonElement value) : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => ValueTask.FromResult(value.Clone());
    }

    public sealed class ValidationWarningRecord { public int Known { get; set; } }
}
