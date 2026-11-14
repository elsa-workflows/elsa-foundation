using System.Text.Json;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

public sealed class WeaverTypedResultAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_declaring_refusal_rechecks_admission_after_the_awaited_module_callback(bool duringExecution)
    {
        var observation = new Observation();
        var tool = new RefusalTool(() => observation.Available = false, duringExecution);
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services => Configure(services, observation));
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(WeaverToolDispatchOutcome.Denied, dispatch.Outcome);
        Assert.Equal("schema-version-not-finalized", dispatch.Error?.Code);
        Assert.Null(dispatch.Result);
        Assert.Null(dispatch.Proposal);
        Assert.Equal(1, tool.Preparations);
        Assert.Equal(duringExecution ? 1 : 0, tool.Executions);
        Assert.True(observation.AsyncCalls >= 2);
    }

    [Fact]
    public async Task Early_refusal_wraps_a_throwing_observer_at_the_public_invoker_boundary()
    {
        var observation = new Observation { RefreshThrows = true };
        var tool = new RefusalTool(() => { }, false);
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services => Configure(services, observation));
        var exception = await Assert.ThrowsAsync<WeaverContributionException>(() => fixture.Invoker.DispatchAsync(
            fixture.Invocation() with { TurnId = "" }, fixture.Session.Policy));
        Assert.IsType<IOException>(exception.InnerException);
        Assert.Equal(1, observation.AsyncCalls);
        Assert.Equal(0, tool.Preparations);
        Assert.Equal(0, tool.Executions);
    }

    [Fact]
    public async Task Stop_bounds_early_refusal_even_when_the_observer_ignores_cancellation()
    {
        var observation = new Observation { RefreshStalls = true };
        var tool = new RefusalTool(() => { }, false);
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services => Configure(services, observation));
        using var cancellation = new CancellationTokenSource();
        var dispatch = fixture.Invoker.DispatchAsync(fixture.Invocation() with { TurnId = "" }, fixture.Session.Policy, cancellation.Token);
        try
        {
            await observation.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, tool.Preparations);
            Assert.Equal(0, tool.Executions);
        }
        finally
        {
            observation.Release.TrySetException(new IOException("Deliberate late observation failure."));
            try { await dispatch.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) { /* The original assertion, not cleanup, is the verdict. */ }
        }
    }

    [Theory]
    [InlineData("refresh-throws", false)]
    [InlineData("refresh-stalls", false)]
    [InlineData("snapshot-unavailable", false)]
    [InlineData("snapshot-throws", false)]
    [InlineData("refresh-throws", true)]
    [InlineData("refresh-stalls", true)]
    [InlineData("snapshot-unavailable", true)]
    [InlineData("snapshot-throws", true)]
    public async Task Optional_error_enrichment_cannot_prevent_a_core_veto_or_legacy_expiry(string failure, bool expired)
    {
        var observation = new Observation();
        var tool = new ProposalMutationTool
        {
            // The expiry branch deliberately uses an old unclassed preparation.
            PrivilegeClasses = expired ? null : [WeaverPrivilegeClass.Activation]
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            Enum.GetValues<WeaverPrivilegeClass>().ToDictionary(value => $"{WeaverPrivilegePolicyOptions.SectionName}:{value}", _ => (string?)"true")).Build();
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            Configure(services, observation);
            services.AddSingleton<IConfiguration>(configuration);
        });
        var proposal = await fixture.ProposeAsync();
        var observedCalls = observation.AsyncCalls;
        if (expired) Assert.Equal(0, observedCalls); // Old unclassed preparation never needed the effects observer.
        else Assert.True(observedCalls > 0);
        observation.RefreshThrows = failure == "refresh-throws";
        observation.RefreshStalls = failure == "refresh-stalls";
        observation.Available = failure != "snapshot-unavailable";
        observation.SnapshotThrows = failure == "snapshot-throws";
        if (expired) fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        else
        {
            configuration[$"{WeaverPrivilegePolicyOptions.SectionName}:Activation"] = "false";
            configuration.Reload();
        }
        var decision = fixture.ApproveAsync(proposal);
        try
        {
            var refused = await decision.WaitAsync(TimeSpan.FromSeconds(5));
            var code = expired ? "weaver.proposal.expired" : "weaver.proposal.privilege_disabled";
            Assert.Equal(code, refused.Error?.Code);
            Assert.Equal(expired ? WeaverActionProposalStatus.Expired : WeaverActionProposalStatus.Failed,
                (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
            Assert.Equal(observedCalls, observation.AsyncCalls); // No refresh, not even one that promptly throws.
            Assert.Equal(0, tool.DispatchCalls);
            var result = await fixture.Proposals.FindResultAsync(proposal.Id);
            if (observation.Available && !observation.SnapshotThrows) Assert.Equal(code, result?.Error?.Code);
            else Assert.Null(result?.Error); // Unknown optional representation keeps truthful old core state.
            var audit = Assert.Single(await fixture.Audit.ListAsync(fixture.Session.Id), item =>
                item.Kind == WeaverAuditEventKind.ProposalFailed && item.Metadata.GetValueOrDefault("proposalId") == proposal.Id);
            Assert.Equal("none", audit.Metadata.GetValueOrDefault("dispatch"));
            Assert.Equal("none", audit.Metadata.GetValueOrDefault("effect"));
            Assert.Equal(0, tool.ExecuteCalls);
            Assert.False((await fixture.ApproveAsync(proposal)).Succeeded);
        }
        finally
        {
            observation.Release.TrySetResult(observation.Current());
            try { await decision.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) { /* Release a stalled pre-fix control without masking its failed assertion. */ }
        }
    }

    private static void Configure(IServiceCollection services, Observation observation)
    {
        new WeaverEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
        services.AddScoped<ISchemaDormancyCheck>(_ => observation);
    }

    private sealed class Observation : ISchemaDormancyCheck
    {
        internal bool Available = true, SnapshotThrows, RefreshThrows, RefreshStalls;
        internal int AsyncCalls;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<SchemaAvailability> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SchemaAvailability Current() => SchemaDormancyRule.Evaluate([new("effects.fixture", "1.0.0")], family => Available
            ? new(family, null, ["1.0.0"], "1.0.0", "1.0.0", false, null, [], null, DateTimeOffset.UnixEpoch) : null);
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements)
        {
            if (SnapshotThrows) throw new IOException("Deliberate cached-observation failure.");
            return SchemaDormancyRule.Evaluate(requirements, family => Available
                ? new(family, null, ["1.0.0"], "1.0.0", "1.0.0", false, null, [], null, DateTimeOffset.UnixEpoch) : null);
        }
        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
        {
            AsyncCalls++;
            Entered.TrySetResult();
            if (RefreshThrows) throw new IOException("Deliberate asynchronous-observation failure.");
            if (RefreshStalls) return new(Release.Task); // Deliberately ignores the caller token.
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Evaluate(requirements));
        }
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<SchemaFamilyObservation>>([]);
    }

    private sealed class RefusalTool(Action loseObservation, bool duringExecution) : IWeaverTool
    {
        internal int Preparations, Executions;
        public WeaverToolDescriptor Descriptor { get; } = new("typed_refusal", "Typed refusal", "A controlled module refusal.",
            WeaverToolMutability.ReadOnly, WeaverRisk.ReadOnly, "{}", [], [])
        { ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId, ModuleCodes = ["test.refused"] };
        public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Preparations++;
            await Task.Yield();
            if (duringExecution) return WeaverResult<WeaverToolPreparation>.Success(new(null, JsonSerializer.SerializeToElement(new { }),
                WeaverRisk.ReadOnly, new("test.preview", "1", JsonSerializer.SerializeToElement(new { })), false));
            loseObservation();
            return WeaverResult<WeaverToolPreparation>.Failure("test.refused", "Module refused.", 409);
        }
        public async Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            await Task.Yield();
            loseObservation();
            return WeaverResult<WeaverToolResult>.Failure("test.refused", "Module refused.", 409);
        }
    }
}
