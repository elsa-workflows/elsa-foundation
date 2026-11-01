using System.Text.Json;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Etl.Workflows.Runtime.Services;
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

    private static ExecutableNode Node(string id) => new(id, "authored-etl", RunEtl.ActivityType, "1",
        "test.clr-activity", JsonSerializer.SerializeToElement(new { captured = "captured-connector-value" }),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
}
