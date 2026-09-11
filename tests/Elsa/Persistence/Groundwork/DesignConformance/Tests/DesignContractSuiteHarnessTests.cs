using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Every shipped provider runs the Target profile, so only this harness can show that a non-applicable
/// profile row skips before the suite asks for a fixture.
/// </summary>
public class DesignContractSuiteHarnessTests
{
    [SkippableFact]
    public async Task Non_applicable_atomicity_row_skips_before_fixture_creation()
    {
        var suite = new NonApplicableAtomicityContractSuite();

        await suite.Lost_acknowledgement_after_durable_decision_reconciles_the_authoritative_result_on_retry();
    }

    private sealed class NonApplicableAtomicityContractSuite : DesignAtomicityContractSuite
    {
        protected override DesignPersistenceContractProfile ContractProfile { get; } = new(
            "harness-non-applicable",
            Enum.GetValues<DesignPersistenceContractScenario>().ToDictionary(
                scenario => scenario,
                scenario => scenario == DesignPersistenceContractScenario.AtomicityLostAcknowledgement
                    ? DesignPersistenceScenarioApplicability.NotApplicable("Harness: this row is intentionally not applicable.")
                    : DesignPersistenceScenarioApplicability.Applicable()));

        protected override Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A non-applicable row must skip before it creates a fixture.");
    }
}
