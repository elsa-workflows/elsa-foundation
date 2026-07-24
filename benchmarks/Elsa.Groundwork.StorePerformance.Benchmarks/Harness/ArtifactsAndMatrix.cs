using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

public sealed record ArtifactManifest(int SchemaVersion, IReadOnlyDictionary<string, string> ArtifactsSha256);
public sealed record ArtifactSet(IReadOnlyList<ProcessArtifact> Artifacts, string ManifestSha256);

public static class ArtifactStore
{
    private const string ManifestFile = "artifact-manifest.v1.json";
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static string PathFor(string outputDirectory, RunRequest request) => Path.Combine(outputDirectory, $"{Safe(request.WorkloadId)}.{Safe(request.Provider)}.{Safe(request.Adapter)}.{Safe(request.PhysicalForm)}.{request.ProcessKind.ToString().ToLowerInvariant()}{request.ProcessIndex}.process.json");

    public static void Write(string outputDirectory, ProcessArtifact artifact)
    {
        ArtifactSafety.ValidateRequest(artifact.Request);
        ArtifactSafety.Validate(artifact);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(PathFor(outputDirectory, artifact.Request), JsonSerializer.Serialize(artifact, JsonOptions));
    }

    /// <summary>Called only after all four child processes have succeeded. Comparison refuses an unsigned
    /// directory, so a partial child run cannot be mistaken for an accepted measurement set.</summary>
    public static void WriteManifest(string outputDirectory)
    {
        var entries = LoadProcessArtifactsWithoutManifest(outputDirectory);
        var hashes = entries.ToDictionary(pair => Path.GetFileName(pair.Path), pair => HashFile(pair.Path), StringComparer.Ordinal);
        File.WriteAllText(Path.Combine(outputDirectory, ManifestFile), JsonSerializer.Serialize(new ArtifactManifest(1, hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)), JsonOptions));
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
        if (manifest.SchemaVersion != 1 || manifest.ArtifactsSha256.Count == 0) throw new PerformanceContractException("Artifact manifest has an unsupported schema or no artifacts.");
        var entries = LoadProcessArtifactsWithoutManifest(outputDirectory);
        var actualNames = entries.Select(pair => Path.GetFileName(pair.Path)).Order(StringComparer.Ordinal).ToArray();
        if (!actualNames.SequenceEqual(manifest.ArtifactsSha256.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal)) throw new PerformanceContractException("Artifact manifest does not bind exactly the process artifacts present on disk.");
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry.Path);
            if (!string.Equals(HashFile(entry.Path), manifest.ArtifactsSha256[name], StringComparison.Ordinal)) throw new PerformanceContractException($"Artifact integrity hash mismatch for {name}.");
        }
        return new ArtifactSet(entries.Select(pair => pair.Artifact).ToArray(), Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant());
    }

    private static IReadOnlyList<(string Path, ProcessArtifact Artifact)> LoadProcessArtifactsWithoutManifest(string outputDirectory)
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
            ArtifactSafety.Validate(artifact);
            if (!string.Equals(Path.GetFileName(path), Path.GetFileName(PathFor(outputDirectory, artifact.Request)), StringComparison.Ordinal)) throw new PerformanceContractException($"Artifact file name does not bind its contained identity: {path}");
            entries.Add((path, artifact));
        }
        if (entries.Count == 0) throw new PerformanceContractException("No process artifacts were found.");
        if (entries.GroupBy(pair => ArtifactIdentity(pair.Artifact.Request), StringComparer.Ordinal).Any(group => group.Count() != 1)) throw new PerformanceContractException("Duplicate process artifact identity detected.");
        return entries;
    }

    internal static string ArtifactIdentity(RunRequest request) => string.Join('|', request.WorkloadId, request.WorkloadVersion, request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.ProcessKind, request.ProcessIndex);
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Safe(string value) => Regex.IsMatch(value, "^[A-Za-z0-9._-]+$") ? value : throw new PerformanceContractException("Artifact identity contains an unsafe path value.");
    private static void RejectDuplicateProperties(JsonElement value)
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
    private static readonly Regex SensitiveName = new("(password|credential|connection|string|secret|access[_-]?key|token)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha1 = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Identifier = new("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PackageVersion = new("^[0-9A-Za-z][0-9A-Za-z.+-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SafeSeed = new("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void ValidateRequest(RunRequest request)
    {
        if (!Sha1.IsMatch(request.CommitSha) || !Sha256.IsMatch(request.CompositionFingerprint) || !Sha256.IsMatch(request.InputFingerprintSha256) || !SafeSeed.IsMatch(request.Seed) || string.IsNullOrWhiteSpace(request.NativePlanIdentity) || string.IsNullOrWhiteSpace(request.NativePlanEvidenceReference) || request.PackageVersions.Count == 0 || request.PackageVersions.Any(pair => !Identifier.IsMatch(pair.Key) || !PackageVersion.IsMatch(pair.Value)) || !Identifier.IsMatch(request.Provider) || !Identifier.IsMatch(request.Adapter) || !Identifier.IsMatch(request.PhysicalForm) || !Identifier.IsMatch(request.Scale) || !Identifier.IsMatch(request.NativePlanIdentity))
            throw new PerformanceContractException("A durable run request requires actual frozen input, native-plan, commit/package/composition metadata using safe identifiers; placeholders are invalid.");
        Validate(request);
    }

    public static void Validate(object artifact)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(artifact, ArtifactStore.JsonOptions));
        ValidateElement(document.RootElement);
    }

    private static void ValidateElement(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (SensitiveName.IsMatch(property.Name)) throw new PerformanceContractException($"Artifacts may not retain sensitive field '{property.Name}'.");
                    ValidateElement(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) ValidateElement(item);
                break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                if (text.Contains("Password=", StringComparison.OrdinalIgnoreCase) || text.Contains("://", StringComparison.Ordinal) && text.Contains('@')) throw new PerformanceContractException("Artifacts may not retain connection values or credentials.");
                break;
        }
    }
}

public sealed record MatrixRequest(string WorkloadId, string WorkloadVersion, string Provider, string Adapter, string PhysicalForm, string Scale, string CommitSha, IReadOnlyDictionary<string, string> PackageVersions, string CompositionFingerprint, string Seed, string InputFingerprintSha256, string NativePlanIdentity, string NativePlanEvidenceReference);
public sealed record MatrixPlan(BenchmarkProtocol Protocol, IReadOnlyList<RunRequest> Runs)
{
    public static MatrixPlan Create(MatrixRequest request)
    {
        var protocol = BenchmarkProtocol.Acceptance; protocol.Validate();
        var runs = new List<RunRequest> { ToRun(request, ProcessKind.Warmup, 0) };
        runs.AddRange(Enumerable.Range(1, protocol.MeasuredProcessCount).Select(index => ToRun(request, ProcessKind.Measured, index)));
        foreach (var run in runs) ArtifactSafety.ValidateRequest(run);
        return new MatrixPlan(protocol, runs);
    }
    private static RunRequest ToRun(MatrixRequest request, ProcessKind kind, int index) => new(request.WorkloadId, request.WorkloadVersion, request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.CommitSha, request.PackageVersions, request.CompositionFingerprint, request.Seed, request.InputFingerprintSha256, request.NativePlanIdentity, request.NativePlanEvidenceReference, kind, index);
}

public static class ProcessMatrixRunner
{
    public static async Task RunAsync(MatrixPlan plan, string childCommand, string outputDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(childCommand)) throw new PerformanceContractException("matrix requires a real adapter child command; no simulated adapters are available.");
        foreach (var run in plan.Runs)
        {
            var start = new ProcessStartInfo(childCommand) { UseShellExecute = false };
            start.ArgumentList.Add("run"); start.ArgumentList.Add("--request"); start.ArgumentList.Add(JsonSerializer.Serialize(run, ArtifactStore.JsonOptions)); start.ArgumentList.Add("--out"); start.ArgumentList.Add(outputDirectory);
            using var child = Process.Start(start) ?? throw new PerformanceContractException("Could not start the adapter child process.");
            await child.WaitForExitAsync(cancellationToken);
            if (child.ExitCode != 0) throw new PerformanceContractException($"The adapter child process {run.ProcessKind}/{run.ProcessIndex} failed with exit code {child.ExitCode}.");
            if (!File.Exists(ArtifactStore.PathFor(outputDirectory, run))) throw new PerformanceContractException($"The adapter child process did not emit its required artifact for {run.ProcessKind}/{run.ProcessIndex}.");
        }
        ArtifactStore.WriteManifest(outputDirectory);
    }
}

public sealed record ComparisonResult(int SchemaVersion, string ArtifactManifestSha256, string WorkloadId, string WorkloadVersion, string Provider, string Scale, string OracleTarget, string Target, bool Complete, bool CorrectnessEqual, IReadOnlyList<ProcessAggregate> OracleOperations, IReadOnlyList<ProcessAggregate> TargetOperations, string? BlockReason);
public static class Comparison
{
    public static ComparisonResult Compare(string outputDirectory, string oracleTarget, string target)
    {
        var artifactSet = ArtifactStore.ReadAll(outputDirectory);
        var oracle = artifactSet.Artifacts.Where(item => Target(item) == oracleTarget).ToArray();
        var targetArtifacts = artifactSet.Artifacts.Where(item => Target(item) == target).ToArray();
        var oracleValidation = ValidateSet(oracle);
        var targetValidation = ValidateSet(targetArtifacts);
        if (!oracleValidation.Valid || !targetValidation.Valid)
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, oracleValidation.Anchor ?? targetValidation.Anchor, oracleValidation.Error ?? targetValidation.Error!);
        var source = oracleValidation.Anchor!;
        var candidate = targetValidation.Anchor!;
        if (source.WorkloadId != candidate.WorkloadId || source.WorkloadVersion != candidate.WorkloadVersion || source.Provider != candidate.Provider || source.Scale != candidate.Scale || source.Seed != candidate.Seed || source.InputFingerprintSha256 != candidate.InputFingerprintSha256)
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, source, "Oracle and target do not share the frozen workload/version/provider/scale/input tuple.");
        if (!oracleValidation.OperationNames.SequenceEqual(targetValidation.OperationNames, StringComparer.Ordinal))
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, source, "Oracle and target operation sets differ.");
        if (oracleValidation.Correctness!.ObservedResultDigestSha256 != targetValidation.Correctness!.ObservedResultDigestSha256)
            return Blocked(artifactSet.ManifestSha256, oracleTarget, target, source, "Oracle and target correctness digests differ.");
        return new ComparisonResult(1, artifactSet.ManifestSha256, source.WorkloadId, source.WorkloadVersion, source.Provider, source.Scale, oracleTarget, target, true, true, Aggregate(oracle), Aggregate(targetArtifacts), null);
    }

    public static string Target(ProcessArtifact artifact) => $"{artifact.Request.Provider}/{artifact.Request.Adapter}/{artifact.Request.PhysicalForm}";

    private static ComparisonResult Blocked(string manifestHash, string oracleTarget, string target, RunRequest? request, string reason) => new(1, manifestHash, request?.WorkloadId ?? "", request?.WorkloadVersion ?? "", request?.Provider ?? "", request?.Scale ?? "", oracleTarget, target, false, false, [], [], reason);

    private static TargetValidation ValidateSet(IReadOnlyList<ProcessArtifact> artifacts)
    {
        if (artifacts.Count != 4) return TargetValidation.Invalid(null, "A comparison target must include exactly four process artifacts.");
        var anchor = artifacts[0].Request;
        if (artifacts.Any(item => !SameTargetTuple(anchor, item.Request))) return TargetValidation.Invalid(anchor, "A target contains more than one immutable run tuple.");
        if (artifacts.Count(item => item.Request.ProcessKind == ProcessKind.Warmup && item.Request.ProcessIndex == 0) != 1 || artifacts.Count(item => item.Request.ProcessKind == ProcessKind.Measured && item.Request.ProcessIndex is 1 or 2 or 3) != 3 || artifacts.Any(item => item.Request.ProcessKind == ProcessKind.Warmup && item.Request.ProcessIndex != 0 || item.Request.ProcessKind == ProcessKind.Measured && item.Request.ProcessIndex is < 1 or > 3)) return TargetValidation.Invalid(anchor, "A target requires warmup index 0 and measured indices 1, 2, and 3 exactly once.");
        if (artifacts.Any(item => item.Protocol != BenchmarkProtocol.Acceptance || !item.CorrectnessPassed)) return TargetValidation.Invalid(anchor, "A target has incomplete acceptance protocol or correctness evidence.");
        var correctness = artifacts[0].Correctness;
        if (artifacts.Any(item => !SameEvidence(correctness, item.Correctness))) return TargetValidation.Invalid(anchor, "Correctness, provider prerequisite, native routes, or native-plan evidence is not stable within the target.");
        var warmup = artifacts.Single(item => item.Request.ProcessKind == ProcessKind.Warmup);
        if (warmup.Operations.Count != 0) return TargetValidation.Invalid(anchor, "Warmup artifacts must not contain timed operation samples.");
        var measured = artifacts.Where(item => item.Request.ProcessKind == ProcessKind.Measured).OrderBy(item => item.Request.ProcessIndex).ToArray();
        var names = measured[0].Operations.Select(operation => operation.Operation).Order(StringComparer.Ordinal).ToArray();
        if (names.Length == 0 || names.Distinct(StringComparer.Ordinal).Count() != names.Length || measured.Any(item => !names.SequenceEqual(item.Operations.Select(operation => operation.Operation).Order(StringComparer.Ordinal), StringComparer.Ordinal) || item.Operations.Any(operation => operation.Count < 100 || operation.SteadyStateSeconds < 30 || operation.RawLatenciesMilliseconds.Count != operation.Count))) return TargetValidation.Invalid(anchor, "Measured runs must contain identical non-empty operation sets with complete raw samples.");
        return TargetValidation.Validated(anchor, correctness, names);
    }

    private static bool SameTargetTuple(RunRequest first, RunRequest second) => first.WorkloadId == second.WorkloadId && first.WorkloadVersion == second.WorkloadVersion && first.Provider == second.Provider && first.Adapter == second.Adapter && first.PhysicalForm == second.PhysicalForm && first.Scale == second.Scale && first.CommitSha == second.CommitSha && first.CompositionFingerprint == second.CompositionFingerprint && first.Seed == second.Seed && first.InputFingerprintSha256 == second.InputFingerprintSha256 && first.NativePlanIdentity == second.NativePlanIdentity && first.NativePlanEvidenceReference == second.NativePlanEvidenceReference && first.PackageVersions.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(second.PackageVersions.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    private static bool SameEvidence(CorrectnessEvidence first, CorrectnessEvidence second) => first.ObservedResultDigestSha256 == second.ObservedResultDigestSha256 && first.ProviderPrerequisite == second.ProviderPrerequisite && first.NativeRoutes.SequenceEqual(second.NativeRoutes, StringComparer.Ordinal) && first.EvidenceReferences.SequenceEqual(second.EvidenceReferences, StringComparer.Ordinal);
    private static IReadOnlyList<ProcessAggregate> Aggregate(IEnumerable<ProcessArtifact> artifacts) => artifacts.Where(item => item.Request.ProcessKind == ProcessKind.Measured).SelectMany(item => item.Operations.Select(operation => (item.Request.ProcessIndex, Operation: operation))).GroupBy(item => item.Operation.Operation, StringComparer.Ordinal).Select(group => new ProcessAggregate(group.Key, group.OrderBy(item => item.ProcessIndex).Select(item => item.Operation.P50Milliseconds).ToArray(), group.OrderBy(item => item.ProcessIndex).Select(item => item.Operation.P95Milliseconds).ToArray(), group.OrderBy(item => item.ProcessIndex).Select(item => item.Operation.P99Milliseconds).ToArray(), group.OrderBy(item => item.ProcessIndex).Select(item => item.Operation.ThroughputPerSecond).ToArray(), group.ToDictionary(item => item.ProcessIndex, item => item.Operation.RawLatenciesMilliseconds, EqualityComparer<int>.Default))).OrderBy(item => item.Operation, StringComparer.Ordinal).ToArray();
    private sealed record TargetValidation(bool Valid, RunRequest? Anchor, CorrectnessEvidence? Correctness, IReadOnlyList<string> OperationNames, string? Error)
    {
        public static TargetValidation Invalid(RunRequest? anchor, string error) => new(false, anchor, null, [], error);
        public static TargetValidation Validated(RunRequest anchor, CorrectnessEvidence correctness, IReadOnlyList<string> names) => new(true, anchor, correctness, names, null);
    }
}
