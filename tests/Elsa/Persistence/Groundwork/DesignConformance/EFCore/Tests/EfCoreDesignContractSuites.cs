using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.EFCore.Tests;

public sealed class EfCoreWorkflowDesignContractSuite : WorkflowDesignContractSuite
{
    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
        await EfCoreDesignPersistenceContractFixture.CreateAsync(cancellationToken);
}

public sealed class EfCoreActivityDesignContractSuite : ActivityDesignContractSuite
{
    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
        await EfCoreDesignPersistenceContractFixture.CreateAsync(cancellationToken);
}

public sealed class EfCoreAtomicityContractSuite : DesignAtomicityContractSuite
{
    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.LegacyEfOracle;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
        await EfCoreDesignPersistenceContractFixture.CreateAsync(cancellationToken);
}

public sealed class EfCoreIsolationAndRestartContractSuite : DesignIsolationAndRestartContractSuite
{
    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.LegacyEfOracle;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
        await EfCoreDesignPersistenceContractFixture.CreateAsync(cancellationToken);
}

public sealed class EfCoreAtomicityCancellationEvidenceTests
{
    [Fact]
    public async Task Cancel_probe_flows_one_cancelled_token_through_public_command_EF_and_exception()
    {
        await using var fixture = await EfCoreDesignPersistenceContractFixture.CreateAsync();
        await using var fault = await fixture.ArmAtomicityFaultAsync(new(
            DesignAtomicityFaultPhase.BeforeProviderDecision,
            DesignAtomicityFaultAction.Cancel));
        var request = new DesignAtomicityOperationRequest(
            DesignPersistenceFixtureData.ScopeA,
            new DesignAtomicityOperationKey("ef-cancellation-token-flow"),
            new DesignCanonicalRequestFingerprint("ef-cancellation-token-flow-v1"));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.ExecuteAtomicityOperationAsync(request));
        var evidence = fixture.CancellationProbeEvidence
            ?? throw new InvalidOperationException("The EF interceptor did not capture cancellation-token evidence.");

        Assert.Equal(evidence.CommandToken, evidence.EventHandlerToken);
        Assert.Equal(evidence.CommandToken, evidence.ProviderToken);
        Assert.Equal(evidence.CommandToken, exception.CancellationToken);
        Assert.True(evidence.CommandToken.IsCancellationRequested);
        Assert.True(evidence.EventHandlerToken.IsCancellationRequested);
        Assert.True(evidence.ProviderToken.IsCancellationRequested);
        Assert.True(exception.CancellationToken.IsCancellationRequested);
        Assert.True(fault.WasTriggered);
    }
}
