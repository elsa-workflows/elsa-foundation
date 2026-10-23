using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal static partial class Program
{
    private static async Task<object> RunStaleProcessOriginAsync(string statePath)
    {
        Require(Path.IsPathFullyQualified(statePath), "Stale-origin state path must be absolute.");
        var fullPath = Path.GetFullPath(statePath);
        var releasePath = fullPath + ".origin-release";
        Require(!File.Exists(fullPath) && !File.Exists(releasePath), "Stale-origin state or release marker already exists.");
        var connection = RequiredProcessConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var control = new StaleProcessControl("origin", fullPath, releasePath, null, connection);
        using var host = BuildStaleProcessHost(connection, control);
        try
        {
            await StartProcessHostAsync(host, timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var prepared = await services.GetRequiredService<IRuleInvocationFactory>().PrepareAsync(
                NewIdentity("stale-process-origin"), Witness(), Facts("seed"), cancellationToken: timeout.Token);
            Require(prepared.Disposition == RuleInvocationPreparationDisposition.Prepared && prepared.Invocation is not null,
                $"Stale-origin invocation could not be prepared: {prepared.Disposition}.");
            control.Invocation = prepared.Invocation;
            var store = services.GetRequiredService<IRuleRunStore>();
            Require(store is StaleProcessStore, "Stale-origin store observer is not active.");
            var admission = await store.AdmitAsync(new RuleRunAdmission(prepared.Invocation), timeout.Token);
            Require(admission.Disposition == RuleAdmissionDisposition.Admitted,
                $"Stale-origin invocation was not admitted: {admission.Disposition}.");

            var state = await control.OriginReady.Task.WaitAsync(timeout.Token);
            Require(state.InvocationFingerprint == RuleRecordCodec.Fingerprint(prepared.Invocation).Sha256,
                "Stale-origin marker does not match the admitted invocation.");
            var stale = await control.OriginRejected.Task.WaitAsync(timeout.Token);
            var release = await control.OriginReleased.Task.WaitAsync(timeout.Token);
            Require(stale.Accept.Disposition == RuleReceiptDisposition.LeaseLost
                    && stale.Accept.ReceiptId is null && stale.Accept.ModelRevision is null
                    && stale.Accept.TerminalId is null && stale.Accept.NextRoundId is null,
                "Expired origin acceptance was not rejected without a receipt payload.");
            Require(stale.Read.Disposition == RuleWorkReadDisposition.LeaseLost
                    && stale.Read.CurrentToken is null && stale.Read.RoundInput is null
                    && stale.Read.PendingReceipt is null && stale.Read.TerminalId is null,
                "Expired origin read exposed successor work or ownership.");
            Require(release.Disposition == RuleLeaseDisposition.LeaseLost && release.Lease is null,
                "Expired origin release retained or changed the successor lease.");
            var after = await ReadStaleSnapshotAsync(services.GetRequiredService<IRuleRunReader>(), prepared.Invocation.Identity,
                timeout.Token);
            var afterJson = SerializeStaleSnapshot(after);
            Require(StringComparer.Ordinal.Equals(state.DurableSnapshot, afterJson),
                "Stale origin operations changed the complete durable run snapshot.");

            // The finally block stops the old polling worker before this result reaches the coordinator.
            return new
            {
                command = "process-stale-origin",
                passed = true,
                processId = Environment.ProcessId,
                machineName = Environment.MachineName,
                databaseEngine = state.DatabaseEngine,
                databaseEngineVersion = state.DatabaseEngineVersion,
                databaseName = state.DatabaseName,
                invocationFingerprint = state.InvocationFingerprint,
                owner = state.TokenOwner,
                fence = state.TokenFence,
                attemptId = state.AttemptId,
                receiptId = state.ReceiptId,
                semanticReceiptSha256 = state.SemanticReceiptSha256,
                staleAcceptDisposition = stale.Accept.Disposition.ToString(),
                staleAcceptHasNoPayload = true,
                staleReadDisposition = stale.Read.Disposition.ToString(),
                staleReadHasNoPayload = true,
                staleReleaseDisposition = release.Disposition.ToString(),
                durableSnapshotBefore = state.DurableSnapshot,
                durableSnapshotAfter = afterJson,
                durableSnapshotSha256 = RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(afterJson)),
                durableStateUnchanged = true
            };
        }
        finally
        {
            await StopProcessHostAsync(host, timeout.Token);
        }
    }

    private static async Task<object> RunStaleProcessSuccessorAsync(string statePath, string successorReadyPath)
    {
        var origin = ReadStaleOriginState(statePath);
        Require(Path.IsPathFullyQualified(successorReadyPath), "Successor-ready path must be absolute.");
        var readyPath = Path.GetFullPath(successorReadyPath);
        var releasePath = readyPath + ".successor-release";
        Require(!File.Exists(readyPath) && !File.Exists(releasePath), "Successor-ready state or release marker already exists.");
        var connection = RequiredProcessConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var control = new StaleProcessControl("successor", readyPath, releasePath, origin, connection)
        {
            Invocation = origin.Invocation
        };
        using var host = BuildStaleProcessHost(connection, control);
        try
        {
            await StartProcessHostAsync(host, timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var reader = services.GetRequiredService<IRuleRunReader>();
            var ready = await control.SuccessorReady.Task.WaitAsync(timeout.Token);
            Require(ready.ProcessId != origin.ProcessId && ready.MachineName == origin.MachineName,
                "Successor did not run as a separate process on the same machine.");
            Require(ready.TokenOwner != origin.TokenOwner && ready.TokenFence > origin.TokenFence
                    && ready.TokenExpectedModelRevision == origin.TokenExpectedModelRevision,
                "Successor did not acquire a new fenced lease for the same unchanged model revision.");
            Require(StringComparer.Ordinal.Equals(ready.DurableSnapshot, origin.DurableSnapshot),
                "Successor lease acquisition changed durable semantic state before evaluation.");
            var profile = await ReadPostgresProfileAsync(connection, timeout.Token);
            Require(profile.DatabaseName == origin.DatabaseName,
                "Successor connected to a different PostgreSQL database.");

            await WaitForReleaseAsync(releasePath, timeout.Token);
            var terminalView = await WaitForTerminalAsync(reader, origin.Invocation.Identity, timeout.Token);
            var terminal = await ReadFoundTerminalAsync(reader, origin.Invocation.Identity, timeout.Token);
            Require(terminalView.Status == RuleRunStatus.Succeeded && terminal.Kind == RuleTerminalKind.Succeeded,
                "Successor worker did not complete the rule program successfully.");
            Require(terminalView.CommittedRounds == 4 && terminal.CommittedRounds == 4
                    && terminalView.PendingReceiptId is null,
                "Successor worker did not commit exactly four rounds and clear pending work.");
            var finalFacts = FactAtoms(terminal.ResultFacts);
            Require(finalFacts.SequenceEqual(ExpectedFinalFacts, StringComparer.Ordinal),
                "Successor terminal facts differ from the deterministic closure.");
            var progress = await ReadProgressPageAsync(reader, origin.Invocation.Identity, timeout.Token);
            var facts = progress.Items.Where(item => item.Kind == RuleProgressItemKind.Fact).Select(item => item.Fact!.Atom)
                .OrderBy(atom => atom, StringComparer.Ordinal).ToArray();
            var rounds = progress.Items.Where(item => item.Kind == RuleProgressItemKind.Round).Select(item => item.Round!)
                .OrderBy(round => round.Ordinal).ToArray();
            Require(facts.SequenceEqual(ExpectedFinalFacts, StringComparer.Ordinal),
                "Successor fact history differs from its terminal facts.");
            Require(rounds.Length == 4 && rounds.Select(round => round.Ordinal).SequenceEqual(Enumerable.Range(0, 4)),
                "Successor round history is not exactly ordinals zero through three.");
            var first = rounds[0];
            Require(first.ReceiptId == origin.ReceiptId && first.SemanticReceiptSha256 == origin.SemanticReceiptSha256,
                "Successor replaced the origin's deterministic first receipt identity.");
            Require(first.AcceptedWorkerId == ready.TokenOwner && first.AcceptedLeaseFence == ready.TokenFence,
                "The successor did not own the accepted first evaluation attempt.");
            Require(rounds.All(round => round.ConsumedModelRevision is not null
                    && !string.IsNullOrWhiteSpace(round.AcceptedAttemptId)
                    && !string.IsNullOrWhiteSpace(round.AcceptedWorkerId)
                    && round.AcceptedLeaseFence is > 0),
                "Successor history is missing accepted or consumed attempt evidence.");
            var stable = await ReadStableFinalAsync(reader, origin.Invocation.Identity, timeout.Token);
            Require(stable.View.TerminalId == terminal.TerminalId
                    && stable.View.ModelRevision == terminalView.ModelRevision
                    && stable.Terminal.TerminalId == terminal.TerminalId
                    && RuleRecordCodec.TerminalBytes(stable.Terminal).AsSpan()
                        .SequenceEqual(RuleRecordCodec.TerminalBytes(terminal)),
                "Successor terminal was not stable across the final read.");
            return new
            {
                command = "process-stale-successor",
                passed = true,
                processId = Environment.ProcessId,
                originalProcessId = origin.ProcessId,
                machineName = Environment.MachineName,
                databaseEngine = profile.Engine,
                databaseEngineVersion = profile.Version,
                databaseName = profile.DatabaseName,
                invocationFingerprint = origin.InvocationFingerprint,
                successorOwner = ready.TokenOwner,
                successorFence = ready.TokenFence,
                originalLeaseExpiresAtUtc = ready.TokenExpiresAtUtc,
                latestLeaseExpiresAtUtc = control.LatestSuccessorLeaseExpiry,
                leaseRenewalCount = control.SuccessorLeaseRenewalCount,
                firstAttemptId = first.AcceptedAttemptId,
                firstReceiptId = first.ReceiptId,
                firstSemanticReceiptSha256 = first.SemanticReceiptSha256,
                terminalBase64 = Convert.ToBase64String(RuleRecordCodec.TerminalBytes(terminal)),
                terminalId = terminal.TerminalId,
                modelRevision = stable.View.ModelRevision,
                status = terminalView.Status.ToString(),
                committedRounds = terminalView.CommittedRounds,
                finalFacts,
                durableSnapshotBeforeEvaluation = ready.DurableSnapshot,
                finalRoundHistory = rounds.Select(round => new
                {
                    ordinal = round.Ordinal,
                    receiptId = round.ReceiptId,
                    semanticReceiptSha256 = round.SemanticReceiptSha256,
                    acceptedAttemptId = round.AcceptedAttemptId,
                    acceptedWorkerId = round.AcceptedWorkerId,
                    acceptedLeaseFence = round.AcceptedLeaseFence,
                    acceptedModelRevision = round.AcceptedModelRevision,
                    consumedModelRevision = round.ConsumedModelRevision
                }).ToArray(),
                terminalStabilityVerified = true,
                designStorageUsed = false
            };
        }
        finally
        {
            await StopProcessHostAsync(host, timeout.Token);
        }
    }

    private static IHost BuildStaleProcessHost(string connection, StaleProcessControl control) =>
        BuildObservedProcessHost(connection, suppressWorker: false,
            (store, reader) => new StaleProcessStore(store, reader, control));

    private static StaleOriginState ReadStaleOriginState(string statePath)
    {
        Require(Path.IsPathFullyQualified(statePath), "Stale-origin state path must be absolute.");
        var bytes = ReadBoundedFile(Path.GetFullPath(statePath), MaxProcessStateBytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        Require(document.RootElement.ValueKind == JsonValueKind.Object, "Stale-origin state must be a JSON object.");
        var state = JsonSerializer.Deserialize<StaleOriginState>(bytes, JsonOptions)
            ?? throw new ProbeAssertionException("Stale-origin state could not be decoded.");
        Require(state.SchemaVersion == 1 && state.ProcessId > 0 && !string.IsNullOrWhiteSpace(state.MachineName)
                && state.DatabaseEngine == "PostgreSQL" && !string.IsNullOrWhiteSpace(state.DatabaseEngineVersion)
                && !string.IsNullOrWhiteSpace(state.DatabaseName),
            "Stale-origin state lacks valid process or database provenance.");
        Require(state.InvocationBase64.Length <= RuleLimits.MaxEnvelopeBytes * 2
                && state.ReceiptRecordBase64.Length <= RuleLimits.MaxEnvelopeBytes * 2,
            "Stale-origin invocation or offered receipt exceeds its encoded byte bound.");
        byte[] invocationBytes;
        byte[] receiptBytes;
        try
        {
            invocationBytes = Convert.FromBase64String(state.InvocationBase64);
            receiptBytes = Convert.FromBase64String(state.ReceiptRecordBase64);
        }
        catch (FormatException)
        {
            throw new ProbeAssertionException("Stale-origin invocation or offered receipt is not valid Base64.");
        }
        var invocation = RuleRecordCodec.ReadInvocation(invocationBytes);
        var receipt = RuleRecordCodec.ReadReceiptRecord(receiptBytes);
        Require(RuleRecordCodec.InvocationBytes(invocation).AsSpan().SequenceEqual(invocationBytes)
                && RuleRecordCodec.Fingerprint(invocation).Sha256 == state.InvocationFingerprint,
            "Stale-origin invocation is not canonical or has a mismatched fingerprint.");
        Require(receipt.Identity.Run == invocation.Identity && receipt.Identity.Fingerprint == RuleRecordCodec.Fingerprint(invocation)
                && receipt.Identity.Ordinal == 0 && receipt.Identity.InputStateSha256 == state.InputStateSha256
                && receipt.ImplementationPinHash == RuleRecordCodec.PinHash(invocation.ExpectedImplementation)
                && receipt.SemanticReceiptSha256 == state.SemanticReceiptSha256
                && ComputeReceiptId(receipt) == state.ReceiptId,
            "Stale-origin candidate receipt does not match its invocation identity.");
        Require(state.TokenExpectedModelRevision == 1 && state.TokenFence > 0
                && state.TokenExpiresAtUtc != default && state.TokenOwner == state.AttemptWorkerId
                && state.TokenFence == state.AttemptLeaseFence && !string.IsNullOrWhiteSpace(state.AttemptId),
            "Stale-origin token and first attempt metadata are inconsistent.");
        Require(state.DurableSnapshotSha256 == RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(state.DurableSnapshot)),
            "Stale-origin durable snapshot digest is invalid.");
        state.Invocation = invocation;
        return state;
    }

    private static async Task<StaleSnapshot> ReadStaleSnapshotAsync(IRuleRunReader reader, RuleRunIdentity identity,
        CancellationToken cancellationToken)
    {
        var run = await ReadRunSnapshotAsync(reader, identity, cancellationToken);
        return new StaleSnapshot(run.View, run.Facts, run.Rounds,
            run.Terminal is null ? null : Convert.ToBase64String(RuleRecordCodec.TerminalBytes(run.Terminal)));
    }

    private static string SerializeStaleSnapshot(StaleSnapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);

    private sealed class StaleProcessControl(string mode, string statePath, string releasePath,
        StaleOriginState? origin, string connection)
    {
        public string Mode { get; } = mode;
        public string StatePath { get; } = statePath;
        public string ReleasePath { get; } = releasePath;
        public StaleOriginState? Origin { get; } = origin;
        public string Connection { get; } = connection;
        public RuleInvocationEnvelope? Invocation { get; set; }
        public TaskCompletionSource<StaleOriginState> OriginReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<StaleOriginOutcome> OriginRejected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<RuleLeaseConfirmation> OriginReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<StaleSuccessorState> SuccessorReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _captured;
        private int _successorLeaseRenewalCount;
        private long _latestSuccessorLeaseExpiryTicks;
        public bool TryCapture() => Interlocked.CompareExchange(ref _captured, 1, 0) == 0;
        public int SuccessorLeaseRenewalCount => Volatile.Read(ref _successorLeaseRenewalCount);
        public DateTimeOffset LatestSuccessorLeaseExpiry => new(Interlocked.Read(ref _latestSuccessorLeaseExpiryTicks), TimeSpan.Zero);
        public void SetSuccessorLeaseExpiry(DateTimeOffset expiresAt) =>
            Interlocked.Exchange(ref _latestSuccessorLeaseExpiryTicks, expiresAt.UtcTicks);
        public void RecordSuccessorRenewal(DateTimeOffset expiresAt)
        {
            SetSuccessorLeaseExpiry(expiresAt);
            Interlocked.Increment(ref _successorLeaseRenewalCount);
        }
    }

    private sealed class StaleProcessStore(IRuleRunStore store, IRuleRunReader reader, StaleProcessControl control)
        : IRuleRunStore, IRuleRunReader
    {
        public ValueTask<RuleAdmissionConfirmation> AdmitAsync(RuleRunAdmission admission, CancellationToken cancellationToken = default) => store.AdmitAsync(admission, cancellationToken);
        public ValueTask<RuleRefusalConfirmation> RefuseAdmissionAsync(RuleAdmissionRefusal refusal, CancellationToken cancellationToken = default) => store.RefuseAdmissionAsync(refusal, cancellationToken);
        public ValueTask<RuleCancellationConfirmation> CancelAsync(RuleCancellationRequest request, CancellationToken cancellationToken = default) => store.CancelAsync(request, cancellationToken);
        public ValueTask<RuleWorkClaimResult> ClaimWorkAsync(RuleClaimRequest request, CancellationToken cancellationToken = default) => store.ClaimWorkAsync(request, cancellationToken);
        public ValueTask<RuleLeaseConfirmation> RenewAsync(RuleWorkLease lease, CancellationToken cancellationToken = default) => store.RenewAsync(lease, cancellationToken);

        public async ValueTask<RuleLeaseConfirmation> ReleaseAsync(RuleWorkLease lease, CancellationToken cancellationToken = default)
        {
            var result = await store.ReleaseAsync(lease, cancellationToken);
            if (control.Mode == "origin" && control.OriginRejected.Task.IsCompletedSuccessfully)
                control.OriginReleased.TrySetResult(result);
            return result;
        }

        public async ValueTask<RuleReceiptConfirmation> AcceptRoundAsync(RuleWorkToken token, RuleRoundSubmission submission,
            CancellationToken cancellationToken = default)
        {
            if (control.Mode != "origin" || !MatchesInvocation(token.Lease.Identity) || !control.TryCapture())
                return await store.AcceptRoundAsync(token, submission, cancellationToken);
            try
            {
                var inputRead = await reader.ReadWorkAsync(token, cancellationToken);
                var input = inputRead.RoundInput;
                Require(inputRead.Disposition == RuleWorkReadDisposition.EvaluateRound && input is not null
                        && input.Identity.Ordinal == 0
                        && inputRead.CurrentToken?.Lease.Fence == token.Lease.Fence,
                    "Origin did not reach an actual first-round evaluation attempt before stale acceptance.");
                Require(submission.Receipt.Identity.Run == token.Lease.Identity
                        && submission.Receipt.Identity.Fingerprint == token.Lease.Fingerprint
                        && submission.Receipt.Identity.Ordinal == 0
                        && submission.Receipt.Identity.InputStateSha256 == input!.Identity.InputStateSha256
                        && submission.Attempt.WorkerId == token.Lease.Owner
                        && submission.Attempt.LeaseFence == token.Lease.Fence,
                    "Origin evaluator submission does not match its claimed work token.");
                var beforeAccept = await ReadStaleSnapshotAsync(reader, token.Lease.Identity, cancellationToken);
                Require(beforeAccept.View.Status == RuleRunStatus.Running && beforeAccept.View.ModelRevision == 1
                        && beforeAccept.View.CommittedRounds == 0 && beforeAccept.View.CommittedFactCount == 1
                        && beforeAccept.View.PendingReceiptId is null && beforeAccept.View.TerminalId is null
                        && beforeAccept.View.TerminalKind is null
                        && beforeAccept.Facts.SequenceEqual(new[] { "seed" }, StringComparer.Ordinal)
                        && beforeAccept.Rounds.Length == 0 && beforeAccept.TerminalBase64 is null,
                    "Origin was not paused at the untouched initial persisted state before first acceptance.");
                var snapshot = SerializeStaleSnapshot(beforeAccept);
                var profile = await ReadPostgresProfileAsync(control.Connection, cancellationToken);
                var invocation = control.Invocation!;
                var receiptBytes = RuleRecordCodec.ReceiptRecordBytes(submission.Receipt);
                var state = new StaleOriginState
                {
                    SchemaVersion = 1,
                    InvocationBase64 = Convert.ToBase64String(RuleRecordCodec.InvocationBytes(invocation)),
                    InvocationFingerprint = RuleRecordCodec.Fingerprint(invocation).Sha256,
                    ReceiptRecordBase64 = Convert.ToBase64String(receiptBytes),
                    ReceiptId = ComputeReceiptId(submission.Receipt),
                    SemanticReceiptSha256 = submission.Receipt.SemanticReceiptSha256,
                    InputStateSha256 = submission.Receipt.Identity.InputStateSha256,
                    TokenOwner = token.Lease.Owner,
                    TokenFence = token.Lease.Fence,
                    TokenExpiresAtUtc = token.Lease.ExpiresAt,
                    TokenExpectedModelRevision = token.ExpectedModelRevision,
                    AttemptId = submission.Attempt.AttemptId,
                    AttemptWorkerId = submission.Attempt.WorkerId,
                    AttemptLeaseFence = submission.Attempt.LeaseFence,
                    DurableSnapshot = snapshot,
                    DurableSnapshotSha256 = RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(snapshot)),
                    DatabaseEngine = profile.Engine,
                    DatabaseEngineVersion = profile.Version,
                    DatabaseName = profile.DatabaseName,
                    ProcessId = Environment.ProcessId,
                    MachineName = Environment.MachineName,
                    CapturedUtc = DateTimeOffset.UtcNow
                };
                WriteJson(control.StatePath, state);
                control.OriginReady.TrySetResult(state);
                await WaitForReleaseAsync(control.ReleasePath, cancellationToken);

                var result = await store.AcceptRoundAsync(token, submission, cancellationToken);
                var staleRead = await reader.ReadWorkAsync(token, cancellationToken);
                Require(result.Disposition == RuleReceiptDisposition.LeaseLost
                        && result.ReceiptId is null && result.ModelRevision is null
                        && result.TerminalId is null && result.NextRoundId is null,
                    "Expired origin acceptance did not return LeaseLost without payload.");
                Require(staleRead.Disposition == RuleWorkReadDisposition.LeaseLost
                        && staleRead.CurrentToken is null && staleRead.RoundInput is null
                        && staleRead.PendingReceipt is null && staleRead.TerminalId is null,
                    "Expired origin read did not return LeaseLost without successor payload.");
                var after = SerializeStaleSnapshot(await ReadStaleSnapshotAsync(reader, token.Lease.Identity, cancellationToken));
                Require(StringComparer.Ordinal.Equals(snapshot, after),
                    "Origin acceptance rejection or stale read changed durable state while successor was held.");
                control.OriginRejected.TrySetResult(new(result, staleRead, after));
                return result;
            }
            catch (Exception error)
            {
                control.OriginReady.TrySetException(error);
                control.OriginRejected.TrySetException(error);
                throw;
            }
        }

        private bool MatchesInvocation(RuleRunIdentity identity) => control.Invocation is { } invocation && invocation.Identity == identity;

        public async ValueTask<RuleWorkReadResult> ReadWorkAsync(RuleWorkToken token, CancellationToken cancellationToken = default)
        {
            var result = await reader.ReadWorkAsync(token, cancellationToken);
            var roundInput = result.RoundInput;
            if (control.Mode != "successor" || !MatchesInvocation(token.Lease.Identity)
                || result.Disposition != RuleWorkReadDisposition.EvaluateRound || roundInput is null || roundInput.Identity.Ordinal != 0
                || !control.TryCapture())
                return result;
            try
            {
                var current = result.CurrentToken ?? throw new ProbeAssertionException("Successor evaluation token is missing.");
                var origin = control.Origin ?? throw new ProbeAssertionException("Successor origin marker is missing.");
                Require(current.Lease.Owner != origin.TokenOwner && current.Lease.Fence > origin.TokenFence
                        && current.ExpectedModelRevision == origin.TokenExpectedModelRevision,
                    "Successor evaluation did not use a fresh lease at the origin's unchanged model revision.");
                var invocation = control.Invocation ?? throw new ProbeAssertionException("Successor invocation is missing.");
                Require(roundInput.Identity.Run == invocation.Identity
                        && roundInput.Identity.InputStateSha256 == origin.InputStateSha256
                        && roundInput.Facts.Select(fact => fact.Atom).SequenceEqual(invocation.InitialFacts.Select(fact => fact.Atom), StringComparer.Ordinal),
                    "Successor evaluation input differs from the original durable run.");
                var snapshot = SerializeStaleSnapshot(await ReadStaleSnapshotAsync(reader, invocation.Identity, cancellationToken));
                var profile = await ReadPostgresProfileAsync(control.Connection, cancellationToken);
                Require(profile.DatabaseName == origin.DatabaseName,
                    "Successor worker connected to a different PostgreSQL database.");
                var state = new StaleSuccessorState
                {
                    SchemaVersion = 1,
                    InvocationFingerprint = origin.InvocationFingerprint,
                    TokenOwner = current.Lease.Owner,
                    TokenFence = current.Lease.Fence,
                    TokenExpiresAtUtc = current.Lease.ExpiresAt,
                    TokenExpectedModelRevision = current.ExpectedModelRevision,
                    RoundOrdinal = roundInput.Identity.Ordinal,
                    InputStateSha256 = roundInput.Identity.InputStateSha256,
                    DurableSnapshot = snapshot,
                    DurableSnapshotSha256 = RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(snapshot)),
                    DatabaseEngine = profile.Engine,
                    DatabaseEngineVersion = profile.Version,
                    DatabaseName = profile.DatabaseName,
                    ProcessId = Environment.ProcessId,
                    MachineName = Environment.MachineName,
                    CapturedUtc = DateTimeOffset.UtcNow
                };
                control.SetSuccessorLeaseExpiry(current.Lease.ExpiresAt);
                WriteJson(control.StatePath, state);
                control.SuccessorReady.TrySetResult(state);
                var activeToken = current;
                while (!File.Exists(control.ReleasePath))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    if (File.Exists(control.ReleasePath))
                        break;
                    var renewed = await store.RenewAsync(activeToken.Lease, cancellationToken);
                    Require(renewed.Disposition == RuleLeaseDisposition.Renewed && renewed.Lease is not null
                            && renewed.Lease.Owner == current.Lease.Owner && renewed.Lease.Fence == current.Lease.Fence,
                        $"Successor could not retain its paused lease: {renewed.Disposition}.");
                    activeToken = new RuleWorkToken(renewed.Lease, current.ExpectedModelRevision);
                    control.RecordSuccessorRenewal(renewed.Lease.ExpiresAt);
                }
                return new RuleWorkReadResult(RuleWorkReadDisposition.EvaluateRound, activeToken,
                    roundInput, null, null);
            }
            catch (Exception error)
            {
                control.SuccessorReady.TrySetException(error);
                throw;
            }
        }

        public ValueTask<RuleDeliveryReadResult> ReadDeliveryAsync(RuleDeliveryLease lease, CancellationToken cancellationToken = default) => reader.ReadDeliveryAsync(lease, cancellationToken);
        public ValueTask<RuleRunReadResult> FindAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default) => reader.FindAsync(identity, cancellationToken);
        public ValueTask<RuleTerminalReadResult> ReadTerminalAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default) => reader.ReadTerminalAsync(identity, cancellationToken);
        public ValueTask<RuleProgressReadResult> ReadProgressAsync(RuleRunIdentity identity, RuleProgressPosition? after, int take, CancellationToken cancellationToken = default) => reader.ReadProgressAsync(identity, after, take, cancellationToken);
        public ValueTask<RuleDeliveryClaimResult> ClaimDeliveryAsync(RuleDeliveryClaimRequest request, CancellationToken cancellationToken = default) => store.ClaimDeliveryAsync(request, cancellationToken);
        public ValueTask<RuleTransitionConfirmation> AcknowledgeDeliveryAsync(RuleDeliveryLease lease, RuleDeliveryAcknowledgement acknowledgement, CancellationToken cancellationToken = default) => store.AcknowledgeDeliveryAsync(lease, acknowledgement, cancellationToken);
        public ValueTask<RuleTransitionConfirmation> ConsumeRoundAsync(RuleWorkToken token, RuleReceiptIdentity receipt, CancellationToken cancellationToken = default) => store.ConsumeRoundAsync(token, receipt, cancellationToken);
    }

    private sealed class StaleOriginState
    {
        public StaleOriginState() { }
        public int SchemaVersion { get; init; }
        public string InvocationBase64 { get; init; } = string.Empty;
        public string InvocationFingerprint { get; init; } = string.Empty;
        public string ReceiptRecordBase64 { get; init; } = string.Empty;
        public string ReceiptId { get; init; } = string.Empty;
        public string SemanticReceiptSha256 { get; init; } = string.Empty;
        public string InputStateSha256 { get; init; } = string.Empty;
        public string TokenOwner { get; init; } = string.Empty;
        public long TokenFence { get; init; }
        public DateTimeOffset TokenExpiresAtUtc { get; init; }
        public long TokenExpectedModelRevision { get; init; }
        public string AttemptId { get; init; } = string.Empty;
        public string AttemptWorkerId { get; init; } = string.Empty;
        public long AttemptLeaseFence { get; init; }
        public string DurableSnapshot { get; init; } = string.Empty;
        public string DurableSnapshotSha256 { get; init; } = string.Empty;
        public string DatabaseEngine { get; init; } = string.Empty;
        public string DatabaseEngineVersion { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
        public int ProcessId { get; init; }
        public string MachineName { get; init; } = string.Empty;
        public DateTimeOffset CapturedUtc { get; init; }
        [JsonIgnore] public RuleInvocationEnvelope Invocation { get; set; } = null!;
    }

    private sealed class StaleSuccessorState
    {
        public int SchemaVersion { get; init; }
        public string InvocationFingerprint { get; init; } = string.Empty;
        public string TokenOwner { get; init; } = string.Empty;
        public long TokenFence { get; init; }
        public DateTimeOffset TokenExpiresAtUtc { get; init; }
        public long TokenExpectedModelRevision { get; init; }
        public int RoundOrdinal { get; init; }
        public string InputStateSha256 { get; init; } = string.Empty;
        public string DurableSnapshot { get; init; } = string.Empty;
        public string DurableSnapshotSha256 { get; init; } = string.Empty;
        public string DatabaseEngine { get; init; } = string.Empty;
        public string DatabaseEngineVersion { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
        public int ProcessId { get; init; }
        public string MachineName { get; init; } = string.Empty;
        public DateTimeOffset CapturedUtc { get; init; }
    }

    private sealed record StaleOriginOutcome(RuleReceiptConfirmation Accept, RuleWorkReadResult Read, string AfterSnapshot);
    private sealed record StaleSnapshot(RuleRunView View, string[] Facts, RuleRoundProgress[] Rounds,
        string? TerminalBase64);
}
