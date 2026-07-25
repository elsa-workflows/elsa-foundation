using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Groundwork.Tools;

/// <summary>
/// Imports one retained, externally-staged checkpoint/fence evidence generation.
/// The importer deliberately owns only the reviewed 36-record checkpoint/fence slice; it cannot
/// be reused to advance statuses or to import another provider-evidence generation.
/// </summary>
public static class CheckpointFenceEvidenceImporter
{
    private const string Historical80AttachmentRelativePath =
        "ledger-attachments/runtime-checkpoint-fence.json";
    private const string Historical80AttachmentSha256 =
        "b8fb7ce1faea246d3746c0c586b4e870d0309f17d84490e19a93b957600fac7c";
    private const string Historical81AttachmentRelativePath =
        "versions/0.0.1-preview.81/ledger-attachments/runtime-checkpoint-fence.json";
    private const string Historical81AttachmentSha256 =
        "ee6ea1c85dad6d1506abfbb7899ca73b33f52ae811fd35e254b0f9bce36ddf34";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<CheckpointFenceEvidenceImportResult> ImportAsync(
        CheckpointFenceEvidenceImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateExpectedProvenance(request);
        ValidateProviderVersion(request.ProviderVersion);
        ValidateSourceRepository(request);

        var ledgerPath = Path.GetFullPath(request.LedgerPath);
        if (!File.Exists(ledgerPath))
            throw new InvalidOperationException($"Coverage ledger '{ledgerPath}' is unavailable.");

        var evidenceRoot = CanonicalPath(
            Path.GetDirectoryName(ledgerPath)
            ?? throw new InvalidOperationException("Coverage ledger has no containing evidence root."));
        ValidateHistoricalGeneration(evidenceRoot, Historical80AttachmentRelativePath, Historical80AttachmentSha256);
        ValidateHistoricalGeneration(evidenceRoot, Historical81AttachmentRelativePath, Historical81AttachmentSha256);

        var ledger = ParseObject(await File.ReadAllTextAsync(ledgerPath, cancellationToken), "coverage ledger");
        if (StringValue(ledger, "groundworkVersion") != request.ProviderVersion)
        {
            throw new InvalidOperationException(
                $"Coverage ledger must target Groundwork '{request.ProviderVersion}' before this importer can run.");
        }

        var destinationVersionDirectory = SafePath(
            evidenceRoot,
            $"versions/{request.ProviderVersion}");
        if (File.Exists(destinationVersionDirectory))
        {
            throw new InvalidOperationException(
                $"Destination generation '{destinationVersionDirectory}' is not a directory.");
        }
        var recoverExistingGeneration = Directory.Exists(destinationVersionDirectory);
        string? stagingRoot = null;
        IReadOnlyList<JsonObject> records;
        if (recoverExistingGeneration)
        {
            records = ReadStagedAttachment(evidenceRoot, request.ProviderVersion);
            ValidateStagedRecords(records, evidenceRoot, request);
            ValidateRecoverableGeneration(evidenceRoot, records, request.ProviderVersion);
        }
        else
        {
            stagingRoot = CanonicalPath(request.ExternalStagingRoot);
            RequireExternalStaging(evidenceRoot, stagingRoot);
            records = ReadStagedAttachment(stagingRoot, request.ProviderVersion);
            ValidateStagedRecords(records, stagingRoot, request);
        }

        ValidateLedgerHasNoCurrentGeneration(ledger, request.ProviderVersion);
        var ledgerWithImportedEvidence = AppendRecords(ledger, records);
        EnsureStatusesUnchanged(ledger, ledgerWithImportedEvidence);

        var temporaryLedgerPath = Path.Combine(
            Path.GetDirectoryName(ledgerPath)!,
            $".{Path.GetFileName(ledgerPath)}.{Guid.NewGuid():N}.tmp");
        var temporaryGenerationDirectory = Path.Combine(
            Path.GetDirectoryName(destinationVersionDirectory)!,
            $".{request.ProviderVersion}.{Guid.NewGuid():N}.tmp");

        var generationMoved = false;
        try
        {
            await WriteFileCreateNewAsync(
                temporaryLedgerPath,
                Serialize(ledgerWithImportedEvidence),
                cancellationToken);
            if (!recoverExistingGeneration)
            {
                await CopyGenerationCreateNewAsync(
                    stagingRoot!,
                    temporaryGenerationDirectory,
                    request.ProviderVersion,
                    records,
                    cancellationToken);

                Directory.Move(temporaryGenerationDirectory, destinationVersionDirectory);
                generationMoved = true;
            }
            File.Move(temporaryLedgerPath, ledgerPath, overwrite: true);
        }
        catch
        {
            if (generationMoved)
                DeleteDirectoryIfExists(destinationVersionDirectory);
            throw;
        }
        finally
        {
            DeleteIfExists(temporaryLedgerPath);
            DeleteDirectoryIfExists(temporaryGenerationDirectory);
        }

        return new CheckpointFenceEvidenceImportResult(
            records.Count,
            AttachmentRelativePath(request.ProviderVersion),
            request.Provenance);
    }

    public static JsonObject ArtifactPayload(JsonObject record)
    {
        var payload = new JsonObject { ["schemaVersion"] = 1 };
        foreach (var (name, value) in record)
        {
            if (name is "evidenceSha256" or "nativeEvidenceSha256")
                continue;
            payload[name] = value?.DeepClone();
        }

        return payload;
    }

    private static void ValidateExpectedProvenance(CheckpointFenceEvidenceImportRequest request)
    {
        if (!GitObjectId(request.Provenance.ElsaCommit) || !GitObjectId(request.Provenance.ElsaTree))
            throw new ArgumentException("The versioned import requires lowercase 40-character commit and tree identifiers.", nameof(request));
        if (!KebabCase(request.Provenance.RunIdentity))
            throw new ArgumentException("The versioned import requires a lowercase kebab-case run identity.", nameof(request));
    }

    private static void ValidateSourceRepository(CheckpointFenceEvidenceImportRequest request)
    {
        var sourceRepository = CanonicalPath(request.SourceRepositoryRoot);
        if (!Directory.Exists(sourceRepository))
            throw new InvalidOperationException($"Source repository '{sourceRepository}' is unavailable.");

        var head = Git(sourceRepository, "rev-parse", "HEAD");
        var tree = Git(sourceRepository, "rev-parse", "HEAD^{tree}");
        if (head != request.Provenance.ElsaCommit || tree != request.Provenance.ElsaTree)
        {
            throw new InvalidOperationException(
                "Versioned provider evidence must be imported against the exact checked-out source commit and tree recorded in its provenance.");
        }
    }

    private static string Git(string repositoryRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repositoryRoot);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start Git source-provenance verification.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git source-provenance verification failed: {standardError.Trim()}");
        }

        return standardOutput.Trim();
    }

    private static void ValidateProviderVersion(string providerVersion)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                providerVersion,
                "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("A semantic Groundwork provider version is required.", nameof(providerVersion));
        }
    }

    private static void RequireExternalStaging(string evidenceRoot, string stagingRoot)
    {
        if (!Directory.Exists(stagingRoot))
            throw new InvalidOperationException($"External staging root '{stagingRoot}' is unavailable.");
        if (IsWithin(stagingRoot, evidenceRoot) || IsWithin(evidenceRoot, stagingRoot))
        {
            throw new InvalidOperationException(
                "Evidence staging must be external to the tracked Spec 094 evidence tree.");
        }
    }

    private static void ValidateHistoricalGeneration(string evidenceRoot, string attachmentRelativePath, string expectedAttachmentSha256)
    {
        var attachmentPath = SafePath(evidenceRoot, attachmentRelativePath);
        if (!File.Exists(attachmentPath) || FileSha256(attachmentPath) != expectedAttachmentSha256)
        {
            throw new InvalidOperationException(
                $"Historical attachment '{attachmentRelativePath}' changed; preview.80 and preview.81 provenance is immutable.");
        }

        var records = ParseArray(File.ReadAllText(attachmentPath), attachmentRelativePath)
            .Select(node => node?.AsObject() ?? throw new InvalidOperationException($"Historical attachment '{attachmentRelativePath}' contains a null record."));
        foreach (var record in records)
        {
            var evidence = RequiredString(record, "evidence", attachmentRelativePath);
            var evidenceSha256 = RequiredString(record, "evidenceSha256", attachmentRelativePath);
            var evidencePath = SafePath(evidenceRoot, evidence);
            if (!File.Exists(evidencePath) || FileSha256(evidencePath) != evidenceSha256)
            {
                throw new InvalidOperationException(
                    $"Historical artifact '{evidence}' changed; preview.80 and preview.81 provenance is immutable.");
            }
        }
    }

    private static IReadOnlyList<JsonObject> ReadStagedAttachment(string stagingRoot, string providerVersion)
    {
        var attachmentRelativePath = AttachmentRelativePath(providerVersion);
        var attachmentPath = SafePath(stagingRoot, attachmentRelativePath);
        if (!File.Exists(attachmentPath))
            throw new InvalidOperationException($"Staged attachment '{attachmentRelativePath}' is unavailable.");

        return ParseArray(File.ReadAllText(attachmentPath), attachmentRelativePath)
            .Select(node => node?.AsObject() ?? throw new InvalidOperationException("Staged attachment contains a null record."))
            .Select(record => (JsonObject)record.DeepClone())
            .ToArray();
    }

    private static void ValidateStagedRecords(
        IReadOnlyList<JsonObject> records,
        string stagingRoot,
        CheckpointFenceEvidenceImportRequest request)
    {
        if (records.Count != 36)
            throw new InvalidOperationException($"Checkpoint/fence import requires exactly 36 records; found {records.Count}.");

        var expectedTuples = CheckpointFenceEvidenceCatalog.SourceScenarios
            .SelectMany(pair => CheckpointFenceEvidenceCatalog.Providers.Select(provider => $"{pair.Key}|{provider}"))
            .ToHashSet(StringComparer.Ordinal);
        var actualTuples = new HashSet<string>(StringComparer.Ordinal);
        var retentionKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            var entry = RequiredString(record, "coverageEntryId", "staged record");
            var scenario = RequiredString(record, "scenarioId", "staged record");
            var provider = RequiredString(record, "provider", "staged record");
            var tuple = $"{entry}|{scenario}|{provider}";
            if (!actualTuples.Add(tuple))
                throw new InvalidOperationException($"Staged attachment contains duplicate evidence tuple '{tuple}'.");
            if (!expectedTuples.Contains(tuple))
                throw new InvalidOperationException($"Staged attachment contains unexpected evidence tuple '{tuple}'.");

            if (RequiredString(record, "providerVersion", tuple) != request.ProviderVersion)
                throw new InvalidOperationException($"Staged evidence tuple '{tuple}' must use provider version '{request.ProviderVersion}'.");
            var retentionKey = $"{tuple}|{request.ProviderVersion}";
            if (!retentionKeys.Add(retentionKey))
                throw new InvalidOperationException($"Staged attachment contains duplicate generation retention key '{retentionKey}'.");

            if (RequiredString(record, "providerIdentity", tuple) != $"groundwork-{provider}")
                throw new InvalidOperationException($"Staged evidence tuple '{tuple}' has an invalid provider identity.");
            if (RequiredString(record, "topology", tuple) != CheckpointFenceEvidenceCatalog.ExpectedTopologies[provider])
                throw new InvalidOperationException($"Staged evidence tuple '{tuple}' has an invalid provider topology.");
            if (RequiredInt(record, "clients", tuple) < 2)
                throw new InvalidOperationException($"Staged evidence tuple '{tuple}' must retain at least two independent clients.");
            if (RequiredString(record, "outcome", tuple) != "pass")
                throw new InvalidOperationException($"Staged evidence tuple '{tuple}' is not passing.");
            var sourceScenario = RequiredString(record, "sourceScenarioId", tuple);
            if (CheckpointFenceEvidenceCatalog.SourceScenarios[$"{entry}|{scenario}"] != sourceScenario)
                throw new InvalidOperationException($"Staged evidence tuple '{tuple}' does not retain its exact source scenario identity.");
            var manifestFingerprint = RequiredString(record, "manifestFingerprint", tuple);
            if (!Sha256(manifestFingerprint))
                throw new InvalidOperationException($"Staged evidence tuple '{tuple}' has an invalid manifest fingerprint.");
            if (RequiredString(record, "executionPath", tuple) !=
                $"provider-driver/{provider}/{sourceScenario}/{manifestFingerprint[..16]}")
            {
                throw new InvalidOperationException(
                    $"Staged evidence tuple '{tuple}' is not bound to its provider, executed source scenario, and physical target.");
            }

            ValidateProvenance(record, tuple, request.Provenance);
            ValidateResultHash(record, tuple);
            ValidateArtifact(record, tuple, stagingRoot, request.ProviderVersion);
        }

        var missing = expectedTuples.Where(tuple => !actualTuples.Contains(tuple)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Staged attachment is missing required evidence tuples: {string.Join(", ", missing)}.");

        foreach (var scenario in records.GroupBy(
                     record => $"{RequiredString(record, "coverageEntryId", "record")}|{RequiredString(record, "scenarioId", "record")}",
                     StringComparer.Ordinal))
        {
            var resultHashes = scenario
                .Select(record => RequiredString(record, "resultHash", scenario.Key))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (resultHashes.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Staged evidence scenario '{scenario.Key}' has non-equivalent provider result hashes.");
            }
        }
    }

    private static void ValidateProvenance(JsonObject record, string tuple, CheckpointFenceEvidenceProvenance expected)
    {
        if (record["provenance"] is not JsonObject provenance ||
            StringValue(provenance, "elsaCommit") != expected.ElsaCommit ||
            StringValue(provenance, "elsaTree") != expected.ElsaTree ||
            StringValue(provenance, "runIdentity") != expected.RunIdentity)
        {
            throw new InvalidOperationException(
                $"Staged evidence tuple '{tuple}' does not retain the requested exact source commit, tree, and run identity.");
        }
    }

    private static void ValidateResultHash(JsonObject record, string tuple)
    {
        if (record["observations"] is not JsonArray observations || observations.Count == 0)
            throw new InvalidOperationException($"Staged evidence tuple '{tuple}' has no retained observations.");

        var parsed = observations.Select(node => node?.AsObject()
            ?? throw new InvalidOperationException($"Staged evidence tuple '{tuple}' has an invalid observation."))
            .Select(observation => (
                Name: RequiredString(observation, "name", tuple),
                Value: RequiredString(observation, "value", tuple)))
            .ToArray();
        if (parsed.Select(observation => observation.Name).Distinct(StringComparer.Ordinal).Count() != parsed.Length)
            throw new InvalidOperationException($"Staged evidence tuple '{tuple}' has duplicate observation names.");

        var digestInput = string.Join(
            '\n',
            new[]
            {
                RequiredString(record, "sourceScenarioId", tuple),
                RequiredString(record, "coverageEntryId", tuple),
                RequiredString(record, "outcome", tuple),
                StringValue(record, "failureWindow") ?? "-"
            }.Concat(parsed
                .OrderBy(observation => observation.Name, StringComparer.Ordinal)
                .ThenBy(observation => observation.Value, StringComparer.Ordinal)
                .Select(observation => $"{observation.Name.Length}:{observation.Name}{observation.Value.Length}:{observation.Value}")));
        var resultHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(digestInput)));
        if (resultHash != RequiredString(record, "resultHash", tuple))
            throw new InvalidOperationException($"Staged evidence tuple '{tuple}' result hash does not bind its observations.");
    }

    private static void ValidateArtifact(JsonObject record, string tuple, string stagingRoot, string providerVersion)
    {
        var provider = RequiredString(record, "provider", tuple);
        var entry = RequiredString(record, "coverageEntryId", tuple);
        var scenario = RequiredString(record, "scenarioId", tuple);
        var evidence = RequiredString(record, "evidence", tuple);
        var expectedEvidence = $"versions/{providerVersion}/evidence/{provider}/{entry}/{ScenarioKey(scenario)}.json";
        if (evidence != expectedEvidence)
            throw new InvalidOperationException($"Staged evidence tuple '{tuple}' is not in the requested version namespace.");
        if (record.ContainsKey("nativeEvidence") || record.ContainsKey("nativeEvidenceSha256"))
            throw new InvalidOperationException($"Staged evidence tuple '{tuple}' unexpectedly includes native-plan evidence.");

        var evidencePath = SafePath(stagingRoot, evidence);
        if (!File.Exists(evidencePath) || FileSha256(evidencePath) != RequiredString(record, "evidenceSha256", tuple))
            throw new InvalidOperationException($"Staged evidence tuple '{tuple}' artifact digest does not match its contents.");

        var artifact = ParseObject(File.ReadAllText(evidencePath), evidence);
        if (!JsonNode.DeepEquals(ArtifactPayload(record), artifact))
            throw new InvalidOperationException($"Staged evidence tuple '{tuple}' does not match its durable artifact payload.");
    }

    private static void ValidateLedgerHasNoCurrentGeneration(JsonObject ledger, string providerVersion)
    {
        var records = Entries(ledger)
            .SelectMany(entry => CheckpointFenceEvidenceCatalog.Providers.SelectMany(provider =>
                (entry["providerEvidence"]?[provider] as JsonArray ?? []).OfType<JsonObject>()))
            .Where(record => StringValue(record, "providerVersion") == providerVersion)
            .ToArray();
        if (records.Length != 0)
        {
            throw new InvalidOperationException(
                $"Coverage ledger already retains '{providerVersion}' provider evidence; this importer accepts one create-new 36-record generation only.");
        }
    }

    private static void ValidateRecoverableGeneration(
        string evidenceRoot,
        IReadOnlyList<JsonObject> retainedRecords,
        string providerVersion)
    {
        var destinationVersionDirectory = SafePath(evidenceRoot, $"versions/{providerVersion}");
        var expectedFiles = retainedRecords
            .Select(record => RequiredString(record, "evidence", "record"))
            .Append(AttachmentRelativePath(providerVersion))
            .ToHashSet(StringComparer.Ordinal);
        var actualFiles = Directory.EnumerateFiles(
                destinationVersionDirectory,
                "*",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(evidenceRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet(StringComparer.Ordinal);
        if (!actualFiles.SetEquals(expectedFiles))
        {
            throw new InvalidOperationException(
                $"Existing destination generation '{providerVersion}' contains an incomplete or unexpected file set.");
        }
    }

    private static JsonObject AppendRecords(JsonObject ledger, IReadOnlyList<JsonObject> records)
    {
        var result = (JsonObject)ledger.DeepClone();
        var entries = Entries(result).ToDictionary(entry => RequiredString(entry, "id", "ledger entry"), StringComparer.Ordinal);
        foreach (var record in records.OrderBy(record => RequiredString(record, "coverageEntryId", "record"), StringComparer.Ordinal)
                     .ThenBy(record => RequiredString(record, "scenarioId", "record"), StringComparer.Ordinal)
                     .ThenBy(record => CheckpointFenceEvidenceCatalog.ProviderOrder(
                         RequiredString(record, "provider", "record"))))
        {
            var entryId = RequiredString(record, "coverageEntryId", "record");
            var provider = RequiredString(record, "provider", "record");
            var evidence = entries[entryId]["providerEvidence"]?[provider] as JsonArray
                           ?? throw new InvalidOperationException($"Ledger entry '{entryId}' has no '{provider}' evidence collection.");
            evidence.Add(record.DeepClone());
        }

        return result;
    }

    private static void EnsureStatusesUnchanged(JsonObject before, JsonObject after)
    {
        var beforeEntries = Entries(before).ToDictionary(entry => RequiredString(entry, "id", "ledger entry"), StringComparer.Ordinal);
        foreach (var entry in Entries(after))
        {
            var id = RequiredString(entry, "id", "ledger entry");
            if (!beforeEntries.TryGetValue(id, out var beforeEntry) ||
                StringValue(beforeEntry, "status") != StringValue(entry, "status"))
            {
                throw new InvalidOperationException("Versioned provider-evidence import must not advance or otherwise change row status.");
            }
        }
    }

    private static async Task CopyGenerationCreateNewAsync(
        string stagingRoot,
        string temporaryGenerationDirectory,
        string providerVersion,
        IReadOnlyList<JsonObject> records,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(temporaryGenerationDirectory);
        foreach (var record in records)
        {
            var relativeEvidence = RequiredString(record, "evidence", "record");
            var source = SafePath(stagingRoot, relativeEvidence);
            var destination = Path.Combine(
                temporaryGenerationDirectory,
                relativeEvidence[("versions/" + providerVersion + "/").Length..].Replace('/', Path.DirectorySeparatorChar));
            await CopyFileCreateNewAsync(source, destination, cancellationToken);
        }

        var sourceAttachment = SafePath(stagingRoot, AttachmentRelativePath(providerVersion));
        var destinationAttachment = Path.Combine(temporaryGenerationDirectory, "ledger-attachments", "runtime-checkpoint-fence.json");
        await CopyFileCreateNewAsync(sourceAttachment, destinationAttachment, cancellationToken);
    }

    private static async Task CopyFileCreateNewAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task WriteFileCreateNewAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static IEnumerable<JsonObject> Entries(JsonObject ledger) =>
        (ledger["entries"] as JsonArray ?? throw new InvalidOperationException("Coverage ledger has no entries."))
        .Select(node => node?.AsObject() ?? throw new InvalidOperationException("Coverage ledger has a null entry."));

    private static string RequiredString(JsonObject value, string property, string subject) =>
        StringValue(value, property)
        ?? throw new InvalidOperationException($"{subject} is missing '{property}'.");

    private static int RequiredInt(JsonObject value, string property, string subject) =>
        value[property] is JsonValue node && node.TryGetValue<int>(out var result)
            ? result
            : throw new InvalidOperationException($"{subject} is missing integer '{property}'.");

    private static string? StringValue(JsonObject value, string property) =>
        value[property] is JsonValue node && node.TryGetValue<string>(out var result) ? result : null;

    private static JsonObject ParseObject(string source, string subject) =>
        JsonNode.Parse(source)?.AsObject()
        ?? throw new InvalidOperationException($"{subject} is not a JSON object.");

    private static JsonArray ParseArray(string source, string subject) =>
        JsonNode.Parse(source)?.AsArray()
        ?? throw new InvalidOperationException($"{subject} is not a JSON array.");

    private static byte[] Serialize(JsonNode node) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(node, JsonOptions) + "\n");

    private static string ScenarioKey(string scenarioId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scenarioId)))[..16];

    private static string AttachmentRelativePath(string providerVersion) =>
        $"versions/{providerVersion}/ledger-attachments/runtime-checkpoint-fence.json";

    private static string FileSha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string SafePath(string root, string relativePath)
    {
        var rootPath = CanonicalPath(root);
        var path = CanonicalPath(Path.Combine(
            Path.GetFullPath(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(path, rootPath))
            throw new InvalidOperationException($"Evidence path '{relativePath}' escapes its root.");
        return path;
    }

    private static bool IsWithin(string path, string root)
    {
        var fullPath = CanonicalPath(path);
        var fullRoot = CanonicalPath(root);
        var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return string.Equals(fullPath, fullRoot, PathComparison) ||
               fullPath.StartsWith(rootPrefix, PathComparison);
    }

    private static string CanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var unresolved = new Stack<string>();
        var existing = fullPath;
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var name = Path.GetFileName(existing);
            if (string.IsNullOrEmpty(name))
                break;
            unresolved.Push(name);
            existing = Path.GetDirectoryName(existing)
                       ?? throw new InvalidOperationException($"Path '{path}' has no resolvable ancestor.");
        }

        var root = Path.GetPathRoot(existing)
                   ?? throw new InvalidOperationException($"Path '{path}' has no root.");
        var resolved = root;
        var relativeExisting = Path.GetRelativePath(root, existing);
        if (relativeExisting != ".")
        {
            foreach (var segment in relativeExisting.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(resolved, segment);
                FileSystemInfo info = Directory.Exists(candidate)
                    ? new DirectoryInfo(candidate)
                    : new FileInfo(candidate);
                resolved = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
            }
        }

        while (unresolved.TryPop(out var segment))
            resolved = Path.Combine(resolved, segment);
        return Path.GetFullPath(resolved);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool GitObjectId(string value) =>
        value.Length == 40 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool Sha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool KebabCase(string value) =>
        value.Length != 0 && value.Split('-').All(segment =>
            segment.Length != 0 && segment.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9'));

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}

public sealed record CheckpointFenceEvidenceProvenance(string ElsaCommit, string ElsaTree, string RunIdentity);

public sealed record CheckpointFenceEvidenceImportRequest(
    string LedgerPath,
    string ExternalStagingRoot,
    string SourceRepositoryRoot,
    string ProviderVersion,
    CheckpointFenceEvidenceProvenance Provenance);

public sealed record CheckpointFenceEvidenceImportResult(
    int ImportedRecordCount,
    string AttachmentRelativePath,
    CheckpointFenceEvidenceProvenance Provenance);
