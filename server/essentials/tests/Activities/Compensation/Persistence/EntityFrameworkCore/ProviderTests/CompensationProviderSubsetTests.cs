using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Compensation.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// Spec 261 Q4: the compensation provider subset S1–S7 on PostgreSQL in Durable, Balanced and Ephemeral.
/// </summary>
[Collection(CompensationPostgreSqlFixture.CollectionName)]
public sealed class CompensationPostgreSqlProviderTests(CompensationPostgreSqlFixture fixture)
{
    public static TheoryData<string, PersistenceMode> Cases() => CompensationProviderSubset.Cases();

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public Task PostgreSql(string journey, PersistenceMode mode) => CompensationProviderRun.RunAsync(fixture, journey, mode);
}

/// <summary>
/// Spec 261 Q5: the compensation provider subset S1–S7 on SQL Server in Durable, Balanced and Ephemeral.
/// </summary>
[Collection(CompensationSqlServerFixture.CollectionName)]
public sealed class CompensationSqlServerProviderTests(CompensationSqlServerFixture fixture)
{
    public static TheoryData<string, PersistenceMode> Cases() => CompensationProviderSubset.Cases();

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public Task SqlServer(string journey, PersistenceMode mode) => CompensationProviderRun.RunAsync(fixture, journey, mode);
}

/// <summary>
/// Spec 261 Q6: the compensation provider subset S1–S7 on MySQL in Durable, Balanced and Ephemeral.
/// </summary>
[Collection(CompensationMySqlFixture.CollectionName)]
public sealed class CompensationMySqlProviderTests(CompensationMySqlFixture fixture)
{
    public static TheoryData<string, PersistenceMode> Cases() => CompensationProviderSubset.Cases();

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public Task MySql(string journey, PersistenceMode mode) => CompensationProviderRun.RunAsync(fixture, journey, mode);
}

internal static class CompensationProviderRun
{
    public static Task RunAsync(CompensationProviderFixture fixture, string journey, PersistenceMode mode)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"{fixture.Provider} is unavailable.");
        return CompensationProviderSubset.RunAsync(journey, mode,
            async () => CompensationStore.EntityFramework(fixture.Provider, await fixture.CreateEmptyDatabaseAsync()));
    }
}
