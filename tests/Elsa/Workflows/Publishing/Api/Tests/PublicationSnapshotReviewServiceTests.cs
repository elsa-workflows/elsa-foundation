using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublicationSnapshotReviewServiceTests
{
    private readonly PublicationSnapshotReviewService _service = new(TimeProvider.System);
    private readonly WorkflowDefinitionState _state = new([], null, [], [], null, null);

    [Fact]
    public void Candidate_hash_is_stable_across_opaque_layout_property_order()
    {
        var first = Layout("""{"zoom":1,"label":"test"}""");
        var second = Layout("""{"label":"test","zoom":1}""");

        Assert.Equal(_service.ComputeCandidateHash(_state, first), _service.ComputeCandidateHash(_state, second));
    }

    [Fact]
    public void Token_is_single_use_and_rejects_authority_changes()
    {
        var hash = _service.ComputeCandidateHash(_state, []);
        var plan = Plan(slotRevision: 3, activePublicationId: "publication-1");
        var issued = _service.Issue(hash, plan);

        _service.ValidateAndConsume(issued.PreflightToken, hash, plan);

        Assert.Throws<PublicationSnapshotReviewException>(() =>
            _service.ValidateAndConsume(issued.PreflightToken, hash, plan));

        var changed = Plan(slotRevision: 4, activePublicationId: "publication-2");
        var fresh = _service.Issue(hash, plan);
        Assert.Throws<PublicationSnapshotReviewException>(() =>
            _service.ValidateAndConsume(fresh.PreflightToken, hash, changed));
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
}
