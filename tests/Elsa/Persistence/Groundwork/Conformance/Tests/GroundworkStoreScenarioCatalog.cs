using Elsa.Persistence.Groundwork.Testing;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

internal enum GroundworkStoreScenarioFamily
{
    Runtime,
    Identity,
    Secrets,
    DistributedRuntime
}

internal enum GroundworkStoreScenarioEvidenceSource
{
    ProviderMatrix,
    LinkedExternalOwner
}

[Flags]
internal enum GroundworkStoreScenarioEvidence
{
    None = 0,
    PersistentStorage = 1 << 0,
    IndependentClients = 1 << 1,
    DisposeAndReopen = 1 << 2,
    ProcessRestart = 1 << 3,
    Cancellation = 1 << 4,
    FailureInjection = 1 << 5,
    NativeBoundedExecution = 1 << 6,
    CapabilityDerivation = 1 << 7,
    MultiDocumentAtomicity = 1 << 8,
    TemporaryEfOracle = 1 << 9
}

/// <summary>
/// One provider-independent public-contract scenario in the #645 release gate.
/// Provider mechanics are deliberately absent. The definition closes the coverage rows,
/// observations, lifecycle evidence, and failure-window vocabulary that may contribute to
/// the scenario's provider-equivalence digest.
/// </summary>
internal sealed class GroundworkStoreScenarioDefinition
{
    public GroundworkStoreScenarioDefinition(
        string scenarioId,
        GroundworkStoreScenarioFamily family,
        IEnumerable<string> coverageEntryIds,
        IEnumerable<string> observationNames,
        GroundworkStoreScenarioEvidence requiredEvidence,
        GroundworkStoreScenarioEvidenceSource evidenceSource = GroundworkStoreScenarioEvidenceSource.ProviderMatrix,
        IEnumerable<string>? failureWindows = null)
    {
        ScenarioId = RequireSlug(scenarioId, nameof(scenarioId));
        Family = family;
        CoverageEntryIds = SnapshotSlugs(coverageEntryIds, nameof(coverageEntryIds));
        ObservationNames = SnapshotSlugs(observationNames, nameof(observationNames));
        RequiredEvidence = requiredEvidence;
        EvidenceSource = evidenceSource;
        FailureWindows = SnapshotSlugs(failureWindows ?? [], nameof(failureWindows), allowEmpty: true);

        if (EvidenceSource is GroundworkStoreScenarioEvidenceSource.ProviderMatrix &&
            !RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.PersistentStorage))
            throw new ArgumentException("Provider-matrix scenarios must require persistent storage.", nameof(requiredEvidence));
        if (EvidenceSource is GroundworkStoreScenarioEvidenceSource.LinkedExternalOwner &&
            RequiredEvidence is not GroundworkStoreScenarioEvidence.None)
            throw new ArgumentException("Linked-owner scenarios cannot claim locally executed evidence.", nameof(requiredEvidence));
        if (RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.FailureInjection) != (FailureWindows.Count > 0))
            throw new ArgumentException(
                "Failure-injection scenarios must declare failure windows, and other scenarios must not.",
                nameof(failureWindows));
    }

    public string ScenarioId { get; }
    public GroundworkStoreScenarioFamily Family { get; }
    public IReadOnlyList<string> CoverageEntryIds { get; }
    public IReadOnlyList<string> ObservationNames { get; }
    public GroundworkStoreScenarioEvidence RequiredEvidence { get; }
    public GroundworkStoreScenarioEvidenceSource EvidenceSource { get; }
    public IReadOnlyList<string> FailureWindows { get; }

    /// <summary>
    /// Creates the canonical provider result after enforcing this scenario's closed observation contract.
    /// <see cref="GroundworkScenarioResult"/> owns digest canonicalization: scenario, coverage row,
    /// classified outcome, failure window, and ordinally sorted length-prefixed observations contribute;
    /// provider metadata, client count, composition fingerprint, and native evidence do not.
    /// </summary>
    public GroundworkScenarioResult CreateResult(
        string coverageEntryId,
        GroundworkProviderDescriptor provider,
        GroundworkCompositionFingerprint compositionFingerprint,
        int clients,
        IEnumerable<GroundworkScenarioObservation> observations,
        GroundworkScenarioOutcome outcome = GroundworkScenarioOutcome.Pass,
        string? failureWindow = null,
        GroundworkNativePlanEvidence? nativeEvidence = null)
    {
        if (EvidenceSource is not GroundworkStoreScenarioEvidenceSource.ProviderMatrix)
            throw new InvalidOperationException(
                $"Scenario '{ScenarioId}' consumes linked owner evidence and cannot create local provider evidence.");
        if (!CoverageEntryIds.Contains(coverageEntryId, StringComparer.Ordinal))
            throw new ArgumentException(
                $"Scenario '{ScenarioId}' does not cover ledger entry '{coverageEntryId}'.",
                nameof(coverageEntryId));

        ArgumentNullException.ThrowIfNull(provider);
        EnsureTopology(provider);
        if (RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.IndependentClients) && clients < 2)
            throw new ArgumentOutOfRangeException(nameof(clients), "The scenario requires at least two independent clients.");

        var observationSnapshot = observations?.ToArray() ?? throw new ArgumentNullException(nameof(observations));
        RequireExactValues("observation", ObservationNames, observationSnapshot.Select(x => x.Name));

        if (FailureWindows.Count > 0)
        {
            if (failureWindow is null || !FailureWindows.Contains(failureWindow, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"Scenario '{ScenarioId}' requires one of its declared failure windows.",
                    nameof(failureWindow));
        }
        else if (failureWindow is not null)
            throw new ArgumentException($"Scenario '{ScenarioId}' does not declare a failure window.", nameof(failureWindow));

        if (RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.NativeBoundedExecution) != (nativeEvidence is not null))
            throw new ArgumentException(
                $"Scenario '{ScenarioId}' requires native evidence exactly when its catalog definition declares it.",
                nameof(nativeEvidence));

        var executionPath = GroundworkExecutionPath.Create(provider.ProviderKey, ScenarioId, compositionFingerprint);
        return GroundworkScenarioResult.Create(
            ScenarioId,
            coverageEntryId,
            provider,
            compositionFingerprint,
            executionPath.Value,
            clients,
            observationSnapshot,
            outcome,
            failureWindow,
            nativeEvidence);
    }

    /// <summary>
    /// Enforces the release-gate rule that all mandatory providers produce one equivalent
    /// provider-independent result digest for this scenario and coverage row.
    /// </summary>
    public void RequireEquivalentProviderResults(
        string coverageEntryId,
        IEnumerable<GroundworkScenarioResult> results)
    {
        if (EvidenceSource is not GroundworkStoreScenarioEvidenceSource.ProviderMatrix)
            throw new InvalidOperationException($"Scenario '{ScenarioId}' is not a provider-matrix scenario.");
        if (!CoverageEntryIds.Contains(coverageEntryId, StringComparer.Ordinal))
            throw new ArgumentException(
                $"Scenario '{ScenarioId}' does not cover ledger entry '{coverageEntryId}'.",
                nameof(coverageEntryId));

        var snapshot = results?.ToArray() ?? throw new ArgumentNullException(nameof(results));
        RequireExactValues(
            "provider",
            GroundworkStoreScenarioCatalog.MandatoryProviderKeys,
            snapshot.Select(result => result.Provider.ProviderKey));

        foreach (var result in snapshot)
        {
            if (!string.Equals(result.ScenarioId, ScenarioId, StringComparison.Ordinal) ||
                !string.Equals(result.CoverageEntryId, coverageEntryId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Provider result '{result.Provider.ProviderKey}' does not belong to scenario '{ScenarioId}' " +
                    $"and ledger entry '{coverageEntryId}'.");
            if (result.Outcome is not GroundworkScenarioOutcome.Pass)
                throw new InvalidOperationException(
                    $"Mandatory provider '{result.Provider.ProviderKey}' did not pass scenario '{ScenarioId}'.");
            RequireExactValues("observation", ObservationNames, result.Observations.Select(x => x.Name));
            if (RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.NativeBoundedExecution) !=
                (result.NativeEvidence is not null))
                throw new InvalidOperationException(
                    $"Provider result '{result.Provider.ProviderKey}' has the wrong native-evidence classification.");
        }

        var digests = snapshot.Select(result => result.ResultDigest).Distinct(StringComparer.Ordinal).ToArray();
        if (digests.Length != 1)
            throw new InvalidOperationException(
                $"Scenario '{ScenarioId}' produced {digests.Length} provider-independent result digests.");
    }

    private void EnsureTopology(GroundworkProviderDescriptor provider)
    {
        var required = GroundworkTopologyCapabilities.PersistentStorage;
        if (RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.IndependentClients))
            required |= GroundworkTopologyCapabilities.IndependentClients;
        if (RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.ProcessRestart))
            required |= GroundworkTopologyCapabilities.ExternalProcessRestart;
        if (RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.MultiDocumentAtomicity))
            required |= GroundworkTopologyCapabilities.MultiDocumentTransactions;
        provider.Topology.EnsureSupports(required);
    }

    private static IReadOnlyList<string> SnapshotSlugs(
        IEnumerable<string> values,
        string parameterName,
        bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(values);
        var snapshot = values.Select(value => RequireSlug(value, parameterName))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!allowEmpty && snapshot.Length == 0)
            throw new ArgumentException("At least one catalog value is required.", parameterName);
        if (snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("Catalog values must be unique.", parameterName);
        return Array.AsReadOnly(snapshot);
    }

    private static string RequireSlug(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value[0] is < 'a' or > 'z' ||
            value.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-') ||
            value.Contains("--", StringComparison.Ordinal) ||
            value[^1] == '-')
            throw new ArgumentException("A lowercase kebab-case catalog value is required.", parameterName);
        return value;
    }

    private static void RequireExactValues(
        string subject,
        IEnumerable<string> expectedValues,
        IEnumerable<string> observedValues)
    {
        var expected = expectedValues.Order(StringComparer.Ordinal).ToArray();
        var observed = observedValues.Order(StringComparer.Ordinal).ToArray();
        if (expected.SequenceEqual(observed, StringComparer.Ordinal))
            return;

        var missing = expected.Except(observed, StringComparer.Ordinal);
        var unexpected = observed.Except(expected, StringComparer.Ordinal);
        throw new InvalidOperationException(
            $"Scenario {subject} coverage mismatch: missing=[{string.Join(", ", missing)}] " +
            $"unexpected=[{string.Join(", ", unexpected)}].");
    }
}

/// <summary>
/// Canonical provider-independent scenario denominator for the #645 release gate.
/// Later provider suites execute this catalog; they may add mechanics, but cannot add
/// provider-specific expected domain outcomes or silently omit a catalog identity.
/// </summary>
internal static class GroundworkStoreScenarioCatalog
{
    private const GroundworkStoreScenarioEvidence Persistent =
        GroundworkStoreScenarioEvidence.PersistentStorage;
    private const GroundworkStoreScenarioEvidence Race =
        Persistent | GroundworkStoreScenarioEvidence.IndependentClients;
    private const GroundworkStoreScenarioEvidence Reopen =
        Persistent | GroundworkStoreScenarioEvidence.DisposeAndReopen;
    private const GroundworkStoreScenarioEvidence Restart =
        Reopen | GroundworkStoreScenarioEvidence.ProcessRestart;
    private const GroundworkStoreScenarioEvidence Bounded =
        Persistent |
        GroundworkStoreScenarioEvidence.NativeBoundedExecution |
        GroundworkStoreScenarioEvidence.CapabilityDerivation;
    private const GroundworkStoreScenarioEvidence Failure =
        Race |
        GroundworkStoreScenarioEvidence.DisposeAndReopen |
        GroundworkStoreScenarioEvidence.ProcessRestart |
        GroundworkStoreScenarioEvidence.FailureInjection |
        GroundworkStoreScenarioEvidence.MultiDocumentAtomicity;
    private const GroundworkStoreScenarioEvidence Cancellation =
        Persistent |
        GroundworkStoreScenarioEvidence.Cancellation |
        GroundworkStoreScenarioEvidence.DisposeAndReopen;

    private static readonly string[] FailureWindows =
    [
        "after-durable-decision-before-caller-acknowledgement",
        "before-provider-decision",
        "during-provider-decision",
        "during-recovery"
    ];

    private static readonly IReadOnlyList<string> RuntimeEntries =
        Array.AsReadOnly(new[]
        {
            "runtime-activity-execution-inspection",
            "runtime-activity-execution-state",
            "runtime-bookmark-state",
            "runtime-checkpoint-commit",
            "runtime-diagnostics-settings",
            "runtime-durable-timer",
            "runtime-durable-value-state",
            "runtime-executable-source-reference",
            "runtime-execution-liveness",
            "runtime-incident-state",
            "runtime-post-commit-outbox",
            "runtime-publication-projection-state",
            "runtime-recurring-trigger-schedule",
            "runtime-scheduler-poison",
            "runtime-scheduler-state",
            "runtime-scheduler-work-queue",
            "runtime-trigger-binding",
            "runtime-workflow-executable",
            "runtime-workflow-alteration",
            "runtime-workflow-execution-state",
            "runtime-workflow-hold-state"
        });

    private static readonly IReadOnlyList<string> IdentityEntries =
        Array.AsReadOnly(new[]
        {
            "iam-application",
            "iam-claim-mapping",
            "iam-credential",
            "iam-external-identity",
            "iam-provider-configuration-global",
            "iam-provider-configuration-tenant",
            "iam-role",
            "iam-tenant-membership",
            "iam-user"
        });

    private static readonly IReadOnlyList<string> SecretsEntries =
        Array.AsReadOnly(new[] { "secrets-repository" });

    private static readonly IReadOnlyList<string> DistributedEntries =
        Array.AsReadOnly(new[]
        {
            "distributed-command-transport",
            "distributed-execution-placement"
        });

    private static readonly IReadOnlyList<string> AllEntries =
        Array.AsReadOnly(
            RuntimeEntries
                .Concat(IdentityEntries)
                .Concat(SecretsEntries)
                .Concat(DistributedEntries)
                .Order(StringComparer.Ordinal)
                .ToArray());

    private static readonly IReadOnlyList<GroundworkStoreScenarioDefinition> ScenarioDefinitions =
        Array.AsReadOnly(
        new[]
        {
            Scenario(
                "runtime-ordinary-state-revision-and-reopen",
                GroundworkStoreScenarioFamily.Runtime,
                [
                    "runtime-activity-execution-inspection",
                    "runtime-activity-execution-state",
                    "runtime-bookmark-state",
                    "runtime-durable-value-state",
                    "runtime-executable-source-reference",
                    "runtime-trigger-binding",
                    "runtime-workflow-executable",
                    "runtime-workflow-execution-state"
                ],
                ["final-state", "reopen-state", "revision"],
                Restart | GroundworkStoreScenarioEvidence.TemporaryEfOracle),
            Scenario(
                "runtime-ordinary-state-scope-isolation",
                GroundworkStoreScenarioFamily.Runtime,
                [
                    "runtime-activity-execution-inspection",
                    "runtime-activity-execution-state",
                    "runtime-bookmark-state",
                    "runtime-durable-value-state",
                    "runtime-executable-source-reference",
                    "runtime-trigger-binding",
                    "runtime-workflow-executable",
                    "runtime-workflow-execution-state"
                ],
                ["cross-scope-disclosure-count", "current-scope-state"],
                Persistent),
            Scenario(
                "runtime-bounded-route-equivalence",
                GroundworkStoreScenarioFamily.Runtime,
                [
                    "runtime-activity-execution-inspection",
                    "runtime-activity-execution-state",
                    "runtime-bookmark-state",
                    "runtime-durable-value-state",
                    "runtime-executable-source-reference",
                    "runtime-trigger-binding",
                    "runtime-workflow-executable",
                    "runtime-workflow-alteration",
                    "runtime-workflow-execution-state"
                ],
                ["native-execution", "ordered-identities", "returned-count"],
                Bounded),
            Scenario(
                "runtime-execution-ownership-fencing",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-checkpoint-commit", "runtime-execution-liveness"],
                ["stale-commit-count", "token-order", "winner-count"],
                Race | GroundworkStoreScenarioEvidence.CapabilityDerivation),
            Scenario(
                "runtime-checkpoint-idempotency",
                GroundworkStoreScenarioFamily.Runtime,
                [
                    "runtime-activity-execution-inspection",
                    "runtime-activity-execution-state",
                    "runtime-bookmark-state",
                    "runtime-checkpoint-commit",
                    "runtime-durable-value-state",
                    "runtime-post-commit-outbox",
                    "runtime-workflow-execution-state"
                ],
                ["conflicting-replay", "durable-outcome-count", "equivalent-replay"],
                Race),
            Scenario(
                "runtime-checkpoint-failure-recovery",
                GroundworkStoreScenarioFamily.Runtime,
                [
                    "runtime-activity-execution-inspection",
                    "runtime-activity-execution-state",
                    "runtime-bookmark-state",
                    "runtime-checkpoint-commit",
                    "runtime-durable-value-state",
                    "runtime-post-commit-outbox",
                    "runtime-workflow-execution-state"
                ],
                ["committed-bundle-count", "partial-bundle-count", "recovered-state"],
                Failure,
                failureWindows: FailureWindows),
            Scenario(
                "runtime-checkpoint-bundle-process-restart",
                GroundworkStoreScenarioFamily.Runtime,
                [
                    "runtime-activity-execution-inspection",
                    "runtime-activity-execution-state",
                    "runtime-bookmark-state",
                    "runtime-checkpoint-commit",
                    "runtime-durable-value-state",
                    "runtime-execution-liveness",
                    "runtime-incident-state",
                    "runtime-post-commit-outbox",
                    "runtime-workflow-execution-state"
                ],
                ["child-process-rehydration", "complete-bundle-count"],
                Restart | Race | GroundworkStoreScenarioEvidence.MultiDocumentAtomicity),
            Scenario(
                "runtime-execution-liveness-takeover",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-execution-liveness"],
                ["current-owner", "stale-owner-mutation-count", "token-order"],
                Race),
            Scenario(
                "runtime-timer-create-once-and-due-selection",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-durable-timer"],
                ["existing-winner-count", "native-execution", "ordered-identities", "winner-count"],
                Race | GroundworkStoreScenarioEvidence.NativeBoundedExecution |
                GroundworkStoreScenarioEvidence.CapabilityDerivation),
            Scenario(
                "runtime-incident-create-once",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-incident-state"],
                ["existing-winner-count", "final-revision", "winner-count"],
                Race),
            Scenario(
                "runtime-recurring-schedule-conditional-advance",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-recurring-trigger-schedule"],
                ["due-order", "native-execution", "stale-advance-count", "winner-count"],
                Race | GroundworkStoreScenarioEvidence.NativeBoundedExecution |
                GroundworkStoreScenarioEvidence.CapabilityDerivation),
            Scenario(
                "runtime-outbox-claim-retry-and-stale-acknowledgement",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-post-commit-outbox"],
                ["attempt-count", "current-owner", "stale-acknowledgement-count"],
                Race | GroundworkStoreScenarioEvidence.CapabilityDerivation),
            Scenario(
                "runtime-scheduler-state-roundtrip",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-scheduler-state"],
                ["final-state", "reopen-state", "revision"],
                Reopen),
            Scenario(
                "runtime-hold-conditional-transition",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-workflow-hold-state"],
                ["final-state", "stale-transition-count", "winner-count"],
                Race),
            Scenario(
                "runtime-scheduler-poison-restart",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-scheduler-poison"],
                ["failed-item-relationship", "retry-state", "reopen-state"],
                Restart),
            Scenario(
                "runtime-scheduler-queue-claim-and-stale-acknowledgement",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-scheduler-work-queue"],
                ["current-owner", "native-execution", "ordered-identities", "stale-acknowledgement-count"],
                Race | GroundworkStoreScenarioEvidence.NativeBoundedExecution |
                GroundworkStoreScenarioEvidence.CapabilityDerivation),
            Scenario(
                "runtime-trigger-stimulus-lookup",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-trigger-binding"],
                ["native-execution", "ordered-identities", "returned-count"],
                Bounded),
            Scenario(
                "runtime-publication-projection-create-once",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-publication-projection-state"],
                ["existing-winner-count", "final-revision", "winner-count"],
                Race),
            Scenario(
                "runtime-cancellation-dispose-and-reuse",
                GroundworkStoreScenarioFamily.Runtime,
                RuntimeEntries.Except(["runtime-diagnostics-settings"], StringComparer.Ordinal),
                ["cancelled-operation-count", "reused-client-state", "scope-leak-count"],
                Cancellation),
            Scenario(
                "runtime-diagnostics-linked-authority",
                GroundworkStoreScenarioFamily.Runtime,
                ["runtime-diagnostics-settings"],
                ["authority-evidence"],
                GroundworkStoreScenarioEvidence.None,
                GroundworkStoreScenarioEvidenceSource.LinkedExternalOwner),

            Scenario(
                "identity-authority-adaptation",
                GroundworkStoreScenarioFamily.Identity,
                ["iam-external-identity", "iam-role", "iam-user"],
                ["authoritative-document-count", "parallel-authority-count", "relationship-state"],
                Restart | GroundworkStoreScenarioEvidence.TemporaryEfOracle),
            Scenario(
                "identity-tenant-local-normalized-uniqueness",
                GroundworkStoreScenarioFamily.Identity,
                ["iam-role", "iam-user"],
                ["cross-tenant-winner-count", "same-tenant-loser-count", "same-tenant-winner-count"],
                Race),
            Scenario(
                "identity-authority-relationship-atomicity",
                GroundworkStoreScenarioFamily.Identity,
                ["iam-external-identity", "iam-role", "iam-user"],
                ["orphan-count", "owner-count", "stale-mutation-count"],
                Failure,
                failureWindows: FailureWindows),
            Scenario(
                "iam-ordinary-store-revision-conflicts",
                GroundworkStoreScenarioFamily.Identity,
                [
                    "iam-application",
                    "iam-claim-mapping",
                    "iam-credential",
                    "iam-tenant-membership"
                ],
                ["current-revision", "stale-delete-count", "stale-update-count"],
                Race),
            Scenario(
                "iam-provider-configuration-scope-and-privilege",
                GroundworkStoreScenarioFamily.Identity,
                ["iam-provider-configuration-global", "iam-provider-configuration-tenant"],
                ["ordinary-write-rejection-count", "scope-disclosure-count", "write-state"],
                Persistent),
            Scenario(
                "iam-bounded-query-equivalence",
                GroundworkStoreScenarioFamily.Identity,
                IdentityEntries,
                ["native-execution", "ordered-identities", "returned-count"],
                Bounded),
            Scenario(
                "iam-dispose-reopen-and-process-restart",
                GroundworkStoreScenarioFamily.Identity,
                IdentityEntries,
                ["reopen-state", "revision-state", "uniqueness-state"],
                Restart),
            Scenario(
                "iam-cancellation-dispose-and-reuse",
                GroundworkStoreScenarioFamily.Identity,
                IdentityEntries,
                ["cancelled-operation-count", "reused-client-state", "scope-leak-count"],
                Cancellation),

            Scenario(
                "secret-concurrent-create-only",
                GroundworkStoreScenarioFamily.Secrets,
                ["secrets-repository"],
                ["loser-count", "winner-count", "winner-value-state"],
                Race),
            Scenario(
                "secret-revision-aware-mutation",
                GroundworkStoreScenarioFamily.Secrets,
                ["secrets-repository"],
                ["current-revision", "stale-delete-count", "stale-update-count"],
                Race),
            Scenario(
                "secret-bounded-list-equivalence",
                GroundworkStoreScenarioFamily.Secrets,
                ["secrets-repository"],
                ["native-execution", "ordered-identities", "returned-count"],
                Bounded),
            Scenario(
                "secret-dispose-reopen-and-process-restart",
                GroundworkStoreScenarioFamily.Secrets,
                ["secrets-repository"],
                ["reopen-state", "revision-state", "version-state"],
                Restart),
            Scenario(
                "secret-cancellation-dispose-and-reuse",
                GroundworkStoreScenarioFamily.Secrets,
                ["secrets-repository"],
                ["cancelled-operation-count", "reused-client-state", "scope-leak-count"],
                Cancellation),

            Scenario(
                "distributed-placement-claim-renew-and-takeover",
                GroundworkStoreScenarioFamily.DistributedRuntime,
                ["distributed-execution-placement"],
                ["current-owner", "token-order", "winner-count"],
                Race | GroundworkStoreScenarioEvidence.CapabilityDerivation),
            Scenario(
                "distributed-placement-stale-release-and-execution-fence",
                GroundworkStoreScenarioFamily.DistributedRuntime,
                ["distributed-execution-placement", "runtime-checkpoint-commit"],
                ["current-owner", "stale-commit-count", "stale-release-count"],
                Race | GroundworkStoreScenarioEvidence.CapabilityDerivation),
            Scenario(
                "distributed-command-send-sequence",
                GroundworkStoreScenarioFamily.DistributedRuntime,
                ["distributed-command-transport"],
                ["duplicate-identity-count", "ordered-sequences", "persisted-command-count"],
                Race),
            Scenario(
                "distributed-command-lease-retry-and-stale-acknowledgement",
                GroundworkStoreScenarioFamily.DistributedRuntime,
                ["distributed-command-transport"],
                ["current-owner", "redelivery-count", "stale-acknowledgement-count"],
                Race | GroundworkStoreScenarioEvidence.CapabilityDerivation),
            Scenario(
                "distributed-command-failure-and-restart",
                GroundworkStoreScenarioFamily.DistributedRuntime,
                ["distributed-command-transport"],
                ["acknowledged-count", "redelivery-count", "reopen-state"],
                Failure,
                failureWindows: FailureWindows),
            Scenario(
                "distributed-bounded-route-equivalence",
                GroundworkStoreScenarioFamily.DistributedRuntime,
                DistributedEntries,
                ["native-execution", "ordered-identities", "returned-count"],
                Bounded),
            Scenario(
                "distributed-cross-partition-isolation",
                GroundworkStoreScenarioFamily.DistributedRuntime,
                DistributedEntries,
                ["cross-partition-mutation-count", "cross-partition-result-count", "current-partition-state"],
                Persistent),
            Scenario(
                "distributed-cancellation-dispose-and-reuse",
                GroundworkStoreScenarioFamily.DistributedRuntime,
                DistributedEntries,
                ["cancelled-operation-count", "reused-client-state", "scope-leak-count"],
                Cancellation)
        }.OrderBy(definition => definition.ScenarioId, StringComparer.Ordinal).ToArray());

    static GroundworkStoreScenarioCatalog()
    {
        var duplicateScenarioIds = All.GroupBy(x => x.ScenarioId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateScenarioIds.Length > 0)
            throw new InvalidOperationException(
                $"Duplicate scenario identities: {string.Join(", ", duplicateScenarioIds)}.");

        var coveredEntries = All.SelectMany(x => x.CoverageEntryIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!CoverageEntryIds.SequenceEqual(coveredEntries, StringComparer.Ordinal))
            throw new InvalidOperationException("The scenario catalog does not cover the exact ALL33 ledger denominator.");

        foreach (var definition in All)
        {
            var expectedFamilyEntries = definition.Family switch
            {
                GroundworkStoreScenarioFamily.Runtime => RuntimeCoverageEntryIds,
                GroundworkStoreScenarioFamily.Identity => IdentityCoverageEntryIds,
                GroundworkStoreScenarioFamily.Secrets => SecretsCoverageEntryIds,
                GroundworkStoreScenarioFamily.DistributedRuntime =>
                    DistributedCoverageEntryIds.Concat(["runtime-checkpoint-commit"]),
                _ => throw new ArgumentOutOfRangeException()
            };
            if (definition.CoverageEntryIds.Any(entry => !expectedFamilyEntries.Contains(entry, StringComparer.Ordinal)))
                throw new InvalidOperationException(
                    $"Scenario '{definition.ScenarioId}' covers an entry outside its family.");
        }
    }

    public static IReadOnlyList<string> MandatoryProviderKeys { get; } =
        Array.AsReadOnly(new[] { "mongodb", "postgresql", "sqlite", "sqlserver" });

    public static IReadOnlyList<string> RuntimeCoverageEntryIds => RuntimeEntries;

    public static IReadOnlyList<string> IdentityCoverageEntryIds => IdentityEntries;

    public static IReadOnlyList<string> SecretsCoverageEntryIds => SecretsEntries;

    public static IReadOnlyList<string> DistributedCoverageEntryIds => DistributedEntries;

    public static IReadOnlyList<string> CoverageEntryIds => AllEntries;

    public static IReadOnlyList<GroundworkStoreScenarioDefinition> All => ScenarioDefinitions;

    public static IReadOnlyList<GroundworkStoreScenarioDefinition> ForFamily(
        GroundworkStoreScenarioFamily family,
        bool includeLinkedExternalOwner = false) =>
        Array.AsReadOnly(
            All.Where(definition =>
                    definition.Family == family &&
                    (includeLinkedExternalOwner ||
                     definition.EvidenceSource is GroundworkStoreScenarioEvidenceSource.ProviderMatrix))
                .ToArray());

    public static IReadOnlyList<GroundworkStoreScenarioDefinition> Requiring(
        GroundworkStoreScenarioEvidence evidence) =>
        Array.AsReadOnly(
            All.Where(definition =>
                    definition.EvidenceSource is GroundworkStoreScenarioEvidenceSource.ProviderMatrix &&
                    definition.RequiredEvidence.HasFlag(evidence))
                .ToArray());

    public static GroundworkStoreScenarioDefinition Get(string scenarioId) =>
        All.SingleOrDefault(definition => string.Equals(definition.ScenarioId, scenarioId, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown Groundwork store scenario '{scenarioId}'.");

    public static void RequireExactExecutionCoverage(
        GroundworkStoreScenarioFamily family,
        IEnumerable<string> executedScenarioIds)
    {
        ArgumentNullException.ThrowIfNull(executedScenarioIds);
        var expected = ForFamily(family).Select(definition => definition.ScenarioId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var observed = executedScenarioIds.Order(StringComparer.Ordinal).ToArray();
        if (expected.SequenceEqual(observed, StringComparer.Ordinal))
            return;

        var missing = expected.Except(observed, StringComparer.Ordinal);
        var unexpected = observed.Except(expected, StringComparer.Ordinal);
        throw new InvalidOperationException(
            $"{family} provider scenario coverage mismatch: missing=[{string.Join(", ", missing)}] " +
            $"unexpected=[{string.Join(", ", unexpected)}].");
    }

    private static GroundworkStoreScenarioDefinition Scenario(
        string scenarioId,
        GroundworkStoreScenarioFamily family,
        IEnumerable<string> coverageEntryIds,
        IEnumerable<string> observationNames,
        GroundworkStoreScenarioEvidence requiredEvidence,
        GroundworkStoreScenarioEvidenceSource evidenceSource = GroundworkStoreScenarioEvidenceSource.ProviderMatrix,
        IEnumerable<string>? failureWindows = null) =>
        new(
            scenarioId,
            family,
            coverageEntryIds,
            observationNames,
            requiredEvidence,
            evidenceSource,
            failureWindows);
}
