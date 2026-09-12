using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// ADR 0073 selects EF Core as the destination persistence family, but each implementation still enters
/// through an explicitly reviewed program issue. This ratchet therefore keeps the currently admitted
/// surface at the vendor-owned OpenIddict host boundary plus the repository implementations explicitly
/// admitted by ADR 0072 and ADR 0073 / Program #1665. It reads
/// each source project's evaluated Release and Debug restore graphs and scans sources, so imported, conditional, transitive,
/// and provider-only EF edges anywhere else under <c>src/</c> fail and name the offender. Workbench's host
/// exception also validates its exact resolved EF package set. Each replacement changes this guard deliberately
/// with its own architecture evidence; the destination ADR alone is not a repository-wide exemption.
/// </summary>
public sealed class EfCoreDependencyGuardTests
{
    private static readonly string[] RestoreConfigurations = ["Release", "Debug"];
    private static readonly string[] AllowedEfConsumers = ["Elsa.Workbench"];
    private static readonly string[] AllowedWorkbenchEfPackages =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.EntityFrameworkCore.Analyzers",
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.InMemory",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.Sqlite.Core",
        "OpenIddict.EntityFrameworkCore",
        "OpenIddict.EntityFrameworkCore.Models"
    ];
    private const string EfPackageToken = "EntityFrameworkCore";

    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Only_admitted_consumers_and_pilot_projects_resolve_ef_core_packages()
    {
        var projects = LoadSrcProjects();

        Assert.All(AllowedEfConsumers, name => Assert.Contains(name, projects.Keys));

        var offenders = projects.Values
            .Where(project => !project.IsAdmittedRepository && !AllowedEfConsumers.Contains(project.Name, StringComparer.Ordinal))
            .SelectMany(project => project.EfPackagesByConfiguration
                .Where(configuration => configuration.Value.Length > 0)
                .Select(configuration => $"{project.Name} ({configuration.Key})"))
            .Order()
            .ToArray();

        Assert.True(offenders.Length == 0, Report("resolve EF Core outside the admitted consumers and pilot projects", offenders));
    }

    [Fact]
    public void Workbench_resolves_only_reviewed_vendor_and_pilot_EF_packages()
    {
        var offenders = LoadSrcProjects()["Elsa.Workbench"].EfPackagesByConfiguration
            .SelectMany(configuration =>
                FindUnexpectedEfPackages(configuration.Value, AllowedWorkbenchEfPackages)
                    .Select(package => $"{package} ({configuration.Key}) is not reviewed")
                    .Concat(FindUnexpectedEfPackages(AllowedWorkbenchEfPackages, configuration.Value)
                        .Select(package => $"{package} ({configuration.Key}) is missing from the reviewed closure")))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("are unreviewed EF packages resolved by Workbench", offenders));
    }

    [Fact]
    public void Every_admitted_Secrets_pilot_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0072SecretsEfPilot.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Secrets EF pilot closure", offenders));
    }

    [Fact]
    public void Every_admitted_Studio_Preferences_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073RepositoryFirstEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Studio Preferences EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Structured_Logs_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073StructuredLogsEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Structured Logs EF closure", offenders));
    }

    [Fact]
    public void Workbench_package_allowlist_rejects_an_unreviewed_transitive_wrapper()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": { "type": "package" },
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/1.0.0": {
                    "type": "package",
                    "dependencies": { "Microsoft.EntityFrameworkCore": "10.0.0" }
                  },
                  "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Equal(["Contoso.Persistence"], FindUnexpectedEfPackages(ReadEfDependencyPackages(assets), AllowedWorkbenchEfPackages));
    }

    [Fact]
    public void Evaluated_assets_detect_an_imported_provider_only_dependency()
    {
        const string assets = """
            {
              "libraries": {
                "Npgsql.EntityFrameworkCore.PostgreSQL/10.0.0": { "type": "package" },
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Npgsql.EntityFrameworkCore.PostgreSQL/10.0.0": {
                    "type": "package",
                    "dependencies": { "Microsoft.EntityFrameworkCore": "10.0.0" }
                  },
                  "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.True(ResolvesEfCore(assets));
        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            FindUnexpectedEfPackages(ReadEfDependencyPackages(assets), AllowedWorkbenchEfPackages));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"libraries\": []}")]
    [InlineData("{\"libraries\": {}}")]
    [InlineData("{\"libraries\": {}, \"targets\": []}")]
    public void Evaluated_assets_reject_missing_or_non_object_dependency_graphs(string assets)
    {
        Assert.Throws<InvalidOperationException>(() => ResolvesEfCore(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_target_node_missing_from_libraries()
    {
        const string assets = """
            {
              "libraries": {},
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/1.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_dependency_node_missing_from_libraries()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/1.0.0": {
                    "type": "package",
                    "dependencies": { "Microsoft.EntityFrameworkCore": "10.0.0" }
                  }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_library_without_a_known_node_type()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": {}
              },
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/1.0.0": {}
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_package_library_missing_from_every_target()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": { "type": "package" },
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_package_target_with_a_different_version_than_its_library_node()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/2.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_target_type_that_differs_from_its_library_node()
    {
        const string assets = """
            {
              "libraries": {
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "project" }
              },
              "targets": {
                "net10.0": {
                  "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void First_party_dependency_edges_cannot_be_conditionally_hidden_from_the_reviewed_restore_graphs()
    {
        var offenders = Directory.EnumerateFiles(RepoRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path) && IsMsBuildFile(path))
            .SelectMany(path => FindConditionalDependencyElements(XDocument.Load(path))
                .Select(element => $"{Path.GetRelativePath(RepoRoot, path)}: {element.Name.LocalName}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            Report("conditionally declare dependency/import edges that are not covered by the Release and Debug restore graphs", offenders));
    }

    [Theory]
    [InlineData("<Project><ItemGroup Condition=\"'$(Configuration)' == 'Debug'\"><PackageReference Include=\"Contoso.Persistence\" /></ItemGroup></Project>")]
    [InlineData("<Project><Choose><When Condition=\"'$(UseContoso)' == 'true'\"><ItemGroup><ProjectReference Include=\"Contoso.csproj\" /></ItemGroup></When></Choose></Project>")]
    [InlineData("<Project><Import Project=\"Contoso.props\" Condition=\"'$(Configuration)' == 'Release'\" /></Project>")]
    public void Conditional_dependency_declarations_are_detected(string xml)
    {
        Assert.NotEmpty(FindConditionalDependencyElements(XDocument.Parse(xml)));
    }

    [Fact]
    public void No_source_file_outside_the_admitted_surfaces_mentions_ef_core()
    {
        var offenders = Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !IsAdmittedEfSource(file))
            .Where(file => File.ReadAllText(file).Contains(EfPackageToken, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoRoot, file))
            .Order()
            .ToArray();

        Assert.True(offenders.Length == 0, Report("mention an EF package namespace in source", offenders));
    }

    private static Dictionary<string, Project> LoadSrcProjects()
    {
        var projects = new Dictionary<string, Project>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
        {
            var relativePath = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            projects.Add(
                Path.GetFileNameWithoutExtension(file),
                new Project(
                    Path.GetFileNameWithoutExtension(file),
                    RestoreConfigurations.ToDictionary(
                        configuration => configuration,
                        configuration => ReadProjectEfPackages(relativePath, configuration),
                        StringComparer.Ordinal),
                    Adr0072SecretsEfPilot.IsProjectPath(relativePath) ||
                    Adr0073RepositoryFirstEf.IsProjectPath(relativePath) ||
                    Adr0073StructuredLogsEf.IsProjectPath(relativePath)));
        }
        return projects;
    }

    private static string[] ReadProjectEfPackages(string relativeProjectPath, string configuration)
    {
        var projectDirectory = Path.GetDirectoryName(Path.Join(RepoRoot, relativeProjectPath))!;
        var assetsPath = configuration == "Release"
            ? Path.Join(projectDirectory, "obj", "project.assets.json")
            : Path.Join(projectDirectory, "obj", "ef-guard", configuration, "project.assets.json");
        if (!File.Exists(assetsPath))
            throw new InvalidOperationException(
                $"Evaluated {configuration} restore assets are required for '{relativeProjectPath}'. Restore Elsa.Server.slnx for both Release and Debug before running the EF dependency guard.");

        return ReadEfDependencyPackages(File.ReadAllText(assetsPath));
    }

    private static bool ResolvesEfCore(string assetsJson) => ReadEfDependencyPackages(assetsJson).Length > 0;

    private static string[] ReadEfDependencyPackages(string assetsJson)
    {
        using var document = JsonDocument.Parse(assetsJson);
        if (!document.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Evaluated restore assets must contain an object-valued 'libraries' graph.");

        if (!document.RootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Evaluated restore assets must contain an object-valued 'targets' graph.");

        var libraryNodes = libraries.EnumerateObject()
            .ToDictionary(
                library => library.Name,
                library => new LibraryNode(library.Name.Split('/', 2)[0], ReadLibraryType(library)),
                StringComparer.OrdinalIgnoreCase);
        var libraryNames = libraryNodes.Values.Select(library => library.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packageNames = libraryNodes.Values
            .Where(library => string.Equals(library.Type, "package", StringComparison.Ordinal))
            .Select(library => library.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dependencies = packageNames.ToDictionary(name => name, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var targetNodeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Evaluated restore target '{target.Name}' must be an object.");

            foreach (var library in target.Value.EnumerateObject())
            {
                if (!libraryNodes.TryGetValue(library.Name, out var libraryNode))
                    throw new InvalidOperationException($"Evaluated restore target node '{library.Name}' is missing from 'libraries'.");

                if (library.Value.ValueKind != JsonValueKind.Object ||
                    !library.Value.TryGetProperty("type", out var targetType) ||
                    targetType.ValueKind != JsonValueKind.String ||
                    !string.Equals(targetType.GetString(), libraryNode.Type, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Evaluated restore target node '{library.Name}' must be an object declaring type '{libraryNode.Type}' to match 'libraries'.");
                }

                var name = libraryNode.Name;
                targetNodeKeys.Add(library.Name);
                if (!library.Value.TryGetProperty("dependencies", out var libraryDependencies))
                    continue;
                if (libraryDependencies.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"Evaluated dependencies for '{name}' must be an object.");

                var dependencyNames = libraryDependencies.EnumerateObject().Select(dependency => dependency.Name).ToArray();
                var missingDependencies = dependencyNames.Where(dependency => !libraryNames.Contains(dependency)).ToArray();
                if (missingDependencies.Length > 0)
                    throw new InvalidOperationException(
                        $"Evaluated dependencies for '{name}' contain nodes missing from 'libraries': {string.Join(", ", missingDependencies)}.");

                if (packageNames.Contains(name))
                    dependencies[name].UnionWith(dependencyNames.Where(packageNames.Contains));
            }
        }

        var packagesMissingFromTargets = libraryNodes
            .Where(library => library.Value.Type == "package" && !targetNodeKeys.Contains(library.Key))
            .Select(library => library.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packagesMissingFromTargets.Length > 0)
            throw new InvalidOperationException(
                $"Evaluated restore package libraries are missing from every target: {string.Join(", ", packagesMissingFromTargets)}.");

        var efDependencyPackages = packageNames
            .Where(name => name.Contains(EfPackageToken, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var newlyDiscovered = dependencies
                .Where(package => package.Value.Overlaps(efDependencyPackages) && !efDependencyPackages.Contains(package.Key))
                .Select(package => package.Key)
                .ToArray();
            if (newlyDiscovered.Length == 0)
                break;

            efDependencyPackages.UnionWith(newlyDiscovered);
        }

        return efDependencyPackages.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ReadLibraryType(JsonProperty library)
    {
        if (!library.Value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Evaluated restore library '{library.Name}' must declare type 'package' or 'project'.");

        var libraryType = type.GetString()!;
        if (libraryType is not "package" and not "project")
            throw new InvalidOperationException($"Evaluated restore library '{library.Name}' must declare type 'package' or 'project'.");

        return libraryType;
    }

    private sealed record LibraryNode(string Name, string Type);

    private static string[] FindUnexpectedEfPackages(IEnumerable<string> resolved, IEnumerable<string> allowed) =>
        resolved.Except(allowed, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IEnumerable<XElement> FindConditionalDependencyElements(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference" or "Import")
            .Where(element => element.AncestorsAndSelf()
                .SelectMany(ancestor => ancestor.Attributes())
                .Any(attribute => attribute.Name.LocalName == "Condition"));

    private static bool IsAdmittedEfSource(string file)
    {
        var relativePath = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
        return Adr0072SecretsEfPilot.IsSurfacePath(relativePath) ||
               Adr0073RepositoryFirstEf.IsSurfacePath(relativePath) ||
               Adr0073StructuredLogsEf.IsSurfacePath(relativePath) ||
               OpenIddictPersistenceArchitectureTests.IsWorkbenchVendorEfSource(relativePath);
    }

    private static bool IsBuildOutput(string path) => path.Replace('\\', '/') is var p && (p.Contains("/bin/") || p.Contains("/obj/"));

    private static bool IsMsBuildFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);

    private static string Report(string what, IEnumerable<string> offenders) =>
        $"These src entries {what}:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}";

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record Project(string Name, IReadOnlyDictionary<string, string[]> EfPackagesByConfiguration, bool IsAdmittedRepository);

    /// <summary>
    /// ADR 0072's accepted, now-superseded Secrets EF surface. ADR 0073 preserves this as the only currently
    /// admitted first-party implementation while later Program #1665 replacements remain evidence-gated.
    /// <see cref="SecretsEfPersistencePilotArchitectureTests"/> owns the exact package and source inventory.
    /// </summary>
    internal static class Adr0072SecretsEfPilot
    {
        public static readonly string[] SurfacePathPrefixes =
        [
            "src/Elsa/Persistence/EntityFramework/",
            "src/Elsa/Secrets/Persistence/EntityFrameworkCore/",
            "tests/Elsa/Persistence/EntityFramework/",
            "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/"
        ];

        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["src/Elsa/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj"] = CorePackages(),
                ["src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.Design",
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ],
                ["tests/Elsa/Persistence/EntityFramework/Tests/Elsa.Persistence.EntityFramework.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"],
                ["tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe.csproj"] =
                [.. CorePackages(), "Npgsql.EntityFrameworkCore.PostgreSQL"],
                ["tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj"] =
                [.. CorePackages(), "Npgsql.EntityFrameworkCore.PostgreSQL"],
                ["tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.SqlServer.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.SqlServer"],
                ["tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsSurfacePath(string relativePath) =>
            SurfacePathPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal));

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// ADR 0073's first repository-first admission beyond the Secrets pilot. Provider engines remain
    /// confined to the focused test project; the production adapter owns only EF Core relational APIs.
    /// </summary>
    internal static class Adr0073RepositoryFirstEf
    {
        public static readonly string[] SurfacePathPrefixes =
        [
            "src/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/",
            "tests/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/"
        ];

        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["src/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["tests/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ],
                ["tests/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/Tests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core"
                ]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsSurfacePath(string relativePath) =>
            SurfacePathPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal));

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// ADR 0073's Diagnostics Structured Logs repository-first admission for issue #1695. The
    /// production adapter is provider-neutral; SQLite belongs to the behavioral test project and
    /// SQL Server, PostgreSQL, and MySQL packages belong only to the live-provider test project.
    /// </summary>
    internal static class Adr0073StructuredLogsEf
    {
        public static readonly string[] SurfacePathPrefixes =
        [
            "src/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/",
            "tests/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/"
        ];

        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["src/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["tests/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ],
                ["tests/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsSurfacePath(string relativePath) =>
            SurfacePathPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal));

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }
}
