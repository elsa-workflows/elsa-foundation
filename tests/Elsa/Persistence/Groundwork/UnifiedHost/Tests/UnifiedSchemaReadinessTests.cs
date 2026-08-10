using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.SchemaEvolution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// Spec 093 T061 (Half B): direct branch coverage for the three provider-neutral composition
/// classes that gate runtime schema readiness — the deployment-schema source, the DI registration
/// surface, and the physical-schema runtime admission source. Every seam is driven through its real
/// public API against small in-assembly fakes; no provider package is pulled into this
/// provider-neutral host test project.
/// </summary>
public sealed class UnifiedSchemaReadinessTests
{
    // ---------------------------------------------------------------------------------------------
    // GroundworkDeploymentSchemaManifestSource — lazy composition validation branches.
    // Every branch is reached through the public CreateManifest()/CreateNamePolicy() surface, which
    // forces the lazy DeploymentDefinition. (The internal Get* accessors are not visible here.)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Empty_manifest_source_list_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new EmptyDeployment().CreateManifest());
        Assert.Equal("A deployment schema must select at least one manifest source.", exception.Message);
    }

    [Fact]
    public void Duplicate_manifest_source_types_are_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new DuplicateDeployment().CreateManifest());
        Assert.Equal(
            "A deployment schema cannot select the same manifest source type more than once.",
            exception.Message);
    }

    [Fact]
    public void A_non_manifest_source_type_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new NonManifestSourceDeployment().CreateManifest());
        Assert.Contains("must be a concrete", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IGroundworkStorageManifestSource), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_source_without_a_public_parameterless_constructor_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new NonConstructibleDeployment().CreateManifest());
        Assert.Contains("must have a public parameterless constructor", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(NonConstructibleManifestSource).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_host_naming_policy_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new NullNamingDeployment().CreateManifest());
        Assert.Equal("The deployment schema returned a null naming policy.", exception.Message);
    }

    [Fact]
    public void The_host_naming_policy_is_passed_through_into_the_composed_name_policy()
    {
        var unit = new IdentityNamingDeployment().CreateManifest().StorageUnits.First();
        var context = new PhysicalNameContext(unit.Identity, PhysicalObjectKind.PrimaryStorage, unit.Identity.Value);

        var identityName = new IdentityNamingDeployment().CreateNamePolicy().ResolveName(context);
        var prefixedName = new HostPrefixDeployment().CreateNamePolicy().ResolveName(context);

        // The identity variant leaves the default naming untouched; the host variant's delegate is
        // threaded through the resolver, so its output is distinct and host-prefixed.
        Assert.StartsWith("host_", prefixedName);
        Assert.NotEqual(identityName, prefixedName);
        Assert.DoesNotContain("host_", identityName, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // GroundworkStorageCompositionRegistration — idempotent registration + resolvable surface.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Adding_the_composition_twice_registers_each_service_once()
    {
        var services = new ServiceCollection();

        services.AddGroundworkStorageComposition();
        services.AddGroundworkStorageComposition();
        // Capability admission is per target, not per composition: a provider leaf declares a target and
        // registers admission for it. Composing storage on its own admits nothing.
        services.AddGroundworkProviderCapabilityAdmission();
        services.AddGroundworkProviderCapabilityAdmission();

        Assert.Single(services, d => d.ServiceType == typeof(GroundworkStorageNamingPolicyOptions));
        Assert.Single(services, d => d.ServiceType == typeof(GroundworkManifestBindings));
        Assert.Single(services, d =>
            d.ServiceType == typeof(GroundworkProviderCapabilityAdmission) && d.IsKeyedService);
        Assert.Single(services, d =>
            d.ServiceType == typeof(GroundworkProviderCapabilityAdmission) && !d.IsKeyedService);
        Assert.Single(services, d => d.ServiceType == typeof(GroundworkStorageCompositionValidator));
        Assert.Single(services, d => d.ServiceType == typeof(GroundworkStorageCompositionHandler));
        Assert.Single(services, d => d.ServiceType == typeof(GroundworkStorageCompositionFactory));
    }

    [Fact]
    public void Composing_storage_alone_registers_no_capability_admission()
    {
        var services = new ServiceCollection();

        services.AddGroundworkStorageComposition();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(GroundworkProviderCapabilityAdmission));
    }

    [Fact]
    public async Task The_complete_registered_composition_surface_resolves_from_a_built_provider()
    {
        var services = new ServiceCollection();
        services.AddGroundworkRuntimeStores();
        services.AddGroundworkStorageComposition();
        services.AddGroundworkProviderCapabilityAdmission();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // Singletons resolve from the root.
        Assert.NotNull(provider.GetRequiredService<GroundworkStorageNamingPolicyOptions>());
        Assert.NotNull(provider.GetRequiredService<GroundworkManifestBindings>());
        // The default target is reachable both by key and unkeyed, and they are the same instance.
        Assert.Same(
            provider.GetRequiredService<GroundworkProviderCapabilityAdmission>(),
            provider.GetRequiredKeyedService<GroundworkProviderCapabilityAdmission>(GroundworkTargetNames.Default));

        // Scoped composition services resolve from a scope.
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GroundworkStorageCompositionValidator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GroundworkStorageCompositionHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GroundworkStorageCompositionFactory>());
    }

    // ---------------------------------------------------------------------------------------------
    // GroundworkPhysicalSchemaManifestSource.InspectRuntimeAdmissionAsync — admission outcomes.
    // The runtime source is composed for real (runtime manifest + generic capabilities); only the
    // IPhysicalSchemaExecutor is faked so we can script history and record apply-surface calls.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pending_admission_reports_pending_without_applying_anything()
    {
        var source = await BuildRuntimeSourceAsync();
        var inspector = new RecordingSchemaInspector(); // empty history => everything pending

        var result = await source.InspectRuntimeAdmissionAsync(inspector);

        Assert.False(result.IsReady);
        Assert.NotEmpty(result.PendingOperations);
        Assert.Single(result.Diagnostics, d => d.Code == "ELSA-GW-SCHEMA-PENDING");
        Assert.Equal(0, result.AppliedOperationCount);
        // Inspect-only guarantee: default options leave AutoApplyOnStartup false, so only the
        // non-mutating history inspector runs; no apply surface fires.
        Assert.True(result.PendingOperations.Count > 0 && inspector.InspectHistoryCalls > 0);
        Assert.Equal(0, inspector.ApplySurfaceCalls);
    }

    [Fact]
    public async Task Ready_admission_after_auto_apply_does_not_re_apply_on_inspection()
    {
        var source = await BuildRuntimeSourceAsync();
        var inspector = new RecordingSchemaInspector();

        // An explicit auto-apply pass provisions the (in-memory) target and records applied state.
        var applied = await source.InspectRuntimeAdmissionAsync(
            inspector,
            new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });

        Assert.True(applied.IsReady);
        Assert.True(applied.AppliedOperationCount > 0);
        Assert.True(inspector.ApplyOperationCalls > 0);
        Assert.True(inspector.RecordAppliedStateCalls > 0);

        // A subsequent inspect-only pass sees the recorded state as ready and never re-applies.
        inspector.ResetCallCounters();
        var reinspect = await source.InspectRuntimeAdmissionAsync(inspector);

        Assert.True(reinspect.IsReady);
        Assert.Empty(reinspect.PendingOperations);
        Assert.Equal(0, inspector.ApplySurfaceCalls);
    }

    [Fact]
    public async Task An_invalid_applied_history_reports_drift_without_repairing_it()
    {
        // Capture a genuine applied state through an auto-apply pass.
        var source = await BuildRuntimeSourceAsync();
        var applier = new RecordingSchemaInspector();
        await source.InspectRuntimeAdmissionAsync(
            applier,
            new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });
        var appliedState = applier.RecordedState;
        Assert.NotNull(appliedState);

        // Present that state as applied history but flagged invalid: the source must report drift and
        // must never attempt a repair.
        var inspector = new RecordingSchemaInspector(() => new PhysicalSchemaInspectionResult(
            PhysicalSchemaHistoryState.FromApplied(appliedState!),
            IsAppliedSchemaValid: false));

        var result = await source.InspectRuntimeAdmissionAsync(inspector);

        Assert.False(result.IsReady);
        Assert.Contains(result.Diagnostics, d => d.Code == "ELSA-GW-SCHEMA-DRIFT");
        Assert.Equal(0, inspector.ApplySurfaceCalls);
    }

    [Fact]
    public async Task An_invalid_operation_from_the_inspector_is_absorbed_as_drift()
    {
        var source = await BuildRuntimeSourceAsync();
        var inspector = new RecordingSchemaInspector(
            () => throw new InvalidOperationException("provider history read failed"));

        var result = await source.InspectRuntimeAdmissionAsync(inspector);

        // The source's own catch turns an InvalidOperationException into a blocking drift verdict.
        Assert.False(result.IsReady);
        Assert.Contains(result.Diagnostics, d => d.Code == "ELSA-GW-SCHEMA-DRIFT");
        Assert.Empty(result.PendingOperations);
    }

    [Fact]
    public async Task A_non_invalid_operation_inspector_failure_propagates()
    {
        var source = await BuildRuntimeSourceAsync();
        var inspector = new RecordingSchemaInspector(
            () => throw new TimeoutException("provider unavailable"));

        await Assert.ThrowsAsync<TimeoutException>(
            () => source.InspectRuntimeAdmissionAsync(inspector).AsTask());
    }

    [Fact]
    public async Task Cancellation_is_observed_before_the_inspector_is_touched()
    {
        var source = await BuildRuntimeSourceAsync();
        var inspector = new RecordingSchemaInspector();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => source.InspectRuntimeAdmissionAsync(inspector, cancellationToken: cancellation.Token).AsTask());

        Assert.Equal(0, inspector.InspectHistoryCalls);
        Assert.Equal(0, inspector.ApplySurfaceCalls);
    }

    // ---------------------------------------------------------------------------------------------
    // Shared helpers + fakes.
    // ---------------------------------------------------------------------------------------------

    private static async Task<GroundworkPhysicalSchemaManifestSource> BuildRuntimeSourceAsync()
    {
        var services = new ServiceCollection();
        services.AddGroundworkRuntimeStores();
        services.AddGroundworkStorageComposition();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<GroundworkStorageCompositionFactory>();
        return await factory.CreateSourceAsync(ProviderCapabilities(), ProviderPhysicalNameNormalizer.Identity);
    }

    private static GroundworkProviderCapabilitySnapshot ProviderCapabilities()
    {
        var provider = new ProviderIdentity("groundwork-sqlite", "1.0.0");
        return GroundworkProviderCapabilitySnapshot.ForFeatureRoutes(
            new ProviderCapabilityReport(
                provider,
                new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit },
                new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit },
                IndexCapabilities.All,
                Enum.GetValues<PortableQueryOperation>().ToHashSet(),
                Enum.GetValues<ConcurrencyKind>().ToHashSet(),
                []),
            new GroundworkProviderTopologySnapshot(
                provider.Name,
                "sqlite-file",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            RuntimeGroundworkStorageManifestSource.FeatureName,
            [RuntimeGroundworkStorageManifestSource.CreateCheckpointCommitRouteRequirement()]);
    }

    /// <summary>
    /// In-memory recording inspector/executor. Runtime admission reads through the non-mutating
    /// <see cref="IPhysicalSchemaHistoryInspector"/> surface; auto-apply additionally drives the
    /// mutating <see cref="IPhysicalSchemaExecutor"/> surface. The fake faithfully stores whatever
    /// applied state the admission core records, so a post-apply inspection reads back a ready
    /// target — with no provider I/O. An optional factory scripts the inspection result (empty,
    /// invalid-with-applied-state, or throwing).
    /// </summary>
    private sealed class RecordingSchemaInspector(Func<PhysicalSchemaInspectionResult>? inspection = null)
        : IPhysicalSchemaExecutor, IPhysicalSchemaHistoryInspector
    {
        private readonly Func<PhysicalSchemaInspectionResult> _inspection = inspection ??
            (() => new PhysicalSchemaInspectionResult(PhysicalSchemaHistoryState.Empty, IsAppliedSchemaValid: true));

        public int InspectHistoryCalls { get; private set; }
        public int AcquireLockCalls { get; private set; }
        public int ReadHistoryCalls { get; private set; }
        public int ApplyOperationCalls { get; private set; }
        public int RecordAppliedStateCalls { get; private set; }
        public int ApplySurfaceCalls => ApplyOperationCalls + RecordAppliedStateCalls;
        public PhysicalSchemaAppliedState? RecordedState { get; private set; }

        public void ResetCallCounters()
        {
            InspectHistoryCalls = 0;
            AcquireLockCalls = 0;
            ReadHistoryCalls = 0;
            ApplyOperationCalls = 0;
            RecordAppliedStateCalls = 0;
        }

        public ValueTask<PhysicalSchemaInspectionResult> InspectHistoryAsync(
            PhysicalSchemaTarget target,
            CancellationToken cancellationToken)
        {
            InspectHistoryCalls++;
            return ValueTask.FromResult(RecordedState is null
                ? _inspection()
                : new PhysicalSchemaInspectionResult(
                    PhysicalSchemaHistoryState.FromApplied(RecordedState),
                    IsAppliedSchemaValid: true));
        }

        public ValueTask<IPhysicalSchemaApplicationLock> AcquireApplicationLockAsync(
            PhysicalSchemaTargetIdentity target,
            CancellationToken cancellationToken)
        {
            AcquireLockCalls++;
            return ValueTask.FromResult<IPhysicalSchemaApplicationLock>(new NoOpLock(target));
        }

        public ValueTask<PhysicalSchemaHistoryState> ReadHistoryAsync(
            PhysicalSchemaTargetIdentity target,
            IPhysicalSchemaApplicationLock applicationLock,
            CancellationToken cancellationToken)
        {
            ReadHistoryCalls++;
            return ValueTask.FromResult(RecordedState is null
                ? PhysicalSchemaHistoryState.Empty
                : PhysicalSchemaHistoryState.FromApplied(RecordedState));
        }

        public ValueTask<PhysicalSchemaOperationAcknowledgement> ApplyOperationAsync(
            PhysicalSchemaTargetIdentity target,
            PhysicalSchemaOperation operation,
            IPhysicalSchemaApplicationLock applicationLock,
            CancellationToken cancellationToken)
        {
            ApplyOperationCalls++;
            return ValueTask.FromResult(new PhysicalSchemaOperationAcknowledgement(
                operation.Identity,
                operation.Fingerprint,
                DateTimeOffset.UnixEpoch));
        }

        public ValueTask RecordAppliedStateAsync(
            PhysicalSchemaAppliedState state,
            string? expectedAppliedTargetFingerprint,
            IPhysicalSchemaApplicationLock applicationLock,
            CancellationToken cancellationToken)
        {
            RecordAppliedStateCalls++;
            RecordedState = state;
            return ValueTask.CompletedTask;
        }

        private sealed class NoOpLock(PhysicalSchemaTargetIdentity target) : IPhysicalSchemaApplicationLock
        {
            public PhysicalSchemaTargetIdentity Target => target;
            public CancellationToken OwnershipLost => CancellationToken.None;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // Deployment-source fakes.

    private sealed class EmptyDeployment : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes => [];
    }

    private sealed class DuplicateDeployment : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
            [typeof(RuntimeGroundworkStorageManifestSource), typeof(RuntimeGroundworkStorageManifestSource)];
    }

    private sealed class NonManifestSourceDeployment : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes => [typeof(string)];
    }

    private sealed class NonConstructibleDeployment : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes => [typeof(NonConstructibleManifestSource)];
    }

    private sealed class NullNamingDeployment : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
            [typeof(RuntimeGroundworkStorageManifestSource)];

        protected override GroundworkStorageNamingPolicyOptions CreateStorageNamingPolicy() => null!;
    }

    private sealed class IdentityNamingDeployment : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
            [typeof(RuntimeGroundworkStorageManifestSource)];
    }

    private sealed class HostPrefixDeployment : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
            [typeof(RuntimeGroundworkStorageManifestSource)];

        protected override GroundworkStorageNamingPolicyOptions CreateStorageNamingPolicy() =>
            new("host-prefix-v1", context => $"host_{context.FeatureDefaultLogicalName}");
    }

    /// <summary>Concrete manifest source whose only constructor takes a parameter.</summary>
    private sealed class NonConstructibleManifestSource(int marker) : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => $"non-constructible-{marker}";

        public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Never reached: this source is rejected before activation.");
    }
}
