using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class QueuedSelectedStartTests
{
    private static readonly ValueTypeDescriptor LexicalInt = new(TypeAliasConvention.CanonicalAlias(typeof(int)));
    private static ValueEnvelope Number(int value) => ValueEnvelope.Inline(LexicalInt, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);
    private static RuntimeVariableDeclaration Declaration(string key, int? initial = null) => new(key, key, LexicalInt,
        ValueProtectionPolicy.InstanceInline, initial is { } value ? Literal(key, value) : null);
    private static RuntimeInputBinding Literal(string key, int value) => new(key, LexicalInt, ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Literal, literal: Number(value));

    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task Selected_leaf_reads_run_owned_container_frame_without_executing_ancestors(PersistenceMode mode)
    {
        var owner = LexicalOwner();
        owner.WorkflowVariables = [Declaration("amount", 99)];
        owner.WrapSelected = selected => LexicalContainer(selected, closed: false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Container, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) })], []);
        await using var harness = Build(owner, mode);
        await Setup(harness, owner, Echo(new("value", LexicalInt, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead, variable: new("amount", "container"))));
        var before = JsonSerializer.Serialize(owner.Executable);
        await RunLexical(harness, owner);
        var state = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId);
        var frame = Assert.Single(state!.TestContext!.Frames).Frame;
        Assert.Equal(7, frame.Values["amount"].InlineValue!.Value.GetInt32());
        Assert.Equal(VariableFrameStatus.Closed, frame.Status);
        Assert.Equal(99, state.RootVariableFrame!.Values["amount"].InlineValue!.Value.GetInt32());
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal(7, activity.InputSnapshot!.Values["value"].InlineValue!.Value.GetInt32());
        Assert.Equal(8, activity.Completion!.Result.InlineValue!.Value.GetProperty("amount").GetInt32());
        Assert.Equal(before, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(owner.Executable!.Identity.ArtifactId)));
    }

    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task Selected_intrinsic_writes_only_its_supplied_lexical_owner(PersistenceMode mode)
    {
        var owner = LexicalOwner();
        owner.WorkflowVariables = [Declaration("amount", 99)];
        owner.WrapSelected = selected => LexicalContainer(selected, closed: false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Container, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) })], []);
        var selected = new ExecutableNode("selected", "selected", "elsa.intrinsic.set", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { [WorkflowIntrinsicInputKeys.Value] = Literal(WorkflowIntrinsicInputKeys.Value, 42) },
            new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new("amount", "container"));
        await using var harness = Build(owner, mode);
        await Setup(harness, owner, selected);
        await RunLexical(harness, owner);
        var state = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId);
        var frame = Assert.Single(state!.TestContext!.Frames).Frame;
        Assert.Equal(42, frame.Values["amount"].InlineValue!.Value.GetInt32());
        Assert.Equal(2, frame.Revision); // write and terminal close
        Assert.Equal(99, state.RootVariableFrame!.Values["amount"].InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData(PersistenceMode.Durable, true)]
    [InlineData(PersistenceMode.Balanced, true)]
    [InlineData(PersistenceMode.Durable, false)]
    [InlineData(PersistenceMode.Balanced, false)]
    public async Task Selected_leaf_reads_closed_request_and_captures_without_a_boundary_invocation(PersistenceMode mode, bool request)
    {
        var owner = LexicalOwner();
        owner.WorkflowVariables = [Declaration("amount", 99)];
        owner.WrapSelected = selected => LexicalContainer(selected, closed: true, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Root, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) }, true,
                new Dictionary<string, ValueEnvelope> { ["input"] = Number(7) })], []);
        await using var harness = Build(owner, mode, s => s.AddSingleton<IRuntimeTestScopeContractSource>(new ClosedLexicalContract()));
        var capture = new RuntimeOutputCapture("amount", "captured-amount", new(LexicalInt.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance, DurableValueStorage.Inline, true, new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CaptureTargetScopeId] = "container",
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = "amount"
            });
        await Setup(harness, owner, Echo(new("value", LexicalInt, ValueProtectionPolicy.InstanceInline,
            request ? RuntimeInputBindingSource.WorkflowRequest : RuntimeInputBindingSource.VariableRead,
            workflowRequest: request ? new("input") : null, variable: request ? null : new("amount", VariableReference.WorkflowScopeId)), capture));
        await RunLexical(harness, owner);
        var state = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId);
        var frame = Assert.Single(state!.TestContext!.Frames).Frame;
        Assert.Equal(8, frame.Values["amount"].InlineValue!.Value.GetInt32());
        Assert.Equal(VariableFrameStatus.Closed, frame.Status);
        Assert.Equal(2, frame.Revision);
        Assert.Equal(99, state.RootVariableFrame!.Values["amount"].InlineValue!.Value.GetInt32());
        Assert.Null(await harness.Services.GetRequiredService<IDurableValueStateStore>().FindAsync(harness.ExecutionId, "captured-amount"));
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_expression_and_direct_reads_share_the_nearest_supplied_shadow(bool expression)
    {
        var owner = LexicalOwner();
        owner.WorkflowVariables = [Declaration("amount", 99)];
        owner.WrapSelected = selected => LexicalContainer(new("inner", "inner", typeof(ProbeActivity).FullName!, "1", "inner",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new("children", [selected])], structure: new("test.container", "1",
                JsonSerializer.SerializeToElement(new { variables = new[] { Declaration("amount") } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Container, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) }),
             new("inner", VariableFrameKind.Container, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(11) })], []);
        await using var harness = Build(owner, PersistenceMode.Balanced, services => services.AddSingleton<IPortableExpressionEvaluator, LexicalEvaluator>());
        var binding = expression ? new RuntimeInputBinding("value", LexicalInt, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: new("JavaScript", "variables.amount + p", parameters: new Dictionary<string, ExpressionParameterBinding>
            { ["p"] = new VariableExpressionParameterBinding("inner", "amount") })) : new RuntimeInputBinding("value", LexicalInt,
                ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead, variable: new("amount", "inner"));
        await Setup(harness, owner, Echo(binding));
        await RunLexical(harness, owner);
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal(expression ? 22 : 11, activity.InputSnapshot!.Values["value"].InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData("frame-input")]
    [InlineData("frame-name")]
    [InlineData("frame-kind")]
    [InlineData("frame-scope")]
    [InlineData("producer-id")]
    public async Task Malformed_trusted_frame_shapes_refuse_before_any_execution(string defect)
    {
        var owner = LexicalOwner(); owner.WrapSelected = selected => LexicalContainer(selected, false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new(defect == "frame-scope" ? " " : "container", defect == "frame-kind" ? (VariableFrameKind)99 : VariableFrameKind.Container,
                new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) },
                Inputs: defect == "frame-input" ? new Dictionary<string, ValueEnvelope> { ["bad"] = null! } : null,
                VariableNames: defect == "frame-name" ? new Dictionary<string, string> { ["bad"] = null! } : null)],
            defect == "producer-id" ? [new(" ", "outside", Number(7))] : []);
        await using var harness = Build(owner, PersistenceMode.Balanced);
        await Setup(harness, owner, Echo(Literal("value", 7)));
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData("readonly")]
    [InlineData("missing")]
    public async Task Unavailable_enclosing_write_owner_never_runs_the_selected_intrinsic(string defect)
    {
        var owner = LexicalOwner(); owner.WrapSelected = selected => LexicalContainer(selected, false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint, defect == "missing" ? [] :
            [new("container", VariableFrameKind.Container, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) }, Writable: false)], []);
        var selected = new ExecutableNode("selected", "selected", "elsa.intrinsic.set", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { [WorkflowIntrinsicInputKeys.Value] = Literal(WorkflowIntrinsicInputKeys.Value, 42) },
            new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new("amount", "container"));
        await using var harness = Build(owner, PersistenceMode.Balanced); await Setup(harness, owner, selected);
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replaced_or_duplicated_frame_owner_cannot_qualify_selected_execution(bool duplicate)
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
        {
            services.AddSingleton<IRuntimeTestContextFrameAccess, ForeignFrames>();
            if (duplicate) services.AddSingleton<IRuntimeTestContextFrameAccess, ForeignFrames>();
        });
        await Setup(harness, owner);
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("identity")]
    [InlineData("scope")]
    [InlineData("outer-root")]
    [InlineData("workflow")]
    [InlineData("fingerprint")]
    public async Task Capture_writeback_checks_exact_persisted_frame_without_partial_mutation(string defect)
    {
        var owner = LexicalOwner(); owner.WrapSelected = selected => LexicalContainer(selected, true, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Root, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) }, true,
                new Dictionary<string, ValueEnvelope> { ["input"] = Number(7) })], []);
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
            services.AddSingleton<IRuntimeTestScopeContractSource, ClosedLexicalContract>());
        await Setup(harness, owner, Echo(Literal("value", 7)));
        await Checkpoints(harness).HandleAsync(await StageCheckpoint(harness, owner));
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        var frame = Assert.Single(state.TestContext!.Frames).Frame;
        var target = new RuntimeTestCaptureTarget(defect == "workflow" ? "foreign" : state.WorkflowExecutionId,
            defect == "identity" ? "foreign" : state.TestContext.ContextId, defect == "fingerprint" ? "foreign" : state.TestContext.ContextFingerprint,
            defect == "outer-root" ? state.RootVariableFrame!.FrameId : frame.FrameId,
            defect == "scope" ? "foreign" : frame.ScopeId, defect == "revision" ? frame.Revision + 1 : frame.Revision);
        var before = JsonSerializer.Serialize(state);
        var access = harness.Services.GetRequiredService<IRuntimeTestContextFrameAccess>();
        Assert.Throws<InvalidOperationException>(() => access.ApplyWrites(state, [new(target, "amount", Number(42))]));
        Assert.Equal(before, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId)));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fault_and_cancel_close_the_supplied_frame_owner(bool cancel)
    {
        var owner = LexicalOwner();
        owner.QualifyTarget = target => target.ActivityType == (cancel ? typeof(LexicalCancelActivity).FullName : typeof(FaultingActivity).FullName);
        owner.WrapSelected = selected => LexicalContainer(selected, false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Container, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) })], []);
        await using var harness = Build(owner, PersistenceMode.Balanced);
        var selected = cancel ? new ExecutableNode("selected", "selected", typeof(LexicalCancelActivity).FullName!, "1", "test.cancel",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()) :
            WorkflowExecutionHarness.NewFaultingNode("selected");
        await Setup(harness, owner, selected);
        _ = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        Assert.Equal(cancel ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Faulted, state.Status);
        Assert.Equal(VariableFrameStatus.Closed, Assert.Single(state.TestContext!.Frames).Frame.Status);
        Assert.Equal(VariableFrameStatus.Closed, state.RootVariableFrame!.Status);
        Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }


    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task Admitted_frames_survive_header_roundtrip_and_fresh_activation_without_reading_seed_again(PersistenceMode mode)
    {
        var owner = LexicalOwner(); owner.WrapSelected = selected => LexicalContainer(selected, false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Container, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) })], []);
        await using var harness = Build(owner, mode, services =>
            new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services));
        await Setup(harness, owner, Echo(new("value", LexicalInt, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead, variable: new("amount", "container"))));
        await Checkpoints(harness).HandleAsync(await StageCheckpoint(harness, owner));
        var stored = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        var restored = JsonSerializer.Deserialize<WorkflowExecutionState>(JsonSerializer.Serialize(stored))!;
        await using (var fresh = harness.Services.CreateAsyncScope())
            await fresh.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(restored);
        owner.Seed = _ => throw new InvalidOperationException("Admitted context must not be prepared again.");
        await harness.SweepUntilQuietAsync();
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(VariableFrameStatus.Closed, Assert.Single(state.TestContext!.Frames).Frame.Status);
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal(7, activity.InputSnapshot!.Values["value"].InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_capture_cannot_escape_a_supplied_closed_root_or_write_the_same_variable_twice(bool duplicate)
    {
        var owner = LexicalOwner(); owner.WrapSelected = selected => LexicalContainer(selected, true, Declaration("amount"));
        owner.WorkflowVariables = [Declaration("amount", 99)];
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Root, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) }, true,
                new Dictionary<string, ValueEnvelope> { ["input"] = Number(7) })], []);
        var metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.TargetVariableReferenceKey] = "amount" };
        if (duplicate) metadata[RuntimeMetadataKeys.CaptureTargetScopeId] = "container";
        var first = new RuntimeOutputCapture("amount", RuntimeWorkflowStateSeed.VariableValueIdPrefix + "amount",
            new(LexicalInt.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null), DurableValueLifecycle.Instance, DurableValueStorage.Inline, true, metadata);
        var echo = Echo(Literal("value", 7), first);
        if (duplicate) echo = new(echo.ExecutableNodeId, echo.AuthoredActivityId, echo.ActivityType, echo.ActivityTypeVersion,
            echo.DescriptorType, echo.DescriptorPayload, echo.InputBindings, echo.Metadata, outputCaptures:
                new Dictionary<string, RuntimeOutputCapture> { ["amount"] = first, ["copy"] = new("copy", "same-target",
                    first.Type, first.Lifecycle, first.Storage, true, metadata) });
        await using var harness = Build(owner, PersistenceMode.Balanced, services => services.AddSingleton<IRuntimeTestScopeContractSource, ClosedLexicalContract>());
        await Setup(harness, owner, echo);
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkpoint_encryption_backstop_checks_supplied_frame_values_and_inputs(bool input)
    {
        var owner = LexicalOwner(); owner.WrapSelected = selected => LexicalContainer(selected, false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Container, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) })], []);
        await using var harness = Build(owner, PersistenceMode.Balanced); await Setup(harness, owner, Echo(Literal("value", 7)));
        await Checkpoints(harness).HandleAsync(await StageCheckpoint(harness, owner));
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        var ownerFrame = Assert.Single(state.TestContext!.Frames);
        var protectedValue = ValueEnvelope.Inline(LexicalInt, JsonSerializer.SerializeToElement(7),
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, requiresEncryption: true));
        var changedFrame = input ? ownerFrame with { Inputs = new Dictionary<string, ValueEnvelope> { ["secret"] = protectedValue } } :
            ownerFrame with { Frame = ownerFrame.Frame.Set("amount", protectedValue, ownerFrame.Frame.Revision) };
        var forged = state with { TestContext = new(state.TestContext.ContextId, state.TestContext.ContextFingerprint,
            [changedFrame], [], state.TestContext.OwnedNodeIds) };
        var commit = new RuntimeCheckpointCommit("protected", new("protected", "Guard", harness.ExecutionId, owner.Clock.Now, [],
            new Dictionary<string, string>()), new(new(harness.ExecutionId, RuntimeStateChangeOperation.Upsert, forged, new Dictionary<string, string>()), null, [], [], [], [], []),
            [], new Dictionary<string, string>());
        var exception = Assert.Throws<RuntimeCheckpointCommitValidationException>(() =>
            Elsa.Workflows.Runtime.Services.Checkpoints.RuntimeCheckpointCommitValidator.Validate(commit));
        Assert.Contains("encryption", exception.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canonical_frame_owner_with_foreign_captured_store_cannot_qualify(bool workflow)
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
            services.AddScoped<RuntimeTestContextOwner>(sp => new(
                workflow ? new Elsa.Workflows.Runtime.Services.Executions.InMemoryWorkflowExecutionStateStore() : sp.GetRequiredService<IWorkflowExecutionStateStore>(),
                workflow ? sp.GetRequiredService<IActivityExecutionStateStore>() : new Elsa.Workflows.Runtime.Services.ActivityExecutions.InMemoryActivityExecutionStateStore())));
        await Setup(harness, owner);
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Supplied_iteration_is_readable_but_never_a_writable_intrinsic_owner(bool write)
    {
        var owner = LexicalOwner(); owner.WrapSelected = selected => LexicalContainer(selected, false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Iteration, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) })], []);
        var selected = write ? new ExecutableNode("selected", "selected", "elsa.intrinsic.set", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { [WorkflowIntrinsicInputKeys.Value] = Literal(WorkflowIntrinsicInputKeys.Value, 42) },
            new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new("amount", "container")) :
            Echo(new("value", LexicalInt, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead, variable: new("amount", "container")));
        await using var harness = Build(owner, PersistenceMode.Balanced, services => services.AddSingleton<IRuntimeTestScopeContractSource, IterationContract>());
        await Setup(harness, owner, selected);
        if (write)
        {
            await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
                await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
            Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
            Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        }
        else
        {
            await RunLexical(harness, owner);
            var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
            var frame = Assert.Single(state.TestContext!.Frames);
            Assert.False(frame.Writable);
            Assert.Equal(7, frame.Frame.Values["amount"].InlineValue!.Value.GetInt32());
            Assert.Equal(VariableFrameStatus.Closed, frame.Frame.Status);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closed_intrinsic_write_resolves_scope_or_workflow_alias_without_outer_writeback(bool alias)
    {
        var owner = LexicalOwner(); owner.WrapSelected = selected => LexicalContainer(selected, true, Declaration("amount"));
        owner.WorkflowVariables = [Declaration("amount", 99)];
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Root, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) }, true,
                new Dictionary<string, ValueEnvelope> { ["input"] = Number(7) })], []);
        await using var harness = Build(owner, PersistenceMode.Balanced, services => services.AddSingleton<IRuntimeTestScopeContractSource, ClosedLexicalContract>());
        var selected = new ExecutableNode("selected", "selected", "elsa.intrinsic.set", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { [WorkflowIntrinsicInputKeys.Value] = Literal(WorkflowIntrinsicInputKeys.Value, 42) },
            new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new("amount", alias ? VariableReference.WorkflowScopeId : "container"));
        await Setup(harness, owner, selected); await RunLexical(harness, owner);
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        Assert.Equal(42, Assert.Single(state.TestContext!.Frames).Frame.Values["amount"].InlineValue!.Value.GetInt32());
        Assert.Equal(99, state.RootVariableFrame!.Values["amount"].InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_null_reaches_the_real_leaf_but_missing_lexical_value_refuses(bool present)
    {
        var type = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(string)));
        var owner = LexicalOwner(); owner.QualifyTarget = target => target.ActivityType == typeof(LexicalStringActivity).FullName;
        owner.WrapSelected = selected => LexicalContainer(selected, false, new RuntimeVariableDeclaration("text", "text", type, ValueProtectionPolicy.InstanceInline));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint, [new("container", VariableFrameKind.Container,
            present ? new Dictionary<string, ValueEnvelope> { ["text"] = ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline) } :
                new Dictionary<string, ValueEnvelope>())], []);
        var selected = new ExecutableNode("selected", "selected", typeof(LexicalStringActivity).FullName!, "1", "test.null", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = new("value", type, ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.VariableRead, variable: new("text", "container")) }, new Dictionary<string, string>());
        await using var harness = Build(owner, PersistenceMode.Balanced); await Setup(harness, owner, selected);
        if (present)
        {
            await RunLexical(harness, owner);
            var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
            Assert.Equal(ValuePresence.ExplicitNull, activity.InputSnapshot!.Values["value"].Presence);
            Assert.Equal(JsonValueKind.Null, activity.Completion!.Result.InlineValue!.Value.GetProperty("value").ValueKind);
        }
        else
        {
            await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
                await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
            Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        }
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_scope_iteration_and_container_keep_native_precedence(bool reversed)
    {
        var owner = LexicalOwner(); owner.WrapSelected = selected => LexicalContainer(selected, false, Declaration("amount"));
        owner.Seed = entry =>
        {
            var iteration = new RuntimeTestFrameSeed("container", VariableFrameKind.Iteration,
                new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) });
            var container = new RuntimeTestFrameSeed("container", VariableFrameKind.Container,
                new Dictionary<string, ValueEnvelope> { ["amount"] = Number(11) });
            return new(entry.ContextId, entry.ContextFingerprint, reversed ? [container, iteration] : [iteration, container], []);
        };
        await using var harness = Build(owner, PersistenceMode.Balanced, services => services.AddSingleton<IRuntimeTestScopeContractSource, IterationContract>());
        await Setup(harness, owner, Echo(new("value", LexicalInt, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead, variable: new("amount", "container"))));
        if (reversed)
        {
            await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
                await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
            Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        }
        else
        {
            await RunLexical(harness, owner);
            var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
            Assert.Equal(11, activity.InputSnapshot!.Values["value"].InlineValue!.Value.GetInt32());
        }
    }

    private sealed class IterationContract : IRuntimeTestScopeContractSource
    {
        public ValueTask<RuntimeTestScopeContract?> GetContractAsync(WorkflowExecutable executable, ExecutableNode node,
            VariableFrameKind kind, CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimeTestScopeContract?>(
            node.ExecutableNodeId != "container" || kind != VariableFrameKind.Iteration ? null : new("container", kind, false,
                new Dictionary<string, RuntimeVariableDeclaration> { ["amount"] = Declaration("amount") }, new Dictionary<string, ActivityInputContract>(), false));
    }
    public sealed record LexicalStringResult([property: Output(Key = "value", Path = "value", IsRequired = false)] string? Value);
    public sealed class LexicalStringActivity : Activity<LexicalStringResult>
    {
        [ActivityInput(Key = "value")] public string? Value { get; set; }
        protected override ValueTask<ActivityTransition<LexicalStringResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new LexicalStringResult(Value)));
    }

    private sealed class LexicalEvaluator : IPortableExpressionEvaluator, IPortableExpressionCapabilities
    {
        public bool CanEvaluate(ExpressionDefinition definition) => definition.Language == "JavaScript" && definition.Source == "variables.amount + p";
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => ValueTask.FromResult(JsonSerializer.SerializeToElement(
            request.ParameterValues["p"].GetInt32() + request.AmbientVariables["amount"].GetInt32()));
    }
    private sealed class ForeignFrames : IRuntimeTestContextFrameAccess
    {
        public ValueTask<RuntimeTestCaptureTarget?> FindCaptureTargetAsync(string workflowExecutionId, string activityExecutionId,
            string scopeId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Foreign frame owner must not execute.");
        public WorkflowExecutionState ApplyWrites(WorkflowExecutionState workflow, IReadOnlyCollection<RuntimeTestFrameWrite> writes) =>
            throw new InvalidOperationException("Foreign frame owner must not execute.");
    }
    public sealed class LexicalCancelActivity : Activity<LexicalResult>
    {
        protected override ValueTask<ActivityTransition<LexicalResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Cancel<LexicalResult>("test cancellation"));
    }

    private static Owner LexicalOwner() => new()
    {
        QualifyTarget = target => target.ActivityType == typeof(LexicalEchoActivity).FullName || target.IntrinsicKind == WorkflowIntrinsicKind.Set
    };

    private static ExecutableNode LexicalContainer(ExecutableNode selected, bool closed, params RuntimeVariableDeclaration[] variables) => new(
        "container", "container", typeof(ProbeActivity).FullName!, "1", "test.container", JsonSerializer.SerializeToElement(new { }),
        new Dictionary<string, RuntimeInputBinding>(), closed ? new Dictionary<string, string>
        { [RuntimeMetadataKeys.ActivityExecutionScope] = RuntimeMetadataKeys.ActivityExecutionScopeClosed } : new Dictionary<string, string>(),
        childSlots: [new("children", [WorkflowExecutionHarness.NewProbeNode("before"), selected, WorkflowExecutionHarness.NewProbeNode("after")])],
        structure: new("test.container", "1", JsonSerializer.SerializeToElement(new { variables }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static ExecutableNode Echo(RuntimeInputBinding binding, RuntimeOutputCapture? capture = null) => new(
        "selected", "selected", typeof(LexicalEchoActivity).FullName!, "1", "test.echo", JsonSerializer.SerializeToElement(new { }),
        new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding }, new Dictionary<string, string>(),
        outputCaptures: capture is null ? null : new Dictionary<string, RuntimeOutputCapture> { [capture.OutputName] = capture });

    private static async Task RunLexical(WorkflowExecutionHarness harness, Owner owner)
    {
        var result = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        Assert.True(result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Reason);
        var state = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId);
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.NotNull(state.TestContext);
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal("selected", activity.Execution.ExecutableNodeId);
        Assert.Null(activity.ParentActivityExecutionId);
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);
    }

    private sealed class ClosedLexicalContract : IRuntimeTestScopeContractSource
    {
        public ValueTask<RuntimeTestScopeContract?> GetContractAsync(WorkflowExecutable executable, ExecutableNode node,
            VariableFrameKind kind, CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimeTestScopeContract?>(
            node.ExecutableNodeId != "container" || kind != VariableFrameKind.Root ? null : new("container", kind, true,
                new Dictionary<string, RuntimeVariableDeclaration> { ["amount"] = Declaration("amount") },
                new Dictionary<string, ActivityInputContract> { ["input"] = new("input", "input", LexicalInt, true, false, false, null, ActivityValuePolicy.Default) }, true));
    }

    public sealed record LexicalResult([property: Output(Key = "amount", Path = "amount")] int Amount)
    {
        [Output(Key = "copy", Path = "copy")] public int Copy => Amount;
    }
    public sealed class LexicalEchoActivity : Activity<LexicalResult>
    {
        [ActivityInput(Key = "value")] public int Value { get; set; }
        protected override ValueTask<ActivityTransition<LexicalResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new LexicalResult(Value + 1)));
    }
}
