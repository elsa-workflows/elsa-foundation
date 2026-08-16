using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Transitions;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class FastEndpointsTransitionTests
{
    [Fact]
    public void First_party_FastEndpoints_surface_matches_the_exact_reviewed_transition_registry()
    {
        var registrations = DiscoverRegistrations();
        Assert.NotEmpty(registrations);
        Assert.Equal(112, registrations.Count);
        Assert.Equal(4, registrations.Select(registration => registration.Owner).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Elsa.Activities.Design.Api"] = 38,
                ["Elsa.Workflows.Design.Api"] = 27,
                ["Elsa.Workflows.Publishing.Api"] = 23,
                ["Elsa.Workflows.Runtime.Api"] = 24
            },
            registrations.GroupBy(registration => registration.Owner, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));

        var baselinePath = Path.Join(RepoRoot, "tests", "Elsa", "Architecture", "Baselines", "fastendpoints-transition-exceptions.json");
        var reviewed = BaselineFile.Load<FastEndpointsTransitionException[]>(baselinePath);
        Assert.All(reviewed, exception =>
        {
            Assert.True(ExpectedRemovalWaves.TryGetValue(exception.Owner, out var expectedWave),
                $"No executable removal wave is assigned to owner '{exception.Owner}'.");
            Assert.Equal(expectedWave, exception.FollowUp);
            Assert.NotEqual("#1350", exception.FollowUp);
        });

        var result = TransitionExceptionValidator.Reconcile(registrations, reviewed);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue =>
            $"{issue.Code}: {issue.RegistrationIdentity}: {issue.Message}")));
    }

    [Fact]
    public void FastEndpoints_retirement_mode_rejects_reviewed_registrations_until_the_surface_is_empty()
    {
        var result = TransitionExceptionValidator.ValidateRetirement(DiscoverRegistrations());

        if (Environment.GetEnvironmentVariable("ELSA_FASTENDPOINTS_RETIREMENT_MODE") == "1")
        {
            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue =>
                $"{issue.Code}: {issue.RegistrationIdentity}: {issue.Message}")));
            return;
        }

        Assert.Equal(112, result.Issues.Count);
        Assert.All(result.Issues, issue => Assert.Equal("FirstPartyFastEndpointsRegistration", issue.Code));
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

    private static IReadOnlyDictionary<string, string> ExpectedRemovalWaves { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Elsa.Activities.Bpmn.Interchange"] = "#1368",
            ["Elsa.Activities.Design.Api"] = "#1373",
            ["Elsa.Agent.Api"] = "#1370",
            ["Elsa.Api.Capabilities"] = "#1367",
            ["Elsa.Attention.Api"] = "#1367",
            ["Elsa.Diagnostics.OpenTelemetry"] = "#1371",
            ["Elsa.Expressions.Api"] = "#1367",
            ["Elsa.Expressions.JavaScript.Rendering"] = "#1367",
            ["Elsa.Foundation.Identity.Api"] = "#1369",
            ["Elsa.Foundation.Identity.AspNetCoreIdentity"] = "#1369",
            ["Elsa.Modularity.Api"] = "#1368",
            ["Elsa.Workflows.Dashboard"] = "#1367",
            ["Elsa.Workflows.Design.Api"] = "#1372",
            ["Elsa.Workflows.ExecutionEvidence"] = "#1368",
            ["Elsa.Workflows.Publishing.Api"] = "#1374",
            ["Elsa.Workflows.Runtime.Api"] = "#1375",
            ["Elsa.Workflows.Runtime.JavaScript"] = "#1367",
            ["Elsa3.Activities.Design.Import"] = "#1368"
        };

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
