using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class RuntimeOutputCaptureNullPolicyTests
{
    public static IEnumerable<object[]> Cases =>
        from target in new[] { "output", "frame", "graph" }
        from encrypted in new[] { false, true }
        from noValue in new[] { false, true }
        from policy in new[] { false, true }
        from alias in new[] { "String", "Int32" }
        select new object[] { target, encrypted, noValue, policy, alias };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Captures_apply_the_pinned_policies_before_either_durable_write(
        string target, bool encrypted, bool noValue, bool policy, string alias)
    {
        var graph = target == "graph";
        var frame = target == "frame";
        var sourceType = new ValueTypeDescriptor("Elsa.Any");
        var descriptor = JsonSerializer.SerializeToElement(new { });
        var contract = new ActivityContract("script", "1", "test", descriptor, [],
            new ActivityResultContract(sourceType, true, ActivityValuePolicy.Default,
                [new ActivityResultProjectionContract("result", "value", sourceType, false, ActivityValuePolicy.Default)]),
            ["Done"], new ActivityActivationRequirement("test", "script"));
        var transition = ActivityTransition.Complete(new ScriptResult(noValue ? null : JsonSerializer.SerializeToElement<object?>(null)));
        var completion = new ActivityCompletionProjector().Project("invocation",
            new ActivityAttempt("attempt", "invocation", 1, ActivityAttemptReason.Initial, DateTimeOffset.UnixEpoch),
            contract, transition, DateTimeOffset.UnixEpoch);
        Assert.Equal(noValue ? ValuePresence.Absent : ValuePresence.ExplicitNull, completion.Projections["result"].Presence);
        if (encrypted)
            completion = completion with { Projections = new Dictionary<string, ValueEnvelope>
            {
                ["result"] = new ValueEnvelope(sourceType, noValue ? ValuePresence.Absent : ValuePresence.ExplicitNull, null, null,
                    new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true))
            } };
        var captureMetadata = graph ? new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.CaptureTargetScopeId] = "graph",
            [RuntimeMetadataKeys.TargetVariableReferenceKey] = "value",
            [RuntimeMetadataKeys.VariableName] = "value"
        } : frame ? new Dictionary<string, string> { [RuntimeMetadataKeys.TargetVariableReferenceKey] = "value" } : new Dictionary<string, string>();
        var capture = new RuntimeOutputCapture("result", (frame ? RuntimeWorkflowStateSeed.VariableValueIdPrefix : RuntimeWorkflowStateSeed.OutputValueIdPrefix) + "value",
            new RuntimeValueTypeDescriptor(alias, WellKnownRuntimeDurableValueStorageDrivers.Json,
                JsonSerializer.SerializeToElement(new TypeReference(alias))),
            DurableValueLifecycle.Instance, DurableValueStorage.Custom, true, captureMetadata);
        var node = new ExecutableNode("script", "script", "test.script", "1", "test", descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string> { [RuntimeMetadataKeys.ScriptResultInputKey] = "script" },
            activityContract: contract, outputCaptures: new Dictionary<string, RuntimeOutputCapture> { ["result"] = capture });
        var warnings = new ExecutionWarningCollector();
        var projector = new RuntimeOutputCaptureProjector(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]))
            .WithValuePolicies(new(policy, policy), warnings);
        var boundary = new ActivityExecutionState(
            new("boundary", "workflow", "graph", "graph", "test.graph", "1"), ActivityExecutionStatus.Completed,
            null, 1, DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch, null, null, null, null,
            new(null, null, null, null, null, null, null, null, new Dictionary<string, string>()),
            null, [], [], 0, 0, new Dictionary<string, string>());
        async Task<RuntimeOutputCaptureProjection> Project() => await projector.ProjectAsync("workflow", "invocation", node,
            (IActivityCompletionTransition)transition, completion, DateTimeOffset.UnixEpoch, closedScopeBoundary: graph ? boundary : null);

        if (!policy && (noValue || alias == "Int32"))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(Project);
            Assert.Contains(noValue ? "VF-UNDEF-001" : "VF-ACT-004", exception.Message);
            return;
        }
        if (encrypted && alias == "Int32")
        {
            if (frame) Assert.NotNull(Assert.Single((await Project()).WorkflowVariableWrites).Value.Withheld);
            else await Assert.ThrowsAnyAsync<InvalidOperationException>(Project);
            Assert.Equal("VF-NULL-001", Assert.Single(warnings.Warnings).Code);
            return;
        }
        var projection = await Project();
        var value = frame ? Assert.Single(projection.WorkflowVariableWrites).Value.InlineValue!.Value
            : Assert.Single(projection.DurableValues).State!.InlineValue!.Value;
        if (alias == "Int32")
        {
            Assert.Equal(0, value.GetInt32());
            Assert.Equal("VF-NULL-001", Assert.Single(warnings.Warnings).Code);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
            Assert.Empty(warnings.Warnings);
        }
    }

    private sealed record ScriptResult([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Value);
}
