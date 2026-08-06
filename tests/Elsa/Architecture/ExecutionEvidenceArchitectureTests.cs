using System.Reflection;
using System.Xml.Linq;
using Elsa.Testing.Reflection;
using Elsa.Workflows.ExecutionEvidence;
using Elsa.Workflows.ExecutionEvidence.Api;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class ExecutionEvidenceArchitectureTests
{
    private static readonly HashSet<(string Project, string Reference)> AllowedRuntimeDesignReferences =
    [
        ("src/Elsa/Workflows/Runtime/JavaScript/Elsa.Workflows.Runtime.JavaScript.csproj", "Elsa.Workflows.Design.Core")
    ];

    private const string CoreProject = "src/Elsa/Workflows/ExecutionEvidence/Core/Elsa.Workflows.ExecutionEvidence.Core.csproj";
    private const string BaseProject = "src/Elsa/Workflows/ExecutionEvidence/Elsa.Workflows.ExecutionEvidence.csproj";
    private const string InMemoryProject = "src/Elsa/Workflows/ExecutionEvidence/InMemory/Elsa.Workflows.ExecutionEvidence.InMemory.csproj";
    private const string ApiProject = "src/Elsa/Workflows/ExecutionEvidence/Api/Elsa.Workflows.ExecutionEvidence.Api.csproj";

    [Fact]
    public void Execution_evidence_uses_the_required_four_project_envelopes()
    {
        foreach (var project in new[] { CoreProject, BaseProject, InMemoryProject, ApiProject })
            Assert.True(File.Exists(Path.Combine(RepoRoot, project)), $"Missing required Execution Evidence project: {project}");

        var solutionProjects = XDocument.Load(Path.Combine(RepoRoot, "Elsa.Server.slnx"))
            .Descendants("Folder")
            .SelectMany(folder => folder.Elements("Project").Select(project => new
            {
                Folder = folder.Attribute("Name")?.Value,
                Path = project.Attribute("Path")?.Value?.Replace('\\', '/')
            }))
            .Where(project => project.Path is not null)
            .ToDictionary(project => project.Path!, project => project.Folder, StringComparer.Ordinal);

        foreach (var project in new[] { CoreProject, BaseProject, InMemoryProject, ApiProject })
            Assert.Equal("/src/Elsa/Workflows/ExecutionEvidence/", solutionProjects[project]);

        Assert.Equal("/tests/Elsa/Workflows/ExecutionEvidence/", solutionProjects["tests/Elsa/Workflows/ExecutionEvidence/Tests/Elsa.Workflows.ExecutionEvidence.Tests.csproj"]);
        Assert.Equal("/tests/Elsa/Workflows/ExecutionEvidence/Core/", solutionProjects["tests/Elsa/Workflows/ExecutionEvidence/Core/Tests/Elsa.Workflows.ExecutionEvidence.Core.Tests.csproj"]);
        Assert.Equal("/tests/Elsa/Workflows/ExecutionEvidence/InMemory/", solutionProjects["tests/Elsa/Workflows/ExecutionEvidence/InMemory/Tests/Elsa.Workflows.ExecutionEvidence.InMemory.Tests.csproj"]);
        Assert.Equal("/tests/Elsa/Workflows/ExecutionEvidence/Api/", solutionProjects["tests/Elsa/Workflows/ExecutionEvidence/Api/Tests/Elsa.Workflows.ExecutionEvidence.Api.Tests.csproj"]);

        Assert.Empty(ProjectReferences(CoreProject));
        Assert.Equal(
            ["Elsa.Tasks.Core", "Elsa.Workflows.ExecutionEvidence.Core", "Elsa.Workflows.Runtime.Core"],
            ProjectReferences(BaseProject).Select(ProjectName).Order());
        Assert.Equal(
            ["Elsa.Workflows.ExecutionEvidence", "Elsa.Workflows.ExecutionEvidence.Core"],
            ProjectReferences(InMemoryProject).Select(ProjectName).Order());
    }

    [Fact]
    public void Api_composes_the_base_feature_without_the_inmemory_leaf()
    {
        var apiReferences = ProjectReferences(ApiProject).ToArray();
        var transitiveReferences = TransitiveProjectReferences(ApiProject).ToArray();
        var apiFeatureType = typeof(WorkflowsExecutionEvidenceApiFeature);
        var overrideMethod = apiFeatureType.GetMethod(
            nameof(WorkflowsExecutionEvidenceApiFeature.ConfigureServices),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
        var baseMethod = typeof(WorkflowsExecutionEvidenceFeature).GetMethod(
            nameof(WorkflowsExecutionEvidenceFeature.ConfigureServices),
            BindingFlags.Instance | BindingFlags.Public)!;
        var calledMethods = MethodCallInspector.GetCalledMethods(overrideMethod);

        Assert.Contains(apiReferences, reference => ProjectName(reference) == "Elsa.Workflows.ExecutionEvidence.Core");
        Assert.Contains(apiReferences, reference => ProjectName(reference) == "Elsa.Workflows.ExecutionEvidence");
        Assert.DoesNotContain(transitiveReferences, reference => ProjectName(reference) == "Elsa.Workflows.ExecutionEvidence.InMemory");
        Assert.Equal(typeof(WorkflowsExecutionEvidenceFeature), apiFeatureType.BaseType);
        Assert.Contains(calledMethods, method => method.Module == baseMethod.Module && method.MetadataToken == baseMethod.MetadataToken);
        Assert.DoesNotContain(calledMethods, MethodCallInspector.IsServiceLocatorOrProviderBuildCall);
    }

    [Fact]
    public void Runtime_has_no_execution_evidence_edge_and_no_new_design_edge()
    {
        var runtimeProjects = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Elsa", "Workflows", "Runtime"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();
        var runtimeReferences = runtimeProjects
            .SelectMany(project => ProjectReferences(project).Select(reference => (Project: project, Reference: ProjectName(reference))))
            .ToArray();
        var unexpectedDesignReferences = runtimeReferences
            .Where(reference =>
                reference.Reference.StartsWith("Elsa.Workflows.Design.", StringComparison.Ordinal) ||
                reference.Reference.StartsWith("Elsa.Activities.Design.", StringComparison.Ordinal))
            .Where(reference => !AllowedRuntimeDesignReferences.Contains(reference))
            .ToArray();

        Assert.DoesNotContain(runtimeReferences, reference => reference.Reference.StartsWith("Elsa.Workflows.ExecutionEvidence", StringComparison.Ordinal));
        Assert.Empty(unexpectedDesignReferences);
    }

    private static IEnumerable<string> TransitiveProjectReferences(string project)
    {
        var pending = new Stack<string>(ProjectReferences(project));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.TryPop(out var reference))
        {
            if (!visited.Add(reference))
                continue;

            yield return reference;

            foreach (var next in ProjectReferences(reference))
                pending.Push(next);
        }
    }

    private static IEnumerable<string> ProjectReferences(string project)
    {
        var fullPath = Path.Combine(RepoRoot, project);
        var document = XDocument.Load(fullPath);
        var projectDirectory = Path.GetDirectoryName(fullPath)!;

        return document.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include => include.Replace('\\', Path.DirectorySeparatorChar))
            .Select(include => Path.GetRelativePath(RepoRoot, Path.GetFullPath(Path.Combine(projectDirectory, include))).Replace(Path.DirectorySeparatorChar, '/'));
    }

    private static string ProjectName(string project) => Path.GetFileNameWithoutExtension(project);

    private static string RepoRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException("Could not find the repository root.");
        }
    }
}
