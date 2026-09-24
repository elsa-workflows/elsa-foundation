using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Owns the ADR 0072 Secrets EF pilot allowlist. <see cref="EfCoreDependencyGuardTests"/> exempts these
/// paths, but not the projects that depend on them; this test is the exact inventory.
/// ADR 0073 supersedes ADR 0072's bounded policy but preserves its admitted implementation as the
/// reviewed starting surface; later EF replacements must extend the ratchet with their own evidence.
/// </summary>
/// <remarks>
/// Until #1878 one test here held the Secrets <c>Tooling/</c> project to being the only Secrets EF package
/// referencing a provider engine. With that project retired, no Secrets EF package carries one at all, and
/// that stronger statement is enforced by two guards rather than by naming a project:
/// <see cref="Policy_and_module_packages_stay_provider_free"/> pins the module's own declared packages, and
/// <c>EfCoreDependencyGuardTests.Every_admitted_Secrets_pilot_project_resolves_only_its_reviewed_EF_closure</c>
/// pins every admitted project's <i>resolved</i> closure in both directions — over exactly the project set
/// <see cref="Pilot_paths_and_projects_are_the_reviewed_adr_0072_allowlist"/> holds fixed.
/// </remarks>
public sealed class SecretsEfPersistencePilotArchitectureTests
{
    private static readonly string[] PolicyPackages =
    [
        "CShells",
        "CShells.Abstractions",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.Extensions.Configuration.EnvironmentVariables",
        "Microsoft.Extensions.Configuration.Json",
        "Microsoft.Extensions.Hosting.Abstractions",
        "Microsoft.Extensions.Options"
    ];

    private static readonly string[] ModulePackages =
    [
        "CShells.Abstractions",
        "Elsa.Specifications.PackageManifest.Generator",
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
        "MySql.EntityFrameworkCore",
        "Npgsql.EntityFrameworkCore.PostgreSQL"
    ];

    private static readonly string[] PilotSources =
    [
        "src/essentials/Persistence/EntityFramework/EfConnectionDefaults.cs",
        "src/essentials/Persistence/EntityFramework/EfDatabaseMigrator.cs",
        "src/essentials/Persistence/EntityFramework/EfMigrateOptions.cs",
        "src/essentials/Persistence/EntityFramework/EfMigratePolicy.cs",
        "src/essentials/Persistence/EntityFramework/EfMigrationsHistory.cs",
        "src/essentials/Persistence/EntityFramework/EfModuleAttribute.cs",
        "src/essentials/Persistence/EntityFramework/EfModuleBinding.cs",
        "src/essentials/Persistence/EntityFramework/EfModuleCatalog.cs",
        "src/essentials/Persistence/EntityFramework/EfModuleDescriptor.cs",
        "src/essentials/Persistence/EntityFramework/EfModuleMigrator.cs",
        "src/essentials/Persistence/EntityFramework/EfOrdinalCollation.cs",
        "src/essentials/Persistence/EntityFramework/EfPayloadCodec.cs",
        "src/essentials/Persistence/EntityFramework/EfPayloadColumns.cs",
        "src/essentials/Persistence/EntityFramework/EfPayloadCompression.cs",
        "src/essentials/Persistence/EntityFramework/EfPayloadCompressionOptions.cs",
        "src/essentials/Persistence/EntityFramework/EfPayloadCompressionOptionsExtension.cs",
        "src/essentials/Persistence/EntityFramework/EfPayloadCompressionSettings.cs",
        "src/essentials/Persistence/EntityFramework/EfPendingMigrationsException.cs",
        "src/essentials/Persistence/EntityFramework/EfPersistenceResourceParticipantAttribute.cs",
        "src/essentials/Persistence/EntityFramework/EfPostMigrationActions.cs",
        "src/essentials/Persistence/EntityFramework/EfPostMigrationRequiredException.cs",
        "src/essentials/Persistence/EntityFramework/EfProviderBindingValidator.cs",
        "src/essentials/Persistence/EntityFramework/EfProviderGuard.cs",
        "src/essentials/Persistence/EntityFramework/EfProviderNames.cs",
        "src/essentials/Persistence/EntityFramework/EfRelationalExceptionClassifier.cs",
        "src/essentials/Persistence/EntityFramework/EfRelationalIdentity.cs",
        "src/essentials/Persistence/EntityFramework/EfRelationalProviderBinding.cs",
        "src/essentials/Persistence/EntityFramework/EfSchema.cs",
        "src/essentials/Persistence/EntityFramework/EfSchemaMigrationsAssembly.cs",
        "src/essentials/Persistence/EntityFramework/EfSchemaOptionsExtension.cs",
        "src/essentials/Persistence/EntityFramework/EfSchemaVersion.cs",
        "src/essentials/Persistence/EntityFramework/EfSchemaVersionSkewException.cs",
        "src/essentials/Persistence/EntityFramework/EfSharedTransaction.cs",
        "src/essentials/Persistence/EntityFramework/EfWriteConflict.cs",
        "src/essentials/Persistence/EntityFramework/EfWriteRetry.cs",
        "src/essentials/Persistence/EntityFramework/IEfPostMigrationAction.cs",
        "src/essentials/Persistence/EntityFramework/ResourceResolution/EfPersistencePreparation.cs",
        "src/essentials/Persistence/EntityFramework/ResourceResolution/PersistenceConfigurationAdapter.cs",
        "src/essentials/Persistence/EntityFramework/ResourceResolution/PersistenceResourceModels.cs",
        "src/essentials/Persistence/EntityFramework/ResourceResolution/PersistenceResourceResolver.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfMigrationPlan.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfModuleOrder.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfMySqlIdempotentScript.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfPersistenceParticipantCatalog.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfPersistenceResourceValidator.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfProviderAgreement.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfToolingConfigurationContext.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfToolingContextContract.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfToolingContextOperation.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfToolingContract.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfToolingExitCode.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfToolingHost.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfToolingShellDefaultsAttribute.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/EfToolingShellDefaultsDeclaration.cs",
        "src/essentials/Persistence/EntityFramework/Tooling/IEfToolingShellDefaults.cs",
        "src/essentials/Persistence/EntityFramework/UnicodeOrdinalCasingTable.Generated.cs",
        "src/essentials/Persistence/EntityFramework/UnicodeOrdinalCasingTable.cs",
        "src/essentials/Persistence/EntityFramework/UsesEfModuleAttribute.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/AssemblyInfo.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Configuration/SecretRecordConfiguration.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/DependencyInjection/SecretsEntityFrameworkCoreRegistration.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Entities/SecretRecord.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260918232200_OrdinalCollation.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260918232200_OrdinalCollation.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/SecretsMySqlDbContextModelSnapshot.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260918221655_OrdinalCollation.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260918221655_OrdinalCollation.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/SecretsPostgreSqlDbContextModelSnapshot.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260918221616_OrdinalCollation.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260918221616_OrdinalCollation.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/SecretsSqlServerDbContextModelSnapshot.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.Designer.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/SecretsSqliteDbContextModelSnapshot.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/SecretsDbContext.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/SecretsEfModule.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/SecretsEntityFrameworkCoreFeature.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/SecretsMySqlDbContext.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/SecretsPostgreSqlDbContext.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/SecretsSqlServerDbContext.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/SecretsSqliteDbContext.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Serialization/SecretsEfJson.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Stores/EfSecretRepository.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Stores/SecretDocument.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Stores/SecretRevisionMapper.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionContract.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionException.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionReindex.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsSearchKeys.cs",
        "src/essentials/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsUnicodeOrdinalIgnoreCaseV1.cs",
        "tests/essentials/Persistence/EntityFramework/BindingDriftTests/EfRelationalProviderBindingDriftTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/CommittedCompositionConnectionTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/ConfigurationServices.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfConnectionDefaultsTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfDatabaseMigratorTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfMigrateOptionsTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfMigrationsHistoryTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfOrdinalCollationTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfPayloadCodecTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfPayloadColumnsTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfPayloadCompressionSettingsTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfPostMigrationActionsTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfProviderBindingValidatorTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfProviderGuardTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfRelationalExceptionClassifierTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfRelationalIdentityTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfRelationalProviderBindingTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfSchemaTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfSchemaVersionTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfToolingConfigurationContextTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/EfWriteRetryTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/PersistenceConfigurationAdapterTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/PersistenceResourceResolverTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/ProviderFailures.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/SharedPersistenceLayoutTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/TemporarySqliteDatabase.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/TemporarySqliteDatabaseTests.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/UnorderedRowLimitGuard.cs",
        "tests/essentials/Persistence/EntityFramework/Tests/UnorderedRowLimitGuardTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/MySqlContainerFixture.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/MySqlEfSecretRepositoryTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Program.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlEfSecretRepositoryTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlSecretsShellJourneyTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgresContainerFixture.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/SecretsPackageFeedProbeTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Support/SecretsPackageFeedProbeRunner.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerContainerFixture.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerEfSecretRepositoryTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfMigrationGeneratorTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfModuleMigrationTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreFeatureTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreShellReloadTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsHostCatalog.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceCompositionTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceHostJourneyTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsProjectionContractTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsProjectionReindexTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsSearchKeysTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SqliteEfSecretRepositoryTests.cs",
        "tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PersistenceCliProcessRunner.cs"
    ];

    [Fact]
    public void Pilot_paths_and_projects_are_the_reviewed_adr_0072_allowlist()
    {
        Assert.Equal(
            [
                "src/essentials/Persistence/EntityFramework/",
                "src/essentials/Secrets/Persistence/EntityFrameworkCore/",
                "tests/essentials/Persistence/EntityFramework/",
                "tests/essentials/Secrets/Persistence/EntityFrameworkCore/"
            ],
            EfCoreDependencyGuardTests.Adr0072SecretsEfPilot.SurfacePathPrefixes);

        var projects = EfCoreDependencyGuardTests.Adr0072SecretsEfPilot.SurfacePathPrefixes
            .SelectMany(prefix => Directory.EnumerateFiles(
                RepoPath(prefix.TrimEnd('/').Split('/')), "*.csproj", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(EfCoreDependencyGuardTests.Adr0072SecretsEfPilot.ProjectPaths, projects);
    }

    [Fact]
    public void Policy_and_module_packages_stay_provider_free()
    {
        var policy = PackageIncludes(RepoPath("src", "essentials", "Persistence", "EntityFramework", "Elsa.Persistence.EntityFramework.csproj"));
        var module = PackageIncludes(RepoPath("src", "essentials", "Secrets", "Persistence", "EntityFrameworkCore", "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"));

        Assert.Equal(PolicyPackages, policy);
        Assert.Equal(ModulePackages, module);
        Assert.Empty(policy.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
        Assert.Empty(module.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void PostgreSql_shell_proof_supplies_exactly_one_relational_provider_package()
    {
        var packages = PackageIncludes(RepoPath(
            "tests", "essentials", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "Tests",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj"));

        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void MySql_shell_proof_supplies_exactly_one_relational_provider_package()
    {
        var packages = PackageIncludes(RepoPath(
            "tests", "essentials", "Secrets", "Persistence", "EntityFrameworkCore", "MySql", "Tests",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.MySql.Tests.csproj"));

        Assert.Equal(
            ["MySql.EntityFrameworkCore"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void Package_feed_probe_has_one_provider_and_no_compile_time_secrets_ef_module_reference()
    {
        var projectPath = RepoPath(
            "tests", "essentials", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "PackageFeedProbe",
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
            "src", "essentials", "Secrets", "Persistence", "EntityFrameworkCore",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj");
        var project = XDocument.Load(projectPath);
        var metadataItem = Assert.Single(project.Descendants("None"), element =>
            string.Equals(element.Attribute("Update")?.Value, "nuplane.json", StringComparison.Ordinal));
        Assert.Equal("true", metadataItem.Attribute("Pack")?.Value);
        Assert.Equal("/", metadataItem.Attribute("PackagePath")?.Value);

        using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            RepoPath("src", "essentials", "Secrets", "Persistence", "EntityFrameworkCore", "nuplane.json")));
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
    public void Workbench_enables_the_secrets_ef_module_by_default()
    {
        var workbench = XDocument.Load(RepoPath("src", "apps", "Elsa.Workbench", "Elsa.Workbench.csproj"));
        var references = workbench.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();
        Assert.Contains(
            references,
            reference => reference.Contains("Secrets.Persistence.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                         && !reference.Contains("Tooling", StringComparison.OrdinalIgnoreCase));

        // EF is the only Secrets persistence family.
        foreach (var relative in new[]
                 {
                     "src/apps/Elsa.Workbench/shells.json",
                     "docker/compose/elsa-workbench.shells.json"
                 })
        {
            Assert.True(
                DefaultShellFeatures(relative).Contains("SecretsEntityFrameworkCore"),
                $"{relative} must enable SecretsEntityFrameworkCore as the default Secrets store.");
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
