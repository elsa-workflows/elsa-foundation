using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Xunit;
using Candidate = Elsa.Workflows.Publishing.Api.Tests.ActivityPublicationPreflightParityTests.Candidate;
using Scenario = Elsa.Workflows.Publishing.Api.Tests.ActivityPublicationPreflightParityTests.Scenario;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// A publish request may name the version id to commit (spec 246, used by the Elsa 3 import so its wrappers and callers
/// keep the ids they reference). Without one, the publisher generates the id as before. A requested id already taken
/// by another version is refused by the commit (<c>EfActivityPublicationCommandTests</c>).
/// </summary>
public sealed class ActivityDefinitionPublisherRequestedVersionTests
{
    [Fact]
    public async Task A_requested_version_id_is_the_id_every_committed_row_carries()
    {
        var scenario = new Scenario(Candidate.Publishable);

        var receipt = await scenario.PublishReviewedAsync("activity-version-imported", "publish-1");

        Assert.Equal(ActivityPublicationReceiptStatus.Applied, receipt.Status);
        Assert.Equal("activity-version-imported", receipt.Outcome!.DefinitionVersionId);
        var commit = Assert.Single(scenario.Commits);
        Assert.Equal("activity-version-imported", commit.CatalogVersion.Id);
        Assert.Equal("activity-version-imported", commit.Publication.DefinitionVersionId);
        Assert.Equal("activity-version-imported", commit.Layout.DefinitionVersionId);
    }

    [Fact]
    public async Task Without_a_requested_id_the_publisher_generates_one()
    {
        var scenario = new Scenario(Candidate.Publishable);

        var receipt = await scenario.PublishReviewedAsync(null, "publish-1");

        Assert.StartsWith("activity-ver-", receipt.Outcome!.DefinitionVersionId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_requested_id_is_a_malformed_request(string requestedVersionId)
    {
        var scenario = new Scenario(Candidate.Publishable);

        var rejection = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            scenario.PublishReviewedAsync(requestedVersionId, "publish-1"));

        Assert.Equal(ActivityErrorCodes.RequestInvalid, rejection.ErrorCode);
        Assert.Empty(scenario.Commits);
    }

    [Fact]
    public async Task A_requested_id_longer_than_the_id_column_is_a_malformed_request()
    {
        var scenario = new Scenario(Candidate.Publishable);

        var rejection = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            scenario.PublishReviewedAsync(new string('v', 451), "publish-1"));

        Assert.Equal(ActivityErrorCodes.RequestInvalid, rejection.ErrorCode);
    }

    [Fact]
    public async Task A_replay_naming_the_committed_id_returns_the_applied_receipt()
    {
        var scenario = new Scenario(Candidate.Publishable);
        await scenario.PublishReviewedAsync("activity-version-imported", "publish-1");

        var replay = await scenario.PublishReviewedAsync("activity-version-imported", "publish-1");

        Assert.Equal(ActivityPublicationReceiptStatus.Applied, replay.Status);
        Assert.Single(scenario.Commits);
    }

    [Fact]
    public async Task A_replay_naming_another_id_is_an_idempotency_conflict()
    {
        var scenario = new Scenario(Candidate.Publishable);
        await scenario.PublishReviewedAsync("activity-version-imported", "publish-1");

        var rejection = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            scenario.PublishReviewedAsync("activity-version-other", "publish-1"));

        Assert.Equal("activity.publication.idempotency-conflict", rejection.ErrorCode);
        Assert.Single(scenario.Commits);
    }
}
