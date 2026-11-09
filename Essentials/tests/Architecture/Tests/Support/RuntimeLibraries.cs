using System.Reflection;
using System.Xml.Linq;
using CShells.Features;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// The compiled first-party runtime libraries this test assembly references, so guards can inspect compiled code instead
/// of source text. <c>FeatureCompositionTests</c> fails when a packable library is missing from the references, which is
/// what keeps every sweep over <see cref="Assemblies"/> complete.
/// </summary>
internal static class RuntimeLibraries
{
    internal sealed record Library(string Name, string ProjectPath, Assembly? Assembly);

    /// <summary>
    /// Every project under <c>src/</c> that ships as a runtime library: packable, and neither an executable (a host or a
    /// dotnet tool) nor a Roslyn component, none of which a shell loads.
    /// </summary>
    internal static IReadOnlyList<Library> Packable => PackableLibraries.Value;

    /// <summary>The loaded assemblies of <see cref="Packable"/>.</summary>
    internal static IReadOnlyList<Assembly> Assemblies => [.. Packable.Select(library => library.Assembly).OfType<Assembly>()];

    /// <summary>Every concrete feature type in the packable runtime libraries, public or not.</summary>
    internal static IReadOnlyList<Type> FeatureTypes => Features.Value;

    private static readonly Lazy<IReadOnlyList<Library>> PackableLibraries = new(() =>
        ModuleRoots.Resolve(RepoRoot, ModuleRoots.Production)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => ModuleRoots.IsNotTestFile(RepoRoot, path) && !IsBuildOutput(path))
            .Select(path => (Path: path, Project: XDocument.Load(path)))
            .Where(project => IsPackableRuntimeLibrary(project.Project))
            .Select(project =>
            {
                var name = Property(project.Project, "AssemblyName") ?? Path.GetFileNameWithoutExtension(project.Path);
                return new Library(name, Path.GetRelativePath(RepoRoot, project.Path).Replace('\\', '/'), TryLoad(name));
            })
            .OrderBy(library => library.Name, StringComparer.Ordinal)
            .ToArray());

    private static readonly Lazy<IReadOnlyList<Type>> Features = new(() =>
        Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IShellFeature).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray());

    private static bool IsPackableRuntimeLibrary(XDocument project) =>
        !string.Equals(Property(project, "IsPackable"), "false", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Property(project, "OutputType"), "Exe", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Property(project, "IsRoslynComponent"), "true", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Property(project, "PackageType"), "Template", StringComparison.OrdinalIgnoreCase);

    private static string? Property(XDocument project, string name) =>
        project.Descendants(name).Select(element => element.Value.Trim()).LastOrDefault();

    private static Assembly? TryLoad(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
