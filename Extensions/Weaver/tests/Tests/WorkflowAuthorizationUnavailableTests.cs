using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows;
using Elsa.Weaver.Workflows.Services;
using Elsa.Weaver.Workflows.Tools;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

/// <summary>
/// An owning workflow authorizer that cannot decide (spec 1193, story 5) fails Weaver closed as unavailable, with the
/// Design API's stable code, distinct from denied and without its cause, at the tool, invoker, proposal and context
/// boundaries.
/// </summary>
public sealed class WorkflowAuthorizationUnavailableTests
{
    private const string Code = "workflow.authorization.unavailable";
    private const string Cause = "Private owner infrastructure detail 91d2";

    [Theory]
    [InlineData(WorkflowReadGraphTool.ToolName)]
    [InlineData("workflow_set_designer_position")]
    public async Task A_tool_s_owner_check_that_cannot_decide_is_unavailable_and_distinct_from_denied(string toolName)
    {
        using var fixture = await Fixture.CreateAsync();
        var tool = await fixture.ToolAsync(toolName);

        fixture.Owner.Mode = OwnerMode.Throw;
        var unavailable = await tool.AuthorizeAsync("actor-1", "tenant-1", Target);
        fixture.Owner.Mode = OwnerMode.Deny;
        var denied = await tool.AuthorizeAsync("actor-1", "tenant-1", Target);

        Assert.False(unavailable.Allowed);
        var violation = Assert.Single(unavailable.Violations);
        Assert.Equal(Code, violation.Code);
        Assert.Equal(WorkflowDefinitionAuthorizationException.UnavailableCode, violation.Code);
        Assert.True(violation.OwnerUnavailable);
        Assert.DoesNotContain(Cause, violation.Message, StringComparison.Ordinal);
        Assert.False(denied.Allowed);
        Assert.Equal("weaver.workflow.permission_denied", Assert.Single(denied.Violations).Code);
        Assert.False(Assert.Single(denied.Violations).OwnerUnavailable);
    }

    [Fact]
    public async Task A_missing_owner_stays_the_composition_fault_not_the_unavailable_outcome()
    {
        using var fixture = await Fixture.CreateAsync(services => services.RemoveAll<IWorkflowDefinitionAuthorizer>());
        var tool = await fixture.ToolAsync(WorkflowReadGraphTool.ToolName);

        var decision = await tool.AuthorizeAsync("actor-1", "tenant-1", Target);

        var violation = Assert.Single(decision.Violations);
        Assert.Equal("weaver.workflow.authority_unavailable", violation.Code);
        Assert.False(violation.OwnerUnavailable);
    }

    [Fact]
    public async Task A_read_dispatch_reports_unavailable_as_503_with_the_code_while_a_denial_stays_forbidden()
    {
        using var fixture = await Fixture.CreateAsync();
        var context = await fixture.CollectAsync();
        Assert.True(context.Succeeded, context.Error?.Message);

        fixture.Owner.Mode = OwnerMode.Throw;
        var unavailable = await fixture.DispatchReadAsync(context.Value!.Attachments, "read-unavailable");
        fixture.Owner.Mode = OwnerMode.Deny;
        var denied = await fixture.DispatchReadAsync(context.Value.Attachments, "read-denied");

        Assert.Equal(AgentToolDispatchOutcome.Denied, unavailable.Outcome);
        Assert.Null(unavailable.Result);
        Assert.Equal(Code, unavailable.Error?.Code);
        Assert.Equal(503, unavailable.Error?.StatusCode);
        Assert.DoesNotContain(Cause, JsonSerializer.Serialize(unavailable), StringComparison.Ordinal);
        Assert.Equal(AgentToolDispatchOutcome.Denied, denied.Outcome);
        Assert.Equal("weaver.tool.forbidden", denied.Error?.Code);
        Assert.Equal(403, denied.Error?.StatusCode);
    }

    [Fact]
    public async Task A_reviewed_change_whose_owner_cannot_decide_is_neither_proposed_nor_executed()
    {
        using var fixture = await Fixture.CreateAsync();
        var context = await fixture.CollectAsync();
        Assert.True(context.Succeeded, context.Error?.Message);

        fixture.Owner.Mode = OwnerMode.Throw;
        var dispatch = await fixture.DispatchMoveAsync(context.Value!.Attachments, "move-unavailable");

        Assert.Equal(AgentToolDispatchOutcome.Denied, dispatch.Outcome);
        Assert.Null(dispatch.Proposal);
        Assert.Equal(Code, dispatch.Error?.Code);
        Assert.Equal(503, dispatch.Error?.StatusCode);
        Assert.DoesNotContain(Cause, JsonSerializer.Serialize(dispatch), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_approved_change_whose_owner_can_no_longer_decide_does_not_execute()
    {
        using var fixture = await Fixture.CreateAsync();
        var context = await fixture.CollectAsync();
        var dispatch = await fixture.DispatchMoveAsync(context.Value!.Attachments, "move-approved");
        var proposal = Assert.IsType<AgentActionProposal>(dispatch.Proposal);
        var proposals = fixture.Services.GetRequiredService<IAgentProposalService>();
        var approved = await proposals.ApproveAsync(proposal.Id, "actor-1", proposal.BaseRevision);
        Assert.True(approved.Succeeded, approved.Error?.Message);

        fixture.Owner.Mode = OwnerMode.Throw;
        var execution = await proposals.ExecuteAsync(proposal.Id, "actor-1", proposal.BaseRevision);

        Assert.False(execution.Succeeded);
        Assert.Null(execution.Value?.ClientInvocation);
        Assert.Equal(Code, execution.Error?.Code);
        Assert.Equal(503, execution.Error?.StatusCode);
        Assert.DoesNotContain(Cause, execution.Error?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Context_collection_withholds_the_snapshot_with_the_unavailable_diagnostic()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Owner.Mode = OwnerMode.Throw;

        var collected = await fixture.CollectAsync();

        Assert.False(collected.Succeeded);
        Assert.Equal(new AgentError(Code, "The owning module could not establish current authority for this operation.", 503), collected.Error);
        Assert.DoesNotContain(collected.Value?.Attachments ?? [], item => item.ContentType == "workflow.definition");
        var diagnostic = Assert.Single(collected.Value!.Diagnostics, item => item.Code == Code);
        Assert.True(diagnostic.Required);
        Assert.True(diagnostic.OwnerUnavailable);
        Assert.DoesNotContain(collected.Value.Diagnostics, item => item.Code == "weaver.workflow.permission_denied");
        Assert.DoesNotContain(Cause, JsonSerializer.Serialize(collected), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OwnerMode.Throw, Code)]
    [InlineData(OwnerMode.Deny, "weaver.workflow.permission_denied")]
    public async Task Disclosure_admission_reports_unavailable_distinctly_from_denied(OwnerMode mode, string expected)
    {
        using var fixture = await Fixture.CreateAsync();
        var context = await fixture.CollectAsync();
        Assert.True(context.Succeeded, context.Error?.Message);
        fixture.Owner.Mode = mode;

        var admission = await fixture.Source.AdmitDisclosureAsync(new(fixture.Session.Id, context.Value!.Attachments, [], false));

        Assert.Empty(admission.Attachments);
        var diagnostic = Assert.Single(admission.Diagnostics);
        Assert.Equal(expected, diagnostic.Code);
        Assert.True(diagnostic.Required);
        // Only the unavailable diagnostic carries the owner-unavailable marker; a denial never does.
        Assert.Equal(mode == OwnerMode.Throw, diagnostic.OwnerUnavailable);
        Assert.DoesNotContain(Cause, diagnostic.Message, StringComparison.Ordinal);
    }

    private static readonly AgentResourceTarget Target = new(WorkflowAgentSnapshot.ModuleId, "workflow-definition", "wf-1", "rev-1");

    public enum OwnerMode { Allow, Deny, Throw }

    private sealed class SwitchableOwner : IWorkflowDefinitionAuthorizer
    {
        internal OwnerMode Mode = OwnerMode.Allow;
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) => Decide(definitionId, "read");
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => Decide(definitionId, "manage");

        private ValueTask<bool> Decide(string definitionId, string operation) => Mode == OwnerMode.Throw
            ? throw new WorkflowDefinitionAuthorizationException(definitionId, operation, new IOException(Cause))
            : ValueTask.FromResult(Mode == OwnerMode.Allow);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        internal SwitchableOwner Owner { get; } = new();
        internal IServiceProvider Services => scope.ServiceProvider;
        internal AgentSession Session { get; private set; } = null!;
        internal DefaultWorkflowAgentContextProvider Source =>
            Services.GetServices<IAgentContextProvider>().OfType<DefaultWorkflowAgentContextProvider>().Single();

        private Fixture(Action<IServiceCollection>? configure)
        {
            root = AgentTestServices.Create(services =>
            {
                new WeaverWorkflowsFeature().ConfigureServices(services);
                services.AddSingleton<IWorkflowDefinitionAuthorizer>(Owner);
                configure?.Invoke(services);
            });
            scope = root.CreateScope();
        }

        internal static async Task<Fixture> CreateAsync(Action<IServiceCollection>? configure = null)
        {
            var fixture = new Fixture(configure);
            fixture.Session = await fixture.Services.GetRequiredService<IAgentSessionService>().CreateAsync(new("tenant-1", "actor-1",
                "conversation-1", "test-provider", "build", null, AgentPolicy.Default, new Dictionary<string, string>()));
            return fixture;
        }

        internal async Task<IAgentToolAuthorization> ToolAsync(string name) =>
            Assert.IsAssignableFrom<IAgentToolAuthorization>(Assert.Single(await Services.GetRequiredService<IAgentToolRegistry>().ListAsync(),
                item => item.Descriptor.Name == name));

        internal Task<AgentResult<AgentContextContribution>> CollectAsync() => Services.GetRequiredService<IAgentContextCollector>()
            .CollectAsync(Session.Policy, WorkflowAgentTests.Request(WorkflowAgentTests.Snapshot()) with { SessionId = Session.Id });

        internal Task<AgentToolDispatch> DispatchReadAsync(IReadOnlyCollection<AgentContextAttachment> context, string turn) =>
            Services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(new($"call-{turn}", WorkflowReadGraphTool.ToolName, Session.Id,
                "actor-1", new Dictionary<string, object?>(), context) { TenantId = "tenant-1", TurnId = turn }, Session.Policy);

        internal Task<AgentToolDispatch> DispatchMoveAsync(IReadOnlyCollection<AgentContextAttachment> context, string turn) =>
            Services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(new($"call-{turn}", "workflow_set_designer_position", Session.Id,
                "actor-1", new Dictionary<string, object?> { ["activityId"] = "node-1", ["x"] = 50, ["y"] = 60 }, context)
            { TenantId = "tenant-1", TurnId = turn, Client = WorkflowAgentTests.Snapshot().Client }, Session.Policy);

        public void Dispose()
        {
            scope.Dispose();
            root.Dispose();
        }
    }
}
