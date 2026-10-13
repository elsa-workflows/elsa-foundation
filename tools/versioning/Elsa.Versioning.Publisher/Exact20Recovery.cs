using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Elsa.Versioning.Calculator;

namespace Elsa.Versioning.Publisher;

internal sealed record Exact20RecoveryPins(
    int SchemaVersion,
    long ArtifactId,
    long RunId,
    long WorkflowId,
    long RepositoryId,
    string SourceCommit,
    string ArtifactSha256,
    string StateCommit,
    string StateSha256,
    string ArtifactName,
    string ManifestSha256,
    int PackageCount,
    int RecoveryCount)
{
    public const string Confirmation = "RECOVER-EXACT20-24533cfe6fcdd000f518d1855a525cfe6b37d6f9";
    public const string Repository = "elsa-workflows/elsa-foundation";
    public const string RepositoryApi = "https://api.github.com/repos/elsa-workflows/elsa-foundation/";
    public const string MainRef = "refs/heads/main";
    public const string RecoveryWorkflowPath = ".github/workflows/publish-exact20-recovery.yml";
    public const string FeedIndex = "https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json";
    public const string FeedKeyName = "FEEDZ_API_KEY";
    public const string ApprovalIssueUrl = "https://api.github.com/repos/elsa-workflows/elsa-foundation/issues/2505";
    public const string ApprovalAuthor = "sfmskywalker";
    public const string ApprovalLine = "APPROVE EXACT20 RECOVERY";

    public static Exact20RecoveryPins Incident { get; } = new(
        1,
        11719153513,
        38185385882,
        307637788,
        1232994340,
        "24533cfe6fcdd000f518d1855a525cfe6b37d6f9",
        "6848547ea1ee218ba234e3d294f43cf5fd42396620fe0125a22d0bb074eab211",
        "bbe803c40c996e8a89c934631ec9930cf90251b2",
        "71a16003e7ca0fab53ced8807016e04e383b6db4ed9184394b7973822c077c8b",
        "elsa-foundation-nuget-packages",
        "7ed68791f5f5d85bd36053fa236cc8ce96002eb7eeb87e25081c21b1f41e4ee1",
        138,
        20);
}

internal sealed record Exact20Manifest
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; }
    [JsonPropertyName("source_commit")] public string? SourceCommit { get; init; }
    [JsonPropertyName("artifact_id")] public long ArtifactId { get; init; }
    [JsonPropertyName("run_id")] public long RunId { get; init; }
    [JsonPropertyName("workflow_id")] public long WorkflowId { get; init; }
    [JsonPropertyName("repository_id")] public long RepositoryId { get; init; }
    [JsonPropertyName("artifact_sha256")] public string? ArtifactSha256 { get; init; }
    [JsonPropertyName("state_commit")] public string? StateCommit { get; init; }
    [JsonPropertyName("state_sha256")] public string? StateSha256 { get; init; }
    [JsonPropertyName("packages")] public IReadOnlyList<Exact20ManifestPackage>? Packages { get; init; }
}

internal sealed record Exact20ManifestPackage
{
    [JsonPropertyName("package_id")] public string? PackageId { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
    [JsonPropertyName("recover")] public bool Recover { get; init; }
    [JsonPropertyName("expected_state_version")] public string? ExpectedStateVersion { get; init; }
    [JsonPropertyName("expected_state_commit")] public string? ExpectedStateCommit { get; init; }
}

internal sealed record Exact20WorkflowRun(
    long Id,
    long WorkflowId,
    string Path,
    string Event,
    string Status,
    string? Conclusion,
    int Attempt,
    string HeadBranch,
    string HeadSha,
    long RepositoryId,
    long HeadRepositoryId);

internal sealed record Exact20Artifact(
    long Id,
    string Name,
    bool Expired,
    long SizeInBytes,
    DateTimeOffset ExpiresAt,
    string Digest,
    string ArchiveDownloadUrl,
    long RunId,
    long RepositoryId,
    long HeadRepositoryId,
    string HeadBranch,
    string HeadSha);

internal sealed record Exact20ApprovalComment(
    long Id,
    string IssueUrl,
    string Author,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record Exact20RunCount(bool IsInteger, int Value);

internal sealed record Exact20PackageResponse(int StatusCode, byte[]? Bytes);

internal sealed record Exact20PackageSource(
    Exact20ManifestPackage Manifest,
    string Path,
    string PackageId,
    string Version,
    string SourceCommit,
    IReadOnlyList<string> Dependencies);

internal sealed record Exact20RunOptions(
    string Repository,
    bool Execute,
    string? Confirmation,
    long? ApprovalCommentId,
    IReadOnlyDictionary<string, string?> Environment,
    string? SummaryPath);

internal interface IExact20GitHub
{
    Task<string> GetMainShaAsync(CancellationToken cancellationToken);
    Task<Exact20WorkflowRun> GetRunAsync(long runId, CancellationToken cancellationToken);
    Task<Exact20Artifact> GetArtifactAsync(long artifactId, CancellationToken cancellationToken);
    Task DownloadArtifactAsync(long artifactId, string destination, long maximumBytes, CancellationToken cancellationToken);
    Task<string> GetWorkflowStateAsync(long workflowId, CancellationToken cancellationToken);
    Task<Exact20RunCount> GetNonterminalRunCountAsync(long workflowId, string status, CancellationToken cancellationToken);
    Task<Exact20ApprovalComment> GetApprovalCommentAsync(long commentId, CancellationToken cancellationToken);
}

internal interface IExact20Feed
{
    void PrepareForExecution();
    Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken cancellationToken);
    Task<Exact20PackageResponse> GetPackageAsync(string packageId, string version, CancellationToken cancellationToken);
    Task<PushResult> PushAsync(string packagePath, CancellationToken cancellationToken);
}

internal interface IExact20LocalGit
{
    string ResolveHead();
    bool IsAncestorOrSelf(string ancestor, string descendant);
    byte[] ReadPublishedVersions(string commit);
}

internal sealed class Exact20LocalGit(string directory) : IExact20LocalGit
{
    private readonly GitRepository git = new(directory);
    public string ResolveHead() => git.ResolveCommit("HEAD");
    public bool IsAncestorOrSelf(string ancestor, string descendant) => git.IsAncestorOrSelf(ancestor, descendant);
    public byte[] ReadPublishedVersions(string commit) => git.ReadBlob($"{commit}:{PublishedVersions.DefaultPath}");
}

internal sealed record Exact20RecoveryReport(
    bool Succeeded,
    bool ReadOnly,
    string Message,
    IReadOnlyList<string> ConfirmedPackageIds,
    IReadOnlyList<string> RemainingPackageIds,
    string ProposedRecord,
    string? WrittenStateCommit)
{
    public string Format()
    {
        var result = new StringBuilder()
            .AppendLine($"Exact20 recovery: {Message}")
            .AppendLine($"Mode: {(ReadOnly ? "read-only preflight" : "execute")}")
            .AppendLine($"Confirmed package uploads: {ConfirmedPackageIds.Count}");

        if (ConfirmedPackageIds.Count > 0)
            result.AppendLine($"Confirmed: {string.Join(", ", ConfirmedPackageIds)}");

        if (RemainingPackageIds.Count > 0)
            result.AppendLine($"Remaining candidates: {string.Join(", ", RemainingPackageIds)}");

        result.AppendLine($"State write: {(WrittenStateCommit is null ? "not confirmed" : WrittenStateCommit)}")
            .AppendLine()
            .AppendLine("Proposed canonical published-versions record:")
            .AppendLine("```json")
            .Append(ProposedRecord)
            .AppendLine("```");

        return result.ToString();
    }
}

internal sealed class Exact20RecoveryCommand(
    Exact20RecoveryPins pins,
    IExact20GitHub github,
    IExact20Feed feed,
    IPublishState state,
    IExact20LocalGit localGit,
    TextWriter output,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const string ManifestRelativePath = "tools/versioning/recovery/merge245-exact20.json";
    private static readonly long[] HeldPublisherWorkflowIds = [307637788, 307637495];
    private const int MaximumArtifactBytes = 64 * 1024 * 1024;
    private const int MaximumPackageBytes = 16 * 1024 * 1024;
    private const int FeedReadConcurrency = 6;
    private const int UploadPollAttempts = 10;
    private static readonly TimeSpan UploadPollDelay = TimeSpan.FromSeconds(2);
    private static readonly string[] NonterminalStatuses = ["queued", "in_progress", "requested", "waiting", "pending"];
    internal static IReadOnlyList<string> NonterminalStatusNames => NonterminalStatuses;
    private static readonly long[] GuardedWorkflowIds = [307637788, 307637495, 368711260];
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<Exact20RecoveryReport> RunAsync(Exact20RunOptions options, CancellationToken cancellationToken = default)
    {
        var manifestBytes = File.ReadAllBytes(Path.Combine(options.Repository, ManifestRelativePath));
        var manifest = ReadAndValidateManifest(manifestBytes, pins);
        var execution = options.Execute ? ValidateExecutionInvocation(options, pins) : null;
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"elsa-exact20-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        Exact20RecoveryReport report;
        PublishedVersions? validatedBaseline = null;
        try
        {
            var currentMain = await github.GetMainShaAsync(cancellationToken);
            ValidateMain(manifest, currentMain, options, execution, localGit);

            if (execution is not null)
            {
                var ownRun = await github.GetRunAsync(execution.RunId, cancellationToken);
                ValidateOwnRun(ownRun, execution, pins);
                var approval = await github.GetApprovalCommentAsync(execution.ApprovalCommentId, cancellationToken);
                ValidateApproval(approval, execution, currentMain, pins);
            }

            var originalRun = await github.GetRunAsync(pins.RunId, cancellationToken);
            ValidateOriginalRun(originalRun, pins);
            var artifact = await github.GetArtifactAsync(pins.ArtifactId, cancellationToken);
            ValidateArtifact(artifact, pins);
            var artifactPath = Path.Combine(temporaryDirectory, "original-artifact.zip");
            await github.DownloadArtifactAsync(pins.ArtifactId, artifactPath, MaximumArtifactBytes, cancellationToken);
            if (new FileInfo(artifactPath).Length != artifact.SizeInBytes)
                throw new InvalidDataException("The downloaded artifact size does not match GitHub's pinned artifact metadata.");
            var artifactBytes = await File.ReadAllBytesAsync(artifactPath, cancellationToken);
            var artifactHash = Convert.ToHexStringLower(SHA256.HashData(artifactBytes));
            if (!string.Equals(artifactHash, manifest.ArtifactSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("The downloaded artifact does not match the pinned SHA-256.");

            var packages = ExtractAndValidatePackages(artifactPath, temporaryDirectory, manifest, cancellationToken);
            var packagesById = packages.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
            await output.WriteLineAsync($"Original pinned artifact verified: {packages.Length} exact package hashes, identities, source commits and ZIP CRCs.");
            var safetyBeforeFeeds = await ReadSafetySnapshotAsync(manifest, currentMain, cancellationToken);
            validatedBaseline = safetyBeforeFeeds.Record;
            await ReadAndValidateFeedAsync(manifest, cancellationToken);
            await output.WriteLineAsync($"Feed verified: {pins.PackageCount - pins.RecoveryCount} exact live byte matches and {pins.RecoveryCount} exact candidate GET404s; all version floors passed.");
            var safetyAfterFeeds = await ReadSafetySnapshotAsync(manifest, currentMain, cancellationToken);
            RequireSameSnapshot(safetyBeforeFeeds, safetyAfterFeeds, "The state, main ref, or workflow controls changed while the 138 feed indexes and exact packages were being read.");

            var recoveryOrder = OrderRecoveryPackages(packagesById, manifest, expectedWaveSizes: [12, 6, 2]);
            if (!options.Execute)
            {
                report = new Exact20RecoveryReport(
                    true,
                    true,
                    "all pinned inputs and live read-only preflight checks passed; no package or state writes were attempted",
                    [],
                    recoveryOrder.Select(item => item.PackageId).ToArray(),
                    RecordWithConfirmed(safetyBeforeFeeds.Record, recoveryOrder,
                        recoveryOrder.Select(package => package.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase)).Serialize(),
                    null);
            }
            else
            {
                feed.PrepareForExecution();
                report = await ExecuteAsync(manifest, recoveryOrder, safetyBeforeFeeds, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException or InvalidDataException or HttpRequestException or OperationCanceledException or FeedException or ArgumentException)
        {
            var reason = SafeFailure(exception);
            report = new Exact20RecoveryReport(false, !options.Execute, reason, [], [], validatedBaseline?.Serialize() ?? "{}\n", null);
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Temporary package files are disposable; a cleanup failure does not change the remote outcome.
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary package files are disposable; a cleanup failure does not change the remote outcome.
            }
        }

        await output.WriteAsync(report.Format());
        if (options.SummaryPath is { Length: > 0 } summaryPath)
        {
            try
            {
                await File.AppendAllTextAsync(summaryPath, report.Format(), CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await output.WriteLineAsync("The recovery result was printed, but the configured step summary could not be updated.");
            }
        }

        return report;
    }

    private async Task<Exact20RecoveryReport> ExecuteAsync(
        Exact20Manifest manifest,
        IReadOnlyList<Exact20PackageSource> recoveryOrder,
        Exact20SafetySnapshot baseline,
        CancellationToken cancellationToken)
    {
        var confirmed = new List<Exact20PackageSource>();
        var stopReason = "all 20 exact package uploads and the state compare-and-swap were confirmed";
        var stopped = false;

        foreach (var package in recoveryOrder)
        {
            try
            {
                var beforePush = await ReadSafetySnapshotAsync(manifest, baseline.MainSha, cancellationToken);
                RequireSameSnapshot(baseline, beforePush, $"Safety conditions changed before uploading {package.PackageId}; no further packages will be pushed.");
                // The workflow lock fences known writers, while a fresh per-candidate read also
                // refuses external feed changes observed since the complete preflight.
                ValidateFeedVersions(package.Manifest, await feed.ListVersionsAsync(package.PackageId, cancellationToken));
                var stillAbsent = await feed.GetPackageAsync(package.PackageId, package.Version, cancellationToken);
                if (stillAbsent.StatusCode != 404 || stillAbsent.Bytes is not null)
                    throw new InvalidOperationException($"{package.PackageId} is no longer absent by exact GET404 before its single PUT; future pushes stopped.");
            }
            catch (Exception exception) when (IsPreflightFailure(exception))
            {
                stopReason = SafeFailure(exception);
                stopped = true;
                break;
            }

            PushResult push;
            try
            {
                push = await feed.PushAsync(package.Path, cancellationToken);
            }
            catch (Exception exception) when (IsPreflightFailure(exception))
            {
                push = new PushResult(PushStatus.Failed, "the single package PUT did not return a result");
                stopReason = SafeFailure(exception);
            }

            var exact = false;
            try
            {
                exact = await PollForExactPackageAsync(package, cancellationToken);
            }
            catch (Exception exception) when (IsPreflightFailure(exception))
            {
                stopReason = SafeFailure(exception);
            }

            if (exact)
                confirmed.Add(package);

            if (push.Status != PushStatus.Pushed)
            {
                stopReason = exact
                    ? $"{package.PackageId} was confirmed by exact GET after an ambiguous, non-2xx, or conflict PUT; future pushes stopped"
                    : $"{package.PackageId} PUT was not confirmed by exact GET; future pushes stopped";
                stopped = true;
                break;
            }

            if (!exact)
            {
                if (stopReason == "all 20 exact package uploads and the state compare-and-swap were confirmed")
                    stopReason = $"{package.PackageId} exact package bytes were not visible before the bounded GET poll ended; future pushes stopped";
                stopped = true;
                break;
            }
        }

        var proposed = RecordWithConfirmed(baseline.Record, recoveryOrder, confirmed.Select(item => item.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase));
        string? writtenCommit = null;
        if (confirmed.Count > 0)
        {
            try
            {
                var beforeWrite = await ReadSafetySnapshotAsync(manifest, baseline.MainSha, cancellationToken);
                RequireSameSnapshot(baseline, beforeWrite, "Safety conditions changed before the state write; the confirmed package rows remain a proposal only.");
                var message = BuildStateCommitMessage(confirmed);
                var pushedCommit = state.Write(proposed, baseline.StateTip, message);
                var readbackTip = state.FetchTip();
                if (!string.Equals(readbackTip, pushedCommit, StringComparison.Ordinal))
                    throw new InvalidOperationException("The publish-state remote tip did not read back as the new commit.");

                var readbackBytes = localGit.ReadPublishedVersions(pushedCommit);
                if (!readbackBytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(proposed.Serialize())))
                    throw new InvalidOperationException("The pushed published-versions bytes did not read back exactly.");

                var readback = state.Read(pushedCommit);
                if (!string.Equals(readback.Serialize(), proposed.Serialize(), StringComparison.Ordinal))
                    throw new InvalidOperationException("The pushed published-versions record did not read back exactly.");

                writtenCommit = pushedCommit;
            }
            catch (Exception exception) when (IsPreflightFailure(exception))
            {
                stopReason = $"{SafeFailure(exception)} The exact confirmed rows and canonical state proposal are retained below; no retry was attempted.";
                stopped = true;
            }
        }

        var remaining = recoveryOrder.Where(package => !confirmed.Contains(package)).Select(package => package.PackageId).ToArray();
        var allConfirmedAndWritten = confirmed.Count == recoveryOrder.Count && writtenCommit is not null && !stopped;
        return new Exact20RecoveryReport(
            allConfirmedAndWritten,
            false,
            stopReason,
            confirmed.Select(package => package.PackageId).ToArray(),
            remaining,
            proposed.Serialize(),
            writtenCommit);
    }

    private async Task<Exact20SafetySnapshot> ReadSafetySnapshotAsync(Exact20Manifest manifest, string expectedMain, CancellationToken cancellationToken)
    {
        var mainSha = await github.GetMainShaAsync(cancellationToken);
        if (!string.Equals(mainSha, expectedMain, StringComparison.Ordinal))
            throw new InvalidOperationException("The current main commit changed during the recovery preflight.");

        var stateTip = state.FetchTip();
        if (!string.Equals(stateTip, manifest.StateCommit, StringComparison.Ordinal))
            throw new InvalidOperationException("The publish-state branch tip no longer matches the pinned incident state commit.");

        var stateBytes = localGit.ReadPublishedVersions(stateTip!);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(stateBytes)), manifest.StateSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("The publish-state file bytes do not match the pinned SHA-256.");

        var record = state.Read(stateTip!);
        if (!stateBytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(record.Serialize())))
            throw new InvalidOperationException("The publish-state file is not in canonical PublishedVersions serialization.");

        ValidateStateRecord(record, manifest, pins);
        var workflowStates = new List<string>(HeldPublisherWorkflowIds.Length);
        foreach (var workflowId in HeldPublisherWorkflowIds)
        {
            var workflowState = await github.GetWorkflowStateAsync(workflowId, cancellationToken);
            if (!string.Equals(workflowState, "disabled_manually", StringComparison.Ordinal))
                throw new InvalidOperationException($"Foundation publisher workflow {workflowId} is not disabled_manually.");
            workflowStates.Add(workflowState);
        }

        var counts = new List<Exact20RunCount>(GuardedWorkflowIds.Length * NonterminalStatuses.Length);
        foreach (var workflowId in GuardedWorkflowIds)
        {
            foreach (var status in NonterminalStatuses)
            {
                var count = await github.GetNonterminalRunCountAsync(workflowId, status, cancellationToken);
                if (!count.IsInteger || count.Value != 0)
                    throw new InvalidOperationException($"Foundation workflow {workflowId} has a nonzero or noninteger {status} run count.");
                counts.Add(count);
            }
        }

        return new Exact20SafetySnapshot(mainSha, stateTip!, Convert.ToHexStringLower(SHA256.HashData(stateBytes)), workflowStates, counts, record);
    }

    private async Task ReadAndValidateFeedAsync(Exact20Manifest manifest, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(FeedReadConcurrency);
        var reads = manifest.Packages!.Select(async row =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var versions = await feed.ListVersionsAsync(row.PackageId!, cancellationToken);
                ValidateFeedVersions(row, versions);
                var exact = await feed.GetPackageAsync(row.PackageId!, row.Version!, cancellationToken);
                if (row.Recover)
                {
                    if (exact.StatusCode != 404 || exact.Bytes is not null)
                        throw new InvalidOperationException($"Recovery candidate {row.PackageId} {row.Version} is not absent by exact GET 404.");
                    if (versions.Contains(row.Version!, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Recovery candidate {row.PackageId} {row.Version} still appears in its fresh feed index.");
                }
                else
                {
                    if (exact.StatusCode != 200 || exact.Bytes is null)
                        throw new InvalidOperationException($"Published package {row.PackageId} {row.Version} did not answer exact GET 200.");
                    if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(exact.Bytes)), row.Sha256, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Published package {row.PackageId} {row.Version} does not match the pinned artifact bytes.");
                    ValidatePackageIdentity(exact.Bytes, row.PackageId!, row.Version!, manifest.SourceCommit!, "feed package");
                }

            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(reads);
    }

    private void ValidateFeedVersions(Exact20ManifestPackage row, IReadOnlyList<string> versions)
    {
        if (versions.Count == 0)
            throw new InvalidOperationException($"Package {row.PackageId} has no versions in its feed index.");

        var parsed = versions.Select(version => PackageVersionNumber.Parse(version, $"feed index for {row.PackageId}")).ToArray();
        var highest = parsed.Select((version, index) => (version, index))
            .OrderByDescending(item => item.version, PackageVersionComparer.Instance)
            .First();
        if (!string.Equals(versions[highest.index], row.ExpectedStateVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Package {row.PackageId}'s highest feed version is not its exact pinned prior-state floor.");

        var target = PackageVersionNumber.Parse(row.Version!, $"manifest package {row.PackageId}");
        var floor = PackageVersionNumber.Parse(row.ExpectedStateVersion!, $"expected state version for {row.PackageId}");
        if (row.Recover)
        {
            if (target.Major != floor.Major || target.Minor != floor.Minor || target.Patch != floor.Patch + 1 ||
                !string.Equals(target.Label, floor.Label, StringComparison.Ordinal))
                throw new InvalidOperationException($"Recovery candidate {row.PackageId} is not the next patch above its pinned prior-state floor.");
        }
        else if (!string.Equals(row.Version, row.ExpectedStateVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Live package {row.PackageId} does not match its expected state version.");
        }
    }

    private async Task<bool> PollForExactPackageAsync(Exact20PackageSource package, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < UploadPollAttempts; attempt++)
        {
            var response = await feed.GetPackageAsync(package.PackageId, package.Version, cancellationToken);
            if (response.StatusCode == 200 && response.Bytes is { } bytes)
            {
                if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), package.Manifest.Sha256, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{package.PackageId} {package.Version} feed bytes differ from the exact pinned artifact; the row will not be recorded.");
                ValidatePackageIdentity(bytes, package.PackageId, package.Version, pins.SourceCommit, "post-upload feed package");
                return true;
            }

            if (response.StatusCode != 404 || response.Bytes is not null)
                throw new InvalidOperationException($"{package.PackageId} {package.Version} exact GET answered an unexpected status; the row will not be recorded.");

            if (attempt + 1 < UploadPollAttempts)
                await (delay ?? Task.Delay)(UploadPollDelay, cancellationToken);
        }

        return false;
    }

    private Exact20PackageSource[] ExtractAndValidatePackages(
        string artifactPath,
        string temporaryDirectory,
        Exact20Manifest manifest,
        CancellationToken cancellationToken)
    {
        var expected = manifest.Packages!.ToDictionary(row => row.PackageId!, StringComparer.OrdinalIgnoreCase);
        var packages = new Dictionary<string, Exact20PackageSource>(StringComparer.OrdinalIgnoreCase);
        var crcEntries = ZipCrcIndex.Read(artifactPath);
        using var artifact = ZipFile.OpenRead(artifactPath);
        long totalExpanded = 0;
        var packageNumber = 0;

        foreach (var entry in artifact.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Length > MaximumArtifactBytes)
                throw new InvalidDataException("The original artifact contains an entry larger than the recovery bound.");

            var isPackage = entry.FullName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith("/", StringComparison.Ordinal);
            var path = isPackage ? Path.Combine(temporaryDirectory, $"package-{packageNumber++}.nupkg") : null;
            var crc = Crc32.Start;
            long entryBytes = 0;
            var buffer = new byte[64 * 1024];
            using (var source = entry.Open())
            {
                using var destination = path is null ? Stream.Null : new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                while (true)
                {
                    var read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;
                    entryBytes += read;
                    totalExpanded += read;
                    if (totalExpanded > MaximumArtifactBytes * 4L || (isPackage && entryBytes > MaximumPackageBytes))
                        throw new InvalidDataException("The original artifact expands beyond the recovery bounds.");
                    crc = Crc32.Append(crc, buffer.AsSpan(0, read));
                    destination.Write(buffer, 0, read);
                }
            }

            if (entryBytes != entry.Length || Crc32.Finish(crc) != crcEntries[entry.FullName])
                throw new InvalidDataException($"The original artifact entry '{SafeEntryName(entry.FullName)}' failed its size or CRC check.");

            if (!isPackage)
                continue;

            var bytesHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path!)));
            var packed = PackedPackage.Read(path!);
            using var sourcePackage = File.OpenRead(path!);
            var sourceCommit = PackedPackage.ReadSourceCommit(sourcePackage, path!);
            var packageId = packed.PackageId;
            if (!expected.TryGetValue(packageId, out var row) || packages.ContainsKey(packageId))
                throw new InvalidDataException("The artifact contains an unexpected or duplicate package id.");
            if (!string.Equals(packed.Version, row.Version, StringComparison.Ordinal) || !string.Equals(bytesHash, row.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"The artifact package {packageId} does not match its pinned version and SHA-256.");
            if (!string.Equals(sourceCommit, manifest.SourceCommit, StringComparison.Ordinal))
                throw new InvalidDataException($"The artifact package {packageId} does not name the pinned repository source commit.");

            ValidateNupkgCrcs(path!);
            packages.Add(packageId, new Exact20PackageSource(row, path!, packageId, packed.Version, sourceCommit, packed.DependencyIds));
        }

        if (packages.Count != pins.PackageCount || packages.Count != expected.Count || packages.Keys.Any(packageId => !expected.ContainsKey(packageId)))
            throw new InvalidDataException("The original artifact does not contain exactly the 138 pinned package ids.");

        return packages.Values.ToArray();
    }

    private void ValidateNupkgCrcs(string packagePath)
    {
        var crcEntries = ZipCrcIndex.Read(packagePath);
        using var package = ZipFile.OpenRead(packagePath);
        long expanded = 0;
        foreach (var entry in package.Entries)
        {
            if (entry.Length > MaximumPackageBytes * 32L)
                throw new InvalidDataException("A pinned package contains an entry larger than the package validation bound.");
            using var stream = entry.Open();
            var crc = Crc32.Start;
            long entryBytes = 0;
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                entryBytes += read;
                expanded += read;
                if (entryBytes > MaximumPackageBytes * 32L || expanded > MaximumPackageBytes * 128L)
                    throw new InvalidDataException("A pinned package expands beyond the package validation bound.");
                crc = Crc32.Append(crc, buffer.AsSpan(0, read));
            }

            if (entryBytes != entry.Length || Crc32.Finish(crc) != crcEntries[entry.FullName])
                throw new InvalidDataException($"A pinned package entry '{SafeEntryName(entry.FullName)}' failed its size or CRC check.");
        }
    }

    private static void ValidatePackageIdentity(byte[] bytes, string packageId, string version, string sourceCommit, string source)
    {
        using var package = new MemoryStream(bytes, writable: false);
        var metadata = PackedPackage.Read(package, source);
        if (!string.Equals(metadata.PackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(metadata.Version, version, StringComparison.Ordinal))
            throw new InvalidOperationException($"{source} nuspec id/version do not match the pinned manifest.");
        using var sourcePackage = new MemoryStream(bytes, writable: false);
        if (!string.Equals(PackedPackage.ReadSourceCommit(sourcePackage, source), sourceCommit, StringComparison.Ordinal))
            throw new InvalidOperationException($"{source} nuspec repository source does not match the pinned commit.");
    }

    private static void ValidateStateRecord(PublishedVersions record, Exact20Manifest manifest, Exact20RecoveryPins pins)
    {
        if (!string.Equals(record.LastPublishCommit, pins.SourceCommit, StringComparison.Ordinal))
            throw new InvalidOperationException("The pinned state record does not preserve last_publish_commit at the incident source commit.");

        var rows = manifest.Packages!.ToDictionary(row => row.PackageId!, StringComparer.OrdinalIgnoreCase);
        if (record.Packages.Count != pins.PackageCount || record.Packages.Count != rows.Count)
            throw new InvalidOperationException("The pinned state record does not have exactly the 138 manifest package rows.");

        foreach (var entry in record.Packages)
        {
            if (!rows.TryGetValue(entry.PackageId, out var row) ||
                !string.Equals(entry.Version.ToString(), row.ExpectedStateVersion, StringComparison.Ordinal) ||
                !string.Equals(entry.Commit, row.ExpectedStateCommit, StringComparison.Ordinal))
                throw new InvalidOperationException($"The pinned state row for {entry.PackageId} differs from its exact manifest version/commit.");
        }
    }

    private static PublishedVersions RecordWithConfirmed(
        PublishedVersions current,
        IReadOnlyList<Exact20PackageSource> recoveryOrder,
        IReadOnlySet<string> confirmed)
    {
        if (confirmed.Count == 0)
            return current;

        var byId = recoveryOrder.Where(package => confirmed.Contains(package.PackageId))
            .ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        return new PublishedVersions(current.LastPublishCommit, current.Packages.Select(entry =>
        {
            if (!byId.TryGetValue(entry.PackageId, out var package))
                return entry;
            return entry with
            {
                Version = PackageVersionNumber.Parse(package.Version, $"confirmed recovery {package.PackageId}"),
                Commit = package.SourceCommit
            };
        }));
    }

    private static IReadOnlyList<Exact20PackageSource> OrderRecoveryPackages(
        IReadOnlyDictionary<string, Exact20PackageSource> packages,
        Exact20Manifest manifest,
        IReadOnlyList<int> expectedWaveSizes)
    {
        var candidates = manifest.Packages!.Where(row => row.Recover)
            .Select(row => packages[row.PackageId!])
            .ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        var depthById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int Depth(Exact20PackageSource package, HashSet<string> visiting)
        {
            if (depthById.TryGetValue(package.PackageId, out var known))
                return known;
            if (!visiting.Add(package.PackageId))
                throw new InvalidDataException("The pinned recovery package dependencies contain a cycle.");

            var dependencies = package.Dependencies.Where(candidates.ContainsKey).ToArray();
            var depth = dependencies.Length == 0 ? 0 : dependencies.Max(id => Depth(candidates[id], visiting)) + 1;
            visiting.Remove(package.PackageId);
            depthById.Add(package.PackageId, depth);
            return depth;
        }

        foreach (var package in candidates.Values)
            Depth(package, []);

        var waves = candidates.Values.GroupBy(package => depthById[package.PackageId])
            .OrderBy(group => group.Key)
            .Select(group => group.OrderBy(package => package.PackageId, StringComparer.Ordinal).ToArray())
            .ToArray();
        if (!waves.Select(wave => wave.Length).SequenceEqual(expectedWaveSizes))
            throw new InvalidDataException("The hash-verified nuspec graph does not produce the required deterministic 12/6/2 dependency waves.");

        return waves.SelectMany(wave => wave).ToArray();
    }

    private static Exact20Manifest ReadAndValidateManifest(byte[] bytes, Exact20RecoveryPins pins)
    {
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), pins.ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The public exact20 manifest bytes do not match the pinned SHA-256.");

        var manifest = JsonSerializer.Deserialize<Exact20Manifest>(bytes, ManifestOptions)
                       ?? throw new InvalidDataException("The public exact20 manifest is empty.");
        if (manifest.SchemaVersion != pins.SchemaVersion ||
            manifest.ArtifactId != pins.ArtifactId || manifest.RunId != pins.RunId ||
            manifest.WorkflowId != pins.WorkflowId || manifest.RepositoryId != pins.RepositoryId ||
            !string.Equals(manifest.SourceCommit, pins.SourceCommit, StringComparison.Ordinal) ||
            !string.Equals(manifest.ArtifactSha256, pins.ArtifactSha256, StringComparison.Ordinal) ||
            !string.Equals(manifest.StateCommit, pins.StateCommit, StringComparison.Ordinal) ||
            !string.Equals(manifest.StateSha256, pins.StateSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The public exact20 manifest does not match the pinned incident metadata.");

        if (manifest.Packages is null || manifest.Packages.Count != pins.PackageCount || manifest.Packages.Count(package => package.Recover) != pins.RecoveryCount)
            throw new InvalidDataException("The public exact20 manifest must hold exactly 138 package rows and the 20-package recovery allowlist.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.PackageId) || !Regex.IsMatch(package.PackageId, "^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant) ||
                !ids.Add(package.PackageId) || !IsSha256(package.Sha256) ||
                !GitRepository.IsFullObjectId(package.ExpectedStateCommit) ||
                package.Version is null || package.ExpectedStateVersion is null)
                throw new InvalidDataException("A public exact20 manifest package row is missing a unique id, digest, version, or state commit.");
            _ = PackageVersionNumber.Parse(package.Version, $"manifest package {package.PackageId}");
            _ = PackageVersionNumber.Parse(package.ExpectedStateVersion, $"manifest state row {package.PackageId}");
        }

        return manifest;
    }

    private static bool IsSha256(string? value) => value is not null && Regex.IsMatch(value, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    private static void ValidateMain(Exact20Manifest manifest, string mainSha, Exact20RunOptions options, Exact20ExecutionContext? execution, IExact20LocalGit localGit)
    {
        if (!GitRepository.IsFullObjectId(mainSha) || !localGit.IsAncestorOrSelf(manifest.SourceCommit!, mainSha))
            throw new InvalidOperationException("The pinned artifact source commit is not on the currently observed main history.");

        if (execution is not null && (!string.Equals(execution.Commit, mainSha, StringComparison.Ordinal) || !string.Equals(localGit.ResolveHead(), mainSha, StringComparison.Ordinal)))
            throw new InvalidOperationException("Execution requires the checked-out commit, GitHub SHA, and current remote main SHA to match exactly.");
    }

    private static Exact20ExecutionContext ValidateExecutionInvocation(Exact20RunOptions options, Exact20RecoveryPins pins)
    {
        var env = options.Environment;
        string Value(string key) => env.GetValueOrDefault(key) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Execution is missing required GitHub Actions context {key}.");
        if (!string.Equals(Value("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Value("GITHUB_EVENT_NAME"), "workflow_dispatch", StringComparison.Ordinal) ||
            !string.Equals(Value("GITHUB_REF"), Exact20RecoveryPins.MainRef, StringComparison.Ordinal) ||
            !string.Equals(Value("GITHUB_WORKFLOW"), "Exact20 recovery", StringComparison.Ordinal) ||
            !string.Equals(Value("GITHUB_JOB"), "recover", StringComparison.Ordinal) ||
            !string.Equals(Value("GITHUB_WORKFLOW_REF"), $"{Exact20RecoveryPins.Repository}/{Exact20RecoveryPins.RecoveryWorkflowPath}@{Exact20RecoveryPins.MainRef}", StringComparison.Ordinal) ||
            !string.Equals(Value("GITHUB_WORKFLOW_SHA"), Value("GITHUB_SHA"), StringComparison.Ordinal) ||
            !string.Equals(Value("GITHUB_REPOSITORY"), Exact20RecoveryPins.Repository, StringComparison.Ordinal) ||
            !string.Equals(Value("GITHUB_RUN_ATTEMPT"), "1", StringComparison.Ordinal) ||
            !string.Equals(options.Confirmation, Exact20RecoveryPins.Confirmation, StringComparison.Ordinal))
            throw new InvalidOperationException("Execution requires the approved manual Exact20 recovery job on main, attempt 1, with the exact confirmation string.");

        if (!long.TryParse(Value("GITHUB_RUN_ID"), out var runId) || runId <= 0 ||
            !GitRepository.IsFullObjectId(Value("GITHUB_SHA")) ||
            options.ApprovalCommentId is not { } approvalCommentId || approvalCommentId <= 0)
            throw new InvalidOperationException("Execution needs a positive current run id, full checkout SHA, and approval comment id.");

        var eventPath = Value("GITHUB_EVENT_PATH");
        var eventBytes = File.ReadAllBytes(eventPath);
        if (eventBytes.Length > 1024 * 1024)
            throw new InvalidDataException("The GitHub dispatch event payload exceeds the recovery bound.");
        using var eventDocument = JsonDocument.Parse(eventBytes);
        if (!eventDocument.RootElement.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Object ||
            !InputIsTrue(inputs, "execute") ||
            !InputEquals(inputs, "confirmation", Exact20RecoveryPins.Confirmation) ||
            !InputEquals(inputs, "approval_comment_id", approvalCommentId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            throw new InvalidOperationException("The authenticated GitHub dispatch payload does not contain the exact execute, confirmation, and approval-comment inputs.");

        return new Exact20ExecutionContext(runId, Value("GITHUB_SHA"), approvalCommentId, pins.ManifestSha256);
    }

    internal static void ValidateExecutionInvocationForCli(Exact20RunOptions options) =>
        _ = ValidateExecutionInvocation(options, Exact20RecoveryPins.Incident);

    private static bool InputIsTrue(JsonElement inputs, string name) =>
        inputs.TryGetProperty(name, out var input) && (input.ValueKind == JsonValueKind.True || input.ValueKind == JsonValueKind.String && input.GetString() == "true");

    private static bool InputEquals(JsonElement inputs, string name, string expected) =>
        inputs.TryGetProperty(name, out var input) && input.ValueKind == JsonValueKind.String && string.Equals(input.GetString(), expected, StringComparison.Ordinal);

    private static void ValidateOwnRun(Exact20WorkflowRun run, Exact20ExecutionContext execution, Exact20RecoveryPins pins)
    {
        if (run.Id != execution.RunId || run.WorkflowId <= 0 || run.RepositoryId != pins.RepositoryId || run.HeadRepositoryId != pins.RepositoryId ||
            !string.Equals(run.Path, $"{Exact20RecoveryPins.RecoveryWorkflowPath}", StringComparison.Ordinal) ||
            !string.Equals(run.Event, "workflow_dispatch", StringComparison.Ordinal) ||
            !string.Equals(run.Status, "in_progress", StringComparison.Ordinal) || run.Conclusion is not null ||
            !string.Equals(run.HeadBranch, "main", StringComparison.Ordinal) ||
            !string.Equals(run.HeadSha, execution.Commit, StringComparison.Ordinal) || run.Attempt != 1)
            throw new InvalidOperationException("GitHub's GET for the current run does not prove the exact main-only recovery workflow identity.");
    }

    private static void ValidateApproval(Exact20ApprovalComment comment, Exact20ExecutionContext execution, string currentMain, Exact20RecoveryPins pins)
    {
        if (comment.Id != execution.ApprovalCommentId || !string.Equals(comment.IssueUrl, Exact20RecoveryPins.ApprovalIssueUrl, StringComparison.Ordinal) ||
            !string.Equals(comment.Author, Exact20RecoveryPins.ApprovalAuthor, StringComparison.Ordinal) ||
            comment.CreatedAt != comment.UpdatedAt)
            throw new InvalidOperationException("The approval receipt is not an unchanged issue #2505 comment by the authorized repository owner.");

        var required = new[]
        {
            Exact20RecoveryPins.ApprovalLine,
            $"manifest_sha256={pins.ManifestSha256}",
            $"workflow_commit={currentMain}"
        };
        var lines = comment.Body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (!lines.SequenceEqual(required, StringComparer.Ordinal))
            throw new InvalidOperationException("The approval receipt must contain only the exact standalone approval, manifest hash, and current-main commit lines in order.");
    }

    private static void ValidateOriginalRun(Exact20WorkflowRun run, Exact20RecoveryPins pins)
    {
        if (run.Id != pins.RunId || run.WorkflowId != pins.WorkflowId || run.RepositoryId != pins.RepositoryId || run.HeadRepositoryId != pins.RepositoryId ||
            !string.Equals(run.Path, ".github/workflows/packages.yml", StringComparison.Ordinal) ||
            !string.Equals(run.Event, "push", StringComparison.Ordinal) ||
            !string.Equals(run.Status, "completed", StringComparison.Ordinal) ||
            !string.Equals(run.Conclusion, "cancelled", StringComparison.Ordinal) || run.Attempt != 1 ||
            !string.Equals(run.HeadBranch, "main", StringComparison.Ordinal) || !string.Equals(run.HeadSha, pins.SourceCommit, StringComparison.Ordinal))
            throw new InvalidOperationException("The original cancelled Packages run metadata does not match the pinned incident.");
    }

    private static void ValidateArtifact(Exact20Artifact artifact, Exact20RecoveryPins pins)
    {
        if (artifact.Id != pins.ArtifactId || artifact.RunId != pins.RunId || artifact.RepositoryId != pins.RepositoryId || artifact.HeadRepositoryId != pins.RepositoryId ||
            !string.Equals(artifact.Name, pins.ArtifactName, StringComparison.Ordinal) || artifact.Expired || artifact.ExpiresAt <= DateTimeOffset.UtcNow ||
            artifact.SizeInBytes <= 0 || artifact.SizeInBytes > MaximumArtifactBytes ||
            !string.Equals(artifact.Digest, $"sha256:{pins.ArtifactSha256}", StringComparison.Ordinal) ||
            !string.Equals(artifact.HeadBranch, "main", StringComparison.Ordinal) || !string.Equals(artifact.HeadSha, pins.SourceCommit, StringComparison.Ordinal) ||
            !string.Equals(artifact.ArchiveDownloadUrl, $"{Exact20RecoveryPins.RepositoryApi}actions/artifacts/{pins.ArtifactId}/zip", StringComparison.Ordinal))
            throw new InvalidOperationException("The original artifact metadata is expired or does not match the pinned name, run, repository, branch, source, and size.");
    }

    private static void RequireSameSnapshot(Exact20SafetySnapshot before, Exact20SafetySnapshot after, string message)
    {
        if (!string.Equals(before.MainSha, after.MainSha, StringComparison.Ordinal) ||
            !string.Equals(before.StateTip, after.StateTip, StringComparison.Ordinal) ||
            !string.Equals(before.StateSha256, after.StateSha256, StringComparison.Ordinal) ||
            !before.WorkflowStates.SequenceEqual(after.WorkflowStates, StringComparer.Ordinal) ||
            !before.RunCounts.Select(count => (count.IsInteger, count.Value)).SequenceEqual(after.RunCounts.Select(count => (count.IsInteger, count.Value))))
            throw new InvalidOperationException(message);
    }

    private static string BuildStateCommitMessage(IReadOnlyList<Exact20PackageSource> confirmed)
    {
        var message = new StringBuilder("Recover exact20 package publication state\n\n")
            .AppendLine("Incident: #2653 / #2505")
            .AppendLine($"Artifact: {Exact20RecoveryPins.Incident.ArtifactId}")
            .AppendLine($"Confirmed package count: {confirmed.Count}")
            .AppendLine("Packages:");
        foreach (var package in confirmed.OrderBy(package => package.PackageId, StringComparer.Ordinal))
            message.AppendLine($"- {package.PackageId} {package.Version} ({package.SourceCommit})");
        return message.ToString();
    }

    private static string SafeEntryName(string value) => value.Length > 80 ? value[..80] : value;

    private static string SafeFailure(Exception exception) => exception switch
    {
        OperationCanceledException => "The exact20 recovery operation was cancelled; no retry was attempted.",
        FeedException => "A Feedz read or write failed; response bodies are omitted.",
        HttpRequestException => "A fixed GitHub or Feedz endpoint did not answer; response bodies and signed URLs are omitted.",
        _ => exception.Message
    };

    private static bool IsPreflightFailure(Exception exception) => exception is InvalidOperationException or IOException or InvalidDataException or HttpRequestException or OperationCanceledException or FeedException or ArgumentException;

}

internal sealed record Exact20ExecutionContext(long RunId, string Commit, long ApprovalCommentId, string ManifestSha256);

internal sealed record Exact20SafetySnapshot(
    string MainSha,
    string StateTip,
    string StateSha256,
    IReadOnlyList<string> WorkflowStates,
    IReadOnlyList<Exact20RunCount> RunCounts,
    PublishedVersions Record);

internal sealed class PackageVersionComparer : IComparer<PackageVersionNumber>
{
    public static PackageVersionComparer Instance { get; } = new();

    public int Compare(PackageVersionNumber left, PackageVersionNumber right)
    {
        var numeric = left.CompareNumeric(right);
        if (numeric != 0)
            return numeric;
        if (left.Label is null)
            return right.Label is null ? 0 : 1;
        if (right.Label is null)
            return -1;

        var leftParts = left.Label.Split('.');
        var rightParts = right.Label.Split('.');
        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            var leftNumeric = IsNumericIdentifier(leftParts[index]);
            var rightNumeric = IsNumericIdentifier(rightParts[index]);
            var part = leftNumeric && rightNumeric ? CompareNumericIdentifier(leftParts[index], rightParts[index])
                : leftNumeric ? -1
                : rightNumeric ? 1
                : StringComparer.OrdinalIgnoreCase.Compare(leftParts[index], rightParts[index]);
            if (part != 0)
                return part;
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static bool IsNumericIdentifier(string value) => value.Length > 0 && value.All(character => character is >= '0' and <= '9');

    private static int CompareNumericIdentifier(string left, string right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');
        if (left.Length != right.Length)
            return left.Length.CompareTo(right.Length);
        return StringComparer.Ordinal.Compare(left, right);
    }
}

internal static class ZipCrcIndex
{
    private const uint EndSignature = 0x06054b50;
    private const uint EntrySignature = 0x02014b50;

    public static IReadOnlyDictionary<string, uint> Read(string path)
    {
        using var stream = File.OpenRead(path);
        var tailLength = (int)Math.Min(stream.Length, 65_557);
        var tail = new byte[tailLength];
        stream.Position = stream.Length - tailLength;
        stream.ReadExactly(tail);
        var end = -1;
        for (var index = tail.Length - 22; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index, 4)) == EndSignature)
            {
                end = index;
                break;
            }
        }

        if (end < 0)
            throw new InvalidDataException("A pinned zip archive has no end-of-central-directory record.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(end + 10, 2));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end + 12, 4));
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end + 16, 4));
        if (count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue || (long)offset + size > stream.Length)
            throw new InvalidDataException("A pinned zip archive uses unsupported ZIP64 directory metadata.");

        stream.Position = offset;
        var entries = new Dictionary<string, uint>(StringComparer.Ordinal);
        Span<byte> header = stackalloc byte[46];
        for (var index = 0; index < count; index++)
        {
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header[..4]) != EntrySignature)
                throw new InvalidDataException("A pinned zip archive has a malformed central directory.");
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(16, 4));
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(28, 2));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(30, 2));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(32, 2));
            var nameBytes = new byte[nameLength];
            stream.ReadExactly(nameBytes);
            if (nameBytes.Any(value => value > 0x7f))
                throw new InvalidDataException("A pinned zip archive has a non-ASCII entry name.");
            var name = Encoding.ASCII.GetString(nameBytes);
            stream.Seek(extraLength + commentLength, SeekOrigin.Current);
            if (!entries.TryAdd(name, crc))
                throw new InvalidDataException("A pinned zip archive has duplicate entry names.");
        }

        return entries;
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = MakeTable();
    public const uint Start = uint.MaxValue;

    public static uint Append(uint current, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
            current = Table[(current ^ value) & 0xff] ^ (current >> 8);
        return current;
    }

    public static uint Finish(uint current) => ~current;

    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) == 1 ? 0xedb88320 ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }
}
