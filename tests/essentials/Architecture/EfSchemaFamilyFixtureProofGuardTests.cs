using Xunit;
using static Elsa.Architecture.Tests.EfSchemaFamilyTestFixtures;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Fixtures and FR-022 proofs (spec 180): every upcaster ships a committed fixture pair, a committed fixture is
/// frozen, and every upcaster and every fixture pair is proven by a test class deriving from
/// <c>EfSchemaUpcasterProof&lt;TUpcaster, TValue&gt;</c>, which runs FR-022's three proofs.
/// </summary>
public sealed class EfSchemaFamilyFixtureProofGuardTests
{
    [Fact]
    public void Every_upcaster_ships_a_fixture_pair_and_every_fixture_is_frozen() =>
        AssertNone(
            UpcasterFixtureRules.Violations(UpcasterFixtures, File.ReadAllText(Path.Join(RepoPaths.RepoRoot, FixtureLock)), Production.UpcasterSteps()),
            $"Every upcaster ships a committed fixture pair under Fixtures/SchemaUpcasters/<family>/<from>-to-<to>/, and a " +
            $"fixture is frozen once its version ships: record a new one in {FixtureLock}, never edit or delete one (spec 180, " +
            "FR-022 and FR-024):");

    [Theory]
    [MemberData(nameof(ViolatingFixtureSets))]
    public void Fixture_detector_flags_a_missing_unpaired_edited_unrecorded_or_deleted_fixture(
        string name, string[] paths, string lockText, string[] upcasters, string expected)
    {
        var violations = UpcasterFixtureRules.Violations(
            paths.Select(path => (path, "{}")).ToArray(),
            lockText,
            upcasters.Select(upcaster => upcaster.Split('/')).Select(step => ("Fixture.cs(1)", step[0], step[1], step[2])));

        Assert.True(violations.Any(violation => violation.Contains(expected, StringComparison.Ordinal)),
            $"The fixture detector missed '{name}'. It reported: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Fixture_detector_accepts_a_recorded_pair_for_every_upcaster() =>
        Assert.Empty(UpcasterFixtureRules.Violations(
            [(Pair + "orders.Content.source.json", "{}"), (Pair + "orders.Content.expected.json", "{}")],
            $"{UpcasterFixtureRules.Hash("{}")}  {Pair}orders.Content.expected.json\n{UpcasterFixtureRules.Hash("{}")}  {Pair}orders.Content.source.json\n",
            [("Fixture.cs(1)", "Orders", "1", "2")]));

    /// <summary>The scan must find the committed fixtures, or the frozen rule would pass having checked nothing.</summary>
    [Fact]
    public void Fixture_scan_finds_the_committed_fixtures() =>
        Assert.True(UpcasterFixtures.Count >= 4, $"Expected at least the four synthetic upcaster fixtures; found {UpcasterFixtures.Count}.");

    /// <summary>
    /// FR-022 asks for three proofs per upcaster, not only a fixture pair. They are fixed in
    /// <c>EfSchemaUpcasterProof&lt;TUpcaster, TValue&gt;</c>, so the build needs only to find a concrete class deriving from it
    /// for each upcaster that ships, and for each fixture pair committed, including the synthetic family's.
    /// </summary>
    [Fact]
    public void Every_upcaster_ships_its_three_proofs_and_every_fixture_pair_is_proven() =>
        AssertNone(
            UpcasterProofRules.Violations(
                Production.ShippedUpcasters(),
                Proving.Proofs.Select(proof => (proof.Location, proof.Path, proof.Upcaster, Proving.Resolve(proof.Family) ?? Production.Resolve(proof.Family))).ToArray(),
                Proving.UpcasterVersions(),
                UpcasterFixtureRules.Pairs(UpcasterFixtures.Select(fixture => fixture.Path))),
            "Every upcaster ships FR-022's three proofs - the upcast, the old-format round trip and the read through the store - as " +
            "a test class deriving from EfSchemaUpcasterProof<TUpcaster, TValue>(family, store), and every committed fixture pair " +
            "is proven by one (spec 180, FR-022):");

    /// <summary>The proof rule passes vacuously if the scan stops finding proofs, so it must find the synthetic family's two.</summary>
    [Fact]
    public void Proof_scan_finds_the_synthetic_familys_proofs()
    {
        Assert.Contains(Proving.Proofs, proof => proof.Upcaster == "AddCurrency" && Proving.Resolve(proof.Family) == "SyntheticOrders");
        Assert.Contains(Proving.Proofs, proof => proof.Upcaster == "AddLines" && Proving.Resolve(proof.Family) == "SyntheticOrders");
        Assert.True(UpcasterFixtureRules.Pairs(UpcasterFixtures.Select(fixture => fixture.Path)).Count >= 2, "Expected the synthetic family's two fixture pairs.");
    }

    [Theory]
    [MemberData(nameof(UnprovenUpcasters))]
    public void Proof_detector_flags_an_unproven_upcaster_or_fixture_pair(string name, bool shipped, string? provenUpcaster, string? provenFamily, string expected)
    {
        var violations = UpcasterProofRules.Violations(
            shipped ? [("Upcasters.cs(1)", "Orders", "OneToTwo", "1", "2")] : [],
            provenUpcaster is null ? [] : [("Proof.cs(1)", "Proof.cs", provenUpcaster, provenFamily)],
            [("Upcasters.cs", "OneToTwo", "1", "2")],
            [(Pair.TrimEnd('/'), "Orders", "1", "2")]);

        Assert.True(violations.Any(violation => violation.Contains(expected, StringComparison.Ordinal)),
            $"The proof detector missed '{name}'. It reported: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Proof_detector_accepts_an_upcaster_proven_for_its_family() =>
        Assert.Empty(UpcasterProofRules.Violations(
            [("Upcasters.cs(1)", "Orders", "OneToTwo", "1", "2")],
            [("Proof.cs(1)", "Proof.cs", "OneToTwo", "Orders")],
            [("Upcasters.cs", "OneToTwo", "1", "2")],
            [(Pair.TrimEnd('/'), "Orders", "1", "2")]));

    public static TheoryData<string, bool, string?, string?, string> UnprovenUpcasters() => new()
    {
        { "an upcaster with no proof class", true, null, null, "ships without FR-022's proofs" },
        { "a proof naming another family", true, "OneToTwo", "Invoices", "ships without FR-022's proofs" },
        { "a fixture pair no proof proves", false, null, null, "no EfSchemaUpcasterProof proves the 'Orders' fixture pair from '1' to '2'" },
        { "a proof whose family cannot be resolved", false, "OneToTwo", null, "for a family this guard cannot resolve" },
        { "a proof of a type that is no upcaster", false, "Helper", "Orders", "cannot resolve to one type carrying [EfSchemaUpcaster(from, to)]" }
    };

    /// <summary>
    /// The scan reads a proof's upcaster and family from its base, however it passes them, and skips an abstract class,
    /// whose proofs run only through a concrete one the rule must see.
    /// </summary>
    [Fact]
    public void Proof_scan_reads_the_upcaster_and_family_a_proof_class_names()
    {
        var scan = Scan(
            """
            public static class Orders { public const string SchemaFamily = "Orders"; }

            public sealed class Primary() : Tests.EfSchemaUpcasterProof<Sales.OneToTwo, Order>(Orders.SchemaFamily, new Store());

            public sealed class Explicit : EfSchemaUpcasterProof<TwoToThree, Order>
            {
                public Explicit() : base("Orders", new Store()) { }
            }

            public abstract class Shared : EfSchemaUpcasterProof<ThreeToFour, Order>;
            """);

        Assert.Equal(
            [("OneToTwo", (string?)"Orders"), ("TwoToThree", "Orders")],
            scan.Proofs.Select(proof => (proof.Upcaster, scan.Resolve(proof.Family))).ToArray());
    }

    public static TheoryData<string, string[], string, string[], string> ViolatingFixtureSets() => new()
    {
        { "an upcaster with no fixture pair", [], "", ["Orders/1/2"], "'Orders' upcaster from '1' to '2' ships no fixture pair" },
        { "a source fixture without its expected fixture", [Pair + "orders.Content.source.json"], "", [], "has no matching expected fixture" },
        { "a fixture named outside the convention", [Pair + "orders.json"], "", [], "is not named <table>.<column>.source.<ext> or .expected.<ext>" },
        { "a committed fixture not recorded in the lock", [Pair + "orders.Content.source.json", Pair + "orders.Content.expected.json"], "", [], "is not recorded" },
        {
            "an edited fixture",
            [Pair + "orders.Content.source.json", Pair + "orders.Content.expected.json"],
            $"0000  {Pair}orders.Content.source.json\n{UpcasterFixtureRules.Hash("{}")}  {Pair}orders.Content.expected.json\n",
            [],
            "was edited"
        },
        {
            "a recorded fixture that was deleted",
            [],
            $"{UpcasterFixtureRules.Hash("{}")}  {Pair}orders.Content.source.json\n",
            [],
            "was deleted"
        }
    };
}
