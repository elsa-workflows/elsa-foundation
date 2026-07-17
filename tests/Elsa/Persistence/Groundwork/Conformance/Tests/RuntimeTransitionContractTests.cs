using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class RuntimeTransitionContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(5);

    public static TheoryData<string> Providers => new()
    {
        "sqlite",
        "sqlserver",
        "postgresql",
        "mongodb"
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task DurableTimer_CreateClaimExpiryAndStaleCompletionAreConditionalOnEveryProvider(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new RuntimeTransitionManifestSource()]);

        await using var firstClient = await driver.OpenPhysicalClientAsync();
        await using var secondClient = await driver.OpenPhysicalClientAsync();
        var first = new GroundworkDurableTimerStore(
            firstClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            firstClient.BoundedDocumentStore);
        var second = new GroundworkDurableTimerStore(
            secondClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            secondClient.BoundedDocumentStore);

        var original = Timer("original");
        var competing = Timer("competing");
        var stored = await Task.WhenAll(
            first.SaveAsync(original).AsTask(),
            second.SaveAsync(competing).AsTask());
        Assert.Single(stored.Select(timer => timer.StimulusHash).Distinct(StringComparer.Ordinal));

        var initialClaims = await Task.WhenAll(
            first.ClaimDueAsync(ClaimRequest("owner-a", Now)).AsTask(),
            second.ClaimDueAsync(ClaimRequest("owner-b", Now)).AsTask());
        var initial = Assert.Single(initialClaims.SelectMany(claims => claims));

        await using var restartedClient = await driver.OpenPhysicalClientAsync();
        var restarted = new GroundworkDurableTimerStore(
            restartedClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            restartedClient.BoundedDocumentStore);
        var reclaimed = Assert.Single(await restarted.ClaimDueAsync(
            ClaimRequest("owner-restarted", Now.Add(VisibilityTimeout))));
        Assert.True(reclaimed.FencingToken > initial.FencingToken);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Stale,
            (await first.CompleteClaimAsync(initial)).Status);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await restarted.CompleteClaimAsync(reclaimed)).Status);
        Assert.Null(await restarted.FindAsync("wf-timer-transition", "timer-1"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task RecurringSchedule_AdvanceHasOneCASWinnerOnEveryProvider(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new RuntimeTransitionManifestSource()]);

        await using var firstClient = await driver.OpenPhysicalClientAsync();
        await using var secondClient = await driver.OpenPhysicalClientAsync();
        var first = new GroundworkRecurringTriggerScheduleStore(
            firstClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            firstClient.BoundedDocumentStore);
        var second = new GroundworkRecurringTriggerScheduleStore(
            secondClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            secondClient.BoundedDocumentStore);
        var schedule = await first.SaveAsync(new RecurringTriggerSchedule(
            "schedule-1",
            "artifact-1",
            "Timer",
            "schedule-hash",
            RecurringScheduleKind.Interval,
            "PT5M",
            Now.AddMinutes(-1),
            Now.AddMinutes(-2)));
        var next = Now.AddMinutes(5);

        var outcomes = await Task.WhenAll(
            first.TryAdvanceAsync(schedule.ScheduleId, schedule.NextOccurrence, next).AsTask(),
            second.TryAdvanceAsync(schedule.ScheduleId, schedule.NextOccurrence, next).AsTask());

        Assert.Single(outcomes.Where(outcome => outcome));
        Assert.Equal(next, (await first.FindAsync(schedule.ScheduleId))!.NextOccurrence);
    }

    private static RuntimeDurableTimerClaimRequest ClaimRequest(string ownerId, DateTimeOffset now) =>
        new(ownerId, now, VisibilityTimeout, limit: 1);

    private static DurableTimer Timer(string stimulusHash) =>
        new(
            "timer-1",
            "wf-timer-transition",
            "DurableTimer",
            stimulusHash,
            Now.AddMinutes(-1),
            Now.AddMinutes(-2));

    private sealed class RuntimeTransitionManifestSource : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => "runtime-transition-conformance";

        public async ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            var runtime = await new RuntimeGroundworkStorageManifestSource()
                .CreateDeclarationAsync(cancellationToken);
            var transitionManifest = runtime.Manifest with
            {
                StorageUnits = runtime.Manifest.StorageUnits
                    .Where(unit =>
                        StringComparer.Ordinal.Equals(
                            unit.Identity.Value,
                            ElsaRuntimeStorageManifest.DurableTimerDocumentKind) ||
                        StringComparer.Ordinal.Equals(
                            unit.Identity.Value,
                            ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind))
                    .ToArray()
            };

            return new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                transitionManifest,
                [],
                [],
                [],
                []);
        }
    }
}
