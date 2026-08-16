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
            "src/Apps/Elsa.Workbench/Program.cs",
            "WithHostOwner(\"Elsa.Workbench\")",
            "WithAuthoringModel(EndpointAuthoringModels.MinimalApi)",
            "AllowPublic(\"health\"",
            "MapShellManagementApi(\"/_admin/shells\")",
            "ADR 0037",
            "WithHostCredentialEnforcement(ManagementApiKeyAuthentication.HeaderName, \"Elsa.Workbench\")",
            "RequireAsync",
            "NamedPolicy(\"Default\", \"Elsa.Workbench\")");

        AssertSourceContains(
            "src/Apps/Elsa.Workbench/Readiness/ShellReadinessEndpointExtensions.cs",
            "WithHostOwner(\"Elsa.Workbench\")",
            "WithAuthoringModel(EndpointAuthoringModels.MinimalApi)",
            "AllowPublic(\"health\"");

        AssertSourceContains(
            "src/Apps/Elsa.Workbench/ElsaModuleManagementApi.cs",
            "WithHostOwner(\"Elsa.Workbench\")",
            "EndpointSecurityDispositionMetadata.HostCredential(\n                ManagementApiKeyAuthentication.HeaderName",
            "WithHostCredentialEnforcement(ManagementApiKeyAuthentication.HeaderName, \"Elsa.Workbench\")");

        AssertSourceContains(
            "src/Elsa/Modularity/ExtensionBuilder/ExtensionBuilderApi.cs",
            "WithHostOwner(\"Elsa.Workbench\")",
            "EndpointSecurityDispositionMetadata.HostCredential(\n                ManagementApiKeyAuthentication.HeaderName",
            "WithHostCredentialEnforcement(ManagementApiKeyAuthentication.HeaderName, \"Elsa.Workbench\")",
            "RequireTrustedCallerAsync");
    }

    [Fact]
    public void Foundation_host_retained_mappers_declare_host_ownership_and_security_disposition()
    {
        AssertSourceContains(
            "src/Apps/Elsa.Foundation.Host/Health/HealthEndpoints.cs",
            "WithHostOwner(\"Elsa.Foundation.Host\")",
            "WithAuthoringModel(EndpointAuthoringModels.MinimalApi)",
            "AllowPublic(\"health\"");

        AssertSourceContains(
            "src/Apps/Elsa.Foundation.Host/ModuleManagement/ModuleManagementEndpoints.cs",
            "WithHostOwner(\"Elsa.Foundation.Host\")",
            "EndpointSecurityDispositionMetadata.HostCredential(\n                ModuleManagementOptions.ApiKeyHeader",
            "WithHostCredentialEnforcement(ModuleManagementOptions.ApiKeyHeader, \"Elsa.Foundation.Host\")",
            "CryptographicOperations.FixedTimeEquals");

        AssertSourceContains(
            "src/Apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj",
            "Elsa.Api.AspNetCore.csproj");
    }

    [Fact]
    public void Retained_host_mappers_do_not_introduce_foundation_user_permissions()
    {
        var paths = new[]
        {
            "src/Apps/Elsa.Workbench/Program.cs",
            "src/Apps/Elsa.Workbench/Readiness/ShellReadinessEndpointExtensions.cs",
            "src/Apps/Elsa.Workbench/ElsaModuleManagementApi.cs",
            "src/Elsa/Modularity/ExtensionBuilder/ExtensionBuilderApi.cs",
            "src/Apps/Elsa.Foundation.Host/Health/HealthEndpoints.cs",
            "src/Apps/Elsa.Foundation.Host/ModuleManagement/ModuleManagementEndpoints.cs"
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
