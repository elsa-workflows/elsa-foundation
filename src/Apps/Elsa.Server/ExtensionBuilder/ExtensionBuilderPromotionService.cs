using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Nuplane.Admin;
using Nuplane.Reconciliation;

namespace Elsa.Server.ExtensionBuilder;

internal interface IExtensionBuilderPromotionService
{
    Task<PackagePromotionResult> PromoteAsync(BuildResult build, PackagePromotionRequest request, CancellationToken cancellationToken = default);
    Task<PackagePromotionResult> RollbackAsync(ExtensionProject project, PackagePromotionRecord target, IReadOnlyList<PackagePromotionRecord> promotions, CancellationToken cancellationToken = default);
    Task<ExtensionBuilderReconcileOutcome> RetryReconciliationAsync(CancellationToken cancellationToken = default);
}

internal sealed partial class ExtensionBuilderPromotionService(
    IWebHostEnvironment environment,
    IOptions<ExtensionBuilderOptions> options,
    INuplaneAdminOperations nuplaneAdmin,
    IConfiguration configuration) : IExtensionBuilderPromotionService
{
    public ExtensionBuilderPromotionService(
        IWebHostEnvironment environment,
        IOptions<ExtensionBuilderOptions> options,
        INuplaneAdminOperations nuplaneAdmin)
        : this(environment, options, nuplaneAdmin, new ConfigurationBuilder().Build())
    {
    }

    public Task<PackagePromotionResult> PromoteAsync(BuildResult build, CancellationToken cancellationToken = default) =>
        PromoteAsync(build, PackagePromotionRequest.Default, cancellationToken);

    public async Task<PackagePromotionResult> PromoteAsync(BuildResult build, PackagePromotionRequest request, CancellationToken cancellationToken = default)
    {
        if (build.Status is not BuildStatus.Succeeded || build.Artifact is null)
            return Rejected(PromotionRejectionReason.InvalidManifest);

        var validation = await ValidatePackageAsync(build.Artifact.Path, cancellationToken);
        if (validation.RejectionReason is not null)
            return Rejected(validation.RejectionReason.Value);
        if (!string.Equals(validation.PackageId, build.Artifact.PackageId, StringComparison.Ordinal) ||
            !string.Equals(validation.Version, build.Artifact.Version, StringComparison.Ordinal))
            return Rejected(PromotionRejectionReason.InvalidManifest);

        var feed = ResolveDropFolderFeed(request.TargetFeed);
        if (feed is null)
            return Rejected(PromotionRejectionReason.InvalidManifest);
        Directory.CreateDirectory(feed.Path);
        var destination = ResolveFeedPackagePath(feed.Path, build.Artifact.FileName);
        if (File.Exists(destination) ||
            FeedPackageExists(feed.Path, validation.PackageId!, validation.Version!) ||
            await ActivePackageExistsAsync(validation.PackageId!, validation.Version!, cancellationToken))
            return Rejected(PromotionRejectionReason.Duplicate);

        if (TryCopyPackageToFeed(build.Artifact.Path, destination) is { } copyRejection)
            return Rejected(copyRejection);

        return new(
            PromotionStatus.Accepted,
            null,
            new(validation.PackageId!, validation.Version!, feed.Name, destination),
            null,
            RequiresReload: true,
            RequiresRestart: false);
    }

    public async Task<PackagePromotionResult> RollbackAsync(ExtensionProject project, PackagePromotionRecord target, IReadOnlyList<PackagePromotionRecord> promotions, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(target.FeedPath))
            return Rejected(PromotionRejectionReason.InvalidManifest);

        var source = target.FeedPath;
        var feed = ResolveDropFolderFeedForPackagePath(source);
        Directory.CreateDirectory(feed.Path);
        var destination = ResolveFeedPackagePath(feed.Path, Path.GetFileName(source));
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase) &&
            TryCopyPackageToFeed(source, destination, overwrite: true) is { } copyRejection)
            return Rejected(copyRejection);
        if (TryDeactivateSupersededPackageVersions(feed.Path, target.PackageId, target.Version, promotions) is { } deactivateRejection)
            return Rejected(deactivateRejection);
        var reconcile = await TryTriggerRollbackReconcileAsync(target.PackageId);

        return new(
            PromotionStatus.Accepted,
            null,
            new(target.PackageId, target.Version, feed.Name, destination),
            reconcile,
            RequiresReload: true,
            RequiresRestart: false);
    }

    private async Task<ExtensionBuilderReconcileOutcome> TryTriggerRollbackReconcileAsync(string packageId)
    {
        try
        {
            return MapReconcileOutcome(await nuplaneAdmin.TriggerReconcileAsync(CancellationToken.None));
        }
        catch
        {
            return new("failed", "", "Rollback reconciliation failed.", true, [packageId]);
        }
    }

    public async Task<ExtensionBuilderReconcileOutcome> RetryReconciliationAsync(CancellationToken cancellationToken = default) =>
        MapReconcileOutcome(await nuplaneAdmin.TriggerReconcileAsync(cancellationToken));

    internal async Task<PromotionValidationResult> ValidatePackageAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath) || !packagePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            return new(PromotionRejectionReason.MalformedPackage);

        try
        {
            await using var stream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var nuspec = archive.Entries.FirstOrDefault(x => x.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspec is null)
                return new(PromotionRejectionReason.InvalidManifest);

            var identity = ReadNuspecIdentity(nuspec);
            if (identity is null || !IsWellFormedPackageId(identity.Value.PackageId) || !IsWellFormedPackageVersion(identity.Value.Version))
                return new(PromotionRejectionReason.InvalidManifest);

            var dependencies = ReadNuspecDependencies(nuspec);
            if (IsDependencyDenied(dependencies))
                return new(PromotionRejectionReason.DependencyPolicy);

            var elsaManifest = archive.Entries.FirstOrDefault(x => NormalizeArchivePath(x.FullName).Equals("elsa-package.json", StringComparison.OrdinalIgnoreCase));
            if (elsaManifest is null)
                return new(PromotionRejectionReason.InvalidManifest);

            await using var manifestStream = elsaManifest.Open();
            using var manifestDocument = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
            if (manifestDocument.RootElement.ValueKind is not JsonValueKind.Object)
                return new(PromotionRejectionReason.InvalidManifest);
            if (!ManifestMatchesPackageIdentity(manifestDocument.RootElement, identity.Value.PackageId, identity.Value.Version))
                return new(PromotionRejectionReason.InvalidManifest);

            return new(null, identity.Value.PackageId, identity.Value.Version);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return new(PromotionRejectionReason.MalformedPackage);
        }
    }

    private async Task<bool> ActivePackageExistsAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        return packages.Packages.Any(x =>
            string.Equals(x.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Version, version, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsDependencyDenied(IReadOnlyList<string> dependencies)
    {
        foreach (var dependency in dependencies)
        {
            foreach (var pattern in options.Value.DeniedDependencyPatterns.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (MatchesPattern(dependency, pattern))
                    return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ReadNuspecDependencies(ZipArchiveEntry nuspec)
    {
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        return document
            .Descendants(ns + "dependency")
            .Select(x => (string?)x.Attribute("id"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();
    }

    private static (string PackageId, string Version)? ReadNuspecIdentity(ZipArchiveEntry nuspec)
    {
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = document.Root?.Element(ns + "metadata");
        var packageId = metadata?.Element(ns + "id")?.Value.Trim();
        var version = metadata?.Element(ns + "version")?.Value.Trim();
        return string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version)
            ? null
            : (packageId, version);
    }

    private static bool ManifestMatchesPackageIdentity(JsonElement manifest, string packageId, string version) =>
        manifest.TryGetProperty("package", out var package) &&
        package.ValueKind is JsonValueKind.Object &&
        package.TryGetProperty("id", out var manifestPackageId) &&
        package.TryGetProperty("version", out var manifestVersion) &&
        manifestPackageId.ValueKind is JsonValueKind.String &&
        manifestVersion.ValueKind is JsonValueKind.String &&
        string.Equals(manifestPackageId.GetString(), packageId, StringComparison.Ordinal) &&
        string.Equals(manifestVersion.GetString(), version, StringComparison.Ordinal);

    private static bool FeedPackageExists(string feedPath, string packageId, string version)
    {
        if (!Directory.Exists(feedPath))
            return false;

        foreach (var packagePath in Directory.EnumerateFiles(feedPath, "*.nupkg", SearchOption.TopDirectoryOnly))
        {
            var identity = TryReadPackageIdentity(packagePath);
            if (identity is not null &&
                string.Equals(identity.Value.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(identity.Value.Version, version, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static PromotionRejectionReason? TryDeactivateSupersededPackageVersions(string feedPath, string packageId, string activeVersion, IReadOnlyList<PackagePromotionRecord> promotions)
    {
        var normalizedFeedPath = Path.GetFullPath(feedPath);
        var promotedPaths = promotions
            .Where(x =>
                string.Equals(x.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x.Version, activeVersion, StringComparison.OrdinalIgnoreCase) &&
                IsPackageInFeed(x.FeedPath, normalizedFeedPath))
            .Select(x => x.FeedPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(feedPath))
        {
            foreach (var packagePath in Directory.EnumerateFiles(feedPath, "*.nupkg", SearchOption.TopDirectoryOnly))
            {
                var identity = TryReadPackageIdentity(packagePath);
                if (identity is not null &&
                    string.Equals(identity.Value.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(identity.Value.Version, activeVersion, StringComparison.OrdinalIgnoreCase))
                    promotedPaths.Add(packagePath);
            }
        }

        foreach (var path in promotedPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return PromotionRejectionReason.MalformedPackage;
                }
            }
        }

        return null;
    }

    private static (string PackageId, string Version)? TryReadPackageIdentity(string packagePath)
    {
        try
        {
            using var stream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var nuspec = archive.Entries.FirstOrDefault(x => x.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            return nuspec is null ? null : ReadNuspecIdentity(nuspec);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private DropFolderFeed? ResolveDropFolderFeed(string? targetFeed = null)
    {
        var feeds = ReadFeeds(configuration);
        var feed = string.IsNullOrWhiteSpace(targetFeed)
            ? feeds.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Path))
            : feeds.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.Path) &&
                string.Equals(x.Name, targetFeed, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(targetFeed) && feed is null)
            return null;
        var path = feed?.Path ?? "packages";
        return new(feed?.Name ?? "drop-folder", ResolveFeedRootPath(path));
    }

    private DropFolderFeed ResolveDropFolderFeedForPackagePath(string packagePath)
    {
        var packageDirectory = Path.GetFullPath(Path.GetDirectoryName(packagePath) ?? environment.ContentRootPath);
        var feed = ReadFeeds(configuration)
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .FirstOrDefault(x => string.Equals(ResolveFeedRootPath(x.Path), packageDirectory, StringComparison.OrdinalIgnoreCase));
        return new(feed?.Name ?? "drop-folder", packageDirectory);
    }

    private static IReadOnlyList<DropFolderFeed> ReadFeeds(IConfiguration configuration) =>
        configuration.GetSection("Nuplane:Setup:Feeds")
            .GetChildren()
            .Select(feed => new DropFolderFeed(feed["Name"] ?? "", feed["DirectoryPath"] ?? ""))
            .Where(feed => !string.IsNullOrWhiteSpace(feed.Name) || !string.IsNullOrWhiteSpace(feed.Path))
            .ToArray();

    internal static ExtensionBuilderReconcileOutcome MapReconcileOutcome(ManualReconcileOutcome outcome) =>
        new(
            outcome.OutcomeCode.ToString().ToLowerInvariant(),
            outcome.CorrelationId,
            outcome.ReasonCode,
            outcome.RunResult?.IsDegraded ?? false,
            outcome.RunResult?.FailedPackages ?? []);

    private static PackagePromotionResult Rejected(PromotionRejectionReason reason) =>
        new(PromotionStatus.Rejected, reason, null, null, RequiresReload: false, RequiresRestart: false);

    internal static PromotionRejectionReason? TryCopyPackageToFeed(string source, string destination, bool overwrite = false)
    {
        try
        {
            File.Copy(source, destination, overwrite);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return !overwrite && File.Exists(destination) ? PromotionRejectionReason.Duplicate : PromotionRejectionReason.MalformedPackage;
        }
    }

    private string ResolveFeedRootPath(string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));

    private static bool IsPackageInFeed(string packagePath, string feedPath)
    {
        var packageDirectory = Path.GetDirectoryName(packagePath);
        return packageDirectory is not null &&
            string.Equals(Path.GetFullPath(packageDirectory), feedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFeedPackagePath(string feedRoot, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        var root = Path.GetFullPath(feedRoot);
        var path = Path.GetFullPath(Path.Combine(root, safeName));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resolved feed package path is outside the feed root.");
        return path;
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (pattern == "*")
            return true;
        if (pattern.StartsWith('*') && pattern.EndsWith('*') && value.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase))
            return true;
        if (pattern.StartsWith('*') && value.EndsWith(pattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase))
            return true;
        if (pattern.EndsWith('*') && value.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeArchivePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool IsWellFormedPackageId(string packageId) =>
        PackageIdRegex().IsMatch(packageId) &&
        !packageId.StartsWith(".", StringComparison.Ordinal) &&
        !packageId.EndsWith(".", StringComparison.Ordinal) &&
        !packageId.Contains("..", StringComparison.Ordinal);

    private static bool IsWellFormedPackageVersion(string version) =>
        PackageVersionRegex().IsMatch(version);

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex PackageIdRegex();

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+){1,3}(-[0-9A-Za-z][0-9A-Za-z.-]*)?(\+[0-9A-Za-z][0-9A-Za-z.-]*)?$")]
    private static partial Regex PackageVersionRegex();

    internal sealed record PromotionValidationResult(PromotionRejectionReason? RejectionReason, string? PackageId = null, string? Version = null);
    private sealed record DropFolderFeed(string Name, string Path);
}
