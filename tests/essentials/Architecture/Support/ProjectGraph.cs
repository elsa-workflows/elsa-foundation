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

    /// <summary>Every Elsa project under <c>src/</c>, by name, with the Elsa projects it references directly.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadElsaReferences(string repoRoot)
    {
        var namesByPath = ModuleRoots.Resolve(repoRoot, ModuleRoots.Production)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(ModuleRoots.IsNotTestFile)
            .Where(path => IsElsa(Path.GetFileNameWithoutExtension(path)))
            .ToDictionary(Path.GetFullPath, Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase);

        return namesByPath.ToDictionary(
            project => project.Value,
            IReadOnlyList<string> (project) =>
                [.. ReferencedProjectPaths(project.Key).Select(namesByPath.GetValueOrDefault).OfType<string>()],
            StringComparer.OrdinalIgnoreCase);
    }
}
