using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Elsa.Architecture.Tests;

internal sealed record EfCoreSurfaceSnapshot(
    IReadOnlyList<string> EfProjects,
    IReadOnlyList<string> DirectPackageReferences,
    IReadOnlyList<string> CentralPackageVersions,
    IReadOnlyList<string> SharedBuildPackageReferences,
    IReadOnlyList<string> DirectEfProjectReferences,
    IReadOnlyList<string> TransitiveEfProjectConsumers,
    IReadOnlyList<string> TransitiveEfPackageConsumers,
    IReadOnlyList<string> ResolvedEfPackageConsumers,
    IReadOnlyList<string> ProjectsMissingAssets,
    IReadOnlyList<string> MigrationFiles,
    IReadOnlyList<string> DbContextFiles,
    IReadOnlyList<string> RegistrationFiles,
    IReadOnlyList<string> HostConfigurationFiles,
    IReadOnlyList<string> EfFreeBoundaryViolations)
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Categories() =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [nameof(EfProjects)] = EfProjects,
            [nameof(DirectPackageReferences)] = DirectPackageReferences,
            [nameof(CentralPackageVersions)] = CentralPackageVersions,
            [nameof(SharedBuildPackageReferences)] = SharedBuildPackageReferences,
            [nameof(DirectEfProjectReferences)] = DirectEfProjectReferences,
            [nameof(TransitiveEfProjectConsumers)] = TransitiveEfProjectConsumers,
            [nameof(TransitiveEfPackageConsumers)] = TransitiveEfPackageConsumers,
            [nameof(ResolvedEfPackageConsumers)] = ResolvedEfPackageConsumers,
            [nameof(ProjectsMissingAssets)] = ProjectsMissingAssets,
            [nameof(MigrationFiles)] = MigrationFiles,
            [nameof(DbContextFiles)] = DbContextFiles,
            [nameof(RegistrationFiles)] = RegistrationFiles,
            [nameof(HostConfigurationFiles)] = HostConfigurationFiles
        };

    public IReadOnlyList<string> FindEfFreeBoundaryViolations() => EfFreeBoundaryViolations;
}

internal sealed record EfCoreSurfaceBaselineDocument(
    int SchemaVersion,
    EfCoreSurfaceSnapshot Surface,
    IReadOnlyList<string>? ProtectedProviderNeutralProjects = null);

internal static class PersistenceProviderNeutralityBoundary
{
    public static IReadOnlyList<string> ProjectNames { get; } =
    [
        "Elsa.Workflows.Runtime.Core",
        "Elsa.Foundation.Identity.Abstractions",
        "Elsa.Secrets.Core",
        "Elsa.Workflows.Runtime.Distributed"
    ];

    public static bool IsProtectedProject(string projectName) =>
        ProjectNames.Contains(projectName, StringComparer.Ordinal);

    public static bool IsConcreteProviderPackage(string packageName) =>
        IsPackageFamily(packageName, "Groundwork") ||
        packageName.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
        IsPackageFamily(packageName, "Microsoft.Data.Sqlite") ||
        IsPackageFamily(packageName, "SQLitePCLRaw") ||
        IsPackageFamily(packageName, "Microsoft.Data.SqlClient") ||
        IsPackageFamily(packageName, "System.Data.SqlClient") ||
        IsPackageFamily(packageName, "Npgsql") ||
        IsPackageFamily(packageName, "MongoDB");

    public static bool IsConcreteProviderProject(string projectName, string relativePath) =>
        HasProviderMarker(projectName) || HasProviderMarker(relativePath);

    private static bool IsPackageFamily(string packageName, string family) =>
        packageName.Equals(family, StringComparison.OrdinalIgnoreCase) ||
        packageName.StartsWith(family + ".", StringComparison.OrdinalIgnoreCase);

    private static bool HasProviderMarker(string value) =>
        value.Contains(".Groundwork", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(".EFCore", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(".EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/Groundwork/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/EFCore/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/EntityFrameworkCore/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/Sqlite/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/SqlServer/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/PostgreSql/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/MongoDb/", StringComparison.OrdinalIgnoreCase);
}

internal static class EfCoreSurfaceBaseline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static EfCoreSurfaceSnapshot Load(string path)
    {
        var document = LoadDocument(path);
        return document.Surface;
    }

    public static EfCoreSurfaceBaselineDocument LoadDocument(string path)
    {
        var document = JsonSerializer.Deserialize<EfCoreSurfaceBaselineDocument>(File.ReadAllText(path), JsonOptions)
                       ?? throw new InvalidOperationException($"EF Core surface baseline '{path}' is empty.");
        if (document.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported EF Core surface baseline schema {document.SchemaVersion}.");
        return document;
    }

    public static void Save(string path, EfCoreSurfaceSnapshot surface)
    {
        if (surface.ProjectsMissingAssets.Count != 0)
        {
            throw IncompleteRestoreError(
                "Cannot update the EF Core surface baseline from an incomplete repository restore.",
                surface.ProjectsMissingAssets);
        }

        if (!File.Exists(path))
            throw new InvalidOperationException($"Cannot update the EF Core surface because the reviewed baseline is missing: '{path}'.");

        var expansions = Compare(Load(path), surface)
            .Where(difference => difference.StartsWith("EF surface expanded", StringComparison.Ordinal))
            .ToArray();
        if (expansions.Length != 0)
        {
            throw new InvalidOperationException(
                "Refusing to expand the reviewed EF Core surface baseline:" + Environment.NewLine +
                string.Join(Environment.NewLine, expansions));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new EfCoreSurfaceBaselineDocument(
                    1,
                    surface,
                    PersistenceProviderNeutralityBoundary.ProjectNames),
                JsonOptions) + Environment.NewLine);
    }

    public static IReadOnlyList<string> Compare(EfCoreSurfaceSnapshot baseline, EfCoreSurfaceSnapshot actual)
    {
        var unrestoredProjects = actual.ProjectsMissingAssets
            .Except(baseline.ProjectsMissingAssets, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unrestoredProjects.Length != 0)
        {
            throw IncompleteRestoreError(
                "Cannot compare the EF Core surface against an incomplete repository restore; " +
                "unrestored projects drop out of the resolved package scan and would report phantom surface changes.",
                unrestoredProjects);
        }

        var differences = new List<string>();
        var actualCategories = actual.Categories();
        foreach (var (category, expectedEntries) in baseline.Categories())
        {
            var expected = expectedEntries.ToHashSet(StringComparer.Ordinal);
            var observed = actualCategories[category].ToHashSet(StringComparer.Ordinal);
            differences.AddRange(observed.Except(expected).Order(StringComparer.Ordinal)
                .Select(entry => $"EF surface expanded [{category}]: {entry}"));
            differences.AddRange(expected.Except(observed).Order(StringComparer.Ordinal)
                .Select(entry => $"EF surface shrank [{category}]; remove this stale baseline entry: {entry}"));
        }
        return differences;
    }

    private static InvalidOperationException IncompleteRestoreError(string reason, IEnumerable<string> missingProjects) =>
        new($"{reason} Run 'dotnet restore Elsa.Server.slnx' first. Missing assets: {string.Join(", ", missingProjects)}");
}

internal sealed class EfCoreSurfaceScanner
{
    private static readonly Regex DbContextDeclarationPattern = new(
        @"\b(?:class|record(?:\s+class)?)\s+(?:\w*DbContext\b|\w+(?:\s*\([^;{]*\))?\s*:\s*(?:global::)?(?:\w+\.)*\w*(?:DbContext|DbContextBase)(?:\s*<|\b))",
        RegexOptions.Compiled);

    private static readonly Regex EfMigrationPattern = new(
        @":\s*(?:\w+\.)*(?:Migration|ModelSnapshot)\b|\[(?:\w+\.)*(?:Migration|DbContext)(?:Attribute)?\s*\(",
        RegexOptions.Compiled);

    private static readonly string[] RegistrationTokens =
    [
        "UseEntityFrameworkCore",
        "AddEntityFrameworkStores",
        "AddDbContext",
        "AddDbContextFactory",
        "AddPooledDbContextFactory",
        "UseSqlite",
        "UseSqlServer",
        "UseNpgsql",
        "UseMySql",
        "UseOracle",
        "UseCosmos",
        "UseInMemoryDatabase",
        "EFCorePersistenceShellFeature",
        "EFCoreActivitiesPersistenceFeature",
        "EFCoreWorkflowsPersistenceFeature",
        "EFCoreOpenTelemetryPersistenceFeature",
        "EFCoreStructuredLogsPersistenceFeature",
        "AspNetCoreIdentityEntityFrameworkCoreFeature"
    ];

    private readonly string _repoRoot;

    public EfCoreSurfaceScanner(string repoRoot) => _repoRoot = Path.GetFullPath(repoRoot);

    public EfCoreSurfaceSnapshot Scan()
    {
        var projects = LoadProjects();
        var projectsByPath = projects.ToDictionary(x => x.FullPath, PathComparer);
        var efProjects = projects.Where(IsEfProject).ToArray();
        var efProjectPaths = efProjects.Select(x => x.FullPath).ToHashSet(PathComparer);
        var reachable = projects.ToDictionary(
            project => project.FullPath,
            project => ReachableProjects(project, projectsByPath),
            PathComparer);

        var directPackages = projects
            .SelectMany(project => project.PackageReferences
                .Where(IsEfPackage)
                .Select(package => Pair(project.RelativePath, package)))
            .Sorted();
        var directEfReferences = projects
            .SelectMany(project => project.ProjectReferences
                .Where(efProjectPaths.Contains)
                .Select(reference => Pair(project.RelativePath, projectsByPath[reference].RelativePath)))
            .Sorted();
        var transitiveEfProjects = projects
            .SelectMany(project => reachable[project.FullPath]
                .Where(efProjectPaths.Contains)
                .Select(reference => Pair(project.RelativePath, projectsByPath[reference].RelativePath)))
            .Sorted();
        var transitiveEfPackages = projects
            .SelectMany(project => reachable[project.FullPath]
                .Append(project.FullPath)
                .SelectMany(reference => projectsByPath[reference].PackageReferences)
                .Where(IsEfPackage)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(package => Pair(project.RelativePath, package)))
            .Sorted();
        var resolvedEfPackages = projects
            .SelectMany(project => project.ResolvedPackages
                .Where(IsEfPackage)
                .Select(package => Pair(project.RelativePath, package)))
            .Sorted();
        var projectsMissingAssets = projects
            .Where(project => !project.HasAssets)
            .Select(project => project.RelativePath)
            .Sorted();
        var sharedBuildPackages = SharedBuildEfPackageReferences();

        var boundaryProjects = projects.Where(IsEfFreeBoundary).ToArray();
        var efBoundaryViolations = boundaryProjects.SelectMany(project =>
        {
            var violations = new List<string>();
            violations.AddRange(reachable[project.FullPath]
                .Where(efProjectPaths.Contains)
                .Select(reference => $"{project.RelativePath} reaches EF project {projectsByPath[reference].RelativePath}"));
            violations.AddRange(reachable[project.FullPath]
                .Append(project.FullPath)
                .SelectMany(reference => projectsByPath[reference].PackageReferences)
                .Where(IsEfPackage)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(package => $"{project.RelativePath} reaches EF package {package}"));
            violations.AddRange(project.ResolvedPackages
                .Where(IsEfPackage)
                .Select(package => $"{project.RelativePath} resolves EF package {package}"));
            return violations;
        });
        var boundaryViolations = efBoundaryViolations
            .Concat(FindProtectedProviderNeutralityViolations(projects, projectsByPath, reachable))
            .Sorted();

        return new(
            efProjects.Select(x => x.RelativePath).Sorted(),
            directPackages,
            CentralEfPackageVersions(),
            sharedBuildPackages,
            directEfReferences,
            transitiveEfProjects,
            transitiveEfPackages,
            resolvedEfPackages,
            projectsMissingAssets,
            SourceFiles("*.cs").Where(IsEfMigration).Select(Relative).Sorted(),
            SourceFiles("*.cs").Where(IsDbContextFile).Select(Relative).Sorted(),
            SourceFiles("*.cs").SelectMany(RegistrationEntries).Sorted(),
            ConfigurationFiles().SelectMany(HostConfigurationEntries).Sorted(),
            boundaryViolations);
    }

    public IReadOnlyList<string> EfFreeBoundaryProjectNames() =>
        LoadProjects()
            .Where(IsEfFreeBoundary)
            .Select(project => project.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> FindProtectedProviderNeutralityViolations()
    {
        var projects = LoadProjects();
        var projectsByPath = projects.ToDictionary(project => project.FullPath, PathComparer);
        var reachable = projects.ToDictionary(
            project => project.FullPath,
            project => ReachableProjects(project, projectsByPath),
            PathComparer);
        return FindProtectedProviderNeutralityViolations(projects, projectsByPath, reachable).Sorted();
    }

    private ProjectInfo[] LoadProjects()
    {
        var paths = Directory.EnumerateFiles(_repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .Select(Path.GetFullPath)
            .Order(PathComparer)
            .ToArray();

        return paths.Select(path =>
        {
            var document = XDocument.Load(path);
            var name = document.Descendants("AssemblyName").Select(x => x.Value).FirstOrDefault()
                       ?? Path.GetFileNameWithoutExtension(path);
            var declaredPackages = EvaluatedBuildPackageReferences(path);
            var declaredReferences = document.Descendants("ProjectReference")
                .Select(x => x.Attribute("Include")?.Value)
                .OfType<string>()
                .Select(reference => reference
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar))
                .Select(reference => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, reference)))
                .ToArray();
            var assetsPath = Path.Combine(Path.GetDirectoryName(path)!, "obj", "project.assets.json");
            var hasAssets = File.Exists(assetsPath);
            var assets = hasAssets ? ReadAssets(assetsPath) : AssetsInfo.Empty;
            var packages = declaredPackages
                .Concat(assets.DirectPackages)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var references = declaredReferences
                .Concat(assets.ProjectReferences)
                .Where(File.Exists)
                .Distinct(PathComparer)
                .ToArray();
            return new ProjectInfo(path, Relative(path), name, packages, references, assets.ResolvedPackages, hasAssets);
        }).ToArray();
    }

    private IEnumerable<string> FindProtectedProviderNeutralityViolations(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<string, ProjectInfo> projectsByPath,
        IReadOnlyDictionary<string, HashSet<string>> reachable)
    {
        foreach (var project in projects.Where(project =>
                     PersistenceProviderNeutralityBoundary.IsProtectedProject(project.Name)))
        {
            var reachableProjectPaths = reachable[project.FullPath];
            foreach (var providerProjectPath in reachableProjectPaths.Where(path =>
                         PersistenceProviderNeutralityBoundary.IsConcreteProviderProject(
                             projectsByPath[path].Name,
                             projectsByPath[path].RelativePath)))
            {
                yield return $"{project.RelativePath} reaches concrete provider project {projectsByPath[providerProjectPath].RelativePath}";
            }

            foreach (var package in reachableProjectPaths
                         .Append(project.FullPath)
                         .SelectMany(path => projectsByPath[path].PackageReferences)
                         .Where(PersistenceProviderNeutralityBoundary.IsConcreteProviderPackage)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return $"{project.RelativePath} reaches concrete provider package {package}";
            }

            foreach (var package in project.ResolvedPackages
                         .Where(PersistenceProviderNeutralityBoundary.IsConcreteProviderPackage)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return $"{project.RelativePath} resolves concrete provider package {package}";
            }
        }
    }

    private IReadOnlyList<string> EvaluatedBuildPackageReferences(string projectPath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildProjectName"] = projectName,
            ["MSBuildProjectFullPath"] = projectPath,
            ["MSBuildProjectDirectory"] = Path.GetDirectoryName(projectPath)!,
            ["AssemblyName"] = projectName
        };
        var packages = new List<string>();
        var visited = new HashSet<string>(PathComparer);

        var directoryBuildProps = FindNearestBuildFile(projectPath, "Directory.Build.props");
        if (directoryBuildProps is not null)
            EvaluateBuildFile(directoryBuildProps, properties, packages, visited);

        EvaluateBuildFile(projectPath, properties, packages, visited);

        var directoryBuildTargets = FindNearestBuildFile(projectPath, "Directory.Build.targets");
        if (directoryBuildTargets is not null)
            EvaluateBuildFile(directoryBuildTargets, properties, packages, visited);

        return packages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void EvaluateBuildFile(
        string path,
        Dictionary<string, string> properties,
        ICollection<string> packages,
        ISet<string> visited)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path) || !visited.Add(path))
            return;

        properties.TryGetValue("MSBuildThisFileDirectory", out var previousThisFileDirectory);
        properties["MSBuildThisFileDirectory"] = Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar;
        var document = XDocument.Load(path);
        foreach (var element in document.Root?.Elements() ?? [])
        {
            if (!ConditionMatches(element.Attribute("Condition")?.Value, path, properties))
                continue;

            switch (element.Name.LocalName)
            {
                case "PropertyGroup":
                    foreach (var property in element.Elements().Where(property =>
                                 ConditionMatches(property.Attribute("Condition")?.Value, path, properties)))
                    {
                        properties[property.Name.LocalName] = ExpandProperties(property.Value, properties);
                    }
                    break;
                case "ItemGroup":
                    foreach (var reference in element.Elements().Where(reference =>
                                 reference.Name.LocalName == "PackageReference" &&
                                 ConditionMatches(reference.Attribute("Condition")?.Value, path, properties)))
                    {
                        var package = reference.Attribute("Include")?.Value;
                        if (!string.IsNullOrWhiteSpace(package))
                            packages.Add(ExpandProperties(package, properties));
                    }
                    break;
                case "Import":
                    EvaluateImport(element, path, properties, packages, visited);
                    break;
                case "ImportGroup":
                    foreach (var import in element.Elements().Where(import =>
                                 import.Name.LocalName == "Import" &&
                                 ConditionMatches(import.Attribute("Condition")?.Value, path, properties)))
                    {
                        EvaluateImport(import, path, properties, packages, visited);
                    }
                    break;
            }
        }

        if (previousThisFileDirectory is null)
            properties.Remove("MSBuildThisFileDirectory");
        else
            properties["MSBuildThisFileDirectory"] = previousThisFileDirectory;
    }

    private void EvaluateImport(
        XElement import,
        string importingPath,
        Dictionary<string, string> properties,
        ICollection<string> packages,
        ISet<string> visited)
    {
        var project = import.Attribute("Project")?.Value;
        if (string.IsNullOrWhiteSpace(project))
            return;

        foreach (var importPath in ExpandProperties(project, properties).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = importPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(Path.GetDirectoryName(importingPath)!, normalized);
            EvaluateBuildFile(fullPath, properties, packages, visited);
        }
    }

    private string? FindNearestBuildFile(string projectPath, string fileName)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(projectPath)!);
        while (directory is not null && IsWithinRepository(directory.FullName))
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        return null;
    }

    private bool IsWithinRepository(string path)
    {
        var relative = Path.GetRelativePath(_repoRoot, path);
        return relative == "." ||
               (!relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static bool ConditionMatches(
        string? condition,
        string sourcePath,
        IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        var expanded = ExpandProperties(condition, properties).Trim();
        if (bool.TryParse(expanded, out var boolean))
            return boolean;

        var comparison = Regex.Match(
            expanded,
            "^\\s*(?<quote>['\"])(?<left>.*?)\\k<quote>\\s*(?<operator>==|!=)\\s*(?<rightQuote>['\"])(?<right>.*?)\\k<rightQuote>\\s*$",
            RegexOptions.CultureInvariant);
        if (comparison.Success)
        {
            var equal = string.Equals(
                comparison.Groups["left"].Value,
                comparison.Groups["right"].Value,
                StringComparison.OrdinalIgnoreCase);
            return comparison.Groups["operator"].Value == "==" ? equal : !equal;
        }

        var exists = Regex.Match(expanded, "^Exists\\((?<quote>['\"])(?<path>.*?)\\k<quote>\\)$", RegexOptions.IgnoreCase);
        if (exists.Success)
        {
            var candidate = exists.Groups["path"].Value
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(Path.GetDirectoryName(sourcePath)!, candidate);
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }

        // An unevaluated condition must not create a blind spot in a provider-neutrality gate.
        return true;
    }

    private static string ExpandProperties(string value, IReadOnlyDictionary<string, string> properties) =>
        Regex.Replace(
            value,
            @"\$\((?<name>[^)]+)\)",
            match => properties.GetValueOrDefault(match.Groups["name"].Value) ?? string.Empty);

    private static AssetsInfo ReadAssets(string assetsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var resolvedPackages = document.RootElement.TryGetProperty("libraries", out var libraries)
            ? libraries.EnumerateObject()
                .Where(library => !library.Value.TryGetProperty("type", out var type) ||
                                  !string.Equals(type.GetString(), "project", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Name.Split('/', 2)[0])
            : [];
        var directPackages = new List<string>();
        var projectReferences = new List<string>();
        if (document.RootElement.TryGetProperty("project", out var project))
        {
            if (project.TryGetProperty("frameworks", out var frameworks))
            {
                foreach (var framework in frameworks.EnumerateObject())
                {
                    if (!framework.Value.TryGetProperty("dependencies", out var dependencies))
                        continue;
                    directPackages.AddRange(dependencies.EnumerateObject()
                        .Where(dependency => !dependency.Value.TryGetProperty("target", out var target) ||
                                             target.GetString() == "Package")
                        .Select(dependency => dependency.Name));
                }
            }

            if (project.TryGetProperty("restore", out var restore) &&
                restore.TryGetProperty("frameworks", out var restoreFrameworks))
            {
                foreach (var framework in restoreFrameworks.EnumerateObject())
                {
                    if (!framework.Value.TryGetProperty("projectReferences", out var references))
                        continue;
                    projectReferences.AddRange(references.EnumerateObject()
                        .Select(reference => Path.GetFullPath(reference.Name)));
                }
            }
        }

        return new(
            resolvedPackages
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray(),
            directPackages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            projectReferences.Distinct(PathComparer).ToArray());
    }

    private HashSet<string> ReachableProjects(ProjectInfo root, IReadOnlyDictionary<string, ProjectInfo> projects)
    {
        var result = new HashSet<string>(PathComparer);
        var pending = new Stack<string>(root.ProjectReferences.Where(projects.ContainsKey));
        while (pending.TryPop(out var current))
        {
            if (!result.Add(current))
                continue;
            foreach (var reference in projects[current].ProjectReferences.Where(projects.ContainsKey))
                pending.Push(reference);
        }
        return result;
    }

    private IReadOnlyList<string> CentralEfPackageVersions() =>
        Directory.EnumerateFiles(_repoRoot, "Directory.Packages.props", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .SelectMany(path => XDocument.Load(path).Descendants("PackageVersion")
                .Select(x => x.Attribute("Include")?.Value ?? x.Attribute("Update")?.Value)
                .OfType<string>()
                .Where(IsEfPackage)
                .Select(package => Pair(Relative(path), package)))
            .Sorted();

    private IReadOnlyList<string> SharedBuildEfPackageReferences() =>
        Directory.EnumerateFiles(_repoRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .Where(path => path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => XDocument.Load(path).Descendants("PackageReference")
                .Select(x => x.Attribute("Include")?.Value ?? x.Attribute("Update")?.Value)
                .OfType<string>()
                .Where(IsEfPackage)
                .Select(package => Pair(Relative(path), package)))
            .Sorted();

    private IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(_repoRoot, pattern, SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .Where(path => !Relative(path).StartsWith("tests/Elsa/Architecture/EfCoreSurface", StringComparison.Ordinal));

    private IEnumerable<string> ConfigurationFiles() =>
        new[] { "*.json", "*.yaml", "*.yml", "*.toml", "*.xml" }
            .SelectMany(SourceFiles);

    private bool IsEfMigration(string path)
    {
        var relative = Relative(path);
        if (relative.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase) &&
            (relative.Contains("/EFCore/", StringComparison.OrdinalIgnoreCase) ||
             relative.Contains("/EntityFrameworkCore/", StringComparison.OrdinalIgnoreCase)))
            return true;

        return EfMigrationPattern.IsMatch(ExecutableCSharp(File.ReadAllText(path)));
    }

    private static bool IsDbContextFile(string path) =>
        DbContextDeclarationPattern.IsMatch(ExecutableCSharp(File.ReadAllText(path)));

    private IEnumerable<string> RegistrationEntries(string path)
    {
        var text = ExecutableCSharp(File.ReadAllText(path));
        foreach (var token in RegistrationTokens)
        {
            var count = CountOccurrences(text, token, StringComparison.Ordinal);
            if (count != 0)
                yield return Pair(Relative(path), $"{token} x {count}");
        }
    }

    private IEnumerable<string> HostConfigurationEntries(string path)
    {
        var relative = Relative(path);
        if (!relative.StartsWith("src/", StringComparison.Ordinal) &&
            !relative.StartsWith("samples/", StringComparison.Ordinal) &&
            !relative.StartsWith("docker/", StringComparison.Ordinal))
            yield break;

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var matches = new List<string>();
            CollectJsonConfigurationEntries(document.RootElement, "$", matches);
            foreach (var match in matches)
                yield return Pair(relative, match);
            yield break;
        }

        var text = File.ReadAllText(path);
        foreach (var token in new[] { "EFCore", "EntityFrameworkCore" })
        {
            var count = CountOccurrences(text, token, StringComparison.OrdinalIgnoreCase);
            if (count != 0)
                yield return Pair(relative, $"{token} x {count}");
        }
    }

    private static void CollectJsonConfigurationEntries(JsonElement element, string path, ICollection<string> entries)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (IsCommentProperty(property.Name))
                    continue;
                var propertyPath = $"{path}.{property.Name}";
                AddEfTokens(property.Name, $"json:{propertyPath}", entries);
                CollectJsonConfigurationEntries(property.Value, propertyPath, entries);
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                CollectJsonConfigurationEntries(item, $"{path}[{index++}]", entries);
            return;
        }

        if (element.ValueKind == JsonValueKind.String)
            AddEfTokens(element.GetString() ?? string.Empty, $"json:{path}", entries);
    }

    private static void AddEfTokens(string value, string location, ICollection<string> entries)
    {
        foreach (var token in new[] { "EFCore", "EntityFrameworkCore" })
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
                entries.Add($"{location} -> {token}");
        }
    }

    private static bool IsCommentProperty(string name) =>
        name.StartsWith("//", StringComparison.Ordinal) ||
        name.Equals("$comment", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("_comment", StringComparison.OrdinalIgnoreCase);

    private static int CountOccurrences(string value, string token, StringComparison comparison)
    {
        var count = 0;
        var start = 0;
        while ((start = value.IndexOf(token, start, comparison)) >= 0)
        {
            count++;
            start += token.Length;
        }
        return count;
    }

    private static bool IsEfPackage(string package) =>
        package.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);

    private static bool IsEfProject(ProjectInfo project) =>
        project.RelativePath.Contains("/EFCore/", StringComparison.OrdinalIgnoreCase) ||
        project.RelativePath.Contains("/EntityFrameworkCore/", StringComparison.OrdinalIgnoreCase) ||
        project.Name.Contains(".EFCore", StringComparison.OrdinalIgnoreCase) ||
        project.Name.Contains(".EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);

    private static bool IsEfFreeBoundary(ProjectInfo project) =>
        PersistenceProviderNeutralityBoundary.ProjectNames.Contains(project.Name, StringComparer.Ordinal) ||
        project.Name.EndsWith(".Core", StringComparison.Ordinal) ||
        project.Name.EndsWith(".Abstractions", StringComparison.Ordinal) ||
        project.Name.EndsWith(".Contracts", StringComparison.Ordinal) ||
        project.Name.Contains(".Groundwork", StringComparison.Ordinal) ||
        project.RelativePath.Contains("/Core/", StringComparison.OrdinalIgnoreCase) ||
        project.RelativePath.Contains("/Abstractions/", StringComparison.OrdinalIgnoreCase) ||
        project.RelativePath.Contains("/Contracts/", StringComparison.OrdinalIgnoreCase) ||
        project.RelativePath.Contains("/Groundwork/", StringComparison.OrdinalIgnoreCase) ||
        project.RelativePath.Contains("/Persistence/Groundwork/", StringComparison.OrdinalIgnoreCase);

    private static string ExecutableCSharp(string source)
    {
        var result = source.ToCharArray();
        var index = 0;
        while (index < result.Length)
        {
            if (index + 1 < result.Length && result[index] == '/' && result[index + 1] == '/')
            {
                BlankUntil(result, ref index, static (chars, i) => chars[i] == '\n');
                continue;
            }

            if (index + 1 < result.Length && result[index] == '/' && result[index + 1] == '*')
            {
                result[index++] = ' ';
                result[index++] = ' ';
                while (index < result.Length)
                {
                    if (index + 1 < result.Length && result[index] == '*' && result[index + 1] == '/')
                    {
                        result[index++] = ' ';
                        result[index++] = ' ';
                        break;
                    }
                    if (result[index] != '\n' && result[index] != '\r')
                        result[index] = ' ';
                    index++;
                }
                continue;
            }

            if (result[index] == '"' || result[index] == '\'')
            {
                var delimiter = result[index];
                var verbatim = delimiter == '"' && index > 0 && result[index - 1] == '@';
                result[index++] = ' ';
                while (index < result.Length)
                {
                    if (!verbatim && result[index] == '\\')
                    {
                        result[index++] = ' ';
                        if (index < result.Length)
                            result[index++] = ' ';
                        continue;
                    }
                    if (result[index] == delimiter)
                    {
                        result[index++] = ' ';
                        if (verbatim && index < result.Length && result[index] == delimiter)
                        {
                            result[index++] = ' ';
                            continue;
                        }
                        break;
                    }
                    if (result[index] != '\n' && result[index] != '\r')
                        result[index] = ' ';
                    index++;
                }
                continue;
            }

            index++;
        }
        return new string(result);
    }

    private static void BlankUntil(char[] chars, ref int index, Func<char[], int, bool> stop)
    {
        while (index < chars.Length && !stop(chars, index))
            chars[index++] = ' ';
    }

    private static bool IsIgnored(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private string Relative(string path) =>
        Path.GetRelativePath(_repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Pair(string source, string target) => $"{source} -> {target}";

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record ProjectInfo(
        string FullPath,
        string RelativePath,
        string Name,
        IReadOnlyList<string> PackageReferences,
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> ResolvedPackages,
        bool HasAssets);

    private sealed record AssetsInfo(
        IReadOnlyList<string> ResolvedPackages,
        IReadOnlyList<string> DirectPackages,
        IReadOnlyList<string> ProjectReferences)
    {
        public static AssetsInfo Empty { get; } = new([], [], []);
    }
}

internal static class EfCoreSurfaceEnumerableExtensions
{
    public static IReadOnlyList<string> Sorted(this IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
