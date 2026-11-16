using System.Diagnostics;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Runs a real command - <c>dotnet</c> or <c>git</c> - as a child process, for the suites that prove what the root build
/// files do to an actual build or pack rather than describing it.
/// </summary>
internal static class ChildProcess
{
    /// <summary>Runs the <c>dotnet</c> this test run was launched with.</summary>
    internal static (int ExitCode, string Output) Dotnet(IEnumerable<string> arguments, string? workingDirectory = null, TimeSpan? timeout = null) =>
        Run(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet", arguments, workingDirectory, timeout);

    /// <summary>
    /// Runs <paramref name="fileName"/> to completion and returns its exit code with its standard output and error
    /// together; kills it and throws when it outlives <paramref name="timeout"/> (two minutes by default).
    /// </summary>
    internal static (int ExitCode, string Output) Run(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? string.Empty
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // dotnet test's own build/vstest launch leaves MSBuild-specific variables (MSBUILD_EXE_PATH,
        // MSBuildExtensionsPath, MSBuildSDKsPath) in this process's environment, pointing at the test
        // host's build context. Inherited by the child process, they make a spawned `dotnet msbuild`
        // resolve the wrong SDK/props set instead of its own, which breaks evaluating the fixture
        // project outright rather than merely producing a different — still checkable — result.
        startInfo.Environment.Remove("MSBUILD_EXE_PATH");
        startInfo.Environment.Remove("MSBuildExtensionsPath");
        startInfo.Environment.Remove("MSBuildSDKsPath");

        using var process = Process.Start(startInfo)!;

        // Start both reads before blocking on exit: stdout and stderr are separate pipes with bounded
        // buffers, so reading them sequentially can deadlock if the child fills one while this process
        // is still blocked reading the other.
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        var limit = timeout ?? TimeSpan.FromMinutes(2);
        var exited = process.WaitForExit(limit);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        var output = stdOutTask.GetAwaiter().GetResult() + stdErrTask.GetAwaiter().GetResult();

        if (!exited)
            throw new TimeoutException(
                $"{Path.GetFileName(fileName)} {string.Join(' ', startInfo.ArgumentList)} did not exit within {limit} and was killed. Output so far:\n{output}");

        return (process.ExitCode, output);
    }
}
