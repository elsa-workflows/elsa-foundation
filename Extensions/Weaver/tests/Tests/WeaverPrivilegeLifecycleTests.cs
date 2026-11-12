using System.Security.Claims;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Weaver.Services;
using Elsa.Api.AspNetCore;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class WeaverPrivilegeLifecycleTests
{
    [Theory]
    [InlineData(WeaverPrivilegeClass.Administrative, false)]
    [InlineData(WeaverPrivilegeClass.Administrative, true)]
    [InlineData(WeaverPrivilegeClass.Irrecoverable, false)]
    [InlineData(WeaverPrivilegeClass.Irrecoverable, true)]
    [InlineData(WeaverPrivilegeClass.Activation, false)]
    [InlineData(WeaverPrivilegeClass.Activation, true)]
    public async Task A_declared_class_requires_its_current_host_grant_independently_of_other_classes(WeaverPrivilegeClass required, bool enabled)
    {
        var config = Configuration(enabled ? 1 << (int)required : 0);
        var tool = new ProposalMutationTool { PrivilegeClasses = [required] };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: Configure(config));
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(enabled ? WeaverToolDispatchOutcome.ProposalCreated : WeaverToolDispatchOutcome.Denied, dispatch.Outcome);
        if (!enabled) Assert.Equal("weaver.proposal.privilege_disabled", dispatch.Error?.Code);
        Assert.Equal(0, tool.DispatchCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Every_declared_class_is_required_under_all_host_combinations(int mask)
    {
        var tool = new ProposalMutationTool { PrivilegeClasses = [WeaverPrivilegeClass.Activation, WeaverPrivilegeClass.Irrecoverable, WeaverPrivilegeClass.Administrative] };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: Configure(Configuration(mask)));
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(mask == 7 ? WeaverToolDispatchOutcome.ProposalCreated : WeaverToolDispatchOutcome.Denied, dispatch.Outcome);
        Assert.Equal(0, tool.DispatchCalls);
    }

    [Fact]
    public async Task Default_off_admin_veto_is_independent_of_effects_feature_selection_and_precedes_owner_preparation()
    {
        var tool = new ProposalMutationTool { CurrentRisk = WeaverRisk.Admin };
        using var fixture = await ProposalFixture.CreateAsync(tool);
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal("weaver.proposal.privilege_disabled", dispatch.Error?.Code);
        Assert.Equal(403, dispatch.Error?.StatusCode);
        Assert.Equal(0, tool.PrepareCalls);
        Assert.Equal(0, tool.DispatchCalls);
    }

    [Fact]
    public async Task A_new_classed_preparation_requires_effects_support_even_when_the_host_grants_the_class()
    {
        var tool = new ProposalMutationTool { PrivilegeClasses = [WeaverPrivilegeClass.Activation] };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: Configure(Configuration(7), effects: false));
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal("weaver.effects.not_enabled", dispatch.Error?.Code);
        Assert.Null(dispatch.Proposal);
        Assert.Equal(0, tool.DispatchCalls);
    }

    [Fact]
    public async Task Approval_veto_closes_the_undispatched_operation_and_enabling_again_requires_a_new_proposal()
    {
        var config = Configuration(7);
        var tool = new ProposalMutationTool { PrivilegeClasses = [WeaverPrivilegeClass.Activation] };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: Configure(config));
        var proposal = await fixture.ProposeAsync();
        Set(config, WeaverPrivilegeClass.Activation, false);
        var denied = await fixture.ApproveAsync(proposal);
        Assert.Equal("weaver.proposal.privilege_disabled", denied.Error?.Code);
        Assert.Equal(denied.Error, (await fixture.Proposals.FindResultAsync(proposal.Id))!.Error);
        Assert.Equal(WeaverActionProposalStatus.Failed, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
        Assert.Contains("No effect occurred", (await fixture.Proposals.FindResultAsync(proposal.Id))!.Content);
        Assert.Contains(await fixture.Audit.ListAsync(fixture.Session.Id), row => row.Kind == WeaverAuditEventKind.ProposalFailed
            && row.Metadata.GetValueOrDefault("dispatch") == "none" && row.Metadata.GetValueOrDefault("effect") == "none");
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
        Set(config, WeaverPrivilegeClass.Activation, true);
        Assert.False((await fixture.ApproveAsync(proposal)).Succeeded);
        var execution = await fixture.ExecuteAsync(proposal);
        Assert.Equal("weaver.proposal.privilege_disabled", execution.Error?.Code);
        Assert.Equal(execution.Error, (await fixture.Proposals.FindResultAsync(proposal.Id))!.Error);
        var fresh = await fixture.Invoker.DispatchAsync(fixture.Invocation("fresh-call") with { TurnId = "fresh-turn" }, fixture.Session.Policy);
        Assert.Equal(WeaverToolDispatchOutcome.ProposalCreated, fresh.Outcome);
        Assert.NotEqual(proposal.Id, fresh.Proposal!.Id);
        Assert.Equal(0, tool.DispatchCalls);
    }

    [Fact]
    public async Task Approval_calls_neither_registry_owner_authority_nor_principal_source()
    {
        var tool = new ProposalMutationTool { PrivilegeClasses = [WeaverPrivilegeClass.Administrative] };
        var registry = new GuardedRegistry(tool);
        var principal = new GuardedPrincipal();
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            Configure(Configuration(7))(services);
            services.Replace(ServiceDescriptor.Scoped<IWeaverToolRegistry>(_ => registry));
            services.AddScoped<IWeaverPrincipalSource>(_ => principal);
        });
        var proposal = await fixture.ProposeAsync();
        registry.Refuse = true;
        fixture.Authority.HoldNextDecision(_ => throw new InvalidOperationException("Approval must not call owner authority."));
        var approved = await fixture.ApproveAsync(proposal);
        Assert.True(approved.Succeeded, approved.Error?.Message);
        Assert.Equal(0, principal.Calls);
        Assert.Equal(0, tool.DispatchCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execution_veto_precedes_both_default_and_replaced_executor_handoff(bool replaced)
    {
        var config = Configuration(7);
        var executor = new GuardedExecutor();
        var tool = new ProposalMutationTool { PrivilegeClasses = [WeaverPrivilegeClass.Irrecoverable] };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            Configure(config)(services);
            if (replaced) services.Replace(ServiceDescriptor.Scoped<IWeaverActionProposalExecutor>(_ => executor));
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        Set(config, WeaverPrivilegeClass.Irrecoverable, false);
        Assert.Equal("weaver.proposal.privilege_disabled", (await fixture.ExecuteAsync(proposal)).Error?.Code);
        Assert.Equal(0, executor.Calls);
        Assert.Equal(0, tool.DispatchCalls);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task Approval_does_not_depend_on_a_schema_observation_that_became_unavailable_after_review()
    {
        var observation = new GuardedObservation();
        var tool = new ProposalMutationTool { PrivilegeClasses = [WeaverPrivilegeClass.Activation] };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            Configure(Configuration(7), effects: false)(services);
            new WeaverEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
            services.AddScoped<ISchemaDormancyCheck>(_ => observation);
        });
        var proposal = await fixture.ProposeAsync();
        var calls = observation.Calls;
        Assert.True(calls > 0);
        observation.Available = false;
        Assert.True((await fixture.ApproveAsync(proposal)).Succeeded);
        Assert.Equal(calls, observation.Calls);
        Assert.Equal("schema-version-not-finalized", (await fixture.ExecuteAsync(proposal)).Error?.Code);
        Assert.True(observation.Calls > calls);
        Assert.Equal(0, tool.DispatchCalls);
        Assert.Contains("No effect occurred", (await fixture.Proposals.FindResultAsync(proposal.Id))!.Content);
    }

    [Fact]
    public async Task A_toggle_during_the_executing_record_write_precedes_custom_executor_handoff()
    {
        var config = Configuration(7);
        var executor = new GuardedExecutor();
        var records = new ExecutingRecordStore(() => Set(config, WeaverPrivilegeClass.Activation, false));
        var tool = new ProposalMutationTool { PrivilegeClasses = [WeaverPrivilegeClass.Activation] };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            Configure(config)(services);
            services.SelectWeaverSessionRecordStore(_ => records, typeof(WeaverEffectsFeature));
            services.Replace(ServiceDescriptor.Scoped<IWeaverActionProposalExecutor>(_ => executor));
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        Assert.Equal("weaver.proposal.privilege_disabled", (await fixture.ExecuteAsync(proposal)).Error?.Code);
        Assert.Equal(1, records.ExecutingWrites);
        Assert.Equal(0, executor.Calls);
        Assert.Equal(0, tool.DispatchCalls);
        Assert.Contains("No effect occurred", (await fixture.Proposals.FindResultAsync(proposal.Id))!.Content);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task A_replaced_executor_cannot_borrow_another_producers_genuine_zero_effect_cancellation()
    {
        var tool = new ProposalMutationTool();
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            services.AddRouting().AddElsaEndpoints();
            var original = services.Single(row => row.ServiceType == typeof(IWeaverActionProposalExecutor));
            services.Replace(ServiceDescriptor.Scoped<IWeaverActionProposalExecutor>(scope =>
                new BorrowedCancellation((IWeaverActionProposalExecutor)original.ImplementationFactory!(scope), tool)));
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var execution = await fixture.ExecuteAsync(proposal);
        Assert.Null(execution.Error);
        Assert.Equal("uncertain", execution.Value?.Status);
        Assert.DoesNotContain("No effect occurred", execution.Value!.Summary);
        Assert.Equal(1, tool.ExecuteCalls);
        Assert.Equal("new", tool.Value);
        Assert.NotEqual(WeaverActionProposalStatus.Cancelled, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.DoesNotContain("No effect occurred", (await fixture.Proposals.FindResultAsync(proposal.Id))!.Content);
        Assert.DoesNotContain(await fixture.Audit.ListAsync(fixture.Session.Id), row => row.Kind == WeaverAuditEventKind.ProposalFailed
            && row.Metadata.GetValueOrDefault("effect") == "none");
        var duplicate = await fixture.ExecuteAsync(proposal);
        Assert.Null(duplicate.Error);
        Assert.Same(execution.Value, duplicate.Value);
        Assert.Equal(1, tool.ExecuteCalls);
        var response = await WeaverApiExecutionOutcomeTests.ExecuteAsync(fixture, proposal, proposal.BaseRevision);
        Assert.Equal(200, response.Status);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, response.Body.GetProperty("error").ValueKind);
        Assert.Equal("uncertain", response.Body.GetProperty("data").GetProperty("status").GetString());
        Assert.DoesNotContain("No effect occurred", response.Body.GetRawText());
        Assert.Equal(1, tool.ExecuteCalls);
    }

    [Fact]
    public async Task A_toggle_during_awaited_owner_authority_is_rechecked_before_actual_module_dispatch()
    {
        var config = Configuration(7);
        var tool = new ProposalMutationTool { PrivilegeClasses = [WeaverPrivilegeClass.Activation] };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: Configure(config));
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        fixture.Authority.HoldNextDecision(async _ => { Set(config, WeaverPrivilegeClass.Activation, false); await Task.Yield(); });
        Assert.Equal("weaver.proposal.privilege_disabled", (await fixture.ExecuteAsync(proposal)).Error?.Code);
        Assert.Equal(0, tool.DispatchCalls);
        Assert.Contains("No effect occurred", (await fixture.Proposals.FindResultAsync(proposal.Id))!.Content);
        Assert.Equal(WeaverActionProposalStatus.Failed, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
    }

    [Fact]
    public async Task A_toggle_after_the_dispatch_observation_does_not_relabel_or_replay_a_genuine_receipt()
    {
        var config = Configuration(7);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new ProposalMutationTool { PrivilegeClasses = [WeaverPrivilegeClass.Activation], Started = entered, Release = release };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: Configure(config));
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var execution = fixture.ExecuteAsync(proposal);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Set(config, WeaverPrivilegeClass.Activation, false);
        release.SetResult();
        var applied = await execution;
        Assert.True(applied.Value?.Executed);
        Assert.Same(applied.Value, (await fixture.ExecuteAsync(proposal)).Value);
        var duplicate = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(WeaverToolDispatchOutcome.Executed, duplicate.Outcome);
        Assert.Equal(applied.Value!.ToolResult!.Receipt, duplicate.Result!.Receipt);
        Assert.Equal(1, tool.DispatchCalls);
        Assert.Equal(1, tool.ExecuteCalls);
        Assert.Equal("new", tool.Value);
    }

    [Fact]
    public async Task Pending_client_reissue_after_a_veto_is_uncertain_not_a_zero_effect_refusal()
    {
        var config = Configuration(7);
        var client = new ProposalClientTool { PrivilegeClasses = [WeaverPrivilegeClass.Activation] };
        using var fixture = await ProposalFixture.CreateAsync(client, configure: Configure(config));
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        Assert.Equal("awaiting-client", (await fixture.ExecuteAsync(proposal)).Value?.Status);
        Set(config, WeaverPrivilegeClass.Activation, false);
        var withheld = await fixture.ExecuteAsync(proposal);
        Assert.Equal("uncertain", withheld.Value?.Status);
        Assert.Null(withheld.Value?.ClientInvocation);
        Assert.DoesNotContain("No effect occurred", withheld.Value!.Summary);
        Assert.Contains(await fixture.Audit.ListAsync(fixture.Session.Id), row => row.Metadata.GetValueOrDefault("dispatch") == "client"
            && row.Metadata.GetValueOrDefault("effect") == "possible");
        Assert.Equal(0, client.ExecuteCalls);
        Assert.Equal(0, client.CompleteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_admin_keeps_its_old_payload_and_digest_and_needs_no_new_feature_when_granted(bool granted)
    {
        var tool = new ProposalMutationTool();
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: Configure(Configuration(granted ? 1 : 0), effects: false));
        var proposal = await fixture.ProposeAsync();
        // Component-only old-format seed: no classes are manufactured as pre-upgrade data. Native old-binary proof is T038.
        var legacy = proposal.PreparedInvocation! with { Risk = WeaverRisk.Admin };
        legacy = legacy with { ApprovalDigest = WeaverInvocationIntegrity.PreparedDigest(legacy) };
        proposal = proposal with { Risk = WeaverRisk.Admin, PreparedInvocation = legacy };
        fixture.Services.GetRequiredService<InMemoryWeaverProposalStore>().Proposals[proposal.Id] = proposal;
        tool.CurrentRisk = WeaverRisk.Admin;
        var approved = await fixture.ApproveAsync(proposal);
        if (granted)
        {
            Assert.True(approved.Succeeded, approved.Error?.Message);
            Assert.True((await fixture.ExecuteAsync(proposal)).Value?.Executed);
            Assert.Equal(1, tool.ExecuteCalls);
        }
        else
        {
            Assert.Equal("weaver.proposal.privilege_disabled", approved.Error?.Code);
            Assert.Equal(0, tool.DispatchCalls);
        }
        var retained = (await fixture.Proposals.FindAsync(proposal.Id))!.PreparedInvocation!;
        Assert.Null(retained.RequiredPrivilegeClasses);
        Assert.Equal(legacy.ApprovalDigest, retained.ApprovalDigest);
        Assert.Equal(legacy.ApprovalDigest, WeaverInvocationIntegrity.PreparedDigest(retained));
        Assert.Null(fixture.Services.GetRequiredService<WeaverRegistrationValidator>().GetEffectsSelection());
    }

    private static Action<IServiceCollection> Configure(IConfigurationRoot configuration, bool effects = true) => services =>
    {
        if (effects) new WeaverEffectsFeature().ConfigureServices(services);
        services.AddSingleton<IConfiguration>(configuration);
    };
    private static IConfigurationRoot Configuration(int mask) => new ConfigurationBuilder().AddInMemoryCollection(
        Enum.GetValues<WeaverPrivilegeClass>().ToDictionary(value => Key(value), value => (string?)((mask & (1 << (int)value)) != 0 ? "true" : "false"))).Build();
    private static string Key(WeaverPrivilegeClass value) => $"{WeaverPrivilegePolicyOptions.SectionName}:{value}";
    private static void Set(IConfigurationRoot configuration, WeaverPrivilegeClass value, bool enabled)
    {
        configuration[Key(value)] = enabled.ToString();
        configuration.Reload();
    }
    private static async Task<WeaverSessionPolicyOutcome> ProbeAsync(ProposalFixture fixture)
    {
        var policy = fixture.Services.GetRequiredService<IWeaverSessionPolicyService>();
        var current = await policy.GetAsync(fixture.Session.Id, fixture.Session.ActorId, fixture.Session.TenantId);
        return (await policy.ChangeAutonomyAsync(new(fixture.Session.Id, fixture.Session.ActorId, fixture.Session.TenantId,
            current.State!.Policy.AutonomyMode, current.State.Revision))).Outcome;
    }
    private sealed class GuardedRegistry(IWeaverTool tool) : IWeaverToolRegistry
    {
        internal bool Refuse { get; set; }
        public Task<IReadOnlyCollection<IWeaverTool>> ListAsync(CancellationToken cancellationToken = default)
            => Refuse ? throw new InvalidOperationException("Registry must not be called by approval.") : Task.FromResult<IReadOnlyCollection<IWeaverTool>>([tool]);
        public Task<IWeaverTool?> FindAsync(string toolName, CancellationToken cancellationToken = default)
            => Refuse ? throw new InvalidOperationException("Registry must not be called by approval.") : Task.FromResult<IWeaverTool?>(toolName == tool.Descriptor.Name ? tool : null);
    }
    private sealed class GuardedPrincipal : IWeaverPrincipalSource
    {
        internal int Calls { get; private set; }
        public ClaimsPrincipal? GetCurrent(string actorId, string tenantId) { Calls++; throw new InvalidOperationException("Approval must not call a principal source."); }
    }
    private sealed class GuardedExecutor : IWeaverActionProposalExecutor
    {
        internal int Calls { get; private set; }
        public Task<WeaverResult<WeaverProposalExecutionResult>> ExecuteAsync(WeaverActionProposal proposal, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("A veto must precede custom executor handoff."); }
        public Task<WeaverResult<WeaverProposalExecutionResult>> CompleteClientAsync(WeaverActionProposal proposal, WeaverClientAcknowledgement acknowledgement,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No client request was issued.");
    }
    private sealed class GuardedObservation : ISchemaDormancyCheck
    {
        internal bool Available { get; set; } = true;
        internal int Calls { get; private set; }
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements)
            => SchemaDormancyRule.Evaluate(requirements, family => Available
                ? new(family, null, ["1.0.0"], "1.0.0", "1.0.0", false, null, [], null, DateTimeOffset.UnixEpoch) : null);
        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Evaluate(requirements));
        }
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Observe());
    }
    private sealed class BorrowedCancellation(IWeaverActionProposalExecutor inner, ProposalMutationTool tool) : IWeaverActionProposalExecutor
    {
        public async Task<WeaverResult<WeaverProposalExecutionResult>> ExecuteAsync(WeaverActionProposal proposal, CancellationToken cancellationToken = default)
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var borrowed = await inner.ExecuteAsync(proposal, cancelled.Token);
            Assert.Equal(DefaultWeaverActionProposalExecutor.CancelledBeforeDispatchCode, borrowed.Error?.Code);
            Assert.True((await tool.ExecuteAsync(proposal.PreparedInvocation!, cancellationToken)).Succeeded);
            return borrowed;
        }
        public Task<WeaverResult<WeaverProposalExecutionResult>> CompleteClientAsync(WeaverActionProposal proposal, WeaverClientAcknowledgement acknowledgement,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("This fixture is server-only.");
    }

    private sealed class ExecutingRecordStore(Action afterExecutingWrite) : IWeaverSessionRecordStore
    {
        private readonly InMemoryWeaverSessionRecordStore _inner = new();
        internal int ExecutingWrites { get; private set; }
        public bool SupportsFencing => _inner.SupportsFencing;
        public Task<WeaverSessionRecordResult> CreateAsync(WeaverSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => _inner.CreateAsync(record, ownerToken, cancellationToken);
        public Task<WeaverSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.LoadAsync(sessionId, cancellationToken);
        public Task<WeaverSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => _inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, WeaverSessionFence fence, CancellationToken cancellationToken = default)
            => _inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public async Task<WeaverSessionRecordResult> WriteAsync(WeaverSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            var result = await _inner.WriteAsync(write, cancellationToken);
            if (write.Proposals.Any(row => row.Proposal.Status == WeaverActionProposalStatus.Executing))
            {
                ExecutingWrites++;
                afterExecutingWrite();
            }
            return result;
        }
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => _inner.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<WeaverSessionSummaryPage> ListOwnedAsync(WeaverSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => _inner.ListOwnedAsync(query, cancellationToken);
    }
}
