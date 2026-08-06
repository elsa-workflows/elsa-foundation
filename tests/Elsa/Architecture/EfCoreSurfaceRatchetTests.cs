using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class EfCoreSurfaceRatchetTests
{
    [Fact]
    public void Ef_core_surface_matches_the_reviewed_shrink_only_baseline()
    {
        var actual = new EfCoreSurfaceScanner(RepoRoot).Scan();
        if (Environment.GetEnvironmentVariable("ELSA_UPDATE_EF_CORE_BASELINE") == "1")
            EfCoreSurfaceBaseline.Save(BaselinePath, actual);

        var baseline = EfCoreSurfaceBaseline.Load(BaselinePath);

        var differences = EfCoreSurfaceBaseline.Compare(baseline, actual);

        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public void Core_and_groundwork_projects_are_ef_free_now()
    {
        var snapshot = new EfCoreSurfaceScanner(RepoRoot).Scan();

        var violations = snapshot.FindEfFreeBoundaryViolations();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Workflow_and_activity_design_source_projects_are_ef_free_boundaries()
    {
        // Spec 093 T075: design persistence ships only Groundwork, so the whole design source lane —
        // including the provider-neutral projects the .Core/.Groundwork rules do not already cover — is a
        // guarded EF-free boundary. Reintroducing EF anywhere they can reach (direct or transitive) fails
        // Core_and_groundwork_projects_are_ef_free_now above.
        var boundaries = new EfCoreSurfaceScanner(RepoRoot).EfFreeBoundaryProjectNames();

        string[] designProjectsBeyondCoreAndGroundwork =
        [
            "Elsa.Workflows.Design.Api",
            "Elsa.Workflows.Design.Validations",
            "Elsa.Workflows.Design.JavaScript",
            "Elsa.Activities.Design.Api"
        ];

        Assert.All(
            designProjectsBeyondCoreAndGroundwork,
            project => Assert.Contains(project, boundaries));
    }

    [Fact]
    public void Reviewed_provider_neutral_persistence_families_are_explicit_ef_free_boundaries()
    {
        var baseline = EfCoreSurfaceBaseline.LoadDocument(BaselinePath);
        var reviewedProjects = baseline.ProtectedProviderNeutralProjects ?? [];
        var discoveredBoundaries = new EfCoreSurfaceScanner(RepoRoot).EfFreeBoundaryProjectNames();

        Assert.Equal(PersistenceProviderNeutralityBoundary.ProjectNames, reviewedProjects);
        Assert.All(reviewedProjects, project => Assert.Contains(project, discoveredBoundaries));
    }

    [Fact]
    public void Scanner_follows_windows_style_project_references_on_every_host()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("src/Core/Elsa.Sample.Core.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\EFCore\Elsa.Sample.EFCore.csproj" />
              </ItemGroup>
            </Project>
            """);
        fixture.Write("src/EFCore/Elsa.Sample.EFCore.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore" />
              </ItemGroup>
            </Project>
            """);

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains(
            "src/Core/Elsa.Sample.Core.csproj -> src/EFCore/Elsa.Sample.EFCore.csproj",
            snapshot.DirectEfProjectReferences);
        Assert.Contains(
            "src/Core/Elsa.Sample.Core.csproj -> Microsoft.EntityFrameworkCore",
            snapshot.TransitiveEfPackageConsumers);
    }

    [Fact]
    public void Scanner_discovers_ef_projects_omitted_from_the_maintained_solution_and_follows_static_references()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("Elsa.Server.slnx", """
            <Solution>
              <Folder Name="/src/Consumer/"><Project Path="src/Consumer/Elsa.Consumer.csproj" /></Folder>
            </Solution>
            """);
        fixture.Write("src/Consumer/Elsa.Consumer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="../Provider/Elsa.Provider.EFCore.csproj" /></ItemGroup>
            </Project>
            """);
        fixture.Write("src/Provider/Elsa.Provider.EFCore.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" /></ItemGroup>
            </Project>
            """);

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains("src/Provider/Elsa.Provider.EFCore.csproj", snapshot.EfProjects);
        Assert.Contains(
            "src/Consumer/Elsa.Consumer.csproj -> src/Provider/Elsa.Provider.EFCore.csproj",
            snapshot.DirectEfProjectReferences);
        Assert.Contains(
            "src/Consumer/Elsa.Consumer.csproj -> Microsoft.EntityFrameworkCore.Sqlite",
            snapshot.TransitiveEfPackageConsumers);
    }

    [Fact]
    public void Scanner_inventories_declared_direct_and_central_ef_package_inputs()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("Directory.Packages.props", """
            <Project>
              <ItemGroup><PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.0" /></ItemGroup>
            </Project>
            """);
        fixture.Write("src/Consumer/Elsa.Consumer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" /></ItemGroup>
            </Project>
            """);

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains(
            "src/Consumer/Elsa.Consumer.csproj -> Microsoft.EntityFrameworkCore.Sqlite",
            snapshot.DirectPackageReferences);
        Assert.Contains(
            "Directory.Packages.props -> Microsoft.EntityFrameworkCore.SqlServer",
            snapshot.CentralPackageVersions);
    }

    [Fact]
    public void Scanner_evaluates_imported_ef_package_references_for_the_importing_project()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("eng/ef-persistence.props", """
            <Project>
              <ItemGroup><PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" /></ItemGroup>
            </Project>
            """);
        fixture.Write("src/Consumer/Elsa.Consumer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="../../eng/ef-persistence.props" />
            </Project>
            """);

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains(
            "src/Consumer/Elsa.Consumer.csproj -> Npgsql.EntityFrameworkCore.PostgreSQL",
            snapshot.DirectPackageReferences);
        Assert.Contains(
            "eng/ef-persistence.props -> Npgsql.EntityFrameworkCore.PostgreSQL",
            snapshot.SharedBuildPackageReferences);
    }

    [Fact]
    public void Scanner_applies_conditional_ef_package_references_only_to_matching_projects()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("Directory.Build.props", """
            <Project>
              <ItemGroup Condition="'$(MSBuildProjectName)' == 'Elsa.Conditional'">
                <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
              </ItemGroup>
            </Project>
            """);
        fixture.Write("src/Conditional/Elsa.Conditional.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        fixture.Write("src/Unrelated/Elsa.Unrelated.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains(
            "src/Conditional/Elsa.Conditional.csproj -> Microsoft.EntityFrameworkCore.InMemory",
            snapshot.DirectPackageReferences);
        Assert.DoesNotContain(
            snapshot.DirectPackageReferences,
            entry => entry.StartsWith("src/Unrelated/Elsa.Unrelated.csproj -> ", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_accepts_a_current_all_project_restore_receipt_without_changing_the_temporary_baseline_path()
    {
        using var fixture = CreateRestoreReceiptFixture();

        var certification = new EfCoreSurfaceScanner(fixture.Repository.Path)
            .ScanForCertification("artifacts/zero-ef/restore-receipt.json", fixture.RepositoryState);

        Assert.Empty(certification.RestoreReceipt.Failures);
        Assert.Contains(
            nameof(EfCoreSurfaceSnapshot.EfFreeBoundaryViolations),
            certification.Surface.Categories().Keys);
    }

    [Theory]
    [InlineData("src/Consumer/Elsa.Consumer.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup></Project>")]
    [InlineData("eng/imported.props", "<Project><PropertyGroup><RestoreSources>https://example.invalid/v2</RestoreSources></PropertyGroup></Project>")]
    [InlineData("Directory.Packages.props", "<Project><ItemGroup><PackageVersion Include=\"Example\" Version=\"2.0.0\" /></ItemGroup></Project>")]
    public void Scanner_rejects_a_receipt_when_a_dependency_input_changes(string path, string content)
    {
        using var fixture = CreateRestoreReceiptFixture();
        fixture.Repository.Write(path, content);

        var validation = new EfCoreSurfaceScanner(fixture.Repository.Path)
            .ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);

        Assert.Contains(validation.Failures, failure =>
            failure.Code == "ZERO_EF_RECEIPT_INPUT_HASH_MISMATCH" && failure.Subject == path);
    }

    [Fact]
    public void Scanner_rejects_a_receipt_when_assets_are_mutated_or_missing()
    {
        using var fixture = CreateRestoreReceiptFixture();
        fixture.Repository.Write(
            "src/Consumer/obj/project.assets.json",
            AssetsJson(fixture.Repository, "src/Consumer/Elsa.Consumer.csproj", "mutated"));

        var mutated = new EfCoreSurfaceScanner(fixture.Repository.Path)
            .ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(mutated.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_ASSETS_HASH_MISMATCH");

        File.Delete(Path.Combine(fixture.Repository.Path, "src/Consumer/obj/project.assets.json"));
        var missing = new EfCoreSurfaceScanner(fixture.Repository.Path)
            .ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(missing.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_ASSETS_MISSING");
    }

    [Fact]
    public void Scanner_rejects_an_assets_file_copied_from_a_different_project_even_when_its_hash_is_bound()
    {
        using var fixture = CreateRestoreReceiptFixture(repository =>
            repository.Write(
                "src/Consumer/obj/project.assets.json",
                AssetsJson(repository, "src/Other/Elsa.Other.csproj", "copied")));

        var validation = new EfCoreSurfaceScanner(fixture.Repository.Path)
            .ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);

        Assert.Contains(validation.Failures, failure =>
            failure.Code == "ZERO_EF_RECEIPT_ASSETS_PROJECT_IDENTITY_MISMATCH");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scanner_rejects_assets_restored_with_unbound_or_additional_config_paths(bool additionalConfig)
    {
        var configPaths = additionalConfig
            ? new[] { "NuGet.config", "User.NuGet.config" }
            : new[] { "User.NuGet.config" };
        using var fixture = CreateRestoreReceiptFixture(repository =>
            repository.Write(
                "src/Consumer/obj/project.assets.json",
                AssetsJson(repository, "src/Consumer/Elsa.Consumer.csproj", "wrong-config", configPaths)));

        var validation = new EfCoreSurfaceScanner(fixture.Repository.Path)
            .ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);

        Assert.Contains(validation.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_ASSETS_CONFIG_MISMATCH");
    }

    [Fact]
    public void Scanner_rejects_non_array_or_non_string_assets_config_metadata()
    {
        using var scalarFixture = CreateRestoreReceiptFixture(repository =>
            repository.Write(
                "src/Consumer/obj/project.assets.json",
                AssetsJsonWithRawConfig(
                    repository,
                    "src/Consumer/Elsa.Consumer.csproj",
                    System.Text.Json.JsonSerializer.Serialize(
                        Path.Combine(repository.Path, "NuGet.config")))));
        var scalar = new EfCoreSurfaceScanner(scalarFixture.Repository.Path)
            .ValidateRestoreReceipt(scalarFixture.ReceiptPath, scalarFixture.RepositoryState);
        Assert.Contains(scalar.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_ASSETS_CONFIG_MISMATCH");

        using var mixedFixture = CreateRestoreReceiptFixture(repository =>
            repository.Write(
                "src/Consumer/obj/project.assets.json",
                AssetsJsonWithRawConfig(
                    repository,
                    "src/Consumer/Elsa.Consumer.csproj",
                    System.Text.Json.JsonSerializer.Serialize(
                        new string?[] { Path.Combine(repository.Path, "NuGet.config"), null }))));
        var mixed = new EfCoreSurfaceScanner(mixedFixture.Repository.Path)
            .ValidateRestoreReceipt(mixedFixture.ReceiptPath, mixedFixture.RepositoryState);
        Assert.Contains(mixed.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_ASSETS_CONFIG_MISMATCH");
    }

    [Fact]
    public void Scanner_rejects_a_new_project_or_an_omitted_project_binding()
    {
        using var fixture = CreateRestoreReceiptFixture();
        fixture.Repository.Write("src/New/Elsa.New.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        fixture.Repository.Write(
            "src/New/obj/project.assets.json",
            AssetsJson(fixture.Repository, "src/New/Elsa.New.csproj", "new"));

        var newProject = new EfCoreSurfaceScanner(fixture.Repository.Path)
            .ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(newProject.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_PROJECT_SET_MISMATCH");

        using var omittedFixture = CreateRestoreReceiptFixture();
        var receipt = CreateReceipt(omittedFixture.Repository.Path, omittedFixture.RepositoryState) with { Projects = [] };
        WriteReceipt(omittedFixture.Repository, receipt);
        var omittedProject = new EfCoreSurfaceScanner(omittedFixture.Repository.Path)
            .ValidateRestoreReceipt(omittedFixture.ReceiptPath, omittedFixture.RepositoryState);
        Assert.Contains(omittedProject.Failures, failure =>
            failure.Code == "ZERO_EF_RECEIPT_UNBOUND_PROJECT" &&
            failure.Subject == "src/Consumer/Elsa.Consumer.csproj");
    }

    [Fact]
    public void Scanner_rejects_added_or_removed_dependency_inputs_and_a_changed_driver()
    {
        using var addedFixture = CreateRestoreReceiptFixture();
        addedFixture.Repository.Write("eng/late.targets", "<Project />");
        var added = new EfCoreSurfaceScanner(addedFixture.Repository.Path)
            .ValidateRestoreReceipt(addedFixture.ReceiptPath, addedFixture.RepositoryState);
        Assert.Contains(added.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_INPUT_UNIVERSE_MISMATCH");

        using var removedFixture = CreateRestoreReceiptFixture();
        File.Delete(Path.Combine(removedFixture.Repository.Path, "eng/imported.props"));
        var removed = new EfCoreSurfaceScanner(removedFixture.Repository.Path)
            .ValidateRestoreReceipt(removedFixture.ReceiptPath, removedFixture.RepositoryState);
        Assert.Contains(removed.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_INPUT_UNIVERSE_MISMATCH");

        using var driverFixture = CreateRestoreReceiptFixture();
        driverFixture.Repository.Write("tools/architecture/restore-zero-ef-certification.sh", "changed\n");
        var changedDriver = new EfCoreSurfaceScanner(driverFixture.Repository.Path)
            .ValidateRestoreReceipt(driverFixture.ReceiptPath, driverFixture.RepositoryState);
        Assert.Contains(changedDriver.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_DRIVER_MISMATCH");

        using var substitutedDriverFixture = CreateRestoreReceiptFixture();
        var currentReceipt = CreateReceipt(
            substitutedDriverFixture.Repository.Path,
            substitutedDriverFixture.RepositoryState);
        WriteReceipt(
            substitutedDriverFixture.Repository,
            currentReceipt with
            {
                Restore = currentReceipt.Restore! with
                {
                    DriverPath = "NuGet.config",
                    DriverSha256 = HashFile(Path.Combine(
                        substitutedDriverFixture.Repository.Path,
                        "NuGet.config"))
                }
            });
        var substitutedDriver = new EfCoreSurfaceScanner(substitutedDriverFixture.Repository.Path)
            .ValidateRestoreReceipt(
                substitutedDriverFixture.ReceiptPath,
                substitutedDriverFixture.RepositoryState);
        Assert.Contains(substitutedDriver.Failures, failure =>
            failure.Code == "ZERO_EF_RECEIPT_DRIVER_MISMATCH");
    }

    [Fact]
    public void Scanner_rejects_a_stale_worktree_binding_and_invalid_receipt_inputs()
    {
        using var fixture = CreateRestoreReceiptFixture();
        var scanner = new EfCoreSurfaceScanner(fixture.Repository.Path);

        var staleWorktree = scanner.ValidateRestoreReceipt(
            fixture.ReceiptPath,
            fixture.RepositoryState with { WorktreeStatusSha256 = HashText("changed") });
        Assert.Contains(staleWorktree.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_WORKTREE_MISMATCH");

        var staleTool = scanner.ValidateRestoreReceipt(
            fixture.ReceiptPath,
            fixture.RepositoryState with { DotnetSdkVersion = "10.0.101" });
        Assert.Contains(staleTool.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_TOOL_MISMATCH");

        var unsupported = CreateReceipt(fixture.Repository.Path, fixture.RepositoryState) with { SchemaVersion = 2 };
        WriteReceipt(fixture.Repository, unsupported);
        var invalidSchema = scanner.ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(invalidSchema.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_SCHEMA_UNSUPPORTED");

        var current = CreateReceipt(fixture.Repository.Path, fixture.RepositoryState);
        var nonCanonicalCommand = current with
        {
            Restore = current.Restore! with { CommandTemplate = ["dotnet", "restore", "<project>"] }
        };
        WriteReceipt(fixture.Repository, nonCanonicalCommand);
        var invalidCommand = scanner.ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(invalidCommand.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_MALFORMED");

        var outsideReceipt = scanner.ValidateRestoreReceipt("../outside-repository.json", fixture.RepositoryState);
        Assert.Contains(outsideReceipt.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_MALFORMED");

        var missingProjectSet = current with
        {
            Discovery = current.Discovery! with { Projects = null }
        };
        WriteReceipt(fixture.Repository, missingProjectSet);
        var invalidProjectSet = scanner.ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(invalidProjectSet.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_MALFORMED");

        var missingInputEntries = current with
        {
            Inputs = current.Inputs! with { Entries = null }
        };
        WriteReceipt(fixture.Repository, missingInputEntries);
        var invalidInputEntries = scanner.ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(invalidInputEntries.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_MALFORMED");

        var inputsWithoutConfig = current.Inputs!.Entries!
            .Where(entry => entry.Path != "NuGet.config")
            .ToArray();
        var missingConfig = current with
        {
            Inputs = current.Inputs with
            {
                Entries = inputsWithoutConfig,
                FingerprintSha256 = Fingerprint(inputsWithoutConfig.Select(entry => $"{entry.Path}\t{entry.Sha256}"))
            }
        };
        WriteReceipt(fixture.Repository, missingConfig);
        var invalidConfig = scanner.ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(invalidConfig.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_MALFORMED");

        var duplicateProjects = current.Discovery!.Projects!.Append("src/Consumer/Elsa.Consumer.csproj").ToArray();
        var duplicate = current with
        {
            Discovery = current.Discovery with
            {
                Projects = duplicateProjects,
                ProjectSetSha256 = Fingerprint(duplicateProjects)
            }
        };
        WriteReceipt(fixture.Repository, duplicate);
        var invalidPaths = scanner.ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(invalidPaths.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_MALFORMED");

        var unsafeProjects = new[] { "/outside-repository/Elsa.Hidden.csproj" };
        var unsafeReceipt = current with
        {
            Discovery = current.Discovery with
            {
                Projects = unsafeProjects,
                ProjectSetSha256 = Fingerprint(unsafeProjects)
            }
        };
        WriteReceipt(fixture.Repository, unsafeReceipt);
        var unsafePaths = scanner.ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(unsafePaths.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_MALFORMED");

        fixture.Repository.Write("artifacts/zero-ef/restore-receipt.json", "{");
        var malformed = scanner.ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(malformed.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_MALFORMED");

        File.Delete(fixture.ReceiptPath);
        var missing = scanner.ValidateRestoreReceipt(fixture.ReceiptPath, fixture.RepositoryState);
        Assert.Contains(missing.Failures, failure => failure.Code == "ZERO_EF_RECEIPT_MISSING");
    }

    [Fact]
    public void Baseline_comparison_reports_both_expansion_and_stale_entries()
    {
        var baseline = EmptySurface() with { EfProjects = ["removed.csproj"] };
        var actual = EmptySurface() with { EfProjects = ["added.csproj"] };

        var differences = EfCoreSurfaceBaseline.Compare(baseline, actual);

        Assert.Contains("EF surface expanded [EfProjects]: added.csproj", differences);
        Assert.Contains(
            "EF surface shrank [EfProjects]; remove this stale baseline entry: removed.csproj",
            differences);

        var boundaryDifferences = EfCoreSurfaceBaseline.Compare(
            EmptySurface(),
            EmptySurface() with { EfFreeBoundaryViolations = ["src/Elsa/Core/Elsa.Core.csproj -> Groundwork"] });
        Assert.Contains(
            "EF surface expanded [EfFreeBoundaryViolations]: src/Elsa/Core/Elsa.Core.csproj -> Groundwork",
            boundaryDifferences);
    }

    [Fact]
    public void Baseline_comparison_rejects_an_incomplete_repository_restore_instead_of_reporting_phantom_changes()
    {
        var baseline = EmptySurface() with
        {
            ResolvedEfPackageConsumers = ["src/Consumer/Consumer.csproj -> Microsoft.EntityFrameworkCore"]
        };
        var actual = EmptySurface() with { ProjectsMissingAssets = ["src/Consumer/Consumer.csproj"] };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EfCoreSurfaceBaseline.Compare(baseline, actual));

        Assert.Contains("dotnet restore Elsa.Server.slnx", exception.Message, StringComparison.Ordinal);
        Assert.Contains("src/Consumer/Consumer.csproj", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("EF surface shrank", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_generation_rejects_an_incomplete_repository_restore()
    {
        using var fixture = new TemporaryRepository();
        var incomplete = EmptySurface() with { ProjectsMissingAssets = ["missing.csproj"] };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EfCoreSurfaceBaseline.Save(
                System.IO.Path.Combine(fixture.Path, "baseline.json"),
                incomplete));

        Assert.Contains("dotnet restore Elsa.Server.slnx", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_generation_accepts_only_shrinkage()
    {
        using var fixture = new TemporaryRepository();
        var path = System.IO.Path.Combine(fixture.Path, "baseline.json");
        var original = EmptySurface() with { EfProjects = ["remaining.csproj", "removed.csproj"] };
        fixture.Write("baseline.json", System.Text.Json.JsonSerializer.Serialize(
            new EfCoreSurfaceBaselineDocument(1, original)));

        EfCoreSurfaceBaseline.Save(path, original with { EfProjects = ["remaining.csproj"] });
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EfCoreSurfaceBaseline.Save(
                path,
                original with { EfProjects = ["remaining.csproj", "added.csproj"] }));

        Assert.Contains("EF surface expanded [EfProjects]: added.csproj", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_generation_requires_the_reviewed_baseline_to_exist()
    {
        using var fixture = new TemporaryRepository();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EfCoreSurfaceBaseline.Save(
                System.IO.Path.Combine(fixture.Path, "missing.json"),
                EmptySurface()));

        Assert.Contains("reviewed baseline is missing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scanner_inventories_migrations_contexts_registrations_and_host_configuration()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("src/Feature/EFCore/Feature.EFCore.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
              </ItemGroup>
            </Project>
            """);
        fixture.Write("src/Feature/EFCore/Migrations/Initial.cs", "public sealed class Initial;");
        fixture.Write("src/Feature/EFCore/FeatureDbContext.cs", "public sealed class FeatureDbContext : DbContext;");
        fixture.Write("src/App/Program.cs", "services.AddDbContext&lt;FeatureDbContext&gt;();");
        fixture.Write("src/App/shells.json", "{ \"Feature\": \"FeatureEFCore\" }");

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains("src/Feature/EFCore/Migrations/Initial.cs", snapshot.MigrationFiles);
        Assert.Contains("src/Feature/EFCore/FeatureDbContext.cs", snapshot.DbContextFiles);
        Assert.Contains(snapshot.RegistrationFiles, entry => entry.StartsWith("src/App/Program.cs -> ", StringComparison.Ordinal));
        Assert.Contains(snapshot.HostConfigurationFiles, entry => entry.StartsWith("src/App/shells.json -> ", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_recognizes_ef_source_shapes_without_counting_comments()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("src/Feature/OutsideEfFolder/Initial.cs", """
            [DbContext(typeof(StoreContext))]
            [Migration("202607120001_Initial")]
            public partial class Initial : Migration;
            """);
        fixture.Write("src/Feature/StoreContext.cs", "public sealed class StoreContext : IdentityDbContext<User>;");
        fixture.Write("src/App/Registration.cs", "options.UseNpgsql(connectionString);");
        fixture.Write("src/App/CommentOnly.cs", "// services.AddDbContext<StoreContext>();");
        fixture.Write("src/App/appsettings.yaml", "persistence: entityframeworkcore");

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains("src/Feature/OutsideEfFolder/Initial.cs", snapshot.MigrationFiles);
        Assert.Contains("src/Feature/StoreContext.cs", snapshot.DbContextFiles);
        Assert.Contains(snapshot.RegistrationFiles, entry => entry.StartsWith("src/App/Registration.cs -> ", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.RegistrationFiles, entry => entry.StartsWith("src/App/CommentOnly.cs -> ", StringComparison.Ordinal));
        Assert.Contains(snapshot.HostConfigurationFiles, entry => entry.StartsWith("src/App/appsettings.yaml -> ", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_records_registration_and_configuration_occurrences_within_existing_files()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("src/App/Program.cs", "services.AddDbContext<FirstContext>();");
        fixture.Write("src/App/shells.json", """
            { "Features": { "FirstPersistenceEFCoreSqlite": {} } }
            """);
        var before = new EfCoreSurfaceScanner(fixture.Path).Scan();

        fixture.Write("src/App/Program.cs", """
            services.AddDbContext<FirstContext>();
            services.AddDbContext<SecondContext>();
            """);
        fixture.Write("src/App/shells.json", """
            { "Features": { "FirstPersistenceEFCoreSqlite": {}, "SecondPersistenceEFCoreSqlite": {} } }
            """);
        var after = new EfCoreSurfaceScanner(fixture.Path).Scan();

        var differences = EfCoreSurfaceBaseline.Compare(before, after);
        Assert.Contains(differences, difference => difference.StartsWith("EF surface expanded [RegistrationFiles]", StringComparison.Ordinal));
        Assert.Contains(differences, difference => difference.StartsWith("EF surface expanded [HostConfigurationFiles]", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_includes_docker_host_configuration_but_ignores_comment_properties()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("docker/compose/comments.json", """
            { "//": "EFCore is intentionally absent" }
            """);
        fixture.Write("docker/compose/elsa-workbench.shells.json", """
            { "Features": { "PersistenceEFCoreSqlite": {} } }
            """);

        var entries = new EfCoreSurfaceScanner(fixture.Path).Scan().HostConfigurationFiles;

        Assert.DoesNotContain(entries, entry => entry.StartsWith("docker/compose/comments.json -> ", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.StartsWith("docker/compose/elsa-workbench.shells.json -> ", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_inventories_resolved_transitive_packages_and_missing_restore_assets()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("src/Restored/Restored.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Some.Provider" /></ItemGroup>
            </Project>
            """);
        fixture.Write("src/Restored/obj/project.assets.json", """
            {
              "libraries": {
                "Some.Provider/1.0.0": {},
                "Elsa.Sample.EntityFrameworkCore/1.0.0": { "type": "project" },
                "Microsoft.EntityFrameworkCore.Relational/10.0.0": {}
              }
            }
            """);
        fixture.Write("src/Missing/Missing.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains(
            "src/Restored/Restored.csproj -> Microsoft.EntityFrameworkCore.Relational",
            snapshot.ResolvedEfPackageConsumers);
        Assert.DoesNotContain(
            "src/Restored/Restored.csproj -> Elsa.Sample.EntityFrameworkCore",
            snapshot.ResolvedEfPackageConsumers);
        Assert.Contains("src/Missing/Missing.csproj", snapshot.ProjectsMissingAssets);
        Assert.DoesNotContain("src/Restored/Restored.csproj", snapshot.ProjectsMissingAssets);
    }

    [Fact]
    public void Scanner_uses_evaluated_restore_inputs_for_imported_dependencies()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("src/Consumer/Consumer.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        fixture.Write("src/EFCore/Provider.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        fixture.Write("src/Consumer/obj/project.assets.json", $$"""
            {
              "libraries": {},
              "project": {
                "frameworks": {
                  "net10.0": {
                    "dependencies": {
                      "Microsoft.EntityFrameworkCore.Sqlite": { "target": "Package" }
                    }
                  }
                },
                "restore": {
                  "frameworks": {
                    "net10.0": {
                      "projectReferences": {
                        "{{System.IO.Path.Combine(fixture.Path, "src/EFCore/Provider.csproj").Replace("\\", "\\\\")}}": {}
                      }
                    }
                  }
                }
              }
            }
            """);

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains(
            "src/Consumer/Consumer.csproj -> Microsoft.EntityFrameworkCore.Sqlite",
            snapshot.DirectPackageReferences);
        Assert.Contains(
            "src/Consumer/Consumer.csproj -> src/EFCore/Provider.csproj",
            snapshot.DirectEfProjectReferences);
    }

    [Fact]
    public void Scanner_inventories_ef_packages_from_shared_build_files()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("Directory.Build.props", """
            <Project>
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore" />
              </ItemGroup>
            </Project>
            """);

        var snapshot = new EfCoreSurfaceScanner(fixture.Path).Scan();

        Assert.Contains(
            "Directory.Build.props -> Microsoft.EntityFrameworkCore",
            snapshot.SharedBuildPackageReferences);
    }

    [Fact]
    public void Scanner_classifies_core_and_groundwork_path_segments_as_ef_free_boundaries()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("src/Feature/Core/Elsa.Feature.Contracts.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore" /></ItemGroup>
            </Project>
            """);
        fixture.Write("src/Feature/Groundwork/Elsa.Feature.Adapter.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore" /></ItemGroup>
            </Project>
            """);
        fixture.Write("src/Feature/Abstractions/Elsa.Feature.Abstractions.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore" /></ItemGroup>
            </Project>
            """);

        var violations = new EfCoreSurfaceScanner(fixture.Path).Scan().FindEfFreeBoundaryViolations();

        Assert.Contains(
            "src/Feature/Core/Elsa.Feature.Contracts.csproj reaches EF package Microsoft.EntityFrameworkCore",
            violations);
        Assert.Contains(
            "src/Feature/Groundwork/Elsa.Feature.Adapter.csproj reaches EF package Microsoft.EntityFrameworkCore",
            violations);
        Assert.Contains(
            "src/Feature/Abstractions/Elsa.Feature.Abstractions.csproj reaches EF package Microsoft.EntityFrameworkCore",
            violations);
    }

    [Fact]
    public void Scanner_classifies_every_in_scope_provider_neutral_persistence_family_as_ef_free()
    {
        using var fixture = new TemporaryRepository();
        foreach (var (path, name) in new[]
                 {
                     ("src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj", "Elsa.Workflows.Runtime.Core"),
                     ("src/Elsa/Foundation/Identity/Abstractions/Elsa.Foundation.Identity.Abstractions.csproj", "Elsa.Foundation.Identity.Abstractions"),
                     ("src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj", "Elsa.Secrets.Core"),
                     ("src/Elsa/Workflows/Runtime/Distributed/Elsa.Workflows.Runtime.Distributed.csproj", "Elsa.Workflows.Runtime.Distributed")
                 })
        {
            fixture.Write(path, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><AssemblyName>{{name}}</AssemblyName></PropertyGroup>
                  <ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore" /></ItemGroup>
                </Project>
                """);
        }

        var violations = new EfCoreSurfaceScanner(fixture.Path).Scan().FindEfFreeBoundaryViolations();

        Assert.All(
            PersistenceProviderNeutralityBoundary.ProjectNames,
            project => Assert.Contains(violations, violation =>
                violation.Contains(project, StringComparison.Ordinal) &&
                violation.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("Groundwork.Documents")]
    [InlineData("Microsoft.Data.Sqlite")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("Npgsql")]
    [InlineData("MongoDB.Driver")]
    public void Scanner_rejects_reviewed_concrete_provider_packages_from_provider_neutral_projects(string packageName)
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><AssemblyName>Elsa.Secrets.Core</AssemblyName></PropertyGroup>
              <ItemGroup><PackageReference Include="{{packageName}}" /></ItemGroup>
            </Project>
            """);

        var violations = new EfCoreSurfaceScanner(fixture.Path).Scan().FindEfFreeBoundaryViolations();

        Assert.Contains(
            $"src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj reaches concrete provider package {packageName}",
            violations);
    }

    [Fact]
    public void Scanner_evaluates_conditional_directory_build_packages_inherited_by_a_protected_project()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("Directory.Build.props", """
            <Project>
              <ItemGroup Condition="'$(MSBuildProjectName)' == 'Elsa.Secrets.Core'">
                <PackageReference Include="MongoDB.Driver" />
              </ItemGroup>
              <ItemGroup Condition="'$(MSBuildProjectName)' == 'Elsa.Unrelated.Core'">
                <PackageReference Include="Npgsql" />
              </ItemGroup>
            </Project>
            """);
        fixture.Write("src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><AssemblyName>Elsa.Secrets.Core</AssemblyName></PropertyGroup>
            </Project>
            """);

        var violations = new EfCoreSurfaceScanner(fixture.Path).Scan().FindEfFreeBoundaryViolations();

        Assert.Contains(
            "src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj reaches concrete provider package MongoDB.Driver",
            violations);
        Assert.DoesNotContain(violations, violation => violation.Contains("Npgsql", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_follows_explicit_shared_build_imports_for_protected_projects()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("eng/persistence-provider.props", """
            <Project>
              <ItemGroup><PackageReference Include="Npgsql" /></ItemGroup>
            </Project>
            """);
        fixture.Write("src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><AssemblyName>Elsa.Secrets.Core</AssemblyName></PropertyGroup>
              <Import Project="../../../../eng/persistence-provider.props" />
            </Project>
            """);

        var violations = new EfCoreSurfaceScanner(fixture.Path).Scan().FindEfFreeBoundaryViolations();

        Assert.Contains(
            "src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj reaches concrete provider package Npgsql",
            violations);
    }

    [Fact]
    public void Scanner_uses_each_protected_projects_resolved_graph_without_counting_unrelated_projects()
    {
        using var fixture = new TemporaryRepository();
        fixture.Write("src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><AssemblyName>Elsa.Secrets.Core</AssemblyName></PropertyGroup>
            </Project>
            """);
        fixture.Write("src/Elsa/Secrets/Core/obj/project.assets.json", """
            {
              "libraries": {
                "Microsoft.Data.SqlClient/6.1.0": {},
                "Unrelated.Json.Library/1.0.0": {}
              }
            }
            """);
        fixture.Write("src/Elsa/Unrelated/Elsa.Unrelated.csproj", """
            <Project Sdk="Microsoft.NET.Sdk" />
            """);
        fixture.Write("src/Elsa/Unrelated/obj/project.assets.json", """
            {
              "libraries": {
                "MongoDB.Driver/3.4.0": {}
              }
            }
            """);

        var violations = new EfCoreSurfaceScanner(fixture.Path).Scan().FindEfFreeBoundaryViolations();

        Assert.Contains(
            "src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj resolves concrete provider package Microsoft.Data.SqlClient",
            violations);
        Assert.DoesNotContain(violations, violation => violation.Contains("Unrelated.Json.Library", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, violation => violation.Contains("Elsa.Unrelated.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, violation => violation.Contains("MongoDB.Driver", StringComparison.Ordinal));
    }

    private static string BaselinePath => Path.Combine(
        RepoRoot,
        "tests",
        "Elsa",
        "Architecture",
        "Baselines",
        "ef-core-surface.json");

    private static EfCoreSurfaceSnapshot EmptySurface() => new(
        [], [], [], [], [], [], [], [], [], [], [], [], [], []);

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }

    private static RestoreReceiptFixture CreateRestoreReceiptFixture(Action<TemporaryRepository>? beforeReceipt = null)
    {
        var repository = new TemporaryRepository();
        repository.Write("tools/architecture/restore-zero-ef-certification.sh", "#!/usr/bin/env bash\nexit 0\n");
        repository.Write("NuGet.config", "<configuration />");
        repository.Write("Directory.Build.props", "<Project />");
        repository.Write("Directory.Packages.props", "<Project />");
        repository.Write("eng/imported.props", "<Project />");
        repository.Write("src/Consumer/Elsa.Consumer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="../../eng/imported.props" />
            </Project>
            """);
        repository.Write(
            "src/Consumer/obj/project.assets.json",
            AssetsJson(repository, "src/Consumer/Elsa.Consumer.csproj", "initial"));
        beforeReceipt?.Invoke(repository);

        var repositoryState = new RestoreReceiptRepositoryState(
            "0123456789abcdef0123456789abcdef01234567",
            HashText("clean"),
            "10.0.100");
        WriteReceipt(repository, CreateReceipt(repository.Path, repositoryState));
        return new RestoreReceiptFixture(repository, repositoryState);
    }

    private static RestoreReceiptDocument CreateReceipt(
        string repositoryPath,
        RestoreReceiptRepositoryState repositoryState)
    {
        var projects = EnumerateRepositoryFiles(repositoryPath, path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var inputs = EnumerateRepositoryFiles(repositoryPath, IsDependencyInput)
            .Select(path => new RestoreReceiptInputEntry(Relative(repositoryPath, path), HashFile(path)))
            .ToArray();
        var inputFingerprint = Fingerprint(inputs.Select(input => $"{input.Path}\t{input.Sha256}"));
        return new RestoreReceiptDocument(
            1,
            "elsa-zero-ef-all-project-restore",
            new RestoreReceiptRepository(repositoryState.GitHead, repositoryState.WorktreeStatusSha256),
            new RestoreReceiptRestore(
                1,
                "tools/architecture/restore-zero-ef-certification.sh",
                HashFile(Path.Combine(repositoryPath, "tools/architecture/restore-zero-ef-certification.sh")),
                ["dotnet", "restore", "<project>", "--force-evaluate", "--configfile", "NuGet.config"],
                "10.0.100"),
            new RestoreReceiptDiscovery(
                1,
                [".git", "bin", "obj"],
                projects.Select(path => Relative(repositoryPath, path)).ToArray(),
                Fingerprint(projects.Select(path => Relative(repositoryPath, path)))),
            new RestoreReceiptInputs(1, inputs, inputFingerprint),
            projects.Select(project =>
            {
                var relativeProject = Relative(repositoryPath, project);
                var directory = Path.GetDirectoryName(relativeProject)!.Replace(Path.DirectorySeparatorChar, '/');
                var relativeAssets = $"{directory}/obj/project.assets.json";
                return new RestoreReceiptProject(
                    relativeProject,
                    inputFingerprint,
                    new RestoreReceiptAssets(
                        relativeAssets,
                        HashFile(Path.Combine(repositoryPath, relativeAssets)),
                        relativeProject));
            }).ToArray());
    }

    private static void WriteReceipt(TemporaryRepository repository, RestoreReceiptDocument receipt) =>
        repository.Write(
            "artifacts/zero-ef/restore-receipt.json",
            System.Text.Json.JsonSerializer.Serialize(
                receipt,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

    private static string AssetsJson(
        TemporaryRepository repository,
        string restoreProjectPath,
        string marker,
        params string[] configPaths)
    {
        if (configPaths.Length == 0)
            configPaths = ["NuGet.config"];
        var projectIdentity = System.Text.Json.JsonSerializer.Serialize(
            Path.Combine(repository.Path, restoreProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        var configs = System.Text.Json.JsonSerializer.Serialize(configPaths
            .Select(path => Path.Combine(repository.Path, path.Replace('/', Path.DirectorySeparatorChar)))
            .ToArray());
        return $$"""
        {
          "marker": "{{marker}}",
          "project": {
            "restore": {
              "projectUniqueName": {{projectIdentity}},
              "configFilePaths": {{configs}}
            }
          }
        }
        """;
    }

    private static string AssetsJsonWithRawConfig(
        TemporaryRepository repository,
        string restoreProjectPath,
        string rawConfig)
    {
        var projectIdentity = System.Text.Json.JsonSerializer.Serialize(
            Path.Combine(repository.Path, restoreProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        return $$"""
        {
          "project": {
            "restore": {
              "projectUniqueName": {{projectIdentity}},
              "configFilePaths": {{rawConfig}}
            }
          }
        }
        """;
    }

    private static string[] EnumerateRepositoryFiles(string root, Func<string, bool> predicate) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .Where(predicate)
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsDependencyInput(string path)
    {
        var name = Path.GetFileName(path);
        return path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NuGet.config", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("global.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedPath(string path) => path
        .Split(Path.DirectorySeparatorChar)
        .Any(segment => segment is ".git" or "bin" or "obj");

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string HashFile(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string HashText(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Fingerprint(IEnumerable<string> entries) =>
        HashText(string.Join("\n", entries.Order(StringComparer.Ordinal)) + "\n");

    private sealed class RestoreReceiptFixture : IDisposable
    {
        public RestoreReceiptFixture(TemporaryRepository repository, RestoreReceiptRepositoryState repositoryState)
        {
            Repository = repository;
            RepositoryState = repositoryState;
        }

        public TemporaryRepository Repository { get; }

        public RestoreReceiptRepositoryState RepositoryState { get; }

        public string ReceiptPath => Path.Combine(Repository.Path, "artifacts/zero-ef/restore-receipt.json");

        public void Dispose() => Repository.Dispose();
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository() => Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"elsa-ef-ratchet-{Guid.NewGuid():N}");

        public string Path { get; }

        public void Write(string relativePath, string content)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
