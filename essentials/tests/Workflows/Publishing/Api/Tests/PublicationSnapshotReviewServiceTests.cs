using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublicationSnapshotReviewServiceTests
{
    private readonly PublicationSnapshotReviewService _service = new(TimeProvider.System, new InMemoryPublicationSnapshotReviewStore());
    private readonly WorkflowDefinitionState _state = WorkflowDefinitionState.Empty;

    [Fact]
    public void Empty_candidate_has_a_fixed_first_stable_hash()
    {
        // SHA-256 independently pinned from the canonical empty layout/state JSON projection.
        Assert.Equal("sha256:eb53c973042b5f07e7fb6a0d30beec54afb06efbd225a90ded92a95a73c85c12", _service.ComputeCandidateHash(_state, []));
    }

    [Fact]
    public void Candidate_hash_is_stable_across_opaque_layout_property_order()
    {
        var first = Layout("""{"zoom":1.0,"label":"\u0074est"}""");
        var second = Layout("""{"label":"test","zoom":1e0}""");

        Assert.Equal(_service.ComputeCandidateHash(_state, first), _service.ComputeCandidateHash(_state, second));
    }

    [Fact]
    public void Candidate_hash_distinguishes_exact_data_and_opaque_node_identity()
    {
        var first = Layout("""{"n":100000000000000000000000000000000000001,"nil":null,"items":[1,2]}""");
        foreach (var changed in new[]
                 {
                     """{"n":100000000000000000000000000000000000002,"nil":null,"items":[1,2]}""",
                     """{"n":100000000000000000000000000000000000001,"items":[1,2]}""",
                     """{"n":100000000000000000000000000000000000001,"nil":null,"items":[2,1]}""",
                     """{"N":100000000000000000000000000000000000001,"nil":null,"items":[1,2]}"""
                 })
            Assert.NotEqual(_service.ComputeCandidateHash(_state, first), _service.ComputeCandidateHash(_state, Layout(changed)));
        Assert.NotEqual(_service.ComputeCandidateHash(_state, [first[0] with { NodeId = "node\ud800" }]),
            _service.ComputeCandidateHash(_state, [first[0] with { NodeId = "node\ufffd" }]));
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
            new WorkflowActivationSlot(
                WorkflowActivationSlotIdentity.Create("definition-1", "default"),
                "definition-1", "default", activePublicationId, WorkflowActivationSource.Publishing,
                slotRevision, DateTimeOffset.UnixEpoch),
            new PublicationPreflightResult(true, [], []),
            []);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
