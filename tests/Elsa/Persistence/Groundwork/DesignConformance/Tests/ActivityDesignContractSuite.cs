using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Persistence.Core;
using Elsa.Primitives.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Provider-neutral activity-design conformance scenarios. Concrete fixtures own reconciliation
/// host wiring and storage materialization; this suite observes only core contracts and outcomes.
/// </summary>
public abstract class ActivityDesignContractSuite
{
    protected abstract Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default);

    [Fact]
    public async Task Definition_and_initial_version_round_trip_across_restart()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            await scope.ServiceProvider.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.ActivityDefinition(),
                DesignPersistenceFixtureData.ActivityVersion(),
                CancellationToken.None);
        }

        var beforeRestart = await ReadActivitySnapshotAsync(fixture);
        Assert.Equal(
            DesignPersistenceFixtureData.ResultHash(DesignPersistenceFixtureData.ActivityVersion().DescriptorPayload),
            DesignPersistenceFixtureData.ResultHash(beforeRestart.Version.DescriptorPayload));

        await fixture.RestartAsync();
        var afterRestart = await ReadActivitySnapshotAsync(fixture);

        Assert.Equal(DesignPersistenceFixtureData.ResultHash(beforeRestart), DesignPersistenceFixtureData.ResultHash(afterRestart));
        Assert.Equal(DesignPersistenceFixtureData.ActivityDefinitionId, afterRestart.Definition.Id);
        Assert.Equal(DesignPersistenceFixtureData.ActivityVersionId, afterRestart.Version.Id);
    }

    [Fact]
    public async Task Versions_preserve_semver_identity_and_missing_outcomes()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
            DesignPersistenceFixtureData.ActivityDefinition(),
            DesignPersistenceFixtureData.ActivityVersion(),
            CancellationToken.None);

        var nextVersion = DesignPersistenceFixtureData.ActivityVersion("2.0.0", "activity-http-request-v2");
        await services.GetRequiredService<IAddCommand<ActivityDefinitionVersion>>().Add(nextVersion);

        var definitions = services.GetRequiredService<IActivityDefinitionStore>();
        var versions = services.GetRequiredService<IActivityDefinitionVersionStore>();
        var exact = await versions.FindByDefinitionAndSortKeyAsync(
            DesignPersistenceFixtureData.ActivityDefinitionId,
            SemVer.ToSortKey("2.0.0"));

        Assert.NotNull(exact);
        Assert.Equal(nextVersion.Id, exact!.Id);
        Assert.Equal("2.0.0", exact.Version);
        Assert.Equal(
            DesignPersistenceFixtureData.ActivityVersionId,
            (await versions.FindByDefinitionAndSortKeyAsync(
                DesignPersistenceFixtureData.ActivityDefinitionId,
                SemVer.ToSortKey("1.0.0+build.42")))!.Id);
        Assert.True(await definitions.ExistsByActivityTypeKeyAsync(DesignPersistenceFixtureData.ActivityDefinition().ActivityTypeKey));
        Assert.Null(await definitions.FindAsync(new ActivityDefinitionFilter { Id = "missing-activity-definition" }));
        Assert.Null(await versions.FindByDefinitionAndSortKeyAsync(
            DesignPersistenceFixtureData.ActivityDefinitionId,
            SemVer.ToSortKey("9.9.9")));
    }

    [Fact]
    public async Task Reconciliation_is_idempotent_after_restart()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var candidate = DesignPersistenceFixtureData.ReconciledActivityVersion();
        await fixture.StageActivityReconciliationCandidatesAsync(DesignPersistenceFixtureData.ScopeA, [candidate]);

        using (var beforeScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var services = beforeScope.ServiceProvider;
            Assert.Null(await services.GetRequiredService<IActivityDefinitionStore>()
                .FindAsync(new ActivityDefinitionFilter { Id = candidate.DefinitionId }));
            Assert.DoesNotContain(
                await services.GetRequiredService<IActivityDefinitionVersionStore>().ListAsync(),
                version => version.Id == candidate.Id);
        }

        fixture.ClearObservedEvents();
        await ReconcileAsync(fixture);
        await AssertReconciliationEventAsync(fixture, candidate);
        var first = await ReadReconciledSnapshotAsync(fixture, candidate);
        Assert.Equal(
            DesignPersistenceFixtureData.ResultHash(candidate.DescriptorPayload),
            DesignPersistenceFixtureData.ResultHash(first.Activity.Version.DescriptorPayload));

        await fixture.RestartAsync();
        await fixture.StageActivityReconciliationCandidatesAsync(DesignPersistenceFixtureData.ScopeA, [candidate]);
        fixture.ClearObservedEvents();
        await ReconcileAsync(fixture);
        await AssertReconciliationEventAsync(fixture, candidate);
        var second = await ReadReconciledSnapshotAsync(fixture, candidate);

        Assert.Equal(DesignPersistenceFixtureData.ResultHash(first), DesignPersistenceFixtureData.ResultHash(second));
        Assert.Equal(1, second.VersionCount);
    }

    private static async Task ReconcileAsync(IDesignPersistenceContractFixture fixture)
    {
        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        await scope.ServiceProvider.GetRequiredService<IActivityVersionReconciler>().Reconcile(CancellationToken.None);
    }

    private static async Task AssertReconciliationEventAsync(
        IDesignPersistenceContractFixture fixture,
        ActivityDefinitionVersion candidate)
    {
        var reconciling = Assert.Single((await fixture.ReadObservedEventsAsync()).OfType<OnActivityVersionsReconciling>());
        var observed = Assert.Single(reconciling.Versions);
        Assert.Equal(candidate.Id, observed.Id);
        Assert.Equal(candidate.DefinitionId, observed.DefinitionId);
        Assert.Equal(candidate.Hash, observed.Hash);
    }

    private static async Task<ActivitySnapshot> ReadActivitySnapshotAsync(IDesignPersistenceContractFixture fixture)
    {
        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var definition = await services.GetRequiredService<IActivityDefinitionStore>().GetAsync(DesignPersistenceFixtureData.ActivityDefinitionId);
        var versions = services.GetRequiredService<IActivityDefinitionVersionStore>();
        var version = await versions.GetWithDefinitionAsync(DesignPersistenceFixtureData.ActivityVersionId);
        var versionDefinition = version.Definition ?? throw new InvalidOperationException("The activity version store did not load the owning definition.");
        return CanonicalSnapshot(definition, version, versionDefinition);
    }

    private static async Task<ReconciledActivitySnapshot> ReadReconciledSnapshotAsync(
        IDesignPersistenceContractFixture fixture,
        ActivityDefinitionVersion candidate)
    {
        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var definition = await services.GetRequiredService<IActivityDefinitionStore>()
            .FindByIdOrActivityTypeKeyAsync(candidate.DefinitionId, candidate.Definition!.ActivityTypeKey);
        Assert.NotNull(definition);

        var versions = await services.GetRequiredService<IActivityDefinitionVersionStore>()
            .ListByDefinitionAsync(definition!.Id);
        var reconciled = Assert.Single(versions);
        return new ReconciledActivitySnapshot(
            CanonicalSnapshot(definition, reconciled),
            versions.Count);
    }

    internal static ActivitySnapshot CanonicalSnapshot(
        ActivityDefinition definition,
        ActivityDefinitionVersion version,
        ActivityDefinition? versionDefinition = null) =>
        new(
            CanonicalSnapshot(definition),
            CanonicalSnapshot(version),
            versionDefinition is null ? null : CanonicalSnapshot(versionDefinition));

    private static ActivityDefinitionSnapshot CanonicalSnapshot(ActivityDefinition definition) =>
        new(
            definition.Id,
            definition.CreatedAt,
            definition.LastModifiedAt,
            definition.TenantId,
            definition.ActivityTypeKey,
            definition.Category,
            definition.DisplayName,
            definition.Description);

    private static ActivityDefinitionVersionSnapshot CanonicalSnapshot(ActivityDefinitionVersion version) =>
        new(
            version.Id,
            version.CreatedAt,
            version.LastModifiedAt,
            version.TenantId,
            version.Version,
            version.SemVerSortKey,
            version.DefinitionId,
            version.ProviderKey,
            version.ProviderSchemaVersion,
            version.ConsumerKey,
            version.ConsumerSchemaVersion,
            version.DescriptorPayload,
            version.ExecutionType,
            version.Inputs.ToArray(),
            version.Outputs.ToArray(),
            version.DesignFacets.ToArray(),
            version.SourceKind,
            version.SourceId,
            version.Hash);

    internal sealed record ActivitySnapshot(
        ActivityDefinitionSnapshot Definition,
        ActivityDefinitionVersionSnapshot Version,
        ActivityDefinitionSnapshot? VersionDefinition);

    internal sealed record ActivityDefinitionSnapshot(
        string Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        string? TenantId,
        string ActivityTypeKey,
        string Category,
        string? DisplayName,
        string? Description);

    internal sealed record ActivityDefinitionVersionSnapshot(
        string Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        string? TenantId,
        string Version,
        string SemVerSortKey,
        string DefinitionId,
        string ProviderKey,
        string ProviderSchemaVersion,
        string ConsumerKey,
        string ConsumerSchemaVersion,
        JsonElement DescriptorPayload,
        ActivityExecutionType ExecutionType,
        IReadOnlyList<InputDefinition> Inputs,
        IReadOnlyList<OutputDefinition> Outputs,
        IReadOnlyList<ActivityDesignFacet> DesignFacets,
        string SourceKind,
        string SourceId,
        string? Hash);

    private sealed record ReconciledActivitySnapshot(
        ActivitySnapshot Activity,
        int VersionCount);
}
