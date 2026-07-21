using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// T064: the provider-neutral additive schema-evolution and safe-apply-recovery contract. This
/// shared suite pins the promises every unified Groundwork design provider must keep when a live,
/// already-applied deployment schema is evolved additively, when an evolving extension collides with
/// an existing physical object, when a safe apply is interrupted partway, when an applied object
/// drifts underneath the durable history, and when inspection or application is cancelled.
///
/// <para>
/// The suite stays provider-neutral by design: it never references a provider SDK or the Groundwork
/// design assemblies. It drives everything through the small <see cref="IDesignSchemaEvolutionFixture"/>
/// abstraction (declared in this file, strings and records only). Concrete evolution manifests are
/// owned by the provider leaves — each leaf hosts its own public deployment-schema extension and its
/// own physical drift, interruption and cancellation seams — mirroring how
/// <see cref="DesignQueryPlanContractSuite"/> and <see cref="DesignQueryScaleContractSuite"/> keep the
/// shared assembly free of provider references.
/// </para>
///
/// <para>
/// Mid-apply interruption (scenario <c>c</c>) is exercised through the fixture's injected-failure
/// apply path rather than through a wall-clock race: the fixture wraps its physical-schema executor
/// so a deterministic failure lands after a fixed number of applied operations, before the target's
/// applied-state snapshot is recorded. The subsequent resume then completes the same plan to ready,
/// proving no operation was applied twice destructively. Cancellation (scenario <c>e</c>) covers the
/// honest, deterministic seam — a token cancelled before any provider I/O — and defers mid-apply
/// interruption to the recovery path in scenario <c>c</c>, because the CLI apply flow exposes no
/// deterministic mid-operation cancellation point.
/// </para>
/// </summary>
public abstract class UnifiedSchemaEvolutionContractSuite
{
    /// <summary>Creates a fresh, baseline-applied evolution fixture for one scenario.</summary>
    protected abstract Task<IDesignSchemaEvolutionFixture> CreateFixtureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// (a) An additive extension — a new declared physical object on the already-applied deployment
    /// schema — plans as pending, safe-applies to ready, and survives a full composed-host restart:
    /// admission accepts, a design aggregate seeded through public commands before the evolution reads
    /// back byte-identically, and the newly declared route is servable end to end.
    /// </summary>
    [Fact]
    public async Task Additive_evolution_applies_and_backfills_across_restart()
    {
        await using var fixture = await CreateFixtureAsync();

        var seed = await fixture.SeedRepresentativeAggregateAsync();
        Assert.False(string.IsNullOrWhiteSpace(seed.AggregateId));
        Assert.False(string.IsNullOrWhiteSpace(seed.ContentHash));

        var apply = await fixture.ApplyAdditiveEvolutionAsync();
        Assert.True(apply.PlanReportedPending, "Additive evolution must plan as pending before it is applied.");
        Assert.True(apply.PendingOperationsBeforeApply > 0, "The additive plan must contain at least one pending operation.");
        Assert.True(apply.ApplySucceeded, "The safe additive apply must succeed.");
        Assert.True(apply.AppliedOperations > 0, "The safe additive apply must apply at least one operation.");
        Assert.True(apply.LiveValidationReady, "Live validation must report ready after the additive apply.");

        var admission = await fixture.RestartUnderEvolvedSchemaAsync();
        Assert.True(admission.IsReady, "Admission must accept the evolved schema after restart.");
        Assert.Equal(0, admission.PendingOperations);

        var rereadHash = await fixture.ReadSeededAggregateHashAsync(seed.AggregateId);
        Assert.Equal(seed.ContentHash, rereadHash);

        Assert.True(await fixture.ProbeEvolvedRouteServableAsync(), "The newly declared evolved route must be servable after restart.");
    }

    /// <summary>
    /// (b) An extension whose resolved physical name collides with an existing applied object surfaces
    /// the exact collision diagnostic during offline composition/plan and never mutates the target.
    /// </summary>
    [Fact]
    public async Task Physical_name_collision_is_a_blocking_diagnostic()
    {
        await using var fixture = await CreateFixtureAsync();

        var fingerprintBefore = await fixture.CaptureTargetFingerprintAsync();

        var report = await fixture.InspectNameCollisionAsync();
        Assert.True(report.SurfacedCollision, "A colliding extension must surface a blocking collision diagnostic.");
        Assert.Contains("COLLISION", report.DiagnosticCode, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(report.CollidingIdentifier));
        Assert.Contains(report.CollidingIdentifier, report.DiagnosticText, StringComparison.Ordinal);
        Assert.False(report.TargetMutated, "A collision diagnostic must not mutate the target.");

        Assert.Equal(fingerprintBefore, await fixture.CaptureTargetFingerprintAsync());
    }

    /// <summary>
    /// (c) A safe apply interrupted after a fixed number of operations — before the applied-state
    /// snapshot is recorded — leaves a target that a subsequent safe apply completes to ready, applying
    /// no operation twice destructively; final validation is ready and admission is green.
    /// </summary>
    [Fact]
    public async Task Interrupted_safe_apply_recovers_to_ready()
    {
        await using var fixture = await CreateFixtureAsync();

        await fixture.SeedRepresentativeAggregateAsync();

        var interrupted = await fixture.ApplyAdditiveEvolutionWithInterruptionAsync(failAfterOperations: 1);
        Assert.True(interrupted.WasInterrupted, "The injected failure must interrupt the safe apply.");
        Assert.False(interrupted.LiveValidationReady, "An interrupted apply must not report ready.");

        var resumed = await fixture.ResumeInterruptedEvolutionAsync();
        Assert.True(resumed.ApplySucceeded, "The resuming safe apply must succeed.");
        Assert.True(resumed.LiveValidationReady, "Validation must report ready after the resume.");

        var admission = await fixture.RestartUnderEvolvedSchemaAsync();
        Assert.True(admission.IsReady, "Admission must be green after the interrupted apply is resumed to ready.");
        Assert.Equal(0, admission.PendingOperations);
    }

    /// <summary>
    /// (d) Physical drift of one applied object under the durable history blocks both the deployment
    /// validation path and runtime admission — never an empty store, never an unvalidated fallback.
    /// </summary>
    [Fact]
    public async Task Applied_object_drift_blocks_validation_and_admission()
    {
        await using var fixture = await CreateFixtureAsync();

        var report = await fixture.PerturbAppliedObjectAndInspectAsync();
        Assert.False(string.IsNullOrWhiteSpace(report.PerturbedObject));
        Assert.True(report.ValidationBlocked, $"Deployment validation must block on applied-object drift ({report.Detail}).");
        Assert.True(report.AdmissionBlocked, $"Runtime admission must block on applied-object drift ({report.Detail}).");
    }

    /// <summary>
    /// (e) Cancellation requested before any provider I/O is observed as cancellation rather than a
    /// partial mutation. Mid-apply interruption is covered by the recovery path in
    /// <see cref="Interrupted_safe_apply_recovers_to_ready"/>.
    /// </summary>
    [Fact]
    public async Task Cancellation_during_inspection_or_apply_is_observed()
    {
        await using var fixture = await CreateFixtureAsync();

        var fingerprintBefore = await fixture.CaptureTargetFingerprintAsync();

        Assert.True(
            await fixture.ObserveCancellationBeforeProviderIoAsync(),
            "Cancellation requested before provider I/O must surface as an observed cancellation.");

        Assert.Equal(fingerprintBefore, await fixture.CaptureTargetFingerprintAsync());
    }
}

/// <summary>
/// The provider-neutral seam the shared <see cref="UnifiedSchemaEvolutionContractSuite"/> drives. Each
/// unified Groundwork design provider implements this against its own composed host, its own public
/// deployment-schema extension, and its own physical drift / interruption / cancellation mechanics.
/// Every member speaks only strings and the small records declared alongside it, so the shared
/// assembly never references a provider SDK or the Groundwork design assemblies.
/// </summary>
public interface IDesignSchemaEvolutionFixture : IAsyncDisposable
{
    /// <summary>The stable provider identity (for diagnostics).</summary>
    string Provider { get; }

    /// <summary>
    /// Seeds one representative design aggregate through public design commands and returns its stable
    /// identity plus a content hash captured through the public read port.
    /// </summary>
    Task<DesignEvolutionSeed> SeedRepresentativeAggregateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Plans and safe-applies the additive evolution against the live baseline target, then live-validates
    /// readiness. Returns the plan/apply/validation evidence.
    /// </summary>
    Task<DesignEvolutionApplyReport> ApplyAdditiveEvolutionAsync(CancellationToken cancellationToken = default);

    /// <summary>Restarts the composed host under the evolved deployment schema and reports admission readiness.</summary>
    Task<DesignEvolutionAdmissionReport> RestartUnderEvolvedSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a previously seeded aggregate through the public read port and returns its content hash.</summary>
    Task<string> ReadSeededAggregateHashAsync(string aggregateId, CancellationToken cancellationToken = default);

    /// <summary>Proves the newly declared evolved route is servable end to end (write then read a probe document).</summary>
    Task<bool> ProbeEvolvedRouteServableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes a colliding extension and returns the surfaced collision diagnostic. Offline: no target I/O.
    /// </summary>
    Task<DesignEvolutionCollisionReport> InspectNameCollisionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the additive evolution with a deterministic failure injected after
    /// <paramref name="failAfterOperations"/> operations, before the applied-state snapshot is recorded.
    /// </summary>
    Task<DesignEvolutionInterruptionReport> ApplyAdditiveEvolutionWithInterruptionAsync(
        int failAfterOperations,
        CancellationToken cancellationToken = default);

    /// <summary>Resumes a previously interrupted safe apply and live-validates readiness.</summary>
    Task<DesignEvolutionApplyReport> ResumeInterruptedEvolutionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Perturbs exactly one applied physical object and reports whether both the deployment validation
    /// path and runtime admission block.
    /// </summary>
    Task<DesignEvolutionDriftReport> PerturbAppliedObjectAndInspectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation before any provider I/O during inspection and reports whether cancellation
    /// was observed (rather than a partial mutation).
    /// </summary>
    Task<bool> ObserveCancellationBeforeProviderIoAsync(CancellationToken cancellationToken = default);

    /// <summary>Captures a stable fingerprint of the durable target, for non-mutation assertions.</summary>
    Task<string> CaptureTargetFingerprintAsync(CancellationToken cancellationToken = default);
}

/// <summary>One seeded design aggregate's stable identity and content hash.</summary>
public sealed record DesignEvolutionSeed(string AggregateId, string ContentHash);

/// <summary>Evidence from planning, safe-applying and live-validating an additive evolution.</summary>
public sealed record DesignEvolutionApplyReport(
    bool PlanReportedPending,
    int PendingOperationsBeforeApply,
    bool ApplySucceeded,
    int AppliedOperations,
    bool LiveValidationReady);

/// <summary>Restart-admission readiness under the evolved deployment schema.</summary>
public sealed record DesignEvolutionAdmissionReport(bool IsReady, int PendingOperations);

/// <summary>The surfaced physical-name collision diagnostic for a colliding extension.</summary>
public sealed record DesignEvolutionCollisionReport(
    bool SurfacedCollision,
    string DiagnosticCode,
    string CollidingIdentifier,
    string DiagnosticText,
    bool TargetMutated);

/// <summary>Evidence from a deliberately interrupted safe apply.</summary>
public sealed record DesignEvolutionInterruptionReport(bool WasInterrupted, bool LiveValidationReady);

/// <summary>Whether applied-object drift blocks both the deployment and runtime readiness paths.</summary>
public sealed record DesignEvolutionDriftReport(
    string PerturbedObject,
    bool ValidationBlocked,
    bool AdmissionBlocked,
    string Detail);
