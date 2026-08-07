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
            CopyFromStaging(outputDirectory, reference);

        var digest = Sha256(destination);
        if (!string.Equals(digest, request.NativePlanContentSha256, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Staged native-plan evidence '{reference}' hashes to {digest}, not the requested --native-plan-sha256 {request.NativePlanContentSha256}. " +
                "Recapture the plan or correct the matrix argument; the harness will not accept a mismatched commitment.");

        // Raw plans are reconciled on every child, not only on the one that copied the document.
        // ProcessMatrixRunner supports resuming a cohort whose directory is already partly populated, so a
        // document that arrived without its raw plans is a reachable state; leaving them uncopied would
        // fail correctness with a message pointing at the raw plan rather than at the real gap.
        var document = Read(destination);
        foreach (var route in document.Routes)
            EnsureRawPlan(outputDirectory, route.RawPlanReference);
        return document;
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

    private static void CopyFromStaging(string outputDirectory, string reference)
    {
        Directory.CreateDirectory(outputDirectory);
        File.Copy(Path.Combine(RequireStaging(reference), reference), Path.Combine(outputDirectory, reference));
    }

    private static void EnsureRawPlan(string outputDirectory, string reference)
    {
        if (File.Exists(Path.Combine(outputDirectory, reference))) return;
        CopyFromStaging(outputDirectory, reference);
    }

    /// <summary>
    /// Resolves the staging directory, and is reached only when something is actually missing — a fully
    /// populated artifact directory must not require the variable at all, because the runner re-invokes
    /// children long after <c>capture-plan</c> ran.
    /// </summary>
    private static string RequireStaging(string reference)
    {
        var staging = Environment.GetEnvironmentVariable(StagingDirectoryVariable);
        if (string.IsNullOrWhiteSpace(staging))
            throw new PerformanceContractException(
                $"'{reference}' is not in the artifact directory and {StagingDirectoryVariable} is unset. " +
                "Run 'capture-plan' first and point that variable at its output directory.");
        if (!File.Exists(Path.Combine(staging, reference)))
            throw new PerformanceContractException($"Staged evidence '{reference}' was not found under {staging}.");
        return staging;
    }
}
