using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

[Collection(DiagnosticsProviderCollection.Name)]
public sealed class DiagnosticsProviderLifecycleSmokeTests(DiagnosticsProviderFixture fixture)
{
    [Fact]
    public async Task Real_fixture_creates_isolated_ready_leases_for_all_four_providers()
    {
        foreach (var provider in Enum.GetValues<DiagnosticsProviderKind>())
        {
            var lease = await fixture.CreateIsolatedAsync(provider);
            var storageCapability = provider == DiagnosticsProviderKind.MongoDb
                ? DiagnosticsProviderCapability.Document
                : DiagnosticsProviderCapability.Relational;

            DiagnosticsProviderAssertions.IsReady(
                lease,
                storageCapability,
                DiagnosticsProviderCapability.Transactions,
                DiagnosticsProviderCapability.BoundedQueries,
                DiagnosticsProviderCapability.ExecutionPlans);
        }
    }
}
