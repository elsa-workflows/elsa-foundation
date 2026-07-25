using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
using Json.Schema;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentityProviderEvidenceTests
{
    private const string AcceptedEvidenceGroundworkVersion = "0.0.1-preview.60";
    private const string CurrentGroundworkVersion = "0.0.1-preview.86";
    private static readonly Lazy<JsonSchema> EvidenceSchema = new(() =>
        JsonSchema.FromText(File.ReadAllText(EvidenceSchemaPath(RepositoryRoot()))));

    [Fact]
    public void Checked_in_provider_artifacts_are_complete_sanitized_and_share_one_tested_code_candidate()
    {
        var repositoryRoot = RepositoryRoot();
        var directory = Path.Combine(
            repositoryRoot,
            "specs",
            "095-groundwork-aspnetcore-identity",
            "evidence",
            "providers");
        var generation = AspNetCoreIdentityEvidenceMatrixPublisher.ReadCurrentGeneration(directory);
        var artifactDirectory = Path.Combine(directory, "generations", generation);
        var artifacts = new[] { "sqlite", "sqlserver", "postgresql", "mongodb" }
            .Select(provider => ReadArtifact(Path.Combine(artifactDirectory, $"{provider}.json")))
            .ToArray();

        AspNetCoreIdentityProviderEvidenceValidator.ValidateMatrix(artifacts);
        var testedCommit = artifacts[0].TestedCandidate.CommitSha;
        Assert.Equal(
            artifacts[0].TestedCandidate.TreeSha,
            Git(repositoryRoot, "rev-parse", $"{testedCommit}^{{tree}}"));
        _ = Git(repositoryRoot, "merge-base", "--is-ancestor", testedCommit, "HEAD");
        foreach (var path in Directory.EnumerateFiles(artifactDirectory, "*.json"))
        {
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("\"nativePlan\":", json, StringComparison.Ordinal);
            Assert.DoesNotContain("runtimeInvocationFingerprint", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"routeValue\":", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"storageScope\":", json, StringComparison.Ordinal);
            Assert.DoesNotContain("candidateContentJson", json, StringComparison.Ordinal);
            Assert.DoesNotContain("connectionString", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("processId", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Sqlite_artifact_bundle_satisfies_the_closed_semantic_and_draft_202012_contracts()
    {
        // Retain this established test identity for the behavioral-baseline ratchet. The accepted
        // preview.60 artifact/schema contract is checked separately above; this is the live current-runtime smoke.
        await using var driver = new SqliteGroundworkProviderDriver();
        var acceptance = await new AspNetCoreIdentityProviderAcceptanceRunner(driver)
            .RunAsync(CancellationToken.None);

        AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactCoverage(acceptance.CompletedObjectiveIds);
        Assert.Equal(CurrentGroundworkVersion, driver.Descriptor.ProviderVersion);
    }

    [Theory]
    [InlineData(AspNetCoreIdentityEvidencePublicationCheckpoint.AfterStagingWritten)]
    [InlineData(AspNetCoreIdentityEvidencePublicationCheckpoint.AfterGenerationInstalled)]
    [InlineData(AspNetCoreIdentityEvidencePublicationCheckpoint.BeforePointerSwap)]
    public async Task Matrix_publication_failure_never_changes_the_active_generation(
        AspNetCoreIdentityEvidencePublicationCheckpoint failureCheckpoint)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-identity-evidence-{Guid.NewGuid():N}");
        try
        {
            var original = ProviderContents("original");
            await AspNetCoreIdentityEvidenceMatrixPublisher.PublishAsync(directory, original);
            var originalGeneration = AspNetCoreIdentityEvidenceMatrixPublisher.ReadCurrentGeneration(directory);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AspNetCoreIdentityEvidenceMatrixPublisher.PublishAsync(
                    directory,
                    ProviderContents("replacement"),
                    checkpoint =>
                    {
                        if (checkpoint == failureCheckpoint)
                            throw new InvalidOperationException("injected publication failure");
                    }));

            Assert.Equal(originalGeneration, AspNetCoreIdentityEvidenceMatrixPublisher.ReadCurrentGeneration(directory));
            foreach (var item in original)
                Assert.Equal(item.Value, File.ReadAllText(Path.Combine(directory, "generations", originalGeneration, $"{item.Key}.json")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [SkippableFact]
    public void Generate_all_preview60_provider_artifacts_only_after_the_complete_matrix_passes()
    {
        Skip.If(
            true,
            "The accepted preview.60 matrix is immutable historical evidence. " +
            "Generate current-generation evidence only in a dedicated evidence-regeneration work unit with a matching schema.");
    }

    private static IReadOnlyDictionary<string, string> ProviderContents(string value) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sqlite"] = $"sqlite-{value}",
            ["sqlserver"] = $"sqlserver-{value}",
            ["postgresql"] = $"postgresql-{value}",
            ["mongodb"] = $"mongodb-{value}"
        };

    private static AspNetCoreIdentityProviderEvidence ReadArtifact(string path)
    {
        Assert.True(File.Exists(path), $"Required provider artifact '{path}' does not exist.");
        var json = File.ReadAllText(path);
        ValidateJsonSchema(RepositoryRoot(), json, path);
        return JsonSerializer.Deserialize<AspNetCoreIdentityProviderEvidence>(
                   json,
                   AspNetCoreIdentityProviderEvidence.JsonOptions)
               ?? throw new InvalidOperationException($"Provider artifact '{path}' deserialized to null.");
    }

    private static void ValidateJsonSchema(string repositoryRoot, string json, string subject)
    {
        var schemaPath = EvidenceSchemaPath(repositoryRoot);
        using var document = JsonDocument.Parse(json);
        var result = EvidenceSchema.Value.Evaluate(document.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });
        if (!result.IsValid)
            throw new InvalidOperationException(
                $"Provider artifact '{subject}' does not conform to '{schemaPath}': " +
                JsonSerializer.Serialize(result));
    }

    private static string EvidenceSchemaPath(string repositoryRoot) => Path.Combine(
        repositoryRoot,
        "specs",
        "095-groundwork-aspnetcore-identity",
        "contracts",
        "provider-evidence.schema.json");

    private static string Git(string repositoryRoot, params string[] arguments) =>
        Process(repositoryRoot, "git", arguments);

    private static string Process(string workingDirectory, string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'{executable}' exited with code {process.ExitCode}: {standardError.Trim()}");
        return standardOutput.Trim();
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}

public enum AspNetCoreIdentityEvidencePublicationCheckpoint
{
    AfterStagingWritten,
    AfterGenerationInstalled,
    BeforePointerSwap
}

internal static class AspNetCoreIdentityEvidenceMatrixPublisher
{
    private static readonly string[] ProviderKeys = ["mongodb", "postgresql", "sqlite", "sqlserver"];
    private static readonly JsonSerializerOptions IndexJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task PublishAsync(
        string directory,
        IReadOnlyDictionary<string, string> providerContents,
        Action<AspNetCoreIdentityEvidencePublicationCheckpoint>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(providerContents);
        RequireProviderKeys(providerContents.Keys);
        var generation = ComputeGeneration(providerContents);
        var generationsDirectory = Path.Combine(directory, "generations");
        var generationDirectory = Path.Combine(generationsDirectory, generation);
        var stagingDirectory = Path.Combine(generationsDirectory, $".{generation}.{Guid.NewGuid():N}.staging");
        var currentPath = Path.Combine(directory, "current.json");
        var pendingPointerPath = Path.Combine(directory, $".current.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(generationsDirectory);
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            foreach (var item in providerContents.OrderBy(item => item.Key, StringComparer.Ordinal))
                await File.WriteAllTextAsync(
                    Path.Combine(stagingDirectory, $"{item.Key}.json"),
                    item.Value,
                    new UTF8Encoding(false));
            checkpoint?.Invoke(AspNetCoreIdentityEvidencePublicationCheckpoint.AfterStagingWritten);

            if (Directory.Exists(generationDirectory))
            {
                EnsureGenerationMatches(generationDirectory, providerContents);
                Directory.Delete(stagingDirectory, recursive: true);
            }
            else
                Directory.Move(stagingDirectory, generationDirectory);
            checkpoint?.Invoke(AspNetCoreIdentityEvidencePublicationCheckpoint.AfterGenerationInstalled);

            var index = new AspNetCoreIdentityEvidenceGenerationIndex(1, generation);
            await File.WriteAllTextAsync(
                pendingPointerPath,
                JsonSerializer.Serialize(index, IndexJsonOptions) + Environment.NewLine,
                new UTF8Encoding(false));
            checkpoint?.Invoke(AspNetCoreIdentityEvidencePublicationCheckpoint.BeforePointerSwap);
            File.Move(pendingPointerPath, currentPath, overwrite: true);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
            if (File.Exists(pendingPointerPath))
                File.Delete(pendingPointerPath);
        }
    }

    public static string ReadCurrentGeneration(string directory)
    {
        var currentPath = Path.Combine(directory, "current.json");
        if (!File.Exists(currentPath))
            throw new InvalidOperationException($"Identity provider evidence pointer '{currentPath}' does not exist.");
        var index = JsonSerializer.Deserialize<AspNetCoreIdentityEvidenceGenerationIndex>(
                        File.ReadAllText(currentPath),
                        IndexJsonOptions)
                    ?? throw new InvalidOperationException($"Identity provider evidence pointer '{currentPath}' is empty.");
        if (index.ArtifactSchemaVersion != 1 || !IsSha256(index.Generation))
            throw new InvalidOperationException($"Identity provider evidence pointer '{currentPath}' is invalid.");
        var generationDirectory = Path.Combine(directory, "generations", index.Generation);
        if (!Directory.Exists(generationDirectory))
            throw new InvalidOperationException($"Identity provider evidence generation '{generationDirectory}' does not exist.");
        RequireProviderKeys(Directory.EnumerateFiles(generationDirectory, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)));
        return index.Generation;
    }

    private static string ComputeGeneration(IReadOnlyDictionary<string, string> providerContents)
    {
        var canonical = string.Join('\n', providerContents
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(item.Value)))}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void EnsureGenerationMatches(
        string generationDirectory,
        IReadOnlyDictionary<string, string> providerContents)
    {
        RequireProviderKeys(Directory.EnumerateFiles(generationDirectory, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)));
        foreach (var item in providerContents)
        {
            var path = Path.Combine(generationDirectory, $"{item.Key}.json");
            if (!File.Exists(path) || File.ReadAllText(path) != item.Value)
                throw new InvalidOperationException($"Existing Identity evidence generation '{generationDirectory}' is not immutable.");
        }
    }

    private static void RequireProviderKeys(IEnumerable<string> actual)
    {
        if (!ProviderKeys.Order(StringComparer.Ordinal).SequenceEqual(actual.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException("Identity evidence publication requires the exact four-provider catalog.");
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
}

internal sealed record AspNetCoreIdentityEvidenceGenerationIndex(int ArtifactSchemaVersion, string Generation);

internal sealed record AspNetCoreIdentityProviderEvidence(
    int ArtifactSchemaVersion,
    string SourceAuthority,
    AspNetCoreIdentityTestedCandidate TestedCandidate,
    AspNetCoreIdentityProviderIdentityEvidence Provider,
    AspNetCoreIdentityAcceptanceEvidence Acceptance,
    AspNetCoreIdentityNativeRoutesEvidence NativeRoutes,
    AspNetCoreIdentitySchemaArtifactEvidence Schema,
    AspNetCoreIdentityWorkloadEvidence Workload)
{
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static AspNetCoreIdentityProviderEvidence Create(
        AspNetCoreIdentityTestedCandidate candidate,
        GroundworkProviderDescriptor descriptor,
        string processLaunchFingerprint,
        AspNetCoreIdentityProviderAcceptanceResult acceptance,
        string diagnosticsSha256,
        string? topologyRejectionSha256,
        string? compoundKeyBudgetSha256,
        AspNetCoreIdentityNativePlanEvidence native,
        AspNetCoreIdentitySchemaEvidence schema) =>
        new(
            1,
            "#644",
            candidate with { GroundworkProviderAssemblyVersion = descriptor.ProviderVersion },
            new AspNetCoreIdentityProviderIdentityEvidence(
                descriptor.ProviderKey,
                descriptor.ProviderIdentity,
                descriptor.Topology.Description,
                CapabilityNames(descriptor.Topology.Capabilities),
                diagnosticsSha256,
                topologyRejectionSha256,
                compoundKeyBudgetSha256),
            new AspNetCoreIdentityAcceptanceEvidence(
                acceptance.CompletedObjectiveIds.Count,
                acceptance.CompletedObjectiveIds,
                acceptance.ObjectiveResultDigest,
                AspNetCoreIdentityProviderAcceptanceCatalog.AdvertisedFrameworkCapabilityIds.Count,
                AspNetCoreIdentityProviderAcceptanceCatalog.AdvertisedFrameworkCapabilityIds,
                acceptance.FindProcessId > 0 &&
                acceptance.DuplicateProcessId > 0 &&
                acceptance.FindProcessId != Environment.ProcessId &&
                acceptance.DuplicateProcessId != Environment.ProcessId,
                processLaunchFingerprint),
            new AspNetCoreIdentityNativeRoutesEvidence(
                native.Routes.Count,
                native.Routes.Select(route => route.Request.PhysicalName).Distinct(StringComparer.Ordinal).Count(),
                native.Routes.Select(route => route.PhysicalCardinality).Distinct().Single(),
                NativeRouteArtifacts(native.Routes)),
            new AspNetCoreIdentitySchemaArtifactEvidence(
                schema.CompositionFingerprint,
                schema.TargetFingerprint,
                new AspNetCoreIdentitySchemaStateArtifact(
                    schema.PendingState.Fingerprint,
                    schema.PendingState.CanonicalItemCount),
                new AspNetCoreIdentitySchemaStateArtifact(
                    schema.AppliedState.Fingerprint,
                    schema.AppliedState.CanonicalItemCount),
                schema.ToolReceipts,
                schema.PendingAdmission,
                schema.ReadyAdmission),
            new AspNetCoreIdentityWorkloadEvidence(
                "1.1.0",
                native.Workload.WorkloadId,
                native.Workload.ScenarioId,
                native.Workload.InputFingerprint,
                native.Workload.ResultDigest,
                native.Workload.NativeRouteEvidence.Count));

    private static IReadOnlyList<AspNetCoreIdentityNativeRouteArtifact> NativeRouteArtifacts(
        IReadOnlyCollection<GroundworkNativeRoutePlanResult> routes)
    {
        var catalog = AspNetCoreIdentityNativePlanTests.EvidenceRouteCatalog()
            .ToDictionary(route => route.QueryIdentity, StringComparer.Ordinal);
        return routes
            .OrderBy(route => route.Request.QueryIdentity, StringComparer.Ordinal)
            .Select(route =>
            {
                var contract = catalog[route.Request.QueryIdentity];
                return new AspNetCoreIdentityNativeRouteArtifact(
                    route.Request.QueryIdentity,
                    route.Request.DocumentKind,
                    route.Request.PhysicalName,
                    route.PhysicalCardinality,
                    route.FiniteLimit!.Value,
                    route.MaterializedCandidateCount,
                    contract.IndexLogicalName,
                    route.IndexName,
                    route.PlanClassification,
                    route.HasStorageScopePredicate,
                    route.HasRoutePredicate,
                    route.Evidence.Sha256,
                    route.Commands.Select(command => new AspNetCoreIdentityNativeCommandArtifact(
                        command.Ordinal,
                        command.Kind.ToString(),
                        command.Identity,
                        command.NativePlanFormat,
                        command.PlanClassification,
                        command.IndexNames,
                        command.PredicateFieldIdentifiers,
                        command.NativePlanSha256)).ToArray());
            })
            .ToArray();
    }

    private static IReadOnlyList<string> CapabilityNames(GroundworkTopologyCapabilities capabilities) =>
        Enum.GetValues<GroundworkTopologyCapabilities>()
            .Where(capability => capability != GroundworkTopologyCapabilities.None && capabilities.HasFlag(capability))
            .Select(capability => capability.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();
}

internal sealed record AspNetCoreIdentityTestedCandidate(
    string CommitSha,
    string TreeSha,
    string GroundworkPackageFamilyVersion,
    string GroundworkToolVersion,
    string IdentityManifestVersion)
{
    public string? GroundworkProviderAssemblyVersion { get; init; }
}

internal sealed record AspNetCoreIdentityProviderIdentityEvidence(
    string Key,
    string Identity,
    string Topology,
    IReadOnlyList<string> Capabilities,
    string DiagnosticsSha256,
    string? TopologyRejectionSha256,
    string? CompoundKeyBudgetSha256);

internal sealed record AspNetCoreIdentityAcceptanceEvidence(
    int ObjectiveCount,
    IReadOnlyList<string> ObjectiveIds,
    string ObjectiveResultDigest,
    int AdvertisedCapabilityCount,
    IReadOnlyList<string> AdvertisedCapabilityIds,
    bool ExternalProcessesConfirmed,
    string ProcessLaunchFingerprint);

internal sealed record AspNetCoreIdentityNativeRoutesEvidence(
    int RouteCount,
    int PhysicalTableCount,
    long CardinalityPerPhysicalTable,
    IReadOnlyList<AspNetCoreIdentityNativeRouteArtifact> Routes);

internal sealed record AspNetCoreIdentityNativeRouteArtifact(
    string QueryIdentity,
    string DocumentKind,
    string PhysicalName,
    long PhysicalCardinality,
    int FiniteLimit,
    int MaterializedCandidateCount,
    string IndexLogicalName,
    string IndexName,
    string PlanClassification,
    bool StorageScopePredicate,
    bool RoutePredicate,
    string EvidenceSha256,
    IReadOnlyList<AspNetCoreIdentityNativeCommandArtifact> Commands);

internal sealed record AspNetCoreIdentityNativeCommandArtifact(
    int Ordinal,
    string Kind,
    string Identity,
    string NativePlanFormat,
    string PlanClassification,
    IReadOnlyList<string> IndexNames,
    IReadOnlyList<string> PredicateFieldIdentifiers,
    string NativePlanSha256);

internal sealed record AspNetCoreIdentitySchemaArtifactEvidence(
    string CompositionFingerprint,
    string TargetFingerprint,
    AspNetCoreIdentitySchemaStateArtifact PendingState,
    AspNetCoreIdentitySchemaStateArtifact AppliedState,
    IReadOnlyList<AspNetCoreIdentitySchemaToolReceipt> ToolReceipts,
    AspNetCoreIdentityRuntimeAdmissionEvidence PendingAdmission,
    AspNetCoreIdentityRuntimeAdmissionEvidence ReadyAdmission);

internal sealed record AspNetCoreIdentitySchemaStateArtifact(string Fingerprint, int CanonicalItemCount);

internal sealed record AspNetCoreIdentityWorkloadEvidence(
    string Version,
    string WorkloadId,
    string ScenarioId,
    string InputFingerprint,
    string ResultDigest,
    int NativeRouteReferenceCount);

internal static class AspNetCoreIdentityProviderEvidenceValidator
{
    private const string AcceptedEvidenceGroundworkVersion = "0.0.1-preview.60";
    private const string AcceptedEvidenceIdentityManifestVersion = "1.0.4";
    private static readonly IReadOnlyDictionary<string, ProviderContract> ProviderCatalog =
        new Dictionary<string, ProviderContract>(StringComparer.Ordinal)
        {
            ["sqlite"] = new(
                "groundwork-sqlite",
                "file-backed-distinct-connections",
                ["ExternalProcessRestart", "IndependentClients", "MultiDocumentTransactions", "PersistentStorage"],
                "sqlite-query-plan",
                "index-search",
                ["index-search"],
                RequiresTopologyRejection: false,
                RequiresCompoundKeyBudget: false),
            ["sqlserver"] = new(
                "groundwork-sqlserver",
                "real-sqlserver-container",
                ["ExternalProcessRestart", "IndependentClients", "MultiDocumentTransactions", "PersistentStorage"],
                "sqlserver-statistics-xml",
                "index-seek",
                ["index-seek"],
                RequiresTopologyRejection: false,
                RequiresCompoundKeyBudget: true),
            ["postgresql"] = new(
                "groundwork-postgresql",
                "real-postgresql-container",
                ["ExternalProcessRestart", "IndependentClients", "MultiDocumentTransactions", "PersistentStorage"],
                "postgresql-json",
                "indexed",
                ["index-only-scan", "index-scan"],
                RequiresTopologyRejection: false,
                RequiresCompoundKeyBudget: false),
            ["mongodb"] = new(
                "groundwork-mongodb",
                "transaction-capable-replica-set",
                ["ExternalProcessRestart", "IndependentClients", "MultiDocumentTransactions", "PersistentStorage", "TransactionCapableMongoTopology"],
                "mongodb-json",
                "index-scan",
                ["index-scan"],
                RequiresTopologyRejection: true,
                RequiresCompoundKeyBudget: false)
        };

    private static readonly (string Operation, int ExitCode, string? Outcome)[] SchemaReceiptCatalog =
    [
        ("validate-offline", 0, null),
        ("validate-pending", 0, "ready"),
        ("plan-pending", 2, "pending"),
        ("status-pending", 2, "pending"),
        ("apply-safe", 0, "applied"),
        ("validate-ready", 0, "ready"),
        ("status-ready", 0, "ready")
    ];

    public static void ValidateMatrix(IReadOnlyCollection<AspNetCoreIdentityProviderEvidence> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        RequireExact("provider keys", ProviderCatalog.Keys, artifacts.Select(artifact => artifact.Provider.Key));
        RequireSingle("candidate commit", artifacts.Select(artifact => artifact.TestedCandidate.CommitSha));
        RequireSingle("candidate tree", artifacts.Select(artifact => artifact.TestedCandidate.TreeSha));
        RequireSingle("objective digest", artifacts.Select(artifact => artifact.Acceptance.ObjectiveResultDigest));
        RequireSingle("workload input fingerprint", artifacts.Select(artifact => artifact.Workload.InputFingerprint));
        RequireSingle("workload result digest", artifacts.Select(artifact => artifact.Workload.ResultDigest));
        foreach (var artifact in artifacts)
            Validate(artifact);
    }

    public static void Validate(AspNetCoreIdentityProviderEvidence artifact)
    {
        if (artifact.ArtifactSchemaVersion != 1 || artifact.SourceAuthority != "#644")
            throw new InvalidOperationException("Identity provider evidence has an unsupported authority or schema version.");
        if (artifact.TestedCandidate.GroundworkPackageFamilyVersion != AcceptedEvidenceGroundworkVersion ||
            artifact.TestedCandidate.GroundworkProviderAssemblyVersion != AcceptedEvidenceGroundworkVersion ||
            artifact.TestedCandidate.GroundworkToolVersion != AcceptedEvidenceGroundworkVersion ||
            artifact.TestedCandidate.IdentityManifestVersion != AcceptedEvidenceIdentityManifestVersion)
            throw new InvalidOperationException("Identity provider evidence versions are not aligned to the accepted candidate.");
        RequireGitObjectId("candidate commit", artifact.TestedCandidate.CommitSha);
        RequireGitObjectId("candidate tree", artifact.TestedCandidate.TreeSha);
        ValidateProvider(artifact.Provider);
        RequireSha("objective result", artifact.Acceptance.ObjectiveResultDigest);
        if (artifact.Acceptance.ObjectiveCount != artifact.Acceptance.ObjectiveIds.Count ||
            artifact.Acceptance.ObjectiveCount != 25 ||
            artifact.Acceptance.AdvertisedCapabilityCount != artifact.Acceptance.AdvertisedCapabilityIds.Count ||
            artifact.Acceptance.AdvertisedCapabilityCount != 15)
            throw new InvalidOperationException("Identity provider evidence does not cover the exact acceptance denominator.");
        AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactCoverage(artifact.Acceptance.ObjectiveIds);
        AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactAdvertisedFrameworkCapabilities(
            artifact.Acceptance.AdvertisedCapabilityIds);
        if (artifact.Acceptance.ObjectiveResultDigest !=
            AspNetCoreIdentityProviderAcceptanceCatalog.ComputeObjectiveResultDigest(artifact.Acceptance.ObjectiveIds))
            throw new InvalidOperationException("Identity provider objective digest does not match the exact result catalog.");
        if (!artifact.Acceptance.ExternalProcessesConfirmed)
            throw new InvalidOperationException("Identity provider evidence did not confirm external restart probes.");
        RequireSha("process launch", artifact.Acceptance.ProcessLaunchFingerprint);
        ValidateNativeRoutes(artifact.Provider.Key, artifact.NativeRoutes);
        ValidateSchema(artifact.Schema);
        if (artifact.Workload.Version != "1.1.0" ||
            artifact.Workload.WorkloadId != AspNetCoreIdentityPerformanceWorkload.WorkloadId ||
            artifact.Workload.ScenarioId != AspNetCoreIdentityPerformanceWorkload.ScenarioId ||
            artifact.Workload.InputFingerprint != AspNetCoreIdentityObservableScenario.ExpectedInputFingerprint ||
            artifact.Workload.ResultDigest != AspNetCoreIdentityObservableScenario.ExpectedResultDigest ||
            artifact.Workload.NativeRouteReferenceCount != AspNetCoreIdentityPerformanceWorkload.RequiredNativeRoutes.Length)
            throw new InvalidOperationException("Identity provider workload evidence does not match the v1.1 contract.");
    }

    private static void ValidateProvider(AspNetCoreIdentityProviderIdentityEvidence provider)
    {
        if (!ProviderCatalog.TryGetValue(provider.Key, out var contract) ||
            provider.Identity != contract.Identity ||
            provider.Topology != contract.Topology)
            throw new InvalidOperationException($"Provider '{provider.Key}' does not match the closed Identity evidence catalog.");
        RequireExact($"provider '{provider.Key}' capabilities", contract.Capabilities, provider.Capabilities);
        RequireSha($"provider '{provider.Key}' diagnostics", provider.DiagnosticsSha256);
        RequireConditionalSha(
            $"provider '{provider.Key}' topology rejection",
            provider.TopologyRejectionSha256,
            contract.RequiresTopologyRejection);
        RequireConditionalSha(
            $"provider '{provider.Key}' compound key budget",
            provider.CompoundKeyBudgetSha256,
            contract.RequiresCompoundKeyBudget);
    }

    private static void ValidateNativeRoutes(
        string providerKey,
        AspNetCoreIdentityNativeRoutesEvidence nativeRoutes)
    {
        var provider = ProviderCatalog[providerKey];
        var routeCatalog = AspNetCoreIdentityNativePlanTests.EvidenceRouteCatalog();
        RequireExact("native route identities", routeCatalog.Select(route => route.QueryIdentity), nativeRoutes.Routes.Select(route => route.QueryIdentity));
        var distinctPhysicalNames = nativeRoutes.Routes
            .Select(route => route.PhysicalName)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (nativeRoutes.RouteCount != nativeRoutes.Routes.Count ||
            nativeRoutes.RouteCount != routeCatalog.Count ||
            nativeRoutes.PhysicalTableCount != distinctPhysicalNames ||
            nativeRoutes.PhysicalTableCount != 6 ||
            nativeRoutes.CardinalityPerPhysicalTable != 100_000)
            throw new InvalidOperationException("Identity provider native evidence does not derive the exact 10-route, 6-table, 100k denominator.");

        foreach (var contract in routeCatalog)
        {
            var route = nativeRoutes.Routes.Single(candidate => candidate.QueryIdentity == contract.QueryIdentity);
            if (route.DocumentKind != contract.DocumentKind ||
                route.PhysicalName != contract.PhysicalName ||
                route.FiniteLimit != contract.Limit ||
                route.IndexLogicalName != contract.IndexLogicalName ||
                route.PhysicalCardinality != nativeRoutes.CardinalityPerPhysicalTable ||
                route.MaterializedCandidateCount != 1 ||
                route.PlanClassification != provider.AggregatePlanClassification ||
                !route.StorageScopePredicate ||
                !route.RoutePredicate ||
                route.Commands.Count != 2)
                throw new InvalidOperationException($"Identity provider route '{route.QueryIdentity}' does not match its closed contract.");
            RequireProviderIdentifier($"route '{route.QueryIdentity}' index", route.IndexName);
            RequireSha($"route '{route.QueryIdentity}' evidence", route.EvidenceSha256);
            ValidateCommand(route, contract, provider, route.Commands[0], 0, "Count", PhysicalDocumentQueryCommandIdentities.Count);
            ValidateCommand(route, contract, provider, route.Commands[1], 1, "Page", PhysicalDocumentQueryCommandIdentities.Page);
        }
    }

    private static void ValidateCommand(
        AspNetCoreIdentityNativeRouteArtifact route,
        AspNetCoreIdentityNativeRouteContract contract,
        ProviderContract provider,
        AspNetCoreIdentityNativeCommandArtifact command,
        int ordinal,
        string kind,
        string identity)
    {
        if (command.Ordinal != ordinal ||
            command.Kind != kind ||
            command.Identity != identity ||
            command.NativePlanFormat != provider.NativePlanFormat ||
            !provider.CommandPlanClassifications.Contains(command.PlanClassification, StringComparer.Ordinal))
            throw new InvalidOperationException($"Route '{route.QueryIdentity}' has invalid command metadata at ordinal {ordinal}.");
        RequireExact($"route '{route.QueryIdentity}' command indexes", [route.IndexName], command.IndexNames);
        RequireExact(
            $"route '{route.QueryIdentity}' command predicates",
            ["document_kind", "storage_scope", contract.Field],
            command.PredicateFieldIdentifiers);
        RequireSha($"route '{route.QueryIdentity}' native plan", command.NativePlanSha256);
    }

    private static void ValidateSchema(AspNetCoreIdentitySchemaArtifactEvidence schema)
    {
        RequireSha("composition", schema.CompositionFingerprint);
        RequireSha("target", schema.TargetFingerprint);
        RequireSha("pending state", schema.PendingState.Fingerprint);
        RequireSha("applied state", schema.AppliedState.Fingerprint);
        if (schema.PendingState.Fingerprint == schema.AppliedState.Fingerprint ||
            schema.PendingState.CanonicalItemCount < 0 ||
            schema.AppliedState.CanonicalItemCount <= schema.PendingState.CanonicalItemCount ||
            schema.ToolReceipts.Count != SchemaReceiptCatalog.Length)
            throw new InvalidOperationException("Identity provider schema state does not prove a physical apply transition.");

        for (var index = 0; index < SchemaReceiptCatalog.Length; index++)
        {
            var expected = SchemaReceiptCatalog[index];
            var receipt = schema.ToolReceipts[index];
            RequireSha($"schema receipt '{receipt.Operation}' before", receipt.BeforeFingerprint);
            RequireSha($"schema receipt '{receipt.Operation}' after", receipt.AfterFingerprint);
            if (receipt.Operation != expected.Operation ||
                receipt.ExitCode != expected.ExitCode ||
                (expected.Outcome is not null && receipt.Outcome != expected.Outcome))
                throw new InvalidOperationException($"Schema receipt at ordinal {index} does not match '{expected.Operation}'.");

            var isApply = receipt.Operation == "apply-safe";
            var expectedState = index < 4 ? schema.PendingState.Fingerprint : schema.AppliedState.Fingerprint;
            if (isApply)
            {
                if (!receipt.TargetMutated || receipt.StateUnchanged ||
                    receipt.BeforeFingerprint != schema.PendingState.Fingerprint ||
                    receipt.AfterFingerprint != schema.AppliedState.Fingerprint)
                    throw new InvalidOperationException("Schema apply receipt does not bind the pending and applied states.");
            }
            else if (receipt.TargetMutated || !receipt.StateUnchanged ||
                     receipt.BeforeFingerprint != expectedState || receipt.AfterFingerprint != expectedState)
                throw new InvalidOperationException($"Schema read-only receipt '{receipt.Operation}' mutated or drifted state.");
        }

        RequireSha("pending admission target", schema.PendingAdmission.TargetFingerprint);
        if (schema.PendingAdmission.AppliedTargetFingerprint is { } pendingAppliedTarget)
            RequireSha("pending admission applied target", pendingAppliedTarget);
        RequireSha("ready admission target", schema.ReadyAdmission.TargetFingerprint);
        if (schema.ReadyAdmission.AppliedTargetFingerprint is not { } readyAppliedTarget)
            throw new InvalidOperationException("Ready schema admission is missing its applied target fingerprint.");
        RequireSha("ready admission applied target", readyAppliedTarget);
        if (schema.ToolReceipts[0].InspectionMode != "offline" ||
            schema.PendingAdmission.IsReady ||
            schema.PendingAdmission.PendingOperationCount <= 0 ||
            schema.PendingAdmission.TargetFingerprint != schema.TargetFingerprint ||
            schema.PendingAdmission.AppliedTargetFingerprint == schema.TargetFingerprint ||
            !schema.ReadyAdmission.IsReady ||
            schema.ReadyAdmission.PendingOperationCount != 0 ||
            schema.ReadyAdmission.TargetFingerprint != schema.TargetFingerprint ||
            schema.ReadyAdmission.AppliedTargetFingerprint != schema.TargetFingerprint)
            throw new InvalidOperationException("Identity provider runtime admission evidence does not bind pending and ready schema states.");
    }

    private static void RequireSha(string subject, string value)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"{subject} is not a SHA-256 digest.");
    }

    private static void RequireConditionalSha(string subject, string? value, bool required)
    {
        if (!required)
        {
            if (value is not null)
                throw new InvalidOperationException($"{subject} is forbidden for this provider.");
            return;
        }
        if (value is null)
            throw new InvalidOperationException($"{subject} is required for this provider.");
        RequireSha(subject, value);
    }

    private static void RequireProviderIdentifier(string subject, string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !char.IsAsciiLetter(value[0]) && value[0] != '_' ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.' or '-')))
            throw new InvalidOperationException($"{subject} is not a safe provider identifier.");
    }

    private static void RequireGitObjectId(string subject, string value)
    {
        if (value.Length is not (40 or 64) || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"{subject} is not a Git object id.");
    }

    private static void RequireSingle(string subject, IEnumerable<string> values)
    {
        if (values.Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidOperationException($"Provider evidence does not share one {subject}.");
    }

    private static void RequireExact(string subject, IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var expectedValues = expected.Order(StringComparer.Ordinal).ToArray();
        var actualValues = actual.Order(StringComparer.Ordinal).ToArray();
        if (!expectedValues.SequenceEqual(actualValues, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Provider evidence {subject} do not match the required catalog: " +
                $"expected=[{string.Join(", ", expectedValues)}], actual=[{string.Join(", ", actualValues)}].");
    }

    private sealed record ProviderContract(
        string Identity,
        string Topology,
        IReadOnlyList<string> Capabilities,
        string NativePlanFormat,
        string AggregatePlanClassification,
        IReadOnlyList<string> CommandPlanClassifications,
        bool RequiresTopologyRejection,
        bool RequiresCompoundKeyBudget);
}
