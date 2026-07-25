using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Groundwork.Tools;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class CheckpointFenceEvidenceImporterTests
{
    private const string ProviderVersion = "0.0.1-preview.90";

    [Fact]
    public async Task Imports_only_the_complete_36_record_generation_without_advancing_statuses()
    {
        using var fixture = new ImportFixture();
        var statuses = fixture.Statuses();

        var result = await fixture.ImportAsync();

        var ledger = fixture.Ledger();
        var imported = fixture.CurrentGenerationRecords(ledger);
        Assert.Equal(36, result.ImportedRecordCount);
        Assert.Equal(
            $"versions/{ProviderVersion}/ledger-attachments/runtime-checkpoint-fence.json",
            result.AttachmentRelativePath);
        Assert.Equal(statuses, fixture.Statuses());
        Assert.Equal(36, imported.Length);
        Assert.All(imported, record =>
        {
            Assert.Equal(ProviderVersion, record["providerVersion"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(record["provenance"], fixture.ProvenanceNode()));
        });
        Assert.Equal(28, imported.Count(record => record["coverageEntryId"]!.GetValue<string>() == "runtime-checkpoint-commit"));
        Assert.Equal(4, imported.Count(record => record["coverageEntryId"]!.GetValue<string>() == "runtime-execution-liveness"));
        Assert.Equal(4, imported.Count(record => record["coverageEntryId"]!.GetValue<string>() == "runtime-post-commit-outbox"));
        Assert.Equal(ImportFixture.Historical80AttachmentSha256, FileSha256(fixture.Historical80AttachmentPath));
        Assert.Equal(ImportFixture.Historical81AttachmentSha256, FileSha256(fixture.Historical81AttachmentPath));
        Assert.Equal(ImportFixture.Historical86AttachmentSha256, FileSha256(fixture.Historical86AttachmentPath));
        Assert.Equal(ImportFixture.Historical88AttachmentSha256, FileSha256(fixture.Historical88AttachmentPath));
    }

    [Fact]
    public async Task Rejects_duplicate_tuple_keys_before_writing_the_generation()
    {
        using var fixture = new ImportFixture();
        var records = fixture.StagedRecords();
        records[^1] = records[0]!.DeepClone();
        fixture.WriteStagedRecords(records);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains("duplicate evidence tuple", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.DestinationGenerationPath));
    }

    [Fact]
    public async Task Rejects_a_missing_tuple_before_writing_the_generation()
    {
        using var fixture = new ImportFixture();
        var records = fixture.StagedRecords();
        records.RemoveAt(records.Count - 1);
        fixture.WriteStagedRecords(records);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains("requires exactly 36 records", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.DestinationGenerationPath));
    }

    [Fact]
    public async Task Rejects_reimporting_an_immutable_historical_generation()
    {
        using var fixture = new ImportFixture();
        foreach (var providerVersion in new[] { "0.0.1-preview.86", "0.0.1-preview.88" })
        {
            var request = fixture.Request with { ProviderVersion = providerVersion };

            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                CheckpointFenceEvidenceImporter.ImportAsync(request));

            Assert.Contains("retained historical generation", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Rejects_records_with_wrong_exact_source_provenance()
    {
        using var fixture = new ImportFixture();
        var records = fixture.StagedRecords();
        records[0]!["provenance"]!["elsaTree"] = new string('c', 40);
        fixture.WriteStagedRecords(records);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains("exact source commit, tree, and run identity", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.DestinationGenerationPath));
    }

    [Fact]
    public async Task Rejects_caller_provenance_that_does_not_match_the_checked_out_source_repository()
    {
        using var fixture = new ImportFixture();
        var request = fixture.Request with
        {
            Provenance = new CheckpointFenceEvidenceProvenance(
                new string('a', 40),
                new string('b', 40),
                "runtime-checkpoint-fence-preview90")
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CheckpointFenceEvidenceImporter.ImportAsync(request));

        Assert.Contains("exact checked-out source commit and tree", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_records_without_the_exact_provider_topology_and_independent_clients()
    {
        using var fixture = new ImportFixture();
        var records = fixture.StagedRecords();
        records[0]!["topology"] = "in-memory";
        records[0]!["clients"] = 1;
        fixture.WriteStagedRecords(records);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains("invalid provider topology", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.DestinationGenerationPath));
    }

    [Fact]
    public async Task Rejects_an_execution_path_synthesized_from_the_ledger_obligation()
    {
        using var fixture = new ImportFixture();
        var records = fixture.StagedRecords();
        records[0]!["executionPath"] =
            "provider-driver/sqlite/runtime-checkpoint-commit/974e1222ea1cded8";
        fixture.WriteStagedRecords(records);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains("executed source scenario, and physical target", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.DestinationGenerationPath));
    }

    [Fact]
    public async Task Rejects_artifact_hash_drift_before_writing_the_generation()
    {
        using var fixture = new ImportFixture();
        var record = fixture.StagedRecords()[0]!.AsObject();
        File.AppendAllText(fixture.StagingArtifactPath(record), "tampered");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains("artifact digest does not match", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.DestinationGenerationPath));
    }

    [Fact]
    public async Task Rejects_provider_divergence_even_when_each_record_and_artifact_is_internally_consistent()
    {
        using var fixture = new ImportFixture();
        var records = fixture.StagedRecords();
        var divergent = records
            .OfType<JsonObject>()
            .First(record =>
                record["provider"]!.GetValue<string>() == "mongodb" &&
                record["scenarioId"]!.GetValue<string>() == "concurrencySemantic:atomic-stale-fence-rejection");
        divergent["observations"]![0]!["value"] = "provider-divergence";
        divergent["resultHash"] = ResultHash(divergent);
        fixture.RewriteStagedArtifact(divergent);
        fixture.WriteStagedRecords(records);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains("non-equivalent provider result hashes", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.DestinationGenerationPath));
    }

    [Theory]
    [InlineData("0.0.1-preview.80")]
    [InlineData("0.0.1-preview.81")]
    [InlineData("0.0.1-preview.86")]
    [InlineData("0.0.1-preview.88")]
    public async Task Rejects_an_import_when_immutable_artifact_history_has_changed(string providerVersion)
    {
        using var fixture = new ImportFixture();
        File.AppendAllText(fixture.HistoricalArtifactPath(providerVersion), "tampered");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains($"Historical {providerVersion} artifact", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.DestinationGenerationPath));
    }

    [Theory]
    [InlineData("0.0.1-preview.80")]
    [InlineData("0.0.1-preview.81")]
    [InlineData("0.0.1-preview.86")]
    [InlineData("0.0.1-preview.88")]
    public async Task Rejects_an_import_when_immutable_attachment_history_has_changed(string providerVersion)
    {
        using var fixture = new ImportFixture();
        File.AppendAllText(fixture.HistoricalAttachmentPath(providerVersion), "tampered");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains($"Historical {providerVersion} attachment", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.DestinationGenerationPath));
    }

    [Fact]
    public async Task Rejects_staging_inside_the_tracked_evidence_tree()
    {
        using var fixture = new ImportFixture();
        var request = fixture.Request with { ExternalStagingRoot = fixture.EvidenceRoot };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CheckpointFenceEvidenceImporter.ImportAsync(request));

        Assert.Contains("external to the tracked", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_an_external_staging_symlink_that_resolves_into_the_tracked_evidence_tree()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new ImportFixture();
        var stagingLink = Path.Combine(fixture.Root, "staging-link");
        Directory.CreateSymbolicLink(stagingLink, fixture.EvidenceRoot);
        var request = fixture.Request with { ExternalStagingRoot = stagingLink };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CheckpointFenceEvidenceImporter.ImportAsync(request));

        Assert.Contains("external to the tracked", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_a_nested_staging_symlink_that_escapes_its_external_root()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new ImportFixture();
        var stagingRoot = Path.Combine(fixture.Root, "nested-link-staging");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateSymbolicLink(
            Path.Combine(stagingRoot, "versions"),
            Path.Combine(fixture.EvidenceRoot, "versions"));
        var request = fixture.Request with { ExternalStagingRoot = stagingRoot };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CheckpointFenceEvidenceImporter.ImportAsync(request));

        Assert.Contains("escapes its root", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recovers_the_exact_generation_left_after_the_post_move_crash_window()
    {
        using var fixture = new ImportFixture();
        fixture.SeedRecoverableDestination();
        Directory.Delete(fixture.StagingRoot, recursive: true);

        var result = await fixture.ImportAsync();

        Assert.Equal(36, result.ImportedRecordCount);
        Assert.Equal(36, fixture.CurrentGenerationRecords(fixture.Ledger()).Length);
    }

    private static string FileSha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string ResultHash(JsonObject record)
    {
        var observations = record["observations"]!.AsArray()
            .Select(candidate => candidate!.AsObject())
            .OrderBy(observation => observation["name"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(observation => observation["value"]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(observation =>
            {
                var name = observation["name"]!.GetValue<string>();
                var value = observation["value"]!.GetValue<string>();
                return $"{name.Length}:{name}{value.Length}:{value}";
            });
        var digestInput = string.Join(
            '\n',
            new[]
            {
                record["sourceScenarioId"]!.GetValue<string>(),
                record["coverageEntryId"]!.GetValue<string>(),
                record["outcome"]!.GetValue<string>(),
                record["failureWindow"]?.GetValue<string>() ?? "-"
            }.Concat(observations));
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(digestInput)));
    }

    private sealed class ImportFixture : IDisposable
    {
        public const string Historical80AttachmentSha256 =
            "b8fb7ce1faea246d3746c0c586b4e870d0309f17d84490e19a93b957600fac7c";
        public const string Historical81AttachmentSha256 =
            "ee6ea1c85dad6d1506abfbb7899ca73b33f52ae811fd35e254b0f9bce36ddf34";
        public const string Historical86AttachmentSha256 =
            "954a34a1bb3ce03881bedd167ba87c95d7d58d3f5abdb573e50e123361e0ef24";
        public const string Historical88AttachmentSha256 =
            "f0b40406e1e5a044bb8e83e6090c3eb84b676124674cd948ed2440f227b065f2";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public ImportFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"elsa-checkpoint-fence-import-{Guid.NewGuid():N}");
            SourceRepositoryRoot = Path.Combine(Root, "source-repository");
            EvidenceRoot = Path.Combine(SourceRepositoryRoot, "specs", "094-harden-groundwork-stores");
            StagingRoot = Path.Combine(Root, "external-staging");
            Directory.CreateDirectory(EvidenceRoot);
            Directory.CreateDirectory(StagingRoot);
            File.Copy(SourceLedgerPath, LedgerPath);
            var preImportLedger = Ledger();
            RemoveEvidenceGeneration(preImportLedger, ProviderVersion);
            preImportLedger["groundworkVersion"] = ProviderVersion;
            File.WriteAllText(LedgerPath, preImportLedger.ToJsonString(JsonOptions));
            CopyHistoricalGeneration(
                "ledger-attachments/runtime-checkpoint-fence.json",
                Historical80AttachmentSha256);
            CopyHistoricalGeneration(
                "versions/0.0.1-preview.81/ledger-attachments/runtime-checkpoint-fence.json",
                Historical81AttachmentSha256);
            CopyHistoricalGeneration(
                "versions/0.0.1-preview.86/ledger-attachments/runtime-checkpoint-fence.json",
                Historical86AttachmentSha256);
            CopyHistoricalGeneration(
                "versions/0.0.1-preview.88/ledger-attachments/runtime-checkpoint-fence.json",
                Historical88AttachmentSha256);
            InitializeSourceRepository();
            WriteStagingGeneration();
        }

        public string Root { get; }
        public string SourceRepositoryRoot { get; }
        public string EvidenceRoot { get; }
        public string StagingRoot { get; }
        public CheckpointFenceEvidenceProvenance Provenance { get; private set; } = null!;
        public string LedgerPath => Path.Combine(EvidenceRoot, "coverage-ledger.json");
        public string DestinationGenerationPath => Path.Combine(EvidenceRoot, "versions", ProviderVersion);
        public string Historical80AttachmentPath => Path.Combine(EvidenceRoot, "ledger-attachments", "runtime-checkpoint-fence.json");
        public string Historical81AttachmentPath => Path.Combine(
            EvidenceRoot,
            "versions",
            "0.0.1-preview.81",
            "ledger-attachments",
            "runtime-checkpoint-fence.json");
        public string Historical86AttachmentPath => Path.Combine(
            EvidenceRoot,
            "versions",
            "0.0.1-preview.86",
            "ledger-attachments",
            "runtime-checkpoint-fence.json");
        public string Historical88AttachmentPath => Path.Combine(
            EvidenceRoot,
            "versions",
            "0.0.1-preview.88",
            "ledger-attachments",
            "runtime-checkpoint-fence.json");
        private Dictionary<string, string> HistoricalArtifactPaths { get; } = new(StringComparer.Ordinal);
        public CheckpointFenceEvidenceImportRequest Request =>
            new(LedgerPath, StagingRoot, SourceRepositoryRoot, ProviderVersion, Provenance);

        public string HistoricalAttachmentPath(string providerVersion) => providerVersion switch
        {
            "0.0.1-preview.80" => Historical80AttachmentPath,
            "0.0.1-preview.81" => Historical81AttachmentPath,
            "0.0.1-preview.86" => Historical86AttachmentPath,
            "0.0.1-preview.88" => Historical88AttachmentPath,
            _ => throw new ArgumentOutOfRangeException(nameof(providerVersion), providerVersion, null)
        };

        public string HistoricalArtifactPath(string providerVersion) =>
            HistoricalArtifactPaths.TryGetValue(providerVersion, out var path)
                ? path
                : throw new InvalidOperationException($"No historical artifact was copied for '{providerVersion}'.");

        public Task<CheckpointFenceEvidenceImportResult> ImportAsync() =>
            CheckpointFenceEvidenceImporter.ImportAsync(Request);

        public JsonObject Ledger() => JsonNode.Parse(File.ReadAllText(LedgerPath))!.AsObject();

        public IReadOnlyDictionary<string, string> Statuses() =>
            Ledger()["entries"]!.AsArray()
                .OfType<JsonObject>()
                .ToDictionary(
                    entry => entry["id"]!.GetValue<string>(),
                    entry => entry["status"]!.GetValue<string>(),
                    StringComparer.Ordinal);

        public JsonObject[] CurrentGenerationRecords(JsonObject ledger) =>
            ledger["entries"]!.AsArray()
                .OfType<JsonObject>()
                .SelectMany(entry => entry["providerEvidence"]!.AsObject().SelectMany(provider =>
                    provider.Value!.AsArray().OfType<JsonObject>()))
                .Where(record => record["providerVersion"]!.GetValue<string>() == ProviderVersion)
                .ToArray();

        private static void RemoveEvidenceGeneration(JsonObject ledger, string providerVersion)
        {
            foreach (var providerRecords in ledger["entries"]!.AsArray()
                         .OfType<JsonObject>()
                         .SelectMany(entry => entry["providerEvidence"]!.AsObject())
                         .Select(provider => provider.Value!.AsArray()))
            {
                for (var index = providerRecords.Count - 1; index >= 0; index--)
                {
                    if (providerRecords[index]?["providerVersion"]?.GetValue<string>() == providerVersion)
                        providerRecords.RemoveAt(index);
                }
            }
        }

        public JsonArray StagedRecords() => JsonNode.Parse(File.ReadAllText(StagingAttachmentPath))!.AsArray();

        public void WriteStagedRecords(JsonArray records) =>
            File.WriteAllText(StagingAttachmentPath, JsonSerializer.Serialize(records, JsonOptions) + "\n");

        public string StagingArtifactPath(JsonObject record) => Path.Combine(
            StagingRoot,
            record["evidence"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));

        public void RewriteStagedArtifact(JsonObject record)
        {
            var artifactPath = StagingArtifactPath(record);
            File.WriteAllText(artifactPath, CheckpointFenceEvidenceImporter.ArtifactPayload(record).ToJsonString());
            record["evidenceSha256"] = FileSha256(artifactPath);
        }

        public JsonObject ProvenanceNode() => new()
        {
            ["elsaCommit"] = Provenance.ElsaCommit,
            ["elsaTree"] = Provenance.ElsaTree,
            ["runIdentity"] = Provenance.RunIdentity
        };

        public void SeedRecoverableDestination()
        {
            var source = Path.Combine(StagingRoot, "versions", ProviderVersion);
            foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(
                    DestinationGenerationPath,
                    Path.GetRelativePath(source, sourceFile));
                CopyFile(sourceFile, destination);
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private string StagingAttachmentPath => Path.Combine(
            StagingRoot,
            "versions",
            ProviderVersion,
            "ledger-attachments",
            "runtime-checkpoint-fence.json");

        private void CopyHistoricalGeneration(string attachmentRelativePath, string expectedAttachmentSha256)
        {
            var sourceAttachment = Path.Combine(SourceEvidenceRoot, attachmentRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var destinationAttachment = Path.Combine(EvidenceRoot, attachmentRelativePath.Replace('/', Path.DirectorySeparatorChar));
            CopyFile(sourceAttachment, destinationAttachment);
            Assert.Equal(expectedAttachmentSha256, FileSha256(destinationAttachment));

            foreach (var record in JsonNode.Parse(File.ReadAllText(sourceAttachment))!.AsArray().OfType<JsonObject>())
            {
                var evidence = record["evidence"]!.GetValue<string>();
                var sourceEvidence = Path.Combine(SourceEvidenceRoot, evidence.Replace('/', Path.DirectorySeparatorChar));
                var destinationEvidence = Path.Combine(EvidenceRoot, evidence.Replace('/', Path.DirectorySeparatorChar));
                CopyFile(sourceEvidence, destinationEvidence);
                var providerVersion = attachmentRelativePath switch
                {
                    var path when path.StartsWith("ledger-attachments/", StringComparison.Ordinal) =>
                        "0.0.1-preview.80",
                    var path when path.StartsWith("versions/0.0.1-preview.81/", StringComparison.Ordinal) =>
                        "0.0.1-preview.81",
                    var path when path.StartsWith("versions/0.0.1-preview.86/", StringComparison.Ordinal) =>
                        "0.0.1-preview.86",
                    var path when path.StartsWith("versions/0.0.1-preview.88/", StringComparison.Ordinal) =>
                        "0.0.1-preview.88",
                    _ => throw new InvalidOperationException(
                        $"Unknown historical attachment '{attachmentRelativePath}'.")
                };
                HistoricalArtifactPaths.TryAdd(providerVersion, destinationEvidence);
            }
        }

        private void WriteStagingGeneration()
        {
            var priorAttachment = Path.Combine(
                SourceEvidenceRoot,
                "versions",
                "0.0.1-preview.81",
                "ledger-attachments",
                "runtime-checkpoint-fence.json");
            var records = JsonNode.Parse(File.ReadAllText(priorAttachment))!.AsArray();
            foreach (var record in records.OfType<JsonObject>())
            {
                var provider = record["provider"]!.GetValue<string>();
                var entry = record["coverageEntryId"]!.GetValue<string>();
                var scenario = record["scenarioId"]!.GetValue<string>();
                var sourceScenario = record["sourceScenarioId"]!.GetValue<string>();
                var manifestFingerprint = record["manifestFingerprint"]!.GetValue<string>();
                record["providerVersion"] = ProviderVersion;
                record["provenance"] = ProvenanceNode();
                record["executionPath"] =
                    $"provider-driver/{provider}/{sourceScenario}/{manifestFingerprint[..16]}";
                record["evidence"] = $"versions/{ProviderVersion}/evidence/{provider}/{entry}/{ScenarioKey(scenario)}.json";
                record.Remove("nativeEvidence");
                record.Remove("nativeEvidenceSha256");

                var artifactPath = StagingArtifactPath(record);
                Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
                File.WriteAllText(artifactPath, CheckpointFenceEvidenceImporter.ArtifactPayload(record).ToJsonString());
                record["evidenceSha256"] = FileSha256(artifactPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(StagingAttachmentPath)!);
            File.WriteAllText(StagingAttachmentPath, JsonSerializer.Serialize(records, JsonOptions) + "\n");
        }

        private static string ScenarioKey(string scenarioId) =>
            Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scenarioId)))[..16];

        private static void CopyFile(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }

        private void InitializeSourceRepository()
        {
            RunGit("init", "--quiet");
            RunGit("config", "user.name", "Elsa Test");
            RunGit("config", "user.email", "elsa-test@example.invalid");
            RunGit("add", ".");
            RunGit("commit", "--quiet", "-m", "seed evidence");
            Provenance = new CheckpointFenceEvidenceProvenance(
                RunGit("rev-parse", "HEAD"),
                RunGit("rev-parse", "HEAD^{tree}"),
                "runtime-checkpoint-fence-preview90");
        }

        private string RunGit(params string[] arguments)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(SourceRepositoryRoot);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = System.Diagnostics.Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Could not start fixture Git process.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Fixture Git failed: {error.Trim()}");
            return output.Trim();
        }

        private static string SourceLedgerPath => Path.Combine(SourceEvidenceRoot, "coverage-ledger.json");

        private static string SourceEvidenceRoot => Path.Combine(
            RepoRoot,
            "specs",
            "094-harden-groundwork-stores");

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
    }
}
