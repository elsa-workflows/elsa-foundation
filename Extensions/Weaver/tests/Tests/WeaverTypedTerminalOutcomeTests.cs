using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class WeaverTypedTerminalOutcomeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unclassed_operation_keeps_the_canonical_missing_owner_failure_when_its_store_admits_effects(bool effects)
    {
        var tool = new ProposalMutationTool();
        var registry = new RemovableRegistry(tool);
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            if (effects) new WeaverEffectsFeature().ConfigureServices(services);
            services.Replace(ServiceDescriptor.Scoped<IWeaverToolRegistry>(_ => registry));
        });
        var proposal = await fixture.ProposeAsync();
        Assert.Null(proposal.PreparedInvocation!.RequiredPrivilegeClasses);
        var digest = proposal.PreparedInvocation.ApprovalDigest;
        await fixture.ApproveAsync(proposal);
        registry.Removed = true;
        var refused = await fixture.ExecuteAsync(proposal);
        Assert.NotNull(refused.Error);
        var retained = await fixture.Proposals.FindResultAsync(proposal.Id);
        Assert.NotNull(retained);
        Assert.False(retained.Succeeded);
        Assert.Null(retained.Receipt);
        if (effects) Assert.Equal(refused.Error, retained.Error);
        else Assert.Null(retained.Error);
        Assert.Equal(refused.Error, (await fixture.ExecuteAsync(proposal)).Error);
        Assert.Equal(digest, (await fixture.Proposals.FindAsync(proposal.Id))!.PreparedInvocation!.ApprovalDigest);
        Assert.Equal(0, tool.DispatchCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expiry_records_typed_reason_only_with_admitted_effects_and_never_changes_the_legacy_preparation(bool effects)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), configure: services =>
        {
            if (effects) new WeaverEffectsFeature().ConfigureServices(services);
        });
        var proposal = await fixture.ProposeAsync();
        var digest = proposal.PreparedInvocation!.ApprovalDigest;
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var expired = await fixture.ApproveAsync(proposal);
        Assert.Equal("weaver.proposal.expired", expired.Error?.Code);
        var result = await fixture.Proposals.FindResultAsync(proposal.Id);
        if (effects)
        {
            Assert.Equal(expired.Error, result!.Error);
            Assert.False(result.Succeeded);
            Assert.Null(result.Receipt);
            Assert.Equal("refused", result.Payload!.Data.GetProperty("status").GetString());
        }
        else Assert.Null(result);
        Assert.Equal(expired.Error, (await fixture.ExecuteAsync(proposal)).Error);
        Assert.Equal(digest, (await fixture.Proposals.FindAsync(proposal.Id))!.PreparedInvocation!.ApprovalDigest);
        Assert.Equal(0, fixture.Mutation!.DispatchCalls);
    }

    private sealed class RemovableRegistry(IWeaverTool tool) : IWeaverToolRegistry
    {
        internal bool Removed;
        public Task<IReadOnlyCollection<IWeaverTool>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<IWeaverTool>>(Removed ? [] : [tool]);
        public Task<IWeaverTool?> FindAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(!Removed && name == tool.Descriptor.Name ? tool : null);
    }
}
