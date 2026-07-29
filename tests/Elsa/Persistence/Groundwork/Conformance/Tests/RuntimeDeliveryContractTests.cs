using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
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
        await driver.ResetPhysicalAsync([
            new RuntimeDeliveryManifestSource(ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind)
        ]);

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

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Outbox_claim_expiry_retry_and_stale_ack_are_fenced_on_every_provider(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([
            new RuntimeDeliveryManifestSource(ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind)
        ]);

        await using var firstClient = await driver.OpenPhysicalClientAsync();
        await using var secondClient = await driver.OpenPhysicalClientAsync();
        var first = Outbox(firstClient);
        var second = Outbox(secondClient);
        await first.SavePendingAsync(PendingOutbox());
        Assert.Empty(await first.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "owner-filter-probe",
            Now,
            VisibilityTimeout,
            limit: 1,
            workflowExecutionId: "other-workflow",
            intentKind: "delivery-contract")));

        var initialClaims = await Task.WhenAll(
            first.ClaimAsync(OutboxClaimRequest("owner-a", Now)).AsTask(),
            second.ClaimAsync(OutboxClaimRequest("owner-b", Now)).AsTask());
        var initial = Assert.Single(initialClaims.SelectMany(claims => claims));
        Assert.Empty(await first.ClaimAsync(OutboxClaimRequest("owner-c", Now.AddMinutes(1))));

        await using var restartedClient = await driver.OpenPhysicalClientAsync();
        var restarted = Outbox(restartedClient);
        var reclaimedAt = Now.Add(VisibilityTimeout).AddSeconds(1);
        var reclaimed = Assert.Single(await restarted.ClaimAsync(
            OutboxClaimRequest("owner-restarted", reclaimedAt)));
        Assert.True(reclaimed.FencingToken > initial.FencingToken);

        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
            first.RecordDeliveryResultAsync(
                initial,
                DeliveryResult(RuntimePostCommitOutboxStatus.Delivered, reclaimedAt)).AsTask());

        await restarted.RecordDeliveryResultAsync(
            reclaimed,
            DeliveryResult(RuntimePostCommitOutboxStatus.FailedRetryable, reclaimedAt));
        var retryable = Assert.IsType<RuntimePostCommitOutboxItem>(
            await restarted.FindAsync("outbox-1"));
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, retryable.Status);
        Assert.Equal(1, retryable.DeliveryAttemptCount);
        Assert.Equal(reclaimedAt.AddMinutes(2), retryable.AvailableAt);
        var retryAt = retryable.AvailableAt
            ?? throw new InvalidOperationException("A retryable outbox item must have a next availability time.");
        Assert.Empty(await first.ClaimAsync(
            OutboxClaimRequest("owner-a", retryAt.AddTicks(-1))));

        var retry = Assert.Single(await first.ClaimAsync(
            OutboxClaimRequest("owner-a", retryAt)));
        Assert.True(retry.FencingToken > reclaimed.FencingToken);
        await first.RecordDeliveryResultAsync(
            retry,
            DeliveryResult(RuntimePostCommitOutboxStatus.Delivered, retryAt));

        var delivered = Assert.IsType<RuntimePostCommitOutboxItem>(
            await restarted.FindAsync("outbox-1"));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Equal(2, delivered.DeliveryAttemptCount);
        Assert.Empty(await restarted.ClaimAsync(
            OutboxClaimRequest("owner-restarted", Now.AddHours(1))));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Scheduler_poison_record_survives_restart_on_every_provider(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([
            new RuntimeDeliveryManifestSource(ElsaRuntimeStorageManifest.SchedulerPoisonDocumentKind)
        ]);

        await using (var firstClient = await driver.OpenPhysicalClientAsync())
        {
            await Poison(firstClient).RecordAsync(PoisonRecord());
        }

        await using var restartedClient = await driver.OpenPhysicalClientAsync();
        var restarted = Poison(restartedClient);
        var recovered = Assert.IsType<RuntimeSchedulerPoisonRecord>(
            await restarted.FindAsync(WorkflowExecutionId, "work-poison"));

        Assert.Equal(3, recovered.FailureCount);
        Assert.Equal(RuntimeSchedulerPoisonDisposition.RetryScheduled, recovered.Disposition);
        Assert.Equal(Now.AddMinutes(2), recovered.NextRetryAt);
        Assert.Equal("delivery-contract", recovered.Metadata["source"]);
        Assert.Equal("work-poison", Assert.Single(
            await restarted.ListAsync(WorkflowExecutionId)).WorkItemId);
    }

    private static GroundworkWorkflowSchedulerWorkQueue Queue(GroundworkProviderClient client) =>
        new(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            client.BoundedDocumentStore ??
            throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));

    private static GroundworkRuntimePostCommitOutboxStore Outbox(GroundworkProviderClient client) =>
        new(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            client.BoundedDocumentStore ??
            throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));

    private static GroundworkWorkflowSchedulerPoisonStore Poison(GroundworkProviderClient client) =>
        new(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            client.BoundedDocumentStore ??
            throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));

    private static RuntimeSchedulerWorkClaimRequest ClaimRequest(string ownerId, DateTimeOffset now) =>
        new(WorkflowExecutionId, ownerId, now, VisibilityTimeout);

    private static RuntimePostCommitOutboxClaimRequest OutboxClaimRequest(
        string ownerId,
        DateTimeOffset now) =>
        new(ownerId, now, VisibilityTimeout, limit: 1);

    private static RuntimePostCommitOutboxItem PendingOutbox() =>
        new(
            "outbox-1",
            new RuntimePostCommitIntent(
                "intent-outbox-1",
                WorkflowExecutionId,
                "delivery-contract",
                Now,
                activityExecutionId: null,
                idempotencyKey: "delivery-contract:outbox-1",
                payload: null),
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            Now,
            new RuntimePostCommitRetryPolicy(maxAttempts: 3, delay: TimeSpan.FromMinutes(2)));

    private static RuntimePostCommitOutboxDeliveryResult DeliveryResult(
        RuntimePostCommitOutboxStatus status,
        DateTimeOffset recordedAt) =>
        new("outbox-1", status, recordedAt, status == RuntimePostCommitOutboxStatus.Delivered ? null : "retry");

    private static RuntimeSchedulerPoisonRecord PoisonRecord() =>
        new(
            WorkflowExecutionId,
            "work-poison",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            "delivery-contract-handler",
            new RuntimeFaultInfo("System.InvalidOperationException", "boom", "stack"),
            failureCount: 3,
            RuntimeSchedulerPoisonDisposition.RetryScheduled,
            Now,
            Now.AddMinutes(1),
            Now.AddMinutes(2),
            new Dictionary<string, string> { ["source"] = "delivery-contract" });

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

    private sealed class RuntimeDeliveryManifestSource(params string[] documentKinds)
        : IGroundworkStorageManifestSource
    {
        private readonly HashSet<string> _documentKinds = documentKinds.ToHashSet(StringComparer.Ordinal);

        public string FeatureIdentity => "runtime-delivery-conformance";

        public async ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            var runtime = await new RuntimeGroundworkStorageManifestSource()
                .CreateDeclarationAsync(cancellationToken);
            var deliveryManifest = runtime.Manifest with
            {
                StorageUnits = runtime.Manifest.StorageUnits
                    .Where(unit => _documentKinds.Contains(unit.Identity.Value))
                    .ToArray()
            };

            return new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                deliveryManifest,
                [],
                [],
                [],
                []);
        }
    }
}
