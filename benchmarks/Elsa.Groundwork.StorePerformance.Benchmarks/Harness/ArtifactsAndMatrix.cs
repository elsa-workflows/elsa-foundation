using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

public sealed record ArtifactManifest(int SchemaVersion, string ComparisonCohortId, string ExpectedCommitSha, string ExpectedHarnessAssemblySha256, string ExpectedHostFingerprintSha256, IReadOnlyDictionary<string, string> ArtifactsSha256);
public sealed record ArtifactSet(IReadOnlyList<ProcessArtifact> Artifacts, string ManifestSha256);

public static class ArtifactStore
{
    private const string ManifestFile = "artifact-manifest.v2.json";
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };

    public static string PathFor(string outputDirectory, RunRequest request) => Path.Combine(outputDirectory, $"{Safe(request.ComparisonCohortId)}.{Safe(request.MeasurementSetId)}.{Safe(request.WorkloadId)}.{Safe(request.Provider)}.{Safe(request.Adapter)}.{Safe(request.PhysicalForm)}.{request.ProcessKind.ToString().ToLowerInvariant()}{request.ProcessIndex}.process.json");

    public static void Write(string outputDirectory, ProcessArtifact artifact)
    {
        if (BenchmarkAdapterAdmission.TryGetBlockedReason(
                artifact.Request.WorkloadId,
                artifact.Request.WorkloadVersion,
                artifact.Request.Provider,
                artifact.Request.Adapter,
                artifact.Request.PhysicalForm,
                out var adapterBlockedReason))
            throw new PerformanceContractException(
                $"Workload '{artifact.Request.WorkloadId}/{artifact.Request.WorkloadVersion}' adapter/form is blocked from artifact creation: {adapterBlockedReason}.");
        ArtifactSafety.ValidateRequest(artifact.Request);
        ArtifactSafety.Validate(artifact);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(PathFor(outputDirectory, artifact.Request), JsonSerializer.Serialize(artifact, JsonOptions));
    }

    /// <summary>Called only after all four child processes have succeeded. Comparison refuses an unsigned
    /// directory, so a partial child run cannot be mistaken for an accepted measurement set.</summary>
    internal static void WriteManifest(string outputDirectory)
    {
        var entries = LoadProcessArtifactsWithoutManifest(outputDirectory);
        ValidateRawPlanOwnership(entries);
        var cohorts = entries.Select(pair => pair.Artifact.Request.ComparisonCohortId).Distinct(StringComparer.Ordinal).ToArray();
        var commits = entries.Select(pair => pair.Artifact.Request.CommitSha).Distinct(StringComparer.Ordinal).ToArray();
        var harnessAssemblies = entries.Select(pair => pair.Artifact.Request.HarnessAssemblySha256).Distinct(StringComparer.Ordinal).ToArray();
        var hosts = entries.Select(pair => pair.Artifact.Request.HostFingerprintSha256).Distinct(StringComparer.Ordinal).ToArray();
        if (cohorts.Length != 1) throw new PerformanceContractException("An artifact manifest may bind exactly one comparison cohort.");
        if (commits.Length != 1) throw new PerformanceContractException("An artifact manifest may bind exactly one expected commit.");
        if (harnessAssemblies.Length != 1) throw new PerformanceContractException("An artifact manifest may bind exactly one expected harness assembly.");
        if (hosts.Length != 1) throw new PerformanceContractException("An artifact manifest may bind exactly one expected host fingerprint.");
        var evidenceNames = entries.Select(pair => EvidenceName(pair.Artifact.Request.NativePlanEvidenceReference)).Distinct(StringComparer.Ordinal).ToArray();
        var rawPlanNames = entries.SelectMany(pair => pair.Artifact.Correctness.NativePlan.Routes).Select(route => RawPlanName(route.RawPlanReference)).Distinct(StringComparer.Ordinal).ToArray();
        var paths = entries.Select(pair => pair.Path)
            .Concat(evidenceNames.Select(name => Path.Combine(outputDirectory, name)))
            .Concat(rawPlanNames.Select(name => Path.Combine(outputDirectory, name)))
            .ToArray();
        if (paths.Any(path => !File.Exists(path))) throw new PerformanceContractException("Every referenced native-plan evidence file must exist before the manifest is written.");
        foreach (var name in rawPlanNames) ValidateRawPlanFile(Path.Combine(outputDirectory, name));
        ValidateRawPlanHashes(outputDirectory, entries);
        var hashes = paths.ToDictionary(path => Path.GetFileName(path), HashFile, StringComparer.Ordinal);
        File.WriteAllText(Path.Combine(outputDirectory, ManifestFile), JsonSerializer.Serialize(new ArtifactManifest(2, cohorts[0], commits[0], harnessAssemblies[0], hosts[0], hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)), JsonOptions));
    }

    public static ArtifactSet ReadAll(string outputDirectory)
    {
        var manifestPath = Path.Combine(outputDirectory, ManifestFile);
        if (!File.Exists(manifestPath)) throw new PerformanceContractException("Artifact manifest is missing; comparison fails closed.");
        var manifestBytes = File.ReadAllBytes(manifestPath);
        RejectDuplicateProperties(JsonDocument.Parse(manifestBytes).RootElement);
        ArtifactManifest manifest;
        try { manifest = JsonSerializer.Deserialize<ArtifactManifest>(manifestBytes, JsonOptions) ?? throw new PerformanceContractException("Artifact manifest is invalid."); }
        catch (JsonException exception) { throw new PerformanceContractException($"Artifact manifest JSON is invalid: {exception.Message}"); }
        if (manifest.SchemaVersion != 2 || !SafeIdentifier(manifest.ComparisonCohortId ?? "") || !Regex.IsMatch(manifest.ExpectedCommitSha ?? "", "^[0-9a-f]{40}$") || !Regex.IsMatch(manifest.ExpectedHarnessAssemblySha256 ?? "", "^[0-9a-f]{64}$") || !Regex.IsMatch(manifest.ExpectedHostFingerprintSha256 ?? "", "^[0-9a-f]{64}$") || manifest.ArtifactsSha256 is null || manifest.ArtifactsSha256.Count == 0) throw new PerformanceContractException("Artifact manifest has an unsupported schema, cohort, expected commit, harness assembly, host fingerprint, or no artifacts.");
        var entries = LoadProcessArtifactsWithoutManifest(outputDirectory);
        ValidateRawPlanOwnership(entries);
        if (entries.Any(entry => entry.Artifact.Request.ComparisonCohortId != manifest.ComparisonCohortId))
            throw new PerformanceContractException("Artifact manifest cohort does not match every process artifact.");
        if (entries.Any(entry => entry.Artifact.Request.CommitSha != manifest.ExpectedCommitSha))
            throw new PerformanceContractException("Artifact manifest expected commit does not match every process artifact.");
        if (entries.Any(entry => entry.Artifact.Request.HarnessAssemblySha256 != manifest.ExpectedHarnessAssemblySha256))
            throw new PerformanceContractException("Artifact manifest expected harness assembly does not match every process artifact.");
        if (entries.Any(entry => entry.Artifact.Request.HostFingerprintSha256 != manifest.ExpectedHostFingerprintSha256))
            throw new PerformanceContractException("Artifact manifest expected host fingerprint does not match every process artifact.");
        var rawPlanNames = entries.SelectMany(pair => pair.Artifact.Correctness.NativePlan.Routes)
            .Select(route => RawPlanName(route.RawPlanReference))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var expectedNames = entries.Select(pair => Path.GetFileName(pair.Path))
            .Concat(entries.Select(pair => EvidenceName(pair.Artifact.Request.NativePlanEvidenceReference)))
            .Concat(rawPlanNames)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!expectedNames.SequenceEqual(manifest.ArtifactsSha256.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal)) throw new PerformanceContractException("Artifact manifest does not bind exactly the process and native-plan evidence artifacts.");
        foreach (var name in expectedNames)
        {
            var path = Path.Combine(outputDirectory, name);
            if (!File.Exists(path) || !string.Equals(HashFile(path), manifest.ArtifactsSha256[name], StringComparison.Ordinal)) throw new PerformanceContractException($"Artifact integrity hash mismatch for {name}.");
        }
        foreach (var name in rawPlanNames) ValidateRawPlanFile(Path.Combine(outputDirectory, name));
        ValidateRawPlanHashes(outputDirectory, entries);
        var allowed = expectedNames.Append(ManifestFile).ToHashSet(StringComparer.Ordinal);
        var unexpected = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !allowed.Contains(name) && !IsAllowedResultFile(name))
            .ToArray();
        if (unexpected.Length > 0) throw new PerformanceContractException("Artifact directory contains a file that is neither manifest-bound evidence nor an allowed result file.");
        return new ArtifactSet(entries.Select(pair => pair.Artifact).ToArray(), Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant());
    }

    internal static IReadOnlyList<(string Path, ProcessArtifact Artifact)> LoadProcessArtifactsWithoutManifest(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory)) throw new PerformanceContractException($"Artifact directory does not exist: {outputDirectory}");
        var entries = new List<(string Path, ProcessArtifact Artifact)>();
        foreach (var path in Directory.EnumerateFiles(outputDirectory, "*.process.json", SearchOption.TopDirectoryOnly))
        {
            var bytes = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(bytes);
            RejectDuplicateProperties(document.RootElement);
            ProcessArtifact artifact;
            try { artifact = JsonSerializer.Deserialize<ProcessArtifact>(bytes, JsonOptions) ?? throw new PerformanceContractException($"Invalid process artifact: {path}"); }
            catch (JsonException exception) { throw new PerformanceContractException($"Process artifact JSON is invalid: {exception.Message}"); }
            ArtifactSafety.ValidateRequest(artifact.Request);
            if (artifact.Correctness?.NativePlan?.Routes is null)
                throw new PerformanceContractException("Process artifact is missing native-plan route evidence.");
            ArtifactSafety.Validate(artifact);
            if (!string.Equals(Path.GetFileName(path), Path.GetFileName(PathFor(outputDirectory, artifact.Request)), StringComparison.Ordinal)) throw new PerformanceContractException($"Artifact file name does not bind its contained identity: {path}");
            entries.Add((path, artifact));
        }
        if (entries.Count == 0) throw new PerformanceContractException("No process artifacts were found.");
        if (entries.GroupBy(pair => ArtifactIdentity(pair.Artifact.Request), StringComparer.Ordinal).Any(group => group.Count() != 1)) throw new PerformanceContractException("Duplicate process artifact identity detected.");
        return entries;
    }

    internal static void ValidateRawPlanOwnership(IReadOnlyList<(string Path, ProcessArtifact Artifact)> entries)
    {
        var bindings = entries
            .SelectMany(entry => entry.Artifact.Correctness.NativePlan.Routes.Select(route => (
                Owner: $"{entry.Artifact.Request.MeasurementSetId}|{route.RouteIdentity}",
                Reference: RawPlanName(route.RawPlanReference))))
            .ToArray();
        if (bindings.GroupBy(binding => binding.Owner, StringComparer.Ordinal)
                .Any(group => group.Select(binding => binding.Reference).Distinct(StringComparer.Ordinal).Count() != 1) ||
            bindings.GroupBy(binding => binding.Reference, StringComparer.Ordinal)
                .Any(group => group.Select(binding => binding.Owner).Distinct(StringComparer.Ordinal).Count() != 1))
            throw new PerformanceContractException("Each measurement-set route must own one distinct raw provider-plan artifact across the cohort.");
    }

    private static void ValidateRawPlanHashes(string outputDirectory, IReadOnlyList<(string Path, ProcessArtifact Artifact)> entries)
    {
        foreach (var route in entries.SelectMany(entry => entry.Artifact.Correctness.NativePlan.Routes))
        {
            var path = RawPlanPath(outputDirectory, route.RawPlanReference);
            if (!File.Exists(path) || HashFile(path) != route.RawPlanSha256)
                throw new PerformanceContractException($"Raw provider-plan evidence does not match its route digest for {route.RouteIdentity}.");
        }
    }

    internal static string ArtifactIdentity(RunRequest request) => string.Join('|', request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion, request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.ProcessKind, request.ProcessIndex);
    internal static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    internal static string EvidencePath(string outputDirectory, string reference) => Path.Combine(outputDirectory, EvidenceName(reference));
    internal static string RawPlanPath(string outputDirectory, string reference) => Path.Combine(outputDirectory, RawPlanName(reference));
    internal static string EvidenceName(string reference) =>
        SafeEvidenceReference(reference) ? reference : throw new PerformanceContractException("Native-plan evidence reference must be a safe top-level relative JSON path.");
    internal static bool SafeEvidenceReference(string reference) =>
        !string.IsNullOrWhiteSpace(reference) &&
        string.Equals(Path.GetFileName(reference), reference, StringComparison.Ordinal) &&
        reference.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(reference, "^[A-Za-z0-9][A-Za-z0-9._-]*\\.json$", RegexOptions.CultureInvariant) &&
        !reference.EndsWith(".process.json", StringComparison.OrdinalIgnoreCase) &&
        !reference.StartsWith("artifact-manifest.", StringComparison.OrdinalIgnoreCase) &&
        !IsAllowedResultFile(reference);
    internal static string RawPlanName(string reference) =>
        SafeRawPlanReference(reference) ? reference : throw new PerformanceContractException("Raw provider-plan evidence reference must be a safe top-level relative JSON, text, or XML path.");
    internal static bool SafeRawPlanReference(string reference) =>
        !string.IsNullOrWhiteSpace(reference) &&
        string.Equals(Path.GetFileName(reference), reference, StringComparison.Ordinal) &&
        Regex.IsMatch(reference, "^[A-Za-z0-9][A-Za-z0-9._-]*\\.(json|txt|xml)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        !reference.EndsWith(".process.json", StringComparison.OrdinalIgnoreCase) &&
        !reference.EndsWith(".native-plan.json", StringComparison.OrdinalIgnoreCase) &&
        !reference.StartsWith("artifact-manifest.", StringComparison.OrdinalIgnoreCase) &&
        !IsAllowedResultFile(reference);
    public static void ValidateRawPlanFile(string path)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 16 * 1024 * 1024)
            throw new PerformanceContractException("Raw provider-plan evidence must be non-empty and no larger than 16 MiB.");
        var content = File.ReadAllText(path);
        if (ArtifactSafety.ContainsSensitiveContent(content))
            throw new PerformanceContractException("Raw provider-plan evidence may not retain connection values or credentials.");
        var structuredContent = SecretRetainedNativePlan.ProviderPlanForStructuredSafetyValidation(content);
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(structuredContent);
                RejectDuplicateProperties(document.RootElement);
                ArtifactSafety.ValidateRawStructured(document.RootElement);
            }
            catch (JsonException exception)
            {
                throw new PerformanceContractException($"Raw provider-plan JSON is invalid: {exception.Message}");
            }
        }
        else if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var document = System.Xml.Linq.XDocument.Parse(structuredContent, System.Xml.Linq.LoadOptions.PreserveWhitespace);
                ArtifactSafety.ValidateRawXml(document);
            }
            catch (System.Xml.XmlException exception)
            {
                throw new PerformanceContractException($"Raw provider-plan XML is invalid: {exception.Message}");
            }
        }
    }
    internal static bool IsAllowedResultFile(string name) =>
        name is "comparison.v1.json" or "comparison.from-gate.v1.json" or "gate.v1.json";
    private static bool SafeIdentifier(string value) => Regex.IsMatch(value, "^[A-Za-z0-9._-]+$");
    private static string Safe(string value) => Regex.IsMatch(value, "^[A-Za-z0-9._-]+$") ? value : throw new PerformanceContractException("Artifact identity contains an unsafe path value.");
    internal static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new PerformanceContractException("JSON artifact contains a duplicate property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }
}

public static class ArtifactSafety
{
    private static readonly Regex SensitiveName = new("(password|pwd|credential|connection|string|secret|account[_-]?key|access[_-]?key|token)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RawConnectionName = new("(server|host|endpoint|data[_-]?source|database|initial[_-]?catalog|port)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SensitiveContent = new("(?i)(password|pwd|connection\\s*string|account[_-]?key|access[_-]?key|server|host|endpoint|data[ _-]?source|database|initial[ _-]?catalog|port|user[ _-]?id|uid)\\s*[:=](?!\\s*[@$:?])\\s*|authorization\\s*:\\s*bearer\\s+\\S+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha1 = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    // Provider projections use stable snake_case field names (for example, min_pool_size). Keep this
    // distinct from connection-name rejection below: underscores are safe metadata syntax, while keys
    // naming hosts, databases, or credentials remain forbidden.
    private static readonly Regex Identifier = new("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PackageVersion = new("^[0-9A-Za-z][0-9A-Za-z.+-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SafeSeed = new("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void ValidateRequest(RunRequest request)
    {
        if (request is null) throw new PerformanceContractException("A durable run request is required.");
        if (!Sha1.IsMatch(request.CommitSha ?? "") || !Sha256.IsMatch(request.HarnessAssemblySha256 ?? "") || !Sha256.IsMatch(request.CompositionFingerprint ?? "") || !Sha256.IsMatch(request.HostFingerprintSha256 ?? "") || !Sha256.IsMatch(request.InputFingerprintSha256 ?? "") || !Sha256.IsMatch(request.NativePlanContentSha256 ?? "") || !SafeSeed.IsMatch(request.Seed ?? "") || !ArtifactStore.SafeEvidenceReference(request.NativePlanEvidenceReference) || request.PackageVersions is null || request.PackageVersions.Count == 0 || request.PackageVersions.Any(pair => !Identifier.IsMatch(pair.Key) || !PackageVersion.IsMatch(pair.Value)) || !PackageVersion.IsMatch(request.ProviderVersion ?? "") || !Identifier.IsMatch(request.ProviderTopology ?? "") || request.ProviderConfiguration is null || request.ProviderConfiguration.Count == 0 || request.ProviderConfiguration.Any(pair => !Identifier.IsMatch(pair.Key) || RawConnectionName.IsMatch(pair.Key) || !SafeSeed.IsMatch(pair.Value)) || !Identifier.IsMatch(request.ComparisonCohortId ?? "") || !Identifier.IsMatch(request.MeasurementSetId ?? "") || !Identifier.IsMatch(request.Provider ?? "") || !Identifier.IsMatch(request.Adapter ?? "") || !Identifier.IsMatch(request.PhysicalForm ?? "") || !Identifier.IsMatch(request.Scale ?? "") || !Identifier.IsMatch(request.NativePlanIdentity ?? ""))
            throw new PerformanceContractException("A durable run request requires actual frozen input, native-plan, commit/package/composition metadata using safe identifiers; placeholders are invalid.");
        Validate(request);
    }

    public static void Validate(object artifact)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(artifact, ArtifactStore.JsonOptions));
        ValidateElement(document.RootElement);
    }

    internal static bool ContainsSensitiveContent(string content) =>
        SensitiveContent.IsMatch(content) ||
        content.Contains("://", StringComparison.Ordinal);

    internal static void ValidateRawStructured(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if ((SensitiveName.IsMatch(property.Name) || RawConnectionName.IsMatch(property.Name)) && !SafeRedactedValue(property.Value))
                    throw new PerformanceContractException($"Raw provider-plan evidence may not retain connection values or credentials (sensitive field '{property.Name}').");
                ValidateRawStructured(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateRawStructured(item);
    }

    internal static void ValidateRawXml(System.Xml.Linq.XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            if ((SensitiveName.IsMatch(element.Name.LocalName) || RawConnectionName.IsMatch(element.Name.LocalName)) && !SafeRedactedText(element.Value))
                throw new PerformanceContractException($"Raw provider-plan evidence may not retain connection values or credentials (sensitive element '{element.Name.LocalName}').");
            foreach (var attribute in element.Attributes())
                if ((SensitiveName.IsMatch(attribute.Name.LocalName) || RawConnectionName.IsMatch(attribute.Name.LocalName)) && !SafeRedactedText(attribute.Value))
                    throw new PerformanceContractException($"Raw provider-plan evidence may not retain connection values or credentials (sensitive attribute '{attribute.Name.LocalName}').");
        }
    }

    private static bool SafeRedactedValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ||
        value.ValueKind == JsonValueKind.String && SafeRedactedText(value.GetString() ?? "");

    private static bool SafeRedactedText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ||
               trimmed is "?" or "[redacted]" or "<redacted>" ||
               trimmed.Equals("redacted", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith('@') ||
               trimmed.StartsWith('$') ||
               trimmed.StartsWith(':');
    }

    private static void ValidateElement(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (SensitiveName.IsMatch(property.Name) &&
                        !(property.Name == nameof(SecretProviderConcurrencyEvidence.DistinctPhysicalConnectionCount) &&
                          property.Value.ValueKind == JsonValueKind.Number))
                        throw new PerformanceContractException($"Artifacts may not retain sensitive field '{property.Name}'.");
                    ValidateElement(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) ValidateElement(item);
                break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                if (ContainsSensitiveContent(text)) throw new PerformanceContractException("Artifacts may not retain connection values or credentials.");
                break;
        }
    }
}

public sealed record MatrixRequest(string ComparisonCohortId, string MeasurementSetId, string WorkloadId, string WorkloadVersion, string Provider, string Adapter, string PhysicalForm, string Scale, string CommitSha, string HarnessAssemblySha256, IReadOnlyDictionary<string, string> PackageVersions, string CompositionFingerprint, string HostFingerprintSha256, string ProviderVersion, string ProviderTopology, IReadOnlyDictionary<string, string> ProviderConfiguration, string Seed, string InputFingerprintSha256, string NativePlanIdentity, string NativePlanEvidenceReference, string NativePlanContentSha256);
public sealed record MatrixPlan(PerformanceWorkload Workload, BenchmarkProtocol Protocol, IReadOnlyList<RunRequest> Runs)
{
    public static MatrixPlan Create(PerformanceWorkload workload, MatrixRequest request)
    {
        BenchmarkAdmissionGuard.RequireReady(workload);
        BenchmarkAdapterAdmission.RequireAdmitted(workload, request.Provider, request.Adapter, request.PhysicalForm);
        if (workload.Id != request.WorkloadId ||
            workload.Version != request.WorkloadVersion ||
            workload.Input.Seed != request.Seed ||
            workload.Input.FingerprintSha256 != request.InputFingerprintSha256 ||
            !workload.RequiredProviders.Contains(request.Provider, StringComparer.Ordinal) ||
            !workload.PhysicalFormsFor646.Contains(request.PhysicalForm, StringComparer.Ordinal) ||
            !workload.RequiredProviderEvidence.TryGetValue(request.Provider, out var topology) ||
            topology != request.ProviderTopology)
            throw new PerformanceContractException("The matrix request does not match the frozen workload/provider/form contract.");
        var protocol = BenchmarkProtocol.Acceptance; protocol.Validate();
        var runs = new List<RunRequest> { ToRun(request, ProcessKind.Warmup, 0) };
        runs.AddRange(Enumerable.Range(1, protocol.MeasuredProcessCount).Select(index => ToRun(request, ProcessKind.Measured, index)));
        foreach (var run in runs) ArtifactSafety.ValidateRequest(run);
        return new MatrixPlan(workload, protocol, runs);
    }
    private static RunRequest ToRun(MatrixRequest request, ProcessKind kind, int index) => new(request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion, request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.CommitSha, request.HarnessAssemblySha256, request.PackageVersions, request.CompositionFingerprint, request.HostFingerprintSha256, request.ProviderVersion, request.ProviderTopology, request.ProviderConfiguration, request.Seed, request.InputFingerprintSha256, request.NativePlanIdentity, request.NativePlanEvidenceReference, request.NativePlanContentSha256, kind, index);
}

public static class ProcessMatrixRunner
{
    public static async Task RunAsync(MatrixPlan plan, string childCommand, string outputDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(childCommand)) throw new PerformanceContractException("matrix requires a real adapter child command; no simulated adapters are available.");
        var anchor = plan.Runs.FirstOrDefault() ?? throw new PerformanceContractException("matrix requires a non-empty acceptance plan.");
        foreach (var run in plan.Runs)
        {
            ArtifactAdmission.ValidateRequest(plan.Workload, run);
            if (run.CommitSha != anchor.CommitSha || run.HarnessAssemblySha256 != anchor.HarnessAssemblySha256)
                throw new PerformanceContractException("Every planned child must use one exact source snapshot and harness assembly.");
        }
        SourceProvenance.RequireCleanHead(SourceProvenance.FindRepositoryRoot(), anchor.CommitSha);
        SourceProvenance.RequireHarnessAssembly(anchor.HarnessAssemblySha256);
        await RunCoreAsync(
            plan,
            outputDirectory,
            async (run, directory, token) =>
            {
                var start = new ProcessStartInfo(childCommand) { UseShellExecute = false };
                start.ArgumentList.Add("run"); start.ArgumentList.Add("--request"); start.ArgumentList.Add(JsonSerializer.Serialize(run, ArtifactStore.JsonOptions)); start.ArgumentList.Add("--out"); start.ArgumentList.Add(directory);
                using var child = Process.Start(start) ?? throw new PerformanceContractException("Could not start the adapter child process.");
                await child.WaitForExitAsync(token);
                return child.ExitCode;
            },
            cancellationToken);
    }

    internal static Task RunForTestAsync(
        MatrixPlan plan,
        string outputDirectory,
        Func<RunRequest, string, CancellationToken, Task<int>> runChild,
        CancellationToken cancellationToken) =>
        RunCoreAsync(plan, outputDirectory, runChild, cancellationToken);

    private static async Task RunCoreAsync(
        MatrixPlan plan,
        string outputDirectory,
        Func<RunRequest, string, CancellationToken, Task<int>> runChild,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runChild);
        BenchmarkAdmissionGuard.RequireReady(plan.Workload);
        foreach (var run in plan.Runs)
            ArtifactAdmission.ValidateRequest(plan.Workload, run);
        var expected = plan.Runs.Select(run => (Run: run, Path: ArtifactStore.PathFor(outputDirectory, run))).ToArray();
        if (expected.Length != 4 ||
            expected.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() != 4 ||
            expected.Count(item => item.Run.ProcessKind == ProcessKind.Warmup && item.Run.ProcessIndex == 0) != 1 ||
            expected.Count(item => item.Run.ProcessKind == ProcessKind.Measured && item.Run.ProcessIndex is 1 or 2 or 3) != 3)
            throw new PerformanceContractException("The acceptance matrix must plan exactly one warmup and three measured artifacts.");

        var existingEntries = Array.Empty<(string Path, ProcessArtifact Artifact)>();
        var manifestPath = Path.Combine(outputDirectory, "artifact-manifest.v2.json");
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFiles(outputDirectory, "*", SearchOption.TopDirectoryOnly).Any())
        {
            if (!File.Exists(manifestPath))
                throw new PerformanceContractException("Preexisting cohort files require a valid schema-v2 artifact manifest.");
            ArtifactStore.ReadAll(outputDirectory);
            existingEntries = ArtifactStore.LoadProcessArtifactsWithoutManifest(outputDirectory).ToArray();
            var existingSets = existingEntries.GroupBy(entry => entry.Artifact.Request.MeasurementSetId, StringComparer.Ordinal).ToArray();
            if (existingSets.Length != 1 ||
                existingSets[0].Count() != 4 ||
                existingEntries.Any(entry => entry.Artifact.Request.ComparisonCohortId != plan.Runs[0].ComparisonCohortId) ||
                existingEntries.Any(entry => entry.Artifact.Request.CommitSha != plan.Runs[0].CommitSha) ||
                existingEntries.Any(entry => entry.Artifact.Request.HarnessAssemblySha256 != plan.Runs[0].HarnessAssemblySha256) ||
                existingEntries.Any(entry => entry.Artifact.Request.HostFingerprintSha256 != plan.Runs[0].HostFingerprintSha256) ||
                existingSets[0].Key == plan.Runs[0].MeasurementSetId)
                throw new PerformanceContractException("An existing cohort must contain one complete, distinct measurement set at the expected commit before the second set is added.");
            foreach (var entry in existingEntries) ArtifactAdmission.Validate(plan.Workload, entry.Artifact, outputDirectory);
        }
        if (expected.Any(item => File.Exists(item.Path)))
            throw new PerformanceContractException("The matrix output contains a preexisting planned artifact path.");
        var plannedEvidencePath = ArtifactStore.EvidencePath(outputDirectory, plan.Runs[0].NativePlanEvidenceReference);
        if (File.Exists(plannedEvidencePath))
            throw new PerformanceContractException("The matrix output contains preexisting native-plan evidence for the planned measurement set.");

        var producedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var existingPaths = existingEntries.Select(entry => entry.Path).ToArray();
        var existingBoundPaths = existingPaths
            .Concat(existingEntries.Select(entry => ArtifactStore.EvidencePath(outputDirectory, entry.Artifact.Request.NativePlanEvidenceReference)))
            .Concat(existingEntries.SelectMany(entry => entry.Artifact.Correctness.NativePlan.Routes).Select(route => ArtifactStore.RawPlanPath(outputDirectory, route.RawPlanReference)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existingHashes = existingBoundPaths.ToDictionary(path => path, ArtifactStore.HashFile, StringComparer.Ordinal);
        foreach (var run in plan.Runs)
        {
            var path = ArtifactStore.PathFor(outputDirectory, run);
            if (File.Exists(path))
                throw new PerformanceContractException($"The matrix output acquired a preexisting planned artifact for {run.ProcessKind}/{run.ProcessIndex}.");
            var exitCode = await runChild(run, outputDirectory, cancellationToken);
            if (exitCode != 0) throw new PerformanceContractException($"The adapter child process {run.ProcessKind}/{run.ProcessIndex} failed with exit code {exitCode}.");
            if (!File.Exists(path)) throw new PerformanceContractException($"The adapter child process did not emit its required artifact for {run.ProcessKind}/{run.ProcessIndex}.");
            if (!File.Exists(plannedEvidencePath) || ArtifactStore.HashFile(plannedEvidencePath) != run.NativePlanContentSha256)
                throw new PerformanceContractException("The adapter child process did not emit native-plan evidence matching the requested content digest.");
            var currentEntries = ArtifactStore.LoadProcessArtifactsWithoutManifest(outputDirectory);
            var admittedPaths = existingPaths.Concat(producedHashes.Keys).Append(path).Order(StringComparer.Ordinal).ToArray();
            if (currentEntries.Count != admittedPaths.Length ||
                !currentEntries.Select(item => item.Path).Order(StringComparer.Ordinal).SequenceEqual(admittedPaths, StringComparer.Ordinal) ||
                producedHashes.Any(pair => ArtifactStore.HashFile(pair.Key) != pair.Value) ||
                existingHashes.Any(pair => ArtifactStore.HashFile(pair.Key) != pair.Value))
                throw new PerformanceContractException("A child emitted an unexpected artifact or changed an already admitted artifact.");
            var produced = currentEntries.SingleOrDefault(item => string.Equals(item.Path, path, StringComparison.Ordinal));
            if (produced == default || !SameRequest(run, produced.Artifact.Request))
                throw new PerformanceContractException($"The adapter child process emitted an artifact that does not exactly match its request for {run.ProcessKind}/{run.ProcessIndex}.");
            foreach (var entry in currentEntries) ArtifactAdmission.Validate(plan.Workload, entry.Artifact, outputDirectory);
            var allowedNames = currentEntries.Select(entry => Path.GetFileName(entry.Path))
                .Concat(currentEntries.Select(entry => ArtifactStore.EvidenceName(entry.Artifact.Request.NativePlanEvidenceReference)))
                .Concat(currentEntries.SelectMany(entry => entry.Artifact.Correctness.NativePlan.Routes).Select(route => ArtifactStore.RawPlanName(route.RawPlanReference)))
                .Append("artifact-manifest.v2.json")
                .ToHashSet(StringComparer.Ordinal);
            if (Directory.EnumerateFiles(outputDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Any(name => name is not null && !allowedNames.Contains(name) && !ArtifactStore.IsAllowedResultFile(name)))
                throw new PerformanceContractException("A child emitted an unexpected unbound file.");
            producedHashes[path] = ArtifactStore.HashFile(path);
        }

        var entries = ArtifactStore.LoadProcessArtifactsWithoutManifest(outputDirectory);
        var allExpectedPaths = existingPaths.Concat(expected.Select(item => item.Path)).Order(StringComparer.Ordinal).ToArray();
        if (entries.Count != allExpectedPaths.Length ||
            !entries.Select(item => item.Path).Order(StringComparer.Ordinal).SequenceEqual(allExpectedPaths, StringComparer.Ordinal) ||
            producedHashes.Any(pair => pair.Value != ArtifactStore.HashFile(pair.Key)) ||
            existingHashes.Any(pair => pair.Value != ArtifactStore.HashFile(pair.Key)))
            throw new PerformanceContractException("The matrix did not produce exactly four unchanged fresh process artifacts.");
        ArtifactStore.WriteManifest(outputDirectory);
        ArtifactStore.ReadAll(outputDirectory);
    }

    private static bool SameRequest(RunRequest first, RunRequest second) =>
        ArtifactAdmission.SameTargetTuple(first, second) &&
        first.ProcessKind == second.ProcessKind &&
        first.ProcessIndex == second.ProcessIndex;
}

public sealed record ComparisonResult(int SchemaVersion, string ArtifactManifestSha256, string WorkloadId, string WorkloadVersion, string Provider, string Scale, string OracleTarget, string Target, bool Complete, bool CorrectnessEqual, IReadOnlyList<ProcessAggregate> OracleOperations, IReadOnlyList<ProcessAggregate> TargetOperations, string? BlockReason)
{
    public SecretProviderConcurrencyEvidence? OracleProviderConcurrency { get; init; }
    public SecretProviderConcurrencyEvidence? TargetProviderConcurrency { get; init; }
}
public static class Comparison
{
    public static ComparisonResult Compare(string outputDirectory, string oracleTarget, string target, WorkloadCatalog catalog)
    {
        var artifactSet = ArtifactStore.ReadAll(outputDirectory);
        var cohorts = artifactSet.Artifacts.Select(item => item.Request.ComparisonCohortId).Distinct(StringComparer.Ordinal).ToArray();
        var measurementSets = artifactSet.Artifacts.GroupBy(item => item.Request.MeasurementSetId, StringComparer.Ordinal).ToArray();
        if (artifactSet.Artifacts.Count != 8 || cohorts.Length != 1 || measurementSets.Length != 2 || measurementSets.Any(set => set.Count() != 4))
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, null, "A comparison cohort requires exactly two distinct complete four-process measurement sets.");
        var oracle = artifactSet.Artifacts.Where(item => Target(item) == oracleTarget).ToArray();
        var targetArtifacts = artifactSet.Artifacts.Where(item => Target(item) == target).ToArray();
        var oracleValidation = ValidateSet(oracle, catalog, outputDirectory);
        var targetValidation = ValidateSet(targetArtifacts, catalog, outputDirectory);
        if (!oracleValidation.Valid || !targetValidation.Valid)
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, oracleValidation.Anchor ?? targetValidation.Anchor, oracleValidation.Error ?? targetValidation.Error!);
        var source = oracleValidation.Anchor!;
        var candidate = targetValidation.Anchor!;
        if (source.ComparisonCohortId != candidate.ComparisonCohortId || source.MeasurementSetId == candidate.MeasurementSetId)
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, source, "Oracle and target must be distinct measurement sets in one comparison cohort.");
        if (source.WorkloadId != candidate.WorkloadId || source.WorkloadVersion != candidate.WorkloadVersion || source.Provider != candidate.Provider || source.Scale != candidate.Scale || source.Seed != candidate.Seed || source.InputFingerprintSha256 != candidate.InputFingerprintSha256)
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, source, "Oracle and target do not share the frozen workload/version/provider/scale/input tuple.");
        if (source.CommitSha != candidate.CommitSha)
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, source, "Oracle and target do not share exact commit provenance.");
        if (!ArtifactAdmission.SameMachineEnvironment(oracleValidation.Machine!, targetValidation.Machine!))
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, source, "Oracle and target do not share a stable machine environment.");
        if (!oracleValidation.OperationNames.SequenceEqual(targetValidation.OperationNames, StringComparer.Ordinal))
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, source, "Oracle and target operation sets differ.");
        if (oracleValidation.Correctness!.ObservedResultDigestSha256 != targetValidation.Correctness!.ObservedResultDigestSha256)
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, source, "Oracle and target correctness digests differ.");
        return new ComparisonResult(1, artifactSet.ManifestSha256, source.WorkloadId, source.WorkloadVersion, source.Provider, source.Scale, oracleTarget, target, true, true, Aggregate(oracle), Aggregate(targetArtifacts), null)
        {
            OracleProviderConcurrency = oracleValidation.Correctness.NativePlan.ProviderConcurrency,
            TargetProviderConcurrency = targetValidation.Correctness.NativePlan.ProviderConcurrency
        };
    }

    public static string Target(ProcessArtifact artifact) => $"{artifact.Request.Provider}/{artifact.Request.Adapter}/{artifact.Request.PhysicalForm}";

    private static ComparisonResult Blocked(string manifestHash, string oracleTarget, string target, RunRequest? request, string reason) => new(1, manifestHash, request?.WorkloadId ?? "", request?.WorkloadVersion ?? "", request?.Provider ?? "", request?.Scale ?? "", oracleTarget, target, false, false, [], [], reason);

    private static TargetValidation ValidateSet(IReadOnlyList<ProcessArtifact> artifacts, WorkloadCatalog catalog, string outputDirectory)
    {
        if (artifacts.Count != 4) return TargetValidation.Invalid(null, "A comparison target must include exactly four process artifacts.");
        var anchor = artifacts[0].Request;
        if (!catalog.Workloads.TryGetValue(anchor.WorkloadId, out var workload) ||
            workload.Version != anchor.WorkloadVersion ||
            workload.Input.Seed != anchor.Seed ||
            workload.Input.FingerprintSha256 != anchor.InputFingerprintSha256)
        {
            return TargetValidation.Invalid(anchor, "A comparison target does not match a frozen workload/version/input contract.");
        }
        if (BenchmarkAdapterAdmission.TryGetBlockedReason(
                workload.Id,
                workload.Version,
                anchor.Provider,
                anchor.Adapter,
                anchor.PhysicalForm,
                out var adapterBlockedReason))
            return TargetValidation.Invalid(
                anchor,
                $"Workload '{workload.Id}' adapter/form is blocked from benchmark comparison: {adapterBlockedReason}.");
        if (!workload.RequiredProviders.Contains(anchor.Provider, StringComparer.Ordinal) ||
            !workload.PhysicalFormsFor646.Contains(anchor.PhysicalForm, StringComparer.Ordinal))
            return TargetValidation.Invalid(anchor, "A comparison target does not match a frozen workload/provider/form contract.");
        if (BenchmarkAdmissionGuard.TryGetBlockedReason(workload, out var blockedReason))
            return TargetValidation.Invalid(
                anchor,
                $"Workload '{workload.Id}' is blocked from benchmark comparison: {blockedReason}.");
        if (artifacts.Any(item => !ArtifactAdmission.SameTargetTuple(anchor, item.Request))) return TargetValidation.Invalid(anchor, "A target contains more than one immutable run tuple.");
        var machine = artifacts[0].Machine;
        if (artifacts.Any(item => !ArtifactAdmission.SameMachineEnvironment(machine, item.Machine))) return TargetValidation.Invalid(anchor, "Machine metadata must be complete and stable within a comparison target.");
        if (artifacts.Count(item => item.Request.ProcessKind == ProcessKind.Warmup && item.Request.ProcessIndex == 0) != 1 || artifacts.Count(item => item.Request.ProcessKind == ProcessKind.Measured && item.Request.ProcessIndex is 1 or 2 or 3) != 3 || artifacts.Any(item => item.Request.ProcessKind == ProcessKind.Warmup && item.Request.ProcessIndex != 0 || item.Request.ProcessKind == ProcessKind.Measured && item.Request.ProcessIndex is < 1 or > 3)) return TargetValidation.Invalid(anchor, "A target requires warmup index 0 and measured indices 1, 2, and 3 exactly once.");
        if (artifacts.Any(item => item.Protocol != BenchmarkProtocol.Acceptance || !item.CorrectnessPassed)) return TargetValidation.Invalid(anchor, "A target has incomplete acceptance protocol or correctness evidence.");
        try
        {
            foreach (var artifact in artifacts) ArtifactAdmission.Validate(workload, artifact, outputDirectory);
        }
        catch (PerformanceContractException exception)
        {
            return TargetValidation.Invalid(anchor, exception.Message);
        }
        var correctness = artifacts[0].Correctness;
        if (artifacts.Any(item => !SameEvidence(correctness, item.Correctness))) return TargetValidation.Invalid(anchor, "Correctness, provider prerequisite, native routes, or native-plan evidence is not stable within the target.");
        var warmup = artifacts.Single(item => item.Request.ProcessKind == ProcessKind.Warmup);
        if (warmup.Operations.Count != 0) return TargetValidation.Invalid(anchor, "Warmup artifacts must not contain timed operation samples.");
        var measured = artifacts.Where(item => item.Request.ProcessKind == ProcessKind.Measured).OrderBy(item => item.Request.ProcessIndex).ToArray();
        var names = measured[0].Operations.Select(operation => operation.Operation).Order(StringComparer.Ordinal).ToArray();
        if (names.Length == 0 || names.Distinct(StringComparer.Ordinal).Count() != names.Length || measured.Any(item => !names.SequenceEqual(item.Operations.Select(operation => operation.Operation).Order(StringComparer.Ordinal), StringComparer.Ordinal) || item.Operations.Any(operation => operation.Count < 100 || operation.SteadyStateSeconds < 30 || !Statistics.HasAuthoritativeRawMetrics(operation)))) return TargetValidation.Invalid(anchor, "Measured runs must contain identical non-empty operation sets with finite positive raw latency and provider round-trip samples, with summaries reproduced from them.");
        return TargetValidation.Validated(anchor, correctness, machine, names);
    }

    private static bool SameEvidence(CorrectnessEvidence first, CorrectnessEvidence second) =>
        first.ObservedResultDigestSha256 == second.ObservedResultDigestSha256 &&
        first.ObservedProviderVersion == second.ObservedProviderVersion &&
        first.ObservedProviderTopology == second.ObservedProviderTopology &&
        first.ObservedProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(second.ObservedProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)) &&
        first.NativePlan.Identity == second.NativePlan.Identity &&
        first.NativePlan.Reference == second.NativePlan.Reference &&
        first.NativePlan.ContentSha256 == second.NativePlan.ContentSha256 &&
        first.NativePlan.ProviderConcurrency == second.NativePlan.ProviderConcurrency &&
        first.NativePlan.Routes.SequenceEqual(second.NativePlan.Routes);
    private static IReadOnlyList<ProcessAggregate> Aggregate(IEnumerable<ProcessArtifact> artifacts) => artifacts
        .Where(item => item.Request.ProcessKind == ProcessKind.Measured)
        .SelectMany(item => item.Operations.Select(operation => (item.Request.ProcessIndex, Operation: operation)))
        .GroupBy(item => item.Operation.Operation, StringComparer.Ordinal)
        .Select(group => new ProcessAggregate(
            group.Key,
            group.OrderBy(item => item.ProcessIndex).Select(item => Statistics.Percentile(item.Operation.RawLatenciesMilliseconds, 50)).ToArray(),
            group.OrderBy(item => item.ProcessIndex).Select(item => Statistics.Percentile(item.Operation.RawLatenciesMilliseconds, 95)).ToArray(),
            group.OrderBy(item => item.ProcessIndex).Select(item => Statistics.Percentile(item.Operation.RawLatenciesMilliseconds, 99)).ToArray(),
            group.OrderBy(item => item.ProcessIndex).Select(item => item.Operation.Count / item.Operation.SteadyStateSeconds).ToArray(),
            group.ToDictionary(item => item.ProcessIndex, item => item.Operation.RawLatenciesMilliseconds, EqualityComparer<int>.Default)))
        .OrderBy(item => item.Operation, StringComparer.Ordinal)
        .ToArray();
    private sealed record TargetValidation(bool Valid, RunRequest? Anchor, CorrectnessEvidence? Correctness, MachineMetadata? Machine, IReadOnlyList<string> OperationNames, string? Error)
    {
        public static TargetValidation Invalid(RunRequest? anchor, string error) => new(false, anchor, null, null, [], error);
        public static TargetValidation Validated(RunRequest anchor, CorrectnessEvidence correctness, MachineMetadata machine, IReadOnlyList<string> names) => new(true, anchor, correctness, machine, names, null);
    }
}

public static class BenchmarkAdmissionGuard
{
    public static bool TryGetBlockedReason(PerformanceWorkload workload, out string reason)
    {
        ArgumentNullException.ThrowIfNull(workload);
        if (ReproducibleWorkloadScenarioCatalog.TryGetBlockedReason(workload.Id, out reason))
            return true;
        if (!workload.BenchmarkAdmission.IsReady)
        {
            reason = workload.BenchmarkAdmission.Reason;
            return true;
        }
        reason = "";
        return false;
    }

    public static void RequireReady(PerformanceWorkload workload)
    {
        if (TryGetBlockedReason(workload, out var reason))
            throw new PerformanceContractException(
                $"Workload '{workload.Id}' is blocked from benchmark execution: {reason}.");
    }
}
