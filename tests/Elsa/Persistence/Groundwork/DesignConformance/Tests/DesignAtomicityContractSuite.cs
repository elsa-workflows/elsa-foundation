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

    [Fact]
    public async Task Partial_staging_failure_leaves_no_visible_partial_aggregate()
    {
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

    [Fact]
    public async Task Non_success_provider_decision_rolls_back_all_staged_parts()
    {
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

    [Fact]
    public async Task Cancellation_rolls_back_and_propagates_cancellation()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        await using var fault = await fixture.ArmAtomicityFaultAsync(new(
            DesignAtomicityFaultPhase.BeforeProviderDecision,
            DesignAtomicityFaultAction.Cancel));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ExecuteAtomicityOperationAsync(Request()));

        Assert.True(fault.WasTriggered);
        await AssertNoDurableOutcomeAsync(fixture);
    }

    [Fact]
    public async Task Lost_acknowledgement_after_durable_decision_reconciles_the_authoritative_result_on_retry()
    {
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
        await AssertCommittedExactlyOnceAsync(fixture);

        var replay = await fixture.ExecuteAtomicityOperationAsync(request);

        Assert.Equal(DesignAtomicityOperationStatus.Replayed, replay.Status);
        Assert.False(string.IsNullOrWhiteSpace(replay.AuthoritativeResultFingerprint));
        await AssertCommittedExactlyOnceAsync(fixture);
    }

    [Fact]
    public async Task Same_stable_operation_key_and_canonical_fingerprint_replay_the_prior_result()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        var request = Request();

        var committed = await fixture.ExecuteAtomicityOperationAsync(request);
        var replayed = await fixture.ExecuteAtomicityOperationAsync(request);

        Assert.Equal(DesignAtomicityOperationStatus.Committed, committed.Status);
        Assert.Equal(DesignAtomicityOperationStatus.Replayed, replayed.Status);
        Assert.Equal(committed.AuthoritativeResultFingerprint, replayed.AuthoritativeResultFingerprint);
        await AssertCommittedExactlyOnceAsync(fixture);
    }

    [Fact]
    public async Task Stable_operation_key_reuse_with_a_different_fingerprint_conflicts_without_mutation()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var committed = await fixture.ExecuteAtomicityOperationAsync(Request());
        var conflict = await fixture.ExecuteAtomicityOperationAsync(Request(ChangedFingerprint));

        Assert.Equal(DesignAtomicityOperationStatus.Committed, committed.Status);
        Assert.Equal(DesignAtomicityOperationStatus.Conflict, conflict.Status);
        Assert.Null(conflict.AuthoritativeResultFingerprint);
        await AssertCommittedExactlyOnceAsync(fixture);
    }

    [Fact]
    public async Task Duplicate_delivery_does_not_duplicate_the_domain_outcome()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        var request = Request();

        await fixture.ExecuteAtomicityOperationAsync(request);
        await fixture.ExecuteAtomicityOperationAsync(request);

        var snapshot = await fixture.ReadAtomicitySnapshotAsync(DesignPersistenceFixtureData.ScopeA);
        Assert.Equal(1, snapshot.DurableOutcomeCount);
        Assert.Equal(1, snapshot.PublishedOutcomeCount);
    }

    private static DesignAtomicityOperationRequest Request(DesignCanonicalRequestFingerprint? fingerprint = null) => new(
        DesignPersistenceFixtureData.ScopeA,
        OperationKey,
        fingerprint ?? CanonicalFingerprint);

    private static async Task AssertNoDurableOutcomeAsync(IDesignPersistenceContractFixture fixture)
    {
        var snapshot = await fixture.ReadAtomicitySnapshotAsync(DesignPersistenceFixtureData.ScopeA);

        Assert.Equal(0, snapshot.VisibleAggregatePartCount);
        Assert.Equal(0, snapshot.DurableOutcomeCount);
        Assert.Equal(0, snapshot.PublishedOutcomeCount);
    }

    private static async Task AssertCommittedExactlyOnceAsync(IDesignPersistenceContractFixture fixture)
    {
        var snapshot = await fixture.ReadAtomicitySnapshotAsync(DesignPersistenceFixtureData.ScopeA);

        Assert.True(snapshot.ExpectedAggregatePartCount > 1, "The atomicity fixture must exercise a multi-document operation.");
        Assert.Equal(snapshot.ExpectedAggregatePartCount, snapshot.VisibleAggregatePartCount);
        Assert.Equal(1, snapshot.DurableOutcomeCount);
        Assert.Equal(1, snapshot.PublishedOutcomeCount);
    }
}
