using System.Text.Json.Nodes;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Models;
using global::Nuplane.Admin;
using global::Nuplane.Reconciliation;

namespace Elsa.Studio.Hosting.Nuplane;

internal sealed record ModuleManagementRegistryResponse(
    ModuleManagementHost Host,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ModuleManagementModule> Modules,
    IReadOnlyList<ModuleManagementPackage> Packages,
    IReadOnlyList<ModuleManagementDropFolderPackage> DropFolderPackages,
    IReadOnlyList<ModuleManagementFeed> Feeds,
    ModuleManagementRetentionPolicy RetentionPolicy,
    ModuleManagementCapabilities Capabilities,
    IReadOnlyList<ModuleManagementDiagnostic> Diagnostics);

internal sealed record ModuleManagementHost(string Id, string DisplayName, string Runtime, string ContentRootPath);

internal sealed record ModuleManagementModule(
    string Id,
    string DisplayName,
    string Surface,
    string Runtime,
    string SourceKind,
    string Scope,
    string Version,
    string Status,
    string Compatibility,
    string? PackageId,
    string? PackageVersion,
    IReadOnlyList<StudioModuleContributionSummary> Contributions,
    IReadOnlyList<ModuleManagementDiagnostic> Diagnostics,
    ModuleManagementStudioManifest Manifest)
{
    public static ModuleManagementModule FromStudioModule(StudioModuleRegistryItem module, string runtime) =>
        new(
            module.Id,
            module.DisplayName,
            "Studio",
            runtime,
            module.SourceKind,
            module.Scope,
            module.Version,
            module.Status,
            module.Compatibility,
            null,
            null,
            module.Contributions.ToArray(),
            module.Diagnostics.Select(x => new ModuleManagementDiagnostic(x.ModuleId, x.Status, x.Reason)).ToArray(),
            new(module.Manifest.Entry, module.Manifest.Styles.ToArray(), module.Manifest.Capabilities.ToArray()));
}

internal sealed record ModuleManagementStudioManifest(
    string Entry,
    IReadOnlyList<string> Styles,
    IReadOnlyList<string> Capabilities);

internal sealed record ModuleManagementPackage(
    string Id, string Version, string FeedName, string SourceName, string InstallPath,
    ModuleManagementPackageManifest? Manifest, IReadOnlyList<ModuleManagementPackageDependency> Dependencies);

internal sealed record ModuleManagementPackageManifest(string Kind, string Path, JsonNode? Content, string? Text);
internal sealed record ModuleManagementPackageDependency(string Id, string VersionRange, string? Group, string Source);

internal sealed record ModuleManagementDropFolderPackage(string FileName, string Path, long Size, DateTimeOffset LastWriteTimeUtc);

internal sealed record ModuleManagementFeed(
    string Name,
    string? ServiceIndex,
    string? DirectoryPath,
    string? Credentials,
    bool IncludeAll,
    IReadOnlyList<string> IncludePatterns,
    ModuleManagementDirectoryFeedSettings Directory);

internal sealed record ModuleManagementDirectoryFeedSettings(bool Watch, string DebounceWindow);

internal sealed record ModuleManagementRetentionPolicy(int? KeepNewestInactiveVersionsPerPackage, string Status);

internal sealed record ModuleManagementCapabilities(
    bool CanUploadPackages,
    bool CanManageFeeds,
    bool CanReconcile,
    bool CanPrunePackages,
    bool FeedChangesRequireRestart,
    bool CanManageRetentionPolicy);

internal sealed record ModuleManagementDiagnostic(string Source, string Status, string Reason);

internal sealed record ModuleManagementErrorResponse(string Error);

internal sealed record ModuleManagementUploadResponse(
    string FileName,
    string Path,
    long Size,
    ModuleManagementReconcileResponse Reconcile,
    bool RequiresReload,
    bool RequiresRestart);

internal sealed record ModuleManagementDeleteResponse(
    string FileName,
    string Path,
    ModuleManagementReconcileResponse Reconcile,
    bool RequiresReload,
    bool RequiresRestart);

internal sealed record ModuleManagementOperationResponse(
    string Operation,
    ModuleManagementReconcileResponse Reconcile,
    bool RequiresReload,
    bool RequiresRestart,
    string Message);

internal sealed record ModuleManagementReconcileResponse(
    string Outcome,
    string CorrelationId,
    string? ReasonCode,
    bool? IsDegraded,
    IReadOnlyList<string> FailedPackages)
{
    public static ModuleManagementReconcileResponse FromOutcome(ManualReconcileOutcome outcome) =>
        new(
            outcome.OutcomeCode.ToString(),
            outcome.CorrelationId,
            outcome.ReasonCode,
            outcome.RunResult?.IsDegraded,
            outcome.RunResult?.FailedPackages ?? []);

    public static ModuleManagementReconcileResponse Deferred(string reason) =>
        new("Deferred", "", reason, null, []);
}

internal sealed record ModuleManagementConfigurationWriteResponse(bool RequiresRestart, string Message);
