using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Native-plan evidence is captured once, before the matrix runs, and republished byte-for-byte by every
/// child process.
///
/// The split exists because <c>matrix</c> takes <c>--native-plan-sha256</c> as an input: the operator
/// commits to a content digest before any child starts, so a child that re-captured its own plan would
/// have to reproduce that digest byte-exactly across four processes and (for server providers) across four
/// freshly started containers. Capturing once and copying removes the entire class of nondeterminism, and
/// it keeps plan capture off the timed path.
///
/// Note that <see cref="ArtifactAdmission.ValidateCorrectness"/> demands an evidence document even for a
/// workload whose <c>requiredNativeRoutes</c> is empty — the document is the provenance binding, and the
/// route list is only part of it. A routeless workload therefore still needs a captured document; it just
/// needs no raw provider-plan files alongside it.
/// </summary>
internal static class NativePlanEvidenceStaging
{
    public const string StagingDirectoryVariable = "ELSA_BENCH_NATIVE_PLAN_STAGING";

    /// <summary>
    /// The <c>.native-plan.json</c> suffix is load-bearing: <c>SafeRawPlanReference</c> rejects it, so an
    /// evidence document can never be mistaken for — or cross-registered as — a raw provider plan.
    /// </summary>
    public static string ReferenceFor(string workloadId, string provider) => $"{workloadId}.{provider}.native-plan.json";

    public static string Write(string directory, NativePlanEvidenceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ReferenceFor(document.WorkloadId, document.Provider));
        File.WriteAllText(path, JsonSerializer.Serialize(document, ArtifactStore.JsonOptions));
        return Sha256(path);
    }

    /// <summary>
    /// Copies the staged evidence (and any raw provider plans it references) into the artifact directory.
    /// Idempotent by necessity: four child processes share one output directory, and the matrix runner
    /// re-reads and re-validates every file after each child, so the second child must find the first
    /// child's bytes unchanged rather than rewriting them.
    /// </summary>
    public static NativePlanEvidenceDocument PublishInto(string outputDirectory, RunRequest request)
    {
        var reference = request.NativePlanEvidenceReference;
        var destination = Path.Combine(outputDirectory, reference);
        if (!File.Exists(destination))
        {
            var staging = Environment.GetEnvironmentVariable(StagingDirectoryVariable);
            if (string.IsNullOrWhiteSpace(staging))
                throw new PerformanceContractException(
                    $"Native-plan evidence '{reference}' is not in the artifact directory and {StagingDirectoryVariable} is unset. " +
                    "Run 'capture-plan' first and point that variable at its output directory.");
            var source = Path.Combine(staging, reference);
            if (!File.Exists(source))
                throw new PerformanceContractException($"Staged native-plan evidence '{reference}' was not found under {staging}.");
            Directory.CreateDirectory(outputDirectory);
            File.Copy(source, destination);
            foreach (var rawPlan in RawPlanReferences(source))
                CopyRawPlan(staging, outputDirectory, rawPlan);
        }

        var digest = Sha256(destination);
        if (!string.Equals(digest, request.NativePlanContentSha256, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Staged native-plan evidence '{reference}' hashes to {digest}, not the requested --native-plan-sha256 {request.NativePlanContentSha256}. " +
                "Recapture the plan or correct the matrix argument; the harness will not accept a mismatched commitment.");
        return Read(destination);
    }

    public static NativePlanEvidenceDocument Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using (var json = JsonDocument.Parse(bytes)) ArtifactStore.RejectDuplicateProperties(json.RootElement);
        return JsonSerializer.Deserialize<NativePlanEvidenceDocument>(bytes, ArtifactStore.JsonOptions)
               ?? throw new PerformanceContractException($"Native-plan evidence document '{path}' is invalid.");
    }

    public static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static IEnumerable<string> RawPlanReferences(string evidencePath) =>
        Read(evidencePath).Routes.Select(route => route.RawPlanReference).Distinct(StringComparer.Ordinal);

    private static void CopyRawPlan(string staging, string outputDirectory, string reference)
    {
        var destination = Path.Combine(outputDirectory, reference);
        if (File.Exists(destination)) return;
        var source = Path.Combine(staging, reference);
        if (!File.Exists(source))
            throw new PerformanceContractException($"Staged raw provider-plan '{reference}' was not found under {staging}.");
        File.Copy(source, destination);
    }
}
