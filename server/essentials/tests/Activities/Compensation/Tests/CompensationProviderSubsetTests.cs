using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// The spec 261 provider subset S1–S7 on SQLite, in every persistence mode, on every pull request. The nightly
/// <c>compensation</c> EF suite runs the same <see cref="CompensationProviderSubset"/> on PostgreSQL, SQL Server and MySQL,
/// so a journey that drifts here fails before it reaches the server providers.
/// </summary>
public sealed class CompensationProviderSubsetTests
{
    public static TheoryData<string, PersistenceMode> Cases() => CompensationProviderSubset.Cases();

    [Theory]
    [MemberData(nameof(Cases))]
    public Task The_provider_subset_passes_on_SQLite(string journey, PersistenceMode mode) =>
        CompensationProviderSubset.RunAsync(journey, mode, () => Task.FromResult(CompensationStore.Sqlite()));
}
