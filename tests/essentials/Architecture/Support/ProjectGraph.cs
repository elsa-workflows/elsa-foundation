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
}
