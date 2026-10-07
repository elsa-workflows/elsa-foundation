using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed class ResponseReplaySafetyProcessTests
{
    private const string ResultPrefix = "RESPONSE_REPLAY_RESULT=";

    [Fact]
    public async Task CapturedExternalClosure_ImportsAndExecutesThroughChildHttpHost()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixtureDirectory = Path.Combine(repositoryRoot,
            "tests", "essentials", "Activities", "Http", "IntegrationTests", "Fixtures", "ResponseReplayHost", "Fixtures");
        var closurePath = Path.Combine(fixtureDirectory, "pre-candidate-external-closure.json");
        var manifestPath = Path.Combine(fixtureDirectory, "pre-candidate-external-manifest.json");
        var childProjectDirectory = Path.Combine(repositoryRoot,
            "tests", "essentials", "Activities", "Http", "IntegrationTests", "Fixtures", "ResponseReplayHost");

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var publication = manifest.RootElement.GetProperty("publication");
        var workload = manifest.RootElement.GetProperty("workload");
        var closureBytes = await File.ReadAllBytesAsync(closurePath);
        var closureHash = Convert.ToHexString(SHA256.HashData(closureBytes)).ToLowerInvariant();
        using var closure = JsonDocument.Parse(closureBytes);
        Assert.Equal(publication.GetProperty("artifactHash").GetString(),
            closure.RootElement.GetProperty("artifacts")[0].GetProperty("identity").GetProperty("artifactHash").GetString());
        Assert.Equal(manifest.RootElement.GetProperty("export").GetProperty("sha256").GetString(), closureHash);

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var childDll = Path.Combine(childProjectDirectory, "bin", configuration, "net10.0", "ResponseReplayHost.dll");
        Assert.True(File.Exists(childDll), $"The referenced child project was not built: {childDll}");

        var ownedRoot = Path.Combine(Path.GetTempPath(), $"elsa-response-replay-t002-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ownedRoot);
        var databasePath = Path.Combine(ownedRoot, "runtime.db");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add(childDll);
        process.StartInfo.ArgumentList.Add(closurePath);
        process.StartInfo.ArgumentList.Add(databasePath);
        ClearInheritedPersistenceRedirects(process.StartInfo.Environment);

        var started = false;
        var passed = false;
        try
        {
            Assert.True(process.Start(), "The response-replay child process did not start.");
            started = true;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                var timedOutStdout = await stdoutTask;
                var timedOutStderr = await stderrTask;
                await SaveChildLogsAsync(ownedRoot, timedOutStdout, timedOutStderr);
                Assert.Fail($"The response-replay child exceeded 90 seconds. Owned child data and logs retained at {ownedRoot}.\n" + timedOutStdout + "\n" + timedOutStderr);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            await SaveChildLogsAsync(ownedRoot, stdout, stderr);
            Assert.True(process.ExitCode == 0, $"Child exited {process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            var resultLine = Assert.Single(
                stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
                line => line.StartsWith(ResultPrefix, StringComparison.Ordinal));
            using var result = JsonDocument.Parse(resultLine[ResultPrefix.Length..]);
            var resultRoot = result.RootElement;

            Assert.Equal(closureHash, resultRoot.GetProperty("closureSha256").GetString());
            Assert.Equal(publication.GetProperty("artifactId").GetString(), resultRoot.GetProperty("artifactId").GetString());
            Assert.Equal(publication.GetProperty("artifactHash").GetString(), resultRoot.GetProperty("artifactHash").GetString());
            Assert.Equal(publication.GetProperty("workflowDefinitionId").GetString(), resultRoot.GetProperty("definitionId").GetString());
            Assert.Equal(publication.GetProperty("workflowDefinitionVersionId").GetString(), resultRoot.GetProperty("definitionVersionId").GetString());
            Assert.Equal(publication.GetProperty("resolvedWriteHttpResponseProfile").GetString(), resultRoot.GetProperty("responseProfile").GetString());
            Assert.Equal(publication.GetProperty("responseNodeId").GetString(), resultRoot.GetProperty("responseNodeId").GetString());
            Assert.Equal(publication.GetProperty("artifactId").GetString(), resultRoot.GetProperty("pinnedArtifactId").GetString());
            Assert.Equal(publication.GetProperty("artifactHash").GetString(), resultRoot.GetProperty("pinnedArtifactHash").GetString());
            Assert.Equal("Completed", resultRoot.GetProperty("executionStatus").GetString());
            Assert.Equal(workload.GetProperty("responseStatus").GetInt32(), resultRoot.GetProperty("responseStatus").GetInt32());
            Assert.Equal(workload.GetProperty("responseBody").GetString(), resultRoot.GetProperty("responseBody").GetString());
            Assert.Equal(workload.GetProperty("responseBody").GetString(), resultRoot.GetProperty("committedResponseBody").GetString());
            Assert.False(string.IsNullOrWhiteSpace(resultRoot.GetProperty("executionId").GetString()));
            passed = true;
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            if (passed)
                Directory.Delete(ownedRoot, recursive: true);
            else
                Console.Error.WriteLine($"Response-replay child DB and logs retained at {ownedRoot}.");
        }
    }

    private static async Task SaveChildLogsAsync(string ownedRoot, string stdout, string stderr)
    {
        await File.WriteAllTextAsync(Path.Combine(ownedRoot, "child.stdout.log"), stdout);
        await File.WriteAllTextAsync(Path.Combine(ownedRoot, "child.stderr.log"), stderr);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;

        throw new DirectoryNotFoundException("Could not find the repository root containing Elsa.Server.slnx.");
    }

    private static void ClearInheritedPersistenceRedirects(IDictionary<string, string?> environment)
    {
        foreach (var name in environment.Keys.ToArray())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(name, "ConnectionStrings__Elsa") ||
                name.StartsWith("Elsa__Persistence__", StringComparison.OrdinalIgnoreCase) ||
                (name.StartsWith("CShells__Shells__", StringComparison.OrdinalIgnoreCase) &&
                 name.EndsWith("__Persistence", StringComparison.OrdinalIgnoreCase)))
            {
                environment.Remove(name);
            }
        }
    }
}
