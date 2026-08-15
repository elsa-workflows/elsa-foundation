using System.Xml.Linq;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Transitions;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class FastEndpointsTransitionTests
{
    [Fact]
    public void First_party_FastEndpoints_surface_matches_the_exact_reviewed_transition_registry()
    {
        var registrations = DiscoverRegistrations();
        Assert.NotEmpty(registrations);

        var baselinePath = Path.Join(RepoRoot, "tests", "Elsa", "Architecture", "Baselines", "fastendpoints-transition-exceptions.json");
        var reviewed = BaselineFile.Load<FastEndpointsTransitionException[]>(baselinePath);

        var result = TransitionExceptionValidator.Reconcile(registrations, reviewed);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue =>
            $"{issue.Code}: {issue.RegistrationIdentity}: {issue.Message}")));
    }

    private static IReadOnlyList<FastEndpointsRegistration> DiscoverRegistrations()
    {
        var scanner = new FastEndpointsRegistrationScanner();
        var documents = Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(IsRepositorySource)
            .Order(StringComparer.Ordinal)
            .Select(path => new FastEndpointsSourceDocument(
                Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(path),
                FindProjectOwner(path),
                DynamicallyUnloadable: false,
                SourcePath: Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/')));
        return scanner.Scan(documents)
            .Where(registration => registration.Endpoints.Count > 0 || registration.DynamicRoute)
            .ToArray();
    }

    private static bool IsRepositorySource(string path)
    {
        var relativePath = Path.GetRelativePath(Path.Join(RepoRoot, "src"), path);
        return !relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");
    }

    private static string FindProjectOwner(string sourcePath)
    {
        var directory = Directory.GetParent(sourcePath);
        while (directory is not null && !string.Equals(directory.FullName, RepoRoot, StringComparison.Ordinal))
        {
            var project = directory.EnumerateFiles("*.csproj", SearchOption.TopDirectoryOnly).SingleOrDefault();
            if (project is not null)
            {
                var assemblyName = XDocument.Load(project.FullName).Descendants("AssemblyName").SingleOrDefault()?.Value;
                return string.IsNullOrWhiteSpace(assemblyName) ? Path.GetFileNameWithoutExtension(project.Name) : assemblyName.Trim();
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"No owning project was found for '{sourcePath}'.");
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
