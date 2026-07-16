using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Migration ratchets for spec 095. Temporary allowlists may only shrink; T103 removes them and
/// turns the final memory-reference ratchet into an unconditional zero-reference guard.
/// </summary>
public sealed partial class ValueFlowArchitectureTests(ITestOutputHelper output)
{
    private static readonly HashSet<(string Project, string Reference)> RuntimeDesignMigrationAllowlist =
    [
        ("Elsa.Workflows.Runtime.JavaScript", "Elsa.Workflows.Design.Core")
    ];

    private static readonly IReadOnlyDictionary<string, int> AssemblyQualifiedMetadataMigrationCeilings =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/Elsa/Activities/Runtime/Core/Models/InputArgument.cs"] = 1,
            ["src/Elsa/Workflows/Publishing/Api/Services/RuntimeInputBindingCompiler.cs"] = 2
        };

    private static readonly IReadOnlyDictionary<string, int> MemoryReferenceMigrationCeilings =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/Elsa/Activities/Primitives/Binding/ActivityArgumentBinder.cs"] = 1,
            ["src/Elsa/Activities/Runtime/Core/Abstractions/CodeActivity.cs"] = 2,
            ["src/Elsa/Activities/Runtime/Core/Models/InputArgument.cs"] = 8,
            ["src/Elsa/Activities/Runtime/Core/Models/OutputArgument.cs"] = 4,
            ["src/Elsa/Activities/Runtime/Services/ActivityOutputPublisher.cs"] = 4,
            ["src/Elsa/Expressions/Core/Contracts/IExpressionExecutionContext.cs"] = 20,
            ["src/Elsa/Expressions/Core/Contracts/IMemoryBlock.cs"] = 1,
            ["src/Elsa/Expressions/Core/Contracts/IMemoryBlockReference.cs"] = 10,
            ["src/Elsa/Expressions/Core/Contracts/IMemoryBlockReferenceFactory.cs"] = 3,
            ["src/Elsa/Expressions/Core/Contracts/IMemoryRegister.cs"] = 7,
            ["src/Elsa/Expressions/Core/Contracts/IVariable.cs"] = 1,
            ["src/Elsa/Expressions/Core/Models/Argument.cs"] = 3,
            ["src/Elsa/Expressions/Models/MemoryBlock.cs"] = 1,
            ["src/Elsa/Expressions/Models/MemoryBlockReference.cs"] = 10,
            ["src/Elsa/Expressions/Models/Variable.cs"] = 1,
            ["src/Elsa/Workflows/Runtime/JavaScript/Activities/RunJavaScript/TestClasses/BlockReference.cs"] = 4,
            ["src/Elsa/Workflows/Runtime/JavaScript/Activities/RunJavaScript/TestClasses/ScriptExpressionContext.cs"] = 9,
            ["src/Elsa/Workflows/Runtime/Services/RuntimeActivityInputMaterializer.cs"] = 16,
            ["src/Elsa/Workflows/Runtime/Services/RuntimeScopedVariableModel.cs"] = 3,
            ["src/Elsa/Workflows/Runtime/Services/SimpleActivityExecutionContext.cs"] = 19
        };

    private static readonly string[] CanonicalMetadataRoots =
    [
        "src/Elsa/Primitives/Primitives/Models",
        "src/Elsa/Activities/Design/Core/Models",
        "src/Elsa/Activities/Runtime/Core/Models",
        "src/Elsa/Workflows/Design/Core/Models",
        "src/Elsa/Workflows/Runtime/Core/Models",
        "src/Elsa/Workflows/Publishing/Api/Services"
    ];

    private static readonly string[] RuntimeGeneratedCarrierTokens =
    [
        "Microsoft.CodeAnalysis",
        "Elsa.Workflows.Design.CodeGeneration",
        "ActivityCallGenerator",
        "ActivityArgument<",
        "CallHandle<"
    ];

    [Fact]
    public void RuntimeProjects_DoNotAddDesignReferencesBeyondMigrationAllowlist()
    {
        var references = ProductionProjects()
            .Where(project => project.Name.Contains(".Runtime", StringComparison.Ordinal))
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => reference.Contains(".Design", StringComparison.Ordinal))
                .Select(reference => (Project: project.Name, Reference: reference)))
            .OrderBy(edge => edge.Project, StringComparer.Ordinal)
            .ThenBy(edge => edge.Reference, StringComparer.Ordinal)
            .ToArray();
        var violations = references.Where(edge => !RuntimeDesignMigrationAllowlist.Contains(edge)).ToArray();

        ReportAllowlisted("Runtime -> Design references pending migration", references.Where(RuntimeDesignMigrationAllowlist.Contains));
        Assert.True(
            violations.Length == 0,
            "Runtime projects must not add Design references. Unexpected edges:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(FormatEdge)));
    }

    [Fact]
    public void CanonicalMetadata_DoesNotAddAssemblyQualifiedTypeNamesBeyondMigrationAllowlist()
    {
        var hits = CanonicalMetadataRoots
            .Select(root => Path.Combine(RepoRoot, NormalizePath(root)))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Distinct(StringComparer.Ordinal)
            .Select(file => new SourceHit(RelativePath(file), AssemblyQualifiedMetadataPattern().Matches(File.ReadAllText(file)).Count))
            .Where(hit => hit.Count > 0)
            .OrderBy(hit => hit.Path, StringComparer.Ordinal)
            .ToArray();
        var violations = ExceedsMigrationCeiling(hits, AssemblyQualifiedMetadataMigrationCeilings);

        ReportAllowlisted("Assembly-qualified canonical metadata hits pending migration", hits.Where(IsAllowlistedAssemblyMetadataHit));
        Assert.True(
            violations.Length == 0,
            "Canonical metadata must use stable aliases and collection/schema descriptors, not assembly-qualified CLR type names. " +
            "Unexpected or expanded hits:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(FormatHit)));
    }

    [Fact]
    public void RoslynAndGeneratedAuthoringCarriers_RemainOutsideRuntime()
    {
        var projects = ProductionProjects().ToArray();
        var roslynPackageViolations = projects
            .SelectMany(project => PackageReferences(project)
                .Where(package => package.Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
                .Where(package => project.Name != "Elsa.Workflows.Design.CodeGeneration" || !package.PrivateAssetsAll)
                .Select(package => $"{project.Name} -> {package.Name} (PrivateAssets=all: {package.PrivateAssetsAll})"))
            .ToArray();
        var runtimeGeneratorReferences = projects
            .Where(project => project.Name.Contains(".Runtime", StringComparison.Ordinal))
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => reference.Contains("CodeGeneration", StringComparison.Ordinal))
                .Select(reference => $"{project.Name} -> {reference}"))
            .ToArray();
        var runtimeSourceHits = projects
            .Where(project => project.Name.Contains(".Runtime", StringComparison.Ordinal))
            .Select(project => Path.GetDirectoryName(project.FullPath)!)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(file))
            .Distinct(StringComparer.Ordinal)
            .SelectMany(file => RuntimeGeneratedCarrierTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{RelativePath(file)}: {token}"))
            .ToArray();
        var violations = roslynPackageViolations.Concat(runtimeGeneratorReferences).Concat(runtimeSourceHits).ToArray();

        Assert.True(
            violations.Length == 0,
            "Roslyn and generated authoring carriers belong only to the Design code-generation envelope; " +
            "Runtime must consume canonical artifacts:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void FinalZeroMemoryReferenceCheck_CurrentCanonicalHitsDoNotExceedT103MigrationAllowlist()
    {
        var hits = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Elsa"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => new SourceHit(RelativePath(file), MemoryReferencePattern().Matches(File.ReadAllText(file)).Count))
            .Where(hit => hit.Count > 0)
            .OrderBy(hit => hit.Path, StringComparer.Ordinal)
            .ToArray();
        var violations = ExceedsMigrationCeiling(hits, MemoryReferenceMigrationCeilings);

        ReportAllowlisted("Legacy memory-reference hits that T103 must reduce to zero", hits.Where(IsAllowlistedMemoryHit));
        Assert.True(
            violations.Length == 0,
            "Canonical Elsa source must not add IMemoryBlock, IMemoryBlockReference, or IMemoryRegister references. " +
            "Unexpected or expanded hits:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(FormatHit)));
    }

    private void ReportAllowlisted<T>(string heading, IEnumerable<T> values)
    {
        var entries = values.Select(value => value?.ToString()).Where(value => value is not null).ToArray();
        if (entries.Length == 0)
            return;

        output.WriteLine(heading + ":");
        foreach (var entry in entries)
            output.WriteLine("  " + entry);
    }

    private static SourceHit[] ExceedsMigrationCeiling(
        IEnumerable<SourceHit> hits,
        IReadOnlyDictionary<string, int> ceilings) =>
        hits.Where(hit => !ceilings.TryGetValue(hit.Path, out var ceiling) || hit.Count > ceiling).ToArray();

    private static bool IsAllowlistedAssemblyMetadataHit(SourceHit hit) =>
        AssemblyQualifiedMetadataMigrationCeilings.TryGetValue(hit.Path, out var ceiling) && hit.Count <= ceiling;

    private static bool IsAllowlistedMemoryHit(SourceHit hit) =>
        MemoryReferenceMigrationCeilings.TryGetValue(hit.Path, out var ceiling) && hit.Count <= ceiling;

    private static string FormatEdge((string Project, string Reference) edge) => $"{edge.Project} -> {edge.Reference}";

    private static string FormatHit(SourceHit hit) => $"{hit.Path}: {hit.Count}";

    private static IEnumerable<ProjectInfo> ProductionProjects() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Elsa"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => new ProjectInfo(Path.GetFileNameWithoutExtension(path), path));

    private static IEnumerable<string> ProjectReferences(ProjectInfo project)
    {
        var document = XDocument.Load(project.FullPath);
        return document.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar)));
    }

    private static IEnumerable<PackageReferenceInfo> PackageReferences(ProjectInfo project)
    {
        var document = XDocument.Load(project.FullPath);
        return document.Descendants("PackageReference")
            .Select(reference => new PackageReferenceInfo(
                reference.Attribute("Include")?.Value ?? string.Empty,
                string.Equals(
                    reference.Attribute("PrivateAssets")?.Value ?? reference.Element("PrivateAssets")?.Value,
                    "all",
                    StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string NormalizePath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    [GeneratedRegex(
        "AssemblyQualifiedName|GetSimpleAssemblyQualifiedName|JsonPropertyName\\s*\\(\\s*\"typeName\"\\s*\\)|InputTypeMetadataKey\\s*=\\s*\"typeName\"|Assembly\\.GetName\\(\\)\\.Name",
        RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyQualifiedMetadataPattern();

    [GeneratedRegex(@"\bIMemory(?:Block|BlockReference|Register)\b", RegexOptions.CultureInvariant)]
    private static partial Regex MemoryReferencePattern();

    private sealed record ProjectInfo(string Name, string FullPath);

    private sealed record PackageReferenceInfo(string Name, bool PrivateAssetsAll);

    private sealed record SourceHit(string Path, int Count)
    {
        public override string ToString() => $"{Path}: {Count}";
    }
}
