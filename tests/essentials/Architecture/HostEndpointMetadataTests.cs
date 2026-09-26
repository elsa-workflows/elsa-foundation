using Elsa.Api.AspNetCore;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps retained root-host mappings tied to the typed host metadata contract. The runtime manifest test
/// verifies the published route count; these source guards keep a future mapper edit from silently dropping
/// the convention at its declaration site.
/// </summary>
public sealed class HostEndpointMetadataTests
{
    [Fact]
    public void Workbench_retained_mappers_declare_host_ownership_and_security_disposition()
    {
        AssertSourceContains(
            "src/apps/Elsa.Workbench/Program.cs",
            "WithHostOwner(\"Elsa.Workbench\")",
            "WithAuthoringModel(EndpointAuthoringModels.MinimalApi)",
            "AllowPublic(\"health\"",
            "MapShellManagementApi(\"/_admin/shells\")",
            "ADR 0037",
            "WithHostCredentialEnforcement(ManagementApiKeyAuthentication.HeaderName, \"Elsa.Workbench\")",
            "RequireAsync",
            "NamedPolicy(\"Default\", \"Elsa.Workbench\")");

        AssertSourceContains(
            "src/apps/Elsa.Workbench/Readiness/ShellReadinessEndpointExtensions.cs",
            "WithHostOwner(\"Elsa.Workbench\")",
            "WithAuthoringModel(EndpointAuthoringModels.MinimalApi)",
            "AllowPublic(\"health\"");

        AssertSourceContains(
            "src/apps/Elsa.Workbench/ElsaModuleManagementApi.cs",
            "WithHostOwner(\"Elsa.Workbench\")",
            "EndpointSecurityDispositionMetadata.HostCredential(\n                ManagementApiKeyAuthentication.HeaderName",
            "WithHostCredentialEnforcement(ManagementApiKeyAuthentication.HeaderName, \"Elsa.Workbench\")");
    }

    [Fact]
    public void Foundation_host_retained_mappers_declare_host_ownership_and_security_disposition()
    {
        AssertSourceContains(
            "src/apps/Elsa.Foundation.Host/Health/HealthEndpoints.cs",
            "WithHostOwner(\"Elsa.Foundation.Host\")",
            "WithAuthoringModel(EndpointAuthoringModels.MinimalApi)",
            "AllowPublic(\"health\"");

        AssertSourceContains(
            "src/apps/Elsa.Foundation.Host/ModuleManagement/ModuleManagementEndpoints.cs",
            "WithHostOwner(\"Elsa.Foundation.Host\")",
            "EndpointSecurityDispositionMetadata.HostCredential(\n                ModuleManagementOptions.ApiKeyHeader",
            "WithHostCredentialEnforcement(ModuleManagementOptions.ApiKeyHeader, \"Elsa.Foundation.Host\")",
            "CryptographicOperations.FixedTimeEquals");

        AssertSourceContains(
            "src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj",
            "Elsa.Api.AspNetCore.csproj");
    }

    [Fact]
    public void Foundation_host_Dockerfile_includes_its_project_reference_graph()
    {
        var projectPath = Path.Combine(RepoRoot, "src", "apps", "Elsa.Foundation.Host", "Elsa.Foundation.Host.csproj");
        var dockerfile = ReadSource("src/apps/Elsa.Foundation.Host/Dockerfile");
        var projectReferences = DiscoverProjectReferences(projectPath);

        Assert.NotEmpty(projectReferences);
        foreach (var referencedProjectPath in projectReferences)
        {
            var relativeProjectPath = Path.GetRelativePath(RepoRoot, referencedProjectPath).Replace('\\', '/');
            var relativeProjectDirectory = Path.GetDirectoryName(relativeProjectPath)!.Replace('\\', '/');

            Assert.Contains($"COPY {relativeProjectPath} {relativeProjectDirectory}/", dockerfile, StringComparison.Ordinal);
            Assert.Contains($"COPY {relativeProjectDirectory}/ {relativeProjectDirectory}/", dockerfile, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Retained_host_mappers_do_not_introduce_foundation_user_permissions()
    {
        var paths = new[]
        {
            "src/apps/Elsa.Workbench/Program.cs",
            "src/apps/Elsa.Workbench/Readiness/ShellReadinessEndpointExtensions.cs",
            "src/apps/Elsa.Workbench/ElsaModuleManagementApi.cs",
            "src/apps/Elsa.Foundation.Host/Health/HealthEndpoints.cs",
            "src/apps/Elsa.Foundation.Host/ModuleManagement/ModuleManagementEndpoints.cs"
        };

        foreach (var path in paths)
            Assert.DoesNotContain("RequirePermission", ReadSource(path), StringComparison.Ordinal);
    }

    private static void AssertSourceContains(string relativePath, params string[] fragments)
    {
        var source = ReadSource(relativePath);
        foreach (var fragment in fragments)
            Assert.Contains(fragment, source, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static IReadOnlyCollection<string> DiscoverProjectReferences(string rootProjectPath)
    {
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(Path.GetFullPath(rootProjectPath));

        while (pending.TryDequeue(out var projectPath))
        {
            foreach (var referencedProjectPath in ProjectGraph.ReferencedProjectPaths(projectPath))
            {
                if (discovered.Add(referencedProjectPath))
                    pending.Enqueue(referencedProjectPath);
            }
        }

        return discovered;
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
        }
    }
}
