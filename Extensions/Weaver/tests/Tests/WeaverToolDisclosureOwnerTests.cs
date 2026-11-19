using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class WeaverToolDisclosureOwnerTests
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
            await fixture.Services.GetRequiredService<IWeaverToolRegistry>().ListAsync());

        fixture.AssertUntouched();
        AssertOwnershipDiagnostic(Assert.IsType<WeaverContributionException>(error).Message, fixture.Tool.Descriptor.DisclosureSourceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("ambiguous")]
    public async Task Direct_full_auto_invocation_refuses_invalid_disclosure_ownership_before_preparation(string? invalidOwner)
    {
        using var fixture = await Fixture.CreateAsync(invalidOwner);
        WeaverToolDispatch? dispatch = null;

        var error = await Record.ExceptionAsync(async () => dispatch = await fixture.Services
            .GetRequiredService<IWeaverToolInvoker>().DispatchAsync(fixture.Invocation(), fixture.Session.Policy));

        fixture.AssertUntouched();
        if (error is not null)
            AssertOwnershipDiagnostic(Assert.IsType<WeaverContributionException>(error).Message, fixture.Tool.Descriptor.DisclosureSourceId);
        else
        {
            Assert.NotNull(dispatch);
            Assert.Equal(WeaverToolDispatchOutcome.Denied, dispatch.Outcome);
            Assert.Null(dispatch.Proposal);
            Assert.Null(dispatch.Result);
            AssertOwnershipDiagnostic(Assert.IsType<WeaverError>(dispatch.Error).Message, fixture.Tool.Descriptor.DisclosureSourceId);
        }
    }

    [Fact]
    public async Task A_valid_context_free_owner_is_discovered_and_applies_one_full_auto_effect()
    {
        using var fixture = await Fixture.CreateAsync("valid");
        var tools = await fixture.Services.GetRequiredService<IWeaverToolRegistry>().ListAsync();
        Assert.Same(fixture.Tool, Assert.Single(tools));

        var dispatch = await fixture.Services.GetRequiredService<IWeaverToolInvoker>()
            .DispatchAsync(fixture.Invocation(), fixture.Session.Policy);

        Assert.Equal(WeaverToolDispatchOutcome.Executed, dispatch.Outcome);
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
        WeaverResult<WeaverProposalExecutionResult>? result = null;

        var error = await Record.ExceptionAsync(async () => result = await scope.ServiceProvider
            .GetRequiredService<IWeaverProposalService>().ExecuteAsync(proposal.Id, fixture.Session.ActorId, proposal.BaseRevision));

        Assert.Equal(1, fixture.Tool.Preparations);
        Assert.Equal(0, fixture.Tool.Executions);
        Assert.Equal("original", fixture.Tool.Value);
        if (error is not null)
            AssertOwnershipDiagnostic(Assert.IsType<WeaverContributionException>(error).Message, SourceId);
        else
        {
            Assert.NotNull(result);
            Assert.False(result.Succeeded);
            Assert.False(result.Value?.Executed ?? false);
            Assert.Null(result.Value?.ToolResult?.Receipt);
            AssertOwnershipDiagnostic(Assert.IsType<WeaverError>(result.Error).Message, SourceId);
        }
    }

    [Fact]
    public async Task A_valid_owner_in_a_fresh_composition_executes_the_genuinely_approved_operation_once()
    {
        using var fixture = await Fixture.CreateAsync("valid", requireApproval: true);
        var proposal = await fixture.ProposeAndApproveAsync();
        using var current = fixture.Recompose("valid");
        using var scope = current.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IWeaverProposalService>()
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
        internal WeaverSession Session { get; private set; } = null!;

        private Fixture(string? owner)
        {
            Tool = new(owner is null ? null : owner == "missing" ? MissingSourceId : SourceId);
            root = Compose(owner);
            scope = root.CreateScope();
        }

        internal static async Task<Fixture> CreateAsync(string? owner, bool requireApproval = false)
        {
            var fixture = new Fixture(owner);
            fixture.Session = await fixture.Services.GetRequiredService<IWeaverSessionService>().CreateAsync(new(
                "tenant", "actor", "conversation", "test-provider", "change", null,
                WeaverPolicy.Default with { AutonomyMode = requireApproval ? WeaverAutonomyMode.AutoReadOnly : WeaverAutonomyMode.FullAuto },
                new Dictionary<string, string>()));
            return fixture;
        }

        private ServiceProvider Compose(string? owner, IWeaverSessionService? sessions = null, InMemoryWeaverProposalStore? proposals = null)
            => WeaverTestServices.Create(services =>
            {
                services.RemoveAll<IWeaverContextProvider>();
                if (owner != "missing")
                    services.AddSingleton<IWeaverContextProvider>(new DisclosureSource(owner == "foreign" ? ForeignModule : Module));
                if (owner == "ambiguous")
                    services.AddSingleton<IWeaverContextProvider>(new DisclosureSource(Module));
                services.AddSingleton<IWeaverTool>(Tool);
                services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer>(_ => Authority));
                if (sessions is not null) services.Replace(ServiceDescriptor.Singleton(sessions));
                if (proposals is not null) services.Replace(ServiceDescriptor.Singleton(proposals));
            });

        internal ServiceProvider Recompose(string owner) => Compose(owner,
            root.GetRequiredService<IWeaverSessionService>(), root.GetRequiredService<InMemoryWeaverProposalStore>());

        internal WeaverToolInvocation Invocation() => new("owner-call", ToolName, Session.Id, Session.ActorId,
            new Dictionary<string, object?> { ["value"] = "updated" }, [])
        { TenantId = Session.TenantId, TurnId = "owner-turn" };

        internal async Task<WeaverActionProposal> ProposeAndApproveAsync()
        {
            var dispatch = await Services.GetRequiredService<IWeaverToolInvoker>().DispatchAsync(Invocation(), Session.Policy);
            Assert.Equal(WeaverToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
            var proposal = Assert.IsType<WeaverActionProposal>(dispatch.Proposal);
            var approval = await Services.GetRequiredService<IWeaverProposalService>()
                .ApproveAsync(proposal.Id, Session.ActorId, proposal.BaseRevision);
            Assert.True(approval.Succeeded, approval.Error?.Message);
            Assert.Equal(WeaverActionProposalStatus.Approved, approval.Value?.Status);
            return Assert.IsType<WeaverActionProposal>(approval.Value);
        }

        internal void AssertUntouched()
        {
            Assert.Equal(0, Tool.Preparations);
            Assert.Equal(0, Tool.Executions);
            Assert.Equal("original", Tool.Value);
        }

        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class PermissiveAuthority : IWeaverToolAuthorizer
    {
        internal int Checks;
        public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId,
            WeaverResourceTarget? target, CancellationToken cancellationToken = default)
        { Checks++; return Task.FromResult(new WeaverPolicyDecision(true, [])); }
    }

    private sealed class DisclosureSource(string moduleId) : IWeaverContextProvider
    {
        public WeaverContextProviderDescriptor Descriptor { get; } = new(SourceId, moduleId, "*", [], ["1"], ["owned-resource"]);
        public ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new WeaverContextContribution([], []));
        public ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            var owned = request.Context.Count == 0 && request.Results.All(result => result.ToolName == ToolName
                && result.Access is { } access && access.ModuleId == moduleId && access.DisclosureSourceId == SourceId
                && access.Target is { } target && target.ModuleId == moduleId && target.ResourceType == "owned-resource" && target.ResourceId == "resource");
            return ValueTask.FromResult(owned ? new WeaverContextContribution([], [])
                : new WeaverContextContribution([], [new(SourceId, "owner.evidence_not_owned", "The source does not own this evidence.", true)]));
        }
    }

    private sealed class MutationTool(string? sourceId) : IWeaverTool
    {
        private static readonly WeaverResourceTarget Target = new(Module, "owned-resource", "resource", "revision-1");
        internal int Preparations, Executions;
        internal string Value = "original";
        public WeaverToolDescriptor Descriptor { get; } = new(ToolName, "Update resource", "Changes one owned resource.",
            WeaverToolMutability.Mutating, WeaverRisk.ReviewRequired, "{\"type\":\"object\"}", [], [])
        { ModuleId = Module, DisclosureSourceId = sourceId, ResultType = "owner.result", ResultSchemaVersion = "1" };

        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Preparations++;
            Assert.Empty(invocation.Context);
            return Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor) with { Target = Target }));
        }

        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            Value = invocation.Arguments.GetProperty("value").GetString() ?? string.Empty;
            var payload = new WeaverPayload("owner.result", "1", JsonSerializer.SerializeToElement(new { value = Value }));
            return Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, ToolName, true, "Observed owning-resource update.", payload)
            { Receipt = new(invocation.InvocationId, Target, Target with { Revision = "revision-2" }, WeaverEffectScope.ServerPersisted, payload) }));
        }
    }
}
