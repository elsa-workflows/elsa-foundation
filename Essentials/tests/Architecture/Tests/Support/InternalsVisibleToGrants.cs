using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Finds every <c>InternalsVisibleTo</c> grant under a repository root, in all three forms the build honors, and judges
/// each against the rule that an assembly opens its internals only to its own domain's test assemblies.
/// </summary>
/// <remarks>
/// The forms are the MSBuild <c>&lt;InternalsVisibleTo Include="..."/&gt;</c> item, an
/// <c>&lt;AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo"&gt;</c> item, and an
/// <c>[assembly: InternalsVisibleTo("...")]</c> attribute in a source file. Reading only the first, as the guard this
/// replaces did, let the other two pass unseen.
/// </remarks>
internal static class InternalsVisibleToGrants
{
    private const string AttributeTypeName = "System.Runtime.CompilerServices.InternalsVisibleTo";

    /// <summary>One grant: the project that opens its internals, the assembly it opens them to, and where it says so.</summary>
    internal sealed record Grant(string Project, string ProjectPath, string Target, string DeclaredIn);

    /// <summary>Every grant declared by a project under <see cref="ModuleRoots.All"/> of <paramref name="repoRoot"/>.</summary>
    internal static IReadOnlyList<Grant> Find(string repoRoot) =>
        [.. Projects(repoRoot).SelectMany(project => GrantsOf(repoRoot, project))];

    /// <summary>
    /// Every grant that breaks the rule, plus every shared build file that declares a grant on behalf of the projects
    /// importing it, as human-readable lines.
    /// </summary>
    internal static IReadOnlyList<string> Violations(string repoRoot, IReadOnlySet<(string Project, string Target)> exceptions)
    {
        var assemblies = Projects(repoRoot)
            .GroupBy(project => AssemblyName(project), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Relative(repoRoot, group.First()), StringComparer.Ordinal);

        var grantViolations = Find(repoRoot)
            .Where(grant => !exceptions.Contains((grant.Project, grant.Target)))
            .Select(grant => Judge(grant, assemblies))
            .OfType<string>();

        var sharedViolations = SharedBuildFiles(repoRoot)
            .Where(path => MsBuildGrantTargets(XDocument.Load(path)).Any())
            .Select(path => $"{Relative(repoRoot, path)} declares InternalsVisibleTo for every project that imports it; declare it in the granting project instead");

        return [.. grantViolations.Concat(sharedViolations).Order(StringComparer.Ordinal)];
    }

    private static string? Judge(Grant grant, IReadOnlyDictionary<string, string> assemblies)
    {
        var granting = $"{grant.Project} -> {grant.Target} ({grant.DeclaredIn})";

        if (!assemblies.TryGetValue(grant.Target, out var targetPath))
            return $"{granting}: names no project of this repository";

        if (!ElsaDomains.IsTestPath(targetPath))
            return $"{granting}: {targetPath} is not a test project; a project never opens its internals to another production project";

        var ownDomain = ElsaDomains.Of(grant.ProjectPath);
        if (ownDomain is null)
            return $"{granting}: {grant.ProjectPath} belongs to no domain, so it has no own test assemblies to grant to";

        var targetDomain = ElsaDomains.Of(targetPath);
        return targetDomain == ownDomain
            ? null
            : $"{granting}: {targetPath} tests domain '{targetDomain}', not the granting project's domain '{ownDomain}'";
    }

    private static IEnumerable<Grant> GrantsOf(string repoRoot, string projectPath)
    {
        var project = AssemblyName(projectPath);
        var relativeProject = Relative(repoRoot, projectPath);

        foreach (var target in MsBuildGrantTargets(XDocument.Load(projectPath)))
            yield return new Grant(project, relativeProject, target, relativeProject);

        foreach (var source in SourceFiles(repoRoot, projectPath))
        {
            foreach (var target in AssemblyAttributeTargets(File.ReadAllText(source)))
                yield return new Grant(project, relativeProject, target, Relative(repoRoot, source));
        }
    }

    /// <summary>
    /// The assemblies a source file grants to through <c>[assembly: InternalsVisibleTo("...")]</c>, read from the parsed
    /// syntax so the same text inside a comment or a string literal is not mistaken for a grant.
    /// </summary>
    private static IEnumerable<string> AssemblyAttributeTargets(string source)
    {
        if (!source.Contains("InternalsVisibleTo", StringComparison.Ordinal))
            return [];

        return CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<AttributeListSyntax>()
            .Where(list => list.Target?.Identifier.ValueText == "assembly")
            .SelectMany(list => list.Attributes)
            .Where(attribute => RightmostName(attribute.Name) is "InternalsVisibleTo" or "InternalsVisibleToAttribute")
            .Select(attribute => attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Select(literal => literal.Token.ValueText.Split(',')[0].Trim())
            .Where(target => target.Length > 0);
    }

    private static string RightmostName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => name.ToString()
    };

    /// <summary>The assemblies an MSBuild file grants to through either item form.</summary>
    private static IEnumerable<string> MsBuildGrantTargets(XDocument document)
    {
        var items = document.Descendants()
            .Where(element => element.Name.LocalName == "InternalsVisibleTo")
            .SelectMany(element => (element.Attribute("Include")?.Value ?? string.Empty).Split(';'));

        var attributes = document.Descendants()
            .Where(element => element.Name.LocalName == "AssemblyAttribute" &&
                              (element.Attribute("Include")?.Value.Trim() is AttributeTypeName or AttributeTypeName + "Attribute"))
            .Select(element => element.Attribute("_Parameter1")?.Value ??
                               element.Elements().FirstOrDefault(child => child.Name.LocalName == "_Parameter1")?.Value ??
                               string.Empty);

        return items.Concat(attributes)
            .Select(target => target.Split(',')[0].Trim())
            .Where(target => target.Length > 0);
    }

    /// <summary>The project's own source files: its directory tree, without build output or a nested project's tree.</summary>
    private static IEnumerable<string> SourceFiles(string repoRoot, string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var nested = Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .Where(directory => !string.Equals(directory, projectDirectory, StringComparison.Ordinal))
            .ToArray();

        return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !RepoPaths.IsBuildOutput(repoRoot, path))
            .Where(path => !nested.Any(directory => path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    /// <summary>Every project file under <see cref="ModuleRoots.All"/>, build output excluded.</summary>
    private static IEnumerable<string> Projects(string repoRoot) =>
        ModuleRoots.Resolve(repoRoot, ModuleRoots.All)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !RepoPaths.IsBuildOutput(repoRoot, path));

    /// <summary>The repository-root build files and every <c>Directory.Build.*</c> and <c>.targets</c> file under the module roots.</summary>
    private static IEnumerable<string> SharedBuildFiles(string repoRoot) =>
        Directory.EnumerateFiles(repoRoot, "*.props").Concat(Directory.EnumerateFiles(repoRoot, "*.targets"))
            .Concat(ModuleRoots.Resolve(repoRoot, ModuleRoots.All)
                .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                .Where(path => path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)))
            .Where(path => !RepoPaths.IsBuildOutput(repoRoot, path))
            .Distinct(StringComparer.Ordinal);

    private static string AssemblyName(string projectPath) =>
        XDocument.Load(projectPath).Descendants().LastOrDefault(element => element.Name.LocalName == "AssemblyName")?.Value.Trim() is { Length: > 0 } name
            ? name
            : Path.GetFileNameWithoutExtension(projectPath);

    private static string Relative(string repoRoot, string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
}
