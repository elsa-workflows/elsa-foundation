using System.Diagnostics;
using System.Text.Json;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

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

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var build = root.GetProperty("Build");
            if (root.GetProperty("SchemaVersion").GetInt32() != 2 ||
                !string.Equals(build.GetProperty("AdapterHostRevision").GetString(), request.CommitSha, StringComparison.Ordinal) ||
                !string.Equals(build.GetProperty("HarnessRevision").GetString(), request.CommitSha, StringComparison.Ordinal) ||
                !root.GetProperty("Registrations").EnumerateArray().Any(registration => Matches(registration, request)))
                throw new PerformanceContractException(
                    "The canonical AdapterHost catalog does not admit the exact matrix request or source revision.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new PerformanceContractException(
                $"The canonical AdapterHost emitted an invalid matrix catalog: {exception.Message}");
        }
        return admittedChild;
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
