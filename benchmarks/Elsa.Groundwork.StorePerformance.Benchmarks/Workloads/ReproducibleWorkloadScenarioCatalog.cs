using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Benchmark-owned deterministic contracts for the ten Spec 094 workloads that have a real
/// public-operation comparator. The frozen v1.0 document hashes remain historical evidence;
/// these v1.1 successors can be regenerated from code and verified before any timing is trusted.
/// </summary>
public static class ReproducibleWorkloadScenarioCatalog
{
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

    public static ReproducibleWorkloadScenario Get(string workloadId) =>
        Successors.TryGetValue(workloadId, out var scenario)
            ? scenario
            : throw new ArgumentOutOfRangeException(nameof(workloadId), workloadId, "No reproducible v1.1 workload successor is registered.");

    public static string SerializeContractSummary() => JsonSerializer.Serialize(new
    {
        successors = Successors.Values.OrderBy(scenario => scenario.WorkloadId).Select(scenario => new
        {
            id = scenario.WorkloadId,
            scenario.Version,
            scenario.Seed,
            inputFingerprintSha256 = scenario.ComputeInputFingerprint(),
            resultDigestSha256 = scenario.ComputeResultDigest()
        }),
        blocked = new
        {
            id = BlockedWorkloadId,
            version = BlockedVersion,
            inputFingerprintSha256 = BlockedInputFingerprint,
            resultDigestSha256 = BlockedResultDigest,
            reasonCode = BlockedReasonCode,
            reason = BlockedReason
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
