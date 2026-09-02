using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

internal sealed record AdmittedAdapterRun(
    RunRequest Request,
    string OutputDirectory,
    string ConnectionString,
    PerformanceWorkload Workload);

/// <summary>
/// Admits direct run and correctness commands before resolving provider connection settings. This is
/// especially important for the SQLite-only EF Secret comparator: a rejected non-SQLite request must
/// not require, probe, or reveal that provider's environment-backed connection string.
/// </summary>
internal static class SecretRunAdmission
{
    internal static AdmittedAdapterRun ParseAndResolve(
        string[] args,
        string command,
        Func<string, string> connectionResolver)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(connectionResolver);

        var request = RunRequestWire.Parse(HostArguments.Require(args, command, "--request"));
        var outputDirectory = HostArguments.Require(args, command, "--out");
        var repositoryRoot = SourceProvenance.FindRepositoryRoot();
        outputDirectory = ArtifactOutputAdmission.RequireExternal(outputDirectory, repositoryRoot);
        var catalog = WorkloadCatalog.Load(repositoryRoot);
        var workload = catalog.Workloads.TryGetValue(request.WorkloadId, out var candidate)
            ? candidate
            : throw new PerformanceContractException($"Workload '{request.WorkloadId}' is not in the frozen catalog.");
        if (string.Equals(command, "verify-correctness", StringComparison.Ordinal) &&
            string.Equals(request.WorkloadId, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal))
            ArtifactAdmission.ValidateEvidenceRequest(workload, request);
        else
            ArtifactAdmission.ValidateRequest(workload, request);
        ProviderPackageProvenance.RequireExactCurrent(
            repositoryRoot,
            request.Adapter,
            request.Provider,
            request.PackageVersions);
        return new AdmittedAdapterRun(
            request,
            outputDirectory,
            connectionResolver(request.Provider),
            workload);
    }
}
