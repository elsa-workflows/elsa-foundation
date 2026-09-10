using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Owns the ADR 0072 Secrets EF pilot allowlist. The shrink-only EF surface baseline excludes these
/// paths the same way it excludes OpenIddict vendor sources; this test is the exact inventory.
/// ADR 0042 still forbids first-party EF until 0072 is accepted.
/// </summary>
public sealed class SecretsEfPersistencePilotArchitectureTests
{
    private static readonly string[] PolicyPackages =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational"
    ];

    private static readonly string[] ModulePackages =
    [
        "CShells.Abstractions",
        "Elsa.Platform.PackageManifest.Generator",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting.Abstractions"
    ];

    private static readonly string[] ForbiddenProviderPackages =
    [
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Npgsql.EntityFrameworkCore.PostgreSQL"
    ];

    [Fact]
    public void Pilot_paths_are_the_reviewed_adr_0072_allowlist()
    {
        Assert.Equal(
            [
                "src/Elsa/Persistence/EntityFramework/",
                "src/Elsa/Secrets/Persistence/EntityFrameworkCore/",
                "tests/Elsa/Persistence/EntityFramework/",
                "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/"
            ],
            EfCoreSurfaceScanner.Adr0072SecretsEfPilot.SurfacePathPrefixes);
    }

    [Fact]
    public void Policy_and_module_packages_stay_provider_free()
    {
        var policy = PackageIncludes(Path.Combine(
            RepoRoot,
            "src/Elsa/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj"));
        var module = PackageIncludes(Path.Combine(
            RepoRoot,
            "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"));

        Assert.Equal(PolicyPackages, policy);
        Assert.Equal(ModulePackages, module);
        Assert.Empty(policy.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
        Assert.Empty(module.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void Tooling_project_is_the_only_secrets_ef_package_that_references_provider_engines()
    {
        var tooling = PackageIncludes(Path.Combine(
            RepoRoot,
            "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj"));
        Assert.Contains("Microsoft.EntityFrameworkCore.Design", tooling);
        Assert.Contains("Microsoft.EntityFrameworkCore.Sqlite", tooling);
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", tooling);
        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", tooling);
    }

    [Fact]
    public void Shrink_only_snapshot_does_not_inventory_the_pilot_trees()
    {
        var snapshot = new EfCoreSurfaceScanner(RepoRoot).Scan();
        var leaked = snapshot.Categories()
            .SelectMany(category => category.Value.Select(entry => $"{category.Key}: {entry}"))
            .Where(entry => EfCoreSurfaceScanner.Adr0072SecretsEfPilot.SurfacePathPrefixes.Any(prefix =>
                entry.Contains(prefix, StringComparison.Ordinal)))
            .ToArray();
        Assert.True(leaked.Length == 0, string.Join(Environment.NewLine, leaked));
    }

    [Fact]
    public void Workbench_does_not_reference_the_secrets_ef_module()
    {
        var workbench = XDocument.Load(Path.Combine(RepoRoot, "src/Apps/Elsa.Workbench/Elsa.Workbench.csproj"));
        var references = workbench.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();
        Assert.DoesNotContain(
            references,
            reference => reference.Contains("Secrets.Persistence.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> PackageIncludes(string csproj) =>
        XDocument.Load(csproj)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }
}
