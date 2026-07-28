using Elsa.Workflows.Design.Persistence.Core.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class WorkflowVersionNumberingTests
{
    [Fact]
    public void AssessPromotion_accepts_a_trimmed_forward_prerelease()
    {
        var assessment = WorkflowVersionNumbering.AssessPromotion("2.0.0", " 2.1.0-rc.1 ", versionIdentityExists: false);

        Assert.True(assessment.IsReady);
        Assert.Equal("exact", assessment.AssignmentMode);
        Assert.Equal("2.1.0-rc.1", assessment.RequestedVersion);
        Assert.Equal("2.1.0-rc.1", assessment.ResolvedVersion);
        Assert.Empty(assessment.Issues);
    }

    [Theory]
    [InlineData("2.0.0", "not-semver", "invalid-version")]
    [InlineData("2.0.0", "2.0.0", "not-forward")]
    [InlineData("2.0.0", "1.9.9", "not-forward")]
    public void AssessPromotion_rejects_malformed_or_non_forward_exact_requests(
        string latestVersion,
        string requestedVersion,
        string expectedIssue)
    {
        var assessment = WorkflowVersionNumbering.AssessPromotion(latestVersion, requestedVersion, versionIdentityExists: false);

        Assert.False(assessment.IsReady);
        Assert.Equal(expectedIssue, Assert.Single(assessment.Issues).Code);
        Assert.Null(assessment.ResolvedVersion);
    }

    [Fact]
    public void AssessPromotion_rejects_a_build_metadata_variant_of_an_existing_identity()
    {
        var assessment = WorkflowVersionNumbering.AssessPromotion("2.0.0", "2.0.0+build.7", versionIdentityExists: true);

        Assert.False(assessment.IsReady);
        Assert.Equal("version-conflict", Assert.Single(assessment.Issues).Code);
        Assert.Null(assessment.ResolvedVersion);
    }

    [Fact]
    public void AssessPromotion_classifies_the_exact_latest_label_as_non_forward_even_when_its_identity_exists()
    {
        var assessment = WorkflowVersionNumbering.AssessPromotion("2.0.0", "2.0.0", versionIdentityExists: true);

        Assert.False(assessment.IsReady);
        Assert.Equal("not-forward", Assert.Single(assessment.Issues).Code);
        Assert.Null(assessment.ResolvedVersion);
    }

    [Fact]
    public void AssessPromotion_preserves_automatic_next_major_assignment()
    {
        var assessment = WorkflowVersionNumbering.AssessPromotion("2.0.0", requestedVersion: null, versionIdentityExists: false);

        Assert.True(assessment.IsReady);
        Assert.Equal("automatic", assessment.AssignmentMode);
        Assert.Null(assessment.RequestedVersion);
        Assert.Equal("3.0.0", assessment.ResolvedVersion);
    }
}
