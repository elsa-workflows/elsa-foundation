using System.Text.Json;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Etl.Tests.Support;
using Elsa.Etl.Core;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class RunEtlStateViewProjectorTests
{
    [Fact]
    public void The_runtime_feature_registers_one_identity_projector()
    {
        using var provider = Provider();
        var projector = Assert.Single(provider.GetServices<IActivityStateViewProjector>());
        Assert.Equal(typeof(RunEtlState).FullName, projector.StateTypeAlias);
        Assert.Equal("etl.run-reference", projector.ViewKind);
        Assert.Equal(RunEtl.ActivityType, projector.ActivityTypeWithoutState);
        Assert.Equal(1, projector.ViewVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_view_contains_only_the_exact_run_and_result_address_without_captured_values(bool sensitive)
    {
        using var provider = Provider();
        var projector = Assert.Single(provider.GetServices<IActivityStateViewProjector>());
        var context = Context() with { CanInspectSensitiveValues = sensitive };
        var original = JsonSerializer.Serialize(context.ActivityExecution);
        var invocation = EtlNativeBoundary.Invocation(context.ActivityExecution);

        var view = await projector.ProjectAsync(context);

        Assert.Equal(["runId", "resultAddress"], view.EnumerateObject().Select(item => item.Name));
        Assert.Equal(invocation.RunId, view.GetProperty("runId").GetString());
        var address = view.GetProperty("resultAddress");
        Assert.Equal(invocation.ResultAddress.WorkflowExecutionId, address.GetProperty("workflowExecutionId").GetString());
        Assert.Equal(invocation.ResultAddress.ActivityExecutionId, address.GetProperty("activityExecutionId").GetString());
        Assert.Equal(invocation.ResultAddress.NodeId, address.GetProperty("nodeId").GetString());
        Assert.Equal(invocation.ResultAddress.RegistrationId, address.GetProperty("registrationId").GetString());
        Assert.Equal(invocation.ResultAddress.ResumeTargetId, address.GetProperty("resumeTargetId").GetString());
        Assert.DoesNotContain("captured-connector-value", view.GetRawText());
        Assert.DoesNotContain("pipeline", view.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, JsonSerializer.Serialize(context.ActivityExecution));
    }

    [Fact]
    public async Task A_consumed_wait_keeps_its_actual_registration_after_payload_retirement()
    {
        using var provider = Provider();
        var projector = Assert.Single(provider.GetServices<IActivityStateViewProjector>());
        var context = Context();
        var invocation = EtlNativeBoundary.Invocation(context.ActivityExecution);
        var type = EtlNativeBoundary.CompletionType;
        context = context with { ActivityExecution = context.ActivityExecution with
        {
            Status = ActivityExecutionStatus.Faulted,
            TriggerRegistrations = [],
            TriggerDeliveries = [new("delivery", invocation.ResultAddress.RegistrationId, type,
                ValueEnvelope.Absent(type, ValueProtectionPolicy.InstanceInline),
                EtlNativeResultAddress.CompletionProviderId, EtlNativeTestFixture.Now, "stable-result", ActivityTriggerDeliveryStatus.Consumed)]
        } };

        var view = await projector.ProjectAsync(context);

        Assert.Equal(invocation.RunId, view.GetProperty("runId").GetString());
        Assert.Equal(invocation.ResultAddress.RegistrationId, view.GetProperty("resultAddress").GetProperty("registrationId").GetString());
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("tenant")]
    [InlineData("partition")]
    [InlineData("pin")]
    [InlineData("node")]
    [InlineData("state")]
    [InlineData("registration")]
    public async Task A_foreign_or_unsupported_identity_cannot_be_projected_as_an_ETL_link(string field)
    {
        using var provider = Provider();
        var projector = Assert.Single(provider.GetServices<IActivityStateViewProjector>());
        var context = Context();
        context = field switch
        {
            "workflow" => context with { WorkflowExecution = context.WorkflowExecution with { WorkflowExecutionId = "other-workflow" } },
            "tenant" => context with { WorkflowExecution = context.WorkflowExecution with { TenantId = "other-tenant" } },
            "partition" => context with { WorkflowExecution = context.WorkflowExecution with { Partition = new("other-partition") } },
            "pin" => context with { WorkflowExecution = context.WorkflowExecution with { PinnedExecutable = new("other", "definition", "version", "1", "other-hash") } },
            "node" => context with { ExecutableNode = Node("other-node") },
            "state" => context with { ActivityExecution = context.ActivityExecution with { PrivateState = new(context.ActivityExecution.InvocationId, 99, context.ActivityExecution.PrivateState!.Value, context.ActivityExecution.PrivateState.ProducedByAttemptId, context.ActivityExecution.PrivateState.CommittedAt) } },
            _ => context with { ActivityExecution = context.ActivityExecution with { TriggerRegistrations = [] } }
        };

        await Assert.ThrowsAsync<InvalidDataException>(async () => await projector.ProjectAsync(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cleared_terminal_projects_only_its_exact_retained_reference(bool payloadRetired)
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        var context = await ClearedContext(fixture, payloadRetired);
        var before = JsonSerializer.Serialize(context.ActivityExecution);
        var projector = new RunEtlStateViewProjector(fixture.Store);

        var view = await projector.ProjectWithoutStateAsync(context);

        Assert.True(view.HasValue);
        Assert.Equal(["runId", "resultAddress"], view.Value.EnumerateObject().Select(item => item.Name));
        Assert.Equal(fixture.Invocation.RunId, view.Value.GetProperty("runId").GetString());
        Assert.Equal(fixture.Invocation.ResultAddress.RegistrationId, view.Value.GetProperty("resultAddress").GetProperty("registrationId").GetString());
        Assert.DoesNotContain("pipeline", view.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, JsonSerializer.Serialize(context.ActivityExecution));
        Assert.Null(context.ActivityExecution.PrivateState);
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("tenant")]
    [InlineData("partition")]
    [InlineData("worker-pin")]
    [InlineData("node")]
    [InlineData("pipeline-pin")]
    [InlineData("registration")]
    [InlineData("unconsumed")]
    [InlineData("outcome")]
    [InlineData("live")]
    public async Task A_cleared_reference_refuses_foreign_or_unproven_context(string change)
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        var context = await ClearedContext(fixture, true);
        context = change switch
        {
            "workflow" => context with { WorkflowExecution = context.WorkflowExecution with { WorkflowExecutionId = "other-workflow" } },
            "tenant" => context with { WorkflowExecution = context.WorkflowExecution with { TenantId = "other-tenant" } },
            "partition" => context with { WorkflowExecution = context.WorkflowExecution with { Partition = new("other-partition") } },
            "worker-pin" => context with { WorkflowExecution = context.WorkflowExecution with { PinnedExecutable = new("other", "definition", "version", "1", "other-hash") } },
            "node" => context with { ExecutableNode = Node("another") },
            "pipeline-pin" => context with { ExecutableNode = Node(context.ExecutableNode.ExecutableNodeId) },
            "registration" => context with { ActivityExecution = context.ActivityExecution with { TriggerDeliveries = [] } },
            "unconsumed" => context with { ActivityExecution = Unconsumed(context.ActivityExecution) },
            "outcome" => context with { ActivityExecution = context.ActivityExecution with { Status = ActivityExecutionStatus.Completed } },
            _ => context with { ActivityExecution = context.ActivityExecution with { Status = ActivityExecutionStatus.Running } }
        };
        Assert.Null(await new RunEtlStateViewProjector(fixture.Store).ProjectWithoutStateAsync(context));
    }

    [Fact]
    public async Task Absent_storage_or_missing_retained_run_does_not_invent_a_reference()
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        var context = await ClearedContext(fixture, true);
        Assert.Null(await new RunEtlStateViewProjector().ProjectWithoutStateAsync(context));
        Assert.Null(await new RunEtlStateViewProjector(new RetainedStore(fixture.Store, null)).ProjectWithoutStateAsync(context));
        var run = (await fixture.Store.FindRunAsync(fixture.Scope, fixture.Invocation.RunId))!;
        var changed = run with { Invocation = run.Invocation with { ResultAddress = run.Invocation.ResultAddress with { ActivityExecutionId = "foreign-activity" } } };
        Assert.Null(await new RunEtlStateViewProjector(new RetainedStore(fixture.Store, changed)).ProjectWithoutStateAsync(context));
    }

    private static ActivityExecutionState Unconsumed(ActivityExecutionState activity)
    {
        var item = activity.TriggerDeliveries!.Single();
        return activity with { TriggerDeliveries = [new(item.DeliveryId, item.RegistrationId, item.PayloadType,
            ValueEnvelope.Inline(item.PayloadType, JsonSerializer.SerializeToElement(new { }), ValueProtectionPolicy.InstanceInline),
            item.ProviderId, item.ReceivedAt, item.DeduplicationKey, ActivityTriggerDeliveryStatus.Received)] };
    }

    private sealed class RetainedStore(IEtlRuntimeStore inner, EtlRunView? value) : ForwardingEtlRuntimeStore(inner)
    {
        public override ValueTask<EtlRunView?> FindRunAsync(EtlScope scope, string runId, CancellationToken cancellationToken = default) => ValueTask.FromResult(value);
    }

    private static async Task<ActivityStateViewContext> ClearedContext(EtlNativeTestFixture fixture, bool payloadRetired)
    {
        var invocation = fixture.Invocation;
        var activity = (await fixture.Activities.FindAsync(invocation.ResultAddress.WorkflowExecutionId, invocation.ResultAddress.ActivityExecutionId))!;
        var workflow = (await fixture.Workflows.FindAsync(invocation.ResultAddress.WorkflowExecutionId))!;
        var run = (await fixture.Store.FindRunAsync(fixture.Scope, invocation.RunId))!;
        var completion = new RunEtlCompletion(EtlNativeBoundary.FormatVersion,
            EtlRuntimeJson.WriteElement(invocation), EtlRuntimeJson.WriteElement(run.TerminalResult));
        var type = EtlNativeBoundary.CompletionType;
        activity = activity with { PrivateState = null, Status = ActivityExecutionStatus.Cancelled, TriggerRegistrations = [],
            TriggerDeliveries = [new("delivery", invocation.ResultAddress.RegistrationId, type,
                payloadRetired ? ValueEnvelope.Absent(type, ValueProtectionPolicy.InstanceInline) : ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(completion), ValueProtectionPolicy.InstanceInline),
                EtlNativeResultAddress.CompletionProviderId, EtlNativeTestFixture.Now, "stable-result", ActivityTriggerDeliveryStatus.Consumed)] };
        var node = Node(invocation.ResultAddress.NodeId, invocation.Artifact);
        return new(workflow, activity, node, [], false);
    }

    [Fact]
    public async Task The_composed_singleton_resolves_a_scoped_store_per_read_under_scope_validation()
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        var context = await ClearedContext(fixture, payloadRetired: true);
        var services = new ServiceCollection();
        new EtlWorkflowsRuntimeFeature().ConfigureServices(services);
        var resolutions = 0;
        services.AddScoped<IEtlRuntimeStore>(_ => { resolutions++; return fixture.Store; });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        // The registry holds projectors for the shell's lifetime, so the projector must not capture a scoped store.
        var projector = Assert.Single(provider.GetServices<IActivityStateViewProjector>().OfType<RunEtlStateViewProjector>());

        Assert.Equal(fixture.Invocation.RunId, (await projector.ProjectWithoutStateAsync(context))!.Value.GetProperty("runId").GetString());
        Assert.NotNull(await projector.ProjectWithoutStateAsync(context));
        Assert.Equal(2, resolutions);
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        new EtlWorkflowsRuntimeFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static ActivityStateViewContext Context()
    {
        var activity = EtlNativeCheckpointTests.Activity(resumeTarget: "actual-opaque-target");
        var request = EtlNativeBoundary.ReadState(activity);
        var workflow = new WorkflowExecutionState(request.WorkflowExecutionId,
            new(request.ExecutableId, "definition", "version", "1", request.ExecutableHash),
            WorkflowExecutionStatus.Running, null, EtlNativeTestFixture.Now, EtlNativeTestFixture.Now,
            null, null, null, null, request.Scope.TenantId, new Dictionary<string, string>())
        { Partition = new(request.Scope.PersistencePartition) };
        return new(workflow, activity, Node(request.NodeId), [], false);
    }

    private static ExecutableNode Node(string id, EtlArtifact? artifact = null) => new(id, "authored-etl", RunEtl.ActivityType, "1",
        "test.clr-activity", JsonSerializer.SerializeToElement(new { captured = "captured-connector-value" }),
        new Dictionary<string, RuntimeInputBinding>(), artifact is null ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [EtlNativeBoundary.PinKey] = JsonSerializer.Serialize(artifact, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
}
