using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Activities.Http.Activities;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed class ResponseReplaySafetyProcessTests
{
    private const string ResultPrefix = "RESPONSE_REPLAY_RESULT=";
    private const string PublicationResultPrefix = "RESPONSE_REPLAY_PUBLICATION_RESULT=";
    private const string RecoveryResultPrefix = "RESPONSE_REPLAY_RECOVERY_RESULT=";
    private const string MeasurementResultPrefix = "RESPONSE_REPLAY_MEASUREMENT_RESULT=";
    private const string ExpectedCandidateProfileEnvironmentVariable = "ELSA_RESPONSE_REPLAY_T012_EXPECT_CANDIDATE_PROFILE";
    private const string HttpNodeId = "http-in";
    private const string ResponseNodeId = "write-response";
    private const string ReplayCorrelationHeader = "X-Response-Replay-Correlation";
    private static readonly JsonSerializerOptions RuntimeJsonOptions = CreateRuntimeJsonOptions();
    private readonly ITestOutputHelper _output;

    public ResponseReplaySafetyProcessTests(ITestOutputHelper output) => _output = output;

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

    [Fact]
    public async Task MatchedCoalescedProfiles_ReportBoundaryCountsWithoutAttributingOtherExecutionWork()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixtureDirectory = Path.Combine(repositoryRoot,
            "tests", "essentials", "Activities", "Http", "IntegrationTests", "Fixtures", "ResponseReplayHost", "Fixtures");
        var closurePath = Path.Combine(fixtureDirectory, "pre-candidate-external-closure.json");
        var manifestPath = Path.Combine(fixtureDirectory, "pre-candidate-external-manifest.json");
        var childProjectDirectory = Path.Combine(repositoryRoot,
            "tests", "essentials", "Activities", "Http", "IntegrationTests", "Fixtures", "ResponseReplayHost");

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var publicationManifest = manifest.RootElement.GetProperty("publication");
        var expectedCandidateProfile = GetExpectedCandidateProfileForMatchedMeasurement();
        var routePath = publicationManifest.GetProperty("routePath").GetString()!;
        var baselineArtifactId = publicationManifest.GetProperty("artifactId").GetString()!;
        var baselineArtifactHash = publicationManifest.GetProperty("artifactHash").GetString()!;
        var closureBytes = await File.ReadAllBytesAsync(closurePath);
        var closureHash = Convert.ToHexString(SHA256.HashData(closureBytes)).ToLowerInvariant();
        Assert.Equal(manifest.RootElement.GetProperty("export").GetProperty("sha256").GetString(), closureHash);
        Assert.Equal("External", publicationManifest.GetProperty("resolvedWriteHttpResponseProfile").GetString());

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var childDll = Path.Combine(childProjectDirectory, "bin", configuration, "net10.0", "ResponseReplayHost.dll");
        Assert.True(File.Exists(childDll), $"The referenced child project was not built: {childDll}");

        var ownedRoot = Path.Combine(Path.GetTempPath(), $"elsa-response-replay-t010-t011-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ownedRoot);
        var seedDatabasePath = Path.Combine(ownedRoot, "seed.db");
        var externalDatabasePath = Path.Combine(ownedRoot, "external.db");
        var candidateDatabasePath = Path.Combine(ownedRoot, "candidate.db");
        var retainSuccessfulEvidence = string.Equals(
            Environment.GetEnvironmentVariable("ELSA_RESPONSE_REPLAY_RETAIN_EVIDENCE"), "1", StringComparison.Ordinal);
        var passed = false;

        try
        {
            var setup = await RunChildProcessAsync(
                childDll, ownedRoot, TimeSpan.FromMinutes(3), "response-replay matched-count publication setup",
                "--measurement-publication-proof", closurePath, seedDatabasePath, ownedRoot, expectedCandidateProfile);
            Assert.Equal(0, setup.ExitCode);
            using var setupResult = ReadSingleResult(setup.Stdout, PublicationResultPrefix);
            var publication = setupResult.RootElement;
            Assert.Equal(baselineArtifactId, publication.GetProperty("baselineArtifactId").GetString());
            Assert.Equal(baselineArtifactHash, publication.GetProperty("baselineArtifactHash").GetString());
            Assert.Equal("External", publication.GetProperty("baselineProfile").GetString());
            Assert.Equal(expectedCandidateProfile, publication.GetProperty("candidateProfile").GetString());
            var candidateDefinitionId = publication.GetProperty("candidateDefinitionId").GetString()!;
            var candidateArtifactId = publication.GetProperty("candidateArtifactId").GetString()!;
            var candidateArtifactHash = publication.GetProperty("candidateArtifactHash").GetString()!;
            Assert.Equal(candidateArtifactHash, publication.GetProperty("candidateExecutableArtifactHash").GetString());

            // The child has exited before the SQLite online backup; this copies one coherent database view,
            // including any committed WAL pages, without copying a live main file.
            await BackupSqliteDatabaseAsync(seedDatabasePath, externalDatabasePath);
            await BackupSqliteDatabaseAsync(seedDatabasePath, candidateDatabasePath);

            var unpublish = await RunChildProcessAsync(
                childDll, ownedRoot, TimeSpan.FromMinutes(2), "response-replay External comparison unpublish",
                "--measurement-unpublish-candidate", closurePath, externalDatabasePath, ownedRoot,
                candidateDefinitionId, candidateArtifactId, candidateArtifactHash, expectedCandidateProfile);
            Assert.Equal(0, unpublish.ExitCode);

            // A separate ordinary host then applies the unchanged historical JSON source through the production
            // reconciler, leaving both immutable artifacts present and the External route active.
            var reconcile = await RunChildProcessAsync(
                childDll, ownedRoot, TimeSpan.FromMinutes(2), "response-replay External comparison reconcile",
                "--measurement-reconcile-external", closurePath, externalDatabasePath, ownedRoot,
                baselineArtifactId, baselineArtifactHash, candidateArtifactId, candidateArtifactHash, expectedCandidateProfile);
            Assert.Equal(0, reconcile.ExitCode);

            var externalResult = await RunMeasuredChildAsync(
                childDll, ownedRoot, closurePath, externalDatabasePath,
                "External", baselineArtifactId, baselineArtifactHash,
                baselineArtifactId, baselineArtifactHash,
                candidateArtifactId, candidateArtifactHash, routePath, expectedCandidateProfile);
            var candidateResult = await RunMeasuredChildAsync(
                childDll, ownedRoot, closurePath, candidateDatabasePath,
                expectedCandidateProfile, candidateArtifactId, candidateArtifactHash,
                baselineArtifactId, baselineArtifactHash,
                candidateArtifactId, candidateArtifactHash, routePath, expectedCandidateProfile);

            Assert.Equal(externalResult.HttpStatus, candidateResult.HttpStatus);
            Assert.Equal(externalResult.ResponseBody, candidateResult.ResponseBody);
            Assert.Equal(externalResult.ResponseContentType, candidateResult.ResponseContentType);
            Assert.Equal(externalResult.AuthoredHeaders.Count, candidateResult.AuthoredHeaders.Count);
            Assert.Empty(externalResult.AuthoredHeaders);
            Assert.Empty(candidateResult.AuthoredHeaders);

            var externalCounts = CountTotals(externalResult.Observation);
            var candidateCounts = CountTotals(candidateResult.Observation);
            if (expectedCandidateProfile == "External")
                AssertExternalProfilePolicyCountsMatch(externalResult.Observation, candidateResult.Observation);
            var report = new
            {
                setup = new
                {
                    closureSha256 = closureHash,
                    baselineArtifactId,
                    baselineArtifactHash,
                    candidateArtifactId,
                    candidateArtifactHash,
                    expectedCandidateProfile,
                    runtime = "same prebuilt child DLL",
                    persistence = "equivalent owned SQLite online-backup copies",
                    hostPreparation = "fresh child process; identical measured-host startup and pre-capture verification; no publication during either count window",
                    window = "correlated POST through delivered response, independent Completed execution read, empty scheduler queue, and terminal outbox state",
                    commandAttemptSemantics = "EF Reader/Scalar/NonQuery interceptor attempts and outcomes; Reader Succeeded means ReaderExecuted returned a reader, not result exhaustion, row count, or physical SQL statements",
                    pageReuseEligible = candidateResult.Observation.DurableValuePageReuseEligible && externalResult.Observation.DurableValuePageReuseEligible
                },
                external = new
                {
                    profile = "External",
                    responseReceived = new
                    {
                        raw = externalResult.ResponseReceivedObservation,
                        allObservedTotals = CountTotals(externalResult.ResponseReceivedObservation)
                    },
                    settled = new { raw = externalResult.Observation, allObservedTotals = externalCounts },
                    settledMinusResponseReceivedByBucket = DifferenceByBucket(
                        externalResult.Observation, externalResult.ResponseReceivedObservation)
                },
                candidate = new
                {
                    profile = expectedCandidateProfile,
                    responseReceived = new
                    {
                        raw = candidateResult.ResponseReceivedObservation,
                        allObservedTotals = CountTotals(candidateResult.ResponseReceivedObservation)
                    },
                    settled = new { raw = candidateResult.Observation, allObservedTotals = candidateCounts },
                    settledMinusResponseReceivedByBucket = DifferenceByBucket(
                        candidateResult.Observation, candidateResult.ResponseReceivedObservation)
                },
                candidateMinusExternalAllObserved = Difference(candidateCounts, externalCounts),
                candidateMinusExternalByBucket = DifferenceByBucket(candidateResult.Observation, externalResult.Observation)
            };
            var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            _output.WriteLine($"RESPONSE_REPLAY_MATCHED_COUNTS={reportJson}");
            if (retainSuccessfulEvidence)
                await File.WriteAllTextAsync(Path.Combine(ownedRoot, "matched-counts.json"), reportJson);

            passed = true;
        }
        finally
        {
            if (passed && !retainSuccessfulEvidence)
                Directory.Delete(ownedRoot, recursive: true);
            else if (passed)
                _output.WriteLine($"Response-replay matched-count evidence retained at {ownedRoot}.");
            else
                _output.WriteLine($"Response-replay matched-count DBs and logs retained at {ownedRoot}.");
        }
    }

    [Fact]
    public async Task ReplaySafeResponseCompletion_IsRecoveredAfterHardProcessLossWithoutResendingRequest()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixtureDirectory = Path.Combine(repositoryRoot,
            "tests", "essentials", "Activities", "Http", "IntegrationTests", "Fixtures", "ResponseReplayHost", "Fixtures");
        var closurePath = Path.Combine(fixtureDirectory, "pre-candidate-external-closure.json");
        var manifestPath = Path.Combine(fixtureDirectory, "pre-candidate-external-manifest.json");
        var childProjectDirectory = Path.Combine(repositoryRoot,
            "tests", "essentials", "Activities", "Http", "IntegrationTests", "Fixtures", "ResponseReplayHost");

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var baselinePublication = manifest.RootElement.GetProperty("publication");
        var routePath = baselinePublication.GetProperty("routePath").GetString()
            ?? throw new InvalidDataException("The immutable baseline manifest has no endpoint route path.");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var childDll = Path.Combine(childProjectDirectory, "bin", configuration, "net10.0", "ResponseReplayHost.dll");
        Assert.True(File.Exists(childDll), $"The referenced child project was not built: {childDll}");

        var ownedRoot = Path.Combine(Path.GetTempPath(), $"elsa-response-replay-t009-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ownedRoot);
        var databasePath = Path.Combine(ownedRoot, "runtime.db");
        var retainSuccessfulEvidence = string.Equals(
            Environment.GetEnvironmentVariable("ELSA_RESPONSE_REPLAY_RETAIN_EVIDENCE"), "1", StringComparison.Ordinal);
        var passed = false;

        try
        {
            var setup = await RunChildProcessAsync(
                childDll, ownedRoot, TimeSpan.FromSeconds(150), "response-replay publication setup",
                "--publication-proof", closurePath, databasePath, ownedRoot);
            Assert.Equal(0, setup.ExitCode);
            using var setupResult = ReadSingleResult(setup.Stdout, PublicationResultPrefix);
            var publication = setupResult.RootElement;
            var candidateArtifactId = publication.GetProperty("candidateArtifactId").GetString()!;
            var candidateArtifactHash = publication.GetProperty("candidateArtifactHash").GetString()!;
            Assert.Equal("ReplaySafe", publication.GetProperty("candidateProfile").GetString());
            Assert.Equal(candidateArtifactHash, publication.GetProperty("candidateExecutableArtifactHash").GetString());
            Assert.Equal(candidateArtifactId, publication.GetProperty("activeSharedRouteArtifactId").GetString());

            var existingCandidateExecutionIds = await ReadCandidateExecutionIdsAsync(databasePath, candidateArtifactId);
            Assert.Contains(publication.GetProperty("candidateHttpExecutionId").GetString()!, existingCandidateExecutionIds);
            Assert.Single(existingCandidateExecutionIds);

            var pipeName = $"rr-{Guid.NewGuid():N}"[..19];
            var correlationId = $"t009-{Guid.NewGuid():N}";
            var endpoint = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await using var pipeServer = endpoint;
            await using var crashChild = StartChildProcess(
                childDll, ownedRoot, "response-replay crash-window child",
                "--crash-stage", closurePath, databasePath, ownedRoot, pipeName,
                candidateArtifactId, candidateArtifactHash, correlationId);

            await WaitForPipeConnectionAsync(pipeServer, crashChild, TimeSpan.FromMinutes(2));
            using var reader = new StreamReader(pipeServer, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            await using var writer = new StreamWriter(pipeServer, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            var gateEvidencePath = Path.Combine(ownedRoot, "gate-messages.jsonl");

            int childProcessId;
            using (var ready = await ReadGateMessageAsync(reader, TimeSpan.FromSeconds(30), gateEvidencePath))
            {
                Assert.Equal("ready", ready.RootElement.GetProperty("type").GetString());
                childProcessId = ready.RootElement.GetProperty("payload").GetProperty("processId").GetInt32();
                Assert.True(childProcessId > 0);
                Assert.Equal(crashChild.ProcessId, childProcessId);
            }
            var childOwnerId = $"inproc:{Environment.MachineName}:{childProcessId}";
            await SendGateCommandAsync(writer, "start-request", gateEvidencePath);

            using var claimMessage = await ReadGateMessageAsync(reader, TimeSpan.FromMinutes(2), gateEvidencePath);
            Assert.Equal("endpoint-claim-durable", claimMessage.RootElement.GetProperty("type").GetString());
            var claim = claimMessage.RootElement.GetProperty("payload");
            Assert.Equal(candidateArtifactId, claim.GetProperty("artifactId").GetString());
            Assert.Equal(candidateArtifactHash, claim.GetProperty("artifactHash").GetString());
            Assert.Equal(HttpNodeId, claim.GetProperty("nodeId").GetString());
            Assert.Equal("Immediate", claim.GetProperty("decision").GetString());
            var executionId = AssertNonEmpty(claim.GetProperty("executionId").GetString());
            var endpointActivityExecutionId = AssertNonEmpty(claim.GetProperty("activityExecutionId").GetString());
            var schedulerWorkItemId = AssertNonEmpty(claim.GetProperty("schedulerWorkItemId").GetString());
            var commandId = AssertNonEmpty(claim.GetProperty("commandId").GetString());
            var attemptId = AssertNonEmpty(claim.GetProperty("attemptId").GetString());

            var durableClaim = await ReadIndependentRuntimeStateAsync(databasePath, executionId);
            AssertEffectiveCoalescedCadence(durableClaim.Execution);
            var claimLease = AssertDurableRequestAndClaim(
                durableClaim, candidateArtifactId, candidateArtifactHash, routePath, correlationId,
                endpointActivityExecutionId, schedulerWorkItemId, commandId, attemptId, childOwnerId);
            AssertOwnerLeaseLive(claimLease, DateTimeOffset.UtcNow);
            await SendGateCommandAsync(writer, "continue", gateEvidencePath);

            using var responseMessage = await ReadGateMessageAsync(reader, TimeSpan.FromMinutes(2), gateEvidencePath);
            Assert.Equal("response-buffered", responseMessage.RootElement.GetProperty("type").GetString());
            var response = responseMessage.RootElement.GetProperty("payload");
            Assert.Equal(executionId, response.GetProperty("executionId").GetString());
            Assert.Equal(candidateArtifactId, response.GetProperty("artifactId").GetString());
            Assert.Equal(candidateArtifactHash, response.GetProperty("artifactHash").GetString());
            Assert.Equal(HttpNodeId, response.GetProperty("endpointNodeId").GetString());
            Assert.Equal(endpointActivityExecutionId, response.GetProperty("endpointActivityExecutionId").GetString());
            Assert.Equal(schedulerWorkItemId, response.GetProperty("schedulerWorkItemId").GetString());
            Assert.Equal(commandId, response.GetProperty("commandId").GetString());
            Assert.Equal(attemptId, response.GetProperty("claimAttemptId").GetString());
            Assert.Equal(ResponseNodeId, response.GetProperty("responseNodeId").GetString());
            Assert.True(response.GetProperty("sessionActive").GetBoolean());
            Assert.True(response.GetProperty("sessionAppliesToExecution").GetBoolean());
            Assert.True(response.GetProperty("hasBufferedChanges").GetBoolean());
            Assert.True(response.GetProperty("hopCount").GetInt32() > 0);
            Assert.Equal("Deferred", response.GetProperty("decision").GetString());
            AssertNonEmpty(response.GetProperty("responseActivityExecutionId").GetString());
            Assert.Equal(200, response.GetProperty("instruction").GetProperty("statusCode").GetInt32());
            Assert.Equal("Alice Smith", response.GetProperty("instruction").GetProperty("body").GetString());
            Assert.Equal("text/plain", response.GetProperty("instruction").GetProperty("contentType").GetString());
            Assert.Equal(0, response.GetProperty("instruction").GetProperty("headerCount").GetInt32());

            var durableWindow = await ReadIndependentRuntimeStateAsync(databasePath, executionId);
            AssertEffectiveCoalescedCadence(durableWindow.Execution);
            var windowLease = AssertDurableRequestAndClaim(
                durableWindow, candidateArtifactId, candidateArtifactHash, routePath, correlationId,
                endpointActivityExecutionId, schedulerWorkItemId, commandId, attemptId, childOwnerId);
            Assert.False(durableWindow.ActivityStates.Any(state =>
                    StringComparer.Ordinal.Equals(state.Execution.AuthoredActivityId, ResponseNodeId) && state.Completion is not null),
                "A response-node completion was already durable before the parent kill boundary.");
            AssertOwnerLeaseLive(windowLease, DateTimeOffset.UtcNow);
            AssertSameOwnerLease(claimLease, windowLease);

            // The child never receives a response-barrier acknowledgement, so it must still be alive inside CommitAsync.
            Assert.False(crashChild.HasExited, "The child left the response barrier before the parent could kill it.");
            var killedChild = await crashChild.KillAndWaitAsync();
            Assert.NotEqual(0, killedChild.ExitCode);

            var afterKill = await ReadIndependentRuntimeStateAsync(databasePath, executionId);
            AssertEffectiveCoalescedCadence(afterKill.Execution);
            Assert.Equal(durableWindow.Execution.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence],
                afterKill.Execution.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence]);
            Assert.Equal(durableWindow.Execution.SystemMetadata[RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints],
                afterKill.Execution.SystemMetadata[RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints]);
            var killedOwnerLease = AssertDurableRequestAndClaim(
                afterKill, candidateArtifactId, candidateArtifactHash, routePath, correlationId,
                endpointActivityExecutionId, schedulerWorkItemId, commandId, attemptId, childOwnerId);
            Assert.False(afterKill.ActivityStates.Any(state =>
                    StringComparer.Ordinal.Equals(state.Execution.AuthoredActivityId, ResponseNodeId) && state.Completion is not null),
                "A response-node completion became durable during the process kill.");
            AssertSameOwnerLease(windowLease, killedOwnerLease);

            // Hold restart until the persisted owner lease is stale. This exercises recovery after the real owner
            // fence expires without inventing a scheduler visibility lease or editing durable time.
            var staleOwnerAt = killedOwnerLease.ExpiresAt.AddSeconds(1);
            var ownerLeaseWaitDeadline = DateTimeOffset.UtcNow.AddMinutes(2);
            if (staleOwnerAt > ownerLeaseWaitDeadline)
                throw new TimeoutException($"The persisted execution-owner lease extends beyond the two-minute stale-owner recovery budget: owner={killedOwnerLease.ExpiresAt:O}.");
            var waitForOwnerLease = staleOwnerAt - DateTimeOffset.UtcNow;
            if (waitForOwnerLease > TimeSpan.Zero)
                await Task.Delay(waitForOwnerLease);

            var eligibleState = await ReadIndependentRuntimeStateAsync(databasePath, executionId);
            AssertEffectiveCoalescedCadence(eligibleState.Execution);
            var eligibleOwnerLease = AssertDurableRequestAndClaim(
                eligibleState, candidateArtifactId, candidateArtifactHash, routePath, correlationId,
                endpointActivityExecutionId, schedulerWorkItemId, commandId, attemptId, childOwnerId);
            AssertSameOwnerLease(killedOwnerLease, eligibleOwnerLease);
            Assert.True(DateTimeOffset.UtcNow >= eligibleOwnerLease.ExpiresAt.AddSeconds(1),
                "The persisted execution-owner lease has not expired for the controlled stale-owner restart.");

            var recovery = await RunChildProcessAsync(
                childDll, ownedRoot, TimeSpan.FromMinutes(4), "normal runtime recovery child",
                "--resume-recovery", closurePath, databasePath, ownedRoot,
                executionId, candidateArtifactId, candidateArtifactHash);
            Assert.Equal(0, recovery.ExitCode);
            using var recoveryResult = ReadSingleResult(recovery.Stdout, RecoveryResultPrefix);
            var recovered = recoveryResult.RootElement;
            Assert.Equal(executionId, recovered.GetProperty("executionId").GetString());
            Assert.Equal(candidateArtifactId, recovered.GetProperty("pinnedArtifactId").GetString());
            Assert.Equal(candidateArtifactHash, recovered.GetProperty("pinnedArtifactHash").GetString());
            Assert.Equal("Completed", recovered.GetProperty("executionStatus").GetString());
            Assert.Equal(200, recovered.GetProperty("responseStatusCode").GetInt32());
            Assert.Equal("Alice Smith", recovered.GetProperty("responseBody").GetString());
            Assert.Equal("text/plain", recovered.GetProperty("responseContentType").GetString());
            Assert.Equal(0, recovered.GetProperty("responseHeaderCount").GetInt32());
            Assert.Equal("Alice", recovered.GetProperty("persistedFirstName").GetString());
            Assert.Equal("Smith", recovered.GetProperty("persistedLastName").GetString());
            Assert.Equal("Alice Smith", recovered.GetProperty("referenceText").GetString());

            var finalState = await ReadIndependentRuntimeStateAsync(databasePath, executionId);
            AssertDurableRequestAndTrigger(finalState, candidateArtifactId, candidateArtifactHash, routePath, correlationId);
            AssertEffectiveCoalescedCadence(finalState.Execution);
            Assert.Equal(durableWindow.Execution.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence],
                finalState.Execution.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence]);
            Assert.Equal(durableWindow.Execution.SystemMetadata[RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints],
                finalState.Execution.SystemMetadata[RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints]);
            Assert.Equal(WorkflowExecutionStatus.Completed, finalState.Execution.Status);
            Assert.Equal("Alice", ReadStringVariable(finalState.Execution.RootVariableFrame!, "content", "firstName"));
            Assert.Equal("Smith", ReadStringVariable(finalState.Execution.RootVariableFrame!, "content", "lastName"));
            Assert.Equal("Alice Smith", ReadStringVariable(finalState.Execution.RootVariableFrame!, "referenceText"));
            var finalResponse = Assert.Single(finalState.ActivityStates,
                state => state.Execution.AuthoredActivityId == ResponseNodeId && state.Completion is not null);
            var finalInstruction = finalResponse.Completion!.Result.InlineValue!.Value;
            Assert.Equal(200, finalInstruction.GetProperty("statusCode").GetInt32());
            Assert.Equal("Alice Smith", finalInstruction.GetProperty("body").GetString());
            Assert.Equal("text/plain", finalInstruction.GetProperty("contentType").GetString());
            Assert.Empty(finalInstruction.GetProperty("headers").EnumerateObject());

            var candidateExecutionIdsAfter = await ReadCandidateExecutionIdsAsync(databasePath, candidateArtifactId);
            Assert.Equal(existingCandidateExecutionIds.Count + 1, candidateExecutionIdsAfter.Count);
            Assert.Contains(executionId, candidateExecutionIdsAfter);
            Assert.Single(candidateExecutionIdsAfter.Except(existingCandidateExecutionIds, StringComparer.Ordinal));
            passed = true;
        }
        finally
        {
            if (passed && !retainSuccessfulEvidence)
                Directory.Delete(ownedRoot, recursive: true);
            else if (passed)
                Console.WriteLine($"Response-replay hard-crash evidence retained at {ownedRoot}.");
            else
                Console.Error.WriteLine($"Response-replay hard-crash DB and logs retained at {ownedRoot}.");
        }
    }

    private static async Task<ResponseReplayMeasurementResult> RunMeasuredChildAsync(
        string childDll,
        string ownedRoot,
        string closurePath,
        string databasePath,
        string profile,
        string targetArtifactId,
        string targetArtifactHash,
        string baselineArtifactId,
        string baselineArtifactHash,
        string candidateArtifactId,
        string candidateArtifactHash,
        string routePath,
        string expectedCandidateProfile)
    {
        var pipeName = $"rr-{Guid.NewGuid():N}"[..19];
        var correlationId = $"t011-{profile}-{Guid.NewGuid():N}";
        var gateEvidencePath = Path.Combine(ownedRoot, $"{profile.ToLowerInvariant()}-{Guid.NewGuid():N}-measurement-gate.jsonl");
        await using var pipeServer = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var child = StartChildProcess(
            childDll, ownedRoot, $"response-replay {profile} matched-count child",
            "--measure", closurePath, databasePath, ownedRoot, pipeName,
            targetArtifactId, targetArtifactHash, profile, correlationId,
            baselineArtifactId, baselineArtifactHash, candidateArtifactId, candidateArtifactHash,
            expectedCandidateProfile);

        await WaitForPipeConnectionAsync(pipeServer, child, TimeSpan.FromMinutes(2));
        using var reader = new StreamReader(pipeServer, new UTF8Encoding(false), false, 1024, leaveOpen: true);
        await using var writer = new StreamWriter(pipeServer, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

        using (var transportReady = await ReadGateMessageAsync(reader, TimeSpan.FromSeconds(45), gateEvidencePath))
        {
            Assert.Equal("ready", transportReady.RootElement.GetProperty("type").GetString());
            Assert.Equal(child.ProcessId, transportReady.RootElement.GetProperty("payload").GetProperty("processId").GetInt32());
        }

        using (var ready = await ReadGateMessageAsync(reader, TimeSpan.FromSeconds(45), gateEvidencePath))
        {
            Assert.Equal("measurement-ready", ready.RootElement.GetProperty("type").GetString());
            var payload = ready.RootElement.GetProperty("payload");
            Assert.Equal(child.ProcessId, payload.GetProperty("processId").GetInt32());
            Assert.Equal(correlationId, payload.GetProperty("correlationId").GetString());
            Assert.Equal(targetArtifactId, payload.GetProperty("targetArtifactId").GetString());
            Assert.Equal(targetArtifactHash, payload.GetProperty("targetArtifactHash").GetString());
            Assert.Equal(profile, payload.GetProperty("targetProfile").GetString());
            Assert.Equal("External", payload.GetProperty("endpointProfile").GetString());
            Assert.Equal(expectedCandidateProfile, payload.GetProperty("candidateProfile").GetString());
            Assert.Equal(1, payload.GetProperty("activeRouteBindingCount").GetInt32());
            Assert.True(payload.GetProperty("durableValuePageReuseEligible").GetBoolean());
            Assert.True(payload.GetProperty("equivalentStartupPreparation").GetBoolean());
            Assert.Equal(baselineArtifactId, payload.GetProperty("baselineArtifactId").GetString());
            Assert.Equal(baselineArtifactHash, payload.GetProperty("baselineArtifactHash").GetString());
            Assert.Equal(candidateArtifactId, payload.GetProperty("candidateArtifactId").GetString());
            Assert.Equal(candidateArtifactHash, payload.GetProperty("candidateArtifactHash").GetString());
        }

        await SendGateCommandAsync(writer, "start", gateEvidencePath);
        using var httpDelivered = await ReadGateMessageAsync(reader, TimeSpan.FromMinutes(2), gateEvidencePath);
        Assert.Equal("http-delivered", httpDelivered.RootElement.GetProperty("type").GetString());
        var delivered = httpDelivered.RootElement.GetProperty("payload");
        var executionId = AssertNonEmpty(delivered.GetProperty("workflowExecutionId").GetString());
        Assert.Equal(targetArtifactId, delivered.GetProperty("targetArtifactId").GetString());
        Assert.Equal(targetArtifactHash, delivered.GetProperty("targetArtifactHash").GetString());
        Assert.Equal(profile, delivered.GetProperty("targetProfile").GetString());
        Assert.Equal(200, delivered.GetProperty("statusCode").GetInt32());
        Assert.Equal("Alice Smith", delivered.GetProperty("body").GetString());
        Assert.Equal("text/plain", delivered.GetProperty("contentType").GetString());
        Assert.Empty(delivered.GetProperty("authoredHeaders").EnumerateObject());

        var settledState = await WaitForMeasurementSettlementAsync(databasePath, executionId, TimeSpan.FromMinutes(2));
        AssertDurableRequestAndTrigger(settledState, targetArtifactId, targetArtifactHash, routePath, correlationId);
        Assert.Equal(targetArtifactId, settledState.Execution.PinnedExecutable.ArtifactId);
        Assert.Equal(targetArtifactHash, settledState.Execution.PinnedExecutable.ArtifactHash);
        AssertMeasurementTerminalState(settledState);

        await SendGateCommandAsync(writer, "settled", gateEvidencePath);
        using var complete = await ReadGateMessageAsync(reader, TimeSpan.FromSeconds(45), gateEvidencePath);
        Assert.Equal("measurement-complete", complete.RootElement.GetProperty("type").GetString());
        var completedPayload = complete.RootElement.GetProperty("payload");
        Assert.Equal(200, completedPayload.GetProperty("responseStatus").GetInt32());
        Assert.Equal("Alice Smith", completedPayload.GetProperty("responseBody").GetString());
        Assert.Equal("text/plain", completedPayload.GetProperty("responseContentType").GetString());
        Assert.Empty(completedPayload.GetProperty("authoredHeaders").EnumerateObject());
        var observationJson = completedPayload.GetProperty("observation");
        Assert.Equal(executionId, observationJson.GetProperty("targetExecutionId").GetString());
        Assert.Equal(targetArtifactId, observationJson.GetProperty("targetArtifactId").GetString());
        Assert.Equal(targetArtifactHash, observationJson.GetProperty("targetArtifactHash").GetString());
        Assert.Equal(profile, observationJson.GetProperty("targetProfile").GetString());
        Assert.True(observationJson.GetProperty("durableValuePageReuseEligible").GetBoolean());
        Assert.True(observationJson.GetProperty("equivalentStartupPreparation").GetBoolean());
        Assert.Equal(0, observationJson.GetProperty("orphanCommandOutcomes").GetInt32());
        Assert.Equal(0, observationJson.GetProperty("inFlightCommandAttempts").GetInt32());

        var process = await child.WaitForExitAsync(TimeSpan.FromMinutes(2));
        Assert.True(process.ExitCode == 0,
            $"The {profile} measured process exited {process.ExitCode}.\nstdout:\n{process.Stdout}\nstderr:\n{process.Stderr}");
        var resultLine = Assert.Single(
            process.Stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith(MeasurementResultPrefix, StringComparison.Ordinal));
        var result = JsonSerializer.Deserialize<ResponseReplayMeasurementResult>(
            resultLine[MeasurementResultPrefix.Length..], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(result);
        Assert.Equal(executionId, result.WorkflowExecutionId);
        Assert.Equal(200, result.HttpStatus);
        Assert.Equal("Alice Smith", result.ResponseBody);
        Assert.Equal("text/plain", result.ResponseContentType);
        Assert.Empty(result.AuthoredHeaders);
        AssertResponseAndSettledSnapshots(
            result.ResponseReceivedObservation,
            result.Observation,
            correlationId,
            executionId,
            targetArtifactId,
            targetArtifactHash,
            profile);
        AssertObservationWindow(result.Observation, correlationId, executionId, targetArtifactId, targetArtifactHash, profile);
        return result;
    }

    private static async Task<IndependentRuntimeState> WaitForMeasurementSettlementAsync(
        string databasePath,
        string executionId,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        InvalidOperationException? lastReadException = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            IndependentRuntimeState state;
            try
            {
                state = await ReadIndependentRuntimeStateAsync(databasePath, executionId);
                lastReadException = null;
            }
            catch (InvalidOperationException exception)
            {
                lastReadException = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(250));
                continue;
            }

            var responseIsCommitted = state.ActivityStates.Count(activity =>
                StringComparer.Ordinal.Equals(activity.Execution.AuthoredActivityId, ResponseNodeId) && activity.Completion is not null) == 1;
            var outboxIsTerminal = state.OutboxStatuses.All(status => status is
                RuntimePostCommitOutboxStatus.Delivered or
                RuntimePostCommitOutboxStatus.FailedFinal or
                RuntimePostCommitOutboxStatus.Cancelled);
            if (state.Execution.Status == WorkflowExecutionStatus.Faulted)
                throw new InvalidOperationException("The matched measurement workflow faulted before the settled boundary.");
            if (state.IncidentCount != 0)
                throw new InvalidOperationException($"The matched measurement workflow recorded {state.IncidentCount} runtime incident(s).");
            if (state.Execution.Status == WorkflowExecutionStatus.Completed &&
                responseIsCommitted && state.SchedulerItems.Count == 0 && outboxIsTerminal)
            {
                if (state.OutboxStatuses.Any(status => status is
                    RuntimePostCommitOutboxStatus.FailedFinal or RuntimePostCommitOutboxStatus.Cancelled))
                    throw new InvalidOperationException("The matched measurement workflow has a failed or cancelled post-commit outbox item.");
                return state;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException(
            $"Workflow execution '{executionId}' did not reach Completed with a committed response and no scheduler or unsettled outbox work within {timeout}.",
            lastReadException);
    }

    private static void AssertMeasurementTerminalState(IndependentRuntimeState state)
    {
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Execution.Status);
        Assert.Empty(state.SchedulerItems);
        Assert.Equal(0, state.IncidentCount);
        Assert.All(state.OutboxStatuses, status => Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, status));

        var response = Assert.Single(state.ActivityStates,
            activity => activity.Execution.AuthoredActivityId == ResponseNodeId && activity.Completion is not null);
        var instruction = response.Completion!.Result.InlineValue!.Value;
        Assert.Equal(200, instruction.GetProperty("statusCode").GetInt32());
        Assert.Equal("Alice Smith", instruction.GetProperty("body").GetString());
        Assert.Equal("text/plain", instruction.GetProperty("contentType").GetString());
        Assert.Empty(instruction.GetProperty("headers").EnumerateObject());
        Assert.Equal("Alice", ReadStringVariable(state.Execution.RootVariableFrame!, "content", "firstName"));
        Assert.Equal("Smith", ReadStringVariable(state.Execution.RootVariableFrame!, "content", "lastName"));
        Assert.Equal("Alice Smith", ReadStringVariable(state.Execution.RootVariableFrame!, "referenceText"));
    }

    private static void AssertObservationWindow(
        ResponseReplayObservationSnapshot observation,
        string correlationId,
        string executionId,
        string artifactId,
        string artifactHash,
        string responseProfile)
    {
        Assert.Equal(correlationId, observation.CorrelationId);
        Assert.Equal(executionId, observation.TargetExecutionId);
        Assert.Equal(artifactId, observation.TargetArtifactId);
        Assert.Equal(artifactHash, observation.TargetArtifactHash);
        Assert.Equal(responseProfile, observation.TargetProfile);
        Assert.True(observation.DurableValuePageReuseEligible);
        Assert.True(observation.EquivalentStartupPreparation);
        Assert.NotNull(observation.TargetClaim);
        Assert.Equal(RuntimeCheckpointNames.ActivityAttemptClaimed, observation.TargetClaim!.CheckpointName);
        Assert.Equal(executionId, observation.TargetClaim.WorkflowExecutionId);
        Assert.Equal(artifactId, observation.TargetClaim.ArtifactId);
        Assert.Equal(artifactHash, observation.TargetClaim.ArtifactHash);
        Assert.Equal(HttpNodeId, observation.TargetClaim.NodeId);
        Assert.Equal("External", observation.TargetClaim.ClaimActivityProfile);
        Assert.Equal(ResponseNodeId, observation.TargetClaim.ResponseNodeId);
        Assert.Equal(responseProfile, observation.TargetClaim.ResponseProfile);
        Assert.NotEmpty(observation.ResponseCompletionAttempts);
        Assert.All(observation.ResponseCompletionAttempts, responseCompletion =>
        {
            Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, responseCompletion.CheckpointName);
            Assert.Equal(executionId, responseCompletion.WorkflowExecutionId);
            Assert.Equal(artifactId, responseCompletion.ArtifactId);
            Assert.Equal(artifactHash, responseCompletion.ArtifactHash);
            Assert.Equal(ResponseNodeId, responseCompletion.NodeId);
            Assert.Equal(responseProfile, responseCompletion.PinnedResponseProfile);
            Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred.ToString(), responseCompletion.PersistenceDecision);
        });
        Assert.Equal(0, observation.OrphanCommandOutcomes);
        Assert.Equal(0, observation.InFlightCommandAttempts);

        foreach (var command in observation.Buckets.Values.SelectMany(bucket => bucket.Commands.Values))
            Assert.Equal(command.Started, command.Succeeded + command.Failed + command.Canceled);
    }

    private static void AssertResponseAndSettledSnapshots(
        ResponseReplayObservationSnapshot responseReceived,
        ResponseReplayObservationSnapshot settled,
        string correlationId,
        string executionId,
        string artifactId,
        string artifactHash,
        string profile)
    {
        Assert.Equal(correlationId, responseReceived.CorrelationId);
        Assert.Equal(executionId, responseReceived.TargetExecutionId);
        Assert.Equal(artifactId, responseReceived.TargetArtifactId);
        Assert.Equal(artifactHash, responseReceived.TargetArtifactHash);
        Assert.Equal(profile, responseReceived.TargetProfile);
        Assert.Equal(settled.TargetClaim, responseReceived.TargetClaim);
        Assert.Equal(settled.DurableValuePageReuseEligible, responseReceived.DurableValuePageReuseEligible);
        Assert.Equal(settled.EquivalentStartupPreparation, responseReceived.EquivalentStartupPreparation);
        Assert.True(settled.ResponseCompletionAttempts.Count >= responseReceived.ResponseCompletionAttempts.Count);
        Assert.Equal(
            responseReceived.ResponseCompletionAttempts,
            settled.ResponseCompletionAttempts.Take(responseReceived.ResponseCompletionAttempts.Count));
        Assert.True(settled.CarryInCommandOutcomes >= responseReceived.CarryInCommandOutcomes);
        Assert.True(settled.OrphanCommandOutcomes >= responseReceived.OrphanCommandOutcomes);
        Assert.Equal(
            responseReceived.Buckets.Keys.OrderBy(key => key, StringComparer.Ordinal),
            settled.Buckets.Keys.OrderBy(key => key, StringComparer.Ordinal));

        foreach (var (bucketName, before) in responseReceived.Buckets)
        {
            var after = settled.Buckets[bucketName];
            AssertAtLeast(after.Dispatches, before.Dispatches, bucketName, nameof(before.Dispatches));
            AssertAtLeast(after.DrainCycles, before.DrainCycles, bucketName, nameof(before.DrainCycles));
            AssertAtLeast(after.ActivityExecutions, before.ActivityExecutions, bucketName, nameof(before.ActivityExecutions));
            AssertAtLeast(after.LogicalCheckpointAttempts, before.LogicalCheckpointAttempts, bucketName, nameof(before.LogicalCheckpointAttempts));
            AssertAtLeast(after.LogicalCheckpointSucceeded, before.LogicalCheckpointSucceeded, bucketName, nameof(before.LogicalCheckpointSucceeded));
            AssertAtLeast(after.LogicalCheckpointFailed, before.LogicalCheckpointFailed, bucketName, nameof(before.LogicalCheckpointFailed));
            AssertAtLeast(after.LogicalCheckpointCanceled, before.LogicalCheckpointCanceled, bucketName, nameof(before.LogicalCheckpointCanceled));
            AssertAtLeast(after.LogicalClaimCheckpointAttempts, before.LogicalClaimCheckpointAttempts, bucketName, nameof(before.LogicalClaimCheckpointAttempts));
            AssertAtLeast(after.LogicalClaimCheckpointSucceeded, before.LogicalClaimCheckpointSucceeded, bucketName, nameof(before.LogicalClaimCheckpointSucceeded));
            AssertAtLeast(after.DeferredDecisionAttempts, before.DeferredDecisionAttempts, bucketName, nameof(before.DeferredDecisionAttempts));
            AssertAtLeast(after.ImmediateDecisionAttempts, before.ImmediateDecisionAttempts, bucketName, nameof(before.ImmediateDecisionAttempts));
            AssertAtLeast(after.DurableCheckpointAttempts, before.DurableCheckpointAttempts, bucketName, nameof(before.DurableCheckpointAttempts));
            AssertAtLeast(after.DurableCheckpointSucceeded, before.DurableCheckpointSucceeded, bucketName, nameof(before.DurableCheckpointSucceeded));
            AssertAtLeast(after.DurableCheckpointFailed, before.DurableCheckpointFailed, bucketName, nameof(before.DurableCheckpointFailed));
            AssertAtLeast(after.DurableCheckpointCanceled, before.DurableCheckpointCanceled, bucketName, nameof(before.DurableCheckpointCanceled));
            AssertAtLeast(after.DurableClaimCheckpointAttempts, before.DurableClaimCheckpointAttempts, bucketName, nameof(before.DurableClaimCheckpointAttempts));
            AssertAtLeast(after.DurableClaimCheckpointSucceeded, before.DurableClaimCheckpointSucceeded, bucketName, nameof(before.DurableClaimCheckpointSucceeded));
            AssertAtLeast(after.SegmentFlushAttempts, before.SegmentFlushAttempts, bucketName, nameof(before.SegmentFlushAttempts));
            AssertAtLeast(after.SegmentFlushes, before.SegmentFlushes, bucketName, nameof(before.SegmentFlushes));

            foreach (var commandName in before.Commands.Keys.Union(after.Commands.Keys, StringComparer.Ordinal))
            {
                before.Commands.TryGetValue(commandName, out var beforeCommand);
                after.Commands.TryGetValue(commandName, out var afterCommand);
                beforeCommand ??= new ResponseReplayCommandSnapshot(0, 0, 0, 0);
                afterCommand ??= new ResponseReplayCommandSnapshot(0, 0, 0, 0);
                AssertAtLeast(afterCommand.Started, beforeCommand.Started, bucketName, $"{commandName}.Started");
                AssertAtLeast(afterCommand.Succeeded, beforeCommand.Succeeded, bucketName, $"{commandName}.Succeeded");
                AssertAtLeast(afterCommand.Failed, beforeCommand.Failed, bucketName, $"{commandName}.Failed");
                AssertAtLeast(afterCommand.Canceled, beforeCommand.Canceled, bucketName, $"{commandName}.Canceled");
            }
        }
    }

    private static void AssertAtLeast(long settled, long responseReceived, string bucket, string counter) =>
        Assert.True(settled >= responseReceived,
            $"The settled {bucket} {counter} counter ({settled}) is below its response-received value ({responseReceived}).");

    private static string GetExpectedCandidateProfileForMatchedMeasurement() =>
        Environment.GetEnvironmentVariable(ExpectedCandidateProfileEnvironmentVariable) switch
        {
            null => "ReplaySafe",
            "External" => "External",
            var value => throw new InvalidOperationException(
                $"{ExpectedCandidateProfileEnvironmentVariable} accepts only 'External' when set; received '{value}'.")
        };

    private static void AssertExternalProfilePolicyCountsMatch(
        ResponseReplayObservationSnapshot external,
        ResponseReplayObservationSnapshot candidate)
    {
        var externalRequest = external.Buckets["requestDrain"];
        var candidateRequest = candidate.Buckets["requestDrain"];
        Assert.Equal(externalRequest.DeferredDecisionAttempts, candidateRequest.DeferredDecisionAttempts);
        Assert.Equal(externalRequest.ImmediateDecisionAttempts, candidateRequest.ImmediateDecisionAttempts);
        Assert.Equal(externalRequest.DurableCheckpointAttempts, candidateRequest.DurableCheckpointAttempts);
        Assert.Equal(externalRequest.DurableCheckpointSucceeded, candidateRequest.DurableCheckpointSucceeded);
        Assert.Equal(externalRequest.DurableClaimCheckpointAttempts, candidateRequest.DurableClaimCheckpointAttempts);
        Assert.Equal(externalRequest.DurableClaimCheckpointSucceeded, candidateRequest.DurableClaimCheckpointSucceeded);
        Assert.Equal(externalRequest.SegmentFlushAttempts, candidateRequest.SegmentFlushAttempts);
        Assert.Equal(externalRequest.SegmentFlushes, candidateRequest.SegmentFlushes);
    }

    private static async Task BackupSqliteDatabaseAsync(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var sourceString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        var destinationString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        await using var source = new SqliteConnection(sourceString);
        await source.OpenAsync();
        await using var destination = new SqliteConnection(destinationString);
        await destination.OpenAsync();
        source.BackupDatabase(destination);
    }

    private static ResponseReplayCountTotals CountTotals(ResponseReplayObservationSnapshot observation)
    {
        var buckets = observation.Buckets.Values.ToArray();
        var commands = buckets.SelectMany(bucket => bucket.Commands.Values).ToArray();
        return new ResponseReplayCountTotals(
            buckets.Sum(bucket => bucket.LogicalCheckpointAttempts),
            buckets.Sum(bucket => bucket.LogicalCheckpointSucceeded),
            buckets.Sum(bucket => bucket.LogicalCheckpointFailed),
            buckets.Sum(bucket => bucket.LogicalCheckpointCanceled),
            buckets.Sum(bucket => bucket.LogicalClaimCheckpointAttempts),
            buckets.Sum(bucket => bucket.LogicalClaimCheckpointSucceeded),
            buckets.Sum(bucket => bucket.DeferredDecisionAttempts),
            buckets.Sum(bucket => bucket.ImmediateDecisionAttempts),
            buckets.Sum(bucket => bucket.DurableCheckpointAttempts),
            buckets.Sum(bucket => bucket.DurableCheckpointSucceeded),
            buckets.Sum(bucket => bucket.DurableCheckpointFailed),
            buckets.Sum(bucket => bucket.DurableCheckpointCanceled),
            buckets.Sum(bucket => bucket.DurableClaimCheckpointAttempts),
            buckets.Sum(bucket => bucket.DurableClaimCheckpointSucceeded),
            buckets.Sum(bucket => bucket.SegmentFlushAttempts),
            buckets.Sum(bucket => bucket.SegmentFlushes),
            buckets.Sum(bucket => bucket.Dispatches),
            buckets.Sum(bucket => bucket.DrainCycles),
            buckets.Sum(bucket => bucket.ActivityExecutions),
            commands.Sum(command => command.Started),
            commands.Sum(command => command.Succeeded),
            commands.Sum(command => command.Failed),
            commands.Sum(command => command.Canceled));
    }

    private static ResponseReplayCountTotals Difference(ResponseReplayCountTotals candidate, ResponseReplayCountTotals external) =>
        new(
            candidate.LogicalCheckpointAttempts - external.LogicalCheckpointAttempts,
            candidate.LogicalCheckpointSucceeded - external.LogicalCheckpointSucceeded,
            candidate.LogicalCheckpointFailed - external.LogicalCheckpointFailed,
            candidate.LogicalCheckpointCanceled - external.LogicalCheckpointCanceled,
            candidate.ActivityClaimAttempts - external.ActivityClaimAttempts,
            candidate.ActivityClaimsSucceeded - external.ActivityClaimsSucceeded,
            candidate.DeferredDecisionAttempts - external.DeferredDecisionAttempts,
            candidate.ImmediateDecisionAttempts - external.ImmediateDecisionAttempts,
            candidate.DurableCheckpointAttempts - external.DurableCheckpointAttempts,
            candidate.DurableCheckpointSucceeded - external.DurableCheckpointSucceeded,
            candidate.DurableCheckpointFailed - external.DurableCheckpointFailed,
            candidate.DurableCheckpointCanceled - external.DurableCheckpointCanceled,
            candidate.DurableClaimCheckpointAttempts - external.DurableClaimCheckpointAttempts,
            candidate.DurableClaimCheckpointsSucceeded - external.DurableClaimCheckpointsSucceeded,
            candidate.SegmentFlushAttempts - external.SegmentFlushAttempts,
            candidate.SegmentFlushesSucceeded - external.SegmentFlushesSucceeded,
            candidate.Dispatches - external.Dispatches,
            candidate.DrainCycles - external.DrainCycles,
            candidate.ActivityExecutions - external.ActivityExecutions,
            candidate.DatabaseCommandAttempts - external.DatabaseCommandAttempts,
            candidate.DatabaseCommandSucceeded - external.DatabaseCommandSucceeded,
            candidate.DatabaseCommandFailed - external.DatabaseCommandFailed,
            candidate.DatabaseCommandCanceled - external.DatabaseCommandCanceled);

    private static IReadOnlyDictionary<string, ResponseReplayCountTotals> DifferenceByBucket(
        ResponseReplayObservationSnapshot settled,
        ResponseReplayObservationSnapshot responseReceived) =>
        settled.Buckets.Keys.ToDictionary(
            bucketName => bucketName,
            bucketName => Difference(CountTotals(settled.Buckets[bucketName]), CountTotals(responseReceived.Buckets[bucketName])),
            StringComparer.Ordinal);

    private static ResponseReplayCountTotals CountTotals(ResponseReplayBucketSnapshot bucket)
    {
        var commands = bucket.Commands.Values.ToArray();
        return new ResponseReplayCountTotals(
            bucket.LogicalCheckpointAttempts,
            bucket.LogicalCheckpointSucceeded,
            bucket.LogicalCheckpointFailed,
            bucket.LogicalCheckpointCanceled,
            bucket.LogicalClaimCheckpointAttempts,
            bucket.LogicalClaimCheckpointSucceeded,
            bucket.DeferredDecisionAttempts,
            bucket.ImmediateDecisionAttempts,
            bucket.DurableCheckpointAttempts,
            bucket.DurableCheckpointSucceeded,
            bucket.DurableCheckpointFailed,
            bucket.DurableCheckpointCanceled,
            bucket.DurableClaimCheckpointAttempts,
            bucket.DurableClaimCheckpointSucceeded,
            bucket.SegmentFlushAttempts,
            bucket.SegmentFlushes,
            bucket.Dispatches,
            bucket.DrainCycles,
            bucket.ActivityExecutions,
            commands.Sum(command => command.Started),
            commands.Sum(command => command.Succeeded),
            commands.Sum(command => command.Failed),
            commands.Sum(command => command.Canceled));
    }

    private sealed record ResponseReplayCountTotals(
        long LogicalCheckpointAttempts,
        long LogicalCheckpointSucceeded,
        long LogicalCheckpointFailed,
        long LogicalCheckpointCanceled,
        long ActivityClaimAttempts,
        long ActivityClaimsSucceeded,
        long DeferredDecisionAttempts,
        long ImmediateDecisionAttempts,
        long DurableCheckpointAttempts,
        long DurableCheckpointSucceeded,
        long DurableCheckpointFailed,
        long DurableCheckpointCanceled,
        long DurableClaimCheckpointAttempts,
        long DurableClaimCheckpointsSucceeded,
        long SegmentFlushAttempts,
        long SegmentFlushesSucceeded,
        long Dispatches,
        long DrainCycles,
        long ActivityExecutions,
        long DatabaseCommandAttempts,
        long DatabaseCommandSucceeded,
        long DatabaseCommandFailed,
        long DatabaseCommandCanceled);

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

    private static async Task SaveChildLogsAsync(string ownedRoot, string logStem, string stdout, string stderr)
    {
        await File.WriteAllTextAsync(Path.Combine(ownedRoot, $"{logStem}.stdout.log"), stdout);
        await File.WriteAllTextAsync(Path.Combine(ownedRoot, $"{logStem}.stderr.log"), stderr);
    }

    private static async Task<ChildProcessResult> RunChildProcessAsync(
        string childDll,
        string ownedRoot,
        TimeSpan timeoutDuration,
        string processDescription,
        params string[] arguments)
    {
        await using var child = StartChildProcess(childDll, ownedRoot, processDescription, arguments);
        return await child.WaitForExitAsync(timeoutDuration);
    }

    private static async Task WaitForPipeConnectionAsync(
        NamedPipeServerStream pipeServer,
        ChildProcessSession child,
        TimeSpan timeout)
    {
        using var connectTimeout = new CancellationTokenSource(timeout);
        var connection = pipeServer.WaitForConnectionAsync(connectTimeout.Token);
        while (!connection.IsCompleted && !child.HasExited)
            await Task.WhenAny(connection, Task.Delay(TimeSpan.FromMilliseconds(100)));

        if (child.HasExited && !pipeServer.IsConnected)
        {
            connectTimeout.Cancel();
            try
            {
                await connection;
            }
            catch (OperationCanceledException)
            {
                // The child exited before connecting; cancellation releases the pending pipe accept.
            }

            var exited = await child.WaitForExitAsync(TimeSpan.FromSeconds(1));
            throw new InvalidOperationException(
                $"The {child.ProcessDescription} exited with code {exited.ExitCode} before connecting to its IPC pipe.\n" +
                $"stdout:\n{exited.Stdout}\nstderr:\n{exited.Stderr}");
        }

        await connection;
    }

    private static void AssertEffectiveCoalescedCadence(WorkflowExecutionState execution)
    {
        Assert.Equal(WorkflowExecutableCheckpointCadence.CoalescedMode,
            execution.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence]);
        Assert.Equal("50", execution.SystemMetadata[RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints]);
    }

    private static ChildProcessSession StartChildProcess(
        string childDll,
        string ownedRoot,
        string processDescription,
        params string[] arguments)
    {
        var process = new Process
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
        var childProcess = process;
        var stdoutTask = childProcess.StandardOutput.ReadToEndAsync();
        var stderrTask = childProcess.StandardError.ReadToEndAsync();
        var logStem = $"{SanitizeLogName(processDescription)}-{childProcess.Id}-{Guid.NewGuid():N}";
        return new ChildProcessSession(childProcess, ownedRoot, processDescription, logStem, stdoutTask, stderrTask);
    }

    private static string SanitizeLogName(string value) =>
        string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-');

    private static async Task<JsonDocument> ReadGateMessageAsync(StreamReader reader, TimeSpan timeout, string evidencePath)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var line = await reader.ReadLineAsync(cancellation.Token);
        if (line is null)
            throw new EndOfStreamException("The gated child closed its IPC channel before sending the next attestation.");
        await RecordGateEventAsync(evidencePath, "child-to-parent", line);
        return JsonDocument.Parse(line);
    }

    private static async Task SendGateCommandAsync(StreamWriter writer, string command, string evidencePath)
    {
        await writer.WriteLineAsync(command);
        await RecordGateEventAsync(evidencePath, "parent-to-child", command);
    }

    private static Task RecordGateEventAsync(string evidencePath, string direction, string message) =>
        File.AppendAllTextAsync(
            evidencePath,
            JsonSerializer.Serialize(new { direction, message }) + Environment.NewLine);

    private static JsonDocument ReadSingleResult(string stdout, string prefix)
    {
        var resultLine = Assert.Single(
            stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith(prefix, StringComparison.Ordinal));
        return JsonDocument.Parse(resultLine[prefix.Length..]);
    }

    private static string AssertNonEmpty(string? value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value!;
    }

    private static async Task<HashSet<string>> ReadCandidateExecutionIdsAsync(string databasePath, string artifactId)
    {
        await using var db = CreateReadOnlyRuntimeDbContext(databasePath);
        await db.Database.OpenConnectionAsync();
        var contents = await db.WorkflowExecutionStates.AsNoTracking()
            .Where(row => row.ArtifactIdHash == EfRelationalIdentity.Hash(artifactId))
            .Select(row => row.ContentJson)
            .ToArrayAsync();
        return contents
            .Select(DeserializeRuntimeJson<WorkflowExecutionState>)
            .Where(execution => StringComparer.Ordinal.Equals(execution.PinnedExecutable.ArtifactId, artifactId))
            .Select(execution => execution.WorkflowExecutionId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<IndependentRuntimeState> ReadIndependentRuntimeStateAsync(string databasePath, string executionId)
    {
        await using var db = CreateReadOnlyRuntimeDbContext(databasePath);
        await db.Database.OpenConnectionAsync();
        var executionHash = EfRelationalIdentity.Hash(executionId);

        var workflowContents = await db.WorkflowExecutionStates.AsNoTracking()
            .Where(row => row.WorkflowExecutionIdHash == executionHash)
            .Select(row => row.ContentJson)
            .ToArrayAsync();
        var execution = workflowContents
            .Select(DeserializeRuntimeJson<WorkflowExecutionState>)
            .Single(state => StringComparer.Ordinal.Equals(state.WorkflowExecutionId, executionId));

        var durableContents = await db.DurableValueStates.AsNoTracking()
            .Where(row => row.WorkflowExecutionIdHash == executionHash)
            .Select(row => row.ContentJson)
            .ToArrayAsync();
        var durableValues = durableContents.Select(DeserializeRuntimeJson<DurableValueState>).ToArray();
        var stimulus = durableValues.Single(value => value.DurableValueId == "durable-stimulus:input");
        var triggerNode = durableValues.Single(value => value.DurableValueId == "durable-trigger:nodeId");
        var triggerMetadata = durableValues.Single(value => value.DurableValueId == "durable-trigger-meta:metadata");

        var activityContents = await db.ActivityExecutionStates.AsNoTracking()
            .Where(row => row.WorkflowExecutionIdHash == executionHash)
            .Select(row => row.ContentJson)
            .ToArrayAsync();
        var activityStates = activityContents.Select(DeserializeRuntimeJson<ActivityExecutionState>).ToArray();

        var workRows = await db.SchedulerWorkItems.AsNoTracking()
            .Where(row => row.WorkflowExecutionIdHash == executionHash)
            .ToArrayAsync();
        var livenessRows = await db.ExecutionLivenessStates.AsNoTracking()
            .Where(row => row.WorkflowExecutionIdHash == executionHash)
            .ToArrayAsync();
        var liveness = livenessRows.SingleOrDefault(row => row.HasOperationalOwner);
        var livenessContent = liveness is null
            ? null
            : DeserializeRuntimeJson<ExecutionLivenessState>(liveness.ContentJson);
        var incidentCount = await db.IncidentStates.AsNoTracking()
            .CountAsync(row => row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionId);
        var outboxStatuses = await db.RuntimePostCommitOutbox.AsNoTracking()
            .Where(row => row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionId)
            .Select(row => row.Status)
            .ToArrayAsync();

        return new IndependentRuntimeState(execution, stimulus, triggerNode, triggerMetadata, activityStates,
            workRows.Select(row => (row, DeserializeRuntimeJson<RuntimeSchedulerWorkItem>(row.ContentJson))).ToArray(),
            outboxStatuses.Select(status => (RuntimePostCommitOutboxStatus)status).ToArray(), incidentCount, liveness, livenessContent);
    }

    private static RuntimeExecutionLease AssertDurableRequestAndClaim(
        IndependentRuntimeState state,
        string artifactId,
        string artifactHash,
        string routePath,
        string correlationId,
        string endpointActivityExecutionId,
        string schedulerWorkItemId,
        string commandId,
        string attemptId,
        string expectedOwnerId)
    {
        AssertDurableRequestAndTrigger(state, artifactId, artifactHash, routePath, correlationId);

        var claimedActivity = Assert.Single(state.ActivityStates,
            activity => StringComparer.Ordinal.Equals(activity.Execution.ActivityExecutionId, endpointActivityExecutionId));
        Assert.Equal(HttpNodeId, claimedActivity.Execution.AuthoredActivityId);
        Assert.Equal(attemptId, claimedActivity.Metadata["runtime.activityAttemptActivationClaim"]);
        Assert.Equal(schedulerWorkItemId, claimedActivity.Metadata["runtime.activityAttemptActivationClaimWorkItemId"]);
        Assert.Contains(claimedActivity.Attempts!, attempt => attempt.AttemptId == attemptId && attempt.EndedAt is null);

        var scheduler = Assert.Single(state.SchedulerItems, item => item.Work.WorkItemId == schedulerWorkItemId);
        Assert.Equal(state.Execution.WorkflowExecutionId, scheduler.Work.WorkflowExecutionId);
        Assert.Equal(commandId, scheduler.Work.CommandId);
        Assert.Equal(WorkflowExecutionCommandKind.InvokeActivity, scheduler.Work.CommandKind);
        Assert.NotNull(scheduler.Work.Payload);
        var payload = scheduler.Work.Payload!.Value.Deserialize<RuntimeInvokeActivityCommandPayload>()!;
        Assert.Equal(HttpNodeId, payload.ExecutableNodeId);
        Assert.Equal(endpointActivityExecutionId, payload.ActivityExecutionId);
        Assert.Equal(artifactId, payload.PinnedExecutable.ArtifactId);
        Assert.Equal(artifactHash, payload.PinnedExecutable.ArtifactHash);
        Assert.Equal(schedulerWorkItemId, EfRelationalIdentity.Decode(scheduler.Row.WorkItemId));
        // Coalescing dispatches in memory; the durable invoke remains unclaimed as redrive work.
        Assert.Null(scheduler.Row.ClaimOwnerId);
        Assert.Equal(0, scheduler.Row.ClaimToken);
        Assert.Null(scheduler.Row.ClaimedAtUtcTicks);
        Assert.Null(scheduler.Row.ClaimedAtOffsetMinutes);
        Assert.Null(scheduler.Row.VisibleAfterUtcTicks);
        Assert.Null(scheduler.Row.VisibleAfterOffsetMinutes);

        return AssertOwnerLeaseIdentity(state, expectedOwnerId);
    }

    private static void AssertDurableRequestAndTrigger(
        IndependentRuntimeState state,
        string artifactId,
        string artifactHash,
        string routePath,
        string correlationId)
    {
        Assert.Equal(artifactId, state.Execution.PinnedExecutable.ArtifactId);
        Assert.Equal(artifactHash, state.Execution.PinnedExecutable.ArtifactHash);
        Assert.Equal("stimulus:input", state.Stimulus.ValueId);
        Assert.Equal(DurableValueLifecycle.Instance, state.Stimulus.Lifecycle);
        Assert.Equal(DurableValueStorage.Inline, state.Stimulus.Storage);
        var request = state.Stimulus.InlineValue!.Value;
        Assert.Equal("POST", request.GetProperty("Method").GetString());
        Assert.Equal(routePath.Trim('/'), request.GetProperty("Path").GetString()!.Trim('/'), ignoreCase: true);
        Assert.Equal("{\"firstName\":\"Alice\",\"lastName\":\"Smith\"}", request.GetProperty("Body").GetString());
        var headers = request.GetProperty("Headers");
        var contentType = headers.EnumerateObject().Single(property =>
            string.Equals(property.Name, "Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.Contains(contentType.EnumerateArray().Select(value => value.GetString()),
            value => value?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true);
        var requestCorrelation = headers.EnumerateObject().Single(property =>
            string.Equals(property.Name, ReplayCorrelationHeader, StringComparison.OrdinalIgnoreCase)).Value;
        Assert.Contains(requestCorrelation.EnumerateArray().Select(value => value.GetString()),
            value => StringComparer.Ordinal.Equals(value, correlationId));

        Assert.Equal("trigger:nodeId", state.TriggerNode.ValueId);
        Assert.Equal(HttpNodeId, state.TriggerNode.InlineValue!.Value.GetString());
        Assert.Equal("trigger-meta:metadata", state.TriggerMetadata.ValueId);
        var triggerMetadata = state.TriggerMetadata.InlineValue!.Value;
        Assert.Equal(routePath.Trim('/'), triggerMetadata.GetProperty("http:template").GetString()!.Trim('/'), ignoreCase: true);
        Assert.Equal("post", triggerMetadata.GetProperty("http:method").GetString(), ignoreCase: true);
    }

    private static RuntimeExecutionLease AssertOwnerLeaseIdentity(IndependentRuntimeState state, string expectedOwnerId)
    {
        Assert.NotNull(state.Liveness);
        Assert.True(state.Liveness!.HasOperationalOwner);
        var liveness = Assert.IsType<ExecutionLivenessState>(state.LivenessContent);
        Assert.Equal(state.Execution.WorkflowExecutionId, liveness.WorkflowExecutionId);
        var lease = Assert.IsType<RuntimeExecutionLease>(liveness.ExecutionLease);
        Assert.Equal(state.Execution.WorkflowExecutionId, lease.WorkflowExecutionId);
        Assert.Equal(expectedOwnerId, lease.OwnerId);
        Assert.False(string.IsNullOrWhiteSpace(lease.LeaseId));
        Assert.True(lease.FencingToken > 0);
        Assert.Equal(expectedOwnerId, EfRelationalIdentity.Decode(state.Liveness.LeaseOwnerId!));
        Assert.Equal(lease.AcquiredAt.UtcTicks, state.Liveness.LeaseAcquiredAtUtcTicks);
        Assert.Equal(lease.ExpiresAt.UtcTicks, state.Liveness.LeaseExpiresAtUtcTicks);

        var heartbeat = Assert.IsType<RuntimeHeartbeat>(liveness.Heartbeat);
        Assert.Equal(state.Execution.WorkflowExecutionId, heartbeat.WorkflowExecutionId);
        Assert.Equal(expectedOwnerId, heartbeat.OwnerId);
        Assert.Equal(lease.LeaseId, heartbeat.LeaseId);
        Assert.Equal(expectedOwnerId, EfRelationalIdentity.Decode(state.Liveness.HeartbeatOwnerId!));
        Assert.Equal(heartbeat.RecordedAt.UtcTicks, state.Liveness.HeartbeatRecordedAtUtcTicks);
        return lease;
    }

    private static void AssertOwnerLeaseLive(RuntimeExecutionLease lease, DateTimeOffset now) =>
        Assert.True(lease.ExpiresAt > now, "The child execution-owner lease must still be live before the parent kills it.");

    private static void AssertSameOwnerLease(RuntimeExecutionLease expected, RuntimeExecutionLease actual)
    {
        Assert.Equal(expected.WorkflowExecutionId, actual.WorkflowExecutionId);
        Assert.Equal(expected.OwnerId, actual.OwnerId);
        Assert.Equal(expected.LeaseId, actual.LeaseId);
        Assert.Equal(expected.FencingToken, actual.FencingToken);
        Assert.True(actual.ExpiresAt >= expected.ExpiresAt, "The execution-owner lease deadline moved backward between snapshots.");
    }

    private static RuntimeSqliteDbContext CreateReadOnlyRuntimeDbContext(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new RuntimeSqliteDbContext(options);
    }

    private static T DeserializeRuntimeJson<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, RuntimeJsonOptions)
        ?? throw new InvalidDataException($"Could not deserialize read-only runtime state as {typeof(T).Name}.");

    private static JsonSerializerOptions CreateRuntimeJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new LosslessRuntimeStringConverter());
        return options;
    }

    private static string ReadStringVariable(VariableFrameState frame, string variableKey, string? memberName = null)
    {
        var value = frame.Values[variableKey].InlineValue
            ?? throw new InvalidOperationException($"Durable variable '{variableKey}' has no inline value.");
        return memberName is null
            ? value.GetString() ?? throw new InvalidOperationException($"Durable variable '{variableKey}' is not a string.")
            : value.GetProperty(memberName).GetString() ?? throw new InvalidOperationException($"Durable variable '{variableKey}.{memberName}' is not a string.");
    }

    private sealed record IndependentRuntimeState(
        WorkflowExecutionState Execution,
        DurableValueState Stimulus,
        DurableValueState TriggerNode,
        DurableValueState TriggerMetadata,
        IReadOnlyCollection<ActivityExecutionState> ActivityStates,
        IReadOnlyCollection<(SchedulerWorkItemEntity Row, RuntimeSchedulerWorkItem Work)> SchedulerItems,
        IReadOnlyCollection<RuntimePostCommitOutboxStatus> OutboxStatuses,
        int IncidentCount,
        ExecutionLivenessStateEntity? Liveness,
        ExecutionLivenessState? LivenessContent)
    {
        public (SchedulerWorkItemEntity Row, RuntimeSchedulerWorkItem Work) SchedulerFor(string workItemId) =>
            SchedulerItems.Single(item => StringComparer.Ordinal.Equals(item.Work.WorkItemId, workItemId));
    }

    private sealed class LosslessRuntimeStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            EfRelationalIdentity.Decode(reader.GetString() ?? throw new JsonException("Expected an encoded runtime string."));

        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            EfRelationalIdentity.Decode(reader.GetString() ?? throw new JsonException("Expected an encoded runtime property name."));

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(EfRelationalIdentity.Encode(value));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(EfRelationalIdentity.Encode(value));
    }

    private sealed class ChildProcessSession(
        Process process,
        string ownedRoot,
        string processDescription,
        string logStem,
        Task<string> stdoutTask,
        Task<string> stderrTask) : IAsyncDisposable
    {
        private ChildProcessResult? _capturedResult;

        public bool HasExited => process.HasExited;
        public int ProcessId => process.Id;
        public string ProcessDescription => processDescription;

        public async Task<ChildProcessResult> WaitForExitAsync(TimeSpan timeoutDuration)
        {
            using var timeout = new CancellationTokenSource(timeoutDuration);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                var timedOut = await KillAndWaitAsync();
                throw new TimeoutException($"The {processDescription} exceeded {timeoutDuration.TotalSeconds:0} seconds. Owned child data and logs retained at {ownedRoot}.\n{timedOut.Stdout}\n{timedOut.Stderr}");
            }

            return await CaptureResultAsync();
        }

        public async Task<ChildProcessResult> KillAndWaitAsync()
        {
            await StopIfRunningAsync();

            return await CaptureResultAsync();
        }

        private async Task StopIfRunningAsync()
        {
            if (process.HasExited)
                return;

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The child exited between the liveness check and Kill.
            }

            await process.WaitForExitAsync();
        }

        private async Task<ChildProcessResult> CaptureResultAsync()
        {
            if (_capturedResult is not null)
                return _capturedResult;

            var exitCode = process.ExitCode;
            await File.WriteAllTextAsync(
                Path.Combine(ownedRoot, $"{logStem}.exit-code.txt"),
                exitCode.ToString(CultureInfo.InvariantCulture));
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            await SaveChildLogsAsync(ownedRoot, logStem, stdout, stderr);
            _capturedResult = new ChildProcessResult(exitCode, stdout, stderr);
            return _capturedResult;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopIfRunningAsync();

                if (_capturedResult is null)
                    await CaptureResultAsync();
            }
            finally
            {
                process.Dispose();
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
