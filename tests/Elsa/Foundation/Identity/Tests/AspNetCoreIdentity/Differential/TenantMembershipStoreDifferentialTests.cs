using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity.Differential;

/// <summary>
/// The #646 EF-vs-Groundwork behavioural differential for <c>ITenantMembershipStore</c> on SQLite.
/// </summary>
/// <remarks>
/// <para>
/// This is the only Elsa IAM contract the differential may currently touch. <c>iam-user</c>,
/// <c>iam-role</c> and <c>iam-external-identity</c> are <c>externally-blocked</c> in the coverage
/// ledger and the validator has no transition out of that state; the ASP.NET Core Identity oracle tree
/// is separately frozen under a content fingerprint.
/// </para>
/// <para>
/// Shares the SQLite identity collection because the EF registration hard-codes one data source, so
/// these tests must not run beside another test using the same database file.
/// </para>
/// </remarks>
[Collection(SqliteIdentityFileCollection.Name)]
public sealed class TenantMembershipStoreDifferentialTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(nameof(TenantMembershipDimension.ConcurrencyConflictShape))]
    [InlineData(nameof(TenantMembershipDimension.ProducerOrdering))]
    [InlineData(nameof(TenantMembershipDimension.NullAndDefaultMaterialization))]
    [InlineData(nameof(TenantMembershipDimension.RollbackVisibility))]
    [InlineData(nameof(TenantMembershipDimension.RestartObservation))]
    [InlineData(nameof(TenantMembershipDimension.IdempotentReplay))]
    [Trait("Category", "Sqlite")]
    public async Task Ef_and_groundwork_agree_or_carry_a_recorded_disposition(string dimensionName)
    {
        var dimension = Enum.Parse<TenantMembershipDimension>(dimensionName);
        var probe = TenantMembershipDifferentialProbes.For(dimension);

        await using var efTarget = TenantMembershipDifferentialTarget.EfCore();
        await using var groundworkTarget = TenantMembershipDifferentialTarget.Groundwork();

        var ef = await probe(efTarget);
        var groundwork = await probe(groundworkTarget);

        output.WriteLine($"dimension     : {dimension}");
        output.WriteLine($"efcore.sqlite : {ef.Canonical}");
        output.WriteLine($"groundwork    : {groundwork.Canonical}");

        Assert.Equal(
            TenantMembershipDivergenceLedger.ComparedFactsFor(dimension).OrderBy(f => f, StringComparer.Ordinal),
            ef.Facts.Keys.Union(groundwork.Facts.Keys, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal));

        var divergentFacts = DivergentFacts(ef, groundwork);
        var recorded = TenantMembershipDivergenceLedger.RecordedDivergencesFor(dimension);
        var unrecorded = divergentFacts.Except(recorded, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var absent = recorded.Except(divergentFacts, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToArray();

        Assert.True(
            unrecorded.Length == 0,
            $"""
             {dimension} diverged on facts with no recorded disposition: {string.Join(", ", unrecorded)}.
             A divergence is not automatically a Groundwork bug. Decide whether Groundwork's behaviour is
             the contract (ContractIsGroundwork), EF's was (ContractIsEf), or it is unresolved (Undecided),
             then record it in TenantMembershipDivergenceLedger and mirror the row into
             specs/094-harden-groundwork-stores/divergence-ledger.md.
               efcore.sqlite : {ef.Canonical}
               groundwork    : {groundwork.Canonical}
             """);

        Assert.True(
            absent.Length == 0,
            $"""
             {dimension} no longer diverges on: {string.Join(", ", absent)}.
             Remove the stale ledger row so it cannot over-report risk to spec 144's T011.
               efcore.sqlite : {ef.Canonical}
               groundwork    : {groundwork.Canonical}
             """);
    }

    [Fact]
    public void Differential_surface_matches_its_recorded_identity()
    {
        var surface = TenantMembershipDivergenceLedger.Dimensions.Select(d => new
        {
            dimension = d.ToString(),
            compared = TenantMembershipDivergenceLedger.ComparedFactsFor(d).OrderBy(f => f, StringComparer.Ordinal).ToArray(),
            divergences = TenantMembershipDivergenceLedger.RecordedDivergencesFor(d).OrderBy(f => f, StringComparer.Ordinal).ToArray()
        });

        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(surface)))).ToLowerInvariant();

        output.WriteLine($"differential surface digest: {digest}");
        Assert.Equal(TenantMembershipDivergenceLedger.SurfaceDigest, digest);
    }

    private static IReadOnlyCollection<string> DivergentFacts(TenantMembershipObservation ef, TenantMembershipObservation groundwork)
    {
        var keys = ef.Facts.Keys.Union(groundwork.Facts.Keys, StringComparer.Ordinal);

        return keys
            .Where(key => !string.Equals(Fact(ef, key), Fact(groundwork, key), StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        static string Fact(TenantMembershipObservation observation, string key) =>
            observation.Facts.TryGetValue(key, out var value) ? value : "<absent>";
    }
}

/// <summary>
/// Serializes every test that binds the EF identity registration's fixed SQLite data source. The
/// registration hard-codes one file, so parallel classes would delete each other's database mid-run.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqliteIdentityFileCollection
{
    public const string Name = "sqlite-identity-file";
}
