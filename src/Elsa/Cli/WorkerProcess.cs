using Elsa.Cli.Worker;
using System.Diagnostics;
using System.Text.Json;

namespace Elsa.Cli;

/// <summary>
/// Launches the worker inside the target host's dependency closure and exchanges one request for one
/// response with it (FR-002, FR-003).
/// </summary>
/// <remarks>
/// <para>
/// The launch carries four arguments and nothing else: the host's runtimeconfig, the host's deps file, and
/// the worker assembly. Everything the command actually asks for travels over stdin. That is not a style
/// choice — process arguments are world-readable on every platform this runs on, and <c>apply</c>/<c>validate</c>
/// (#1876) take a connection string. This method's signature has nowhere for one to land: it is built from
/// the host layout alone, never from a request.
/// </para>
/// <para>
/// The worker's stderr is inherited rather than captured, so its warnings reach the operator as they happen,
/// and stdout carries the response alone.
/// </para>
/// </remarks>
public static class WorkerProcess
{
    public const string WorkerAssemblyFileName = "Elsa.Cli.Worker.dll";

    /// <summary>
    /// The exact argument list handed to the muxer. Separated from the launch so it can be asserted on
    /// directly: what must never appear here is as much the point as what must.
    /// </summary>
    public static IReadOnlyList<string> Arguments(HostLayout host, string workerAssembly) =>
        ["exec", "--runtimeconfig", host.RuntimeConfig, "--depsfile", host.DepsFile, workerAssembly];

    internal static async Task<WorkerResponse> RunAsync(HostLayout host, WorkerRequest request, CancellationToken cancellationToken)
    {
        var worker = Path.Join(AppContext.BaseDirectory, WorkerAssemblyFileName);
        if (!File.Exists(worker))
        {
            throw CliRefusal.Resolution(
                "worker-missing",
                $"This dotnet-elsa installation is incomplete: '{WorkerAssemblyFileName}' is not beside the tool at '{AppContext.BaseDirectory}'.");
        }

        var startInfo = new ProcessStartInfo(DotnetMuxer.Path())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in Arguments(host, worker))
            startInfo.ArgumentList.Add(argument);

        using var process = Start(startInfo);
        await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, request, WorkerContract.Json, cancellationToken);
        await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Terminate(process);
            throw;
        }

        return Parse(output, process.ExitCode);
    }

    private static Process Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw CliRefusal.Resolution("worker-start-failed", "The worker process could not be started.");
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw CliRefusal.Resolution(
                "worker-start-failed",
                $"The worker process could not be started with '{startInfo.FileName}': {failure.Message}");
        }
    }

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The process is already gone, which is the state this call wanted.
        }
    }

    /// <summary>
    /// A worker that answered anything but one response document is a worker that did not run: reporting
    /// what it did say, and with which code it exited, is the difference between a diagnosable failure and
    /// a silent one.
    /// </summary>
    private static WorkerResponse Parse(string output, int exitCode)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            try
            {
                if (JsonSerializer.Deserialize<WorkerResponse>(output, WorkerContract.Json) is { } response)
                    return response;
            }
            catch (JsonException failure)
            {
                throw CliRefusal.Resolution(
                    "worker-response-invalid",
                    $"The worker exited with code {exitCode} and did not answer with a response this tool understands: {failure.Message}",
                    [Excerpt(output)]);
            }
        }

        throw CliRefusal.Resolution(
            "worker-no-response",
            $"The worker exited with code {exitCode} without answering. Its own diagnostics, if any, were written above.");
    }

    private static string Excerpt(string output) =>
        output.Length <= 500 ? output.Trim() : output[..500].Trim() + "…";
}

/// <summary>
/// Finds the <c>dotnet</c> that will run the worker.
/// </summary>
/// <remarks>
/// A tool installed as <c>dotnet-elsa</c> runs from its own apphost, so the muxer is not this process, and
/// the environment is consulted in the order that gets the same runtime the tool itself is running on:
/// what the SDK told us, then this process when it is the muxer, then an explicit install root, then the
/// path.
/// </remarks>
public static class DotnetMuxer
{
    public static string Path()
    {
        if (System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath && File.Exists(hostPath))
            return hostPath;

        if (System.Environment.ProcessPath is { Length: > 0 } processPath &&
            System.IO.Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        if (System.Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root)
        {
            var candidate = System.IO.Path.Join(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }

        return "dotnet";
    }
}
