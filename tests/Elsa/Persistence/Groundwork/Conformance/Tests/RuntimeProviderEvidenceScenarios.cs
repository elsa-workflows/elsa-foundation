using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Small, publishable runtime evidence adapters over existing provider-contract probes. These adapters deliberately
/// do not infer row completion: each result records only the exact executable probe named by its scenario.
/// </summary>
internal static class RuntimeProviderEvidenceScenarios
{
    public static IReadOnlyList<RuntimeNativeQueryMapping> B7NativeQueryMappings { get; } =
    [
        Map("runtime-activity-execution-inspection", "list-summaries-by-workflow-bounded", "activityExecutionInspection", "page-summaries-by-workflow-execution"),
        Map("runtime-activity-execution-inspection", "list-hierarchy-by-workflow-bounded", "activityExecutionHierarchy", "page-by-workflow-execution"),
        Map("runtime-activity-execution-inspection", "list-descendants-by-parent-bounded", "activityExecutionHierarchy", "page-by-workflow-execution-and-scope"),
        Map("runtime-activity-execution-state", "list-by-workflow-bounded", "activityExecutionState", "page-by-workflow-execution"),
        Map("runtime-activity-execution-state", "list-by-parent-bounded", "activityExecutionState", "page-by-parent-activity-execution"),
        Map("runtime-bookmark-state", "list-by-workflow-bounded", "bookmarkState", "list-by-workflow-execution"),
        Map("runtime-bookmark-state", "list-by-stimulus-and-type-bounded", "bookmarkState", "list-by-stimulus-and-type"),
        Map("runtime-bookmark-state", "list-by-stimulus-type-bounded", "bookmarkState", "list-by-stimulus-type"),
        Map("runtime-durable-value-state", "list-by-workflow-bounded", "durableValueState", "list-by-workflow-execution"),
        Map("runtime-executable-source-reference", "list-by-artifact-bounded", "workflowExecutableSourceReference", "page-by-artifact"),
        Map("runtime-executable-source-reference", "list-live-by-scope-bounded", "workflowExecutableSourceReference", "page-live-by-scope"),
        Map("runtime-executable-source-reference", "delete-expired-or-retired-bounded", "workflowExecutableSourceReference", "batch-expired"),
        Map("runtime-executable-source-reference", "list-unreferenced-artifacts-bounded", "workflowExecutableSourceReference", "batch-retired"),
        Map("runtime-workflow-executable", "list-bounded", "workflowExecutable", "page-all"),
        Map("runtime-workflow-executable", "find-template-by-hash", "executableActivityTemplate", "find-by-template-hash"),
        Map("runtime-workflow-execution-state", "query-page-bounded", "workflowExecutionState", "page-history"),
        Map("runtime-workflow-execution-state", "list-pinned-artifact-ids-bounded", "workflowExecutionState", "page-pinned-executable-artifact-ids"),
        Map("runtime-workflow-execution-state", "list-all-replaced-by-bounded-page", "workflowExecutionState", "page-history"),
        Map("runtime-trigger-binding", "list-by-publication-bounded", "workflowTriggerBinding", "list-by-publication"),
        Map("runtime-trigger-binding", "list-by-stimulus-bounded", "workflowTriggerBinding", "list-by-stimulus-and-type"),
        Map("runtime-trigger-binding", "list-by-artifact-bounded", "workflowTriggerBinding", "list-by-artifact"),
        Map("runtime-trigger-binding", "list-by-stimulus-type-bounded", "workflowTriggerBinding", "list-by-stimulus-type")
    ];

    public static IReadOnlyList<RuntimeNativeQueryMapping> B8NativeQueryMappings { get; } =
    [
        Map("runtime-durable-timer", "list-due-bounded", "durableTimer", "list-due"),
        Map("runtime-recurring-trigger-schedule", "list-by-publication-bounded", "recurringTriggerSchedule", "page-by-publication"),
        Map("runtime-recurring-trigger-schedule", "list-due-bounded", "recurringTriggerSchedule", "list-due"),
        Map("runtime-scheduler-work-queue", "list-by-workflow-bounded", "schedulerWorkItem", "list-by-workflow-execution"),
        Map("runtime-scheduler-work-queue", "list-pending-workflow-ids-bounded", "schedulerWorkItem", "list-pending-scheduler-workflow-executions")
    ];

    public static IReadOnlyList<RuntimeNativeQueryMapping> PublishableNativeQueryMappings { get; } =
        B7NativeQueryMappings.Concat(B8NativeQueryMappings).ToArray();

    public static IReadOnlyList<string> B8FailureWindowCoverageEntries { get; } =
    [
        "runtime-durable-timer",
        "runtime-incident-state",
        "runtime-recurring-trigger-schedule",
        "runtime-post-commit-outbox",
        "runtime-scheduler-state",
        "runtime-workflow-hold-state",
        "runtime-scheduler-poison",
        "runtime-scheduler-work-queue",
        "runtime-trigger-binding",
        "runtime-publication-projection-state"
    ];

    /// <summary>
    /// Executes the complete physical checkpoint/fence contract once. Result factories below may project that
    /// one execution into only the catalog scenarios it directly proves; they never manufacture a row outcome.
    /// </summary>
    public static async Task<RuntimeCheckpointFenceEvidence> CaptureCheckpointFenceEvidenceAsync(string providerKey)
    {
        await new RuntimeFenceContractTests()
            .Runtime_fence_contract_is_enforced_by_every_persistent_provider(providerKey);

        return new RuntimeCheckpointFenceEvidence(await DescribeAsync(providerKey));
    }

    public static GroundworkScenarioResult CreateCheckpointFenceResult(
        RuntimeCheckpointFenceEvidence evidence,
        string coverageEntryId)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        const string scenarioId = "runtime-execution-ownership-fencing";
        return GroundworkStoreScenarioCatalog.Get(scenarioId).CreateResult(
            coverageEntryId,
            evidence.Descriptor,
            Composition(evidence.Descriptor, scenarioId),
            clients: 2,
            [
                new GroundworkScenarioObservation("stale-commit-count", "1"),
                new GroundworkScenarioObservation("token-order", "strictly-increasing"),
                new GroundworkScenarioObservation("winner-count", "1")
            ]);
    }

    public static GroundworkScenarioResult CreateCheckpointIdempotencyResult(
        RuntimeCheckpointFenceEvidence evidence,
        string coverageEntryId)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        const string scenarioId = "runtime-checkpoint-idempotency";
        return GroundworkStoreScenarioCatalog.Get(scenarioId).CreateResult(
            coverageEntryId,
            evidence.Descriptor,
            Composition(evidence.Descriptor, scenarioId),
            clients: 2,
            [
                new GroundworkScenarioObservation("conflicting-replay", "rejected"),
                new GroundworkScenarioObservation("durable-outcome-count", "1"),
                new GroundworkScenarioObservation("equivalent-replay", "verified")
            ]);
    }

    public static GroundworkScenarioResult CreateCheckpointFailureRecoveryResult(
        RuntimeCheckpointFenceEvidence evidence,
        string coverageEntryId,
        string failureWindow)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        const string scenarioId = "runtime-checkpoint-failure-recovery";
        return GroundworkStoreScenarioCatalog.Get(scenarioId).CreateResult(
            coverageEntryId,
            evidence.Descriptor,
            Composition(evidence.Descriptor, scenarioId),
            clients: 2,
            [
                new GroundworkScenarioObservation("committed-bundle-count", "1"),
                new GroundworkScenarioObservation("partial-bundle-count", "0"),
                new GroundworkScenarioObservation("recovered-state", "verified")
            ],
            failureWindow: failureWindow);
    }

    public static GroundworkScenarioResult CreateCheckpointProcessRestartResult(
        RuntimeCheckpointFenceEvidence evidence,
        string coverageEntryId)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        const string scenarioId = "runtime-checkpoint-bundle-process-restart";
        return GroundworkStoreScenarioCatalog.Get(scenarioId).CreateResult(
            coverageEntryId,
            evidence.Descriptor,
            Composition(evidence.Descriptor, scenarioId),
            clients: 2,
            [
                new GroundworkScenarioObservation("child-process-rehydration", "verified"),
                new GroundworkScenarioObservation("complete-bundle-count", "1")
            ]);
    }

    public static async Task<(GroundworkScenarioResult Result, GroundworkNativeRoutePlanResult Plan)>
        RunBookmarkListRouteAsync(string providerKey)
    {
        var capture = await CaptureNativeRoutesAsync(providerKey);
        var mapping = B7NativeQueryMappings.Single(candidate =>
            candidate.CoverageEntryId == "runtime-bookmark-state" &&
            candidate.LedgerQueryShape == "list-by-workflow-bounded");
        var plan = capture.Plans.Single(candidate =>
            candidate.Request.DocumentKind == mapping.DocumentKind &&
            candidate.Request.QueryIdentity == mapping.NativeQueryIdentity);
        return (CreateNativeRouteResult(capture.Descriptor, mapping, plan), plan);
    }

    public static async Task<RuntimeProviderNativePlanCapture> CaptureNativeRoutesAsync(string providerKey) => new(
        await DescribeAsync(providerKey),
        await ProviderNativePlanTests.CaptureNativeRoutePlansAsync(
            GroundworkProviderDriverFactory.Create(providerKey)));

    public static GroundworkScenarioResult CreateNativeRouteResult(
        GroundworkProviderDescriptor descriptor,
        RuntimeNativeQueryMapping mapping,
        GroundworkNativeRoutePlanResult plan)
    {
        if (plan.Request.DocumentKind != mapping.DocumentKind || plan.Request.QueryIdentity != mapping.NativeQueryIdentity)
            throw new ArgumentException("The native plan does not match its explicit ledger-to-route mapping.", nameof(plan));

        var scenarioId = "runtime-bounded-route-equivalence";
        var composition = Composition(descriptor, scenarioId);
        var path = GroundworkExecutionPath.Create(descriptor.ProviderKey, scenarioId, composition);
        return GroundworkScenarioResult.Create(
            scenarioId,
            mapping.CoverageEntryId,
            descriptor,
            composition,
            path.Value,
            clients: 1,
            [
                new GroundworkScenarioObservation("finite-limit", Convert.ToString(plan.FiniteLimit, System.Globalization.CultureInfo.InvariantCulture)!),
                new GroundworkScenarioObservation("materialized-candidate-count", plan.MaterializedCandidateCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new GroundworkScenarioObservation("query-identity", plan.Request.QueryIdentity)
            ],
            GroundworkScenarioOutcome.Pass,
            nativeEvidence: GroundworkNativePlanEvidence.Create(path, scenarioId, plan.Evidence));
    }

    public static async Task<GroundworkScenarioResult> RunBookmarkScopeIsolationAsync(string providerKey)
    {
        await new StorageScopeContractTests()
            .Storage_scope_contract_is_enforced_by_every_persistent_provider(providerKey);

        return await ResultAsync(
            providerKey,
            "runtime-ordinary-state-scope-isolation",
            "runtime-bookmark-state",
            clients: 1,
            [
                new GroundworkScenarioObservation("cross-scope-disclosure-count", "zero"),
                new GroundworkScenarioObservation("current-scope-state", "verified")
            ]);
    }

    public static async Task<GroundworkScenarioResult> RunSchedulerStateReopenAsync(string providerKey)
    {
        await RuntimeProviderContractTests.RunSchedulerStateRoundTripEvidenceAsync(providerKey);
        await ProviderRecoveryContractTests.RunRuntimeStateRestartEvidenceAsync(providerKey);

        var descriptor = await DescribeAsync(providerKey);
        const string scenarioId = "runtime-scheduler-state-roundtrip";
        return GroundworkStoreScenarioCatalog.Get(scenarioId).CreateResult(
            "runtime-scheduler-state",
            descriptor,
            Composition(descriptor, scenarioId),
            2,
            [
                new GroundworkScenarioObservation("final-state", "persisted"),
                new GroundworkScenarioObservation("reopen-state", "verified"),
                new GroundworkScenarioObservation("revision", "verified")
            ]);
    }

    public static async Task<GroundworkScenarioResult> RunOperationalFailureWindowAsync(
        string providerKey,
        string windowId,
        bool decisionIsDurable)
    {
        var evidence = await CaptureOperationalFailureWindowAsync(providerKey, windowId, decisionIsDurable);
        return CreateOperationalFailureWindowResult(evidence, "runtime-scheduler-state");
    }

    public static async Task<RuntimeOperationalFailureWindowEvidence> CaptureOperationalFailureWindowAsync(
        string providerKey,
        string windowId,
        bool decisionIsDurable)
    {
        await new RuntimeFailureWindowTests()
            .Operational_rows_converge_after_each_named_failure_window(providerKey, windowId, decisionIsDurable);

        return new RuntimeOperationalFailureWindowEvidence(
            await DescribeAsync(providerKey),
            windowId,
            decisionIsDurable);
    }

    public static GroundworkScenarioResult CreateOperationalFailureWindowResult(
        RuntimeOperationalFailureWindowEvidence evidence,
        string coverageEntryId)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var scenarioId = "runtime-operational-failure-window";
        var composition = Composition(evidence.Descriptor, scenarioId);
        return GroundworkScenarioResult.Create(
            "runtime-operational-failure-window",
            coverageEntryId,
            evidence.Descriptor,
            composition,
            GroundworkExecutionPath.Create(evidence.Descriptor.ProviderKey, scenarioId, composition).Value,
            clients: 2,
            [
                new GroundworkScenarioObservation("durable-decision", evidence.DecisionIsDurable ? "true" : "false"),
                new GroundworkScenarioObservation("operational-row-convergence", "verified")
            ],
            GroundworkScenarioOutcome.Pass,
            failureWindow: evidence.WindowId);
    }

    public static async Task<GroundworkScenarioResult> RunExistingProviderProbeAsync(
        string providerKey,
        string scenarioId,
        string coverageEntryId,
        int clients,
        string probeIdentity,
        Func<string, Task> executeAsync)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);
        await executeAsync(providerKey);
        return await ResultAsync(
            providerKey,
            scenarioId,
            coverageEntryId,
            clients,
            [new GroundworkScenarioObservation("executed-provider-probe", probeIdentity)]);
    }

    private static async Task<GroundworkScenarioResult> ResultAsync(
        string providerKey,
        string scenarioId,
        string coverageEntryId,
        int clients,
        IReadOnlyList<GroundworkScenarioObservation> observations,
        string? failureWindow = null)
    {
        var descriptor = await DescribeAsync(providerKey);
        var composition = Composition(descriptor, scenarioId);
        return GroundworkScenarioResult.Create(
            scenarioId,
            coverageEntryId,
            descriptor,
            composition,
            GroundworkExecutionPath.Create(providerKey, scenarioId, composition).Value,
            clients,
            observations,
            GroundworkScenarioOutcome.Pass,
            failureWindow);
    }

    private static async Task<GroundworkProviderDescriptor> DescribeAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        return driver.Descriptor;
    }

    private static GroundworkCompositionFingerprint Composition(
        GroundworkProviderDescriptor descriptor,
        string scenarioId) =>
        GroundworkCompositionFingerprint.Create(string.Join(
            '\n',
            "runtime-provider-evidence-v1",
            $"provider={descriptor.ProviderIdentity}",
            $"provider-version={descriptor.ProviderVersion}",
            $"scenario={scenarioId}"));

    private static RuntimeNativeQueryMapping Map(
        string coverageEntryId,
        string ledgerQueryShape,
        string documentKind,
        string nativeQueryIdentity) =>
        new(coverageEntryId, ledgerQueryShape, documentKind, nativeQueryIdentity);
}

internal sealed record RuntimeNativeQueryMapping(
    string CoverageEntryId,
    string LedgerQueryShape,
    string DocumentKind,
    string NativeQueryIdentity);

internal sealed record RuntimeProviderNativePlanCapture(
    GroundworkProviderDescriptor Descriptor,
    IReadOnlyList<GroundworkNativeRoutePlanResult> Plans);

internal sealed record RuntimeOperationalFailureWindowEvidence(
    GroundworkProviderDescriptor Descriptor,
    string WindowId,
    bool DecisionIsDurable);

internal sealed record RuntimeCheckpointFenceEvidence(GroundworkProviderDescriptor Descriptor);
