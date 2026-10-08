using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed class ResponseReplaySafetyProcessTests
{
    private const string ResultPrefix = "RESPONSE_REPLAY_RESULT=";
    private const string PublicationResultPrefix = "RESPONSE_REPLAY_PUBLICATION_RESULT=";

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

        var passed = false;
        try
        {
            var child = await RunChildProcessAsync(
                childDll, ownedRoot, TimeSpan.FromSeconds(90), "response-replay child", closurePath, databasePath);
            Assert.True(child.ExitCode == 0, $"Child exited {child.ExitCode}.\nstdout:\n{child.Stdout}\nstderr:\n{child.Stderr}");
            var resultLine = Assert.Single(
                child.Stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
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
            if (passed)
                Directory.Delete(ownedRoot, recursive: true);
            else
                Console.Error.WriteLine($"Response-replay child DB and logs retained at {ownedRoot}.");
        }
    }

    [Fact]
    public async Task NormalPublication_PreservesExternalBaselineAndExecutesReplaySafeCandidate()
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
        using var baselineClosure = JsonDocument.Parse(closureBytes);
        Assert.Equal(manifest.RootElement.GetProperty("export").GetProperty("sha256").GetString(), closureHash);
        Assert.Equal(publication.GetProperty("artifactHash").GetString(),
            baselineClosure.RootElement.GetProperty("artifacts")[0].GetProperty("identity").GetProperty("artifactHash").GetString());

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var childDll = Path.Combine(childProjectDirectory, "bin", configuration, "net10.0", "ResponseReplayHost.dll");
        Assert.True(File.Exists(childDll), $"The referenced child project was not built: {childDll}");

        var ownedRoot = Path.Combine(Path.GetTempPath(), $"elsa-response-replay-t007-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ownedRoot);
        var databasePath = Path.Combine(ownedRoot, "runtime.db");

        var passed = false;
        try
        {
            var child = await RunChildProcessAsync(
                childDll, ownedRoot, TimeSpan.FromSeconds(150), "response-replay publication child",
                "--publication-proof", closurePath, databasePath, ownedRoot);
            Assert.True(child.ExitCode == 0, $"Child exited {child.ExitCode}.\nstdout:\n{child.Stdout}\nstderr:\n{child.Stderr}");
            var resultLine = Assert.Single(
                child.Stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
                line => line.StartsWith(PublicationResultPrefix, StringComparison.Ordinal));
            using var result = JsonDocument.Parse(resultLine[PublicationResultPrefix.Length..]);
            var resultRoot = result.RootElement;

            Assert.Equal(closureHash, resultRoot.GetProperty("baselineClosureSha256").GetString());
            Assert.Equal(publication.GetProperty("artifactId").GetString(), resultRoot.GetProperty("baselineArtifactId").GetString());
            Assert.Equal(publication.GetProperty("artifactHash").GetString(), resultRoot.GetProperty("baselineArtifactHash").GetString());
            Assert.Equal(publication.GetProperty("workflowDefinitionId").GetString(), resultRoot.GetProperty("baselineDefinitionId").GetString());
            Assert.Equal(publication.GetProperty("workflowDefinitionVersionId").GetString(), resultRoot.GetProperty("baselineDefinitionVersionId").GetString());
            Assert.Equal("External", resultRoot.GetProperty("baselineProfile").GetString());
            Assert.Equal(workload.GetProperty("responseStatus").GetInt32(), resultRoot.GetProperty("baselineHttpStatus").GetInt32());
            Assert.Equal(workload.GetProperty("responseBody").GetString(), resultRoot.GetProperty("baselineHttpBody").GetString());
            Assert.Equal("text/plain", resultRoot.GetProperty("baselineHttpContentType").GetString());
            Assert.Equal("Alice", resultRoot.GetProperty("baselinePersistedFirstName").GetString());
            Assert.Equal("Smith", resultRoot.GetProperty("baselinePersistedLastName").GetString());
            Assert.Equal("Alice Smith", resultRoot.GetProperty("baselineReferenceText").GetString());
            Assert.Equal(0, resultRoot.GetProperty("baselineCommittedHeaderCount").GetInt32());
            Assert.Equal(0, resultRoot.GetProperty("baselineDeliveredAuthoredHeaderCount").GetInt32());

            Assert.NotEqual(resultRoot.GetProperty("baselineArtifactId").GetString(), resultRoot.GetProperty("candidateArtifactId").GetString());
            Assert.NotEqual(resultRoot.GetProperty("baselineDefinitionId").GetString(), resultRoot.GetProperty("candidateDefinitionId").GetString());
            Assert.Equal("ReplaySafe", resultRoot.GetProperty("candidateProfile").GetString());
            Assert.Equal(resultRoot.GetProperty("candidateArtifactHash").GetString(), resultRoot.GetProperty("candidateExecutableArtifactHash").GetString());
            Assert.Equal(1, resultRoot.GetProperty("activeSharedRouteBindingCount").GetInt32());
            Assert.Equal(resultRoot.GetProperty("candidateArtifactId").GetString(), resultRoot.GetProperty("activeSharedRouteArtifactId").GetString());
            Assert.Equal(workload.GetProperty("responseStatus").GetInt32(), resultRoot.GetProperty("candidateHttpStatus").GetInt32());
            Assert.Equal(workload.GetProperty("responseBody").GetString(), resultRoot.GetProperty("candidateHttpBody").GetString());
            Assert.Equal("text/plain", resultRoot.GetProperty("candidateHttpContentType").GetString());
            Assert.Equal(workload.GetProperty("responseStatus").GetInt32(), resultRoot.GetProperty("candidateCommittedStatusCode").GetInt32());
            Assert.Equal(workload.GetProperty("responseBody").GetString(), resultRoot.GetProperty("candidateCommittedBody").GetString());
            Assert.Equal("text/plain", resultRoot.GetProperty("candidateCommittedContentType").GetString());
            Assert.Equal(0, resultRoot.GetProperty("candidateCommittedHeaderCount").GetInt32());
            Assert.Equal(0, resultRoot.GetProperty("candidateDeliveredAuthoredHeaderCount").GetInt32());
            Assert.Equal("Alice", resultRoot.GetProperty("candidatePersistedFirstName").GetString());
            Assert.Equal("Smith", resultRoot.GetProperty("candidatePersistedLastName").GetString());
            Assert.Equal("Alice Smith", resultRoot.GetProperty("candidateReferenceText").GetString());
            Assert.Equal("Completed", resultRoot.GetProperty("candidateExecutionStatus").GetString());

            var candidateClosurePath = Path.Combine(ownedRoot, resultRoot.GetProperty("candidateClosureFile").GetString()!);
            var candidateClosureBytes = await File.ReadAllBytesAsync(candidateClosurePath);
            var candidateClosureHash = Convert.ToHexString(SHA256.HashData(candidateClosureBytes)).ToLowerInvariant();
            Assert.Equal(resultRoot.GetProperty("candidateClosureSha256").GetString(), candidateClosureHash);
            using var candidateClosure = JsonDocument.Parse(candidateClosureBytes);
            AssertAuthoredBehaviorMatches(baselineClosure.RootElement, candidateClosure.RootElement,
                publication.GetProperty("artifactId").GetString()!, resultRoot.GetProperty("candidateArtifactId").GetString()!);

            Assert.Equal(200, resultRoot.GetProperty("restAdmissionStatus").GetInt32());
            Assert.Equal("Completed", resultRoot.GetProperty("restExecutionStatus").GetString());
            Assert.Equal(200, resultRoot.GetProperty("restCommittedStatusCode").GetInt32());
            Assert.Equal("Alice Smith", resultRoot.GetProperty("restCommittedBody").GetString());
            Assert.Equal("text/plain", resultRoot.GetProperty("restCommittedContentType").GetString());
            Assert.Equal(0, resultRoot.GetProperty("restCommittedHeaderCount").GetInt32());
            Assert.Equal("Alice", resultRoot.GetProperty("restPersistedFirstName").GetString());
            Assert.Equal("Smith", resultRoot.GetProperty("restPersistedLastName").GetString());
            Assert.Equal("Alice Smith", resultRoot.GetProperty("restReferenceText").GetString());
            Assert.NotEqual(resultRoot.GetProperty("candidateArtifactId").GetString(), resultRoot.GetProperty("restCompanionArtifactId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(resultRoot.GetProperty("candidateHttpExecutionId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(resultRoot.GetProperty("restExecutionId").GetString()));
            passed = true;
        }
        finally
        {
            if (passed)
                Directory.Delete(ownedRoot, recursive: true);
            else
                Console.Error.WriteLine($"Response-replay publication child DB and logs retained at {ownedRoot}.");
        }
    }

    private static void AssertAuthoredBehaviorMatches(JsonElement baselineClosure, JsonElement candidateClosure, string baselineArtifactId, string candidateArtifactId)
    {
        var baselineArtifact = baselineClosure.GetProperty("artifacts").EnumerateArray().Single(artifact =>
            artifact.GetProperty("identity").GetProperty("artifactId").GetString() == baselineArtifactId);
        var candidateArtifact = candidateClosure.GetProperty("artifacts").EnumerateArray().Single(artifact =>
            artifact.GetProperty("identity").GetProperty("artifactId").GetString() == candidateArtifactId);
        var baselineProjection = AuthoredBehaviorProjection(baselineArtifact);
        var candidateProjection = AuthoredBehaviorProjection(candidateArtifact);
        Assert.True(JsonNode.DeepEquals(baselineProjection, candidateProjection),
            "Normal candidate publication changed authored variables, node order/types, or input bindings beyond the reviewed contract-profile/version metadata allowance.\n" +
            $"Baseline: {baselineProjection.ToJsonString()}\nCandidate: {candidateProjection.ToJsonString()}");
    }

    private static JsonObject AuthoredBehaviorProjection(JsonElement artifact)
    {
        var projected = JsonNode.Parse(artifact.GetRawText())!.AsObject();
        // Publication identity and time may differ. Retain every other artifact field, including the compatibility,
        // runtime, storage, input, incident, and resume contracts, so unknown differences fail visibly.
        projected.Remove("identity");
        projected.Remove("createdAt");
        StripAllowedContractMetadata(projected);
        return projected;
    }

    private static void StripAllowedContractMetadata(JsonObject artifact)
    {
        if (artifact["rootActivity"] is JsonObject rootActivity)
            StripExecutableNodeContractMetadata(rootActivity);
    }

    private static void StripExecutableNodeContractMetadata(JsonObject node)
    {
        if (node["descriptorType"]?.GetValue<string>() == "elsa.clr-activity" &&
            node["activityContract"] is JsonObject contract &&
            contract["descriptorKind"]?.GetValue<string>() == "Elsa.Primitives.Models.ClrActivityDescriptor")
        {
            node.Remove("activityTypeVersion");
            contract.Remove("contractVersion");
            contract.Remove("schemaFingerprint");
            if (node["authoredActivityId"]?.GetValue<string>() == "write-response")
                contract.Remove("sideEffectProfile");
        }

        if (node["childSlots"] is not JsonArray childSlots)
            return;

        foreach (var slot in childSlots)
        {
            if (slot?["activities"] is not JsonArray activities)
                continue;

            foreach (var child in activities.OfType<JsonObject>())
                StripExecutableNodeContractMetadata(child);
        }
    }

    private static async Task SaveChildLogsAsync(string ownedRoot, string stdout, string stderr)
    {
        await File.WriteAllTextAsync(Path.Combine(ownedRoot, "child.stdout.log"), stdout);
        await File.WriteAllTextAsync(Path.Combine(ownedRoot, "child.stderr.log"), stderr);
    }

    private static async Task<ChildProcessResult> RunChildProcessAsync(
        string childDll,
        string ownedRoot,
        TimeSpan timeoutDuration,
        string processDescription,
        params string[] arguments)
    {
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
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        ClearInheritedPersistenceRedirects(process.StartInfo.Environment);

        Assert.True(process.Start(), $"The {processDescription} did not start.");
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(timeoutDuration);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                var timedOutStdout = await stdoutTask;
                var timedOutStderr = await stderrTask;
                await SaveChildLogsAsync(ownedRoot, timedOutStdout, timedOutStderr);
                throw new TimeoutException($"The {processDescription} exceeded {timeoutDuration.TotalSeconds:0} seconds. Owned child data and logs retained at {ownedRoot}.\n{timedOutStdout}\n{timedOutStderr}");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            await SaveChildLogsAsync(ownedRoot, stdout, stderr);
            return new ChildProcessResult(process.ExitCode, stdout, stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private sealed record ChildProcessResult(int ExitCode, string Stdout, string Stderr);

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
