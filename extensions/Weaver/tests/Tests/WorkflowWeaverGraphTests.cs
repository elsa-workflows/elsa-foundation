using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Services;
using Elsa.Weaver.Workflows.Tools;

namespace Elsa.Weaver.Tests;

public sealed class WorkflowWeaverGraphTests
{
    [Fact]
    public async Task Layout_preparation_binds_exact_target_and_requires_review_even_when_engine_allows_auto()
    {
        var authority = new Authority(true);
        var tool = new WorkflowSetDesignerPositionTool(authority);
        var result = await tool.PrepareAsync(Invocation());
        Assert.True(result.Succeeded);
        var preparation = result.Value!;
        Assert.Equal(WorkflowWeaverTests.Snapshot().Target, preparation.Target);
        Assert.Equal(WorkflowWeaverTests.Snapshot().Client, preparation.Client);
        Assert.True(preparation.RequiresApproval);
        Assert.Equal(WeaverRisk.ReviewRequired, preparation.Risk);
        Assert.Equal(WeaverExecutionLocation.ClientWorkingState, tool.Descriptor.ExecutionLocation);
        Assert.Equal("workflows/design:manage", Assert.Single(tool.Descriptor.RequiredPermissions));
        Assert.Equal("actor-1", authority.Actor);
        Assert.Equal("tenant-1", authority.Tenant);
        Assert.Equal(preparation.Target, authority.Target);
        Assert.Equal("workflows.layout-change", preparation.Preview.TypeId);
    }

    [Fact]
    public async Task Layout_preparation_derives_the_owning_snapshot_binding_when_global_binding_is_absent()
    {
        var result = await new WorkflowSetDesignerPositionTool(new Authority(true)).PrepareAsync(Invocation() with { Client = null });
        Assert.True(result.Succeeded);
        Assert.Equal(WorkflowWeaverTests.Snapshot().Client, result.Value?.Client);
    }

    [Fact]
    public async Task Current_backend_permission_denial_prevents_review_preparation()
    {
        var result = await new WorkflowSetDesignerPositionTool(new Authority(false)).PrepareAsync(Invocation());
        Assert.False(result.Succeeded);
        Assert.Equal("weaver.workflow.permission_denied", result.Error?.Code);
    }

    [Theory]
    [InlineData("unknown-node", 10, 20)]
    [InlineData("node-1", double.PositiveInfinity, 20)]
    [InlineData("node-1", 10, double.NaN)]
    public async Task Invalid_activity_or_coordinates_do_not_prepare_a_change(string activityId, double x, double y)
    {
        var invocation = Invocation() with { Arguments = new Dictionary<string, object?> { ["activityId"] = activityId, ["x"] = x, ["y"] = y } };
        var result = await new WorkflowSetDesignerPositionTool(new Authority(true)).PrepareAsync(invocation);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Wrong_generation_and_forged_revision_do_not_prepare_a_change()
    {
        var tool = new WorkflowSetDesignerPositionTool(new Authority(true));
        Assert.False((await tool.PrepareAsync(Invocation() with { Client = new("client-1", "another-generation") })).Succeeded);
        var context = WorkflowWeaverTests.Snapshot() with { Target = WorkflowWeaverTests.Snapshot().Target! with { Revision = "other-revision" } };
        Assert.False((await tool.PrepareAsync(Invocation() with { Context = [context] })).Succeeded);
    }

    [Fact]
    public async Task A_browser_tool_refuses_server_execution()
    {
        var tool = new WorkflowSetDesignerPositionTool(new Authority(true));
        var result = await tool.ExecuteAsync(await Prepared(tool));
        Assert.False(result.Succeeded);
        Assert.Equal("weaver.workflow.client_execution_required", result.Error?.Code);
    }

    [Fact]
    public async Task Observed_exact_layout_acknowledgement_returns_a_redacted_local_receipt()
    {
        var tool = new WorkflowSetDesignerPositionTool(new Authority(true));
        var prepared = await Prepared(tool);
        var result = await tool.CompleteAsync(prepared, Acknowledgement(prepared));
        Assert.True(result.Succeeded);
        Assert.Equal(WeaverEffectScope.ClientWorkingState, result.Value!.Receipt?.Scope);
        Assert.Equal("rev-2", result.Value.Receipt?.After.Revision);
        Assert.Equal(prepared.Target, result.Value.Receipt?.Before);
        Assert.Contains("has not been saved or published", result.Value.Content);
        Assert.DoesNotContain("secret-value", result.Value.Payload!.Data.GetRawText());
        Assert.True(result.Value.Payload.Data.GetProperty("observed").GetBoolean());
    }

    [Theory]
    [InlineData("wrong-node")]
    [InlineData("wrong-position")]
    [InlineData("wrong-resource")]
    [InlineData("same-revision")]
    [InlineData("not-observed")]
    [InlineData("wrong-schema")]
    [InlineData("refused")]
    [InlineData("wrong-generation")]
    [InlineData("wrong-digest")]
    public async Task Invalid_or_unobserved_acknowledgements_never_claim_an_effect(string defect)
    {
        var tool = new WorkflowSetDesignerPositionTool(new Authority(true));
        var prepared = await Prepared(tool);
        var ack = Acknowledgement(prepared);
        ack = defect switch
        {
            "wrong-resource" => ack with { After = ack.After! with { ResourceId = "another-workflow" } },
            "same-revision" => ack with { After = ack.After! with { Revision = "rev-1" } },
            "wrong-schema" => ack with { Result = ack.Result! with { SchemaVersion = "2" } },
            "refused" => ack with { Outcome = WeaverClientOutcome.Refused },
            "wrong-generation" => ack with { Client = new("client-1", "other-generation") },
            "wrong-digest" => ack with { ApprovalDigest = "other-digest" },
            _ => ack with { Result = new("workflows.layout-change", "1", JsonSerializer.SerializeToElement(new
                { activityId = defect == "wrong-node" ? "another-node" : "node-1", x = defect == "wrong-position" ? 999 : 100, y = 200, revision = "rev-2", undoToken = "undo-1", observed = defect != "not-observed" })) }
        };
        var result = await tool.CompleteAsync(prepared, ack);
        Assert.False(result.Succeeded);
        Assert.Null(result.Value?.Receipt);
    }

    [Fact]
    public async Task Graph_read_preserves_module_typed_data_without_an_effect_receipt()
    {
        var tool = new WorkflowReadGraphTool();
        var invocation = Invocation() with { ToolName = WorkflowReadGraphTool.ToolName, Arguments = new Dictionary<string, object?>() };
        var preparation = await tool.PrepareAsync(invocation);
        Assert.True(preparation.Succeeded);
        var result = await tool.ExecuteAsync(WeaverTestToolHelpers.Prepared(invocation, tool.Descriptor, preparation.Value!));
        Assert.True(result.Succeeded);
        Assert.Equal("workflows.graph", result.Value!.Payload?.TypeId);
        Assert.Contains("node-1", result.Value.Payload!.Data.GetRawText());
        Assert.DoesNotContain("secret-value", result.Value.Payload.Data.GetRawText());
        Assert.Null(result.Value.Receipt);
    }

    private static WeaverToolInvocation Invocation() => new("call-1", WorkflowSetDesignerPositionTool.ToolName, "session-1", "actor-1",
        new Dictionary<string, object?> { ["activityId"] = "node-1", ["x"] = 100, ["y"] = 200 }, [WorkflowWeaverTests.Snapshot()])
    { TenantId = "tenant-1", TurnId = "turn-1", Client = WorkflowWeaverTests.Snapshot().Client };
    private static async Task<WeaverPreparedToolInvocation> Prepared(WorkflowSetDesignerPositionTool tool)
    {
        var invocation = Invocation();
        var preparation = await tool.PrepareAsync(invocation);
        Assert.True(preparation.Succeeded);
        return WeaverTestToolHelpers.Prepared(invocation, tool.Descriptor, preparation.Value!);
    }
    private static WeaverClientAcknowledgement Acknowledgement(WeaverPreparedToolInvocation invocation) => new(invocation.Client!, invocation.ApprovalDigest,
        WeaverClientOutcome.Applied, "Untrusted summary secret-value", invocation.Target! with { Revision = "rev-2" },
        new("workflows.layout-change", "1", JsonSerializer.SerializeToElement(new { activityId = "node-1", x = 100, y = 200, revision = "rev-2", undoToken = "undo-1", observed = true, secret = "secret-value" })));
    private sealed class Authority(bool allowed) : IWeaverToolAuthorizer
    {
        public string? Actor { get; private set; }
        public string? Tenant { get; private set; }
        public WeaverResourceTarget? Target { get; private set; }
        public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
        {
            Actor = actorId; Tenant = tenantId; Target = target;
            return Task.FromResult(new WeaverPolicyDecision(allowed, []));
        }
    }
}
