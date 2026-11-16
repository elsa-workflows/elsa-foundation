using System.Xml.Linq;

namespace Elsa.Architecture.Tests;

/// <summary>The one way these guards follow a <c>ProjectReference</c> to the project it names.</summary>
internal static class ProjectGraph
{
    /// <summary>
    /// The full path of every project <paramref name="projectPath"/> references, each <c>Include</c> resolved
    /// against the referencing project's own directory, as MSBuild resolves it — never guessed from a file name.
    /// </summary>
    internal static IEnumerable<string> ReferencedProjectPaths(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include => Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(projectPath))!,
                include.Replace('\\', Path.DirectorySeparatorChar))));

    /// <summary>Whether a project name is an Elsa project, by the repository's one naming rule.</summary>
    internal static bool IsElsa(string name) => name == "Elsa" || name.StartsWith("Elsa.", StringComparison.Ordinal);

    /// <summary>Every Elsa project's <c>.csproj</c> path under <see cref="ModuleRoots.Production"/>.</summary>
    internal static IEnumerable<string> ElsaProjectPaths(string repoRoot) =>
        ModuleRoots.Resolve(repoRoot, ModuleRoots.Production)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => ModuleRoots.IsNotTestFile(repoRoot, path))
            .Where(path => IsElsa(Path.GetFileNameWithoutExtension(path)!));

    /// <summary>Every Elsa project under <c>src/</c>, by name, with the Elsa projects it references directly.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadElsaReferences(string repoRoot)
    {
        var namesByPath = ElsaProjectPaths(repoRoot)
            .ToDictionary(Path.GetFullPath, path => Path.GetFileNameWithoutExtension(path)!, StringComparer.OrdinalIgnoreCase);

        return namesByPath.ToDictionary(
            project => project.Value,
            IReadOnlyList<string> (project) =>
                [.. ReferencedProjectPaths(project.Key).Select(namesByPath.GetValueOrDefault).OfType<string>()],
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The projects the root <c>Directory.Build.props</c> adds as a <c>ProjectReference</c> to the projects importing it
    /// (today the naming analyzer, which every production project compiles with), as full paths. They are part of every
    /// production project's graph without appearing in any project file, so a sweep over project files alone misses them.
    /// </summary>
    internal static IReadOnlyList<string> BuildWideProjectReferences(string repoRoot)
    {
        var props = XDocument.Load(Path.Join(repoRoot, "Directory.Build.props"));
        var properties = props.Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.Ordinal);

        return
        [
            .. props.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .OfType<string>()
                .Select(include => Path.GetFullPath(Expand(include)))
        ];

        string Expand(string value)
        {
            for (var depth = 0; value.Contains("$(", StringComparison.Ordinal); depth++)
            {
                if (depth > 8)
                    throw new InvalidOperationException($"Directory.Build.props: '{value}' does not resolve to a path.");

                value = System.Text.RegularExpressions.Regex.Replace(value, @"\$\((?<name>[A-Za-z_][A-Za-z0-9_]*)\)", match =>
                    match.Groups["name"].Value == "MSBuildThisFileDirectory"
                        ? repoRoot + Path.DirectorySeparatorChar
                        : properties.TryGetValue(match.Groups["name"].Value, out var property)
                            ? property
                            : throw new InvalidOperationException(
                                $"Directory.Build.props: a ProjectReference uses $({match.Groups["name"].Value}), which this file does not define."));
            }

            return value.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
