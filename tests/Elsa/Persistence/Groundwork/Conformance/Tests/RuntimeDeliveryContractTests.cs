using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class RuntimeDeliveryContractTests
{
    private const string WorkflowExecutionId = "wf-scheduler-delivery-contract";
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
    public async Task Scheduler_claim_expiry_renewal_release_and_stale_ack_are_fenced_on_every_provider(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new SchedulerWorkManifestSource()]);

        await using var firstClient = await driver.OpenPhysicalClientAsync();
        await using var secondClient = await driver.OpenPhysicalClientAsync();
        var first = Queue(firstClient);
        var second = Queue(secondClient);
        await first.EnqueueAsync(NewWorkItem(1));
        await first.EnqueueAsync(NewWorkItem(2));
        Assert.Equal(
            [WorkflowExecutionId],
            await first.ListPendingWorkflowExecutionIdsAsync(10));

        var initialClaims = await Task.WhenAll(
            first.ClaimAsync(ClaimRequest("owner-a", Now)).AsTask(),
            second.ClaimAsync(ClaimRequest("owner-b", Now)).AsTask());
        var initial = Assert.Single(initialClaims.OfType<RuntimeSchedulerWorkClaim>());
        Assert.Equal("work-1", initial.Item.WorkItemId);
        Assert.Null(await second.ClaimAsync(ClaimRequest("owner-c", Now.AddMinutes(1))));

        await using var restartedClient = await driver.OpenPhysicalClientAsync();
        var restarted = Queue(restartedClient);
        var reclaimedAt = Now.Add(VisibilityTimeout).AddSeconds(1);
        var reclaimed = Assert.IsType<RuntimeSchedulerWorkClaim>(
            await restarted.ClaimAsync(ClaimRequest("owner-restarted", reclaimedAt)));
        Assert.True(reclaimed.FencingToken > initial.FencingToken);
        Assert.Equal(
            RuntimeSchedulerWorkClaimTransitionStatus.Stale,
            (await first.CompleteClaimAsync(initial)).Status);

        var renewal = await restarted.RenewClaimAsync(
            reclaimed,
            reclaimedAt.AddMinutes(1),
            VisibilityTimeout);
        var renewed = Assert.IsType<RuntimeSchedulerWorkClaim>(renewal.Claim);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, renewal.Status);
        Assert.True(renewed.Revision > reclaimed.Revision);
        Assert.Equal(
            RuntimeSchedulerWorkClaimTransitionStatus.Stale,
            (await restarted.CompleteClaimAsync(reclaimed)).Status);
        Assert.Equal(
            RuntimeSchedulerWorkClaimTransitionStatus.Succeeded,
            (await restarted.CompleteClaimAsync(renewed)).Status);
        Assert.Equal(
            RuntimeSchedulerWorkClaimTransitionStatus.AlreadyApplied,
            (await restarted.CompleteClaimAsync(renewed)).Status);

        var secondItemClaimedAt = reclaimedAt.AddMinutes(1);
        var secondItem = Assert.IsType<RuntimeSchedulerWorkClaim>(
            await restarted.ClaimAsync(ClaimRequest("owner-restarted", secondItemClaimedAt)));
        Assert.Equal("work-2", secondItem.Item.WorkItemId);
        var visibleAt = secondItemClaimedAt.AddMinutes(2);
        Assert.Equal(
            RuntimeSchedulerWorkClaimTransitionStatus.Succeeded,
            (await restarted.ReleaseClaimAsync(secondItem, visibleAt)).Status);
        Assert.Null(await first.ClaimAsync(ClaimRequest("owner-a", visibleAt.AddTicks(-1))));

        var released = Assert.IsType<RuntimeSchedulerWorkClaim>(
            await first.ClaimAsync(ClaimRequest("owner-a", visibleAt)));
        Assert.True(released.FencingToken > secondItem.FencingToken);
        Assert.Equal(
            RuntimeSchedulerWorkClaimTransitionStatus.Succeeded,
            (await first.CompleteClaimAsync(released)).Status);
        Assert.Empty(await first.ListPendingWorkflowExecutionIdsAsync(10));
    }

    private static GroundworkWorkflowSchedulerWorkQueue Queue(GroundworkProviderClient client) =>
        new(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            client.BoundedDocumentStore ??
            throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));

    private static RuntimeSchedulerWorkClaimRequest ClaimRequest(string ownerId, DateTimeOffset now) =>
        new(WorkflowExecutionId, ownerId, now, VisibilityTimeout);

    private static RuntimeSchedulerWorkItem NewWorkItem(int index)
    {
        using var payload = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new RuntimeSchedulerWorkItem(
            workItemId: $"work-{index}",
            workflowExecutionId: WorkflowExecutionId,
            commandId: $"command-{index}",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{WorkflowExecutionId}:command-{index}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: index,
            payload: payload.RootElement.Clone());
    }

    private sealed class SchedulerWorkManifestSource : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => "runtime-scheduler-delivery-conformance";

        public async ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            var runtime = await new RuntimeGroundworkStorageManifestSource()
                .CreateDeclarationAsync(cancellationToken);
            var schedulerManifest = runtime.Manifest with
            {
                StorageUnits = runtime.Manifest.StorageUnits
                    .Where(unit => StringComparer.Ordinal.Equals(
                        unit.Identity.Value,
                        ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind))
                    .ToArray()
            };

            return new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                schedulerManifest,
                [],
                [],
                [],
                []);
        }
    }
}
