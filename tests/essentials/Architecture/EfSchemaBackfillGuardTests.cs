using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// The post-finalization backfill's declarations (spec 186), over every production source under <c>src/</c>:
/// <list type="bullet">
/// <item>a family whose chain is longer than one names a rewriter, a class of its own EF module's project that implements
/// <c>IEfSchemaRowRewriter</c>, before the version that needs it ships (FR-004): without one, its backfill can upgrade
/// nothing and it is never recorded complete;</item>
/// <item>the tables holding executables and executable activity templates are named content-addressed (FR-010b);</item>
/// <item>a feature that needs completeness never names a family with content-addressed tables, since it could never
/// leave dormancy (FR-011c).</item>
/// </list>
/// The first-party model's side of FR-010b, every stamped table whose name says it holds executables, is
/// <c>EfSchemaContentAddressedDeclarationTests</c>.
/// </summary>
public sealed class EfSchemaBackfillGuardTests
{
    private static readonly BackfillDeclarationScan Production = BackfillDeclarationScan.Of(
        Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !HasSegment(RepoRoot, file, "Migrations"))
            .Select(file => (Path: Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/'), Text: File.ReadAllText(file)))
            .Where(source => source.Text.Contains("SchemaFamily", StringComparison.Ordinal) ||
                             source.Text.Contains("RequiresSchemaVersion", StringComparison.Ordinal) ||
                             source.Text.Contains("IEfSchemaRowRewriter", StringComparison.Ordinal)));

    [Fact]
    public void Every_family_with_a_chain_longer_than_one_names_a_rewriter_of_its_own_module() =>
        AssertNone(Production.RewriterViolations(), "A family whose chain is longer than one names its rewriter with " +
            "[EfSchemaFamily(..., Rewriter = typeof(...))], a class of its own EF module's project implementing IEfSchemaRowRewriter, " +
            "or the post-finalization backfill can never upgrade its rows below the finalized version (spec 186, FR-004):");

    [Fact]
    public void The_tables_holding_executables_and_executable_activity_templates_are_declared_content_addressed() =>
        Assert.Superset(
            new HashSet<string> { "RuntimeArtifact:WorkflowExecutableEntity", "RuntimeArtifact:ExecutableActivityTemplateEntity" },
            Production.ContentAddressedTables().ToHashSet(StringComparer.Ordinal));

    [Fact]
    public void No_feature_needs_completeness_of_a_family_with_content_addressed_tables() =>
        AssertNone(Production.CompletenessViolations(), "A feature that declares [RequiresSchemaVersion(..., RequiresCompleteness = true)] on a " +
            "family with content-addressed tables could never leave dormancy, because the backfill never rewrites those rows " +
            "(spec 186, FR-011c). Keep the rows it queries in a family of their own:");

    /// <summary>
    /// The rules pass vacuously if the scan stops finding what they judge, so pin a floor on the declarations it reads.
    /// No first-party feature declares a completeness requirement yet, and no family has a chain longer than one, so the
    /// fixtures below are what show those two rules bite.
    /// </summary>
    [Fact]
    public void Guard_scans_the_declarations_it_claims_to_scan() =>
        Assert.True(Production.Declarations.Count >= 28, $"Expected at least twenty-eight [EfSchemaFamily] declarations; found {Production.Declarations.Count}.");

    [Theory]
    [MemberData(nameof(ViolatingFixtures))]
    public void Detector_flags_every_kind_of_violation(string name, string source, string expected)
    {
        var scan = BackfillDeclarationScan.Of([("src/Sales/Persistence/AssemblyInfo.cs", Shared), ("src/Sales/Persistence/Declarations.cs", source)]);
        var violations = scan.RewriterViolations().Concat(scan.CompletenessViolations()).ToArray();

        Assert.True(violations.Any(violation => violation.Contains(expected, StringComparison.Ordinal)),
            $"The detector missed '{name}'. It reported: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Detector_accepts_a_chained_family_with_its_own_rewriter_and_a_feature_needing_completeness_of_another_family()
    {
        var scan = BackfillDeclarationScan.Of(
        [
            ("src/Sales/Persistence/AssemblyInfo.cs", Shared),
            ("src/Sales/Persistence/Declarations.cs",
                """
                [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", "2", Upcasters = [typeof(OrdersOneToTwo)], Rewriter = typeof(OrdersRewriter))]
                [assembly: EfSchemaFamily(Receipts.SchemaFamily, "Sales", "1", ContentAddressed = [typeof(ReceiptEntity)])]
                public sealed class OrdersRewriter : IEfSchemaRowRewriter { }
                """),
            ("src/Sales/Api/OrdersFeature.cs",
                """
                [RequiresSchemaVersion(Orders.SchemaFamily, "2", RequiresCompleteness = true)]
                [RequiresSchemaVersion(Receipts.SchemaFamily, "1")]
                public sealed class OrdersFeature { }
                """)
        ]);

        Assert.Empty(scan.RewriterViolations());
        Assert.Empty(scan.CompletenessViolations());
        Assert.Equal(["Receipts:ReceiptEntity"], scan.ContentAddressedTables());
    }

    private const string Shared =
        """
        public static class Orders { public const string SchemaFamily = "Orders"; }
        public static class Receipts { public const string SchemaFamily = "Receipts"; }
        """;

    public static TheoryData<string, string, string> ViolatingFixtures() => new()
    {
        {
            "a chained family with no rewriter",
            """[assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", "2", Upcasters = [typeof(OrdersOneToTwo)])]""",
            "'Orders' has a chain longer than one but names no rewriter"
        },
        {
            "a rewriter that is not a rewriter",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", "2", Upcasters = [typeof(OrdersOneToTwo)], Rewriter = typeof(OrdersStore))]
            public sealed class OrdersStore { }
            """,
            "names 'OrdersStore' as its rewriter, which no class of its own module's project implementing IEfSchemaRowRewriter declares"
        },
        {
            "a completeness requirement on a family with content-addressed tables",
            """
            [assembly: EfSchemaFamily(Receipts.SchemaFamily, "Sales", "1", ContentAddressed = [typeof(ReceiptEntity)])]
            [RequiresSchemaVersion(Receipts.SchemaFamily, "1", RequiresCompleteness = true)]
            public sealed class ReceiptsFeature { }
            """,
            "'ReceiptsFeature' needs completeness of 'Receipts', which declares content-addressed tables"
        }
    };

    private static void AssertNone(IReadOnlyList<string> violations, string rule) =>
        Assert.True(violations.Count == 0, rule + Environment.NewLine + string.Join(Environment.NewLine, violations));
}

/// <summary>What one pass over a set of sources found of the backfill's declarations: families, rewriters and completeness requirements.</summary>
internal sealed class BackfillDeclarationScan
{
    private static readonly string[] FamilyNames = ["EfSchemaFamily", "EfSchemaFamilyAttribute"];
    private static readonly string[] RequirementNames = ["RequiresSchemaVersion", "RequiresSchemaVersionAttribute"];

    private readonly Dictionary<string, string> _constants = new(StringComparer.Ordinal);
    private readonly List<(string Path, string Class)> _rewriters = [];

    public List<Declaration> Declarations { get; } = [];

    public List<(string Location, string Feature, ExpressionSyntax Family)> Requirements { get; } = [];

    public static BackfillDeclarationScan Of(IEnumerable<(string Path, string Text)> sources)
    {
        var scan = new BackfillDeclarationScan();
        foreach (var (path, text) in sources)
            scan.Read(path, CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest)).GetCompilationUnitRoot());
        return scan;
    }

    /// <summary>Every content-addressed table a declaration names, as <c>family:entity</c>.</summary>
    public IReadOnlyList<string> ContentAddressedTables() =>
        Declarations.SelectMany(declaration => declaration.ContentAddressed.Select(entity => $"{Resolve(declaration.Family)}:{entity}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> RewriterViolations()
    {
        var violations = new List<string>();
        foreach (var declaration in Declarations.Where(declaration => declaration.Upcasters > 0))
        {
            var family = Resolve(declaration.Family) ?? declaration.Family.ToString();
            if (declaration.Rewriter is null)
                violations.Add($"{declaration.Location}: '{family}' has a chain longer than one but names no rewriter.");
            else if (!_rewriters.Any(rewriter => rewriter.Class == declaration.Rewriter && Project(rewriter.Path) == Project(declaration.Path)))
                violations.Add($"{declaration.Location}: '{family}' names '{declaration.Rewriter}' as its rewriter, which no class of its own module's " +
                               "project implementing IEfSchemaRowRewriter declares.");
        }

        return violations;
    }

    public IReadOnlyList<string> CompletenessViolations()
    {
        var contentAddressed = Declarations
            .Where(declaration => declaration.ContentAddressed.Length > 0)
            .Select(declaration => Resolve(declaration.Family))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return Requirements
            .Where(requirement => Resolve(requirement.Family) is { } family && contentAddressed.Contains(family))
            .Select(requirement => $"{requirement.Location}: '{requirement.Feature}' needs completeness of '{Resolve(requirement.Family)}', which declares content-addressed tables.")
            .ToArray();
    }

    private void Read(string path, CompilationUnitSyntax root)
    {
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>().Where(field => field.Modifiers.Any(SyntaxKind.ConstKeyword)))
        {
            if (field.Parent is not TypeDeclarationSyntax owner)
                continue;
            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Initializer?.Value is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                    _constants.TryAdd($"{owner.Identifier.ValueText}.{variable.Identifier.ValueText}", literal.Token.ValueText);
            }
        }

        foreach (var attribute in root.AttributeLists
                     .Where(list => list.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true)
                     .SelectMany(list => list.Attributes)
                     .Where(attribute => FamilyNames.Contains(Rightmost(attribute.Name), StringComparer.Ordinal)))
        {
            var arguments = attribute.ArgumentList?.Arguments ?? default;
            var positional = arguments.Where(argument => argument.NameEquals is null).ToArray();
            if (positional.Length < 2)
                continue;
            Declarations.Add(new Declaration(
                Locate(path, attribute),
                path,
                positional[0].Expression,
                Types(arguments, "Upcasters").Length,
                Types(arguments, "ContentAddressed"),
                Types(arguments, "Rewriter").FirstOrDefault()));
        }

        foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (type.BaseList?.Types.Any(baseType => Rightmost(baseType.Type) == "IEfSchemaRowRewriter") == true)
                _rewriters.Add((path, type.Identifier.ValueText));
            foreach (var attribute in type.AttributeLists.SelectMany(list => list.Attributes)
                         .Where(attribute => RequirementNames.Contains(Rightmost(attribute.Name), StringComparer.Ordinal)))
            {
                var arguments = attribute.ArgumentList?.Arguments ?? default;
                var completeness = arguments.Any(argument =>
                    argument.NameEquals?.Name.Identifier.ValueText == "RequiresCompleteness" && argument.Expression.IsKind(SyntaxKind.TrueLiteralExpression));
                if (completeness && arguments.FirstOrDefault(argument => argument.NameEquals is null) is { } family)
                    Requirements.Add((Locate(path, attribute), type.Identifier.ValueText, family.Expression));
            }
        }
    }

    private static string[] Types(SeparatedSyntaxList<AttributeArgumentSyntax> arguments, string member) =>
        arguments
            .Where(argument => argument.NameEquals?.Name.Identifier.ValueText == member)
            .SelectMany(argument => argument.Expression.DescendantNodesAndSelf().OfType<TypeOfExpressionSyntax>())
            .Select(type => Rightmost(type.Type) ?? type.Type.ToString())
            .ToArray();

    private string? Resolve(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
        MemberAccessExpressionSyntax access when Rightmost(access.Expression) is { } owner => _constants.GetValueOrDefault($"{owner}.{access.Name.Identifier.ValueText}"),
        _ => null
    };

    /// <summary>The project a source belongs to: the nearest directory above it that holds a project file, or its own directory in a fixture.</summary>
    private static string Project(string path)
    {
        for (var directory = Path.GetDirectoryName(path); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            var absolute = Path.Join(RepoRoot, directory);
            if (Directory.Exists(absolute) && Directory.EnumerateFiles(absolute, "*.csproj").Any())
                return directory;
        }

        return Path.GetDirectoryName(path) ?? "";
    }

    private static string? Rightmost(SyntaxNode node) => node switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        _ => null
    };

    private static string Locate(string path, SyntaxNode node) => $"{path}({node.GetLocation().GetLineSpan().StartLinePosition.Line + 1})";

    public sealed record Declaration(string Location, string Path, ExpressionSyntax Family, int Upcasters, string[] ContentAddressed, string? Rewriter);
}
