using Elsa.Persistence.Groundwork.DesignConformance.Tests;

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
