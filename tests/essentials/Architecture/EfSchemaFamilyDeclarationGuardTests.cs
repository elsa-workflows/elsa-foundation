using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// A family's <c>[EfSchemaFamily]</c> declaration (spec 180, FR-001) is the one source of what a build reads and writes
/// for it: a host's readability report is derived from it alone (spec 183, FR-020), and every store reads through the
/// chain resolved from it. This guard holds the tree to that, over every production source under <c>src/</c>:
/// <list type="bullet">
/// <item>every family a check names, and every <c>SchemaFamily</c> constant, is declared exactly once, and every check
/// names its family through the family's one chain handle, never a string (FR-002);</item>
/// <item>every family's module class holds that handle, resolved from its own assembly and its own family;</item>
/// <item>every declared chain is sound: no gap, duplicate, branch or cycle, and it ends at the current version (FR-005);</item>
/// <item>every upcaster belongs to exactly one declared chain (FR-003);</item>
/// <item>a family EF materializes directly, through <c>IEfSchemaVersionedContext</c>, declares no upcasters, since its
/// content is deserialized before any upcaster could run;</item>
/// <item>every write that stamps a constant stamps the version its family's declaration names current (FR-013);</item>
/// <item>every write that rewrites a row's content in place stamps the row again, so its stamp always describes its
/// content: a row read at an older version and written back is upgraded, never left claiming the old version over
/// content now in the current format (FR-014);</item>
/// <item>every upcaster ships a committed fixture pair, and a committed fixture is frozen: editing, deleting or adding
/// one without recording it in <c>Baselines/schema-upcaster-fixtures.sha256</c> fails the build (FR-022, SC-005).</item>
/// </list>
/// </summary>
/// <remarks>
/// It reads syntax only, and resolves a family or version when it is a string literal or a <c>Type.Constant</c> whose
/// <c>const string</c> initializer is a literal. The version rule this guard had before the chain landed, which held a
/// check's version to its declaration, is gone: a check states no version any more, it reads the family's chain.
/// </remarks>
public sealed class EfSchemaFamilyDeclarationGuardTests
{
    private static SchemaFamilyScan Production { get; } = SchemaFamilyScan.Of(
        Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !HasSegment(RepoRoot, file, "Migrations"))
            .Select(file => (Path: Path.GetRelativePath(RepoRoot, file), Text: File.ReadAllText(file)))
            .Where(source => source.Text.Contains("SchemaFamily", StringComparison.Ordinal) ||
                             source.Text.Contains("SchemaVersion", StringComparison.Ordinal) ||
                             source.Text.Contains("EfSchemaUpcaster", StringComparison.Ordinal)));

    [Fact]
    public void Every_family_the_stores_check_is_declared_once_and_checked_through_its_chain() =>
        AssertNone(Production.DeclarationViolations(), "Every schema family a store checks, and every SchemaFamily constant, needs exactly one " +
            "[EfSchemaFamily] declaration, and every check names its family through the family's chain handle, or the host's " +
            "readability report omits it or its reads use another family's versions (spec 180, FR-001 and FR-002):");

    [Fact]
    public void Every_family_has_one_chain_handle_resolved_from_its_own_declaration() =>
        AssertNone(Production.HandleViolations(), "Every class with a SchemaFamily constant holds 'public static readonly EfSchemaChain Chain = " +
            "EfSchemaChain.Of(typeof(<that class>).Assembly, SchemaFamily)', so its stores read through the chain the host " +
            "reports (spec 180, FR-010):");

    [Fact]
    public void Every_declared_chain_is_sound() =>
        AssertNone(Production.ChainViolations(), "A family's upcaster chain must list upcasters oldest first, each starting where the one " +
            "before it ends, the last ending at the current version, with no version twice; a gap is never bridged at read " +
            "time (spec 180, FR-005):");

    [Fact]
    public void Every_upcaster_belongs_to_exactly_one_declared_chain() =>
        AssertNone(Production.UpcasterViolations(), "An upcaster transforms one version of one family (spec 180, FR-003), so exactly one " +
            "[EfSchemaFamily] declaration lists it; one listed by none never runs:");

    [Fact]
    public void A_family_EF_materializes_directly_declares_no_upcasters() =>
        AssertNone(Production.MaterializedFamilyViolations(), "EF deserializes an IEfSchemaVersionedContext family's content in its value " +
            "converters before any upcaster could run, so the materialization interceptor reads its current version alone " +
            "and the family must declare no chain until its content moves to store code:");

    [Fact]
    public void Every_constant_stamp_is_its_familys_declared_current_version() =>
        AssertNone(Production.StampViolations(), "A write stamps the current version its family's declaration names (spec 180, FR-013), " +
            "never a literal or another constant:");

    /// <summary>Where the frozen upcaster fixtures are recorded, one '&lt;sha-256&gt;  &lt;repo-relative path&gt;' line each.</summary>
    private const string FixtureLock = "tests/essentials/Architecture/Baselines/schema-upcaster-fixtures.sha256";

    /// <summary>Every committed upcaster fixture: a file under a <c>Fixtures/SchemaUpcasters</c> directory of a test tree.</summary>
    private static IReadOnlyList<(string Path, string Text)> UpcasterFixtures { get; } =
        new[] { "tests", "src" }
            .SelectMany(root => Directory.EnumerateDirectories(Path.Join(RepoRoot, root), "SchemaUpcasters", SearchOption.AllDirectories))
            .Where(directory => !IsBuildOutput(directory) && Path.GetFileName(Path.GetDirectoryName(directory)) == "Fixtures")
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Select(file => (Path: Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/'), Text: File.ReadAllText(file)))
            .OrderBy(fixture => fixture.Path, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void Every_upcaster_ships_a_fixture_pair_and_every_fixture_is_frozen() =>
        AssertNone(
            UpcasterFixtureRules.Violations(UpcasterFixtures, File.ReadAllText(Path.Join(RepoRoot, FixtureLock)), Production.UpcasterSteps()),
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

    private const string Pair = "tests/Module/Fixtures/SchemaUpcasters/Orders/1-to-2/";

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

    [Fact]
    public void Every_in_place_content_rewrite_restamps_the_row() =>
        AssertNone(Production.RestampViolations(), "A write that rewrites a row's content column in place writes it in the current format, so it " +
            "stamps the row with the current version in the same member; left at an older stamp, the next read would upcast " +
            "content that is already current (spec 180, FR-014):");

    /// <summary>
    /// The rules pass vacuously if the scan stops finding what they judge, so pin floors rather than counts: the
    /// twenty-seven declared families, the checks and stamps of their stores, and a chain handle for each family.
    /// </summary>
    [Fact]
    public void Guard_scans_the_checks_declarations_handles_and_stamps_it_claims_to_scan()
    {
        Assert.True(Production.Checks.Count >= 80, $"Expected the EF stores to keep checking families through EfSchemaVersion; found {Production.Checks.Count} checks.");
        Assert.True(Production.Declarations.Count >= 27, $"Expected at least twenty-seven [EfSchemaFamily] declarations; found {Production.Declarations.Count}.");
        Assert.True(Production.NamedFamilies.Count >= 27, $"Expected at least twenty-seven families named by checks and constants; found {Production.NamedFamilies.Count}.");
        Assert.True(Production.Handles.Count >= 27, $"Expected a chain handle for each of at least twenty-seven families; found {Production.Handles.Count}.");
        Assert.True(Production.Stamps.Count >= 60, $"Expected the EF stores to keep stamping their families' constants; found {Production.Stamps.Count} stamps.");
        Assert.True(Production.MaterializedFamilies.Count >= 2, $"Expected the two design contexts EF materializes directly; found {Production.MaterializedFamilies.Count}.");
        Assert.True(Production.ContentRewrites.Count >= 20, $"Expected the EF stores' in-place content rewrites; found {Production.ContentRewrites.Count}.");
    }

    [Theory]
    [MemberData(nameof(ViolatingFixtures))]
    public void Detector_flags_every_kind_of_violation(string name, string source, string expected)
    {
        var violations = Scan(source).AllViolations();

        Assert.True(violations.Any(violation => violation.Contains(expected, StringComparison.Ordinal)),
            $"The detector missed '{name}'. It reported: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Detector_accepts_a_family_declared_checked_stamped_and_chained_as_the_rules_require() =>
        Assert.Empty(Scan(SoundFixture).AllViolations());

    /// <summary>
    /// The shared, no-module form (spec 180, FR-001 extension for a family owned by no single EF module) is a two-argument
    /// declaration; it must still be read, not silently dropped for having one fewer argument than an owned family's.
    /// </summary>
    [Fact]
    public void Detector_accepts_a_family_declared_shared_with_no_module()
    {
        var scan = Scan(
            """
            [assembly: EfSchemaFamily(SchemaFinalization.SchemaFamily, SchemaFinalization.SchemaVersion)]

            public static class SchemaFinalization
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "SchemaFinalization";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(SchemaFinalization).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                bool Valid(Row row) => EfSchemaVersion.Readable(SchemaFinalization.Chain, row.SchemaVersion);
            }
            """);

        Assert.Empty(scan.AllViolations());
        Assert.Equal(["SchemaFinalization"], scan.NamedFamilies);
    }

    private const string SoundFixture =
        """
        [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion, Upcasters = new[] { typeof(OrdersOneToTwo), typeof(OrdersTwoToThree) })]
        [assembly: Elsa.Persistence.EntityFramework.EfSchemaFamilyAttribute(Invoices.SchemaFamily, "Sales", Invoices.SchemaVersion)]

        public static class Orders
        {
            public const string SchemaVersion = "3";
            public const string SchemaFamily = "Orders";
            public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
        }

        public static class Invoices
        {
            public const string SchemaVersion = "7";
            public const string SchemaFamily = "Invoices";
            public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Invoices).Assembly, SchemaFamily);
        }

        [EfSchemaUpcaster("1", "2")]
        public sealed class OrdersOneToTwo : IEfSchemaUpcaster { public string Upcast(EfSchemaContent content) => content.Value; }

        [EfSchemaUpcaster("2", Orders.SchemaVersion)]
        public sealed class OrdersTwoToThree : IEfSchemaUpcaster { public string Upcast(EfSchemaContent content) => content.Value; }

        public sealed class Store
        {
            void Read(Row row, IContext context)
            {
                if (EfSchemaVersion.NotReadable(Orders.Chain, row.SchemaVersion) || row.Id is null)
                    throw new InvalidDataException("corrupt");
                EfSchemaVersion.EnsureReadable(
                    Invoices.Chain,
                    Entry(row).Property<string>("SchemaVersion").CurrentValue);
                EfSchemaVersion.EnsureCurrent(context.SchemaChain, row.SchemaVersion);
            }

            Row Write(Order order) => new() { Id = order.Id, SchemaVersion = Orders.SchemaVersion };

            void Replace(Row row, Row replacement) => row.SchemaVersion = replacement.SchemaVersion;

            void Upgrade(Row row) => row.SchemaVersion = Invoices.Chain.CurrentVersion;

            Cursor Page() => new() { SchemaVersion = 1 };

            void Rewrite(Row row, Order order)
            {
                row.ContentJson = Serialize(order);
                row.SchemaVersion = Orders.SchemaVersion;
            }

            Row Fresh(Order order)
            {
                var row = new Row { Id = order.Id, SchemaVersion = Orders.SchemaVersion };
                row.ContentJson = Serialize(order);
                return row;
            }
        }
        """;

    public static TheoryData<string, string, string> ViolatingFixtures() => new()
    {
        {
            "a family checked through a handle whose class declares no family",
            """
            public static class Orders
            {
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                bool Valid(Row row) => EfSchemaVersion.Readable(Orders.Chain, row.SchemaVersion) && row.Id is not null;
            }
            """,
            "'Orders' is checked here but no [EfSchemaFamily] declares it"
        },
        {
            "a SchemaFamily constant with no declaration",
            """
            public static class Orders
            {
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }
            """,
            "'Orders' is a SchemaFamily constant but no [EfSchemaFamily] declares it"
        },
        {
            "a check that names its family by something other than a chain handle",
            """
            public sealed class Store
            {
                bool Valid(Row row, EfSchemaChain chain) => EfSchemaVersion.Readable(chain, row.SchemaVersion);
            }
            """,
            "names its family through 'chain'"
        },
        {
            "one family declared twice",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "1")]
            [assembly: EfSchemaFamily("Orders", "Billing", "1")]
            """,
            "'Orders' is declared 2 times"
        },
        {
            "a declaration whose family is not a literal or a constant",
            """
            [assembly: EfSchemaFamily(nameof(Orders), "Sales", "1")]
            """,
            "declares a family this guard cannot resolve"
        },
        {
            "a family class without a chain handle",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
            }
            """,
            "'Orders' holds no chain handle"
        },
        {
            "a chain handle resolved from another class's assembly",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Invoices).Assembly, SchemaFamily);
            }
            """,
            "resolves its chain from 'typeof(Invoices).Assembly'"
        },
        {
            "a chain handle resolving another family",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]
            [assembly: EfSchemaFamily("Invoices", "Sales", "1")]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, "Invoices");
            }
            """,
            "resolves family 'Invoices' but its class's SchemaFamily is 'Orders'"
        },
        {
            "a chain with a gap",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "3", Upcasters = new[] { typeof(ZeroToOne), typeof(TwoToThree) })]

            [EfSchemaUpcaster("0", "1")] public sealed class ZeroToOne : IEfSchemaUpcaster { }
            [EfSchemaUpcaster("2", "3")] public sealed class TwoToThree : IEfSchemaUpcaster { }
            """,
            "'Orders' has a gap: 'ZeroToOne' produces '1' but 'TwoToThree' reads '2'"
        },
        {
            "a chain that does not end at the current version",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "4", Upcasters = [typeof(OneToTwo)])]

            [EfSchemaUpcaster("1", "2")] public sealed class OneToTwo : IEfSchemaUpcaster { }
            """,
            "'Orders' ends its chain at '2', not at its current version '4'"
        },
        {
            "a chain with a cycle",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "2", Upcasters = new[] { typeof(TwoToOne), typeof(OneToTwo) })]

            [EfSchemaUpcaster("2", "1")] public sealed class TwoToOne : IEfSchemaUpcaster { }
            [EfSchemaUpcaster("1", "2")] public sealed class OneToTwo : IEfSchemaUpcaster { }
            """,
            "'Orders' reaches version '2' more than once"
        },
        {
            "a chain listing a type that is no upcaster",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "2", Upcasters = new[] { typeof(Helper) })]

            public sealed class Helper { }
            """,
            "'Orders' lists 'Helper', which carries no [EfSchemaUpcaster(from, to)]"
        },
        {
            "an upcaster no chain lists",
            """
            [EfSchemaUpcaster("1", "2")] public sealed class Orphan : IEfSchemaUpcaster { }
            """,
            "'Orphan' is listed by 0 [EfSchemaFamily] declarations"
        },
        {
            "an upcaster two chains list",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "2", Upcasters = new[] { typeof(OneToTwo) })]
            [assembly: EfSchemaFamily("Invoices", "Sales", "2", Upcasters = new[] { typeof(OneToTwo) })]

            [EfSchemaUpcaster("1", "2")] public sealed class OneToTwo : IEfSchemaUpcaster { }
            """,
            "'OneToTwo' is listed by 2 [EfSchemaFamily] declarations"
        },
        {
            "a family EF materializes directly that declares a chain",
            """
            [assembly: EfSchemaFamily(Designs.SchemaFamily, "Design", Designs.SchemaVersion, Upcasters = new[] { typeof(OneToTwo) })]

            public static class Designs
            {
                public const string SchemaVersion = "2";
                public const string SchemaFamily = "Designs";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Designs).Assembly, SchemaFamily);
            }

            [EfSchemaUpcaster("1", "2")] public sealed class OneToTwo : IEfSchemaUpcaster { }

            public abstract class DesignContext : DbContext, IEfSchemaVersionedContext
            {
                EfSchemaChain IEfSchemaVersionedContext.SchemaChain => Designs.Chain;
            }
            """,
            "'Designs' is materialized directly by 'DesignContext' but declares 1 upcaster"
        },
        {
            "a literal stamp",
            """
            public sealed class Store
            {
                Row Write() => new() { SchemaVersion = "1.0.0" };
            }
            """,
            "stamps the literal '1.0.0'"
        },
        {
            "a content rewrite that leaves the row's old stamp",
            """
            public sealed class Store
            {
                void Touch(Row row, Order order)
                {
                    row.ContentJson = Serialize(order);
                    row.Revision++;
                }
            }
            """,
            "rewrites 'row.ContentJson' but never stamps 'row'"
        },
        {
            "a stamp of a constant no declaration names current",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]

            public static class Orders
            {
                public const string SchemaVersion = "2";
                public const string PreviousVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                void Write(Row row) => row.SchemaVersion = Orders.PreviousVersion;
            }
            """,
            "stamps 'Orders.PreviousVersion', which no [EfSchemaFamily] declaration names as its current version"
        }
    };

    private static void AssertNone(IReadOnlyList<string> violations, string rule) =>
        Assert.True(violations.Count == 0, rule + Environment.NewLine + string.Join(Environment.NewLine, violations));

    private static SchemaFamilyScan Scan(string source) => SchemaFamilyScan.Of([("Fixture.cs", source)]);

    /// <summary>What one pass over a set of sources found: constants, checks, declarations, upcasters, handles and stamps.</summary>
    private sealed class SchemaFamilyScan
    {
        private static readonly string[] CheckMethods = ["EnsureReadable", "Readable", "NotReadable", "IsReadable", "EnsureCurrent"];
        private static readonly string[] DeclarationNames = ["EfSchemaFamily", "EfSchemaFamilyAttribute"];
        private static readonly string[] UpcasterNames = ["EfSchemaUpcaster", "EfSchemaUpcasterAttribute"];

        private readonly Dictionary<string, string?> _constants = new(StringComparer.Ordinal);
        private readonly List<(string Location, string Class, string Value)> _familyConstants = [];
        private readonly List<(string Location, string Class, ExpressionSyntax? From, ExpressionSyntax? To)> _upcasters = [];

        public List<(string Location, ExpressionSyntax Family)> Checks { get; } = [];

        public List<(string Location, ExpressionSyntax Family, ExpressionSyntax Version, string[] Upcasters)> Declarations { get; } = [];

        public List<(string Location, string Class, string? AssemblyOf, ExpressionSyntax Family)> Handles { get; } = [];

        public List<(string Location, ExpressionSyntax Value)> Stamps { get; } = [];

        public List<(string Location, string Context, string? FamilyClass)> MaterializedFamilies { get; } = [];

        /// <summary>In-place writes of a content column: the receiver, the column, and whether the same member restamps it.</summary>
        public List<(string Location, string Row, string Column, bool Restamped)> ContentRewrites { get; } = [];

        /// <summary>The columns a family's stores keep their content document in, by the names the tree gives them.</summary>
        private static readonly string[] ContentColumns = ["ContentJson", "PayloadJson", "Content", "Payload", "ValueJson", "OutcomeJson"];

        /// <summary>Every family a check names through a handle, or a <c>SchemaFamily</c> constant holds.</summary>
        public IReadOnlySet<string> NamedFamilies =>
            Checks.Select(check => FamilyOfHandle(check.Family)).OfType<string>()
                .Concat(_familyConstants.Select(constant => constant.Value))
                .ToHashSet(StringComparer.Ordinal);

        public static SchemaFamilyScan Of(IEnumerable<(string Path, string Text)> sources)
        {
            var scan = new SchemaFamilyScan();
            foreach (var (path, text) in sources)
                scan.Read(path, CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest)).GetCompilationUnitRoot());
            return scan;
        }

        public IReadOnlyList<string> AllViolations() =>
        [
            .. DeclarationViolations(), .. HandleViolations(), .. ChainViolations(), .. UpcasterViolations(),
            .. MaterializedFamilyViolations(), .. StampViolations(), .. RestampViolations()
        ];

        /// <summary>Every declared chain's steps, resolved: the family, and the versions each upcaster reads and produces.</summary>
        public IEnumerable<(string Location, string Family, string From, string To)> UpcasterSteps() =>
            Declared()
                .Where(declaration => declaration.Family is not null)
                .SelectMany(declaration => declaration.Upcasters
                    .Select(name => _upcasters.FirstOrDefault(upcaster => upcaster.Class == name))
                    .Where(upcaster => upcaster.Class is not null)
                    .Select(upcaster => (upcaster.Location, Family: declaration.Family!, From: Resolve(upcaster.From), To: Resolve(upcaster.To))))
                .Where(step => step.From is not null && step.To is not null)
                .Select(step => (step.Location, step.Family, step.From!, step.To!));

        public IReadOnlyList<string> RestampViolations() =>
            Ordered(ContentRewrites
                .Where(rewrite => !rewrite.Restamped)
                .Select(rewrite => $"{rewrite.Location}: rewrites '{rewrite.Row}.{rewrite.Column}' but never stamps '{rewrite.Row}' in the same member."));

        public IReadOnlyList<string> DeclarationViolations()
        {
            var declared = Declared();
            var versions = declared
                .Where(declaration => declaration.Family is not null)
                .GroupBy(declaration => declaration.Family!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Version, StringComparer.Ordinal);

            var unresolved = declared
                .Where(declaration => declaration.Family is null || declaration.Version is null)
                .Select(declaration => $"{declaration.Location}: declares a family this guard cannot resolve; write its name and version as literals or constants.");
            var duplicated = declared
                .Where(declaration => declaration.Family is not null)
                .GroupBy(declaration => declaration.Family!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => $"{string.Join(", ", group.Select(declaration => declaration.Location))}: '{group.Key}' is declared {group.Count()} times; a family has exactly one declaration.");
            var checks = Checks.SelectMany(check => IsContextChain(check.Family)
                ? Array.Empty<string>()
                : FamilyOfHandle(check.Family) is not { } family
                    ? [$"{check.Location}: names its family through '{check.Family}', not through a family's chain handle ('<Module>.Chain'); FR-002 names a family by one declared value."]
                    : versions.ContainsKey(family)
                        ? []
                        : [$"{check.Location}: '{family}' is checked here but no [EfSchemaFamily] declares it."]);
            var undeclaredConstants = _familyConstants
                .Where(constant => !versions.ContainsKey(constant.Value))
                .Select(constant => $"{constant.Location}: '{constant.Value}' is a SchemaFamily constant but no [EfSchemaFamily] declares it.");

            return Ordered(unresolved.Concat(duplicated).Concat(checks).Concat(undeclaredConstants));
        }

        public IReadOnlyList<string> HandleViolations()
        {
            var missing = _familyConstants
                .Where(constant => !Handles.Any(handle => handle.Class == constant.Class))
                .Select(constant => $"{constant.Location}: '{constant.Class}' holds no chain handle for family '{constant.Value}'.");
            var foreign = Handles
                .Where(handle => handle.AssemblyOf != handle.Class)
                .Select(handle => $"{handle.Location}: '{handle.Class}' resolves its chain from 'typeof({handle.AssemblyOf}).Assembly', not from its own assembly.");
            var wrongFamily = Handles
                .Select(handle => (handle, Family: Resolve(handle.Family), Own: _familyConstants.FirstOrDefault(constant => constant.Class == handle.Class).Value))
                .Where(item => item.Own is not null && item.Family != item.Own)
                .Select(item => $"{item.handle.Location}: '{item.handle.Class}'s chain handle resolves family '{item.Family}' but its class's SchemaFamily is '{item.Own}'.");
            return Ordered(missing.Concat(foreign).Concat(wrongFamily));
        }

        public IReadOnlyList<string> ChainViolations()
        {
            var violations = new List<string>();
            foreach (var declaration in Declared().Where(declaration => declaration.Family is not null && declaration.Upcasters.Length > 0))
            {
                var steps = declaration.Upcasters
                    .Select(name => (Name: name, Upcaster: _upcasters.FirstOrDefault(upcaster => upcaster.Class == name)))
                    .Select(step => (step.Name, Found: step.Upcaster.Class is not null, From: Resolve(step.Upcaster.From), To: Resolve(step.Upcaster.To)))
                    .ToArray();
                foreach (var step in steps.Where(step => !step.Found || step.From is null || step.To is null))
                    violations.Add($"{declaration.Location}: '{declaration.Family}' lists '{step.Name}', which carries no [EfSchemaUpcaster(from, to)] this guard can resolve.");
                for (var index = 1; index < steps.Length; index++)
                {
                    if (steps[index - 1].To is { } produced && steps[index].From is { } read && produced != read)
                        violations.Add($"{declaration.Location}: '{declaration.Family}' has a gap: '{steps[index - 1].Name}' produces '{produced}' but '{steps[index].Name}' reads '{read}'.");
                }

                if (steps[^1].To is { } last && declaration.Version is { } current && last != current)
                    violations.Add($"{declaration.Location}: '{declaration.Family}' ends its chain at '{last}', not at its current version '{current}'.");
                violations.AddRange(steps.Select(step => step.From).Append(declaration.Version).OfType<string>()
                    .GroupBy(version => version, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{declaration.Location}: '{declaration.Family}' reaches version '{group.Key}' more than once, which makes its chain a branch or a cycle."));
            }

            return Ordered(violations);
        }

        public IReadOnlyList<string> UpcasterViolations() =>
            Ordered(_upcasters
                .Select(upcaster => (upcaster, Listings: Declarations.Count(declaration => declaration.Upcasters.Contains(upcaster.Class, StringComparer.Ordinal))))
                .Where(item => item.Listings != 1)
                .Select(item => $"{item.upcaster.Location}: '{item.upcaster.Class}' is listed by {item.Listings} [EfSchemaFamily] declarations; an upcaster belongs to exactly one."));

        public IReadOnlyList<string> MaterializedFamilyViolations()
        {
            var declared = Declared();
            return Ordered(MaterializedFamilies
                .Select(context => (context, Family: context.FamilyClass is null ? null : _familyConstants.FirstOrDefault(constant => constant.Class == context.FamilyClass).Value))
                .SelectMany(item => item.Family is null
                    ? [$"{item.context.Location}: '{item.context.Context}' names a chain this guard cannot resolve; return a module class's Chain."]
                    : declared.Where(declaration => declaration.Family == item.Family && declaration.Upcasters.Length > 0)
                        .Select(declaration => $"{item.context.Location}: '{item.Family}' is materialized directly by '{item.context.Context}' but declares {declaration.Upcasters.Length} upcaster(s) at {declaration.Location}.")));
        }

        public IReadOnlyList<string> StampViolations()
        {
            var current = Declarations.Select(declaration => Key(declaration.Version)).OfType<string>().ToHashSet(StringComparer.Ordinal);
            return Ordered(Stamps.SelectMany(stamp => stamp.Value switch
            {
                LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) =>
                    [$"{stamp.Location}: stamps the literal '{literal.Token.ValueText}'; stamp the family's SchemaVersion constant."],
                MemberAccessExpressionSyntax access when Key(access) is { } key && _constants.ContainsKey(key) && !current.Contains(key) =>
                    [$"{stamp.Location}: stamps '{key}', which no [EfSchemaFamily] declaration names as its current version."],
                _ => Array.Empty<string>()
            }));
        }

        private (string Location, string? Family, string? Version, string[] Upcasters)[] Declared() =>
            Declarations.Select(declaration => (declaration.Location, Resolve(declaration.Family), Resolve(declaration.Version), declaration.Upcasters)).ToArray();

        private void Read(string path, CompilationUnitSyntax root)
        {
            var constants = root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Select(variable => StringConstant(path, variable))
                .OfType<StringConstantSyntax>()
                .ToArray();
            // A constant that two types of one name define differently cannot be resolved by name, so it resolves to nothing.
            foreach (var constant in constants)
                _constants[constant.Key] = _constants.TryGetValue(constant.Key, out var existing) && existing != constant.Value ? null : constant.Value;
            _familyConstants.AddRange(constants
                .Where(constant => constant.Key.EndsWith(".SchemaFamily", StringComparison.Ordinal))
                .Select(constant => (constant.Location, constant.Key[..constant.Key.IndexOf('.')], constant.Value)));

            Checks.AddRange(root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation => invocation is
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var method } access,
                    ArgumentList.Arguments.Count: 2
                } && CheckMethods.Contains(method, StringComparer.Ordinal) && Rightmost(access.Expression) == "EfSchemaVersion")
                .Select(invocation => (Locate(path, invocation), invocation.ArgumentList.Arguments[0].Expression)));

            // A declaration's version is its last positional argument, so the shared, no-module two-argument form
            // (name, currentVersion) and the owned three-argument form (name, module, currentVersion) are both read.
            foreach (var attribute in root.AttributeLists
                         .Where(list => list.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true)
                         .SelectMany(list => list.Attributes)
                         .Where(attribute => DeclarationNames.Contains(Rightmost(attribute.Name), StringComparer.Ordinal)))
            {
                var arguments = attribute.ArgumentList?.Arguments ?? default;
                var positional = arguments.Where(argument => argument.NameEquals is null).ToArray();
                if (positional.Length < 2)
                    continue;
                var upcasters = arguments
                    .Where(argument => argument.NameEquals?.Name.Identifier.ValueText == "Upcasters")
                    .SelectMany(argument => argument.Expression.DescendantNodesAndSelf().OfType<TypeOfExpressionSyntax>())
                    .Select(type => Rightmost(type.Type) ?? type.Type.ToString())
                    .ToArray();
                Declarations.Add((Locate(path, attribute), positional[0].Expression, positional[^1].Expression, upcasters));
            }

            foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var upcaster = type.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(attribute => UpcasterNames.Contains(Rightmost(attribute.Name), StringComparer.Ordinal));
                if (upcaster is not null)
                {
                    var arguments = upcaster.ArgumentList?.Arguments ?? default;
                    _upcasters.Add((Locate(path, type), type.Identifier.ValueText, arguments.Count == 2 ? arguments[0].Expression : null, arguments.Count == 2 ? arguments[1].Expression : null));
                }

                if (type.BaseList?.Types.Any(baseType => Rightmost(baseType.Type) == "IEfSchemaVersionedContext") == true)
                {
                    var chain = type.Members.OfType<PropertyDeclarationSyntax>()
                        .FirstOrDefault(property => property.Identifier.ValueText == "SchemaChain")?.ExpressionBody?.Expression;
                    MaterializedFamilies.Add((Locate(path, type), type.Identifier.ValueText, chain is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Chain" } handle ? Rightmost(handle.Expression) : null));
                }
            }

            Handles.AddRange(root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(variable => variable is
                {
                    Identifier.ValueText: "Chain",
                    Initializer.Value: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Of" } of, ArgumentList.Arguments.Count: 2 }
                } && Rightmost(of.Expression) == "EfSchemaChain" && variable.Parent?.Parent is FieldDeclarationSyntax { Parent: TypeDeclarationSyntax })
                .Select(variable =>
                {
                    var arguments = ((InvocationExpressionSyntax)variable.Initializer!.Value).ArgumentList.Arguments;
                    var owner = ((TypeDeclarationSyntax)variable.Parent!.Parent!.Parent!).Identifier.ValueText;
                    var assemblyOf = arguments[0].Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Assembly", Expression: TypeOfExpressionSyntax typeOf }
                        ? Rightmost(typeOf.Type)
                        : null;
                    var family = arguments[1].Expression is IdentifierNameSyntax bare
                        ? SyntaxFactory.ParseExpression($"{owner}.{bare.Identifier.ValueText}")
                        : arguments[1].Expression;
                    return (Locate(path, variable), owner, assemblyOf, family);
                }));

            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                         .Where(assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                                              assignment.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var column } &&
                                              ContentColumns.Contains(column, StringComparer.Ordinal)))
            {
                var target = (MemberAccessExpressionSyntax)assignment.Left;
                var row = target.Expression.ToString();
                var member = assignment.Ancestors().FirstOrDefault(node => node is MemberDeclarationSyntax or LocalFunctionStatementSyntax);
                var restamped = member is not null && (
                    member.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(stamp =>
                        stamp.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SchemaVersion" } stamped && stamped.Expression.ToString() == row) ||
                    member.DescendantNodes().OfType<VariableDeclaratorSyntax>().Any(variable =>
                        variable.Identifier.ValueText == row &&
                        variable.Initializer?.Value is BaseObjectCreationExpressionSyntax { Initializer: { } initializer } &&
                        initializer.Expressions.OfType<AssignmentExpressionSyntax>().Any(stamp => Rightmost(stamp.Left) == "SchemaVersion")));
                ContentRewrites.Add((Locate(path, assignment), row, target.Name.Identifier.ValueText, restamped));
            }

            Stamps.AddRange(root.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && Rightmost(assignment.Left) == "SchemaVersion")
                .Select(assignment => (Locate(path, assignment), assignment.Right)));
        }

        /// <summary>A chain handle names its family through its class's <c>SchemaFamily</c> constant: <c>Module.Chain</c>.</summary>
        private string? FamilyOfHandle(ExpressionSyntax expression) =>
            expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Chain" } handle && Rightmost(handle.Expression) is { } owner
                ? _constants.GetValueOrDefault($"{owner}.SchemaFamily")
                : null;

        /// <summary>
        /// The materialization interceptor checks a context's chain, <c>context.SchemaChain</c>; the context's own family is
        /// held to its declaration by the materialized-family rule instead.
        /// </summary>
        private static bool IsContextChain(ExpressionSyntax expression) =>
            expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SchemaChain" };

        /// <summary>A <c>const string</c> field of a type, initialized with a literal, keyed <c>Type.Field</c>.</summary>
        private static StringConstantSyntax? StringConstant(string path, VariableDeclaratorSyntax variable) =>
            variable is { Initializer.Value: LiteralExpressionSyntax literal, Parent.Parent: FieldDeclarationSyntax { Parent: TypeDeclarationSyntax type } field } &&
            literal.IsKind(SyntaxKind.StringLiteralExpression) &&
            field.Modifiers.Any(SyntaxKind.ConstKeyword)
                ? new StringConstantSyntax(Locate(path, variable), $"{type.Identifier.ValueText}.{variable.Identifier.ValueText}", literal.Token.ValueText)
                : null;

        private string? Resolve(ExpressionSyntax? expression) => expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            MemberAccessExpressionSyntax access => _constants.GetValueOrDefault(Key(access) ?? ""),
            _ => null
        };

        private static string? Key(ExpressionSyntax? expression) =>
            expression is MemberAccessExpressionSyntax access && Rightmost(access.Expression) is { } owner ? $"{owner}.{access.Name.Identifier.ValueText}" : null;

        private static string? Rightmost(SyntaxNode node) => node switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            _ => null
        };

        private static IReadOnlyList<string> Ordered(IEnumerable<string> violations) => violations.Order(StringComparer.Ordinal).ToArray();

        private static string Locate(string path, SyntaxNode node) =>
            $"{path.Replace(Path.DirectorySeparatorChar, '/')}({node.GetLocation().GetLineSpan().StartLinePosition.Line + 1})";

        private sealed record StringConstantSyntax(string Location, string Key, string Value);
    }

    /// <summary>
    /// FR-022's fixture rules over a set of committed fixtures, their lock and the declared upcasters, so the detector can
    /// be pinned on fixtures that never touch the tree.
    /// </summary>
    internal static class UpcasterFixtureRules
    {
        private static readonly System.Text.RegularExpressions.Regex Named = new(
            @"/Fixtures/SchemaUpcasters/(?<family>[^/]+)/(?<from>[^/]+)-to-(?<to>[^/]+)/(?<name>[^/]+)\.(?<role>source|expected)\.(?<ext>[^./]+)$");

        public static IReadOnlyList<string> Violations(
            IReadOnlyList<(string Path, string Text)> fixtures,
            string lockText,
            IEnumerable<(string Location, string Family, string From, string To)> upcasters)
        {
            var violations = new List<string>();
            var named = fixtures.Select(fixture => (fixture.Path, fixture.Text, Match: Named.Match("/" + fixture.Path))).ToArray();
            violations.AddRange(named.Where(fixture => !fixture.Match.Success)
                .Select(fixture => $"{fixture.Path}: is not named <table>.<column>.source.<ext> or .expected.<ext> under Fixtures/SchemaUpcasters/<family>/<from>-to-<to>/."));

            var pairs = named.Where(fixture => fixture.Match.Success)
                .GroupBy(fixture => fixture.Path[..(fixture.Path.Length - fixture.Match.Groups["role"].Length - fixture.Match.Groups["ext"].Length - 1)], StringComparer.Ordinal)
                .ToArray();
            foreach (var pair in pairs)
            {
                var roles = pair.Select(fixture => fixture.Match.Groups["role"].Value).ToHashSet(StringComparer.Ordinal);
                if (!roles.Contains("expected"))
                    violations.Add($"{pair.Key}source: has no matching expected fixture.");
                if (!roles.Contains("source"))
                    violations.Add($"{pair.Key}expected: has no matching source fixture.");
            }

            foreach (var (location, family, from, to) in upcasters)
            {
                var complete = pairs.Any(pair => pair.Count() == 2 && pair.All(fixture =>
                    fixture.Match.Groups["family"].Value == family && fixture.Match.Groups["from"].Value == from && fixture.Match.Groups["to"].Value == to));
                if (!complete)
                    violations.Add($"{location}: the '{family}' upcaster from '{from}' to '{to}' ships no fixture pair under Fixtures/SchemaUpcasters/{family}/{from}-to-{to}/.");
            }

            var recorded = lockText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split("  ", 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
            foreach (var (path, text) in fixtures)
            {
                if (!recorded.TryGetValue(path, out var hash))
                    violations.Add($"{path}: is not recorded in the fixture lock; record '{Hash(text)}  {path}' once its version ships.");
                else if (!StringComparer.Ordinal.Equals(hash, Hash(text)))
                    violations.Add($"{path}: was edited after it was recorded; a shipped fixture is frozen and never regenerated.");
            }

            violations.AddRange(recorded.Keys.Where(path => fixtures.All(fixture => fixture.Path != path))
                .Select(path => $"{path}: was deleted; a fixture stays committed even after its upcaster retires (FR-024)."));
            return violations.Order(StringComparer.Ordinal).ToArray();
        }

        /// <summary>SHA-256 of the fixture with line endings normalized, so a checkout's line-ending conversion is no edit.</summary>
        public static string Hash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
    }
}
