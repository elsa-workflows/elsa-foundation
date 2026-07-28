using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Provider-neutral atomic-write conformance scenarios. Concrete fixtures execute the same
/// multi-document operation through their composed design persistence boundary and expose only
/// logical fault points, operation identity, canonical request fingerprints, and outcomes.
/// </summary>
public abstract class DesignAtomicityContractSuite
{
    private static readonly DesignAtomicityOperationKey OperationKey = new("design-atomicity-create-v1");
    private static readonly DesignCanonicalRequestFingerprint CanonicalFingerprint = new("canonical:create-workflow-and-draft:v1");
    private static readonly DesignCanonicalRequestFingerprint ChangedFingerprint = new("canonical:create-workflow-and-draft:v2");

    protected abstract Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default);
    protected abstract DesignPersistenceContractProfile ContractProfile { get; }

    [SkippableFact]
    public async Task Partial_staging_failure_leaves_no_visible_partial_aggregate()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.AtomicityPartialStagingFailure);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        await using var fault = await fixture.ArmAtomicityFaultAsync(new(
            DesignAtomicityFaultPhase.AfterStagedWrite,
            DesignAtomicityFaultAction.Throw));

        var exception = await Record.ExceptionAsync(() => fixture.ExecuteAtomicityOperationAsync(Request()));

        Assert.NotNull(exception);
        Assert.False(exception is OperationCanceledException);
        Assert.True(fault.WasTriggered);
        await AssertNoDurableOutcomeAsync(fixture);
    }

    [SkippableFact]
    public async Task Non_success_provider_decision_rolls_back_all_staged_parts()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.AtomicityNonSuccessProviderDecision);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        await using var fault = await fixture.ArmAtomicityFaultAsync(new(
            DesignAtomicityFaultPhase.BeforeProviderDecision,
            DesignAtomicityFaultAction.ReturnNonSuccess));

        var result = await fixture.ExecuteAtomicityOperationAsync(Request());

        Assert.Equal(DesignAtomicityOperationStatus.Rejected, result.Status);
        Assert.Null(result.AuthoritativeResultFingerprint);
        Assert.True(fault.WasTriggered);
        await AssertNoDurableOutcomeAsync(fixture);
    }

    [SkippableFact]
    public async Task Cancellation_rolls_back_and_propagates_cancellation()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.AtomicityCancellation);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        await using var fault = await fixture.ArmAtomicityFaultAsync(new(
            DesignAtomicityFaultPhase.BeforeProviderDecision,
            DesignAtomicityFaultAction.Cancel));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ExecuteAtomicityOperationAsync(Request()));

        Assert.True(exception.CancellationToken.IsCancellationRequested);
        Assert.True(fault.WasTriggered);
        await AssertNoDurableOutcomeAsync(fixture);
    }

    [SkippableFact]
    public async Task Lost_acknowledgement_after_durable_decision_reconciles_the_authoritative_result_on_retry()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.AtomicityLostAcknowledgement);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        var request = Request();
        await using var fault = await fixture.ArmAtomicityFaultAsync(new(
            DesignAtomicityFaultPhase.AfterDurableDecision,
            DesignAtomicityFaultAction.Throw));

        var exception = await Record.ExceptionAsync(() => fixture.ExecuteAtomicityOperationAsync(request));

        Assert.NotNull(exception);
        Assert.False(exception is OperationCanceledException);
        Assert.True(fault.WasTriggered);
        var durable = await AssertCommittedExactlyOnceAsync(fixture);

        var replay = await fixture.ExecuteAtomicityOperationAsync(request);

        Assert.Equal(DesignAtomicityOperationStatus.Replayed, replay.Status);
        Assert.Equal(durable.AuthoritativeDurableResultFingerprint, replay.AuthoritativeResultFingerprint);
        var afterReplay = await AssertCommittedExactlyOnceAsync(fixture);
        Assert.Equal(durable.CanonicalAggregateStateFingerprint, afterReplay.CanonicalAggregateStateFingerprint);
        Assert.Equal(durable.AuthoritativeDurableResultFingerprint, afterReplay.AuthoritativeDurableResultFingerprint);
    }

    [SkippableFact]
    public async Task Same_stable_operation_key_and_canonical_fingerprint_replay_the_prior_result()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.AtomicityExactReplay);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        var scopeARequest = Request(DesignPersistenceFixtureData.ScopeA);
        var scopeBRequest = Request(DesignPersistenceFixtureData.ScopeB);

        var committedA = await fixture.ExecuteAtomicityOperationAsync(scopeARequest);
        var committedB = await fixture.ExecuteAtomicityOperationAsync(scopeBRequest);
        var replayedA = await fixture.ExecuteAtomicityOperationAsync(scopeARequest);
        var replayedB = await fixture.ExecuteAtomicityOperationAsync(scopeBRequest);

        Assert.Equal(DesignAtomicityOperationStatus.Committed, committedA.Status);
        Assert.Equal(DesignAtomicityOperationStatus.Committed, committedB.Status);
        Assert.Equal(DesignAtomicityOperationStatus.Replayed, replayedA.Status);
        Assert.Equal(DesignAtomicityOperationStatus.Replayed, replayedB.Status);
        Assert.Equal(committedA.AuthoritativeResultFingerprint, replayedA.AuthoritativeResultFingerprint);
        Assert.Equal(committedB.AuthoritativeResultFingerprint, replayedB.AuthoritativeResultFingerprint);
        Assert.NotEqual(committedA.AuthoritativeResultFingerprint, committedB.AuthoritativeResultFingerprint);
        await AssertCommittedExactlyOnceAsync(fixture, DesignPersistenceFixtureData.ScopeA);
        await AssertCommittedExactlyOnceAsync(fixture, DesignPersistenceFixtureData.ScopeB);
    }

    [SkippableFact]
    public async Task Stable_operation_key_reuse_with_a_different_fingerprint_conflicts_without_mutation()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.AtomicityKeyReuseConflict);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var committed = await fixture.ExecuteAtomicityOperationAsync(Request());
        var beforeConflict = await AssertCommittedExactlyOnceAsync(fixture);
        var conflict = await fixture.ExecuteAtomicityOperationAsync(Request(ChangedFingerprint));

        Assert.Equal(DesignAtomicityOperationStatus.Committed, committed.Status);
        Assert.Equal(DesignAtomicityOperationStatus.Conflict, conflict.Status);
        Assert.Null(conflict.AuthoritativeResultFingerprint);
        var afterConflict = await AssertCommittedExactlyOnceAsync(fixture);
        Assert.Equal(beforeConflict.CanonicalAggregateStateFingerprint, afterConflict.CanonicalAggregateStateFingerprint);
        Assert.Equal(beforeConflict.AuthoritativeDurableResultFingerprint, afterConflict.AuthoritativeDurableResultFingerprint);
    }

    [SkippableFact]
    public async Task Duplicate_delivery_does_not_repeat_the_fixture_post_commit_outcome()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.AtomicityDuplicateDelivery);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        var request = Request();

        await fixture.ExecuteAtomicityOperationAsync(request);
        await fixture.ExecuteAtomicityOperationAsync(request);

        var snapshot = await fixture.ReadAtomicitySnapshotAsync(DesignPersistenceFixtureData.ScopeA);
        Assert.Equal(1, snapshot.DurableOutcomeCount);
        Assert.Equal(1, snapshot.PostCommitOutcomeCount);
    }

    private static DesignAtomicityOperationRequest Request(DesignCanonicalRequestFingerprint? fingerprint = null) =>
        Request(DesignPersistenceFixtureData.ScopeA, fingerprint);

    private static DesignAtomicityOperationRequest Request(
        string storageScope,
        DesignCanonicalRequestFingerprint? fingerprint = null) => new(
        storageScope,
        OperationKey,
        fingerprint ?? CanonicalFingerprint);

    private static async Task AssertNoDurableOutcomeAsync(IDesignPersistenceContractFixture fixture)
    {
        var snapshot = await fixture.ReadAtomicitySnapshotAsync(DesignPersistenceFixtureData.ScopeA);

        Assert.Equal(0, snapshot.VisibleAggregatePartCount);
        Assert.Equal(0, snapshot.DurableOutcomeCount);
        Assert.Equal(0, snapshot.PostCommitOutcomeCount);
        Assert.True(string.IsNullOrWhiteSpace(snapshot.CanonicalAggregateStateFingerprint));
        Assert.True(string.IsNullOrWhiteSpace(snapshot.AuthoritativeDurableResultFingerprint));
    }

    private static async Task<DesignAtomicitySnapshot> AssertCommittedExactlyOnceAsync(
        IDesignPersistenceContractFixture fixture,
        string storageScope = DesignPersistenceFixtureData.ScopeA)
    {
        var snapshot = await fixture.ReadAtomicitySnapshotAsync(storageScope);

        Assert.True(snapshot.ExpectedAggregatePartCount > 1, "The atomicity fixture must exercise a multi-document operation.");
        Assert.Equal(snapshot.ExpectedAggregatePartCount, snapshot.VisibleAggregatePartCount);
        Assert.Equal(1, snapshot.DurableOutcomeCount);
        Assert.Equal(1, snapshot.PostCommitOutcomeCount);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.CanonicalAggregateStateFingerprint));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.AuthoritativeDurableResultFingerprint));
        return snapshot;
    }

    /// <summary>
    /// An over-limit value for a bounded projected text column fails validation before provider
    /// I/O — uniformly on every provider, never truncating and never partially persisting. The
    /// shared Groundwork guard surfaces diagnostic GW-PHYSICAL-037 for all four providers.
    /// </summary>
    [SkippableFact]
    public async Task Over_limit_projected_text_fails_validation_without_persisting()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.AtomicityProjectionOverLimitRejection);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        var overLimitDescription = new string('d', 257);

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;

        var workflow = DesignPersistenceFixtureData.WorkflowDefinition();
        workflow.Description = overLimitDescription;
        var workflowException = await Record.ExceptionAsync(() =>
            services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey("over-limit-workflow-create"),
                workflow,
                DesignPersistenceFixtureData.WorkflowDraft(),
                DesignPersistenceFixtureData.WorkflowDraftLayout(),
                CancellationToken.None));
        AssertOverLimitRejection(workflowException);
        Assert.Null(await services.GetRequiredService<IWorkflowDefinitionStore>()
            .FindByIdAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));

        var activity = DesignPersistenceFixtureData.ActivityDefinition();
        activity.Description = overLimitDescription;
        var activityException = await Record.ExceptionAsync(() =>
            services.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey("over-limit-activity-create"),
                activity,
                DesignPersistenceFixtureData.ActivityVersion(),
                CancellationToken.None));
        AssertOverLimitRejection(activityException);
        Assert.Null(await services.GetRequiredService<IActivityDefinitionStore>()
            .FindAsync(new ActivityDefinitionFilter { Id = DesignPersistenceFixtureData.ActivityDefinitionId }));
    }

    private static void AssertOverLimitRejection(Exception? exception)
    {
        Assert.NotNull(exception);
        Assert.False(exception is OperationCanceledException, "An over-limit rejection must not be reported as cancellation.");
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        Assert.Contains(messages, message =>
            message.Contains("exceeds its declared maximum length", StringComparison.Ordinal) ||
            message.Contains("GW-PHYSICAL-037", StringComparison.Ordinal));
    }

    private void SkipIfNotApplicable(DesignPersistenceContractScenario scenario)
    {
        var applicability = ContractProfile.GetApplicability(scenario);
        Xunit.Skip.If(!applicability.IsApplicable, applicability.NotApplicableReason!);
    }
}
