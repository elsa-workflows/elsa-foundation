using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal static partial class Program
{
    private static async Task<object> RunProcessUpgradeAdmissionCheckAsync(string role, string statePath)
    {
        Require(role is "successor" or "rollback", "Upgrade admission role must be successor or rollback.");
        var state = ReadProcessState(statePath);
        Require(state.Boundary == "accepted", "Upgrade proof requires the original accepted-unconsumed receipt boundary.");
        var connection = RequiredProcessConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var host = BuildProcessHost(connection, control: null, suppressWorker: true, acceptNewInvocations: false);
        try
        {
            await StartProcessHostAsync(host, timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var invocation = state.Invocation;
            var installedPin = await services.GetRequiredService<IRuleImplementationIdentitySource>().ReadAsync(timeout.Token);
            Require(installedPin is not null, "Upgrade deployment did not load a trusted Rules identity.");
            RequireSameCommonSchemas(installedPin, invocation.ExpectedImplementation);
            var installedPinHash = RuleRecordCodec.PinHash(installedPin);
            var retainedPinHash = RuleRecordCodec.PinHash(invocation.ExpectedImplementation);
            if (role == "successor")
                Require(installedPinHash != retainedPinHash,
                    "Successor deployment's actual loaded semantic pin is not incompatible with the accepted invocation.");
            else
                Require(installedPinHash == retainedPinHash,
                    "Rollback deployment did not restore the invocation's original trusted semantic pin.");

            var reader = services.GetRequiredService<IRuleRunReader>();
            var store = services.GetRequiredService<IRuleRunStore>();
            var profile = await ReadPostgresProfileAsync(connection, timeout.Token);
            Require(profile.DatabaseName == state.DatabaseName && profile.Engine == state.DatabaseEngine,
                "Upgrade admission probe connected to a different process-witness database.");
            var before = await ReadRunSnapshotAsync(reader, invocation.Identity, timeout.Token);
            if (role == "successor")
                RequireAcceptedBoundarySnapshot(before, state);
            else
                RequireConsumedFirstRoundSnapshot(before, state);

            var replay = await store.AdmitAsync(new RuleRunAdmission(invocation), timeout.Token);
            Require(replay.Disposition == RuleAdmissionDisposition.Replay,
                $"Disabled {role} deployment did not replay the exact retained invocation: {replay.Disposition}.");
            var afterReplay = await ReadRunSnapshotAsync(reader, invocation.Identity, timeout.Token);
            Require(SameSnapshot(before, afterReplay), "Exact invocation replay mutated retained state while admission was disabled.");

            var fresh = new RuleInvocationEnvelope(NewIdentity("upgrade-disabled-new-admission"), invocation.Artifact,
                invocation.InitialFacts, invocation.MaxRounds, invocation.ExpectedImplementation,
                invocation.InvocationSchemaVersion, invocation.NativeDestination);
            var freshAdmission = await store.AdmitAsync(new RuleRunAdmission(fresh), timeout.Token);
            var freshLookup = await reader.FindAsync(fresh.Identity, timeout.Token);
            Require(freshAdmission.Disposition == RuleAdmissionDisposition.Unavailable
                    && freshLookup.Disposition == RuleRunReadDisposition.NotFound,
                $"Disabled {role} deployment admitted fresh work or created a durable row.");
            var afterFresh = await ReadRunSnapshotAsync(reader, invocation.Identity, timeout.Token);
            Require(SameSnapshot(before, afterFresh), "Rejected fresh admission mutated the retained run.");

            return new
            {
                command = "process-upgrade-admission",
                passed = true,
                role,
                acceptNewInvocations = false,
                workerSuppressedForAdmissionSnapshot = true,
                processId = Environment.ProcessId,
                databaseEngine = profile.Engine,
                databaseEngineVersion = profile.Version,
                databaseName = profile.DatabaseName,
                sourceProcessId = state.ProcessId,
                installedPinHash,
                retainedPinHash,
                installedPin,
                retainedInvocationFingerprint = state.InvocationFingerprint,
                exactAdmissionReplay = replay.Disposition.ToString(),
                freshAdmission = freshAdmission.Disposition.ToString(),
                freshLookup = freshLookup.Disposition.ToString(),
                snapshotBefore = SnapshotEvidence(before),
                snapshotAfterReplay = SnapshotEvidence(afterReplay),
                snapshotAfterFreshAdmission = SnapshotEvidence(afterFresh),
                snapshotUnchanged = true
            };
        }
        finally
        {
            await StopProcessHostAsync(host, timeout.Token);
        }
    }

    private static async Task<object> RunProcessUpgradeSuccessorAsync(string statePath, string admissionEvidencePath)
    {
        var state = ReadProcessState(statePath);
        Require(state.Boundary == "accepted", "Successor proof requires the original accepted-unconsumed receipt boundary.");
        var admissionEvidence = ReadUpgradeAdmissionEvidence(admissionEvidencePath, "successor");
        var accepted = admissionEvidence.SnapshotAfterFreshAdmission;
        RequireAcceptedBoundarySnapshot(accepted, state);
        Require(admissionEvidence.InstalledPinHash != RuleRecordCodec.PinHash(state.Invocation.ExpectedImplementation),
            "Successor admission proof did not establish a different loaded semantic pin.");

        var connection = RequiredProcessConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        using var host = BuildProcessHost(connection, control: null, suppressWorker: false, acceptNewInvocations: false);
        try
        {
            await StartProcessHostAsync(host, timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var installedPin = await services.GetRequiredService<IRuleImplementationIdentitySource>().ReadAsync(timeout.Token);
            Require(installedPin is not null, "Successor worker did not load a trusted Rules identity.");
            RequireSameCommonSchemas(installedPin, state.Invocation.ExpectedImplementation);
            var installedPinHash = RuleRecordCodec.PinHash(installedPin);
            Require(installedPinHash == admissionEvidence.InstalledPinHash
                    && installedPinHash != RuleRecordCodec.PinHash(state.Invocation.ExpectedImplementation),
                "Successor worker identity differs from its verified preflight deployment or matches the retained pin.");
            var profile = await ReadPostgresProfileAsync(connection, timeout.Token);
            Require(profile.DatabaseName == state.DatabaseName && profile.Engine == state.DatabaseEngine,
                "Successor worker connected to a different process-witness database.");
            Require(Environment.ProcessId != state.ProcessId, "Successor worker did not run in a new process.");

            var reader = services.GetRequiredService<IRuleRunReader>();
            var atWorkerStart = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token);
            Require(SameSnapshot(accepted, atWorkerStart) || IsConsumedFirstRoundSnapshot(atWorkerStart, state),
                "Successor worker started from an unexpected retained-state snapshot.");

            RunSnapshot afterConsumption = atWorkerStart;
            while (!IsConsumedFirstRoundSnapshot(afterConsumption, state))
            {
                await Task.Delay(50, timeout.Token);
                afterConsumption = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token);
                Require(afterConsumption.View.Status == RuleRunStatus.Running && afterConsumption.View.TerminalId is null,
                    "Incompatible successor changed the accepted receipt into a terminal outcome.");
                Require(afterConsumption.View.CommittedRounds <= 1,
                    "Incompatible successor evaluated or advanced beyond the retained common receipt.");
            }

            var stableOne = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token);
            await Task.Delay(250, timeout.Token);
            var stableTwo = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token);
            RequireConsumedFirstRoundSnapshot(stableOne, state);
            Require(SameSnapshot(stableOne, stableTwo),
                "Incompatible successor mutated the retained run after consuming its common receipt.");
            var store = services.GetRequiredService<IRuleRunStore>();
            var compatibleClaim = await ClaimCompatibleSuccessorWorkAsync(store, state, timeout.Token);
            Require(compatibleClaim.Token is not null
                    && compatibleClaim.Token.ExpectedModelRevision == stableTwo.View.ModelRevision,
                "Original implementation could not claim its next round after successor receipt consumption.");
            var release = await store.ReleaseAsync(compatibleClaim.Token.Lease, timeout.Token);
            Require(release.Disposition == RuleLeaseDisposition.Released,
                $"Compatible-implementation claim control did not release its lease: {release.Disposition}.");
            var afterCompatibleControl = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token);
            RequireConsumedFirstRoundSnapshot(afterCompatibleControl, state);
            Require(SameSnapshot(stableTwo, afterCompatibleControl),
                "Compatible-implementation claim control changed retained semantic state.");
            var eligibility = await store.ClaimWorkAsync(
                new RuleClaimRequest("upgrade-successor-eligibility", [installedPin],
                    [state.Invocation.InvocationSchemaVersion], [installedPin.ReceiptSchemaVersion],
                    [installedPin.StateSchemaVersion]), timeout.Token);
            Require(eligibility.Disposition == RuleWorkClaimDisposition.NoEligibleWork && eligibility.Token is null,
                $"Incompatible successor could claim semantic work after the common receipt: {eligibility.Disposition}.");
            var afterEligibility = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token);
            RequireConsumedFirstRoundSnapshot(afterEligibility, state);
            Require(SameSnapshot(afterCompatibleControl, afterEligibility),
                "Successor claim-eligibility probe mutated the retained run.");
            return new
            {
                command = "process-upgrade-successor",
                passed = true,
                acceptNewInvocations = false,
                workerEnabled = true,
                commonReceiptConsumed = true,
                semanticEvaluationAfterReceipt = false,
                processId = Environment.ProcessId,
                sourceProcessId = state.ProcessId,
                databaseEngine = profile.Engine,
                databaseEngineVersion = profile.Version,
                databaseName = profile.DatabaseName,
                installedPinHash,
                retainedPinHash = RuleRecordCodec.PinHash(state.Invocation.ExpectedImplementation),
                installedPin,
                retainedInvocationFingerprint = state.InvocationFingerprint,
                retainedReceiptId = state.ReceiptId,
                retainedReceiptSemanticSha256 = state.SemanticReceiptSha256,
                compatibleClaimControlDisposition = compatibleClaim.Disposition.ToString(),
                compatibleClaimControlExpectedModelRevision = compatibleClaim.Token.ExpectedModelRevision,
                compatibleClaimControlLeaseRelease = release.Disposition.ToString(),
                successorClaimDisposition = eligibility.Disposition.ToString(),
                successorClaimTokenAbsent = eligibility.Token is null,
                noEligibleSemanticEvaluationClaim = true,
                snapshotBeforeWorker = SnapshotEvidence(accepted),
                snapshotAtWorkerStart = SnapshotEvidence(atWorkerStart),
                snapshotAfterReceiptConsumption = SnapshotEvidence(stableOne),
                snapshotAfterStabilityRead = SnapshotEvidence(stableTwo),
                snapshotAfterSuccessorClaimCheck = SnapshotEvidence(afterEligibility),
                stateStableAfterReceiptConsumption = true
            };
        }
        finally
        {
            await StopProcessHostAsync(host, timeout.Token);
        }
    }

    private static void RequireSameCommonSchemas(RuleSemanticPin installed, RuleSemanticPin retained)
    {
        Require(installed.PinSchemaVersion == retained.PinSchemaVersion
                && StringComparer.Ordinal.Equals(installed.LanguageId, retained.LanguageId)
                && installed.LanguageVersion == retained.LanguageVersion
                && installed.ReceiptSchemaVersion == retained.ReceiptSchemaVersion
                && installed.StateSchemaVersion == retained.StateSchemaVersion,
            "Deployment does not support the retained common invocation, receipt and state schemas.");
    }

    private static void RequireAcceptedBoundarySnapshot(RunSnapshot snapshot, ProcessBoundaryState state)
    {
        var invocation = state.Invocation;
        Require(snapshot.View.Status == RuleRunStatus.Running && snapshot.View.ModelRevision == 2
                && snapshot.View.CommittedRounds == 0 && snapshot.View.CommittedFactCount == 1
                && snapshot.View.PendingReceiptId == state.ReceiptId && snapshot.View.TerminalId is null
                && snapshot.Terminal is null && snapshot.View.Fingerprint.Sha256 == state.InvocationFingerprint
                && snapshot.View.ArtifactId == invocation.Artifact.ArtifactId
                && snapshot.View.ArtifactSha256 == invocation.Artifact.Sha256
                && snapshot.View.ImplementationPinHash == RuleRecordCodec.PinHash(invocation.ExpectedImplementation)
                && snapshot.Facts.SequenceEqual(["seed"], StringComparer.Ordinal)
                && snapshot.TerminalFacts.Length == 0 && snapshot.Rounds.Length == 1
                && snapshot.Rounds[0].Ordinal == 0 && snapshot.Rounds[0].ReceiptId == state.ReceiptId
                && snapshot.Rounds[0].SemanticReceiptSha256 == state.SemanticReceiptSha256
                && snapshot.Rounds[0].ConsumedModelRevision is null
                && snapshot.Rounds[0].AcceptedModelRevision == state.AcceptedModelRevision
                && snapshot.Rounds[0].AcceptedAttemptId == state.AcceptedAttemptId
                && snapshot.Rounds[0].AcceptedWorkerId == state.AcceptedWorkerId
                && snapshot.Rounds[0].AcceptedLeaseFence == state.AcceptedLeaseFence,
            "Retained run is not the complete original accepted-unconsumed receipt snapshot.");
    }

    private static bool IsConsumedFirstRoundSnapshot(RunSnapshot snapshot, ProcessBoundaryState state) =>
        snapshot.View.Status == RuleRunStatus.Running && snapshot.View.ModelRevision == 3
        && snapshot.View.CommittedRounds == 1 && snapshot.View.CommittedFactCount == 3
        && snapshot.View.PendingReceiptId is null && snapshot.View.TerminalId is null
        && snapshot.Terminal is null && snapshot.View.Fingerprint.Sha256 == state.InvocationFingerprint
        && snapshot.View.ArtifactId == state.Invocation.Artifact.ArtifactId
        && snapshot.View.ArtifactSha256 == state.Invocation.Artifact.Sha256
        && snapshot.View.ImplementationPinHash == RuleRecordCodec.PinHash(state.Invocation.ExpectedImplementation)
        && snapshot.Facts.SequenceEqual(["left", "right", "seed"], StringComparer.Ordinal)
        && snapshot.TerminalFacts.Length == 0 && snapshot.Rounds.Length == 1
        && snapshot.Rounds[0].Ordinal == 0 && snapshot.Rounds[0].ReceiptId == state.ReceiptId
        && snapshot.Rounds[0].SemanticReceiptSha256 == state.SemanticReceiptSha256
        && snapshot.Rounds[0].ConsumedModelRevision is not null
        && snapshot.Rounds[0].AcceptedModelRevision == state.AcceptedModelRevision
        && snapshot.Rounds[0].AcceptedAttemptId == state.AcceptedAttemptId
        && snapshot.Rounds[0].AcceptedWorkerId == state.AcceptedWorkerId
        && snapshot.Rounds[0].AcceptedLeaseFence == state.AcceptedLeaseFence;

    private static void RequireConsumedFirstRoundSnapshot(RunSnapshot snapshot, ProcessBoundaryState state) =>
        Require(IsConsumedFirstRoundSnapshot(snapshot, state),
            "Successor did not preserve the original pin and consume exactly the one common accepted receipt.");

    private static async Task<RuleWorkClaimResult> ClaimCompatibleSuccessorWorkAsync(IRuleRunStore store,
        ProcessBoundaryState state, CancellationToken cancellationToken)
    {
        var invocation = state.Invocation;
        var request = new RuleClaimRequest("upgrade-compatible-eligibility-control",
            [invocation.ExpectedImplementation], [invocation.InvocationSchemaVersion],
            [invocation.ExpectedImplementation.ReceiptSchemaVersion], [invocation.ExpectedImplementation.StateSchemaVersion]);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            var claim = await store.ClaimWorkAsync(request, cancellationToken);
            if (claim.Disposition == RuleWorkClaimDisposition.Claimed && claim.Token is not null)
                return claim;
            Require(claim.Disposition == RuleWorkClaimDisposition.NoEligibleWork,
                $"Compatible implementation claim control returned {claim.Disposition}.");
            Require(DateTimeOffset.UtcNow < deadline,
                "Original implementation could not claim its next round after successor receipt consumption.");
            await Task.Delay(50, cancellationToken);
        }
    }

    private static JsonElement SnapshotEvidence(RunSnapshot snapshot) =>
        JsonSerializer.SerializeToElement(snapshot, JsonOptions);

    private static UpgradeAdmissionEvidence ReadUpgradeAdmissionEvidence(string path, string expectedRole)
    {
        Require(Path.IsPathFullyQualified(path), "Upgrade admission evidence path must be absolute.");
        var bytes = ReadBoundedFile(Path.GetFullPath(path), 2 * 1024 * 1024);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object,
            "Upgrade admission evidence is absent or malformed.");
        Require(root.TryGetProperty("passed", out var passed) && passed.ValueKind == JsonValueKind.True,
            "Upgrade admission evidence did not pass.");
        Require(root.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String
                && role.GetString() == expectedRole,
            "Upgrade admission evidence is for a different role.");
        Require(root.TryGetProperty("installedPinHash", out var installedPinHash)
                && installedPinHash.ValueKind == JsonValueKind.String,
            "Upgrade admission evidence lacks its loaded semantic pin hash.");
        Require(root.TryGetProperty("snapshotAfterFreshAdmission", out var snapshotElement)
                && snapshotElement.ValueKind == JsonValueKind.Object,
            "Upgrade admission evidence lacks its full retained-state snapshot.");
        var snapshot = JsonSerializer.Deserialize<RunSnapshot>(snapshotElement.GetRawText(), JsonOptions)
            ?? throw new ProbeAssertionException("Upgrade admission snapshot could not be decoded.");
        return new(installedPinHash.GetString()!, snapshot);
    }

    private sealed record UpgradeAdmissionEvidence(string InstalledPinHash, RunSnapshot SnapshotAfterFreshAdmission);
}
