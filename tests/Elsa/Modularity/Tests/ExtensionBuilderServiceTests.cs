using System.IO.Compression;
using System.Text.Json;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Server.ExtensionBuilder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Operational;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Models;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ExtensionBuilderServiceTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-extension-builder-{Guid.NewGuid():N}");
    private readonly ExtensionBuilderCaller _caller = new("owner-1", "Owner One", true, true);

    public ExtensionBuilderServiceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task WorkspacesAndProjectsArePersistedAndOwnerScoped()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("elsa-activity-module", "Elsa.Test.Activity", "1.0.0", "net10.0", null, null));

        var restarted = CreateService();
        var files = await restarted.ListProjectFilesAsync(_caller, project.Id);
        var otherOwnerProject = await restarted.GetProjectAsync(_caller with { OwnerId = "other" }, project.Id);

        Assert.NotNull(await restarted.GetWorkspaceAsync(_caller, workspace.Id));
        Assert.NotNull(await restarted.GetProjectAsync(_caller, project.Id));
        Assert.Contains(files!, x => x.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Null(otherOwnerProject);
    }

    [Fact]
    public async Task FileEditsPersistAndRejectUnsafePaths()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Generic", "1.0.0", "net10.0", null, null));

        var written = await service.WriteProjectFileAsync(_caller, project.Id, "src/NewFile.cs", new("namespace Test;"));
        var read = await service.ReadProjectFileAsync(_caller, project.Id, "src/NewFile.cs");
        var deleted = await service.DeleteProjectFileAsync(_caller, project.Id, "src/NewFile.cs");

        Assert.Equal("namespace Test;", written!.Content);
        Assert.Equal("namespace Test;", read!.Content);
        Assert.True(deleted);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadProjectFileAsync(_caller, project.Id, "../outside.cs"));
    }

    [Fact]
    public async Task SubmitBuildStoresSuccessfulArtifactAndLog()
    {
        var buildRunner = new FakeBuildRunner(BuildStatus.Succeeded);
        var service = CreateService(buildRunner: buildRunner);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Build", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id);
        var log = await service.GetBuildLogAsync(_caller, build!.Id);
        var artifact = await service.GetBuildArtifactAsync(_caller, build.Id);

        Assert.Equal(BuildStatus.Succeeded, build.Status);
        Assert.NotNull(artifact);
        Assert.Contains("fake build", log);
        Assert.Equal(project.Id, buildRunner.LastProjectId);
    }

    [Fact]
    public async Task FailedBuildHasDiagnosticsAndNoArtifact()
    {
        var service = CreateService(buildRunner: new FakeBuildRunner(BuildStatus.Failed));
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Fail", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id);

        Assert.Equal(BuildStatus.Failed, build!.Status);
        Assert.Null(build.Artifact);
        Assert.Contains(build.Diagnostics, x => x.Severity is BuildDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task PromoteStoresPromotionAndRuntimeStatusUsesNuplaneCatalog()
    {
        var nuplane = new FakeNuplaneAdmin();
        var featureManagement = new FakeFeatureManagement();
        var promotion = new FakePromotionService();
        var service = CreateService(new FakeBuildRunner(BuildStatus.Succeeded), promotion, nuplane, featureManagement);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("elsa-activity-module", "Elsa.Test.Promote", "1.0.0", "net10.0", null, null));
        var build = await service.SubmitBuildAsync(_caller, project.Id);

        var result = await service.PromoteBuildAsync(_caller, build!.Id);
        nuplane.Packages =
        [
            new(result!.PublishedPackage!.PackageId, result.PublishedPackage.Version, "local", "drop", result.PublishedPackage.Path, DateTimeOffset.UtcNow, "corr", "graph", "gen", default, [], [], true)
        ];
        featureManagement.Features =
        [
            new("FeatureA", "Feature A", null, [], "manifest", result.PublishedPackage.PackageId, result.PublishedPackage.Version, true, Json("{}"), false, false, null, null, null, [])
        ];

        var status = await service.GetRuntimeStatusAsync(_caller, project.Id);

        Assert.Equal(PromotionStatus.Accepted, result.Status);
        var package = Assert.Single(status!.Packages);
        Assert.Equal(ExtensionPackageRuntimeState.Loaded, package.State);
        Assert.Contains(package.Contributions, x => x.Id == "FeatureA");
    }

    [Fact]
    public async Task RollbackMissingVersionIsRejectedAndRetryReturnsOutcome()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Rollback", "1.0.0", "net10.0", null, null));

        var rollback = await service.RollbackPackageAsync(_caller, project.Id, new("0.9.0"));
        var retry = await service.RetryReconciliationAsync(_caller, project.Id);

        Assert.Equal(PromotionStatus.Rejected, rollback!.Status);
        Assert.Equal("completed", retry!.ReconcileOutcome.Outcome);
    }

    [Fact]
    public async Task PromotionValidationRejectsMalformedMissingManifestAndDeniedDependencyPackages()
    {
        var malformed = Path.Combine(_directory, "bad.nupkg");
        await File.WriteAllTextAsync(malformed, "not a zip");
        var service = new ExtensionBuilderPromotionService(
            new FakeEnvironment(_directory),
            Options.Create(new ExtensionBuilderOptions { DeniedDependencyPatterns = ["Dangerous.*"] }),
            new FakeNuplaneAdmin());
        var denied = Path.Combine(_directory, "denied.nupkg");
        CreatePackage(denied, "Elsa.Test.Denied", "1.0.0", "Dangerous.Package", includeManifest: true);
        var missingManifest = Path.Combine(_directory, "missing-manifest.nupkg");
        CreatePackage(missingManifest, "Elsa.Test.MissingManifest", "1.0.0", "Safe.Package");

        var malformedResult = await service.ValidatePackageAsync(malformed);
        var deniedResult = await service.ValidatePackageAsync(denied);
        var missingManifestResult = await service.ValidatePackageAsync(missingManifest);

        Assert.Equal(PromotionRejectionReason.MalformedPackage, malformedResult.RejectionReason);
        Assert.Equal(PromotionRejectionReason.DependencyPolicy, deniedResult.RejectionReason);
        Assert.Equal(PromotionRejectionReason.InvalidManifest, missingManifestResult.RejectionReason);
    }

    [Fact]
    public async Task SubmitBuildReturnsFailedBuildWhenRunnerCannotStart()
    {
        var service = CreateService(buildRunner: new ThrowingBuildRunner());
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Throw", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id);

        Assert.Equal(BuildStatus.Failed, build!.Status);
        Assert.Contains(build.Diagnostics, x => x.Severity is BuildDiagnosticSeverity.Error);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return ValueTask.CompletedTask;
    }

    private ExtensionBuilderService CreateService(
        IExtensionBuilderBuildRunner? buildRunner = null,
        IExtensionBuilderPromotionService? promotion = null,
        FakeNuplaneAdmin? nuplane = null,
        IFeatureManagementService? featureManagement = null)
    {
        nuplane ??= new FakeNuplaneAdmin();
        var storage = new ExtensionBuilderStorage(
            new FakeEnvironment(_directory),
            Options.Create(new ExtensionBuilderOptions { StoragePath = Path.Combine(_directory, "state") }));
        return new(
            storage,
            new ExtensionBuilderTemplateCatalog(),
            buildRunner ?? new FakeBuildRunner(BuildStatus.Succeeded),
            promotion ?? new FakePromotionService(),
            nuplane,
            featureManagement ?? new FakeFeatureManagement(),
            NullLogger<ExtensionBuilderService>.Instance);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static void CreatePackage(string path, string packageId, string version, string dependencyId, bool includeManifest = false)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var nuspec = archive.CreateEntry($"{packageId}.nuspec");
        using (var writer = new StreamWriter(nuspec.Open()))
        {
            writer.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>{packageId}</id>
                    <version>{version}</version>
                    <dependencies>
                      <dependency id="{dependencyId}" version="1.0.0" />
                    </dependencies>
                  </metadata>
                </package>
                """);
        }

        if (includeManifest)
        {
            var manifest = archive.CreateEntry("elsa-package.json");
            using var writer = new StreamWriter(manifest.Open());
            writer.Write($$"""
                {
                  "package": {
                    "id": "{{packageId}}",
                    "version": "{{version}}"
                  },
                  "features": []
                }
                """);
        }
    }

    private sealed class ThrowingBuildRunner : IExtensionBuilderBuildRunner
    {
        public Task<BuildResult> RunAsync(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, string artifactsPath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("dotnet executable was not found.");
    }

    private sealed class FakeBuildRunner(BuildStatus status) : IExtensionBuilderBuildRunner
    {
        public string? LastProjectId { get; private set; }

        public async Task<BuildResult> RunAsync(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, string artifactsPath, CancellationToken cancellationToken = default)
        {
            LastProjectId = project.Id;
            await File.WriteAllTextAsync(logPath, "fake build", cancellationToken);
            if (status is BuildStatus.Failed)
            {
                return new(buildId, project.Id, project.WorkspaceId, snapshot.Id, BuildStatus.Failed,
                    [new(BuildDiagnosticSeverity.Error, "compile failed", "Class1.cs", 1, 1, "CS1001")],
                    null, logPath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            }

            Directory.CreateDirectory(artifactsPath);
            var artifactPath = Path.Combine(artifactsPath, $"{project.PackageId}.{project.PackageVersion}.nupkg");
            await File.WriteAllTextAsync(artifactPath, "fake package", cancellationToken);
            return new(buildId, project.Id, project.WorkspaceId, snapshot.Id, BuildStatus.Succeeded, [],
                new($"artifact_{Guid.NewGuid():N}", buildId, project.PackageId, project.PackageVersion, Path.GetFileName(artifactPath), artifactPath, new FileInfo(artifactPath).Length, DateTimeOffset.UtcNow),
                logPath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
    }

    private sealed class FakePromotionService : IExtensionBuilderPromotionService
    {
        public Task<PackagePromotionResult> PromoteAsync(BuildResult build, CancellationToken cancellationToken = default)
        {
            var artifact = build.Artifact!;
            return Task.FromResult(new PackagePromotionResult(
                PromotionStatus.Accepted,
                null,
                new(artifact.PackageId, artifact.Version, "local", artifact.Path),
                new("completed", "corr", null, false, []),
                true,
                false));
        }

        public Task<PackagePromotionResult> RollbackAsync(ExtensionProject project, PackagePromotionRecord target, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PackagePromotionResult(PromotionStatus.Accepted, null, new(project.PackageId, target.Version, "local", target.FeedPath), new("completed", "corr", null, false, []), true, false));

        public Task<ExtensionBuilderReconcileOutcome> RetryReconciliationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtensionBuilderReconcileOutcome("completed", "corr", null, false, []));
    }

    private sealed class FakeNuplaneAdmin : INuplaneAdminOperations
    {
        public IReadOnlyList<ActivePackage> Packages { get; set; } = [];

        public Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ActivePackagesSnapshot(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Packages, "corr"));

        public Task<OperationalStateSnapshot> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalStateSnapshot(DateTimeOffset.UtcNow, new("corr", DateTimeOffset.UtcNow, false, false, []), default, [], "corr"));

        public Task<ManualReconcileOutcome> TriggerReconcileAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ManualReconcileOutcome(
                ManualReconcileOutcomeCode.Completed,
                "corr",
                new ReconciliationRunResult(false, new PackageChangeSet([], [], [], "corr", DateTimeOffset.UtcNow), [], false),
                null));
    }

    private sealed class FakeFeatureManagement : IFeatureManagementService
    {
        public IReadOnlyList<FeatureCatalogItem> Features { get; set; } = [];

        public Task<FeatureCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FeatureCatalogResponse("rev", Features));

        public Task<FeatureApplyResult> ApplyAsync(FeatureApplyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
