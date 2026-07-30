using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
            [nameof(HostConfigurationFiles)] = HostConfigurationFiles,
            [nameof(EfFreeBoundaryViolations)] = EfFreeBoundaryViolations
        };

    public IReadOnlyList<string> FindEfFreeBoundaryViolations() => EfFreeBoundaryViolations;
}

internal sealed record RestoreReceiptRepository(
    string? GitHead,
    string? WorktreeStatusSha256);

internal sealed record RestoreReceiptRestore(
    int DriverProtocolVersion,
    string? DriverPath,
    string? DriverSha256,
    IReadOnlyList<string>? CommandTemplate,
    string? DotnetSdkVersion);

internal sealed record RestoreReceiptDiscovery(
    int RulesVersion,
    IReadOnlyList<string>? ExcludedDirectoryNames,
    IReadOnlyList<string>? Projects,
    string? ProjectSetSha256);

internal sealed record RestoreReceiptInputEntry(string? Path, string? Sha256);

internal sealed record RestoreReceiptInputs(
    int RulesVersion,
    IReadOnlyList<RestoreReceiptInputEntry>? Entries,
    string? FingerprintSha256);

internal sealed record RestoreReceiptAssets(
    string? Path,
    string? Sha256,
    string? RestoreProjectPath);

internal sealed record RestoreReceiptProject(
    string? Path,
    string? InputFingerprintSha256,
    RestoreReceiptAssets? Assets);

internal sealed record RestoreReceiptDocument(
    int SchemaVersion,
    string? Kind,
    RestoreReceiptRepository? Repository,
    RestoreReceiptRestore? Restore,
    RestoreReceiptDiscovery? Discovery,
    RestoreReceiptInputs? Inputs,
    IReadOnlyList<RestoreReceiptProject>? Projects);

internal sealed record RestoreReceiptRepositoryState(
    string? GitHead,
    string? WorktreeStatusSha256,
    string? DotnetSdkVersion);

internal sealed record RestoreReceiptFailure(
    string Code,
    string Subject,
    string Detail,
    string Remediation);

internal sealed record RestoreReceiptValidationResult(IReadOnlyList<RestoreReceiptFailure> Failures)
{
    public bool IsValid => Failures.Count == 0;
}

internal sealed record EfCoreCertificationSnapshot(
    EfCoreSurfaceSnapshot Surface,
    RestoreReceiptValidationResult RestoreReceipt);

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
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

    public RestoreReceiptValidationResult ValidateRestoreReceipt(string receiptPath) =>
        ValidateRestoreReceipt(receiptPath, ReadRepositoryState());

    internal RestoreReceiptValidationResult ValidateRestoreReceipt(
        string receiptPath,
        RestoreReceiptRepositoryState repositoryState)
    {
        var failures = new List<RestoreReceiptFailure>();
        var resolvedReceiptPath = ResolveRepositoryPath(receiptPath);
        if (resolvedReceiptPath is null)
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MALFORMED",
                "<outside-repository>",
                "The receipt path must remain within the repository.",
                "Write the all-project restore receipt beneath artifacts/zero-ef/."));
            return ValidationResult(failures);
        }

        if (!File.Exists(resolvedReceiptPath))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MISSING",
                SafeRelativePath(resolvedReceiptPath),
                "The all-project restore receipt is missing.",
                "Run tools/architecture/restore-zero-ef-certification for this worktree."));
            return ValidationResult(failures);
        }

        RestoreReceiptDocument? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<RestoreReceiptDocument>(
                File.ReadAllText(resolvedReceiptPath),
                ReceiptJsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MALFORMED",
                SafeRelativePath(resolvedReceiptPath),
                "The receipt cannot be read as valid JSON.",
                "Regenerate the all-project restore receipt."));
            return ValidationResult(failures);
        }

        if (receipt is null)
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MALFORMED",
                SafeRelativePath(resolvedReceiptPath),
                "The receipt is empty.",
                "Regenerate the all-project restore receipt."));
            return ValidationResult(failures);
        }

        ValidateReceiptShape(receipt, resolvedReceiptPath, failures);
        if (failures.Count != 0)
            return ValidationResult(failures);

        var discovery = receipt.Discovery!;
        var inputs = receipt.Inputs!;
        var receiptProjects = discovery.Projects!;
        var receiptInputs = inputs.Entries!;

        ValidateRepositoryAndToolBinding(receipt.Repository!, receipt.Restore!, repositoryState, failures);
        ValidateDriverBinding(receipt.Restore!, failures);

        var currentProjects = DiscoverProjectPaths()
            .Select(Relative)
            .ToArray();
        var receiptProjectSet = receiptProjects.ToHashSet(StringComparer.Ordinal);
        var currentProjectSet = currentProjects.ToHashSet(StringComparer.Ordinal);
        if (!receiptProjectSet.SetEquals(currentProjectSet))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_PROJECT_SET_MISMATCH",
                "projects",
                ProjectSetDifference(receiptProjectSet, currentProjectSet),
                "Run the all-project restore driver after reconciling every repository project."));
        }

        var currentInputs = DiscoverInputUniverse()
            .Select(path => new RestoreReceiptInputEntry(Relative(path), ComputeSha256(path)))
            .ToArray();
        ValidateInputUniverse(receiptInputs, currentInputs, failures);

        var receiptProjectRecords = receipt.Projects!
            .GroupBy(project => project.Path!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        foreach (var project in currentProjects)
        {
            if (!receiptProjectRecords.TryGetValue(project, out var projectReceipt))
            {
                failures.Add(Failure(
                    "ZERO_EF_RECEIPT_UNBOUND_PROJECT",
                    project,
                    "The discovered project has no receipt binding.",
                    "Run the all-project restore driver."));
                continue;
            }

            ValidateProjectBinding(project, projectReceipt, inputs.FingerprintSha256!, failures);
        }

        foreach (var project in receiptProjectRecords.Keys.Except(currentProjectSet, StringComparer.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_UNBOUND_PROJECT",
                project,
                "The receipt binds a project that is no longer discovered.",
                "Regenerate the all-project restore receipt."));
        }

        return ValidationResult(failures);
    }

    public EfCoreCertificationSnapshot ScanForCertification(string receiptPath) =>
        new(Scan(), ValidateRestoreReceipt(receiptPath));

    internal EfCoreCertificationSnapshot ScanForCertification(
        string receiptPath,
        RestoreReceiptRepositoryState repositoryState) =>
        new(Scan(), ValidateRestoreReceipt(receiptPath, repositoryState));

    public EfCoreSurfaceSnapshot Scan()
    {
        var projects = LoadProjects();
        var inventoryProjects = projects;
        var projectsByPath = projects.ToDictionary(x => x.FullPath, PathComparer);
        var efProjects = inventoryProjects.Where(IsEfProject).ToArray();
        var efProjectPaths = projects.Where(IsEfProject).Select(x => x.FullPath).ToHashSet(PathComparer);
        var reachable = projects.ToDictionary(
            project => project.FullPath,
            project => ReachableProjects(project, projectsByPath),
            PathComparer);

        var directPackages = inventoryProjects
            .SelectMany(project => project.PackageReferences
                .Where(IsEfPackage)
                .Select(package => Pair(project.RelativePath, package)))
            .Sorted();
        var directEfReferences = inventoryProjects
            .SelectMany(project => project.ProjectReferences
                .Where(efProjectPaths.Contains)
                .Select(reference => Pair(project.RelativePath, projectsByPath[reference].RelativePath)))
            .Sorted();
        var transitiveEfProjects = inventoryProjects
            .SelectMany(project => reachable[project.FullPath]
                .Where(efProjectPaths.Contains)
                .Select(reference => Pair(project.RelativePath, projectsByPath[reference].RelativePath)))
            .Sorted();
        var transitiveEfPackages = inventoryProjects
            .SelectMany(project => reachable[project.FullPath]
                .Append(project.FullPath)
                .SelectMany(reference => projectsByPath[reference].PackageReferences)
                .Where(IsEfPackage)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(package => Pair(project.RelativePath, package)))
            .Sorted();
        var resolvedEfPackages = inventoryProjects
            .SelectMany(project => project.ResolvedPackages
                .Where(IsEfPackage)
                .Select(package => Pair(project.RelativePath, package)))
            .Sorted();
        var projectsMissingAssets = inventoryProjects
            .Where(project => !project.HasAssets)
            .Select(project => project.RelativePath)
            .Sorted();
        var sharedBuildPackages = SharedBuildEfPackageReferences();

        var boundaryProjects = inventoryProjects.Where(IsEfFreeBoundary).ToArray();
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
        var paths = DiscoverProjectPaths();

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

    private string[] DiscoverProjectPaths() =>
        Directory.EnumerateFiles(_repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .Select(Path.GetFullPath)
            .Order(PathComparer)
            .ToArray();

    private string[] DiscoverInputUniverse() =>
        Directory.EnumerateFiles(_repoRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .Where(IsDependencyInput)
            .Select(Path.GetFullPath)
            .Order(PathComparer)
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

    private void ValidateReceiptShape(
        RestoreReceiptDocument receipt,
        string receiptPath,
        ICollection<RestoreReceiptFailure> failures)
    {
        if (receipt.SchemaVersion != 1 ||
            !string.Equals(receipt.Kind, "elsa-zero-ef-all-project-restore", StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_SCHEMA_UNSUPPORTED",
                SafeRelativePath(receiptPath),
                $"Expected schemaVersion 1 and kind 'elsa-zero-ef-all-project-restore', received schemaVersion {receipt.SchemaVersion}.",
                "Regenerate the receipt with the repository-owned restore driver."));
        }

        if (receipt.Repository is null ||
            receipt.Restore is null ||
            receipt.Discovery is null ||
            receipt.Inputs is null ||
            receipt.Projects is null ||
            receipt.Discovery.Projects is null ||
            receipt.Inputs.Entries is null ||
            receipt.Inputs.Entries?.Any(entry => entry is null) == true ||
            receipt.Projects.Any(project => project is null) ||
            receipt.Restore.DriverProtocolVersion != 1 ||
            string.IsNullOrWhiteSpace(receipt.Restore.DriverPath) ||
            !IsSha256(receipt.Restore.DriverSha256) ||
            receipt.Restore.CommandTemplate is null ||
            !receipt.Restore.CommandTemplate.SequenceEqual(
                ["dotnet", "restore", "<project>", "--force-evaluate", "--configfile", "NuGet.config"],
                StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(receipt.Restore.DotnetSdkVersion) ||
            receipt.Discovery.RulesVersion != 1 ||
            receipt.Inputs.RulesVersion != 1 ||
            !IsSha256(receipt.Repository?.WorktreeStatusSha256) ||
            string.IsNullOrWhiteSpace(receipt.Repository?.GitHead) ||
            !IsSha256(receipt.Discovery.ProjectSetSha256) ||
            !IsSha256(receipt.Inputs.FingerprintSha256))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MALFORMED",
                SafeRelativePath(receiptPath),
                "A required receipt field is missing or invalid.",
                "Regenerate the all-project restore receipt."));
            return;
        }

        if (!MatchesExactly(
                receipt.Discovery.ExcludedDirectoryNames,
                [".git", "bin", "obj"]))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MALFORMED",
                "discovery.excludedDirectoryNames",
                "The receipt does not use the required all-project discovery exclusions.",
                "Regenerate the receipt with the repository-owned restore driver."));
        }

        ValidateUniqueSafePaths(receipt.Discovery.Projects, "discovery.projects", failures);
        ValidateUniqueSafePaths(receipt.Inputs.Entries?.Select(entry => entry.Path), "inputs.entries", failures);
        ValidateUniqueSafePaths(receipt.Projects.Select(project => project.Path), "projects", failures);

        foreach (var entry in receipt.Inputs.Entries ?? [])
        {
            if (!IsSha256(entry.Sha256))
            {
                failures.Add(Failure(
                    "ZERO_EF_RECEIPT_MALFORMED",
                    entry.Path ?? "inputs.entries",
                    "An input hash is missing or invalid.",
                    "Regenerate the all-project restore receipt."));
            }
        }

        if (!(receipt.Inputs.Entries ?? []).Any(entry =>
                string.Equals(entry.Path, "NuGet.config", StringComparison.Ordinal)))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MALFORMED",
                "inputs.entries",
                "The canonical NuGet.config restore input is not bound by the receipt.",
                "Regenerate the all-project restore receipt."));
        }

        foreach (var project in receipt.Projects)
        {
            if (!IsSha256(project.InputFingerprintSha256) ||
                project.Assets is null ||
                !IsSafeRepositoryPath(project.Assets.Path) ||
                !IsSha256(project.Assets.Sha256) ||
                !IsSafeRepositoryPath(project.Assets.RestoreProjectPath))
            {
                failures.Add(Failure(
                    "ZERO_EF_RECEIPT_MALFORMED",
                    project.Path ?? "projects",
                    "A project binding is missing a valid input or assets identity.",
                    "Regenerate the all-project restore receipt."));
            }
        }

        var projectFingerprint = Fingerprint(receipt.Discovery.Projects ?? []);
        if (!string.Equals(projectFingerprint, receipt.Discovery.ProjectSetSha256, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MALFORMED",
                "discovery.projectSetSha256",
                "The receipt project-set fingerprint does not bind its listed projects.",
                "Regenerate the all-project restore receipt."));
        }

        var inputFingerprint = Fingerprint((receipt.Inputs.Entries ?? [])
            .Select(entry => $"{entry.Path}\t{entry.Sha256}"));
        if (!string.Equals(inputFingerprint, receipt.Inputs.FingerprintSha256, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MALFORMED",
                "inputs.fingerprintSha256",
                "The receipt input fingerprint does not bind its listed inputs.",
                "Regenerate the all-project restore receipt."));
        }
    }

    private void ValidateRepositoryAndToolBinding(
        RestoreReceiptRepository receipt,
        RestoreReceiptRestore restore,
        RestoreReceiptRepositoryState current,
        ICollection<RestoreReceiptFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(current.GitHead) ||
            string.IsNullOrWhiteSpace(current.WorktreeStatusSha256) ||
            !string.Equals(receipt.GitHead, current.GitHead, StringComparison.Ordinal) ||
            !string.Equals(receipt.WorktreeStatusSha256, current.WorktreeStatusSha256, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_WORKTREE_MISMATCH",
                "repository",
                "The current git head or worktree status differs from the receipt.",
                "Run the all-project restore driver in the current worktree."));
        }

        if (string.IsNullOrWhiteSpace(current.DotnetSdkVersion) ||
            !string.Equals(restore.DotnetSdkVersion, current.DotnetSdkVersion, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_TOOL_MISMATCH",
                "restore.dotnetSdkVersion",
                "The current dotnet SDK version differs from the receipt.",
                "Run the all-project restore driver with the current SDK."));
        }
    }

    private void ValidateDriverBinding(RestoreReceiptRestore restore, ICollection<RestoreReceiptFailure> failures)
    {
        if (!IsSafeRepositoryPath(restore.DriverPath))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_DRIVER_MISMATCH",
                restore.DriverPath ?? "restore.driverPath",
                "The restore driver path is not a repository-relative path.",
                "Regenerate the receipt with the repository-owned restore driver."));
            return;
        }

        var relativeDriverPath = restore.DriverPath!;
        var driverPath = Path.Combine(_repoRoot, relativeDriverPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(driverPath) || !string.Equals(ComputeSha256(driverPath), restore.DriverSha256, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_DRIVER_MISMATCH",
                relativeDriverPath,
                "The recorded restore driver is missing or has changed.",
                "Regenerate the receipt with the current repository-owned restore driver."));
        }
    }

    private void ValidateInputUniverse(
        IReadOnlyList<RestoreReceiptInputEntry> receiptInputs,
        IReadOnlyList<RestoreReceiptInputEntry> currentInputs,
        ICollection<RestoreReceiptFailure> failures)
    {
        var receiptByPath = receiptInputs.ToDictionary(entry => entry.Path!, StringComparer.Ordinal);
        var currentByPath = currentInputs.ToDictionary(entry => entry.Path!, StringComparer.Ordinal);
        var receiptPaths = receiptByPath.Keys.ToHashSet(StringComparer.Ordinal);
        var currentPaths = currentByPath.Keys.ToHashSet(StringComparer.Ordinal);
        if (!receiptPaths.SetEquals(currentPaths))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_INPUT_UNIVERSE_MISMATCH",
                "inputs",
                ProjectSetDifference(receiptPaths, currentPaths),
                "Run the all-project restore driver after reconciling dependency inputs."));
        }

        foreach (var path in receiptPaths.Intersect(currentPaths, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!string.Equals(receiptByPath[path].Sha256, currentByPath[path].Sha256, StringComparison.Ordinal))
            {
                failures.Add(Failure(
                    "ZERO_EF_RECEIPT_INPUT_HASH_MISMATCH",
                    path,
                    "The dependency input content hash differs from the receipt.",
                    "Run the all-project restore driver."));
            }
        }
    }

    private void ValidateProjectBinding(
        string project,
        RestoreReceiptProject receipt,
        string expectedInputFingerprint,
        ICollection<RestoreReceiptFailure> failures)
    {
        if (!string.Equals(receipt.InputFingerprintSha256, expectedInputFingerprint, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_INPUT_HASH_MISMATCH",
                project,
                "The project does not bind the receipt input-universe fingerprint.",
                "Run the all-project restore driver."));
        }

        var projectDirectory = Path.GetDirectoryName(project)?.Replace(Path.DirectorySeparatorChar, '/');
        var expectedAssetsPath = string.IsNullOrEmpty(projectDirectory)
            ? "obj/project.assets.json"
            : $"{projectDirectory}/obj/project.assets.json";
        var assets = receipt.Assets!;
        if (!string.Equals(assets.Path, expectedAssetsPath, StringComparison.Ordinal) ||
            !string.Equals(assets.RestoreProjectPath, project, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_ASSETS_PROJECT_IDENTITY_MISMATCH",
                project,
                "The receipt assets binding does not belong to the project.",
                "Run the all-project restore driver."));
            return;
        }

        var assetsPath = Path.Combine(_repoRoot, assets.Path!.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(assetsPath))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_ASSETS_MISSING",
                assets.Path,
                "The project.assets.json file is missing.",
                "Run the all-project restore driver."));
            return;
        }

        if (!string.Equals(ComputeSha256(assetsPath), assets.Sha256, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_ASSETS_HASH_MISMATCH",
                assets.Path,
                "The project.assets.json content hash differs from the receipt.",
                "Run the all-project restore driver."));
            return;
        }

        var assetsMetadata = ReadAssetsRestoreMetadata(assetsPath);
        if (assetsMetadata is null ||
            assetsMetadata.ConfigFilePaths.Count != 1 ||
            !string.Equals(assetsMetadata.ConfigFilePaths[0], "NuGet.config", StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_ASSETS_CONFIG_MISMATCH",
                assets.Path,
                "The assets restore configFilePaths does not bind exactly the repository NuGet.config.",
                "Run the all-project restore driver with the canonical NuGet.config."));
        }

        if (!string.Equals(assetsMetadata?.RestoreProjectPath, project, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_ASSETS_PROJECT_IDENTITY_MISMATCH",
                assets.Path,
                "The assets restore-project identity does not match the bound project.",
                "Run the all-project restore driver."));
        }
    }

    private AssetsRestoreMetadata? ReadAssetsRestoreMetadata(string assetsPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            if (!document.RootElement.TryGetProperty("project", out var project) ||
                !project.TryGetProperty("restore", out var restore))
                return null;
            var identity = restore.TryGetProperty("projectUniqueName", out var uniqueName) &&
                           uniqueName.ValueKind == JsonValueKind.String
                ? uniqueName.GetString()
                : restore.TryGetProperty("projectPath", out var projectPath) &&
                  projectPath.ValueKind == JsonValueKind.String
                    ? projectPath.GetString()
                    : null;
            if (!restore.TryGetProperty("configFilePaths", out var configPaths) ||
                configPaths.ValueKind != JsonValueKind.Array)
                return null;
            var configPathElements = configPaths.EnumerateArray().ToArray();
            if (configPathElements.Any(path =>
                    path.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(path.GetString())))
                return null;
            var configFilePaths = configPathElements
                .Select(path => path.GetString()!)
                .Select(path => ToSafeRepositoryPath(path, Path.GetDirectoryName(assetsPath)))
                .ToArray();
            if (configFilePaths.Any(path => path is null))
                return null;
            return new(
                ToSafeRepositoryPath(identity, Path.GetDirectoryName(assetsPath)),
                configFilePaths.Select(path => path!).ToArray());
        }
        catch (Exception exception) when (exception is
               JsonException or
               InvalidOperationException or
               IOException or
               UnauthorizedAccessException or
               ArgumentException or
               NotSupportedException or
               PathTooLongException)
        {
            return null;
        }
    }

    private sealed record AssetsRestoreMetadata(
        string? RestoreProjectPath,
        IReadOnlyList<string> ConfigFilePaths);

    private RestoreReceiptRepositoryState ReadRepositoryState()
    {
        var head = TryRunCommand("git", "rev-parse", "HEAD");
        var status = TryRunCommandBytes("git", "status", "--porcelain=v1", "-z", "--untracked-files=all");
        var dotnetSdkVersion = TryRunCommand("dotnet", "--version");
        return new(
            head is null ? null : head.Trim(),
            status is null ? null : ComputeSha256(status),
            dotnetSdkVersion is null ? null : dotnetSdkVersion.Trim());
    }

    private string? TryRunCommand(string fileName, params string[] arguments)
    {
        var output = TryRunCommandBytes(fileName, arguments);
        return output is null ? null : Encoding.UTF8.GetString(output);
    }

    private byte[]? TryRunCommandBytes(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;
            using var output = new MemoryStream();
            var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output);
            var errorTask = process.StandardError.ReadToEndAsync();
            Task.WaitAll(outputTask, errorTask);
            process.WaitForExit();
            return process.ExitCode == 0 ? output.ToArray() : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static RestoreReceiptFailure Failure(string code, string subject, string detail, string remediation) =>
        new(code, subject, detail, remediation);

    private static RestoreReceiptValidationResult ValidationResult(IEnumerable<RestoreReceiptFailure> failures) =>
        new(failures
            .OrderBy(failure => failure.Code, StringComparer.Ordinal)
            .ThenBy(failure => failure.Subject, StringComparer.Ordinal)
            .ThenBy(failure => failure.Detail, StringComparer.Ordinal)
            .ToArray());

    private static bool MatchesExactly(IReadOnlyList<string>? actual, IReadOnlyList<string> expected) =>
        actual is not null && actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static void ValidateUniqueSafePaths(
        IEnumerable<string?>? paths,
        string subject,
        ICollection<RestoreReceiptFailure> failures)
    {
        var values = paths?.ToArray() ?? [];
        if (values.Any(path => !IsSafeRepositoryPath(path)) ||
            values.OfType<string>().Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            failures.Add(Failure(
                "ZERO_EF_RECEIPT_MALFORMED",
                subject,
                "Receipt paths must be unique, repository-relative, and normalized with '/'.",
                "Regenerate the all-project restore receipt."));
        }
    }

    private static bool IsSafeRepositoryPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        !path.Contains('\\') &&
        !path.Contains(':') &&
        path.Split('/').All(segment => !string.IsNullOrEmpty(segment) && segment is not "." and not "..");

    private string? ToSafeRepositoryPath(string? path, string? relativeDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(relativeDirectory ?? _repoRoot, path));
        if (!IsWithinRepository(fullPath))
            return null;
        var relative = Relative(fullPath);
        return IsSafeRepositoryPath(relative) ? relative : null;
    }

    private string SafeRelativePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return IsWithinRepository(fullPath) ? Relative(fullPath) : "<outside-repository>";
    }

    private string? ResolveRepositoryPath(string path)
    {
        try
        {
            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(_repoRoot, path));
            return IsWithinRepository(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' || character is >= 'a' and <= 'f');

    private static string ComputeSha256(string path) => ComputeSha256(File.ReadAllBytes(path));

    private static string ComputeSha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string Fingerprint(IEnumerable<string> entries) =>
        ComputeSha256(Encoding.UTF8.GetBytes(string.Join("\n", entries.Order(StringComparer.Ordinal)) + "\n"));

    private static string ProjectSetDifference(IReadOnlySet<string> receipt, IReadOnlySet<string> current)
    {
        var missingFromReceipt = current.Except(receipt, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var receiptOnly = receipt.Except(current, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return $"missing-from-receipt=[{string.Join(", ", missingFromReceipt)}]; receipt-only=[{string.Join(", ", receiptOnly)}]";
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
        project.RelativePath.Contains("/Persistence/Groundwork/", StringComparison.OrdinalIgnoreCase) ||
        IsDesignLaneSourceProject(project);

    /// <summary>
    /// Spec 093 T075: the workflow and activity design lanes ship only Groundwork persistence, so every
    /// design source project (including the provider-neutral <c>.Api</c>, <c>.Validations</c>,
    /// <c>.JavaScript</c> and reconciliation projects, not just <c>.Core</c>/<c>.Groundwork</c>) must stay
    /// EF-free, direct and transitive. Scoped to <c>src/</c> so design test projects that keep the
    /// preserved base-EF lane are not swept in.
    /// </summary>
    private static bool IsDesignLaneSourceProject(ProjectInfo project) =>
        project.RelativePath.StartsWith("src/", StringComparison.OrdinalIgnoreCase) &&
        (project.RelativePath.Contains("/Workflows/Design/", StringComparison.OrdinalIgnoreCase) ||
         project.RelativePath.Contains("/Activities/Design/", StringComparison.OrdinalIgnoreCase));

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
