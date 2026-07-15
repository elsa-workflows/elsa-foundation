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
    public void Baseline_comparison_reports_both_expansion_and_stale_entries()
    {
        var baseline = EmptySurface() with { EfProjects = ["removed.csproj"] };
        var actual = EmptySurface() with { EfProjects = ["added.csproj"] };

        var differences = EfCoreSurfaceBaseline.Compare(baseline, actual);

        Assert.Contains("EF surface expanded [EfProjects]: added.csproj", differences);
        Assert.Contains(
            "EF surface shrank [EfProjects]; remove this stale baseline entry: removed.csproj",
            differences);
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
        fixture.Write("docker/compose/elsa-server.shells.json", """
            { "Features": { "PersistenceEFCoreSqlite": {} } }
            """);

        var entries = new EfCoreSurfaceScanner(fixture.Path).Scan().HostConfigurationFiles;

        Assert.DoesNotContain(entries, entry => entry.StartsWith("docker/compose/comments.json -> ", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.StartsWith("docker/compose/elsa-server.shells.json -> ", StringComparison.Ordinal));
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
