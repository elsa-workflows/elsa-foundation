using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Server.ExtensionBuilder;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ExtensionTemplateKind
{
    ElsaActivityModule,
    GenericDotNet
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ProjectFileKind
{
    Source,
    Project,
    Manifest,
    Configuration,
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum RepositoryFileKind
{
    Solution,
    Project,
    Folder,
    File
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ExtensionTemplateScope
{
    Repository,
    Solution,
    Project,
    Item
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum RemoteSyncOperation
{
    Push,
    Pull
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum RemoteSyncState
{
    Completed,
    Blocked
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum BuildStatus
{
    Pending,
    Running,
    Succeeded,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum BuildDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum PromotionStatus
{
    Accepted,
    Rejected
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum PromotionRejectionReason
{
    Duplicate,
    InvalidManifest,
    DependencyPolicy,
    MalformedPackage
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ExtensionPackageRuntimeState
{
    Loaded,
    PendingRestart,
    FailedReconciliation
}

internal sealed record ExtensionRepositorySummary(
    string Id,
    string Name,
    string OwnerId,
    string? ActiveBranch,
    bool IsDirty,
    string RemoteState,
    BuildStatus? LatestBuildStatus,
    int AttentionCount,
    int ProjectCount,
    DateTimeOffset UpdatedAt);

internal sealed record ExtensionBuilderCaller(
    string OwnerId,
    string DisplayName,
    bool HasManagementAccess,
    bool IsTrusted);

internal sealed record ExtensionBuilderCapabilities(
    bool CanCreateWorkspace,
    bool CanEditFiles,
    bool CanBuild,
    bool CanPromote,
    bool CanRollback)
{
    public static ExtensionBuilderCapabilities FromTrust(bool trusted) =>
        new(trusted, trusted, trusted, trusted, trusted);
}

internal sealed record CreateWorkspaceRequest(string DisplayName);

internal sealed record SelectWorkingCopyRequest(
    string SessionId,
    string? BranchName,
    bool AllowProtectedBranchEdit);

internal sealed record ExtensionWorkingCopySummary(
    string Id,
    string WorkspaceId,
    string OwnerId,
    string SessionId,
    string BranchName,
    bool IsActive,
    bool IsProtectedBranch,
    bool IsDirty,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record CreateProjectRequest(
    string TemplateId,
    string PackageId,
    string? PackageVersion,
    string? TargetFramework,
    string? DisplayName,
    string? Description);

internal sealed record WriteProjectFileRequest(string Content);

internal sealed record MoveRepositoryFileRequest(
    string SourcePath,
    string DestinationPath);

internal sealed record ApplyRepositoryTemplateRequest(
    string TemplateId,
    ExtensionTemplateScope? Scope,
    string? TargetPath,
    IReadOnlyDictionary<string, string>? Parameters);

internal sealed record RollbackPackageRequest(string Version);

internal sealed record ExtensionWorkspace(
    string Id,
    string OwnerId,
    string TrustContext,
    string DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> ProjectIds);

internal sealed record ExtensionProject(
    string Id,
    string WorkspaceId,
    string TemplateId,
    ExtensionTemplateKind TemplateKind,
    string PackageId,
    string PackageVersion,
    string TargetFramework,
    ExtensionProjectManifest Manifest,
    string CurrentSourceRevisionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record ExtensionProjectManifest(
    string DisplayName,
    string? Description,
    IReadOnlyList<string> Categories,
    JsonElement Content);

internal sealed record ProjectFile(
    string Path,
    string Content,
    ProjectFileKind Kind,
    long Size,
    DateTimeOffset UpdatedAt);

internal sealed record ProjectFileSummary(
    string Path,
    ProjectFileKind Kind,
    long Size,
    DateTimeOffset UpdatedAt);

internal sealed record RepositorySolutionSummary(
    string Path,
    string Name,
    bool IsSelected);

internal sealed record RepositoryFileSummary(
    string Path,
    RepositoryFileKind Kind,
    long Size,
    bool IsDirty,
    DateTimeOffset UpdatedAt);

internal sealed record RepositoryTree(
    string WorkspaceId,
    string? ActiveBranch,
    bool IsDirty,
    IReadOnlyList<RepositorySolutionSummary> Solutions,
    IReadOnlyList<RepositoryFileSummary> Entries);

internal sealed record RepositoryFile(
    string Path,
    string Content,
    RepositoryFileKind Kind,
    long Size,
    bool IsDirty,
    DateTimeOffset UpdatedAt);

internal sealed record SourceControlFileStatus(
    string Path,
    string Status,
    bool IsStaged,
    bool IsUnstaged);

internal sealed record SourceControlStatus(
    string WorkspaceId,
    string? ActiveBranch,
    bool IsDirty,
    IReadOnlyList<SourceControlFileStatus> ChangedFiles,
    IReadOnlyList<SourceControlFileStatus> StagedFiles,
    IReadOnlyList<SourceControlFileStatus> UnstagedFiles);

internal sealed record SourceControlDiff(
    string Path,
    bool IsStaged,
    string Patch);

internal sealed record SourceControlPathRequest(string Path);

internal sealed record SourceControlCommitRequest(string Message);

internal sealed record SourceControlCommitResult(
    string CommitId,
    string Message,
    SourceControlStatus Status);

internal sealed record RemoteSyncResult(
    RemoteSyncOperation Operation,
    RemoteSyncState State,
    string Message,
    string? Remote,
    string? Branch,
    int Ahead,
    int Behind,
    SourceControlStatus Status);

internal sealed record AppliedRepositoryTemplate(
    string TemplateId,
    ExtensionTemplateScope Scope,
    IReadOnlyList<RepositoryFileSummary> Files,
    RepositoryTree Tree);

internal sealed record ExtensionTemplateParameter(
    string Name,
    string DisplayName,
    bool Required,
    string? DefaultValue);

internal sealed record ExtensionTemplate(
    string Id,
    ExtensionTemplateKind Kind,
    string DisplayName,
    string Description,
    ExtensionTemplateScope Scope,
    IReadOnlyList<string> CompatibleFileExtensions,
    IReadOnlyList<ExtensionTemplateParameter> Parameters,
    string DefaultPackageId,
    string DefaultPackageVersion,
    string DefaultTargetFramework,
    ExtensionProjectManifest DefaultManifest,
    IReadOnlyList<ProjectTemplateFile> Files);

internal sealed record ProjectTemplateFile(string Path, string Content, ProjectFileKind Kind);

internal sealed record BuildRequest(
    string ProjectId,
    string SourceRevisionId,
    DateTimeOffset SubmittedAt);

internal sealed record BuildResult(
    string Id,
    string ProjectId,
    string WorkspaceId,
    string SourceRevisionId,
    BuildStatus Status,
    IReadOnlyList<BuildDiagnostic> Diagnostics,
    BuildArtifact? Artifact,
    string LogPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

internal sealed record BuildDiagnostic(
    BuildDiagnosticSeverity Severity,
    string Message,
    string? File,
    int? Line,
    int? Column,
    string? Code);

internal sealed record BuildArtifact(
    string Id,
    string BuildId,
    string PackageId,
    string Version,
    string FileName,
    string Path,
    long Size,
    DateTimeOffset CreatedAt);

internal sealed record PackagePromotionRequest(string? TargetFeed);

internal sealed record PackagePromotionResult(
    PromotionStatus Status,
    PromotionRejectionReason? RejectionReason,
    PublishedPackage? PublishedPackage,
    ExtensionBuilderReconcileOutcome? ReconcileOutcome,
    bool RequiresReload,
    bool RequiresRestart);

internal sealed record PublishedPackage(
    string PackageId,
    string Version,
    string FeedName,
    string Path);

internal sealed record ExtensionBuilderReconcileOutcome(
    string Outcome,
    string CorrelationId,
    string? Reason,
    bool IsDegraded,
    IReadOnlyList<string> FailedPackages);

internal sealed record ExtensionRuntimeStatus(
    string ProjectId,
    string PackageId,
    string? ActiveVersion,
    IReadOnlyList<ExtensionRuntimePackageStatus> Packages,
    IReadOnlyList<string> AvailableRollbackVersions,
    ExtensionBuilderReconcileOutcome? LastReconcileOutcome,
    string? LastReconcileReason);

internal sealed record ExtensionRuntimePackageStatus(
    string PackageId,
    string Version,
    ExtensionPackageRuntimeState State,
    IReadOnlyList<ExtensionRuntimeContribution> Contributions,
    bool RequiresReload,
    bool RequiresRestart,
    string? Reason);

internal sealed record ExtensionRuntimeContribution(
    string Type,
    string Id,
    string Label,
    string Status);

internal sealed record ExtensionBuilderErrorResponse(string Message);

internal sealed record ExtensionBuilderDeleteResponse(bool Deleted, string Message);

internal sealed record ExtensionBuilderOperationResponse(
    string Operation,
    ExtensionBuilderReconcileOutcome ReconcileOutcome,
    bool RequiresReload,
    bool RequiresRestart,
    string Message);

internal sealed record PackagePromotionRecord(
    string PackageId,
    string Version,
    string ArtifactPath,
    string FeedPath,
    DateTimeOffset PromotedAt,
    ExtensionBuilderReconcileOutcome ReconcileOutcome,
    bool RequiresReload,
    bool RequiresRestart,
    DateTimeOffset? LastReconciledAt = null);
