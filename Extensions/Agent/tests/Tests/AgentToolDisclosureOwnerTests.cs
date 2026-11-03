using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class AgentToolDisclosureOwnerTests
{
    private const string Module = "disclosure-owner.module";
    private const string SourceId = "disclosure-owner.source";
    private const string MissingSourceId = "disclosure-owner.uninstalled";
    private const string ForeignModule = "disclosure-owner.foreign-module";
    private const string ToolName = "disclosure_owner_write";

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("ambiguous")]
    public async Task Native_discovery_refuses_a_tool_without_an_installed_same_module_disclosure_owner(string? invalidOwner)
    {
        using var fixture = await Fixture.CreateAsync(invalidOwner);

        var error = await Record.ExceptionAsync(async () =>
            await fixture.Services.GetRequiredService<IAgentToolRegistry>().ListAsync());

        fixture.AssertUntouched();
        AssertOwnershipDiagnostic(Assert.IsType<AgentContributionException>(error).Message, fixture.Tool.Descriptor.DisclosureSourceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("ambiguous")]
    public async Task Direct_full_auto_invocation_refuses_invalid_disclosure_ownership_before_preparation(string? invalidOwner)
    {
        using var fixture = await Fixture.CreateAsync(invalidOwner);
        AgentToolDispatch? dispatch = null;

        var error = await Record.ExceptionAsync(async () => dispatch = await fixture.Services
            .GetRequiredService<IAgentToolInvoker>().DispatchAsync(fixture.Invocation(), fixture.Session.Policy));

        fixture.AssertUntouched();
        if (error is not null)
            AssertOwnershipDiagnostic(Assert.IsType<AgentContributionException>(error).Message, fixture.Tool.Descriptor.DisclosureSourceId);
        else
        {
            Assert.NotNull(dispatch);
            Assert.Equal(AgentToolDispatchOutcome.Denied, dispatch.Outcome);
            Assert.Null(dispatch.Proposal);
            Assert.Null(dispatch.Result);
            AssertOwnershipDiagnostic(Assert.IsType<AgentError>(dispatch.Error).Message, fixture.Tool.Descriptor.DisclosureSourceId);
        }
    }

    [Fact]
    public async Task A_valid_context_free_owner_is_discovered_and_applies_one_full_auto_effect()
    {
        using var fixture = await Fixture.CreateAsync("valid");
        var tools = await fixture.Services.GetRequiredService<IAgentToolRegistry>().ListAsync();
        Assert.Same(fixture.Tool, Assert.Single(tools));

        var dispatch = await fixture.Services.GetRequiredService<IAgentToolInvoker>()
            .DispatchAsync(fixture.Invocation(), fixture.Session.Policy);

        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        Assert.Null(dispatch.Error);
        Assert.True(dispatch.Result?.Succeeded);
        Assert.NotNull(dispatch.Result?.Receipt);
        Assert.Equal(1, fixture.Tool.Preparations);
        Assert.Equal(1, fixture.Tool.Executions);
        Assert.Equal("updated", fixture.Tool.Value);
        Assert.True(fixture.Authority.Checks > 0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    public async Task An_approved_operation_cannot_execute_after_its_disclosure_owner_is_unloaded_or_replaced(string currentOwner)
    {
        using var fixture = await Fixture.CreateAsync("valid", requireApproval: true);
        var proposal = await fixture.ProposeAndApproveAsync();
        Assert.Equal(1, fixture.Tool.Preparations);
        Assert.Equal(0, fixture.Tool.Executions);
        // Recompose installed services while retaining the genuine server session/preparation.
        // A fresh native scoped registry must observe the source unloading or changing modules.
        using var current = fixture.Recompose(currentOwner);
        using var scope = current.CreateScope();
        AgentResult<AgentProposalExecutionResult>? result = null;

        var error = await Record.ExceptionAsync(async () => result = await scope.ServiceProvider
            .GetRequiredService<IAgentProposalService>().ExecuteAsync(proposal.Id, fixture.Session.ActorId, proposal.BaseRevision));

        Assert.Equal(1, fixture.Tool.Preparations);
        Assert.Equal(0, fixture.Tool.Executions);
        Assert.Equal("original", fixture.Tool.Value);
        if (error is not null)
            AssertOwnershipDiagnostic(Assert.IsType<AgentContributionException>(error).Message, SourceId);
        else
        {
            Assert.NotNull(result);
            Assert.False(result.Succeeded);
            Assert.False(result.Value?.Executed ?? false);
            Assert.Null(result.Value?.ToolResult?.Receipt);
            AssertOwnershipDiagnostic(Assert.IsType<AgentError>(result.Error).Message, SourceId);
        }
    }

    [Fact]
    public async Task A_valid_owner_in_a_fresh_composition_executes_the_genuinely_approved_operation_once()
    {
        using var fixture = await Fixture.CreateAsync("valid", requireApproval: true);
        var proposal = await fixture.ProposeAndApproveAsync();
        using var current = fixture.Recompose("valid");
        using var scope = current.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IAgentProposalService>()
            .ExecuteAsync(proposal.Id, fixture.Session.ActorId, proposal.BaseRevision);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.True(result.Value?.Executed);
        Assert.NotNull(result.Value?.ToolResult?.Receipt);
        Assert.Equal(1, fixture.Tool.Preparations);
        Assert.Equal(1, fixture.Tool.Executions);
        Assert.Equal("updated", fixture.Tool.Value);
    }

    private static void AssertOwnershipDiagnostic(string message, string? sourceId)
    {
        Assert.Contains(ToolName, message, StringComparison.Ordinal);
        if (sourceId is not null) Assert.Contains(sourceId, message, StringComparison.Ordinal);
        else Assert.Contains("disclosure", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source", message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        internal IServiceProvider Services => scope.ServiceProvider;
        internal MutationTool Tool { get; }
        internal PermissiveAuthority Authority { get; } = new();
        internal AgentSession Session { get; private set; } = null!;

        private Fixture(string? owner)
        {
            Tool = new(owner is null ? null : owner == "missing" ? MissingSourceId : SourceId);
            root = Compose(owner);
            scope = root.CreateScope();
        }

        internal static async Task<Fixture> CreateAsync(string? owner, bool requireApproval = false)
        {
            var fixture = new Fixture(owner);
            fixture.Session = await fixture.Services.GetRequiredService<IAgentSessionService>().CreateAsync(new(
                "tenant", "actor", "conversation", "test-provider", "change", null,
                AgentPolicy.Default with { AutonomyMode = requireApproval ? AgentAutonomyMode.AutoReadOnly : AgentAutonomyMode.FullAuto },
                new Dictionary<string, string>()));
            return fixture;
        }

        private ServiceProvider Compose(string? owner, IAgentSessionService? sessions = null, InMemoryAgentProposalStore? proposals = null)
            => AgentTestServices.Create(services =>
            {
                services.RemoveAll<IAgentContextProvider>();
                if (owner != "missing")
                    services.AddSingleton<IAgentContextProvider>(new DisclosureSource(owner == "foreign" ? ForeignModule : Module));
                if (owner == "ambiguous")
                    services.AddSingleton<IAgentContextProvider>(new DisclosureSource(Module));
                services.AddSingleton<IAgentTool>(Tool);
                services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => Authority));
                if (sessions is not null) services.Replace(ServiceDescriptor.Singleton(sessions));
                if (proposals is not null) services.Replace(ServiceDescriptor.Singleton(proposals));
            });

        internal ServiceProvider Recompose(string owner) => Compose(owner,
            root.GetRequiredService<IAgentSessionService>(), root.GetRequiredService<InMemoryAgentProposalStore>());

        internal AgentToolInvocation Invocation() => new("owner-call", ToolName, Session.Id, Session.ActorId,
            new Dictionary<string, object?> { ["value"] = "updated" }, [])
        { TenantId = Session.TenantId, TurnId = "owner-turn" };

        internal async Task<AgentActionProposal> ProposeAndApproveAsync()
        {
            var dispatch = await Services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(Invocation(), Session.Policy);
            Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
            var proposal = Assert.IsType<AgentActionProposal>(dispatch.Proposal);
            var approval = await Services.GetRequiredService<IAgentProposalService>()
                .ApproveAsync(proposal.Id, Session.ActorId, proposal.BaseRevision);
            Assert.True(approval.Succeeded, approval.Error?.Message);
            Assert.Equal(AgentActionProposalStatus.Approved, approval.Value?.Status);
            return Assert.IsType<AgentActionProposal>(approval.Value);
        }

        internal void AssertUntouched()
        {
            Assert.Equal(0, Tool.Preparations);
            Assert.Equal(0, Tool.Executions);
            Assert.Equal("original", Tool.Value);
        }

        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class PermissiveAuthority : IAgentToolAuthorizer
    {
        internal int Checks;
        public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId,
            AgentResourceTarget? target, CancellationToken cancellationToken = default)
        { Checks++; return Task.FromResult(new AgentPolicyDecision(true, [])); }
    }

    private sealed class DisclosureSource(string moduleId) : IAgentContextProvider
    {
        public AgentContextProviderDescriptor Descriptor { get; } = new(SourceId, moduleId, "*", [], ["1"], ["owned-resource"]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([], []));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            var owned = request.Context.Count == 0 && request.Results.All(result => result.ToolName == ToolName
                && result.Access is { } access && access.ModuleId == moduleId && access.DisclosureSourceId == SourceId
                && access.Target is { } target && target.ModuleId == moduleId && target.ResourceType == "owned-resource" && target.ResourceId == "resource");
            return ValueTask.FromResult(owned ? new AgentContextContribution([], [])
                : new AgentContextContribution([], [new(SourceId, "owner.evidence_not_owned", "The source does not own this evidence.", true)]));
        }
    }

    private sealed class MutationTool(string? sourceId) : IAgentTool
    {
        private static readonly AgentResourceTarget Target = new(Module, "owned-resource", "resource", "revision-1");
        internal int Preparations, Executions;
        internal string Value = "original";
        public AgentToolDescriptor Descriptor { get; } = new(ToolName, "Update resource", "Changes one owned resource.",
            AgentToolMutability.Mutating, AgentRisk.ReviewRequired, "{\"type\":\"object\"}", [], [])
        { ModuleId = Module, DisclosureSourceId = sourceId, ResultType = "owner.result", ResultSchemaVersion = "1" };

        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Preparations++;
            Assert.Empty(invocation.Context);
            return Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with { Target = Target }));
        }

        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            Value = invocation.Arguments.GetProperty("value").GetString() ?? string.Empty;
            var payload = new AgentPayload("owner.result", "1", JsonSerializer.SerializeToElement(new { value = Value }));
            return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, ToolName, true, "Observed owning-resource update.", payload)
            { Receipt = new(invocation.InvocationId, Target, Target with { Revision = "revision-2" }, AgentEffectScope.ServerPersisted, payload) }));
        }
    }
}
