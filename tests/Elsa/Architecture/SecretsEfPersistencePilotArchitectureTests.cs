using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Owns the ADR 0072 Secrets EF pilot allowlist. <see cref="EfCoreDependencyGuardTests"/> exempts these
/// paths, but not the projects that depend on them; this test is the exact inventory.
/// ADR 0073 supersedes ADR 0072's bounded policy but preserves its admitted implementation as the
/// reviewed starting surface; later EF replacements must extend the ratchet with their own evidence.
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

    private static readonly string[] ToolingPackages =
    [
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Npgsql.EntityFrameworkCore.PostgreSQL"
    ];

    private static readonly string[] PilotSources =
    [
        "src/Elsa/Persistence/EntityFramework/EfDatabaseMigrator.cs",
        "src/Elsa/Persistence/EntityFramework/EfMigrateOptions.cs",
        "src/Elsa/Persistence/EntityFramework/EfMigratePolicy.cs",
        "src/Elsa/Persistence/EntityFramework/EfMigrationsHistory.cs",
        "src/Elsa/Persistence/EntityFramework/EfProviderGuard.cs",
        "src/Elsa/Persistence/EntityFramework/EfProviderNames.cs",
        "src/Elsa/Persistence/EntityFramework/EfRelationalProviderBinding.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Configuration/SecretRecordConfiguration.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/DependencyInjection/SecretsEntityFrameworkCoreRegistration.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Entities/SecretRecord.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.Designer.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.Designer.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/SecretsPostgreSqlDbContextModelSnapshot.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.Designer.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.Designer.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/SecretsSqlServerDbContextModelSnapshot.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.Designer.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.Designer.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/SecretsSqliteDbContextModelSnapshot.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsDbContext.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsEfMigrationHostedService.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsEfModule.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsEntityFrameworkCoreFeature.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsPostgreSqlDbContext.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsSqlServerDbContext.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsSqliteDbContext.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Serialization/SecretsEfJson.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/EfSecretRepository.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/SecretDocument.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/SecretRevisionMapper.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionContract.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionException.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsSearchKeys.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsUnicodeOrdinalIgnoreCaseV1.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/UnicodeOrdinalCasingData.Generated.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Program.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/SecretsDesignTimeConnection.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/SecretsPostgreSqlDesignTimeFactory.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/SecretsSqlServerDesignTimeFactory.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/SecretsSqliteDesignTimeFactory.cs",
        "tests/Elsa/Persistence/EntityFramework/Tests/EfDatabaseMigratorTests.cs",
        "tests/Elsa/Persistence/EntityFramework/Tests/EfMigrationsHistoryTests.cs",
        "tests/Elsa/Persistence/EntityFramework/Tests/EfProviderGuardTests.cs",
        "tests/Elsa/Persistence/EntityFramework/Tests/EfRelationalProviderBindingTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Program.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlEfSecretRepositoryTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlSecretsShellJourneyTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgresContainerFixture.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/SecretsPackageFeedProbeTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Support/SecretsPackageFeedProbeRunner.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerContainerFixture.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerEfSecretRepositoryTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsDesignTimeConnectionTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfDualMigrateToolTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfMigrationHostedServiceTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreFeatureTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreShellReloadTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsHostCatalog.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceCompositionTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceHostJourneyTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsProjectionContractTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsSearchKeysTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SqliteEfSecretRepositoryTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/Support/DualMigrateProcessRunner.cs"
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
            EfCoreDependencyGuardTests.Adr0072SecretsEfPilot.SurfacePathPrefixes);
    }

    [Fact]
    public void Policy_and_module_packages_stay_provider_free()
    {
        var policy = PackageIncludes(RepoPath("src", "Elsa", "Persistence", "EntityFramework", "Elsa.Persistence.EntityFramework.csproj"));
        var module = PackageIncludes(RepoPath("src", "Elsa", "Secrets", "Persistence", "EntityFrameworkCore", "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"));

        Assert.Equal(PolicyPackages, policy);
        Assert.Equal(ModulePackages, module);
        Assert.Empty(policy.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
        Assert.Empty(module.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void Tooling_project_is_the_only_secrets_ef_package_that_references_provider_engines()
    {
        var tooling = PackageIncludes(RepoPath(
            "src", "Elsa", "Secrets", "Persistence", "EntityFrameworkCore", "Tooling",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj"));
        Assert.Equal(ToolingPackages, tooling);
    }

    [Fact]
    public void PostgreSql_shell_proof_supplies_exactly_one_relational_provider_package()
    {
        var packages = PackageIncludes(RepoPath(
            "tests", "Elsa", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "Tests",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj"));

        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void Package_feed_probe_has_one_provider_and_no_compile_time_secrets_ef_module_reference()
    {
        var projectPath = RepoPath(
            "tests", "Elsa", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "PackageFeedProbe",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe.csproj");
        var project = XDocument.Load(projectPath);
        var packages = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        var projects = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
        Assert.DoesNotContain(
            projects,
            reference => reference.Contains("Secrets\\Persistence\\EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Module_package_declares_host_integrated_nuplane_loading()
    {
        var projectPath = RepoPath(
            "src", "Elsa", "Secrets", "Persistence", "EntityFrameworkCore",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj");
        var project = XDocument.Load(projectPath);
        var metadataItem = Assert.Single(project.Descendants("None"), element =>
            string.Equals(element.Attribute("Update")?.Value, "nuplane.json", StringComparison.Ordinal));
        Assert.Equal("true", metadataItem.Attribute("Pack")?.Value);
        Assert.Equal("/", metadataItem.Attribute("PackagePath")?.Value);

        using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            RepoPath("src", "Elsa", "Secrets", "Persistence", "EntityFrameworkCore", "nuplane.json")));
        var loading = metadata.RootElement.GetProperty("loading");
        Assert.Equal("HostIntegrated", loading.GetProperty("loadMode").GetString());
        Assert.Equal("DependencyClosure", loading.GetProperty("scope").GetString());
    }

    [Fact]
    public void Pilot_sources_are_the_exact_reviewed_inventory()
    {
        var actual = EfCoreDependencyGuardTests.Adr0072SecretsEfPilot.SurfacePathPrefixes
            .SelectMany(prefix => Directory.EnumerateFiles(RepoPath(prefix.TrimEnd('/').Split('/')), "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/')))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(PilotSources, actual);
    }

    [Fact]
    public void Workbench_catalogs_the_secrets_ef_module_without_enabling_it_by_default()
    {
        var workbench = XDocument.Load(RepoPath("src", "Apps", "Elsa.Workbench", "Elsa.Workbench.csproj"));
        var references = workbench.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();
        Assert.Contains(
            references,
            reference => reference.Contains("Secrets.Persistence.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                         && !reference.Contains("Tooling", StringComparison.OrdinalIgnoreCase));

        foreach (var relative in new[]
                 {
                     "src/Apps/Elsa.Workbench/shells.json",
                     "src/Apps/Elsa.Workbench/shells.baseline.json",
                     "src/Apps/Elsa.Workbench/shells.Production.json",
                     "docker/compose/elsa-workbench.shells.json"
                 })
        {
            Assert.False(
                DefaultShellFeatures(relative).Contains("SecretsEntityFrameworkCore"),
                $"{relative} must not enable SecretsEntityFrameworkCore; Groundwork remains the default.");
        }

        foreach (var relative in new[]
                 {
                     "src/Apps/Elsa.Workbench/shells.json",
                     "docker/compose/elsa-workbench.shells.json"
                 })
        {
            Assert.True(
                DefaultShellFeatures(relative).Contains("SecretsGroundworkPersistence"),
                $"{relative} must keep SecretsGroundworkPersistence as the default Secrets store.");
        }
    }

    private static HashSet<string> DefaultShellFeatures(string relative)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(RepoPath(relative.Split('/'))),
            new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        return document.RootElement
            .GetProperty("CShells").GetProperty("Shells").GetProperty("default").GetProperty("Features")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> PackageIncludes(string csproj) =>
        XDocument.Load(csproj)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string RepoPath(params string[] segments) => Path.Join([RepoRoot, ..segments]);

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }
}
