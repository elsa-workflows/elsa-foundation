using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// A host's readability report is derived from its <c>[EfSchemaFamily]</c> declarations alone (spec 183, FR-020;
/// spec 180, FR-001). A member whose report lacks a family is not counted for it, so a family the stores check but
/// nothing declares would let spec 181's gate finalize a version that member cannot read: the failure that looks like
/// success. This guard fails the build when a family named by a skew check or by a <c>SchemaFamily</c> constant is not
/// declared, when a declaration's version is not the version that family's checks compare against, or when a family is
/// declared twice.
/// </summary>
/// <remarks>
/// <para>
/// It reads syntax only, over every production source under <c>src/</c> (essentials, extensions and apps) that is not
/// a generated migration. A family
/// or version argument is resolved when it is a string literal or a <c>Type.Constant</c> whose <c>const string</c>
/// initializer is a literal. A check whose family arrives through a parameter or an instance property, as
/// <c>EfSchemaVersion</c>'s own helpers and a context-level check do, is not resolved at the call: the constant
/// that feeds it is itself held to a declaration by the <c>SchemaFamily</c> rule.
/// </para>
/// <para>
/// Once spec 180's chain (B4, #2100) has the stores read their readable set from the declaration itself, the version
/// rule has nothing left to compare and can go.
/// </para>
/// </remarks>
public sealed class EfSchemaFamilyDeclarationGuardTests
{
    private static SchemaFamilyScan Production { get; } = SchemaFamilyScan.Of(
        Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !HasSegment(RepoRoot, file, "Migrations"))
            .Select(file => (Path: Path.GetRelativePath(RepoRoot, file), Text: File.ReadAllText(file)))
            .Where(source => source.Text.Contains("SchemaFamily", StringComparison.Ordinal) ||
                             source.Text.Contains("SchemaVersion", StringComparison.Ordinal)));

    [Fact]
    public void Every_family_the_stores_check_is_declared_once_at_the_version_they_check()
    {
        var violations = Production.Violations();

        Assert.True(
            violations.Count == 0,
            "Every schema family a store checks, and every SchemaFamily constant, must have exactly one [EfSchemaFamily] " +
            "declaration at the version the family's checks compare against, or the host's readability report omits or " +
            "misstates it (spec 183, FR-020):" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The rule passes vacuously if the scan stops finding the checks and declarations, so pin floors rather than
    /// counts: the twenty-six families every EF content table stamps since #2119, and the call sites that check them.
    /// </summary>
    [Fact]
    public void Guard_scans_the_checks_and_declarations_it_claims_to_scan()
    {
        Assert.True(Production.Checks.Count >= 30, $"Expected the EF stores to keep checking families through EfSchemaVersion; found {Production.Checks.Count} checks.");
        Assert.True(Production.Declarations.Count >= 27, $"Expected at least twenty-seven [EfSchemaFamily] declarations; found {Production.Declarations.Count}.");
        Assert.True(Production.NamedFamilies.Count >= 27, $"Expected at least twenty-seven families named by checks and constants; found {Production.NamedFamilies.Count}.");
    }

    [Theory]
    [MemberData(nameof(ViolatingFixtures))]
    public void Detector_flags_a_family_that_is_missing_misdeclared_or_duplicated(string name, string source, string expected)
    {
        var violations = Scan(source).Violations();

        Assert.True(violations.Any(violation => violation.Contains(expected, StringComparison.Ordinal)),
            $"The detector missed '{name}'. It reported: {string.Join("; ", violations)}");
    }

    /// <summary>
    /// The shared, no-module form (spec 180, FR-001 extension for a family owned by no single EF module, #2099 B3) is a
    /// two-argument declaration; it must still be read, not silently dropped for having one fewer argument than an
    /// owned family's.
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
            }

            public sealed class Store
            {
                bool Valid(Row row) => EfSchemaVersion.Readable(SchemaFinalization.SchemaFamily, row.SchemaVersion, SchemaFinalization.SchemaVersion);
            }
            """);

        Assert.Empty(scan.Violations());
        Assert.Equal(["SchemaFinalization"], scan.NamedFamilies);
    }

    [Fact]
    public void Detector_accepts_families_declared_at_the_version_they_are_checked_at()
    {
        var scan = Scan(
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]
            [assembly: Elsa.Persistence.EntityFramework.EfSchemaFamilyAttribute("Invoices", "Sales", "7")]

            public static class Orders
            {
                public const string SchemaVersion = "1.0.0";
                public const string SchemaFamily = "Orders";
            }

            public sealed class Store
            {
                void Read(Row row, IContext context, string module)
                {
                    if (EfSchemaVersion.NotReadable(Orders.SchemaFamily, row.SchemaVersion, Orders.SchemaVersion) || row.Id is null)
                        throw new InvalidDataException("corrupt");
                    EfSchemaVersion.EnsureReadable(
                        "Invoices",
                        Entry(row).Property<string>("SchemaVersion").CurrentValue,
                        "7");
                    EfSchemaVersion.EnsureReadable(context.SchemaFamily, row.SchemaVersion, context.SchemaVersion);
                    EfSchemaVersion.EnsureReadable(module, row.SchemaVersion, "any");
                }
            }
            """);

        Assert.Empty(scan.Violations());
        Assert.Equal(2, scan.Declarations.Count);
        Assert.Equal(["Invoices", "Orders"], scan.NamedFamilies.Order(StringComparer.Ordinal));
    }

    public static TheoryData<string, string, string> ViolatingFixtures() => new()
    {
        {
            "a literal family checked but never declared",
            """
            public sealed class Store
            {
                bool Valid(Row row) => EfSchemaVersion.Readable("Orders", row.SchemaVersion, "1.0.0") && row.Id is not null;
            }
            """,
            "'Orders' is checked here but no [EfSchemaFamily] declares it"
        },
        {
            "a check spread over lines, its version read through a nested call",
            """
            [assembly: EfSchemaFamily("Invoices", "Sales", "1")]

            public sealed class Store
            {
                void Read(Row row) =>
                    EfSchemaVersion.EnsureReadable(
                        "Orders",
                        Entry(row).Property<string>("SchemaVersion").CurrentValue,
                        "1");
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
            }
            """,
            "'Orders' is a SchemaFamily constant but no [EfSchemaFamily] declares it"
        },
        {
            "a declaration at another version than the checks read",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.PreviousVersion)]

            public static class Orders
            {
                public const string SchemaFamily = "Orders";
                public const string SchemaVersion = "2";
                public const string PreviousVersion = "1";
            }

            public sealed class Store
            {
                bool Valid(Row row) => EfSchemaVersion.Readable(Orders.SchemaFamily, row.SchemaVersion, Orders.SchemaVersion);
            }
            """,
            "'Orders' is checked against version '2' here but declared at '1'"
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
        }
    };

    private static SchemaFamilyScan Scan(string source) => SchemaFamilyScan.Of([("Fixture.cs", source)]);

    /// <summary>What one pass over a set of sources found: the family constants, the skew checks and the declarations.</summary>
    private sealed class SchemaFamilyScan
    {
        private static readonly string[] CheckMethods = ["EnsureReadable", "Readable", "NotReadable"];
        private static readonly string[] DeclarationNames = ["EfSchemaFamily", "EfSchemaFamilyAttribute"];

        private readonly Dictionary<string, string?> _constants = new(StringComparer.Ordinal);
        private readonly List<(string Location, string Value)> _familyConstants = [];

        public List<(string Location, ExpressionSyntax Family, ExpressionSyntax Version)> Checks { get; } = [];

        public List<(string Location, ExpressionSyntax Family, ExpressionSyntax Version)> Declarations { get; } = [];

        /// <summary>Every family a check names by literal or constant, or a <c>SchemaFamily</c> constant holds.</summary>
        public IReadOnlySet<string> NamedFamilies =>
            Checks.Select(check => Resolve(check.Family)).OfType<string>()
                .Concat(_familyConstants.Select(constant => constant.Value))
                .ToHashSet(StringComparer.Ordinal);

        public static SchemaFamilyScan Of(IEnumerable<(string Path, string Text)> sources)
        {
            var scan = new SchemaFamilyScan();
            foreach (var (path, text) in sources)
                scan.Read(path, CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest)).GetCompilationUnitRoot());
            return scan;
        }

        public IReadOnlyList<string> Violations()
        {
            var declared = Declarations
                .Select(declaration => (declaration.Location, Family: Resolve(declaration.Family), Version: Resolve(declaration.Version)))
                .ToArray();
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
            var undeclaredChecks = Checks
                .Select(check => (check.Location, Family: Resolve(check.Family), Version: Resolve(check.Version)))
                .Where(check => check.Family is not null)
                .SelectMany(check => !versions.TryGetValue(check.Family!, out var version)
                    ? [$"{check.Location}: '{check.Family}' is checked here but no [EfSchemaFamily] declares it."]
                    : check.Version is not null && version is not null && !string.Equals(check.Version, version, StringComparison.Ordinal)
                        ? [$"{check.Location}: '{check.Family}' is checked against version '{check.Version}' here but declared at '{version}'."]
                        : Array.Empty<string>());
            var undeclaredConstants = _familyConstants
                .Where(constant => !versions.ContainsKey(constant.Value))
                .Select(constant => $"{constant.Location}: '{constant.Value}' is a SchemaFamily constant but no [EfSchemaFamily] declares it.");

            return unresolved.Concat(duplicated).Concat(undeclaredChecks).Concat(undeclaredConstants).Order(StringComparer.Ordinal).ToArray();
        }

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
                .Select(constant => (constant.Location, constant.Value)));

            Checks.AddRange(root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation => invocation is
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var method } access,
                    ArgumentList.Arguments.Count: 3
                } && CheckMethods.Contains(method, StringComparer.Ordinal) && Rightmost(access.Expression) == "EfSchemaVersion")
                .Select(invocation => (Locate(path, invocation), invocation.ArgumentList.Arguments[0].Expression, invocation.ArgumentList.Arguments[2].Expression)));

            // A declaration's version is its last constructor argument, so the shared, no-module two-argument form
            // (name, currentVersion) and the owned three-argument form (name, module, currentVersion) are both read.
            Declarations.AddRange(root.AttributeLists
                .Where(list => list.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true)
                .SelectMany(list => list.Attributes)
                .Where(attribute => DeclarationNames.Contains(Rightmost(attribute.Name), StringComparer.Ordinal))
                .Select(attribute => (Attribute: attribute, Arguments: attribute.ArgumentList?.Arguments ?? default))
                .Where(declaration => declaration.Arguments.Count >= 2)
                .Select(declaration => (Locate(path, declaration.Attribute), declaration.Arguments[0].Expression, declaration.Arguments[^1].Expression)));
        }

        /// <summary>A <c>const string</c> field of a type, initialized with a literal, keyed <c>Type.Field</c>.</summary>
        private static StringConstantSyntax? StringConstant(string path, VariableDeclaratorSyntax variable) =>
            variable is { Initializer.Value: LiteralExpressionSyntax literal, Parent.Parent: FieldDeclarationSyntax { Parent: TypeDeclarationSyntax type } field } &&
            literal.IsKind(SyntaxKind.StringLiteralExpression) &&
            field.Modifiers.Any(SyntaxKind.ConstKeyword)
                ? new StringConstantSyntax(Locate(path, variable), $"{type.Identifier.ValueText}.{variable.Identifier.ValueText}", literal.Token.ValueText)
                : null;

        private string? Resolve(ExpressionSyntax expression) => expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            MemberAccessExpressionSyntax access => _constants.GetValueOrDefault($"{Rightmost(access.Expression)}.{access.Name.Identifier.ValueText}"),
            _ => null
        };

        private static string? Rightmost(SyntaxNode node) => node switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            _ => null
        };

        private static string Locate(string path, SyntaxNode node) =>
            $"{path.Replace(Path.DirectorySeparatorChar, '/')}({node.GetLocation().GetLineSpan().StartLinePosition.Line + 1})";

        private sealed record StringConstantSyntax(string Location, string Key, string Value);
    }
}
