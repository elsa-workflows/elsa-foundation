using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

/// <summary>Real model-owned Graph scope preparation and normal native execution, not publishing/Studio acceptance.</summary>
public class GraphSelectedTestContextTests
{
    private static readonly ValueTypeDescriptor IntType = new(TypeAliasConvention.CanonicalAlias(typeof(int)));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false, "total-key", false, false, false, false)]
    [InlineData(false, "total-key", false, true, false, false)]
    [InlineData(true, "total-key", false, false, false, false)]
    [InlineData(true, "missing-key", false, false, false, false)]
    [InlineData(true, "total-key", true, false, false, false)]
    [InlineData(true, "total-key", false, false, true, false)]
    [InlineData(true, "total-key", false, false, true, true)]
    public async Task Selected_graph_body_uses_only_a_prepared_model_owned_context(bool clrCapture, string captureKey,
        bool wrongCaptureType, bool javaScript, bool protectedResult, bool siblingProtection)
    {
        var source = new Setup();
        var executionId = KeyedWorkflowStartIdentity.For("graph-context", WorkflowExecutionHarness.Identity.ArtifactId).WorkflowExecutionId;
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(s => new GraphActivitiesRuntimeFeature().ConfigureServices(s))
            .WithFeature(s => new ActivitiesSequenceFeature().ConfigureServices(s))
            .WithFeature(s => new ExpressionsFeature().ConfigureServices(s))
            .WithFeature(s => new JavaScriptFeature().ConfigureServices(s))
            .WithFeature(s => new JintFeature().ConfigureServices(s))
            .ConfigureServices(s =>
            {
                s.AddSingleton<IWorkflowTestEntryContextResolver>(source);
                s.AddSingleton<IRuntimeTestContextSeedSource>(source);
                s.AddSingleton<IRuntimeTestEffectPolicySource>(source);
            }).Build(WorkflowExecutionHarness.Identity, executionId, Enumerable.Range(1, 20).Select(x => "graph-activity-" + x));
        var types = harness.Services.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(int), IntType.Alias);
        types.RegisterType(typeof(Echo), TypeAliasConvention.CanonicalAlias(typeof(Echo)));
        types.RegisterType(typeof(EchoResult), TypeAliasConvention.CanonicalAlias(typeof(EchoResult)));
        var selected = Selected(clrCapture, captureKey, wrongCaptureType, javaScript);
        if (protectedResult) selected = WithProtectedResult(selected, siblingProtection);
        var boundary = Boundary(selected);
        var outside = new[] { WorkflowExecutionHarness.NewProbeNode("before"), boundary, WorkflowExecutionHarness.NewProbeNode("after") };
        var root = new ExecutableNode("container", "container", typeof(Elsa.Activities.Sequence.Activities.Sequence).FullName!, "1", "test.sequence",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new(Elsa.Activities.Sequence.Activities.Sequence.ActivitiesSlotName, outside)],
            structure: new(Elsa.Activities.Sequence.Activities.Sequence.StructureKind, Elsa.Activities.Sequence.Activities.Sequence.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = outside.Select(x => x.ExecutableNodeId) })));
        var candidate = WorkflowExecutionHarness.NewExecutable(root);
        // The harness pins normal CLR contracts into an owned artifact fixture; no external host is published.
        var reference = await harness.PublishAsync(candidate, "graph-fixture");
        var stored = (await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(candidate.Identity.ArtifactId))!;
        var original = JsonSerializer.Serialize(stored);
        var now = TimeProvider.System.GetUtcNow();
        var scope = new WorkflowTestScope("graph-test-scope", now.AddMinutes(10), null, new(WorkflowExecutionPartition.DefaultValue));
        reference = reference with { SourceReferenceId = "graph-test-source", Scope = WorkflowExecutableReferenceScope.TestRun,
            ExpiresAt = scope.ExpiresAt, PublishedAt = null, ActivationId = null, SlotId = null };
        await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
        await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(scope, now);
        var entry = new WorkflowTestEntry("graph-context", "graph-context-fingerprint", stored.Identity, "selected", scope, new("tests", "tests"));
        source.Entry = entry;
        source.Seed = new(entry.ContextId, entry.ContextFingerprint,
            [new("boundary", VariableFrameKind.Root, new Dictionary<string, ValueEnvelope>(), IsClosedBoundary: true)], []);
        var preparation = await harness.Services.GetRequiredService<IRuntimeTestContextPreparer>().PrepareAsync(
            entry, stored, executionId, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>());
        // Preparation is pure: invalid capture setup cannot run earlier work or create execution rows.
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId));
        if (captureKey != "total-key" || wrongCaptureType)
        {
            Assert.False(preparation.IsAvailable);
            Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(preparation.Findings).Kind);
            return;
        }
        Assert.True(preparation.IsAvailable, string.Join("; ", preparation.Findings.Select(x => x.Code)));
        if (protectedResult)
        {
            // The model declares an ordinary inline variable; this writer preserves the captured source
            // protection, so canonical-root refusal rules must not be applied to this supplied frame.
            var declared = preparation.Context!.Frames.Single().Frame.Values["total-key"].Policy;
            Assert.False(declared.IsSensitive);
            Assert.Null(declared.RedactionMode);
            Assert.Null(declared.RetentionPolicy);
        }
        var request = new WorkflowExecutionStartDispatchRequest(stored.Identity.ArtifactId, "tests", executionId,
            "graph-context", null, null, null, null, null, WorkflowRunKind.TestRun, new(reference.SourceReferenceId),
            WorkflowExecutableProvenanceRequirement.RequireLiveReference, null, null, null, scope.Partition, entry.Authority, null, 0, scope)
            { TestEntry = entry };
        await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(request, WorkflowExecutableReferenceScope.TestRun);
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(clrCapture ? 23 : javaScript ? 8 : 7, state.TestContext!.Frames.Single().Frame.Values["total-key"].InlineValue!.Value.GetInt32());
        Assert.Equal(VariableFrameStatus.Closed, state.TestContext.Frames.Single().Frame.Status);
        if (protectedResult)
        {
            var captured = state.TestContext.Frames.Single().Frame.Values["total-key"].Policy;
            Assert.True(captured.IsSensitive);
            Assert.Equal("test-mask", captured.RedactionMode);
            Assert.Equal("test-retain", captured.RetentionPolicy);
        }
        Assert.Empty(state.RootVariableFrame!.Values);
        Assert.Equal("selected", Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId)).Execution.ExecutableNodeId);
        Assert.Equal(original, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(stored.Identity.ArtifactId)));
    }

    private static ExecutableNode WithProtectedResult(ExecutableNode node, bool sibling)
    {
        var policy = ActivityValuePolicy.Default with
        { IsSensitive = true, RedactionMode = "test-mask", RetentionPolicy = "test-retain" };
        var projections = new List<ActivityResultProjectionContract>
        { new("value", "value", IntType, true, sibling ? ActivityValuePolicy.Default : policy) };
        if (sibling) projections.Add(new("sibling", "value", IntType, true, policy));
        var descriptor = new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, "1",
            JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(Echo)))));
        var contract = new ActivityContract(typeof(Echo).FullName!, "1", typeof(ClrActivityDescriptor).FullName!, descriptor.Payload,
            [new("value", "Value", IntType, false, false, false, null, ActivityValuePolicy.Default)],
            new(new(TypeAliasConvention.CanonicalAlias(typeof(EchoResult))), true,
                ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result }, projections),
            ["Done"], new(typeof(ClrActivityDescriptor).FullName!, TypeAliasConvention.CanonicalAlias(typeof(Echo))));
        return new(node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion,
            descriptor, node.InputBindings, node.OutputCaptures, node.Metadata, activityContract: contract);
    }

    private static ExecutableNode Selected(bool capture, string captureKey, bool wrongCaptureType, bool javaScript)
    {
        var binding = capture
            ? new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(IntType, JsonSerializer.SerializeToElement(23), ValueProtectionPolicy.InstanceInline))
            : new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
                variable: new("total-key", "boundary"));
        if (javaScript)
            binding = new("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
                expression: new("JavaScript", "variables.total + 1"));
        if (!capture)
            return new("selected", "selected", "elsa.intrinsic.set", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, RuntimeInputBinding> { ["value"] = binding }, new Dictionary<string, string>(),
                intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new("total-key", "boundary"));
        var captureType = wrongCaptureType ? new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(string))) : IntType;
        var output = new RuntimeOutputCapture("value", "graph-variable:" + captureKey,
            new RuntimeValueTypeDescriptor(captureType.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, JsonSerializer.SerializeToElement(new { alias = captureType.Alias })),
            DurableValueLifecycle.Instance, DurableValueStorage.Custom, true,
            new Dictionary<string, string> { [RuntimeMetadataKeys.CaptureTargetScopeId] = "boundary", [RuntimeMetadataKeys.TargetVariableReferenceKey] = captureKey });
        return new("selected", "selected", typeof(Echo).FullName!, "1", "test.echo", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = binding }, new Dictionary<string, string>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { ["value"] = output });
    }

    private static ExecutableNode Boundary(ExecutableNode selected)
    {
        var descriptor = new GraphActivityDescriptor("definition", "definition-version", "1.0.0", "template-hash", "selected", null, [],
            [new("total-key", "total", JsonSerializer.SerializeToElement(new { alias = IntType.Alias }), WellKnownRuntimeDurableValueStorageDrivers.Json,
                new("Literal", JsonSerializer.SerializeToElement(7)))], [], [], [], []);
        var data = JsonNode.Parse(JsonSerializer.Serialize(descriptor, Json))!.AsObject();
        data["scopeModel"] = RuntimeMetadataKeys.ActivityExecutionScopeClosed;
        var runtime = new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.GraphActivity, "1", JsonSerializer.SerializeToElement(data, Json));
        var contract = new ActivityContract("graph", "1.0.0", WellKnownRuntimeActivityConsumers.GraphActivity, runtime.Payload, [],
            new(new("object"), true, ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result }, []), ["Done"],
            new(WellKnownRuntimeActivityConsumers.GraphActivity, WellKnownRuntimeActivityConsumers.GraphActivity));
        return new("boundary", "boundary", "graph", "1.0.0", runtime, new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ActivityExecutionScope] = RuntimeMetadataKeys.ActivityExecutionScopeClosed,
                ["activity.definitionId"] = descriptor.DefinitionId, ["activity.definitionVersionId"] = descriptor.DefinitionVersionId,
                ["activity.version"] = descriptor.Version, ["activity.templateHash"] = descriptor.TemplateHash,
                ["graph.templateHash"] = descriptor.TemplateHash
            }, childSlots: [new("Graph.Entry", [selected])], activityContract: contract);
    }

    // This fixture explicitly qualifies its known pure invocation; it supplies no external effect capability.
    private sealed class Setup : IWorkflowTestEntryContextResolver, IRuntimeTestContextSeedSource, IRuntimeTestEffectPolicySource
    {
        public WorkflowTestEntry Entry { get; set; } = null!;
        public RuntimeTestContextSeed Seed { get; set; } = null!;
        public ValueTask<WorkflowTestEntry?> FindAsync(string contextId, CancellationToken cancellationToken = default) => ValueTask.FromResult<WorkflowTestEntry?>(contextId == Entry.ContextId ? Entry : null);
        public ValueTask<RuntimeTestContextSeed?> ResolveAsync(WorkflowTestEntry entry, WorkflowExecutable executable, string workflowExecutionId, CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimeTestContextSeed?>(Seed);
        public ValueTask ValidateNodeAsync(WorkflowTestEntry entry, RuntimeTestNodeRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ValidateActivityAsync(WorkflowTestEntry entry, RuntimeTestInvocation invocation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<RuntimeSecretReference> MapSecretAsync(WorkflowTestEntry entry, RuntimeTestInvocation invocation, string inputKey,
            RuntimeSecretReference reference, CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimeSecretReference>(Unavailable());
        public ValueTask<RuntimeTestEffectAdmission> BeginAsync(WorkflowTestEntry entry, RuntimeTestEffectRequest request, CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimeTestEffectAdmission>(Unavailable());
        public ValueTask RevalidateAsync(WorkflowTestEntry entry, RuntimeTestEffectAdmission admission, CancellationToken cancellationToken = default) => ValueTask.FromException(Unavailable());
        public ValueTask CompleteAsync(WorkflowTestEntry entry, RuntimeTestEffectCompletion completion, CancellationToken cancellationToken = default) => ValueTask.FromException(Unavailable());
        private static RuntimeTestEffectUnavailableException Unavailable() => new("test-only.external-unavailable", "The Graph context witness has no external effect profile.");
    }

    public sealed class Echo : Activity<EchoResult>
    {
        [ActivityInput(Key = "value")] public int Value { get; set; }
        protected override ValueTask<ActivityTransition<EchoResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new EchoResult(Value)));
    }
    public sealed record EchoResult([property: Output(Key = "value", Path = "value")] int Value);
}
