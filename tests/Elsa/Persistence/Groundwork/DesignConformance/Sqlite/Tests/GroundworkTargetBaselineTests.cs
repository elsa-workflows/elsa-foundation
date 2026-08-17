using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

public sealed class GroundworkTargetBaselineTests
{
    private const string EvidenceDirectoryVariable = "ELSA_DESIGN_GROUNDWORK_BASELINE_EVIDENCE_DIR";
    private const string CurrentGroundworkVersion = "0.0.1-preview.131";
    private const string AcceptedEvidenceGroundworkVersion = "0.0.1-preview.81";
    private const string AcceptedTargetFingerprint = "ed6bb6a165a08b34c8ad5a53da40f57f83ce0d2b67867abfd2e618da68473b8c";
    private const string AcceptedPlanFingerprint = "73f2004225f6c3ad58f57f807d2d81fcbd26e4d2603a61528c13ce36617197c4";
    // 2026-08-13: Groundwork moves to 0.0.1-preview.131, the first post-refactor family (provider
    // consolidation, core decomposition of physical-storage resolution and route compilation, declared
    // index key lengths/precision per ADR 0008). Zero-EF program re-entry per the #647 record; the
    // accepted preview.81 floor is untouched, and the pending fingerprints move below only if the
    // refactor changed the computed physical target or provisioning plan.
    // 2026-08-12: every index backing a scale-bearing sweep now declares IncludedAsNull, so no provider
    // omits rows whose keyed columns have no value, and Groundwork moves to 0.0.1-preview.120. Deliberately
    // sparse indexes are unchanged and now state Excluded on both their logical and physical declarations.
    // 2026-08-11: the design-lane search indexes and the trigger-binding traversal indexes now declare
    // IncludedAsNull, so no provider omits rows whose keyed columns have no value. That changes the
    // physical target. Only the PENDING fingerprint moves;
    // AcceptedTargetFingerprint is the ratified floor at preview.81 and is deliberately left alone, so
    // this records a head that has moved rather than re-ratifying anything.
    // 2026-08-17 (spec 151 / T036): BOTH PENDING FINGERPRINTS BELOW ARE KNOWN STALE AND MUST BE RE-MEASURED.
    // This feature changes the composed physical target three ways: the runtime manifest gains the
    // `workflowActivationSlot` storage unit (T026); `workflowTriggerBinding` and `recurringTriggerSchedule`
    // move their index identity and projected field from `by-publication`/`publicationId` and
    // `by-publication-and-*-id` to `by-activation`/`activationId` and `by-activation-and-*-id` (T034); and
    // the publishing manifest drops the orphaned `publishingPublicationSlot` unit (T036). Each of those moves
    // the target fingerprint, and the added/removed units move the provisioning plan fingerprint.
    //
    // The values were NOT updated here because they could not be measured: this workspace cannot run the
    // Groundwork schema CLI (every scenario in this class fails with "Groundwork schema tool emitted invalid
    // JSON (exit 1)"), and the fingerprints are read from that tool's output via GroundworkBaselineTelemetry,
    // not computed in-process. Leaving the stale values is deliberate — the assertion messages print the
    // observed fingerprints, so the first run on a machine with a working schema CLI reports exactly what to
    // paste in. AcceptedTargetFingerprint / AcceptedPlanFingerprint are the ratified preview.81 floor and
    // stay untouched, as they have through every drift since.
    private const string PendingTargetFingerprint = "b0edf8cee1bea256f2c4d7ada93ad5aba56c6654b6ca210506cbd055776cd46c";
    // Moves with PendingTargetFingerprint above, and for the same reason: a new storage unit and a bounded
    // projected column change the provisioning plan. AcceptedPlanFingerprint is untouched.
    private const string PendingPlanFingerprint = "ac21d2897906bd3f7d37b706696983c09d6570c0c00f335325653a37055ea6f4";

    [Fact]
    public async Task Target_profile_matches_the_ratified_twenty_five_green_baseline()
    {
        var telemetry = new GroundworkBaselineTelemetry();
        var workflow = new WorkflowSuite(telemetry);
        var activity = new ActivitySuite(telemetry);
        var atomicity = new AtomicitySuite(telemetry);
        var isolation = new IsolationSuite(telemetry);
        var catalog = new[]
        {
            Green(nameof(workflow.Definition_draft_and_layout_round_trip_across_restart), workflow.Definition_draft_and_layout_round_trip_across_restart),
            Green(nameof(workflow.Promoted_version_preserves_authored_state_layout_identity_and_missing_outcomes), workflow.Promoted_version_preserves_authored_state_layout_identity_and_missing_outcomes),
            Green(nameof(workflow.Draft_update_and_clone_preserve_layout_state_and_source_version), workflow.Draft_update_and_clone_preserve_layout_state_and_source_version),
            Green(nameof(workflow.Submission_creates_a_complete_durable_aggregate), workflow.Submission_creates_a_complete_durable_aggregate),
            Green(nameof(workflow.Submission_rejects_missing_root_activity_without_creating_a_definition), workflow.Submission_rejects_missing_root_activity_without_creating_a_definition),
            Green(nameof(workflow.Discard_removes_the_draft_and_publishes_its_documented_outcome), workflow.Discard_removes_the_draft_and_publishes_its_documented_outcome),
            Green(nameof(workflow.Permanent_delete_removes_the_complete_workflow_aggregate), workflow.Permanent_delete_removes_the_complete_workflow_aggregate),
            Green(nameof(activity.Definition_and_initial_version_round_trip_across_restart), activity.Definition_and_initial_version_round_trip_across_restart),
            Green(nameof(activity.Versions_preserve_semver_identity_and_missing_outcomes), activity.Versions_preserve_semver_identity_and_missing_outcomes),
            Green(nameof(activity.Reconciliation_is_idempotent_after_restart), activity.Reconciliation_is_idempotent_after_restart),
            Green(nameof(atomicity.Partial_staging_failure_leaves_no_visible_partial_aggregate), atomicity.Partial_staging_failure_leaves_no_visible_partial_aggregate),
            Green(nameof(atomicity.Non_success_provider_decision_rolls_back_all_staged_parts), atomicity.Non_success_provider_decision_rolls_back_all_staged_parts),
            Green(nameof(atomicity.Cancellation_rolls_back_and_propagates_cancellation), atomicity.Cancellation_rolls_back_and_propagates_cancellation),
            Green(nameof(atomicity.Lost_acknowledgement_after_durable_decision_reconciles_the_authoritative_result_on_retry), atomicity.Lost_acknowledgement_after_durable_decision_reconciles_the_authoritative_result_on_retry),
            Green(nameof(atomicity.Same_stable_operation_key_and_canonical_fingerprint_replay_the_prior_result), atomicity.Same_stable_operation_key_and_canonical_fingerprint_replay_the_prior_result),
            Green(nameof(atomicity.Stable_operation_key_reuse_with_a_different_fingerprint_conflicts_without_mutation), atomicity.Stable_operation_key_reuse_with_a_different_fingerprint_conflicts_without_mutation),
            Green(nameof(atomicity.Duplicate_delivery_does_not_repeat_the_fixture_post_commit_outcome), atomicity.Duplicate_delivery_does_not_repeat_the_fixture_post_commit_outcome),
            Green(nameof(isolation.Same_point_identities_resolve_only_their_own_scope), isolation.Same_point_identities_resolve_only_their_own_scope),
            Green(nameof(isolation.Foreign_point_reads_are_indistinguishable_from_missing_identities), isolation.Foreign_point_reads_are_indistinguishable_from_missing_identities),
            Green(nameof(isolation.Foreign_scope_point_writes_are_rejected_without_mutating_either_scope), isolation.Foreign_scope_point_writes_are_rejected_without_mutating_either_scope),
            Green(nameof(isolation.Duplicate_workflow_and_activity_identities_are_rejected_within_a_scope), isolation.Duplicate_workflow_and_activity_identities_are_rejected_within_a_scope),
            Green(nameof(isolation.Reusable_activity_draft_rejects_a_stale_expected_revision_without_replacing_state_or_layout), isolation.Reusable_activity_draft_rejects_a_stale_expected_revision_without_replacing_state_or_layout),
            Green(nameof(isolation.Workflow_draft_updates_preserve_the_intentional_last_writer_wins_policy), isolation.Workflow_draft_updates_preserve_the_intentional_last_writer_wins_policy),
            Green(nameof(isolation.Single_scope_point_read_snapshot_survives_restart), isolation.Single_scope_point_read_snapshot_survives_restart),
            Green(nameof(isolation.Cross_scope_same_identity_point_read_snapshots_survive_restart), isolation.Cross_scope_same_identity_point_read_snapshots_survive_restart)
        };

        Assert.Equal(25, catalog.Length);
        Assert.Equal(25, catalog.Count(x => x.Expected == ExpectedOutcome.Green));
        Assert.Equal(0, catalog.Count(x => x.Expected == ExpectedOutcome.Red));

        var observed = new List<ScenarioEvidence>(catalog.Length);
        foreach (var scenario in catalog)
        {
            var exception = await Record.ExceptionAsync(scenario.Execute);
            var actual = exception is null ? ExpectedOutcome.Green : ExpectedOutcome.Red;
            var classification = scenario.Classify(exception);
            observed.Add(new(
                scenario.Name,
                scenario.Expected.ToString().ToLowerInvariant(),
                actual.ToString().ToLowerInvariant(),
                classification,
                exception?.GetType().FullName,
                exception is null ? null : Digest(exception.Message)));
        }

        var drift = observed.Where(x =>
                !string.Equals(x.ExpectedOutcome, x.ObservedOutcome, StringComparison.Ordinal)
                || (x.ExpectedOutcome == "red" && x.Classification is null))
            .ToArray();
        Assert.True(drift.Length == 0, JsonSerializer.Serialize(drift, JsonOptions));

        var telemetrySnapshot = telemetry.Snapshot();
        var packageFamilyVersion = PackageVersion(typeof(SqlitePhysicalDocumentStore).Assembly);
        var schemaToolVersion = GroundworkSchemaCli.ToolPackageVersion();
        Assert.Equal(CurrentGroundworkVersion, PackageVersion(typeof(StorageManifest).Assembly));
        Assert.Equal(CurrentGroundworkVersion, PackageVersion(typeof(IDocumentStore).Assembly));
        Assert.Equal(CurrentGroundworkVersion, packageFamilyVersion);
        Assert.Equal(CurrentGroundworkVersion, schemaToolVersion);
        // preview.111 plus the durable runtime-alteration and workflow-executable coordination schemas retains
        // unaccepted physical target and plan fingerprint drift from the accepted preview.81 values. Pin the
        // observed drift without accepting it as evidence; the exact-source publication work unit must ratify
        // it before enabling evidence output. Both fingerprints are unchanged from preview.103: the SQL Server
        // index-pin fix in that release changes query rendering only, not the physical target or plan.
        Assert.True(
            StringComparer.Ordinal.Equals(PendingTargetFingerprint, telemetrySnapshot.TargetFingerprint),
            $"Pending target fingerprint mismatch. Expected: {PendingTargetFingerprint}; Actual: {telemetrySnapshot.TargetFingerprint}");
        Assert.True(
            StringComparer.Ordinal.Equals(PendingPlanFingerprint, telemetrySnapshot.PlanFingerprint),
            $"Pending plan fingerprint mismatch. Expected: {PendingPlanFingerprint}; Actual: {telemetrySnapshot.PlanFingerprint}");
        Assert.NotEqual(AcceptedTargetFingerprint, telemetrySnapshot.TargetFingerprint);
        Assert.NotEqual(AcceptedPlanFingerprint, telemetrySnapshot.PlanFingerprint);

        var evidence = new BaselineEvidence(
            "3",
            DesignPersistenceContractProfiles.Target.Name,
            "sqlite",
            "groundwork",
            packageFamilyVersion,
            schemaToolVersion,
            "sqlite-file",
            typeof(Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema).FullName!,
            telemetrySnapshot.TargetFingerprint,
            telemetrySnapshot.PlanFingerprint,
            ExpectedGreenCount: 25,
            ExpectedRedCount: 0,
            telemetrySnapshot.RestartCount,
            telemetrySnapshot.BoundScopeCount,
            telemetrySnapshot.ReconciliationCandidateCount,
            telemetrySnapshot.ReconciliationPassCount,
            telemetrySnapshot.EventTypeDigest,
            observed);
        WriteEvidenceIfRequested(evidence);
    }

    private static Scenario Green(string name, Func<Task> execute) =>
        new(name, ExpectedOutcome.Green, execute, exception => exception is null ? null : "unexpected-failure");

    private static void WriteEvidenceIfRequested(BaselineEvidence evidence)
    {
        var directory = Environment.GetEnvironmentVariable(EvidenceDirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        if (evidence.PackageFamilyVersion != AcceptedEvidenceGroundworkVersion ||
            evidence.TargetFingerprint != AcceptedTargetFingerprint ||
            evidence.PlanFingerprint != AcceptedPlanFingerprint)
        {
            throw new InvalidOperationException(
                "The preview.103 target/plan fingerprint drift is captured but not accepted for evidence publication. " +
                "Ratify it in the exact-source evidence work unit before enabling baseline evidence output.");
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Join(directory, "groundwork-sqlite-target-baseline.json"),
            JsonSerializer.Serialize(evidence, JsonOptions));
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string PackageVersion(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var packageVersion = informationalVersion?.Split('+', 2)[0];
        return !string.IsNullOrWhiteSpace(packageVersion)
            ? packageVersion
            : throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' does not declare an informational package version.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private enum ExpectedOutcome { Green, Red }
    private sealed record Scenario(
        string Name,
        ExpectedOutcome Expected,
        Func<Task> Execute,
        Func<Exception?, string?> Classify);

    private sealed record ScenarioEvidence(
        string Scenario,
        string ExpectedOutcome,
        string ObservedOutcome,
        string? Classification,
        string? ExceptionType,
        string? ExceptionMessageDigest);

    private sealed record BaselineEvidence(
        string SchemaVersion,
        string ContractProfile,
        string Provider,
        string ProviderFamily,
        string PackageFamilyVersion,
        string SchemaToolVersion,
        string Topology,
        string ManifestType,
        string TargetFingerprint,
        string PlanFingerprint,
        int ExpectedGreenCount,
        int ExpectedRedCount,
        int RestartCount,
        int BoundScopeCount,
        int ReconciliationCandidateCount,
        int ReconciliationPassCount,
        string EventTypeDigest,
        IReadOnlyList<ScenarioEvidence> Scenarios);

    private sealed class WorkflowSuite(GroundworkBaselineTelemetry telemetry) : WorkflowDesignContractSuite
    {
        protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
            await SqliteDesignPersistenceContractFixture.CreateAsync(telemetry, cancellationToken);
    }

    private sealed class ActivitySuite(GroundworkBaselineTelemetry telemetry) : ActivityDesignContractSuite
    {
        protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
            await SqliteDesignPersistenceContractFixture.CreateAsync(telemetry, cancellationToken);
    }

    private sealed class AtomicitySuite(GroundworkBaselineTelemetry telemetry) : DesignAtomicityContractSuite
    {
        protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;
        protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
            await SqliteDesignPersistenceContractFixture.CreateAsync(telemetry, cancellationToken);
    }

    private sealed class IsolationSuite(GroundworkBaselineTelemetry telemetry) : DesignIsolationAndRestartContractSuite
    {
        protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;
        protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
            await SqliteDesignPersistenceContractFixture.CreateAsync(telemetry, cancellationToken);
    }
}
