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
        // Admitted before use for the same reason as the raw plans below: ArtifactSafety screens the
        // request for connection material, not for path traversal, and the destination is built from it.
        var reference = ArtifactStore.EvidenceName(request.NativePlanEvidenceReference);
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

    /// <summary>
    /// Copies one already-admitted reference. Callers must pass a name that has been through
    /// <see cref="ArtifactStore.EvidenceName"/> or <see cref="ArtifactStore.RawPlanName"/> first — this
    /// method does no checking of its own, and the parameter name says so.
    /// </summary>
    private static void CopyFromStaging(string outputDirectory, string admittedReference)
    {
        Directory.CreateDirectory(outputDirectory);
        File.Copy(
            Path.Combine(RequireStaging(admittedReference), admittedReference),
            Path.Combine(outputDirectory, admittedReference));
    }

    /// <summary>
    /// Admits the reference before it touches the filesystem.
    ///
    /// The route list is deserialized from the staged document, so <c>RawPlanReference</c> is untrusted
    /// input: a value like <c>../outside.txt</c> resolves outside both the staging and artifact roots.
    /// <c>ArtifactAdmission.ValidateCorrectness</c> does apply <c>SafeRawPlanReference</c>, but only after
    /// this runs, so relying on it would mean the copy has already happened by the time the reference is
    /// rejected. <see cref="ArtifactStore.RawPlanName"/> is the harness's own checked resolver and throws
    /// on anything that is not a safe top-level name.
    /// </summary>
    private static void EnsureRawPlan(string outputDirectory, string reference)
    {
        var admitted = ArtifactStore.RawPlanName(reference);
        if (File.Exists(Path.Combine(outputDirectory, admitted))) return;
        CopyFromStaging(outputDirectory, admitted);
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
