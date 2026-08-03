using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublicationSnapshotReviewServiceTests
{
    private readonly PublicationSnapshotReviewService _service = new(TimeProvider.System, new InMemoryPublicationSnapshotReviewStore());
    private readonly WorkflowDefinitionState _state = WorkflowDefinitionState.Empty;

    [Fact]
    public void Candidate_hash_is_stable_across_opaque_layout_property_order()
    {
        var first = Layout("""{"zoom":1,"label":"test"}""");
        var second = Layout("""{"label":"test","zoom":1}""");

        Assert.Equal(_service.ComputeCandidateHash(_state, first), _service.ComputeCandidateHash(_state, second));
    }

    [Fact]
    public async Task Token_is_single_use_and_rejects_authority_changes()
    {
        var hash = _service.ComputeCandidateHash(_state, []);
        var plan = Plan(slotRevision: 3, activePublicationId: "publication-1");
        var issued = await _service.IssueAsync(hash, plan);

        await _service.ValidateAndConsumeAsync(issued.PreflightToken, hash, plan);

        await Assert.ThrowsAsync<PublicationSnapshotReviewException>(() =>
            _service.ValidateAndConsumeAsync(issued.PreflightToken, hash, plan).AsTask());

        var changed = Plan(slotRevision: 4, activePublicationId: "publication-2");
        var fresh = await _service.IssueAsync(hash, plan);
        await Assert.ThrowsAsync<PublicationSnapshotReviewException>(() =>
            _service.ValidateAndConsumeAsync(fresh.PreflightToken, hash, changed).AsTask());
    }

    [Fact]
    public async Task Token_is_bound_to_tenant_and_tampering_is_rejected()
    {
        var hash = _service.ComputeCandidateHash(_state, []);
        var issued = await _service.IssueAsync(hash, Plan(0, null!), tenantId: "tenant-a");

        await Assert.ThrowsAsync<PublicationSnapshotReviewException>(() =>
            _service.GetAsync(issued.PreflightToken, "tenant-b").AsTask());
        await Assert.ThrowsAsync<PublicationSnapshotReviewException>(() =>
            _service.GetAsync(issued.PreflightToken + "x", "tenant-a").AsTask());
    }

    [Fact]
    public async Task Token_issued_by_one_replica_is_consumed_once_by_another()
    {
        var reviews = new InMemoryPublicationSnapshotReviewStore();
        var firstReplica = new PublicationSnapshotReviewService(TimeProvider.System, reviews);
        var secondReplica = new PublicationSnapshotReviewService(TimeProvider.System, reviews);
        var hash = firstReplica.ComputeCandidateHash(_state, []);
        var plan = Plan(3, "publication-1");
        var issued = await firstReplica.IssueAsync(hash, plan, tenantId: "tenant-a");

        await secondReplica.ValidateAndConsumeAsync(issued.PreflightToken, hash, plan, "tenant-a");
        await Assert.ThrowsAsync<PublicationSnapshotReviewException>(() =>
            firstReplica.ValidateAndConsumeAsync(issued.PreflightToken, hash, plan, "tenant-a").AsTask());
    }

    [Fact]
    public async Task Expired_abandoned_tokens_are_rejected_and_cleaned_before_capacity_is_reused()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-13T12:00:00Z"));
        var reviews = new InMemoryPublicationSnapshotReviewStore(capacity: 1);
        var service = new PublicationSnapshotReviewService(clock, reviews);
        var hash = service.ComputeCandidateHash(_state, []);
        var plan = Plan(0, null!);
        var expired = await service.IssueAsync(hash, plan);
        clock.Advance(TimeSpan.FromMinutes(16));

        await Assert.ThrowsAsync<PublicationSnapshotReviewException>(() =>
            service.GetAsync(expired.PreflightToken).AsTask());
        var replacement = await service.IssueAsync(hash, plan);

        Assert.NotEqual(expired.PreflightToken, replacement.PreflightToken);
    }

    private static DesignMetadataRecord[] Layout(string additionalProperties) =>
        [new("node-1", 10, 20, AdditionalProperties: JsonDocument.Parse(additionalProperties).RootElement.Clone())];

    private static WorkflowPublicationPreflightPlan Plan(long slotRevision, string activePublicationId) =>
        new(
            new ResolvedPublicationAction(
                "definition-1", "snapshot", PublicationAction.Replace, "default",
                PublicationPolicySource.Workflow, PolicyRevision: 7),
            new PublicationSlot("slot-1", "definition-1", "default", activePublicationId, slotRevision, DateTimeOffset.UnixEpoch),
            new PublicationPreflightResult(true, [], []),
            []);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
