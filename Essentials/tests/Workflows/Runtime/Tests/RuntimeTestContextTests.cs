using System.Text.Json;
using Elsa.Activities.Sequence;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeTestContextTests
{
    private static readonly ValueTypeDescriptor IntType = new(TypeAliasConvention.CanonicalAlias(typeof(int)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selected_intrinsic_reads_supplied_scope_and_writes_its_owner_in_normal_runtime(bool expression)
    {
        var binding = expression
            ? new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
                expression: new("Test", "value", parameters: new Dictionary<string, ExpressionParameterBinding>
                { ["value"] = new VariableExpressionParameterBinding("container", "source") }))
            : new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
                variable: new("source", "container"));
        await using var fixture = await Fixture.CreateAsync(SetNode(binding));
        await fixture.StartAsync();

        var state = await fixture.StateAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(7, Assert.Single(state.TestContext!.Frames).Frame.Values["counter"].InlineValue!.Value.GetInt32());
        Assert.Equal(VariableFrameStatus.Closed, state.TestContext.Frames[0].Frame.Status);
        Assert.Empty(state.RootVariableFrame!.Values);
        Assert.Equal("selected", Assert.Single(await fixture.ActivitiesAsync()).Execution.ExecutableNodeId);
    }

    [Fact]
    public async Task An_ordinary_scope_cannot_be_relabelled_as_a_closed_boundary()
    {
        var binding = new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new("request-value"));
        await using var fixture = await Fixture.CreateAsync(SetNode(binding), closed: true);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.ValidateSetupAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Unqualified_iteration_scope_is_unavailable_before_scheduling()
    {
        await using var fixture = await Fixture.CreateAsync(SetNode(Literal(9)), iteration: true);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.ValidateSetupAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Selected_composite_runs_owned_child_with_supplied_parent_scope_only()
    {
        var child = SetNode(Literal(9), "owned-child");
        await using var fixture = await Fixture.CreateAsync(Sequence("selected", [child]));
        await fixture.StartAsync();
        var state = await fixture.StateAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(9, state.TestContext!.Frames[0].Frame.Values["counter"].InlineValue!.Value.GetInt32());
        Assert.Equal(new[] { "owned-child", "selected" },
            (await fixture.ActivitiesAsync()).Select(x => x.Execution.ExecutableNodeId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Cancellation_closes_supplied_frames_of_a_waiting_selected_activity()
    {
        var waiting = new ExecutableNode("selected", "selected", typeof(WorkflowTestEntryTests.WaitingActivity).FullName!, "1",
            "test.wait", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        await using var fixture = await Fixture.CreateAsync(waiting);
        fixture.Harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(
            typeof(WorkflowTestEntryTests.WaitingActivity), TypeAliasConvention.CanonicalAlias(typeof(WorkflowTestEntryTests.WaitingActivity)));
        await fixture.StartAsync();
        Assert.Equal(VariableFrameStatus.Active, (await fixture.StateAsync()).TestContext!.Frames[0].Frame.Status);
        await fixture.Harness.CancelAsync(fixture.Harness.ExecutionId, fixture.Entry.Scope.Partition);
        var stopped = await fixture.StateAsync();
        Assert.Equal(WorkflowExecutionStatus.Cancelled, stopped.Status);
        Assert.Equal(VariableFrameStatus.Closed, stopped.TestContext!.Frames[0].Frame.Status);
    }

    [Fact]
    public async Task Expression_fixture_value_and_dependency_policy_use_the_same_typed_source()
    {
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var context = Context([new("outside", "container", ValueEnvelope.Inline(IntType, JsonSerializer.SerializeToElement(7), policy))]);
        var expression = new RuntimeExpressionBinding("Test", "value", parameters: new Dictionary<string, ExpressionParameterBinding>
        { ["value"] = new ActivityResultExpressionParameterBinding("outside", "$result", true) });
        var result = await new RuntimePortableExpressionEvaluator(new Echo(), null).EvaluateAsync(expression, IntType,
            ValueProtectionPolicy.InstanceInline, "selected", "value", context, CancellationToken.None);
        Assert.Equal(7, result.Value.GetInt32());
        Assert.True(result.EffectivePolicy.IsSensitive);
        await Assert.ThrowsAsync<ExpressionInputFailureException>(() => new RuntimePortableExpressionEvaluator(new Echo(), null)
            .EvaluateAsync(expression, IntType, ValueProtectionPolicy.InstanceInline, "selected", "value", Context([]), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Closed_capture_requires_a_model_qualified_owner_before_scheduling()
    {
        var capture = new RuntimeOutputCapture("value", "captured-value",
            new RuntimeValueTypeDescriptor(IntType.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance, DurableValueStorage.Inline, true,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CaptureTargetScopeId] = "container",
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = "counter"
            });
        var selected = new ExecutableNode("selected", "selected", typeof(EchoActivity).FullName!, "1", "test.echo",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding> { ["value"] = Literal(23) },
            new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture> { ["value"] = capture });
        await using var fixture = await Fixture.CreateAsync(selected, closed: true);
        fixture.Harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(EchoActivity), TypeAliasConvention.CanonicalAlias(typeof(EchoActivity)));
        fixture.Harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(EchoResult), TypeAliasConvention.CanonicalAlias(typeof(EchoResult)));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.ValidateSetupAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Wrong_inline_fixture_payload_refuses_a_composite_before_its_earlier_child_runs()
    {
        await using var fixture = await Fixture.CreateAsync(Sequence("selected",
            [WorkflowExecutionHarness.NewProbeNode("earlier-owned-child"), SetNode(Literal(9), "later-owned-child")]));
        ReplaceSourceValue(fixture, ValueEnvelope.Inline(IntType, JsonSerializer.SerializeToElement("wrong-number"), ValueProtectionPolicy.InstanceInline));
        await Record.ExceptionAsync(() => fixture.StartAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.ValidateSetupAsync());
        Assert.Contains("payload is incompatible", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unqualified_external_payload_is_unavailable_without_reading_its_locator()
    {
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.External);
        await using var fixture = await Fixture.CreateAsync(SetNode(Literal(9)), withDefaults: false, sourcePolicy: policy);
        ReplaceSourceValue(fixture, ValueEnvelope.External(IntType,
            new("test-unavailable-reader", "must-not-read", new Dictionary<string, string>()), policy));
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.ValidateSetupAsync());
        Assert.Contains("External test context payloads", error.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task A_schema_without_a_qualified_validator_is_unavailable_before_execution()
    {
        var type = new ValueTypeDescriptor(IntType.Alias, schema: JsonSerializer.SerializeToElement(new { type = "integer", minimum = 8 }));
        await using var fixture = await Fixture.CreateAsync(SetNode(Literal(9)), withDefaults: false, sourceType: type);
        ReplaceSourceValue(fixture, ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(7), ValueProtectionPolicy.InstanceInline));
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.ValidateSetupAsync());
        Assert.Contains("schema has no qualified", error.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Scope_declaration_protection_cannot_be_weakened_by_fixture_metadata()
    {
        var sensitive = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        await using var fixture = await Fixture.CreateAsync(SetNode(Literal(9)), withDefaults: false, sourcePolicy: sensitive);
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.ValidateSetupAsync());
        Assert.Contains("captured type or protection", error.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_external_variable_is_rejected_before_a_selected_composite_runs(bool expression)
    {
        var read = expression
            ? new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
                expression: new("Test", "value", parameters: new Dictionary<string, ExpressionParameterBinding>
                { ["value"] = new VariableExpressionParameterBinding("container", "source") }))
            : new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.VariableRead, variable: new("source", "container"));
        await using var fixture = await Fixture.CreateAsync(Sequence("selected",
            [WorkflowExecutionHarness.NewProbeNode("earlier-owned-child"), SetNode(read, "later-owned-child")]), withDefaults: false);
        var frame = fixture.SetupSource.Seed.Frames.Single();
        fixture.SetupSource.Seed = fixture.SetupSource.Seed with
        { Frames = [frame with { Values = new Dictionary<string, ValueEnvelope> { ["counter"] = Number(1) } }] };
        await Record.ExceptionAsync(() => fixture.StartAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.ValidateSetupAsync());
        Assert.Contains("explicit visible variable", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_external_request_is_rejected_before_a_selected_composite_runs(bool expression)
    {
        var read = expression
            ? new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
                expression: new("Test", "value", parameters: new Dictionary<string, ExpressionParameterBinding>
                { ["value"] = new WorkflowRequestExpressionParameterBinding("missing-request") }))
            : new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new("missing-request"));
        await using var fixture = await Fixture.CreateAsync(Sequence("selected",
            [WorkflowExecutionHarness.NewProbeNode("earlier-owned-child"), SetNode(read, "later-owned-child")]));
        await Record.ExceptionAsync(() => fixture.StartAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.ValidateSetupAsync());
        Assert.Contains("explicit request input", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-owner")]
    [InlineData("readonly-owner")]
    [InlineData("undeclared-member")]
    public async Task Unavailable_external_write_target_prevents_every_owned_child(string fault)
    {
        var later = SetNode(Literal(9), "later-owned-child", fault == "undeclared-member" ? "unknown" : "counter");
        await using var fixture = await Fixture.CreateAsync(Sequence("selected",
            [WorkflowExecutionHarness.NewProbeNode("earlier-owned-child"), later]));
        var seed = fixture.SetupSource.Seed;
        fixture.SetupSource.Seed = seed with
        { Frames = fault == "missing-owner" ? [] : [seed.Frames.Single() with { Writable = fault != "readonly-owner" }] };
        var prepared = await fixture.PrepareAsync();
        Assert.False(prepared.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(prepared.Findings).Kind);
        Assert.Contains("declared writable", prepared.Findings[0].Message, StringComparison.Ordinal);
        await Record.ExceptionAsync(() => fixture.StartAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Set_can_initialize_a_declared_absent_slot_without_an_invented_null()
    {
        await using var fixture = await Fixture.CreateAsync(SetNode(Literal(9)), withDefaults: false);
        var frame = fixture.SetupSource.Seed.Frames.Single();
        fixture.SetupSource.Seed = fixture.SetupSource.Seed with
        { Frames = [frame with { Values = new Dictionary<string, ValueEnvelope> { ["source"] = Number(7) } }] };
        var prepared = await fixture.PrepareAsync();
        Assert.True(prepared.IsAvailable);
        Assert.Equal(ValuePresence.Absent, prepared.Context!.Frames.Single().Frame.Values["counter"].Presence);
        await fixture.StartAsync();
        Assert.Equal(9, (await fixture.StateAsync()).TestContext!.Frames.Single().Frame.Values["counter"].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task Unqualified_external_capture_owner_prevents_every_owned_child()
    {
        var capture = new RuntimeOutputCapture("value", "captured-value", new(IntType.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance, DurableValueStorage.Inline, true,
            new Dictionary<string, string> { [RuntimeMetadataKeys.CaptureTargetScopeId] = "container", [RuntimeMetadataKeys.TargetVariableReferenceKey] = "counter" });
        var later = new ExecutableNode("later-owned-child", "later-owned-child", typeof(EchoActivity).FullName!, "1", "test.echo",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding> { ["value"] = Literal(23) },
            new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture> { ["value"] = capture });
        await using var fixture = await Fixture.CreateAsync(Sequence("selected", [WorkflowExecutionHarness.NewProbeNode("earlier-owned-child"), later]));
        fixture.Harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(EchoActivity), TypeAliasConvention.CanonicalAlias(typeof(EchoActivity)));
        fixture.Harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(EchoResult), TypeAliasConvention.CanonicalAlias(typeof(EchoResult)));
        var prepared = await fixture.PrepareAsync();
        Assert.False(prepared.IsAvailable);
        Assert.Contains("writable enclosing capture owner", Assert.Single(prepared.Findings).Message, StringComparison.Ordinal);
        await Record.ExceptionAsync(() => fixture.StartAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Ambient_Javascript_requires_missing_declared_context_before_every_owned_child()
    {
        var expression = new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: new("JavaScript", "getVariable('source') ?? 0"));
        await using var fixture = await Fixture.CreateAsync(Sequence("selected",
            [WorkflowExecutionHarness.NewProbeNode("earlier-owned-child"), SetNode(expression, "later-owned-child")]), withDefaults: false);
        var frame = fixture.SetupSource.Seed.Frames.Single();
        fixture.SetupSource.Seed = fixture.SetupSource.Seed with
        { Frames = [frame with { Values = new Dictionary<string, ValueEnvelope> { ["counter"] = Number(1) } }] };
        var prepared = await fixture.PrepareAsync();
        Assert.False(prepared.IsAvailable);
        Assert.Contains("explicit visible variable", Assert.Single(prepared.Findings).Message, StringComparison.Ordinal);
        await Record.ExceptionAsync(() => fixture.StartAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Preparation_is_repeatable_read_only_and_matches_the_queued_setup_owner()
    {
        await using var fixture = await Fixture.CreateAsync(SetNode(Literal(9)));
        var store = fixture.Harness.Services.GetRequiredService<IWorkflowTestScopeStore>();
        var scopeBefore = JsonSerializer.Serialize(await store.FindAsync(fixture.Entry.Scope.ScopeId));
        var first = await fixture.PrepareAsync();
        var second = await fixture.PrepareAsync();
        Assert.True(first.IsAvailable);
        Assert.Equal(JsonSerializer.Serialize(first.Context), JsonSerializer.Serialize(second.Context));
        Assert.Empty(first.Findings);
        Assert.Empty(await fixture.ActivitiesAsync());
        Assert.Null(await fixture.Harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(fixture.Harness.ExecutionId));
        Assert.Equal(scopeBefore, JsonSerializer.Serialize(await store.FindAsync(fixture.Entry.Scope.ScopeId)));
        await fixture.StartAsync();
        var state = await fixture.StateAsync();
        Assert.Equal(first.Context!.ContextId, state.TestContext!.ContextId);
        Assert.Equal(first.Context.Frames.Single().Frame.FrameId, state.TestContext.Frames.Single().Frame.FrameId);
        Assert.Equal(first.RootVariableFrame!.FrameId, state.RootVariableFrame!.FrameId);
        Assert.Equal(JsonSerializer.Serialize(first.RootVariableFrame.Values), JsonSerializer.Serialize(state.RootVariableFrame.Values));
        Assert.Equal(VariableFrameStatus.Closed, state.RootVariableFrame.Status);
    }

    [Fact]
    public async Task Compiler_style_atomic_result_lifecycle_can_capture_into_the_canonical_instance_variable()
    {
        var selected = ResultLifecycleCaptureNode();
        await using var fixture = await Fixture.CreateAsync(selected, workflowVariables: [CaptureDeclaration()]);
        RegisterCaptureActivity(fixture);

        var prepared = await fixture.PrepareAsync();

        Assert.True(prepared.IsAvailable, JsonSerializer.Serialize(prepared.Findings));
        Assert.Empty(prepared.Findings);
        Assert.Equal(ActivityValueLifecycle.Result, selected.ActivityContract!.Result.Policy.Lifecycle);
        Assert.Equal(DurableValueLifecycle.Instance, prepared.RootVariableFrame!.Values["captured"].Policy.Lifecycle);
        Assert.Empty(await fixture.ActivitiesAsync());
        Assert.Null(await fixture.Harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(fixture.Harness.ExecutionId));

        await fixture.StartAsync();

        var state = await fixture.StateAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(23, state.RootVariableFrame!.Values["captured"].InlineValue!.Value.GetInt32());
        Assert.Equal(DurableValueLifecycle.Instance, state.RootVariableFrame.Values["captured"].Policy.Lifecycle);
        Assert.Equal("selected", Assert.Single(await fixture.ActivitiesAsync()).Execution.ExecutableNodeId);
    }

    [Theory]
    [InlineData("encryption")]
    [InlineData("external")]
    [InlineData("nonpersistable")]
    [InlineData("incompatible-retention")]
    public async Task Complete_atomic_sibling_policy_refuses_capture_as_typed_unavailable_before_any_activity(string restriction)
    {
        var sibling = restriction switch
        {
            "encryption" => ActivityValuePolicy.Default with { RequiresEncryption = true },
            "external" => ActivityValuePolicy.Default with { Storage = ActivityValueStorage.External },
            "nonpersistable" => ActivityValuePolicy.Default with { IsPersistable = false },
            _ => ActivityValuePolicy.Default with { RetentionPolicy = "private-provider-retention-b" }
        };
        var atomic = ActivityValuePolicy.Default with
        {
            Lifecycle = ActivityValueLifecycle.Result,
            RetentionPolicy = restriction == "incompatible-retention" ? "private-provider-retention-a" : null
        };
        var selected = ResultLifecycleCaptureNode(sibling, atomic);
        await using var fixture = await Fixture.CreateAsync(selected, workflowVariables: [CaptureDeclaration()]);
        RegisterCaptureActivity(fixture);
        // Only the uncaptured sibling carries the added requirement. The selected projection remains ordinary.
        Assert.Equal(ActivityValuePolicy.Default, selected.ActivityContract!.Result.Projections["value"].Policy);

        var prepared = await fixture.PrepareAsync();

        Assert.False(prepared.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(prepared.Findings).Kind);
        if (restriction == "incompatible-retention")
            Assert.Equal("runtime.test-context.capture-policy-unavailable", prepared.Findings[0].Code);
        Assert.DoesNotContain("private-provider-retention", JsonSerializer.Serialize(prepared), StringComparison.Ordinal);
        Assert.Empty(await fixture.ActivitiesAsync());
        Assert.Null(await fixture.Harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(fixture.Harness.ExecutionId));
        await Record.ExceptionAsync(() => fixture.StartAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Preparation_distinguishes_expected_refusal_from_sanitized_infrastructure_failure_and_cancellation()
    {
        await using var fixture = await Fixture.CreateAsync(SetNode(Literal(9)));
        fixture.SetupSource.Seed = fixture.SetupSource.Seed with { ContextFingerprint = "changed" };
        var unavailable = await fixture.PrepareAsync();
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(unavailable.Findings).Kind);
        fixture.SetupSource.Failure = new InvalidOperationException("provider secret payload must stay private");
        var infrastructure = await fixture.PrepareAsync();
        Assert.Equal(RuntimeTestContextFindingKind.InfrastructureFailure, Assert.Single(infrastructure.Findings).Kind);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(infrastructure), StringComparison.Ordinal);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.PrepareAsync(cancellationToken: cancelled.Token));
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Conflicting_raw_and_trusted_root_seeds_refuse_instead_of_choosing_an_owner()
    {
        var declaration = new RuntimeVariableDeclaration("workflow-counter", "workflowCounter", IntType, ValueProtectionPolicy.InstanceInline);
        await using var fixture = await Fixture.CreateAsync(SetNode(Literal(9)), workflowVariables: [declaration]);
        fixture.SetupSource.Seed = fixture.SetupSource.Seed with
        { RootVariables = new Dictionary<string, ValueEnvelope> { [declaration.VariableKey] = Number(7) } };
        var conflict = await fixture.PrepareAsync(new Dictionary<string, JsonElement> { [declaration.Name] = JsonSerializer.SerializeToElement(9) });
        Assert.False(conflict.IsAvailable);
        Assert.Contains("seeds conflict", Assert.Single(conflict.Findings).Message, StringComparison.Ordinal);
        var matched = await fixture.PrepareAsync(new Dictionary<string, JsonElement> { [declaration.Name] = JsonSerializer.SerializeToElement(7) });
        Assert.True(matched.IsAvailable);
        Assert.Equal(7, matched.RootVariableFrame!.Values[declaration.VariableKey].InlineValue!.Value.GetInt32());
        Assert.Empty(await fixture.ActivitiesAsync());
        await Record.ExceptionAsync(() => fixture.StartAsync(new Dictionary<string, object?> { [declaration.Name] = 9 }));
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Request_payload_preparation_uses_the_same_pinned_type_validation_as_start(bool expression)
    {
        var binding = expression
            ? new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
                expression: new("Test", "value", parameters: new Dictionary<string, ExpressionParameterBinding>
                { ["value"] = new WorkflowRequestExpressionParameterBinding("amount") }))
            : new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequest,
                workflowRequest: new("amount"));
        var contract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, [new("amount", new(IntType.Alias), true)]);
        await using var fixture = await Fixture.CreateAsync(Sequence("selected",
            [WorkflowExecutionHarness.NewProbeNode("earlier-owned-child"), SetNode(binding, "later-owned-child")]), inputContract: contract);
        var invalid = await fixture.PrepareAsync(workflowInputs: new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement("not-an-integer") });
        Assert.False(invalid.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(invalid.Findings).Kind);
        Assert.Contains("payload is incompatible", invalid.Findings[0].Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.ActivitiesAsync());
        var valid = await fixture.PrepareAsync(workflowInputs: new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(31) });
        Assert.True(valid.IsAvailable);
        await fixture.StartAsync(inputs: new Dictionary<string, object?> { ["amount"] = 31 });
        Assert.Equal(31, (await fixture.StateAsync()).TestContext!.Frames.Single().Frame.Values["counter"].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task Duplicate_preparation_replacement_is_rejected_during_composition()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddScoped<IRuntimeTestContextPreparer, RuntimeTestContextPreparer>();
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var task in provider.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
        });
        Assert.Contains("exactly one test context preparer", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_selected_leaf_consumes_an_explicit_outside_result_without_executing_its_producer(bool expression)
    {
        var binding = expression
            ? new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
                expression: new("Test", "value", parameters: new Dictionary<string, ExpressionParameterBinding>
                { ["value"] = new ActivityResultExpressionParameterBinding("before", "$result", true) }))
            : new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
                activityResult: new("before", "$result", "container", true));
        await using var fixture = await Fixture.CreateAsync(SetNode(binding), outsideProducer: IntProducer("before"));
        fixture.SetupSource.Seed = fixture.SetupSource.Seed with { Results = [new("before", "container", Number(11))] };
        await fixture.StartAsync();
        Assert.Equal(11, (await fixture.StateAsync()).TestContext!.Frames.Single().Frame.Values["counter"].InlineValue!.Value.GetInt32());
        Assert.Equal("selected", Assert.Single(await fixture.ActivitiesAsync()).Execution.ExecutableNodeId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_selected_composite_reads_its_actual_owned_child_result(bool expression)
    {
        var binding = expression
            ? new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
                expression: new("Test", "value", parameters: new Dictionary<string, ExpressionParameterBinding>
                { ["value"] = new ActivityResultExpressionParameterBinding("owned-producer", "$result", false) }))
            : new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
                activityResult: new("owned-producer", "$result", "selected"));
        await using var fixture = await Fixture.CreateAsync(Sequence("selected", [IntProducer("owned-producer"), SetNode(binding, "owned-consumer")]));
        await fixture.StartAsync();
        var state = await fixture.StateAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(17, state.TestContext!.Frames.Single().Frame.Values["counter"].InlineValue!.Value.GetInt32());
        Assert.Equal(3, (await fixture.ActivitiesAsync()).Count);
        Assert.Empty(state.TestContext.Results);
    }

    [Fact]
    public void Prior_result_fixture_is_explicit_null_instead_of_optional_absence()
    {
        var fixture = new RuntimeTestResultFixture("outside", "container", ValueEnvelope.Null(IntType, ValueProtectionPolicy.InstanceInline));
        var context = Context([fixture]);
        var binding = new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult, activityResult: new("outside", "$result", "container", true));
        Assert.Equal(ValuePresence.ExplicitNull, new RuntimeInputBindingResolver().Resolve(binding, context).Envelope!.Presence);
        var missing = Context([]);
        Assert.Throws<InvalidOperationException>(() => new RuntimeInputBindingResolver().Resolve(binding, missing));
    }

    [Fact]
    public void A_fixture_cannot_shadow_owned_target_or_child_results()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeTestContextState("context", "fingerprint", [],
            [new("child", "scope", Number(3))], ["selected", "child"]));
    }

    [Fact]
    public void Supplied_frame_writes_preserve_type_policy_revision_and_close_boundaries()
    {
        var root = new VariableFrameState("test-frame", "scope", "context", null, VariableFrameKind.Root,
            new Dictionary<string, ValueEnvelope> { ["counter"] = Number(1) });
        var state = new RuntimeTestContextState("context", "fingerprint",
            [new(root, true, true, new Dictionary<string, ValueEnvelope>(), new Dictionary<string, string>())], [], ["selected"]);
        var changed = state.Set("test-frame", "counter", Number(7), 0);
        Assert.Equal(1, state.Frames[0].Frame.Values["counter"].InlineValue!.Value.GetInt32());
        Assert.Equal(7, changed.Frames[0].Frame.Values["counter"].InlineValue!.Value.GetInt32());
        Assert.Throws<VariableFrameRevisionException>(() => changed.Set("test-frame", "counter", Number(9), 0));
        Assert.Throws<InvalidOperationException>(() => changed.Close().Set("test-frame", "counter", Number(9), 1));
        Assert.Throws<InvalidOperationException>(() => changed.Set("test-frame", "counter",
            ValueEnvelope.Inline(new("System.String"), JsonSerializer.SerializeToElement("wrong"), ValueProtectionPolicy.InstanceInline), 1));
        Assert.Throws<InvalidOperationException>(() => changed.Set("test-frame", "counter", ValueEnvelope.Absent(IntType, ValueProtectionPolicy.InstanceInline), 1));
        var roundTrip = JsonSerializer.Deserialize<RuntimeTestContextState>(JsonSerializer.Serialize(changed))!;
        Assert.Equal(changed.ContextId, roundTrip.ContextId);
        Assert.Equal(7, roundTrip.Frames[0].Frame.Values["counter"].InlineValue!.Value.GetInt32());
    }

    private static RuntimeInputBindingResolutionContext Context(IReadOnlyList<RuntimeTestResultFixture> fixtures)
    {
        // A typed producer contract is required even when its ordinary invocation was deliberately not run.
        var producer = WorkflowExecutionHarness.NewProbeNode("outside");
        var contract = producer.ActivityContract!;
        var typed = new Elsa.Activities.Runtime.Core.Models.ActivityContract(contract.ActivityTypeKey, contract.ContractVersion,
            contract.DescriptorKind, contract.DescriptorPayload, contract.Inputs.Values,
            new Elsa.Activities.Runtime.Core.Models.ActivityResultContract(IntType, false,
                Elsa.Activities.Runtime.Core.Models.ActivityValuePolicy.Default, []), contract.Outcomes, contract.Activation, contract.SideEffectProfile);
        producer = new("outside", "outside", producer.ActivityType, producer.ActivityTypeVersion,
            producer.DescriptorType, producer.DescriptorPayload, producer.InputBindings, producer.Metadata, activityContract: typed);
        var executable = WorkflowExecutionHarness.NewExecutable(producer);
        return new("execution", "selected", null, null, executable, null, null, null, null,
            new("context", "fingerprint", [], fixtures, ["selected"]));
    }

    private static RuntimeInputBinding Literal(int value) => new("value", IntType, ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Literal, literal: Number(value));
    private static ValueEnvelope Number(int value) => ValueEnvelope.Inline(IntType, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);
    private static RuntimeVariableDeclaration CaptureDeclaration() => new("captured", "captured", IntType,
        ValueProtectionPolicy.InstanceInline, new RuntimeInputBinding("captured", IntType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, literal: Number(1)));

    private static ExecutableNode ResultLifecycleCaptureNode(ActivityValuePolicy? siblingPolicy = null, ActivityValuePolicy? atomicPolicy = null)
    {
        var descriptor = new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, "1",
            JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(EchoActivity)))));
        var projections = new List<ActivityResultProjectionContract>
        { new("value", "value", IntType, true, ActivityValuePolicy.Default) };
        if (siblingPolicy is not null)
            projections.Add(new("sibling", "value", IntType, true, siblingPolicy));
        // ExecutableNodeCompiler pins atomic results with Result lifetime. The destination variable remains Instance.
        var contract = new ActivityContract(typeof(EchoActivity).FullName!, "1", typeof(ClrActivityDescriptor).FullName!, descriptor.Payload,
            [new("value", "Value", IntType, false, false, false, null, ActivityValuePolicy.Default)],
            new(new(TypeAliasConvention.CanonicalAlias(typeof(EchoResult))), true,
                atomicPolicy ?? (ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result }), projections),
            ["Done"], new(typeof(ClrActivityDescriptor).FullName!, TypeAliasConvention.CanonicalAlias(typeof(EchoActivity))));
        var capture = new RuntimeOutputCapture("value", RuntimeWorkflowStateSeed.VariableValueIdPrefix + "captured",
            new(IntType.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance, DurableValueStorage.Custom, true,
            new Dictionary<string, string> { [RuntimeMetadataKeys.TargetVariableReferenceKey] = "captured" });
        return new("selected", "selected", typeof(EchoActivity).FullName!, "1", descriptor,
            new Dictionary<string, RuntimeInputBinding> { ["value"] = Literal(23) },
            new Dictionary<string, RuntimeOutputCapture> { ["value"] = capture }, new Dictionary<string, string>(), activityContract: contract);
    }

    private static void RegisterCaptureActivity(Fixture fixture)
    {
        var types = fixture.Harness.Services.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(EchoActivity), TypeAliasConvention.CanonicalAlias(typeof(EchoActivity)));
        types.RegisterType(typeof(EchoResult), TypeAliasConvention.CanonicalAlias(typeof(EchoResult)));
    }

    private static ExecutableNode IntProducer(string id) => new(id, id, typeof(IntProducerActivity).FullName!, "1", "test.int-producer",
        JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());

    private static void ReplaceSourceValue(Fixture fixture, ValueEnvelope value)
    {
        var frame = fixture.SetupSource.Seed.Frames.Single();
        fixture.SetupSource.Seed = fixture.SetupSource.Seed with
        { Frames = [frame with { Values = new Dictionary<string, ValueEnvelope> { ["counter"] = Number(1), ["source"] = value } }] };
    }
    private static ExecutableNode SetNode(RuntimeInputBinding binding, string id = "selected", string variableKey = "counter") => new(id, id, "elsa.intrinsic.set", "1",
        "intrinsic", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding> { ["value"] = binding },
        new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new(variableKey, "container"));

    private static ExecutableNode Sequence(string id, IReadOnlyCollection<ExecutableNode> children) =>
        new(id, id, typeof(SequenceActivity).FullName!, "1", "test.sequence", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new(SequenceActivity.ActivitiesSlotName, children)], structure: new(SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(new { activities = children.Select(x => x.ExecutableNodeId) })));

    private sealed class Setup : IWorkflowTestEntryContextResolver, IRuntimeTestContextSeedSource
    {
        public WorkflowTestEntry Entry { get; set; } = null!;
        public RuntimeTestContextSeed Seed { get; set; } = null!;
        public Exception? Failure { get; set; }
        public ValueTask<WorkflowTestEntry?> FindAsync(string contextId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowTestEntry?>(Entry.ContextId == contextId ? Entry : null);
        public ValueTask<RuntimeTestContextSeed?> ResolveAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
            string workflowExecutionId, CancellationToken cancellationToken = default) => Failure is { } failure
                ? ValueTask.FromException<RuntimeTestContextSeed?>(failure) : ValueTask.FromResult<RuntimeTestContextSeed?>(Seed);
    }

    private sealed class Echo : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => ValueTask.FromResult(request.ParameterValues["value"]);
    }

    public sealed class EchoActivity : Elsa.Activities.Runtime.Core.Models.Activity<EchoResult>
    {
        [ActivityInput(Key = "value")]
        public int Value { get; set; }
        protected override ValueTask<Elsa.Activities.Runtime.Core.Models.ActivityTransition<EchoResult>> ExecuteAsync(
            Elsa.Activities.Runtime.Core.Models.ActivityExecutionContext context) =>
            ValueTask.FromResult(Elsa.Activities.Runtime.Core.Models.ActivityTransition.Complete(new EchoResult(Value)));
    }

    public sealed record EchoResult([property: Output(Key = "value", Path = "value")] int Value);

    public sealed class IntProducerActivity : Elsa.Activities.Runtime.Core.Models.Activity<int>
    {
        protected override ValueTask<Elsa.Activities.Runtime.Core.Models.ActivityTransition<int>> ExecuteAsync(
            Elsa.Activities.Runtime.Core.Models.ActivityExecutionContext context) =>
            ValueTask.FromResult(Elsa.Activities.Runtime.Core.Models.ActivityTransition.Complete(17));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required WorkflowExecutionHarness Harness { get; init; }
        public required WorkflowTestEntry Entry { get; init; }
        public required WorkflowExecutableSourceReference Reference { get; init; }
        public required Setup SetupSource { get; init; }
        public ValueTask DisposeAsync() => Harness.DisposeAsync();
        public async Task<WorkflowExecutionState> StateAsync() => (await Harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(Harness.ExecutionId))!;
        public async Task<IReadOnlyCollection<ActivityExecutionState>> ActivitiesAsync() =>
            await Harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(Harness.ExecutionId);

        public async Task ValidateSetupAsync()
        {
            await RuntimeTestContextOwner.CreateAsync(Entry,
                (await Harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(Entry.Executable.ArtifactId))!,
                Harness.ExecutionId, new VariableFrameFactory().CreateRoot(Harness.ExecutionId, "workflow", new Dictionary<string, ValueEnvelope>()),
                [SetupSource], Harness.Services.GetRequiredService<IInlineEventPublisher>(), new Dictionary<string, JsonElement>(),
                new Dictionary<string, JsonElement>(),
                Harness.Services.GetRequiredService<IWorkflowExecutableInputValidator>(), Harness.Services.GetRequiredService<IWellKnownTypeRegistry>(), CancellationToken.None);
        }

        public async Task<RuntimeTestContextPreparation> PrepareAsync(IReadOnlyDictionary<string, JsonElement>? rootSeeds = null,
            CancellationToken cancellationToken = default, IReadOnlyDictionary<string, JsonElement>? workflowInputs = null) => await Harness.Services.GetRequiredService<IRuntimeTestContextPreparer>().PrepareAsync(
                Entry, (await Harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(Entry.Executable.ArtifactId))!,
                Harness.ExecutionId, workflowInputs ?? new Dictionary<string, JsonElement>(), rootSeeds ?? new Dictionary<string, JsonElement>(), cancellationToken);

        public static async Task<Fixture> CreateAsync(ExecutableNode selected, bool closed = false, bool iteration = false,
            bool withDefaults = true, ValueProtectionPolicy? sourcePolicy = null, ValueTypeDescriptor? sourceType = null,
            IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables = null, ExecutableNode? outsideProducer = null,
            WorkflowExecutableInputContract? inputContract = null)
        {
            var setup = new Setup();
            var harness = WorkflowExecutionHarness.Create().WithFeature(s => new ActivitiesSequenceFeature().ConfigureServices(s))
                .ConfigureServices(s =>
                {
                    s.AddSingleton<IWorkflowTestEntryContextResolver>(setup);
                    s.AddSingleton<IRuntimeTestContextSeedSource>(setup);
                    s.AddSingleton<IRuntimeTestEffectPolicySource, TestOnlyPureEffectPolicy>();
                    s.AddSingleton<IPortableExpressionEvaluator, Echo>();
                }).Build(WorkflowExecutionHarness.Identity,
                    KeyedWorkflowStartIdentity.For("context-key", WorkflowExecutionHarness.Identity.ArtifactId).WorkflowExecutionId,
                    Enumerable.Range(1, 30).Select(x => $"activity-{x}"));
            harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(int), IntType.Alias);
            harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(IntProducerActivity), TypeAliasConvention.CanonicalAlias(typeof(IntProducerActivity)));
            var children = new[] { outsideProducer ?? WorkflowExecutionHarness.NewProbeNode("before"), selected, WorkflowExecutionHarness.NewProbeNode("after") };
            var declarations = new[] { "counter", "source" }.Select(key =>
            {
                var type = key == "source" ? sourceType ?? IntType : IntType;
                var policy = key == "source" ? sourcePolicy ?? ValueProtectionPolicy.InstanceInline : ValueProtectionPolicy.InstanceInline;
                return new RuntimeVariableDeclaration(key, key, type, policy, withDefaults
                    ? new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal,
                        literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(key == "source" ? 7 : 1), policy)) : null);
            }).ToArray();
            var root = new ExecutableNode("container", "container", typeof(SequenceActivity).FullName!, "1", "test.sequence",
                JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
                childSlots: [new(SequenceActivity.ActivitiesSlotName, children)], structure: new(SequenceActivity.StructureKind,
                    SequenceActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(new { activities = children.Select(x => x.ExecutableNodeId), variables = declarations },
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            var candidate = WorkflowExecutionHarness.NewExecutable(root);
            if (workflowVariables is not null || inputContract is not null)
                candidate = new(candidate.Identity, root, candidate.ResumeTargets, candidate.CreatedAt, candidate.CompatibilityMetadata,
                    inputContract ?? candidate.InputContract, candidate.Dependencies, candidate.RuntimeRequirements, candidate.StorageDriverRequirements,
                    candidate.IncidentStrategy, candidate.PersistenceMode, workflowVariables);
            if (selected.ActivityType == typeof(WorkflowTestEntryTests.WaitingActivity).FullName)
                candidate = new(candidate.Identity, root, new Dictionary<string, WorkflowExecutableResumeTarget>
                {
                    ["selected:wait"] = new("selected:wait", "selected", "ResumeAsync", new Dictionary<string, string>(), "wait")
                }, candidate.CreatedAt, candidate.CompatibilityMetadata, candidate.IncidentStrategy);
            var published = await harness.PublishAsync(candidate, "context-published");
            var now = TimeProvider.System.GetUtcNow();
            var scope = new WorkflowTestScope("context-scope", now.AddMinutes(10), null, new(WorkflowExecutionPartition.DefaultValue));
            var reference = published with { SourceReferenceId = "context-source", Scope = WorkflowExecutableReferenceScope.TestRun,
                ExpiresAt = scope.ExpiresAt, PublishedAt = null, ActivationId = null, SlotId = null };
            await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
            await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(scope, now);
            var entry = new WorkflowTestEntry("context", "fingerprint", candidate.Identity, "selected", scope, new("tests", "tests"));
            setup.Entry = entry;
            setup.Seed = new(entry.ContextId, entry.ContextFingerprint,
                [new("container", closed ? VariableFrameKind.Root : iteration ? VariableFrameKind.Iteration : VariableFrameKind.Container,
                    new Dictionary<string, ValueEnvelope> { ["counter"] = Number(1), ["source"] = Number(7) }, closed,
                    closed ? new Dictionary<string, ValueEnvelope> { ["request-value"] = Number(11) } : null)], []);
            return new() { Harness = harness, Entry = entry, Reference = reference, SetupSource = setup };
        }

        public async Task StartAsync(IReadOnlyDictionary<string, object?>? variables = null, IReadOnlyDictionary<string, object?>? inputs = null)
        {
            var request = new WorkflowExecutionStartDispatchRequest(Entry.Executable.ArtifactId, "tests", Harness.ExecutionId,
                "context-key", null, variables, inputs, null, null, WorkflowRunKind.TestRun, new(Reference.SourceReferenceId),
                WorkflowExecutableProvenanceRequirement.RequireLiveReference, null, null, null, Entry.Scope.Partition,
                Entry.Authority, null, 0, Entry.Scope) { TestEntry = Entry };
            await Harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(request, WorkflowExecutableReferenceScope.TestRun);
        }
    }
}
