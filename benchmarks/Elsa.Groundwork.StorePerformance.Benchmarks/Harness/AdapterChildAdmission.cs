using System.Diagnostics;
using System.Text.Json;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

internal static class MatrixCatalogContract
{
    public const int SchemaVersion = 3;
}

/// <summary>Binds matrix execution to the repository's current AdapterHost control plane.</summary>
public static class AdapterChildAdmission
{
    public static async Task<string> RequireAsync(
        string repositoryRoot,
        string childCommand,
        RunRequest request,
        CancellationToken cancellationToken)
    {
        var admittedChild = RequireCanonicalPath(repositoryRoot, childCommand);
        var start = new ProcessStartInfo(admittedChild)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repositoryRoot
        };
        start.ArgumentList.Add("describe-matrix");
        using var process = Process.Start(start)
                            ?? throw new PerformanceContractException("Could not start the AdapterHost admission handshake.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cancellationToken));
        var output = await outputTask;
        if (process.ExitCode != 0)
            throw new PerformanceContractException(
                "The canonical AdapterHost did not admit its current matrix catalog; rebuild it from clean current source.");

        ValidateCatalog(output, request);
        return admittedChild;
    }

    internal static void ValidateCatalog(string output, RunRequest request)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var build = root.GetProperty("Build");
            var schemaVersion = root.GetProperty("SchemaVersion").GetInt32();
            if (schemaVersion != MatrixCatalogContract.SchemaVersion)
                throw new PerformanceContractException(
                    $"The canonical AdapterHost catalog uses schema version {schemaVersion}; expected {MatrixCatalogContract.SchemaVersion}.");

            var adapterHostRevision = build.GetProperty("AdapterHostRevision").GetString();
            if (!string.Equals(adapterHostRevision, request.CommitSha, StringComparison.Ordinal))
                throw new PerformanceContractException(
                    $"The canonical AdapterHost catalog was built from '{adapterHostRevision}', not requested commit '{request.CommitSha}'.");

            var harnessRevision = build.GetProperty("HarnessRevision").GetString();
            if (!string.Equals(harnessRevision, request.CommitSha, StringComparison.Ordinal))
                throw new PerformanceContractException(
                    $"The canonical AdapterHost catalog references harness '{harnessRevision}', not requested commit '{request.CommitSha}'.");

            if (!root.GetProperty("Registrations").EnumerateArray().Any(registration => Matches(registration, request)))
                throw new PerformanceContractException(
                    "The canonical AdapterHost catalog does not register exact target " +
                    $"'{request.WorkloadId}/{request.WorkloadVersion}/{request.Adapter}/{request.PhysicalForm}/{request.Provider}'.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new PerformanceContractException(
                $"The canonical AdapterHost emitted an invalid matrix catalog: {exception.Message}");
        }
    }

    internal static string RequireCanonicalPath(string repositoryRoot, string childCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childCommand);
        var executable = OperatingSystem.IsWindows()
            ? "Elsa.Groundwork.StorePerformance.AdapterHost.exe"
            : "Elsa.Groundwork.StorePerformance.AdapterHost";
        var expected = ArtifactOutputAdmission.Canonicalize(Path.Combine(
            repositoryRoot,
            "benchmarks",
            "Elsa.Groundwork.StorePerformance.AdapterHost",
            "bin",
            "Release",
            "net10.0",
            executable));
        var actual = ArtifactOutputAdmission.Canonicalize(childCommand);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(actual, expected, comparison) || !File.Exists(actual))
            throw new PerformanceContractException(
                $"matrix --child-command must be the canonical Release AdapterHost executable: {expected}");
        return actual;
    }

    private static bool Matches(JsonElement registration, RunRequest request) =>
        string.Equals(registration.GetProperty("WorkloadId").GetString(), request.WorkloadId, StringComparison.Ordinal) &&
        string.Equals(registration.GetProperty("WorkloadVersion").GetString(), request.WorkloadVersion, StringComparison.Ordinal) &&
        string.Equals(registration.GetProperty("Adapter").GetString(), request.Adapter, StringComparison.Ordinal) &&
        string.Equals(registration.GetProperty("PhysicalForm").GetString(), request.PhysicalForm, StringComparison.Ordinal) &&
        registration.GetProperty("Providers").EnumerateArray().Any(provider =>
            string.Equals(provider.GetString(), request.Provider, StringComparison.Ordinal));
}
