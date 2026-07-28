using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Json;
using Elsa.Groundwork.Tools;

namespace Elsa.Architecture.Tests;

internal sealed class GroundworkCoverageLedgerValidator
{
    private static readonly string[] EvidenceCompleteStatuses =
    [
        "evidence-complete",
        "performance-complete",
        "ready"
    ];

    private readonly JsonObject _schema;
    private readonly HashSet<string> _baselineEntryIds;
    private readonly string _evidenceRoot;
    private readonly string _compositionEvidenceRoot;

    public GroundworkCoverageLedgerValidator(
        string schemaPath,
        IReadOnlyCollection<string> baselineEntryIds,
        string? evidenceRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaPath);
        ArgumentNullException.ThrowIfNull(baselineEntryIds);

        _schema = JsonNode.Parse(File.ReadAllText(schemaPath))?.AsObject()
                  ?? throw new InvalidOperationException($"Coverage-ledger schema '{schemaPath}' is empty.");
        _baselineEntryIds = baselineEntryIds.ToHashSet(StringComparer.Ordinal);
        _compositionEvidenceRoot = Path.GetFullPath(
            Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(schemaPath))!)!.FullName);
        _evidenceRoot = Path.GetFullPath(evidenceRoot ?? _compositionEvidenceRoot);
    }

    public IReadOnlyList<string> Validate(JsonObject ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var findings = new List<string>();
        JsonSchemaValidator.Validate(_schema, ledger, findings);

        if (ledger["entries"] is JsonArray entries)
        {
            var entryIds = entries
                .OfType<JsonObject>()
                .Select(entry => entry["id"]?.GetValue<string>())
                .Where(id => id is not null)
                .Cast<string>()
                .ToArray();

            foreach (var baselineEntryId in _baselineEntryIds.Order(StringComparer.Ordinal))
            {
                var occurrences = entryIds.Count(id => StringComparer.Ordinal.Equals(id, baselineEntryId));
                if (occurrences == 0)
                    findings.Add($"$.entries: baseline entry '{baselineEntryId}' is missing.");
                else if (occurrences != 1)
                    findings.Add($"$.entries: baseline entry '{baselineEntryId}' occurs {occurrences} times; expected exactly once.");
            }

            foreach (var unexpectedEntryId in entryIds
                         .Where(id => !_baselineEntryIds.Contains(id))
                         .Distinct(StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                findings.Add($"$.entries: entry '{unexpectedEntryId}' is outside the reviewed baseline denominator.");
            }

            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index] is JsonObject entry)
                    ValidateEntry(
                        entry,
                        index,
                        StringValue(ledger, "groundworkVersion"),
                        findings);
            }

            ValidateRetainedCheckpointFenceGeneration(
                entries,
                StringValue(ledger, "groundworkVersion"),
                findings);

            if (ledger["compositionEvidence"] is JsonObject compositionEvidence)
                ValidateCompositionEvidence(compositionEvidence, entries, findings);
        }

        return findings.Order(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateRetainedCheckpointFenceGeneration(
        JsonArray entries,
        string? providerVersion,
        ICollection<string> findings)
    {
        if (providerVersion is null)
            return;

        var expectedTuples = CheckpointFenceEvidenceCatalog.SourceScenarios
            .SelectMany(pair => CheckpointFenceEvidenceCatalog.Providers
                .Select(provider => $"{pair.Key}|{provider}"))
            .ToHashSet(StringComparer.Ordinal);
        var records = entries
            .OfType<JsonObject>()
            .Where(entry => CheckpointFenceEvidenceCatalog.SourceScenarios.Keys.Any(key =>
                key.StartsWith($"{StringValue(entry, "id")}|", StringComparison.Ordinal)))
            .SelectMany(entry => entry["providerEvidence"] is JsonObject evidence
                ? evidence.SelectMany(pair => pair.Value is JsonArray values
                    ? values.OfType<JsonObject>()
                    : [])
                : [])
            .Where(record => StringValue(record, "providerVersion") == providerVersion)
            .Where(record => expectedTuples.Contains(string.Join(
                '|',
                StringValue(record, "coverageEntryId") ?? "<missing-entry>",
                StringValue(record, "scenarioId") ?? "<missing-scenario>",
                StringValue(record, "provider") ?? "<missing-provider>")))
            .ToArray();
        if (records.Length == 0)
            return;

        if (records.Length != expectedTuples.Count)
        {
            findings.Add(
                $"retained checkpoint/fence generation '{providerVersion}' must contain exactly {expectedTuples.Count} records; found {records.Length}.");
        }

        JsonObject? exactProvenance = null;
        var actualTuples = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var entryId = StringValue(record, "coverageEntryId") ?? "<missing-entry>";
            var scenarioId = StringValue(record, "scenarioId") ?? "<missing-scenario>";
            var provider = StringValue(record, "provider") ?? "<missing-provider>";
            var tuple = $"{entryId}|{scenarioId}|{provider}";
            if (!actualTuples.Add(tuple))
                findings.Add($"retained checkpoint/fence generation '{providerVersion}' duplicates tuple '{tuple}'.");
            if (!expectedTuples.Contains(tuple))
                findings.Add($"retained checkpoint/fence generation '{providerVersion}' contains unexpected tuple '{tuple}'.");

            var sourceScenarioKey = $"{entryId}|{scenarioId}";
            if (CheckpointFenceEvidenceCatalog.SourceScenarios.TryGetValue(sourceScenarioKey, out var expectedSourceScenario) &&
                StringValue(record, "sourceScenarioId") != expectedSourceScenario)
            {
                findings.Add(
                    $"retained checkpoint/fence tuple '{tuple}' must retain source scenario '{expectedSourceScenario}'.");
            }

            if (record["provenance"] is not JsonObject provenance)
            {
                findings.Add($"retained checkpoint/fence tuple '{tuple}' lacks exact source provenance.");
            }
            else if (exactProvenance is null)
            {
                exactProvenance = provenance;
            }
            else if (!JsonNode.DeepEquals(exactProvenance, provenance))
            {
                findings.Add(
                    $"retained checkpoint/fence generation '{providerVersion}' must use one exact source provenance tuple.");
            }

            if (record.ContainsKey("nativeEvidence") || record.ContainsKey("nativeEvidenceSha256"))
            {
                findings.Add($"retained checkpoint/fence tuple '{tuple}' must not claim native-plan evidence.");
            }
        }

        foreach (var missing in expectedTuples.Where(tuple => !actualTuples.Contains(tuple)).Order(StringComparer.Ordinal))
            findings.Add($"retained checkpoint/fence generation '{providerVersion}' is missing tuple '{missing}'.");
    }

    private void ValidateCompositionEvidence(
        JsonObject compositionEvidence,
        JsonArray entries,
        ICollection<string> findings)
    {
        var entriesById = entries
            .OfType<JsonObject>()
            .Where(entry => StringValue(entry, "id") is not null)
            .GroupBy(entry => StringValue(entry, "id")!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var coveredRows = StringArray(compositionEvidence, "coveredEntryIds");
        var coveredSet = coveredRows.ToHashSet(StringComparer.Ordinal);

        foreach (var missing in entriesById.Keys.Where(id => !coveredSet.Contains(id)).Order(StringComparer.Ordinal))
            findings.Add($"composition evidence: coverage row '{missing}' is missing from the reviewed host selection.");
        foreach (var unknown in coveredSet.Where(id => !entriesById.ContainsKey(id)).Order(StringComparer.Ordinal))
            findings.Add($"composition evidence: coverage row '{unknown}' is outside the reviewed ledger denominator.");

        var expectedExternalAuthorities = new Dictionary<string, ExpectedExternalAuthority>(StringComparer.Ordinal)
        {
            ["#644"] = new(
                "adapter-only",
                new HashSet<string>(["iam-user", "iam-role", "iam-external-identity"], StringComparer.Ordinal)),
            ["#660"] = new(
                "linked-source-evidence",
                new HashSet<string>(["runtime-diagnostics-settings"], StringComparer.Ordinal))
        };
        var links = compositionEvidence["externalAuthorityLinks"] as JsonArray;
        foreach (var (authority, expected) in expectedExternalAuthorities)
        {
            var link = links?.OfType<JsonObject>()
                .FirstOrDefault(candidate => StringValue(candidate, "authority") == authority);
            if (link is null)
            {
                findings.Add($"composition evidence: external authority '{authority}' is not linked.");
                continue;
            }

            var relationship = StringValue(link, "relationship");
            if (relationship != expected.Relationship)
            {
                findings.Add(
                    $"composition evidence: external authority '{authority}' must use relationship '{expected.Relationship}', not '{relationship ?? "<missing>"}'.");
            }

            var linkedRows = StringArray(link, "coverageRows").ToHashSet(StringComparer.Ordinal);
            if (!linkedRows.SetEquals(expected.Rows))
            {
                findings.Add(
                    $"composition evidence: external authority '{authority}' must link exactly [{string.Join(", ", expected.Rows.Order(StringComparer.Ordinal))}].");
            }

            foreach (var row in linkedRows.Where(entriesById.ContainsKey))
            {
                var entry = entriesById[row];
                if (StringValue(entry, "authority") != authority ||
                    StringValue(entry, "outcome") != "external-authority-adapter")
                {
                    findings.Add(
                        $"composition evidence: coverage row '{row}' does not preserve external authority '{authority}' as an adapter boundary.");
                }
            }
        }

        ValidateCompositionArtifact(compositionEvidence, findings);
    }

    private sealed record ExpectedExternalAuthority(
        string Relationship,
        IReadOnlySet<string> Rows);

    private void ValidateCompositionArtifact(
        JsonObject compositionEvidence,
        ICollection<string> findings)
    {
        var relativePath = StringValue(compositionEvidence, "artifact");
        if (relativePath is null)
            return;

        var path = Path.GetFullPath(Path.Combine(
            _compositionEvidenceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = _compositionEvidenceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _compositionEvidenceRoot
            : _compositionEvidenceRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.Ordinal) || !File.Exists(path))
        {
            findings.Add($"composition evidence: artifact '{relativePath}' is unavailable.");
            return;
        }

        var actualSha256 = GroundworkEvidenceArtifactContract.FileSha256(path);
        if (StringValue(compositionEvidence, "artifactSha256") != actualSha256)
        {
            findings.Add($"composition evidence: artifact '{relativePath}' digest does not match its contents.");
            return;
        }

        var expectedPayload = GroundworkEvidenceArtifactContract.CompositionArtifactPayload(compositionEvidence);
        JsonObject? actualPayload;
        try
        {
            actualPayload = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            actualPayload = null;
        }

        if (!JsonNode.DeepEquals(expectedPayload, actualPayload))
            findings.Add($"composition evidence: artifact '{relativePath}' does not match its durable payload.");
    }

    public GroundworkCoverageLedger Load(string ledgerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerPath);

        var json = File.ReadAllText(ledgerPath);
        var ledgerNode = JsonNode.Parse(json)?.AsObject()
                         ?? throw new InvalidOperationException($"Coverage ledger '{ledgerPath}' is empty.");
        var findings = Validate(ledgerNode);
        if (findings.Count != 0)
        {
            throw new InvalidOperationException(
                $"Coverage ledger '{ledgerPath}' is invalid:{Environment.NewLine}{string.Join(Environment.NewLine, findings)}");
        }

        return JsonSerializer.Deserialize<GroundworkCoverageLedger>(json, TypedJsonOptions)
               ?? throw new InvalidOperationException($"Coverage ledger '{ledgerPath}' could not be materialized.");
    }

    public IReadOnlyList<string> ValidateTransition(JsonObject previous, JsonObject current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var findings = new List<string>();
        var previousEntries = EntriesById(previous);
        var currentEntries = EntriesById(current);

        foreach (var (id, previousEntry) in previousEntries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!currentEntries.TryGetValue(id, out var currentEntry))
                continue;

            ValidateAppendOnlyObligations(previousEntry, currentEntry, id, "reviewed", findings);

            var previousStatus = StringValue(previousEntry, "status");
            var currentStatus = StringValue(currentEntry, "status");
            if (previousStatus is null || currentStatus is null || previousStatus == currentStatus)
                continue;

            if (previousStatus == "excluded")
            {
                findings.Add($"{id}: status transition 'excluded' -> '{currentStatus}' is not allowed because excluded is terminal.");
                continue;
            }

            if (previousStatus == "performance-complete" && currentStatus == "planned")
            {
                var verdict = currentEntry["performanceVerdict"]?["outcome"]?.GetValue<string>();
                if (verdict != "redesign")
                {
                    findings.Add(
                        $"{id}: status transition 'performance-complete' -> 'planned' requires a current #646 redesign verdict.");
                }

                continue;
            }

            if (!IsAllowedTransition(previousStatus, currentStatus))
                findings.Add($"{id}: status transition '{previousStatus}' -> '{currentStatus}' is not allowed.");
        }

        return findings.Order(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<string> ValidateImmutableBaseline(JsonObject immutable, JsonObject current)
    {
        ArgumentNullException.ThrowIfNull(immutable);
        ArgumentNullException.ThrowIfNull(current);

        var findings = new List<string>();
        var immutableBaselineRef = StringValue(immutable, "baselineRef");
        var currentBaselineRef = StringValue(current, "baselineRef");
        if (immutableBaselineRef is not null && currentBaselineRef != immutableBaselineRef)
        {
            findings.Add(
                $"$: immutable baselineRef '{immutableBaselineRef}' changed to '{currentBaselineRef ?? "<missing>"}'.");
        }

        var currentEntries = EntriesById(current);
        foreach (var (id, immutableEntry) in EntriesById(immutable).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (currentEntries.TryGetValue(id, out var currentEntry))
                ValidateAppendOnlyObligations(immutableEntry, currentEntry, id, "immutable", findings);
        }

        return findings.Order(StringComparer.Ordinal).ToArray();
    }

    private void ValidateEntry(
        JsonObject entry,
        int index,
        string? groundworkVersion,
        ICollection<string> findings)
    {
        var path = $"$.entries[{index}]";
        var id = StringValue(entry, "id") ?? path;
        var status = StringValue(entry, "status");
        var outcome = StringValue(entry, "outcome");
        var authority = StringValue(entry, "authority");
        var dependency = StringValue(entry, "dependency");

        if (outcome == "external-authority-adapter")
        {
            var expectedAuthority = id switch
            {
                "iam-user" or "iam-role" or "iam-external-identity" => "#644",
                "runtime-diagnostics-settings" => "#660",
                _ => null
            };

            if (expectedAuthority is null)
                findings.Add($"{id}: external-authority-adapter is not an approved external ownership boundary.");
            else if (authority != expectedAuthority)
                findings.Add($"{id}: external authority must be '{expectedAuthority}', not '{authority ?? "<missing>"}'.");

            if (dependency != authority)
                findings.Add($"{id}: external dependency must match authority '{authority ?? "<missing>"}'.");
        }

        if (status == "externally-blocked" && outcome != "external-authority-adapter")
            findings.Add($"{id}: externally-blocked requires outcome 'external-authority-adapter'.");

        if (status == "excluded" && outcome != "explicit-exclusion")
            findings.Add($"{id}: excluded requires outcome 'explicit-exclusion'.");

        if (outcome == "explicit-exclusion" && status != "excluded")
            findings.Add($"{id}: explicit-exclusion must use terminal status 'excluded'.");

        var evidenceIsComplete = status is not null && EvidenceCompleteStatuses.Contains(status, StringComparer.Ordinal);
        var evidenceByProvider = ValidateRecordedEvidence(
            entry,
            id,
            groundworkVersion,
            evidenceIsComplete,
            findings);

        if (evidenceIsComplete)
            ValidateEvidenceCompleteness(entry, id, status!, evidenceByProvider, findings);

        if (status is "performance-complete" or "ready")
        {
            if (entry["performanceVerdict"] is not JsonObject performanceVerdict)
            {
                findings.Add($"{id}: status '{status}' requires a #646 performance verdict.");
            }
            else
            {
                var verdictOutcome = StringValue(performanceVerdict, "outcome");
                var requiredVerdict = StringValue(entry, "requiredPerformanceVerdict");
                var accepted = verdictOutcome == "pass" ||
                               verdictOutcome == "not-hot-path" && requiredVerdict != "pass";
                if (!accepted)
                {
                    findings.Add(
                        $"{id}: status '{status}' requires a passing or reviewed not-hot-path #646 verdict; found '{verdictOutcome ?? "<missing>"}'.");
                }
            }
        }
    }

    private void ValidateEvidenceCompleteness(
        JsonObject entry,
        string id,
        string status,
        IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> evidenceByProvider,
        ICollection<string> findings)
    {
        foreach (var provider in new[] { "sqlite", "sqlserver", "postgresql", "mongodb" })
        {
            var records = evidenceByProvider[provider];
            if (records.Count == 0)
            {
                findings.Add($"{id}: status '{status}' requires {provider} provider evidence.");
                continue;
            }

            ValidateObligations(entry, id, provider, records, "queryShapes", "queryShape", findings);
            ValidateObligations(entry, id, provider, records, "concurrencySemantics", "concurrencySemantic", findings);
            ValidateObligations(entry, id, provider, records, "failureWindows", "failureWindow", findings);
            ValidateObligations(entry, id, provider, records, "restartScenarios", "restartScenario", findings);

            foreach (var queryShape in StringArray(entry, "queryShapes"))
            {
                var queryRecords = records.Where(record => StringValue(record, "queryShape") == queryShape).ToArray();
                if (queryRecords.Length != 0 && queryRecords.All(record => string.IsNullOrWhiteSpace(StringValue(record, "nativeEvidence"))))
                    findings.Add($"{id}: {provider} query '{queryShape}' lacks provider-native evidence.");
            }
        }

        var scenarioIds = evidenceByProvider.Values
            .SelectMany(records => records)
            .Select(record => StringValue(record, "scenarioId"))
            .Where(scenario => scenario is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var scenarioId in scenarioIds)
        {
            var scenarioRecords = evidenceByProvider
                .Select(pair => (Provider: pair.Key, Record: pair.Value.FirstOrDefault(record =>
                    StringValue(record, "scenarioId") == scenarioId)))
                .ToArray();
            foreach (var missing in scenarioRecords.Where(candidate => candidate.Record is null))
                findings.Add($"{id}: scenario '{scenarioId}' has no {missing.Provider} evidence record.");

            var hashes = scenarioRecords
                .Select(candidate => candidate.Record is null ? null : StringValue(candidate.Record, "resultHash"))
                .Where(hash => hash is not null)
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count();
            if (hashes > 1)
                findings.Add($"{id}: scenario '{scenarioId}' has non-equivalent provider result hashes.");
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> ValidateRecordedEvidence(
        JsonObject entry,
        string id,
        string? groundworkVersion,
        bool requireCurrentGeneration,
        ICollection<string> findings)
    {
        var evidenceByProvider = new Dictionary<string, IReadOnlyList<JsonObject>>(StringComparer.Ordinal);
        foreach (var provider in new[] { "sqlite", "sqlserver", "postgresql", "mongodb" })
        {
            var allRecords = entry["providerEvidence"]?[provider] is JsonArray evidence
                ? evidence.OfType<JsonObject>().ToArray()
                : [];
            var records = allRecords
                .Where(record => StringValue(record, "providerVersion") == groundworkVersion)
                .ToArray();
            evidenceByProvider[provider] = records;
            if (requireCurrentGeneration && records.Length == 0)
            {
                foreach (var retained in allRecords)
                {
                    var retainedScenario = StringValue(retained, "scenarioId") ?? "<missing>";
                    var retainedVersion = StringValue(retained, "providerVersion") ?? "<missing>";
                    findings.Add(
                        $"{id}: {provider} evidence record '{retainedScenario}' uses provider version '{retainedVersion}', not ledger Groundwork version '{groundworkVersion ?? "<missing>"}'.");
                }
            }

            var recordsToValidate = records;

            foreach (var duplicateScenario in recordsToValidate
                         .Select(record => (
                             Scenario: StringValue(record, "scenarioId"),
                             Version: StringValue(record, "providerVersion")))
                         .Where(candidate => candidate.Scenario is not null)
                         .GroupBy(
                             candidate => $"{candidate.Version}\u001f{candidate.Scenario}",
                             StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                findings.Add(
                    $"{id}: {provider} evidence scenario '{duplicateScenario.First().Scenario}' occurs {duplicateScenario.Count()} times in provider generation '{duplicateScenario.First().Version ?? "<missing>"}'.");
            }

            foreach (var record in recordsToValidate)
            {
                var scenarioId = StringValue(record, "scenarioId") ?? "<missing>";
                var declaredEntryId = StringValue(record, "coverageEntryId");
                if (declaredEntryId != id)
                {
                    findings.Add(
                        $"{id}: {provider} evidence record '{scenarioId}' declares coverage entry '{declaredEntryId ?? "<missing>"}'.");
                }

                var declaredProvider = StringValue(record, "provider");
                if (declaredProvider != provider)
                {
                    findings.Add(
                        $"{id}: {provider} evidence record '{scenarioId}' declares provider '{declaredProvider ?? "<missing>"}'.");
                }

                var expectedProviderIdentity = ProviderIdentity(provider);
                var providerIdentity = StringValue(record, "providerIdentity");
                if (providerIdentity != expectedProviderIdentity)
                {
                    findings.Add(
                        $"{id}: {provider} evidence record '{scenarioId}' requires provider identity '{expectedProviderIdentity}'; found '{providerIdentity ?? "<missing>"}'.");
                }

                var topology = StringValue(record, "topology");
                var allowedTopologies = ProviderTopologies(provider);
                if (!allowedTopologies.Contains(topology, StringComparer.Ordinal))
                {
                    findings.Add(
                        allowedTopologies.Count == 1
                            ? $"{id}: {provider} evidence record '{scenarioId}' requires topology '{allowedTopologies[0]}'; found '{topology ?? "<missing>"}'."
                            : $"{id}: {provider} evidence record '{scenarioId}' requires one of [{string.Join(", ", allowedTopologies)}]; found '{topology ?? "<missing>"}'.");
                }

                ValidateCatalogBinding(entry, id, provider, record, scenarioId, findings);
                ValidateEvidenceArtifacts(id, provider, record, scenarioId, findings);

                if (StringValue(record, "outcome") != "pass")
                    findings.Add($"{id}: {provider} evidence record '{scenarioId}' is not passing.");

                if ((record.ContainsKey("concurrencySemantic") ||
                     record.ContainsKey("failureWindow") ||
                     record.ContainsKey("restartScenario")) &&
                    IntValue(record, "clients") is not >= 2)
                {
                    findings.Add($"{id}: {provider} evidence record '{scenarioId}' must use at least two independent clients.");
                }
            }

        }

        return evidenceByProvider;
    }

    private void ValidateCatalogBinding(
        JsonObject entry,
        string id,
        string provider,
        JsonObject record,
        string scenarioId,
        ICollection<string> findings)
    {
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "ordinary-round-trip" };
        AddCatalogEntries(catalog, entry, "queryShapes", "queryShape");
        AddCatalogEntries(catalog, entry, "concurrencySemantics", "concurrencySemantic");
        AddCatalogEntries(catalog, entry, "failureWindows", "failureWindow");
        AddCatalogEntries(catalog, entry, "restartScenarios", "restartScenario");
        if (!catalog.Contains(scenarioId))
            findings.Add($"{id}: {provider} evidence scenario '{scenarioId}' is outside the active scenario catalog.");

        var sourceScenarioId = StringValue(record, "sourceScenarioId");
        var manifestFingerprint = StringValue(record, "manifestFingerprint");
        var expectedExecutionPath =
            sourceScenarioId is not null && manifestFingerprint?.Length >= 16
                ? $"provider-driver/{provider}/{sourceScenarioId}/{manifestFingerprint[..16]}"
                : null;
        if (StringValue(record, "executionPath") != expectedExecutionPath)
        {
            findings.Add(
                $"{id}: {provider} evidence record '{scenarioId}' does not identify its executed source scenario and physical target.");
        }

        var declaredDimensions = new[] { "queryShape", "concurrencySemantic", "failureWindow", "restartScenario" }
            .Where(record.ContainsKey)
            .ToArray();
        if (scenarioId == "ordinary-round-trip")
        {
            if (declaredDimensions.Length != 0)
            {
                findings.Add(
                    $"{id}: {provider} evidence scenario '{scenarioId}' must identify exactly its catalog obligation.");
            }

            return;
        }

        var expectedDimension = new[] { "queryShape", "concurrencySemantic", "failureWindow", "restartScenario" }
            .FirstOrDefault(dimension => scenarioId.StartsWith($"{dimension}:", StringComparison.Ordinal));
        if (declaredDimensions.Length != 1 || expectedDimension is null || declaredDimensions[0] != expectedDimension)
        {
            findings.Add(
                $"{id}: {provider} evidence scenario '{scenarioId}' must identify exactly its catalog obligation.");
            return;
        }

        var expectedValue = scenarioId[(expectedDimension.Length + 1)..];
        var actualValue = StringValue(record, expectedDimension);
        if (actualValue != expectedValue)
        {
            findings.Add(
                $"{id}: {provider} evidence scenario '{scenarioId}' declares {expectedDimension} '{actualValue ?? "<missing>"}' instead of '{expectedValue}'.");
        }
    }

    private void ValidateEvidenceArtifacts(
        string id,
        string provider,
        JsonObject record,
        string scenarioId,
        ICollection<string> findings)
    {
        var providerVersion = StringValue(record, "providerVersion");
        ValidateObservationResultHash(id, provider, scenarioId, record, findings);
        var expectedEvidence = GroundworkEvidenceArtifactContract.EvidencePath(provider, id, scenarioId);
        var expectedVersionedEvidence = providerVersion is null
            ? null
            : GroundworkEvidenceArtifactContract.VersionedEvidencePath(providerVersion, provider, id, scenarioId);
        var evidence = StringValue(record, "evidence");
        if (evidence is null || (evidence != expectedEvidence && evidence != expectedVersionedEvidence))
        {
            findings.Add(
                $"{id}: {provider} evidence record '{scenarioId}' does not reference its catalog-bound evidence artifact.");
        }
        else
        {
            VerifyArtifact(
                id,
                provider,
                scenarioId,
                evidence,
                StringValue(record, "evidenceSha256"),
                GroundworkEvidenceArtifactContract.ArtifactPayload(record),
                findings);
        }

        var queryShape = StringValue(record, "queryShape");
        if (queryShape is null)
            return;

        var nativeQueryIdentity = StringValue(record, "nativeQueryIdentity");
        var documentKind = StringValue(record, "documentKind");
        if (nativeQueryIdentity is null || documentKind is null)
        {
            findings.Add(
                $"{id}: {provider} query '{queryShape}' must identify its native query identity and document kind.");
            return;
        }

        var expectedNativeEvidence = GroundworkEvidenceArtifactContract.NativeEvidencePath(provider, id, scenarioId);
        var expectedVersionedNativeEvidence = providerVersion is null
            ? null
            : GroundworkEvidenceArtifactContract.VersionedNativeEvidencePath(
                providerVersion,
                provider,
                id,
                scenarioId);
        var nativeEvidence = StringValue(record, "nativeEvidence");
        if (nativeEvidence is null ||
            (nativeEvidence != expectedNativeEvidence && nativeEvidence != expectedVersionedNativeEvidence))
        {
            findings.Add(
                $"{id}: {provider} query '{queryShape}' does not reference its catalog-bound provider-native artifact.");
            return;
        }

        VerifyArtifact(
            id,
            provider,
            scenarioId,
            nativeEvidence,
            StringValue(record, "nativeEvidenceSha256"),
            expectedPayload: null,
            findings);
        ValidateNativeArtifactBinding(
            id,
            provider,
            scenarioId,
            queryShape,
            documentKind,
            nativeQueryIdentity,
            nativeEvidence,
            findings);
    }

    private void ValidateNativeArtifactBinding(
        string id,
        string provider,
        string scenarioId,
        string queryShape,
        string documentKind,
        string nativeQueryIdentity,
        string relativePath,
        ICollection<string> findings)
    {
        var path = Path.GetFullPath(Path.Combine(
            _evidenceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = _evidenceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _evidenceRoot
            : _evidenceRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.Ordinal) || !File.Exists(path))
            return;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || !values.TryAdd(line[..separator], line[(separator + 1)..]))
                continue;
        }

        if (!values.TryGetValue("provider", out var nativeProvider) ||
            !string.Equals(nativeProvider, provider, StringComparison.Ordinal))
        {
            findings.Add(
                $"{id}: {provider} query '{queryShape}' native artifact does not bind the provider.");
        }
        if (!values.TryGetValue("document-kind", out var nativeDocumentKind) ||
            !string.Equals(nativeDocumentKind, documentKind, StringComparison.Ordinal))
        {
            findings.Add(
                $"{id}: {provider} query '{queryShape}' native artifact does not bind document kind '{documentKind}'.");
        }
        if (!values.TryGetValue("route", out var nativeRoute) ||
            !string.Equals(nativeRoute, nativeQueryIdentity, StringComparison.Ordinal))
        {
            findings.Add(
                $"{id}: {provider} query '{queryShape}' native artifact does not bind route '{nativeQueryIdentity}'.");
        }
    }

    private void VerifyArtifact(
        string id,
        string provider,
        string scenarioId,
        string relativePath,
        string? expectedSha256,
        JsonObject? expectedPayload,
        ICollection<string> findings)
    {
        var path = Path.GetFullPath(Path.Combine(
            _evidenceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = _evidenceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _evidenceRoot
            : _evidenceRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.Ordinal) || !File.Exists(path))
        {
            findings.Add(
                $"{id}: {provider} evidence record '{scenarioId}' artifact '{relativePath}' is unavailable.");
            return;
        }

        var actualSha256 = GroundworkEvidenceArtifactContract.FileSha256(path);
        if (expectedSha256 != actualSha256)
        {
            findings.Add(
                $"{id}: {provider} evidence record '{scenarioId}' artifact '{relativePath}' digest does not match its contents.");
            return;
        }

        if (expectedPayload is null)
            return;

        JsonObject? actualPayload;
        try
        {
            actualPayload = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            actualPayload = null;
        }

        if (!JsonNode.DeepEquals(expectedPayload, actualPayload))
        {
            findings.Add(
                $"{id}: {provider} evidence record '{scenarioId}' does not match its durable artifact payload.");
        }
    }

    private static void ValidateObservationResultHash(
        string id,
        string provider,
        string scenarioId,
        JsonObject record,
        ICollection<string> findings)
    {
        if (record["observations"] is not JsonArray observations)
            return;

        var parsed = new List<(string Name, string Value)>();
        foreach (var candidate in observations)
        {
            if (candidate is not JsonObject observation ||
                StringValue(observation, "name") is not { } name ||
                StringValue(observation, "value") is not { } value)
            {
                return;
            }
            parsed.Add((name, value));
        }
        if (parsed.Count == 0)
            return;
        if (parsed.Select(observation => observation.Name).Distinct(StringComparer.Ordinal).Count() != parsed.Count)
        {
            findings.Add(
                $"{id}: {provider} evidence record '{scenarioId}' contains duplicate observation names.");
            return;
        }

        var sourceScenarioId = StringValue(record, "sourceScenarioId");
        var coverageEntryId = StringValue(record, "coverageEntryId");
        var outcome = StringValue(record, "outcome");
        var resultHash = StringValue(record, "resultHash");
        if (sourceScenarioId is null || coverageEntryId is null || outcome is null || resultHash is null)
            return;

        var digestInput = string.Join(
            '\n',
            new[] { sourceScenarioId, coverageEntryId, outcome, StringValue(record, "failureWindow") ?? "-" }
                .Concat(parsed
                    .OrderBy(observation => observation.Name, StringComparer.Ordinal)
                    .ThenBy(observation => observation.Value, StringComparer.Ordinal)
                    .Select(observation =>
                        $"{observation.Name.Length}:{observation.Name}{observation.Value.Length}:{observation.Value}")));
        var observedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestInput)))
            .ToLowerInvariant();
        if (!string.Equals(observedHash, resultHash, StringComparison.Ordinal))
        {
            findings.Add(
                $"{id}: {provider} evidence record '{scenarioId}' result hash does not bind its retained observations.");
        }
    }

    private static void AddCatalogEntries(
        ISet<string> catalog,
        JsonObject entry,
        string obligationCollection,
        string evidenceProperty)
    {
        foreach (var obligation in StringArray(entry, obligationCollection))
            catalog.Add($"{evidenceProperty}:{obligation}");
    }

    private static string ProviderIdentity(string provider) => provider switch
    {
        "sqlite" => "groundwork-sqlite",
        "sqlserver" => "groundwork-sqlserver",
        "postgresql" => "groundwork-postgresql",
        "mongodb" => "groundwork-mongodb",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown mandatory provider.")
    };

    private static IReadOnlyList<string> ProviderTopologies(string provider) => provider switch
    {
        "sqlite" => ["file-backed-distinct-connections"],
        "sqlserver" => ["real-sqlserver-container"],
        "postgresql" => ["real-postgresql-container"],
        "mongodb" => ["transaction-capable-replica-set", "transaction-capable-sharded-cluster"],
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown mandatory provider.")
    };

    private static void ValidateObligations(
        JsonObject entry,
        string id,
        string provider,
        IReadOnlyCollection<JsonObject> records,
        string obligationCollection,
        string evidenceProperty,
        ICollection<string> findings)
    {
        foreach (var obligation in StringArray(entry, obligationCollection))
        {
            if (records.Any(record => StringValue(record, evidenceProperty) == obligation))
                continue;

            var label = evidenceProperty switch
            {
                "queryShape" => "query shape",
                "concurrencySemantic" => "concurrency semantic",
                "failureWindow" => "failure window",
                "restartScenario" => "restart scenario",
                _ => evidenceProperty
            };
            findings.Add($"{id}: {provider} evidence does not cover {label} '{obligation}'.");
        }
    }

    private static void ValidateAppendOnlyObligations(
        JsonObject previous,
        JsonObject current,
        string id,
        string baselineLabel,
        ICollection<string> findings)
    {
        foreach (var property in new[]
                 {
                     "behavioralBaseline",
                     "queryShapes",
                     "concurrencySemantics",
                     "failureWindows",
                     "restartScenarios"
                 })
        {
            var currentValues = StringArray(current, property).ToHashSet(StringComparer.Ordinal);
            foreach (var removed in StringArray(previous, property).Where(value => !currentValues.Contains(value)))
                findings.Add($"{id}: {baselineLabel} {property} obligation '{removed}' was removed.");
        }
    }

    private static IReadOnlyList<string> StringArray(JsonObject value, string propertyName) =>
        value[propertyName] is JsonArray array
            ? array.OfType<JsonValue>()
                .Select(item => item.TryGetValue<string>(out var result) ? result : null)
                .Where(item => item is not null)
                .Cast<string>()
                .ToArray()
            : [];

    private static int? IntValue(JsonObject value, string propertyName) =>
        value[propertyName] is JsonValue property && property.TryGetValue<int>(out var result)
            ? result
            : null;

    private static bool IsAllowedTransition(string previous, string current) =>
        (previous, current) switch
        {
            ("missing", "planned") => true,
            ("planned", "implemented") => true,
            ("implemented", "evidence-complete") => true,
            ("evidence-complete", "performance-complete") => true,
            ("performance-complete", "ready") => true,
            ("planned", "externally-blocked") => true,
            ("externally-blocked", "planned") => true,
            ("ready", "evidence-complete") => true,
            _ => false
        };

    private static Dictionary<string, JsonObject> EntriesById(JsonObject ledger) =>
        ledger["entries"]?.AsArray()
            .OfType<JsonObject>()
            .Where(entry => entry["id"] is JsonValue)
            .ToDictionary(entry => entry["id"]!.GetValue<string>(), StringComparer.Ordinal)
        ?? new Dictionary<string, JsonObject>(StringComparer.Ordinal);

    private static string? StringValue(JsonObject value, string propertyName) =>
        value[propertyName] is JsonValue property && property.TryGetValue<string>(out var result)
            ? result
            : null;

    private static readonly JsonSerializerOptions TypedJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static class JsonSchemaValidator
    {
        public static void Validate(JsonObject schema, JsonNode instance, ICollection<string> findings) =>
            ValidateNode(schema, schema, instance, "$", findings);

        private static void ValidateNode(
            JsonObject rootSchema,
            JsonNode? schemaNode,
            JsonNode? instance,
            string path,
            ICollection<string> findings)
        {
            if (schemaNode is null)
                return;

            if (schemaNode is JsonValue booleanSchema && booleanSchema.TryGetValue<bool>(out var allowed))
            {
                if (!allowed)
                    findings.Add($"{path}: value is not allowed by coverage-ledger.schema.json.");
                return;
            }

            if (schemaNode is not JsonObject schema)
                return;

            if (schema["$ref"] is JsonValue referenceValue &&
                referenceValue.TryGetValue<string>(out var reference))
            {
                ValidateNode(rootSchema, ResolveReference(rootSchema, reference), instance, path, findings);
                return;
            }

            if (schema["type"] is JsonValue typeValue &&
                typeValue.TryGetValue<string>(out var expectedType) &&
                !MatchesType(instance, expectedType))
            {
                findings.Add($"{path}: expected {expectedType} by coverage-ledger.schema.json.");
                return;
            }

            if (schema["const"] is { } constant && !JsonNode.DeepEquals(constant, instance))
            {
                findings.Add($"{path}: value must equal {Display(constant)} by coverage-ledger.schema.json.");
                return;
            }

            if (schema["enum"] is JsonArray allowedValues &&
                !allowedValues.Any(value => JsonNode.DeepEquals(value, instance)))
            {
                findings.Add($"{path}: value {Display(instance)} is not allowed by coverage-ledger.schema.json.");
                return;
            }

            if (instance is JsonObject instanceObject)
                ValidateObject(rootSchema, schema, instanceObject, path, findings);
            else if (instance is JsonArray instanceArray)
                ValidateArray(rootSchema, schema, instanceArray, path, findings);
            else if (instance is JsonValue instanceValue)
                ValidateValue(schema, instanceValue, path, findings);

            if (schema["allOf"] is JsonArray allOf)
            {
                foreach (var childSchema in allOf)
                    ValidateNode(rootSchema, childSchema, instance, path, findings);
            }

            if (schema["if"] is { } condition && Matches(rootSchema, condition, instance) && schema["then"] is { } consequence)
                ValidateNode(rootSchema, consequence, instance, path, findings);
        }

        private static void ValidateObject(
            JsonObject rootSchema,
            JsonObject schema,
            JsonObject instance,
            string path,
            ICollection<string> findings)
        {
            if (schema["required"] is JsonArray required)
            {
                foreach (var requiredProperty in required.Select(node => node!.GetValue<string>()))
                {
                    if (!instance.ContainsKey(requiredProperty))
                        findings.Add($"{path}: required property '{requiredProperty}' is missing.");
                }
            }

            var properties = schema["properties"] as JsonObject;
            if (schema["additionalProperties"] is JsonValue additionalProperties &&
                additionalProperties.TryGetValue<bool>(out var permitsAdditionalProperties) &&
                !permitsAdditionalProperties)
            {
                foreach (var propertyName in instance.Select(pair => pair.Key))
                {
                    if (properties is null || !properties.ContainsKey(propertyName))
                        findings.Add($"{path}: property '{propertyName}' is not declared in coverage-ledger.schema.json.");
                }
            }

            if (properties is null)
                return;

            foreach (var (propertyName, propertySchema) in properties)
            {
                if (instance.TryGetPropertyValue(propertyName, out var propertyValue))
                    ValidateNode(rootSchema, propertySchema, propertyValue, $"{path}.{propertyName}", findings);
            }
        }

        private static void ValidateArray(
            JsonObject rootSchema,
            JsonObject schema,
            JsonArray instance,
            string path,
            ICollection<string> findings)
        {
            if (IntValue(schema, "minItems") is { } minItems && instance.Count < minItems)
                findings.Add($"{path}: expected at least {minItems} items by coverage-ledger.schema.json.");
            if (IntValue(schema, "maxItems") is { } maxItems && instance.Count > maxItems)
                findings.Add($"{path}: expected at most {maxItems} items by coverage-ledger.schema.json.");

            if (BoolValue(schema, "uniqueItems") == true)
            {
                for (var left = 0; left < instance.Count; left++)
                {
                    if (instance.Skip(left + 1).Any(right => JsonNode.DeepEquals(instance[left], right)))
                    {
                        findings.Add($"{path}: array items must be unique by coverage-ledger.schema.json.");
                        break;
                    }
                }
            }

            var prefixItems = schema["prefixItems"] as JsonArray;
            if (prefixItems is not null)
            {
                for (var index = 0; index < Math.Min(prefixItems.Count, instance.Count); index++)
                    ValidateNode(rootSchema, prefixItems[index], instance[index], $"{path}[{index}]", findings);
            }

            if (schema["items"] is not { } itemSchema)
                return;

            var startIndex = prefixItems?.Count ?? 0;
            for (var index = startIndex; index < instance.Count; index++)
                ValidateNode(rootSchema, itemSchema, instance[index], $"{path}[{index}]", findings);
        }

        private static void ValidateValue(
            JsonObject schema,
            JsonValue instance,
            string path,
            ICollection<string> findings)
        {
            if (instance.TryGetValue<int>(out var integerValue) &&
                IntValue(schema, "minimum") is { } minimum &&
                integerValue < minimum)
            {
                findings.Add($"{path}: integer must be at least {minimum}.");
            }

            if (!instance.TryGetValue<string>(out var stringValue))
                return;

            if (IntValue(schema, "minLength") is { } minLength && stringValue.Length < minLength)
                findings.Add($"{path}: string must contain at least {minLength} characters.");
            if (schema["pattern"] is JsonValue patternValue &&
                patternValue.TryGetValue<string>(out var pattern) &&
                !Regex.IsMatch(stringValue, pattern, RegexOptions.CultureInvariant))
            {
                findings.Add($"{path}: value {Display(instance)} does not match the required pattern.");
            }
        }

        private static bool Matches(JsonObject rootSchema, JsonNode schema, JsonNode? instance)
        {
            var findings = new List<string>();
            ValidateNode(rootSchema, schema, instance, "$", findings);
            return findings.Count == 0;
        }

        private static JsonNode ResolveReference(JsonObject rootSchema, string reference)
        {
            if (!reference.StartsWith("#/", StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsupported coverage-ledger schema reference '{reference}'.");

            JsonNode? current = rootSchema;
            foreach (var segment in reference[2..].Split('/'))
            {
                var propertyName = segment.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                current = current?[propertyName];
            }

            return current ?? throw new InvalidOperationException($"Coverage-ledger schema reference '{reference}' does not resolve.");
        }

        private static bool MatchesType(JsonNode? instance, string type) => type switch
        {
            "object" => instance is JsonObject,
            "array" => instance is JsonArray,
            "string" => instance is JsonValue value && value.TryGetValue<string>(out _),
            "integer" => instance is JsonValue value && value.TryGetValue<int>(out _),
            "boolean" => instance is JsonValue value && value.TryGetValue<bool>(out _),
            _ => throw new InvalidOperationException($"Unsupported coverage-ledger schema type '{type}'.")
        };

        private static int? IntValue(JsonObject value, string propertyName) =>
            value[propertyName] is JsonValue property && property.TryGetValue<int>(out var result)
                ? result
                : null;

        private static bool? BoolValue(JsonObject value, string propertyName) =>
            value[propertyName] is JsonValue property && property.TryGetValue<bool>(out var result)
                ? result
                : null;

        private static string Display(JsonNode? value) => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text)
            ? $"'{text}'"
            : value?.ToJsonString() ?? "null";
    }
}

internal static class GroundworkEvidenceArtifactContract
{
    public static string ScenarioKey(string scenarioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scenarioId)))
            .ToLowerInvariant()[..16];
    }

    public static string EvidencePath(string provider, string entryId, string scenarioId) =>
        $"evidence/{provider}/{entryId}/{ScenarioKey(scenarioId)}.json";

    public static string VersionedEvidencePath(
        string providerVersion,
        string provider,
        string entryId,
        string scenarioId) =>
        $"versions/{providerVersion}/{EvidencePath(provider, entryId, scenarioId)}";

    public static string NativeEvidencePath(string provider, string entryId, string scenarioId) =>
        $"evidence/{provider}/{entryId}/{ScenarioKey(scenarioId)}.plan.txt";

    public static string VersionedNativeEvidencePath(
        string providerVersion,
        string provider,
        string entryId,
        string scenarioId) =>
        $"versions/{providerVersion}/{NativeEvidencePath(provider, entryId, scenarioId)}";

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

    public static JsonObject CompositionArtifactPayload(JsonObject record)
    {
        var payload = new JsonObject { ["schemaVersion"] = 1 };
        foreach (var (name, value) in record)
        {
            if (name == "artifactSha256")
                continue;

            payload[name] = value?.DeepClone();
        }

        return payload;
    }

    public static string FileSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

internal sealed record GroundworkCoverageLedger(
    int SchemaVersion,
    string Feature,
    string GroundworkVersion,
    string BaselineRef,
    IReadOnlyList<string> MandatoryProviders,
    GroundworkCompositionEvidence CompositionEvidence,
    IReadOnlyList<GroundworkCoverageEntry> Entries);

internal sealed record GroundworkCompositionEvidence(
    string EvidenceId,
    string Outcome,
    IReadOnlyList<string> SelectedFeatureIdentities,
    IReadOnlyList<string> CoveredEntryIds,
    IReadOnlyList<GroundworkExternalAuthorityLink> ExternalAuthorityLinks,
    string Artifact,
    string ArtifactSha256);

internal sealed record GroundworkExternalAuthorityLink(
    string Authority,
    string Relationship,
    IReadOnlyList<string> CoverageRows);

internal sealed record GroundworkCoverageEntry(
    string Id,
    string Family,
    string Contract,
    string Outcome,
    string Scope,
    string? ScopeReason,
    string AccessPolicy,
    string? AccessReason,
    string Authority,
    string Status,
    string InitialGap,
    IReadOnlyList<string> QueryShapes,
    IReadOnlyList<string> ConcurrencySemantics,
    string AtomicBoundary,
    IReadOnlyList<string> FailureWindows,
    IReadOnlyList<string> RestartScenarios,
    GroundworkProviderEvidence ProviderEvidence,
    string RequiredPerformanceVerdict,
    string PerformanceWorkload,
    GroundworkPerformanceVerdict? PerformanceVerdict,
    string Dependency,
    string DeliveryOwner,
    IReadOnlyList<string> BehavioralBaseline);

internal sealed record GroundworkProviderEvidence(
    IReadOnlyList<GroundworkProviderEvidenceRecord> Sqlite,
    IReadOnlyList<GroundworkProviderEvidenceRecord> Sqlserver,
    IReadOnlyList<GroundworkProviderEvidenceRecord> Postgresql,
    IReadOnlyList<GroundworkProviderEvidenceRecord> Mongodb);

internal sealed record GroundworkProviderEvidenceRecord(
    string ScenarioId,
    string SourceScenarioId,
    string CoverageEntryId,
    string Provider,
    string ProviderIdentity,
    string ProviderVersion,
    string Topology,
    string ManifestFingerprint,
    string ExecutionPath,
    int Clients,
    string? QueryShape,
    string? NativeQueryIdentity,
    string? DocumentKind,
    string? ConcurrencySemantic,
    string? FailureWindow,
    string? RestartScenario,
    string ResultHash,
    string? NativeEvidence,
    string? NativeEvidenceSha256,
    string Outcome,
    string Evidence,
    string EvidenceSha256);

internal sealed record GroundworkPerformanceVerdict(
    string Outcome,
    string AcceptedShape,
    string Evidence);
