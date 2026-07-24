using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Benchmark-owned deterministic contract vectors for ten Spec 094 workloads. These definitions
/// describe the inputs, operation names, and expected observations that future real EF and Groundwork
/// adapter runners must execute; they are not public-operation runners themselves.
/// </summary>
public static class ReproducibleWorkloadScenarioCatalog
{
    public const string ReadyReasonCode = "benchmark.ready";
    public const string BlockedWorkloadId = "secret-create-read-list";
    public const string BlockedVersion = "1.0.0";
    public const string BlockedInputFingerprint = "339a6adc9ba6c34e85ce43eafd3e0b8b7b74f7ccbb7d52bd34efe1fbe394014c";
    public const string BlockedResultDigest = "615f7bbd8e160dd34d38180d5def0e99d0b4225822e6ebee5ea31ed21bbabcdb";
    public const string BlockedReasonCode = "comparator.secret.real-ef-required";
    public const string BlockedReason = "A real EF Secret repository comparator is required; synthetic or waived comparison is not admissible.";

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyDictionary<string, ReproducibleWorkloadScenario> Successors { get; } =
        CreateSuccessors().ToDictionary(scenario => scenario.WorkloadId, StringComparer.Ordinal);

    /// <summary>
    /// Independent literal golden vectors. These values deliberately do not derive from the scenario
    /// generator, so a generator change cannot update both the expected and actual hashes together.
    /// </summary>
    public static IReadOnlyDictionary<string, WorkloadGoldenVector> GoldenVectors { get; } =
        new Dictionary<string, WorkloadGoldenVector>(StringComparer.Ordinal)
        {
            ["bookmark-lookup"] = new("d006e25e22dc8d9374d8931f03e27c6dc45c27314bfe2f819a4dd61b588062e8", "e723ae42c3fd4e970cff04d4a6e867fa40b8d6ea23b0305ab82bf80d3916d6a9"),
            ["checkpoint-commit"] = new("ee4cef346ca64739bbe7cfc84ee3f74e6acefec582f537c685991ca73c62ce13", "ebb92b59a7a331e863c813f7110272093be6a78794a9cc7a0d914103ab4c9c62"),
            ["command-send-lease-ack"] = new("a108e41c890af94ee37d610817e2c4d6339451cbfbbd0e33e0bd794d0d1af5b1", "86439fbc13d29102d02615ee98a5beb53e008e673f6523681e3ee2d926d3389f"),
            ["due-timer-selection"] = new("02cfb91f4f415fcfe8fe6cd64e7c056b88b908e068735d2ec91eb81e0ec8d5bd", "8f380d449eb3a8e88f1edbea73cf9a7ddfa7a7502cab3ac5a8fcfe3e175ffed3"),
            ["iam-normalized-lookup-update"] = new("5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9", "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc"),
            ["outbox-drain"] = new("bc5c6ca1113e78fe948a61de35c66a644129c79028a198d9143dc316cea7bede", "7228f024095bc2fadc0649e0841d56259f3408b55368911ea402b7d96c8b2e71"),
            ["placement-takeover"] = new("17f22a7e7896b3842ebd771e604b13e859d1b480bc5b6093ce576f14a673e985", "3ad65cc7ff9287f9c20a68ec6cd267bc78fa083fb775dda36062c185706fb4b4"),
            ["queue-drain"] = new("15f2d5f9dc8d5814a1613156b7c686e59a150a35bd7e51787a145b6d7230d5e2", "7db639fdbfddc02973a7275d7c0e8835872b62449ca160e97e8086c0ca46eba4"),
            ["recovery-scan"] = new("36277c9b9c525d4cbb611c1a7e83c96a02eb3434fb85b6657ce2ede9b8a7a5e3", "3c7cae42737a2a995968852a862f769070a016b4e4a0289c7a9a5e7205e9eabf"),
            ["recurring-schedule-selection"] = new("384bcbf0fd72f306b63d78b71a8130c4e2e02de146cbd45d066ef581f4d78d17", "9728bad4f576c7e50c3f6210994524ffb1d77761c5258a71f27fe1cf1793cec4"),
            ["secret-create-read-list"] = new("339a6adc9ba6c34e85ce43eafd3e0b8b7b74f7ccbb7d52bd34efe1fbe394014c", "615f7bbd8e160dd34d38180d5def0e99d0b4225822e6ebee5ea31ed21bbabcdb"),
            ["trigger-binding-stimulus-lookup"] = new("4f2515dfa9549935712019f178283f79e6ac1cc9428e810524e733cfdea4cabc", "00b6651345cdb8b6724a205b094c712d383c7a19ef87dcce6fdf026bc7dd7c8a")
        };

    public static ReproducibleWorkloadScenario Get(string workloadId) =>
        Successors.TryGetValue(workloadId, out var scenario)
            ? scenario
            : throw new ArgumentOutOfRangeException(nameof(workloadId), workloadId, "No reproducible v1.1 workload successor is registered.");

    public static string SerializeDefinitionSummary() => JsonSerializer.Serialize(new
    {
        definitions = Successors.Values.OrderBy(scenario => scenario.WorkloadId).Select(scenario => new
        {
            id = scenario.WorkloadId,
            scenario.Version,
            scenario.Seed,
            inputFingerprintSha256 = scenario.ComputeInputFingerprint(),
            resultDigestSha256 = scenario.ComputeResultDigest(),
            benchmarkAdmission = new { status = "ready", reason = ReadyReasonCode }
        }),
        blocked = new
        {
            id = BlockedWorkloadId,
            version = BlockedVersion,
            inputFingerprintSha256 = BlockedInputFingerprint,
            resultDigestSha256 = BlockedResultDigest,
            benchmarkAdmission = new { status = "blocked", reason = BlockedReasonCode },
            explanation = BlockedReason
        }
    }, CanonicalJson);

    private static IEnumerable<ReproducibleWorkloadScenario> CreateSuccessors()
    {
        yield return Scenario(
            "checkpoint-commit",
            "runtime-checkpoint-commit",
            "spec094-checkpoint-commit-v1.1",
            [
                ("activityChangesPerCheckpoint", 4),
                ("checkpointCount", 1024),
                ("concurrentFenceContenders", 2),
                ("durableValueChangesPerCheckpoint", 3),
                ("executionCount", 128),
                ("outboxEntriesPerCheckpoint", 2),
                ("payloadBytes", 512),
                ("timedSetup", "excluded")
            ],
            ["seed-fenced-executions", "commit-checkpoint-bundle", "replay-equivalent-commit", "attempt-stale-fence-commit", "reopen-and-read-committed-bundle"],
            parameters => Observations(
                ("acceptedCheckpointCount", Int(parameters, "checkpointCount")),
                ("activityChangeCount", Int(parameters, "checkpointCount") * Int(parameters, "activityChangesPerCheckpoint")),
                ("committedBundleIdentityDigest", SequenceDigest("checkpoint", Int(parameters, "checkpointCount"))),
                ("durableValueChangeCount", Int(parameters, "checkpointCount") * Int(parameters, "durableValueChangesPerCheckpoint")),
                ("outboxEntryCount", Int(parameters, "checkpointCount") * Int(parameters, "outboxEntriesPerCheckpoint")),
                ("replayCreatedDuplicateWork", false),
                ("reopenedBundleMatched", true),
                ("staleFenceRejected", true)));

        yield return Scenario(
            "bookmark-lookup",
            "runtime-bookmark-state",
            "spec094-bookmark-lookup-v1.1",
            [
                ("bookmarksPerWorkflow", 64),
                ("matchingBookmarks", 37),
                ("pageSize", 25),
                ("payloadBytes", 256),
                ("tenantCount", 2),
                ("timedSetup", "excluded"),
                ("workflowCount", 128)
            ],
            ["seed-bookmarks", "lookup-by-stimulus-and-type", "read-next-bounded-page", "verify-cross-scope-isolation"],
            parameters => Observations(
                ("crossScopeResultCount", 0),
                ("firstPageCount", Int(parameters, "pageSize")),
                ("matchingBookmarkIdentityDigest", SequenceDigest("bookmark-match", Int(parameters, "matchingBookmarks"))),
                ("secondPageCount", Int(parameters, "matchingBookmarks") - Int(parameters, "pageSize")),
                ("stableContinuation", "bookmark-match-0025")));

        yield return Scenario(
            "trigger-binding-stimulus-lookup",
            "runtime-trigger-binding-stimulus-lookup",
            "spec094-trigger-binding-stimulus-lookup-v1.1",
            [
                ("activeMatches", 31),
                ("bindingsPerPublication", 48),
                ("pageSize", 20),
                ("publicationCount", 96),
                ("retiredBindings", 17),
                ("tenantCount", 2),
                ("timedSetup", "excluded")
            ],
            ["seed-publications-and-bindings", "lookup-active-bindings-by-stimulus-type", "load-executable-source-references", "verify-publication-and-scope-isolation"],
            parameters => Observations(
                ("activeBindingIdentityDigest", SequenceDigest("active-binding", Int(parameters, "activeMatches"))),
                ("crossScopeResultCount", 0),
                ("executableSourceReferenceCount", Int(parameters, "activeMatches")),
                ("firstPageCount", Int(parameters, "pageSize")),
                ("retiredBindingResultCount", 0),
                ("secondPageCount", Int(parameters, "activeMatches") - Int(parameters, "pageSize"))));

        yield return Scenario(
            "recovery-scan",
            "runtime-recovery-scan",
            "spec094-recovery-scan-v1.1",
            [
                ("executionCount", 2048),
                ("fixedNowUtc", "2026-07-20T10:00:00Z"),
                ("liveExecutions", 1875),
                ("pageSize", 64),
                ("recoverableCandidates", 173),
                ("tenantCount", 2),
                ("timedSetup", "excluded")
            ],
            ["seed-live-and-recoverable-state", "scan-recovery-candidates", "reopen-and-rescan", "verify-bounded-order-and-non-candidates"],
            parameters => Observations(
                ("candidateIdentityDigest", SequenceDigest("recovery-candidate", Int(parameters, "recoverableCandidates"))),
                ("firstPageCount", Int(parameters, "pageSize")),
                ("liveExecutionResultCount", 0),
                ("reopenedCandidateIdentityDigest", SequenceDigest("recovery-candidate", Int(parameters, "recoverableCandidates"))),
                ("scanNowUtc", String(parameters, "fixedNowUtc"))));

        yield return Scenario(
            "queue-drain",
            "runtime-scheduler-queue-drain",
            "spec094-queue-drain-v1.1",
            [
                ("batchSize", 16),
                ("concurrentClaimants", 2),
                ("poisonItems", 3),
                ("retryableItems", 5),
                ("timedSetup", "excluded"),
                ("workItemsPerWorkflow", 32),
                ("workflowCount", 128)
            ],
            ["enqueue-work-items", "claim-bounded-batch", "complete-current-claims", "retry-expired-claim", "record-and-read-poison-state", "attempt-stale-acknowledgement"],
            parameters => Observations(
                ("claimedIdentityDigest", SequenceDigest("scheduler-work", Int(parameters, "batchSize"))),
                ("completedItemCount", Int(parameters, "batchSize") - Int(parameters, "retryableItems") - Int(parameters, "poisonItems")),
                ("currentOwnerCount", Int(parameters, "batchSize")),
                ("poisonItemCount", Int(parameters, "poisonItems")),
                ("retryableItemCount", Int(parameters, "retryableItems")),
                ("staleAcknowledgementRejected", true)));

        yield return Scenario(
            "outbox-drain",
            "runtime-post-commit-outbox-drain",
            "spec094-outbox-drain-v1.1",
            [
                ("batchSize", 32),
                ("concurrentClaimants", 2),
                ("dueEntries", 211),
                ("fixedNowUtc", "2026-07-20T10:00:00Z"),
                ("outboxEntryCount", 1024),
                ("retryableEntries", 7),
                ("timedSetup", "excluded")
            ],
            ["seed-due-and-not-due-outbox-entries", "claim-due-batch", "record-delivered-and-retryable-results", "reclaim-after-visibility-expiry", "attempt-stale-completion"],
            parameters => Observations(
                ("claimedIdentityDigest", SequenceDigest("outbox-due", Int(parameters, "batchSize"))),
                ("deliveredEntryCount", Int(parameters, "batchSize") - Int(parameters, "retryableEntries")),
                ("notDueEntryResultCount", 0),
                ("retryableEntryCount", Int(parameters, "retryableEntries")),
                ("staleCompletionRejected", true),
                ("visibilityNowUtc", String(parameters, "fixedNowUtc"))));

        yield return Scenario(
            "due-timer-selection",
            "runtime-durable-timer-selection",
            "spec094-due-timer-selection-v1.1",
            [
                ("concurrentClaimants", 2),
                ("dueTimers", 193),
                ("fixedNowUtc", "2026-07-20T10:00:00Z"),
                ("pageSize", 50),
                ("sameDueTimestampTimers", 17),
                ("timedSetup", "excluded"),
                ("timerCount", 2048)
            ],
            ["seed-due-and-not-due-timers", "list-bounded-due-timers", "advance-due-timer", "attempt-stale-advance", "reopen-and-read-due-state"],
            parameters => Observations(
                ("advancedTimerId", "timer-due-0000"),
                ("dueIdentityDigest", SequenceDigest("timer-due", Int(parameters, "dueTimers"))),
                ("firstPageCount", Int(parameters, "pageSize")),
                ("notDueResultCount", 0),
                ("reopenedAdvanceMatched", true),
                ("staleAdvanceRejected", true),
                ("tieIdentityDigest", SequenceDigest("timer-due", Int(parameters, "sameDueTimestampTimers")))));

        yield return Scenario(
            "recurring-schedule-selection",
            "runtime-recurring-schedule-selection",
            "spec094-recurring-schedule-selection-v1.1",
            [
                ("concurrentAdvancers", 2),
                ("dueSchedules", 179),
                ("fixedNowUtc", "2026-07-20T10:00:00Z"),
                ("inactivePublications", 41),
                ("pageSize", 50),
                ("publicationCount", 256),
                ("scheduleCount", 2048),
                ("timedSetup", "excluded")
            ],
            ["seed-publications-and-schedules", "list-bounded-due-schedules", "load-publication-projections", "advance-current-schedule", "attempt-stale-advance", "reopen-and-read-projection-state"],
            parameters => Observations(
                ("advancedScheduleId", "schedule-due-0000"),
                ("dueScheduleIdentityDigest", SequenceDigest("schedule-due", Int(parameters, "dueSchedules"))),
                ("firstPageCount", Int(parameters, "pageSize")),
                ("inactivePublicationResultCount", 0),
                ("loadedProjectionCount", Int(parameters, "pageSize")),
                ("reopenedProjectionMatched", true),
                ("staleAdvanceRejected", true)));

        yield return Scenario(
            "placement-takeover",
            "distributed-placement-takeover",
            "spec094-placement-takeover-v1.1",
            [
                ("activePlacements", 256),
                ("concurrentClaimants", 2),
                ("executionCount", 512),
                ("fixedNowUtc", "2026-07-20T10:00:00Z"),
                ("leaseDurationSeconds", 30),
                ("takeoverCandidates", 64),
                ("timedSetup", "excluded")
            ],
            ["seed-placement-leases", "claim-current-placement", "renew-current-placement", "advance-past-expiry", "take-over-expired-placement", "attempt-stale-release", "reopen-and-read-current-placement"],
            parameters => Observations(
                ("currentOwner", "worker-beta"),
                ("placementTokenSequence", new[] { 1, 2, 3 }),
                ("reopenedOwnerMatched", true),
                ("staleReleaseRejected", true),
                ("takeoverCandidateIdentityDigest", SequenceDigest("placement-expired", Int(parameters, "takeoverCandidates")))));

        yield return Scenario(
            "command-send-lease-ack",
            "distributed-command-send-lease-ack",
            "spec094-command-send-lease-ack-v1.1",
            [
                ("batchSize", 16),
                ("commandsPerWorkflow", 64),
                ("concurrentLeasers", 2),
                ("concurrentSenders", 2),
                ("fixedNowUtc", "2026-07-20T10:00:00Z"),
                ("timedSetup", "excluded"),
                ("visibilityTimeoutSeconds", 30),
                ("workflowCount", 128)
            ],
            ["seed-command-streams", "send-concurrent-commands", "lease-visible-bounded-batch", "advance-past-visibility-expiry", "re-lease-current-batch", "attempt-stale-acknowledgement", "acknowledge-current-batch", "reopen-and-count-pending"],
            parameters => Observations(
                ("acknowledgedCount", Int(parameters, "batchSize")),
                ("leasedIdentityDigest", SequenceDigest("command", Int(parameters, "batchSize"))),
                ("pendingAfterReopen", Int(parameters, "workflowCount") * Int(parameters, "commandsPerWorkflow") - Int(parameters, "batchSize")),
                ("redeliveredCount", Int(parameters, "batchSize")),
                ("staleAcknowledgementRejected", true)));
    }

    private static ReproducibleWorkloadScenario Scenario(
        string workloadId,
        string scenarioId,
        string seed,
        IEnumerable<(string Name, object Value)> parameters,
        IReadOnlyList<string> operations,
        Func<IReadOnlyDictionary<string, object>, IReadOnlyDictionary<string, object>> observe) =>
        new(workloadId, "1.1.0", scenarioId, seed, Parameters(parameters), operations, observe);

    private static IReadOnlyDictionary<string, object> Parameters(IEnumerable<(string Name, object Value)> values) =>
        new SortedDictionary<string, object>(values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, object> Observations(params (string Name, object Value)[] values) =>
        Parameters(values);

    private static int Int(IReadOnlyDictionary<string, object> parameters, string name) => (int)parameters[name];
    private static string String(IReadOnlyDictionary<string, object> parameters, string name) => (string)parameters[name];

    private static string SequenceDigest(string prefix, int count) =>
        Hash(JsonSerializer.Serialize(Enumerable.Range(0, count).Select(index => $"{prefix}-{index:D4}"), CanonicalJson));

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, CanonicalJson);
}

public sealed class ReproducibleWorkloadScenario
{
    private readonly Func<IReadOnlyDictionary<string, object>, IReadOnlyDictionary<string, object>> _observe;

    internal ReproducibleWorkloadScenario(
        string workloadId,
        string version,
        string scenarioId,
        string seed,
        IReadOnlyDictionary<string, object> parameters,
        IReadOnlyList<string> operationSequence,
        Func<IReadOnlyDictionary<string, object>, IReadOnlyDictionary<string, object>> observe)
    {
        WorkloadId = workloadId;
        Version = version;
        ScenarioId = scenarioId;
        Seed = seed;
        Parameters = parameters;
        OperationSequence = operationSequence;
        _observe = observe;
    }

    public string WorkloadId { get; }
    public string Version { get; }
    public string ScenarioId { get; }
    public string Seed { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
    public IReadOnlyList<string> OperationSequence { get; }

    public string ComputeInputFingerprint() => ReproducibleWorkloadScenarioCatalog.Hash(
        ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            ScenarioId,
            Seed,
            Parameters,
            OperationSequence
        }));

    public IReadOnlyDictionary<string, object> CreateExpectedObservations() => _observe(Parameters);

    public string ComputeResultDigest() => ReproducibleWorkloadScenarioCatalog.Hash(
        ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            ScenarioId,
            InputFingerprint = ComputeInputFingerprint(),
            Operations = OperationSequence,
            ObservableResults = CreateExpectedObservations()
        }));
}

public sealed record WorkloadGoldenVector(string InputFingerprint, string ResultDigest);
