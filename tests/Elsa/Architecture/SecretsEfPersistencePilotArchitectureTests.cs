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
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsEfModule.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsEntityFrameworkCoreFeature.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsPostgreSqlDbContext.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsSqlServerDbContext.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsSqliteDbContext.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Serialization/SecretsEfJson.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/EfSecretRepository.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/SecretDocument.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/SecretRevisionMapper.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsSearchKeys.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/SecretsPostgreSqlDesignTimeFactory.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/SecretsSqlServerDesignTimeFactory.cs",
        "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/SecretsSqliteDesignTimeFactory.cs",
        "tests/Elsa/Persistence/EntityFramework/Tests/EfDatabaseMigratorTests.cs",
        "tests/Elsa/Persistence/EntityFramework/Tests/EfMigrationsHistoryTests.cs",
        "tests/Elsa/Persistence/EntityFramework/Tests/EfProviderGuardTests.cs",
        "tests/Elsa/Persistence/EntityFramework/Tests/EfRelationalProviderBindingTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlEfSecretRepositoryTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgresContainerFixture.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerContainerFixture.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerEfSecretRepositoryTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreFeatureTests.cs",
        "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/SqliteEfSecretRepositoryTests.cs"
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
    public void Pilot_sources_are_the_exact_reviewed_inventory()
    {
        var actual = EfCoreSurfaceScanner.Adr0072SecretsEfPilot.SurfacePathPrefixes
            .SelectMany(prefix => Directory.EnumerateFiles(RepoPath(prefix.TrimEnd('/').Split('/')), "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/')))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(PilotSources, actual);
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
        var workbench = XDocument.Load(RepoPath("src", "Apps", "Elsa.Workbench", "Elsa.Workbench.csproj"));
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
