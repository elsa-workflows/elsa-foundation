using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests.Differential;

/// <summary>
/// The #646 EF-vs-Groundwork behavioural differential for <c>IOpenTelemetryStore</c> on SQLite.
/// </summary>
/// <remarks>
/// <para>
/// Same contract as the structured-log seam: a divergence does not fail the suite on its own, because
/// sometimes the EF behaviour was the accident. What fails is an <em>unrecorded</em> divergence, or a
/// recorded one the stores no longer exhibit.
/// </para>
/// <para>
/// This seam carries the concurrency dimension the append-only log stream cannot express: the resource
/// and instrument catalogs are mutable and upserted, so last-writer-wins is genuinely contested here.
/// </para>
/// </remarks>
public sealed class OpenTelemetryStoreDifferentialTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(nameof(OpenTelemetryDimension.ConcurrencyConflictShape))]
    [InlineData(nameof(OpenTelemetryDimension.ProducerOrdering))]
    [InlineData(nameof(OpenTelemetryDimension.NullAndDefaultMaterialization))]
    [InlineData(nameof(OpenTelemetryDimension.RollbackVisibility))]
    [InlineData(nameof(OpenTelemetryDimension.RestartObservation))]
    [InlineData(nameof(OpenTelemetryDimension.IdempotentReplay))]
    public async Task Ef_and_groundwork_agree_or_carry_a_recorded_disposition(string dimensionName)
    {
        var dimension = Enum.Parse<OpenTelemetryDimension>(dimensionName);
        var probe = OpenTelemetryDifferentialProbes.For(dimension);

        await using var efTarget = OpenTelemetryDifferentialTarget.EfCore();
        await using var groundworkTarget = OpenTelemetryDifferentialTarget.Groundwork();

        var ef = await probe(efTarget);
        var groundwork = await probe(groundworkTarget);

        output.WriteLine($"dimension     : {dimension}");
        output.WriteLine($"efcore.sqlite : {ef.Canonical}");
        output.WriteLine($"groundwork    : {groundwork.Canonical}");

        // Pin the comparison's width first: a probe that quietly stops observing a fact would otherwise
        // report agreement it never actually tested.
        Assert.Equal(
            OpenTelemetryDivergenceLedger.ComparedFactsFor(dimension).OrderBy(f => f, StringComparer.Ordinal),
            ef.Facts.Keys.Union(groundwork.Facts.Keys, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal));

        var divergentFacts = DivergentFacts(ef, groundwork);
        var recorded = OpenTelemetryDivergenceLedger.RecordedDivergencesFor(dimension);
        var unrecorded = divergentFacts.Except(recorded, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var absent = recorded.Except(divergentFacts, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToArray();

        Assert.True(
            unrecorded.Length == 0,
            $"""
             {dimension} diverged on facts with no recorded disposition: {string.Join(", ", unrecorded)}.
             A divergence is not automatically a Groundwork bug. Decide whether Groundwork's behaviour is
             the contract (ContractIsGroundwork), EF's was (ContractIsEf), or it is unresolved (Undecided),
             then record it in OpenTelemetryDivergenceLedger and mirror the row into
             specs/094-harden-groundwork-stores/divergence-ledger.md.
               efcore.sqlite : {ef.Canonical}
               groundwork    : {groundwork.Canonical}
             """);

        Assert.True(
            absent.Length == 0,
            $"""
             {dimension} no longer diverges on: {string.Join(", ", absent)}.
             The ledger claims a divergence the stores no longer exhibit. Remove the stale row so it
             cannot over-report risk to spec 144's T011.
               efcore.sqlite : {ef.Canonical}
               groundwork    : {groundwork.Canonical}
             """);
    }

    /// <summary>Pins the evidence identity so a probe or ledger edit cannot silently restate the comparison.</summary>
    [Fact]
    public void Differential_surface_matches_its_recorded_identity()
    {
        var surface = OpenTelemetryDivergenceLedger.Dimensions.Select(d => new
        {
            dimension = d.ToString(),
            compared = OpenTelemetryDivergenceLedger.ComparedFactsFor(d).OrderBy(f => f, StringComparer.Ordinal).ToArray(),
            divergences = OpenTelemetryDivergenceLedger.RecordedDivergencesFor(d).OrderBy(f => f, StringComparer.Ordinal).ToArray()
        });

        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(surface)))).ToLowerInvariant();

        output.WriteLine($"differential surface digest: {digest}");
        Assert.Equal(OpenTelemetryDivergenceLedger.SurfaceDigest, digest);
    }

    private static IReadOnlyCollection<string> DivergentFacts(OpenTelemetryObservation ef, OpenTelemetryObservation groundwork)
    {
        var keys = ef.Facts.Keys.Union(groundwork.Facts.Keys, StringComparer.Ordinal);

        return keys
            .Where(key => !string.Equals(Fact(ef, key), Fact(groundwork, key), StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        static string Fact(OpenTelemetryObservation observation, string key) =>
            observation.Facts.TryGetValue(key, out var value) ? value : "<absent>";
    }
}
