using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class QueuedSelectedStartTests
{
    private static readonly ValueTypeDescriptor FixtureString = new(TypeAliasConvention.CanonicalAlias(typeof(string)));
    private static readonly ValueProtectionPolicy FixtureSensitive = new(DurableValueLifecycle.Instance, DurableValueStorage.Inline,
        isSensitive: true, redactionMode: "full", retentionPolicy: "test-only");

    [Theory]
    [InlineData(PersistenceMode.Durable, false, "present")]
    [InlineData(PersistenceMode.Balanced, false, "present")]
    [InlineData(PersistenceMode.Durable, true, "present")]
    [InlineData(PersistenceMode.Balanced, true, "present")]
    [InlineData(PersistenceMode.Durable, false, "null")]
    [InlineData(PersistenceMode.Balanced, false, "null")]
    [InlineData(PersistenceMode.Durable, true, "null")]
    [InlineData(PersistenceMode.Balanced, true, "null")]
    [InlineData(PersistenceMode.Durable, false, "sensitive")]
    [InlineData(PersistenceMode.Balanced, false, "sensitive")]
    [InlineData(PersistenceMode.Durable, true, "sensitive")]
    [InlineData(PersistenceMode.Balanced, true, "sensitive")]
    [InlineData(PersistenceMode.Durable, false, "contract-sensitive")]
    [InlineData(PersistenceMode.Balanced, false, "contract-sensitive")]
    [InlineData(PersistenceMode.Durable, true, "contract-sensitive")]
    [InlineData(PersistenceMode.Balanced, true, "contract-sensitive")]
    public async Task Native_leaf_consumes_whole_fixtures_through_bindings_and_expression_parameters(PersistenceMode mode, bool expression, string shape)
    {
        var owner = ResultOwner(shape);
        await using var harness = Build(owner, mode, ResultServices);
        await Setup(harness, owner, ResultConsumer(expression));
        var pinned = JsonSerializer.Serialize(owner.Executable);
        await RunLexical(harness, owner);
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        var value = activity.InputSnapshot!.Values["value"];
        Assert.Equal(shape == "null" ? ValuePresence.ExplicitNull : ValuePresence.Present, value.Presence);
        if (shape != "null") Assert.Equal("fixture-value", value.InlineValue!.Value.GetString());
        Assert.Equal(shape.Contains("sensitive", StringComparison.Ordinal), value.Policy.IsSensitive);
        if (shape.Contains("sensitive", StringComparison.Ordinal))
        {
            Assert.Equal("full", value.Policy.RedactionMode);
            Assert.Equal("test-only", value.Policy.RetentionPolicy);
        }
        var fixture = Assert.Single(state.TestContext!.Results);
        Assert.Equal("before", fixture.ProducerNodeId);
        Assert.Equal("fixture-scope", fixture.ProducerScopeId);
        Assert.Equal(shape == "null" ? ValuePresence.ExplicitNull : ValuePresence.Present, fixture.Result.Presence);
        Assert.Equal(pinned, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(owner.Executable!.Identity.ArtifactId)));
        Assert.Null(activity.SchedulingActivityExecutionId);
        Assert.Null(activity.ParentActivityExecutionId);
        Assert.DoesNotContain((await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId)),
            x => x.SourceActivityExecutionId == "before");
    }

    [Theory]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    [InlineData(false, "wrong-scope")]
    [InlineData(false, "wrong-type")]
    [InlineData(true, "wrong-type")]
    [InlineData(false, "owned-shadow")]
    [InlineData(true, "owned-shadow")]
    [InlineData(false, "duplicate")]
    [InlineData(true, "duplicate")]
    [InlineData(true, "ambiguous-scope")]
    [InlineData(false, "unknown-producer")]
    [InlineData(true, "unknown-producer")]
    [InlineData(false, "absent")]
    [InlineData(true, "absent")]
    [InlineData(false, "encrypted-plaintext")]
    [InlineData(true, "encrypted-plaintext")]
    [InlineData(false, "projection")]
    [InlineData(true, "projection")]
    public async Task Unsupported_fixture_setup_refuses_before_root_or_activity_even_for_optional_references(bool expression, string defect)
    {
        var owner = ResultOwner(defect);
        await using var harness = Build(owner, PersistenceMode.Balanced, ResultServices);
        await Setup(harness, owner, ResultConsumer(expression, defect == "projection" ? "named" : "$result"));
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData(PersistenceMode.Durable, false)]
    [InlineData(PersistenceMode.Balanced, false)]
    [InlineData(PersistenceMode.Durable, true)]
    [InlineData(PersistenceMode.Balanced, true)]
    public async Task Native_fixture_survives_header_roundtrip_and_fresh_activation_without_repreparation(PersistenceMode mode, bool expression)
    {
        var owner = ResultOwner("present");
        await using var harness = Build(owner, mode, services =>
        {
            ResultServices(services);
            new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        });
        await Setup(harness, owner, ResultConsumer(expression));
        await Checkpoints(harness).HandleAsync(await StageCheckpoint(harness, owner));
        var store = harness.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var state = (await store.FindAsync(harness.ExecutionId))!;
        var restored = JsonSerializer.Deserialize<WorkflowExecutionState>(JsonSerializer.Serialize(state))!;
        await using (var fresh = harness.Services.CreateAsyncScope())
            await fresh.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(restored);
        owner.Seed = _ => throw new InvalidOperationException("Fixture setup must not be resolved after admission.");
        await harness.SweepUntilQuietAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, (await store.FindAsync(harness.ExecutionId))!.Status);
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal("fixture-value", activity.InputSnapshot!.Values["value"].InlineValue!.Value.GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_intrinsic_consumes_fixture_through_the_same_result_reader(bool expression)
    {
        var owner = ResultOwner("present"); owner.QualifyTarget = target => target.IntrinsicKind == WorkflowIntrinsicKind.Set;
        owner.WorkflowVariables = [new("target", "target", FixtureString, ValueProtectionPolicy.InstanceInline,
            new("target", FixtureString, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(FixtureString, JsonSerializer.SerializeToElement("initial"), ValueProtectionPolicy.InstanceInline)))];
        await using var harness = Build(owner, PersistenceMode.Balanced, ResultServices);
        var binding = FixtureBinding(WorkflowIntrinsicInputKeys.Value, expression);
        var selected = new ExecutableNode("selected", "selected", "elsa.intrinsic.set", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding }, new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new("target", VariableReference.WorkflowScopeId));
        await Setup(harness, owner, selected); await RunLexical(harness, owner);
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        Assert.Equal("fixture-value", state.RootVariableFrame!.Values["target"].InlineValue!.Value.GetString());
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("context")]
    [InlineData("fingerprint")]
    [InlineData("consumer")]
    [InlineData("parent")]
    [InlineData("closed")]
    [InlineData("pin")]
    [InlineData("missing")]
    [InlineData("invocation")]
    [InlineData("consumer-workflow")]
    [InlineData("root-closed")]
    [InlineData("type")]
    [InlineData("policy")]
    public async Task Persisted_fixture_cannot_cross_a_consumer_or_context_boundary_or_fall_back_to_optional_null(string defect)
    {
        var owner = ResultOwner("present");
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
        {
            ResultServices(services);
            new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        });
        await Setup(harness, owner, ResultConsumer(false));
        await Checkpoints(harness).HandleAsync(await StageCheckpoint(harness, owner));
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        // Keep the committed running header, then obtain the real consumer identity through normal delivery.
        await harness.SweepUntilQuietAsync();
        var consumer = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        var validContext = new RuntimeInputBindingResolutionContext(harness.ExecutionId, consumer.InvocationId, consumer, executable: owner.Executable) { WorkflowState = state };
        var valid = await harness.Services.GetRequiredService<IRuntimeActivityInputMaterializer>().MaterializeSnapshotAsync(
            owner.Executable!.NodesById["selected"], consumer.InvocationId, validContext, owner.Clock.Now);
        Assert.Equal("fixture-value", valid.Values["value"].InlineValue!.Value.GetString());
        var original = state.TestContext!;
        if (defect is "context" or "fingerprint" or "missing") state = state with
        {
            TestContext = new(defect == "context" ? "foreign" : original.ContextId, defect == "fingerprint" ? "foreign" : original.ContextFingerprint,
                original.Frames, defect == "missing" ? [] : original.Results, original.OwnedNodeIds) { RootActivityExecutionId = original.RootActivityExecutionId }
        };
        if (defect == "root-closed") state = state with { RootVariableFrame = state.RootVariableFrame!.Close(state.RootVariableFrame.Revision) };
        if (defect is "type" or "policy") state = state with
        {
            TestContext = new(original.ContextId, original.ContextFingerprint, original.Frames,
                [original.Results[0] with { Result = defect == "type" ? Number(7) : ValueEnvelope.Inline(FixtureString,
                    JsonSerializer.SerializeToElement("protected-fixture"), new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, requiresEncryption: true)) }],
                original.OwnedNodeIds) { RootActivityExecutionId = original.RootActivityExecutionId }
        };
        if (defect == "workflow") state = state with { WorkflowExecutionId = "foreign" };
        if (defect == "closed") state = state with { Status = WorkflowExecutionStatus.Completed };
        if (defect == "parent") consumer = consumer with { ParentActivityExecutionId = "foreign" };
        if (defect == "consumer-workflow") consumer = consumer with { Execution = consumer.Execution with { WorkflowExecutionId = "foreign" } };
        if (defect == "consumer") consumer = consumer with { Execution = consumer.Execution with { ExecutableNodeId = "after" } };
        var executable = defect == "pin" ? WorkflowExecutionHarness.NewExecutable(owner.Executable!.RootActivity,
            owner.Executable.Identity with { ArtifactId = "foreign", ArtifactHash = "foreign" }) : owner.Executable!;
        var context = new RuntimeInputBindingResolutionContext(harness.ExecutionId, defect == "invocation" ? "foreign" : consumer.InvocationId, consumer, executable: executable) { WorkflowState = state };
        var node = owner.Executable!.NodesById["selected"];
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () =>
            await harness.Services.GetRequiredService<IRuntimeActivityInputMaterializer>().MaterializeSnapshotAsync(node, consumer.InvocationId, context, owner.Clock.Now));
    }

    private static Owner ResultOwner(string shape) => new()
    {
        QualifyTarget = target => target.ActivityType == typeof(LexicalStringActivity).FullName,
        WrapSelected = selected => new("container", "container", typeof(ProbeActivity).FullName!, "1", "test.container", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots: [new("children", [FixtureProducer(shape), selected, WorkflowExecutionHarness.NewProbeNode("after")])]),
        Seed = entry =>
        {
            var policy = shape.Contains("sensitive", StringComparison.Ordinal) ? FixtureSensitive : shape == "encrypted-plaintext" ?
                new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, requiresEncryption: true) : ValueProtectionPolicy.InstanceInline;
            var value = shape == "null" ? ValueEnvelope.Null(FixtureString, policy) : shape == "absent" ? ValueEnvelope.Absent(FixtureString, policy) :
                shape == "wrong-type" ? Number(3) : ValueEnvelope.Inline(FixtureString, JsonSerializer.SerializeToElement("fixture-value"), policy);
            var fixture = new RuntimeTestResultFixture(shape == "owned-shadow" ? "selected" : shape == "unknown-producer" ? "foreign" : "before",
                shape == "wrong-scope" ? "foreign" : "fixture-scope", value);
            return new(entry.ContextId, entry.ContextFingerprint, [], shape == "missing" ? [] : shape == "duplicate" ? [fixture, fixture] :
                shape == "ambiguous-scope" ? [fixture, fixture with { ProducerScopeId = "another-scope" }] : [fixture]);
        }
    };

    private static ExecutableNode FixtureProducer(string shape) => new("before", "before", "fixture.producer", "1", "fixture.producer", JsonSerializer.SerializeToElement(new { }),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: new("fixture.producer", "1", "fixture.producer",
            JsonSerializer.SerializeToElement(new { }), [], new(FixtureString, false, shape == "contract-sensitive" ?
                new(true, true, false, RedactionMode: "full", RetentionPolicy: "test-only") : ActivityValuePolicy.Default, []), ["Done"], new("fixture.producer", "probe")));

    private static ExecutableNode ResultConsumer(bool expression, string projection = "$result") => new("selected", "selected", typeof(LexicalStringActivity).FullName!,
        "1", "test.consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding> { ["value"] = FixtureBinding("value", expression, projection) },
        new Dictionary<string, string>());

    private static RuntimeInputBinding FixtureBinding(string key, bool expression, string projection = "$result") => new(key, FixtureString, ValueProtectionPolicy.InstanceInline,
        expression ? RuntimeInputBindingSource.Expression : RuntimeInputBindingSource.ActivityResult,
        activityResult: expression ? null : new("before", projection, "fixture-scope", isOptional: true),
        expression: expression ? new("fixture-expression", "p", parameters: new Dictionary<string, ExpressionParameterBinding>
        { ["p"] = new ActivityResultExpressionParameterBinding("before", projection, isOptional: true) }) : null);

    private static void ResultServices(IServiceCollection services)
    {
        services.AddSingleton<IPortableExpressionEvaluator, FixtureEvaluator>();
        services.AddSingleton<IRuntimeTestScopeContractSource, EmptyFixtureScope>();
    }
    private sealed class FixtureEvaluator : IPortableExpressionEvaluator, IPortableExpressionCapabilities
    {
        public bool CanEvaluate(ExpressionDefinition definition) => definition.Language == "fixture-expression" && definition.Source == "p";
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => ValueTask.FromResult(request.ParameterValues["p"].Clone());
    }
    private sealed class EmptyFixtureScope : IRuntimeTestScopeContractSource
    {
        public ValueTask<RuntimeTestScopeContract?> GetContractAsync(WorkflowExecutable executable, ExecutableNode node, VariableFrameKind kind,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimeTestScopeContract?>(node.ExecutableNodeId != "container" ? null :
                new("container", kind, false, new Dictionary<string, RuntimeVariableDeclaration>(), new Dictionary<string, ActivityInputContract>(), false));
    }
}
