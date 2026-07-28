using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Publishes one verified four-provider scenario matrix in the fixed evidence locations consumed by
/// the Spec 094 ledger validator. The validator has no generation pointer contract, so publishing
/// intentionally overwrites the current scenario files at their deterministic paths.
/// </summary>
public static class GroundworkProviderEvidencePublisher
{
    private static readonly string[] MandatoryProviders = ["sqlite", "sqlserver", "postgresql", "mongodb"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<GroundworkProviderEvidencePublication> PublishAsync(
        string evidenceRoot,
        GroundworkLedgerObligation obligation,
        IEnumerable<GroundworkScenarioResult> scenarioResults,
        IEnumerable<GroundworkNativeRoutePlanResult>? nativeRoutePlans = null,
        CancellationToken cancellationToken = default)
        => await PublishCoreAsync(
            evidenceRoot,
            artifactNamespace: null,
            provenance: null,
            obligation,
            scenarioResults,
            nativeRoutePlans,
            cancellationToken);

    public static async Task<GroundworkProviderEvidencePublication> PublishVersionedAsync(
        string evidenceRoot,
        string providerVersion,
        GroundworkProviderEvidenceProvenance provenance,
        GroundworkLedgerObligation obligation,
        IEnumerable<GroundworkScenarioResult> scenarioResults,
        IEnumerable<GroundworkNativeRoutePlanResult>? nativeRoutePlans = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        var versionNamespace = VersionNamespace(providerVersion);
        return await PublishCoreAsync(
            evidenceRoot,
            versionNamespace,
            provenance,
            obligation,
            scenarioResults,
            nativeRoutePlans,
            cancellationToken);
    }

    private static async Task<GroundworkProviderEvidencePublication> PublishCoreAsync(
        string evidenceRoot,
        string? artifactNamespace,
        GroundworkProviderEvidenceProvenance? provenance,
        GroundworkLedgerObligation obligation,
        IEnumerable<GroundworkScenarioResult> scenarioResults,
        IEnumerable<GroundworkNativeRoutePlanResult>? nativeRoutePlans,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        ArgumentNullException.ThrowIfNull(obligation);
        var results = RequireEquivalentPassingMatrix(obligation, scenarioResults);
        if (artifactNamespace is not null &&
            results.Any(result => !string.Equals(
                result.Provider.ProviderVersion,
                artifactNamespace["versions/".Length..],
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Versioned provider evidence requires every result to match the artifact namespace version.");
        }
        var routes = RequireNativePlans(obligation, results, nativeRoutePlans);
        var records = new JsonArray();
        var artifacts = new List<GroundworkProviderEvidenceArtifact>();

        foreach (var result in results)
        {
            routes.TryGetValue(result.Provider.ProviderKey, out var nativeRoute);
            var record = CreateLedgerRecord(
                obligation,
                result,
                nativeRoute,
                artifactNamespace,
                provenance);
            if (nativeRoute is not null)
            {
                var nativePath = record["nativeEvidence"]!.GetValue<string>();
                var nativeBytes = Encoding.UTF8.GetBytes(RequireSanitized(nativeRoute.Evidence));
                var nativeSha256 = await WriteAndHashAsync(
                    evidenceRoot,
                    nativePath,
                    nativeBytes,
                    createNew: artifactNamespace is not null,
                    cancellationToken);
                record["nativeEvidenceSha256"] = nativeSha256;
                artifacts.Add(new GroundworkProviderEvidenceArtifact(nativePath, nativeSha256));
            }

            var payload = ArtifactPayload(record);
            var payloadBytes = Serialize(payload);
            _ = GroundworkSanitizedEvidence.Create("provider-evidence", Encoding.UTF8.GetString(payloadBytes));
            var evidencePath = record["evidence"]!.GetValue<string>();
            var evidenceSha256 = await WriteAndHashAsync(
                evidenceRoot,
                evidencePath,
                payloadBytes,
                createNew: artifactNamespace is not null,
                cancellationToken);
            record["evidenceSha256"] = evidenceSha256;
            artifacts.Add(new GroundworkProviderEvidenceArtifact(evidencePath, evidenceSha256));
            records.Add(record);
        }

        var attachment = Serialize(records);
        return new GroundworkProviderEvidencePublication(attachment, records, artifacts);
    }

    /// <summary>
    /// Writes one deterministic, merge-ready ledger attachment for a family publication run.
    /// Provider artifacts remain at their validator-owned paths; this transient attachment prevents
    /// test output parsing or hand-authored ledger records during the reviewed import step.
    /// </summary>
    public static async Task<GroundworkProviderEvidenceArtifact> WriteLedgerAttachmentAsync(
        string evidenceRoot,
        string attachmentIdentity,
        IEnumerable<GroundworkProviderEvidencePublication> publications,
        CancellationToken cancellationToken = default)
        => await WriteLedgerAttachmentCoreAsync(
            evidenceRoot,
            attachmentNamespace: null,
            attachmentIdentity,
            publications,
            cancellationToken);

    public static async Task<GroundworkProviderEvidenceArtifact> WriteVersionedLedgerAttachmentAsync(
        string evidenceRoot,
        string providerVersion,
        string attachmentIdentity,
        IEnumerable<GroundworkProviderEvidencePublication> publications,
        CancellationToken cancellationToken = default)
        => await WriteLedgerAttachmentCoreAsync(
            evidenceRoot,
            VersionNamespace(providerVersion),
            attachmentIdentity,
            publications,
            cancellationToken);

    private static async Task<GroundworkProviderEvidenceArtifact> WriteLedgerAttachmentCoreAsync(
        string evidenceRoot,
        string? attachmentNamespace,
        string attachmentIdentity,
        IEnumerable<GroundworkProviderEvidencePublication> publications,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        if (!Regex.IsMatch(
                attachmentIdentity,
                "^[a-z0-9]+(?:-[a-z0-9]+)*$",
                RegexOptions.CultureInvariant))
        {
            throw new ArgumentException(
                "A lowercase kebab-case attachment identity is required.",
                nameof(attachmentIdentity));
        }

        ArgumentNullException.ThrowIfNull(publications);
        var records = publications
            .SelectMany(publication => publication.LedgerRecords)
            .Select(node => node?.AsObject()
                ?? throw new InvalidOperationException("A provider evidence publication contains a null record."))
            .ToArray();
        var duplicate = records
            .GroupBy(
                record => string.Join(
                    '\u001f',
                    record["coverageEntryId"]!.GetValue<string>(),
                    record["scenarioId"]!.GetValue<string>(),
                    record["provider"]!.GetValue<string>(),
                    record["providerVersion"]!.GetValue<string>()),
                StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Ledger attachment contains duplicate evidence key '{duplicate.Key}'.");
        if (attachmentNamespace is not null)
            RequireVersionedAttachmentRecords(attachmentNamespace, records);

        var ordered = new JsonArray(records
            .OrderBy(record => record["coverageEntryId"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(record => record["scenarioId"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(record => ProviderOrder(record["provider"]!.GetValue<string>()))
            .Select(record => (JsonNode)record.DeepClone())
            .ToArray());
        var relativePath = JoinNamespace(
            attachmentNamespace,
            $"ledger-attachments/{attachmentIdentity}.json");
        var sha256 = await WriteAndHashAsync(
            evidenceRoot,
            relativePath,
            Serialize(ordered),
            createNew: attachmentNamespace is not null,
            cancellationToken);
        return new GroundworkProviderEvidenceArtifact(relativePath, sha256);
    }

    private static IReadOnlyList<GroundworkScenarioResult> RequireEquivalentPassingMatrix(
        GroundworkLedgerObligation obligation,
        IEnumerable<GroundworkScenarioResult> scenarioResults)
    {
        ArgumentNullException.ThrowIfNull(scenarioResults);
        var results = scenarioResults.OrderBy(result => ProviderOrder(result.Provider.ProviderKey)).ToArray();
        if (results.Length != MandatoryProviders.Length ||
            !results.Select(result => result.Provider.ProviderKey).SequenceEqual(MandatoryProviders, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Provider evidence publication requires exactly one result from sqlite, sqlserver, postgresql, and mongodb.");
        }

        var first = results[0];
        if (results.Any(result =>
                !string.Equals(result.ScenarioId, first.ScenarioId, StringComparison.Ordinal) ||
                !string.Equals(result.CoverageEntryId, first.CoverageEntryId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Provider evidence publication accepts one scenario and one coverage entry at a time.");
        }
        if (results.Any(result => result.Outcome is not GroundworkScenarioOutcome.Pass))
            throw new InvalidOperationException("Provider evidence publication accepts only passing results.");
        if (results.Any(result => !string.Equals(result.CoverageEntryId, obligation.CoverageEntryId, StringComparison.Ordinal)))
            throw new InvalidOperationException("The ledger obligation must target the real scenario result coverage entry.");
        if (results.Any(result => !string.Equals(result.ScenarioId, obligation.SourceScenarioId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"The ledger obligation source scenario '{obligation.SourceScenarioId}' must match every real scenario result.");
        }
        if ((obligation.Kind is GroundworkLedgerObligationKind.ConcurrencySemantic
                or GroundworkLedgerObligationKind.FailureWindow
                or GroundworkLedgerObligationKind.RestartScenario) &&
            results.Any(result => result.Clients < 2))
        {
            throw new InvalidOperationException(
                "Concurrency, failure-window, and restart evidence requires at least two independent clients.");
        }
        if (results.Select(result => result.ResultDigest).Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new InvalidOperationException("Provider evidence publication requires one equivalent public-result digest across all mandatory providers.");
        if (results.Select(result => result.NativeEvidence is not null).Distinct().Skip(1).Any())
            throw new InvalidOperationException("Provider evidence publication requires a uniform native-evidence classification across all mandatory providers.");
        if (obligation.Kind is GroundworkLedgerObligationKind.FailureWindow &&
            results.Any(result => !string.Equals(result.FailureWindow, obligation.Value, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("A failure-window ledger obligation must match the real scenario failure window.");
        }
        if (obligation.Kind is not GroundworkLedgerObligationKind.FailureWindow &&
            results.Any(result => result.FailureWindow is not null))
        {
            throw new InvalidOperationException("Only a failure-window ledger obligation can publish a failure-window scenario result.");
        }

        return results;
    }

    private static IReadOnlyDictionary<string, GroundworkNativeRoutePlanResult> RequireNativePlans(
        GroundworkLedgerObligation obligation,
        IReadOnlyList<GroundworkScenarioResult> results,
        IEnumerable<GroundworkNativeRoutePlanResult>? nativeRoutePlans)
    {
        var routes = (nativeRoutePlans ?? []).ToArray();
        if (obligation.Kind is not GroundworkLedgerObligationKind.QueryShape)
        {
            if (routes.Length != 0 || results[0].NativeEvidence is not null)
                throw new InvalidOperationException("Only a query-shape ledger obligation can publish native route evidence.");
            return new Dictionary<string, GroundworkNativeRoutePlanResult>(StringComparer.Ordinal);
        }
        if (results[0].NativeEvidence is null)
            throw new InvalidOperationException("A query-shape ledger obligation requires real native route evidence from every provider result.");

        var byProvider = routes.ToDictionary(route => route.ProviderKey, StringComparer.Ordinal);
        if (byProvider.Count != MandatoryProviders.Length ||
            !byProvider.Keys.Order(StringComparer.Ordinal).SequenceEqual(MandatoryProviders.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("A query-evidence publication requires exactly one real native route plan from each mandatory provider.");
        }

        var documentKind = byProvider.Values
            .Select(route => route.Request.DocumentKind)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (documentKind.Length != 1)
            throw new InvalidOperationException("A query-evidence publication requires one matching document kind across all mandatory providers.");

        foreach (var result in results)
        {
            var route = byProvider[result.Provider.ProviderKey];
            var nativeEvidence = result.NativeEvidence!;
            if (!string.Equals(route.Request.QueryIdentity, obligation.NativeQueryIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Provider '{result.Provider.ProviderKey}' native route '{route.Request.QueryIdentity}' does not match query obligation " +
                    $"'{obligation.Value}' mapped to native route '{obligation.NativeQueryIdentity}'.");
            if (!string.Equals(nativeEvidence.Evidence.Sha256, route.Evidence.Sha256, StringComparison.Ordinal) ||
                !string.Equals(nativeEvidence.Evidence.Content, route.Evidence.Content, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Provider '{result.Provider.ProviderKey}' result does not reference the supplied native route evidence.");
            }
        }

        return byProvider;
    }

    private static JsonObject CreateLedgerRecord(
        GroundworkLedgerObligation obligation,
        GroundworkScenarioResult result,
        GroundworkNativeRoutePlanResult? nativeRoute,
        string? artifactNamespace,
        GroundworkProviderEvidenceProvenance? provenance)
    {
        var record = new JsonObject
        {
            ["scenarioId"] = obligation.ScenarioId,
            ["sourceScenarioId"] = obligation.SourceScenarioId,
            ["coverageEntryId"] = obligation.CoverageEntryId,
            ["provider"] = result.Provider.ProviderKey,
            ["providerIdentity"] = result.Provider.ProviderIdentity,
            ["providerVersion"] = result.Provider.ProviderVersion,
            ["topology"] = result.Provider.Topology.Description,
            ["manifestFingerprint"] = result.CompositionFingerprint.Value,
            ["executionPath"] = result.ExecutionPath.Value,
            ["clients"] = result.Clients,
            ["resultHash"] = result.ResultDigest,
            ["observations"] = new JsonArray(result.Observations
                .Select(observation => (JsonNode)new JsonObject
                {
                    ["name"] = observation.Name,
                    ["value"] = observation.Value
                })
                .ToArray()),
            ["outcome"] = "pass"
        };
        if (provenance is not null)
        {
            record["provenance"] = new JsonObject
            {
                ["elsaCommit"] = provenance.ElsaCommit,
                ["elsaTree"] = provenance.ElsaTree,
                ["runIdentity"] = provenance.RunIdentity
            };
        }
        if (obligation.DimensionProperty is { } dimension)
            record[dimension] = obligation.Value;
        if (nativeRoute is not null)
        {
            record["nativeQueryIdentity"] = nativeRoute.Request.QueryIdentity;
            record["documentKind"] = nativeRoute.Request.DocumentKind;
            record["nativeEvidence"] = NativeEvidencePath(result, obligation, artifactNamespace);
        }

        record["evidence"] = EvidencePath(result, obligation, artifactNamespace);
        return record;
    }

    private static JsonObject ArtifactPayload(JsonObject record)
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

    private static void RequireVersionedAttachmentRecords(
        string attachmentNamespace,
        IReadOnlyCollection<JsonObject> records)
    {
        const string versionsPrefix = "versions/";
        if (!attachmentNamespace.StartsWith(versionsPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("A versioned attachment requires a versions/<provider-version> namespace.");
        var providerVersion = attachmentNamespace[versionsPrefix.Length..];
        JsonObject? exactProvenance = null;
        foreach (var record in records)
        {
            if (!string.Equals(
                    record["providerVersion"]?.GetValue<string>(),
                    providerVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A versioned attachment requires every record provider version to match its requested namespace.");
            }
            var provider = record["provider"]?.GetValue<string>();
            var coverageEntryId = record["coverageEntryId"]?.GetValue<string>();
            var scenarioId = record["scenarioId"]?.GetValue<string>();
            if (provider is null || coverageEntryId is null || scenarioId is null)
                throw new InvalidOperationException("A versioned attachment record is missing its catalog identity.");
            var scenarioKey = ScenarioKey(scenarioId);
            var expectedEvidence =
                $"{attachmentNamespace}/evidence/{provider}/{coverageEntryId}/{scenarioKey}.json";
            if (!string.Equals(
                    record["evidence"]?.GetValue<string>(),
                    expectedEvidence,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A versioned attachment requires every evidence path to remain inside its requested namespace.");
            }
            if (record["nativeEvidence"]?.GetValue<string>() is { } nativeEvidence)
            {
                var expectedNativeEvidence =
                    $"{attachmentNamespace}/evidence/{provider}/{coverageEntryId}/{scenarioKey}.plan.txt";
                if (!string.Equals(nativeEvidence, expectedNativeEvidence, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "A versioned attachment requires every native-evidence path to remain inside its requested namespace.");
                }
            }
            if (record["provenance"] is not JsonObject provenance)
            {
                throw new InvalidOperationException(
                    "A versioned attachment requires exact source provenance on every record.");
            }
            exactProvenance ??= provenance;
            if (!JsonNode.DeepEquals(exactProvenance, provenance))
            {
                throw new InvalidOperationException(
                    "A versioned attachment requires one exact source provenance tuple across every record.");
            }
        }
    }

    private static string EvidencePath(
        GroundworkScenarioResult result,
        GroundworkLedgerObligation obligation,
        string? artifactNamespace) =>
        JoinNamespace(
            artifactNamespace,
            $"evidence/{result.Provider.ProviderKey}/{obligation.CoverageEntryId}/{ScenarioKey(obligation.ScenarioId)}.json");

    private static string NativeEvidencePath(
        GroundworkScenarioResult result,
        GroundworkLedgerObligation obligation,
        string? artifactNamespace) =>
        JoinNamespace(
            artifactNamespace,
            $"evidence/{result.Provider.ProviderKey}/{obligation.CoverageEntryId}/{ScenarioKey(obligation.ScenarioId)}.plan.txt");

    private static string VersionNamespace(string providerVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerVersion);
        if (!Regex.IsMatch(
                providerVersion,
                "^[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*$",
                RegexOptions.CultureInvariant))
        {
            throw new ArgumentException(
                "A provider version containing only alphanumeric dot- or hyphen-delimited segments is required.",
                nameof(providerVersion));
        }

        return $"versions/{providerVersion}";
    }

    private static string JoinNamespace(string? artifactNamespace, string relativePath) =>
        artifactNamespace is null ? relativePath : $"{artifactNamespace}/{relativePath}";

    private static string ScenarioKey(string scenarioId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scenarioId)))[..16];

    private static byte[] Serialize(JsonNode value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions) + "\n");

    private static string RequireSanitized(GroundworkSanitizedEvidence evidence) =>
        GroundworkSanitizedEvidence.Create(evidence.Kind, evidence.Content).Content;

    private static async Task<string> WriteAndHashAsync(
        string evidenceRoot,
        string relativePath,
        byte[] content,
        bool createNew,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(evidenceRoot);
        var root = GroundworkEvidencePath.Canonicalize(evidenceRoot);
        var path = GroundworkEvidencePath.ResolveWithin(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        path = GroundworkEvidencePath.ResolveWithin(root, relativePath);
        if (createNew)
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(content, cancellationToken);
        }
        else
        {
            await File.WriteAllBytesAsync(path, content, cancellationToken);
        }
        return Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)));
    }

    private static int ProviderOrder(string providerKey)
    {
        var index = Array.IndexOf(MandatoryProviders, providerKey);
        if (index < 0)
            throw new InvalidOperationException($"Unknown provider '{providerKey}'.");
        return index;
    }
}

public sealed record GroundworkProviderEvidencePublication(
    byte[] LedgerAttachmentJson,
    JsonArray LedgerRecords,
    IReadOnlyList<GroundworkProviderEvidenceArtifact> Artifacts);

public sealed record GroundworkProviderEvidenceArtifact(string RelativePath, string Sha256);

public sealed record GroundworkProviderEvidenceProvenance
{
    private static readonly Regex GitObjectPattern =
        new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex RunIdentityPattern =
        new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private GroundworkProviderEvidenceProvenance(
        string elsaCommit,
        string elsaTree,
        string runIdentity)
    {
        ElsaCommit = elsaCommit;
        ElsaTree = elsaTree;
        RunIdentity = runIdentity;
    }

    public string ElsaCommit { get; }
    public string ElsaTree { get; }
    public string RunIdentity { get; }

    public static GroundworkProviderEvidenceProvenance Create(
        string elsaCommit,
        string elsaTree,
        string runIdentity)
    {
        if (!GitObjectPattern.IsMatch(elsaCommit))
            throw new ArgumentException("A lowercase 40-character Elsa commit SHA is required.", nameof(elsaCommit));
        if (!GitObjectPattern.IsMatch(elsaTree))
            throw new ArgumentException("A lowercase 40-character Elsa tree SHA is required.", nameof(elsaTree));
        if (!RunIdentityPattern.IsMatch(runIdentity))
            throw new ArgumentException("A lowercase kebab-case run identity is required.", nameof(runIdentity));
        return new GroundworkProviderEvidenceProvenance(elsaCommit, elsaTree, runIdentity);
    }
}

public enum GroundworkLedgerObligationKind
{
    OrdinaryRoundTrip,
    QueryShape,
    ConcurrencySemantic,
    FailureWindow,
    RestartScenario
}

/// <summary>
/// Maps a real provider-neutral scenario matrix to exactly one row-level ledger obligation.
/// Its catalog scenario identity is intentionally separate from the source scenario identity.
/// </summary>
public sealed record GroundworkLedgerObligation
{
    private static readonly Regex ValuePattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public GroundworkLedgerObligation(
        string coverageEntryId,
        string sourceScenarioId,
        GroundworkLedgerObligationKind kind,
        string? value = null,
        string? nativeQueryIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coverageEntryId);
        if (!ValuePattern.IsMatch(sourceScenarioId))
            throw new ArgumentException("A lowercase kebab-case source scenario id is required.", nameof(sourceScenarioId));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ledger obligation kind.");
        if (!ValuePattern.IsMatch(coverageEntryId))
            throw new ArgumentException("A lowercase kebab-case coverage entry id is required.", nameof(coverageEntryId));
        if (kind is GroundworkLedgerObligationKind.OrdinaryRoundTrip)
        {
            if (value is not null || nativeQueryIdentity is not null)
                throw new ArgumentException("The ordinary round-trip obligation cannot declare a dimension value.", nameof(value));
        }
        else if (!ValuePattern.IsMatch(value ?? string.Empty))
        {
            throw new ArgumentException("A lowercase kebab-case obligation value is required.", nameof(value));
        }
        if (kind is GroundworkLedgerObligationKind.QueryShape)
        {
            nativeQueryIdentity ??= value;
            if (!ValuePattern.IsMatch(nativeQueryIdentity ?? string.Empty))
                throw new ArgumentException("A lowercase kebab-case native query identity is required.", nameof(nativeQueryIdentity));
        }
        else if (nativeQueryIdentity is not null)
        {
            throw new ArgumentException("Only a query-shape obligation can map a native query identity.", nameof(nativeQueryIdentity));
        }

        CoverageEntryId = coverageEntryId;
        SourceScenarioId = sourceScenarioId;
        Kind = kind;
        Value = value;
        NativeQueryIdentity = nativeQueryIdentity;
    }

    public string CoverageEntryId { get; }
    /// <summary>
    /// The executable scenario that produced the provider results. This provenance is persisted
    /// separately from the ledger obligation's catalog scenario identity.
    /// </summary>
    public string SourceScenarioId { get; }
    public GroundworkLedgerObligationKind Kind { get; }
    public string? Value { get; }
    /// <summary>
    /// The concrete bounded-route identity whose provider-native plan proves a provider-neutral
    /// <see cref="GroundworkLedgerObligationKind.QueryShape"/>. It may differ from <see cref="Value"/>.
    /// </summary>
    public string? NativeQueryIdentity { get; }
    public string ScenarioId => DimensionProperty is null ? "ordinary-round-trip" : $"{DimensionProperty}:{Value}";
    public string? DimensionProperty => Kind switch
    {
        GroundworkLedgerObligationKind.OrdinaryRoundTrip => null,
        GroundworkLedgerObligationKind.QueryShape => "queryShape",
        GroundworkLedgerObligationKind.ConcurrencySemantic => "concurrencySemantic",
        GroundworkLedgerObligationKind.FailureWindow => "failureWindow",
        GroundworkLedgerObligationKind.RestartScenario => "restartScenario",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null)
    };

    public static GroundworkLedgerObligation OrdinaryRoundTrip(string coverageEntryId, string sourceScenarioId) =>
        new(coverageEntryId, sourceScenarioId, GroundworkLedgerObligationKind.OrdinaryRoundTrip);

    public static GroundworkLedgerObligation QueryShape(
        string coverageEntryId,
        string queryShape,
        string nativeQueryIdentity,
        string sourceScenarioId) =>
        new(coverageEntryId, sourceScenarioId, GroundworkLedgerObligationKind.QueryShape, queryShape, nativeQueryIdentity);

    public static GroundworkLedgerObligation FailureWindow(string coverageEntryId, string failureWindow, string sourceScenarioId) =>
        new(coverageEntryId, sourceScenarioId, GroundworkLedgerObligationKind.FailureWindow, failureWindow);

    public static GroundworkLedgerObligation ConcurrencySemantic(string coverageEntryId, string concurrencySemantic, string sourceScenarioId) =>
        new(coverageEntryId, sourceScenarioId, GroundworkLedgerObligationKind.ConcurrencySemantic, concurrencySemantic);

    public static GroundworkLedgerObligation RestartScenario(string coverageEntryId, string restartScenario, string sourceScenarioId) =>
        new(coverageEntryId, sourceScenarioId, GroundworkLedgerObligationKind.RestartScenario, restartScenario);
}
