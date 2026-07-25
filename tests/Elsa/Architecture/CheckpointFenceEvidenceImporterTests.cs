using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Groundwork.Tools;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class CheckpointFenceEvidenceImporterTests
{
    private const string ProviderVersion = "0.0.1-preview.86";
    private static readonly CheckpointFenceEvidenceProvenance Provenance = new(
        "8468976be63b75d8c238b635264a78e055abd66d",
        "39cd8a893ad5466f40c6e7d2131524703f1f0eba",
        "runtime-checkpoint-fence-preview86");

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
            Assert.True(JsonNode.DeepEquals(record["provenance"], ProvenanceNode()));
        });
        Assert.Equal(28, imported.Count(record => record["coverageEntryId"]!.GetValue<string>() == "runtime-checkpoint-commit"));
        Assert.Equal(4, imported.Count(record => record["coverageEntryId"]!.GetValue<string>() == "runtime-execution-liveness"));
        Assert.Equal(4, imported.Count(record => record["coverageEntryId"]!.GetValue<string>() == "runtime-post-commit-outbox"));
        Assert.Equal(ImportFixture.Historical80AttachmentSha256, FileSha256(fixture.Historical80AttachmentPath));
        Assert.Equal(ImportFixture.Historical81AttachmentSha256, FileSha256(fixture.Historical81AttachmentPath));
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
    public async Task Rejects_an_import_when_immutable_preview80_or_preview81_history_has_changed()
    {
        using var fixture = new ImportFixture();
        File.AppendAllText(fixture.Historical80ArtifactPath, "tampered");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());

        Assert.Contains("Historical artifact", exception.Message, StringComparison.Ordinal);
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

    private static string FileSha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static JsonObject ProvenanceNode() => new()
    {
        ["elsaCommit"] = Provenance.ElsaCommit,
        ["elsaTree"] = Provenance.ElsaTree,
        ["runIdentity"] = Provenance.RunIdentity
    };

    private sealed class ImportFixture : IDisposable
    {
        public const string Historical80AttachmentSha256 =
            "b8fb7ce1faea246d3746c0c586b4e870d0309f17d84490e19a93b957600fac7c";
        public const string Historical81AttachmentSha256 =
            "ee6ea1c85dad6d1506abfbb7899ca73b33f52ae811fd35e254b0f9bce36ddf34";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public ImportFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"elsa-checkpoint-fence-import-{Guid.NewGuid():N}");
            EvidenceRoot = Path.Combine(Root, "target", "specs", "094-harden-groundwork-stores");
            StagingRoot = Path.Combine(Root, "external-staging");
            Directory.CreateDirectory(EvidenceRoot);
            Directory.CreateDirectory(StagingRoot);
            File.Copy(SourceLedgerPath, LedgerPath);
            CopyHistoricalGeneration(
                "ledger-attachments/runtime-checkpoint-fence.json",
                Historical80AttachmentSha256);
            CopyHistoricalGeneration(
                "versions/0.0.1-preview.81/ledger-attachments/runtime-checkpoint-fence.json",
                Historical81AttachmentSha256);
            WriteStagingGeneration();
        }

        public string Root { get; }
        public string EvidenceRoot { get; }
        public string StagingRoot { get; }
        public string LedgerPath => Path.Combine(EvidenceRoot, "coverage-ledger.json");
        public string DestinationGenerationPath => Path.Combine(EvidenceRoot, "versions", ProviderVersion);
        public string Historical80AttachmentPath => Path.Combine(EvidenceRoot, "ledger-attachments", "runtime-checkpoint-fence.json");
        public string Historical81AttachmentPath => Path.Combine(
            EvidenceRoot,
            "versions",
            "0.0.1-preview.81",
            "ledger-attachments",
            "runtime-checkpoint-fence.json");
        public string Historical80ArtifactPath { get; private set; } = string.Empty;
        public CheckpointFenceEvidenceImportRequest Request => new(LedgerPath, StagingRoot, ProviderVersion, Provenance);

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

        public JsonArray StagedRecords() => JsonNode.Parse(File.ReadAllText(StagingAttachmentPath))!.AsArray();

        public void WriteStagedRecords(JsonArray records) =>
            File.WriteAllText(StagingAttachmentPath, JsonSerializer.Serialize(records, JsonOptions) + "\n");

        public string StagingArtifactPath(JsonObject record) => Path.Combine(
            StagingRoot,
            record["evidence"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));

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
                if (attachmentRelativePath.StartsWith("ledger-attachments/", StringComparison.Ordinal))
                    Historical80ArtifactPath = destinationEvidence;
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
                record["providerVersion"] = ProviderVersion;
                record["provenance"] = ProvenanceNode();
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
