using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests.Differential;

/// <summary>
/// The #646 EF-vs-Groundwork behavioural differential for <c>IStructuredLogStore</c> on SQLite.
/// </summary>
/// <remarks>
/// <para>
/// Each dimension drives one identical operation sequence against both stacks and compares the
/// normalized observations. A divergence is not automatically a Groundwork defect — sometimes the EF
/// behaviour was the accident — so divergence does not fail this suite on its own. What fails is an
/// <em>unrecorded</em> divergence: every known one must carry a disposition in
/// <see cref="StructuredLogDivergenceLedger"/>, mirrored into
/// <c>specs/094-harden-groundwork-stores/divergence-ledger.md</c>.
/// </para>
/// <para>
/// This supplies the correctness precondition #646 needs before timing is valid. It is not a
/// performance verdict and advances no coverage-ledger row: SQLite alone cannot satisfy the
/// four-provider evidence requirement that gates <c>evidence-complete</c>.
/// </para>
/// </remarks>
public sealed class StructuredLogStoreDifferentialTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(nameof(StructuredLogDimension.ConcurrencyConflictShape))]
    [InlineData(nameof(StructuredLogDimension.ProducerOrdering))]
    [InlineData(nameof(StructuredLogDimension.NullAndDefaultMaterialization))]
    [InlineData(nameof(StructuredLogDimension.RollbackVisibility))]
    [InlineData(nameof(StructuredLogDimension.RestartObservation))]
    [InlineData(nameof(StructuredLogDimension.IdempotentReplay))]
    public async Task Ef_and_groundwork_agree_or_carry_a_recorded_disposition(string dimensionName)
    {
        var dimension = Enum.Parse<StructuredLogDimension>(dimensionName);
        var probe = StructuredLogDifferentialProbes.For(dimension);

        await using var efTarget = StructuredLogDifferentialTarget.EfCore();
        await using var groundworkTarget = StructuredLogDifferentialTarget.Groundwork();

        // Sequential on purpose: a shared temp directory and two SQLite files are cheap, but running the
        // stacks concurrently would let one stack's drain scheduling perturb the other's timings.
        var ef = await probe(efTarget);
        var groundwork = await probe(groundworkTarget);

        output.WriteLine($"dimension     : {dimension}");
        output.WriteLine($"efcore.sqlite : {ef.Canonical}");
        output.WriteLine($"groundwork    : {groundwork.Canonical}");

        // Pin the comparison's width first: a probe that quietly stops observing a fact would otherwise
        // report agreement it never actually tested.
        Assert.Equal(
            StructuredLogDivergenceLedger.ComparedFactsFor(dimension).OrderBy(f => f, StringComparer.Ordinal),
            ef.Facts.Keys.Union(groundwork.Facts.Keys, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal));

        var divergentFacts = DivergentFacts(ef, groundwork);
        var recorded = StructuredLogDivergenceLedger.RecordedDivergencesFor(dimension);
        var unrecorded = divergentFacts.Except(recorded, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var absent = recorded.Except(divergentFacts, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToArray();

        Assert.True(
            unrecorded.Length == 0,
            $"""
             {dimension} diverged on facts with no recorded disposition: {string.Join(", ", unrecorded)}.
             A divergence is not automatically a Groundwork bug. Decide whether Groundwork's behaviour is
             the contract (ContractIsGroundwork), EF's was (ContractIsEf), or it is unresolved (Undecided),
             then record it in StructuredLogDivergenceLedger and mirror the row into
             specs/094-harden-groundwork-stores/divergence-ledger.md.
               efcore.sqlite : {ef.Canonical}
               groundwork    : {groundwork.Canonical}
             """);

        Assert.True(
            absent.Length == 0,
            $"""
             {dimension} no longer diverges on: {string.Join(", ", absent)}.
             The ledger claims a divergence that the stores no longer exhibit. Remove the stale row so the
             ledger cannot over-report risk to spec 144's T011.
               efcore.sqlite : {ef.Canonical}
               groundwork    : {groundwork.Canonical}
             """);
    }

    /// <summary>
    /// Pins the evidence identity so a silent change to a probe cannot quietly restate what was compared.
    /// The digest binds dimension and fact names only — never observed values, which are the finding.
    /// </summary>
    [Fact]
    public void Differential_surface_matches_its_recorded_identity()
    {
        var surface = StructuredLogDivergenceLedger.Dimensions.Select(d => new
        {
            dimension = d.ToString(),
            compared = StructuredLogDivergenceLedger.ComparedFactsFor(d).OrderBy(f => f, StringComparer.Ordinal).ToArray(),
            divergences = StructuredLogDivergenceLedger.RecordedDivergencesFor(d).OrderBy(f => f, StringComparer.Ordinal).ToArray()
        });

        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(surface)))).ToLowerInvariant();

        output.WriteLine($"differential surface digest: {digest}");
        Assert.Equal(StructuredLogDivergenceLedger.SurfaceDigest, digest);
    }

    private static IReadOnlyCollection<string> DivergentFacts(StructuredLogObservation ef, StructuredLogObservation groundwork)
    {
        var keys = ef.Facts.Keys.Union(groundwork.Facts.Keys, StringComparer.Ordinal);

        return keys
            .Where(key => !string.Equals(Fact(ef, key), Fact(groundwork, key), StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        static string Fact(StructuredLogObservation observation, string key) =>
            observation.Facts.TryGetValue(key, out var value) ? value : "<absent>";
    }
}
