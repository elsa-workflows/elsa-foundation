using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Publishing.Releases.Core;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Tests;

/// <summary>Controlled generic owner answers exercise root-selection gates, not production source ownership.</summary>
public sealed class ReleaseSemanticRootTests
{
    [Fact]
    public async Task Selected_owner_explicitly_allows_its_verified_associated_content_and_action()
    {
        var fixture = AssociatedSelection(2);
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("wrong-content")]
    [InlineData("cross-kind-self-content")]
    [InlineData("wrong-action")]
    [InlineData("omitted-capability")]
    [InlineData("dependency-as-root")]
    public async Task Changing_requested_root_does_not_change_the_selected_owners_allowed_pairs(string change)
    {
        var fixture = AssociatedSelection(3);
        var original = fixture.Bundle.Validate();
        Assert.Equal(ReleaseSemanticStatus.Verified, (await fixture.Verifier().VerifyAsync(original)).Status);
        var root = fixture.Bundle.Manifest["roots"]![0]!;
        switch (change)
        {
            case "wrong-content": root["content"] = ReleaseBundleFixture.Key(2); break;
            case "cross-kind-self-content": root["content"] = ReleaseBundleFixture.Key(0); break;
            case "wrong-action": root["action"] = "make-available"; break;
            case "omitted-capability": fixture.Set(fixture.First, rootCapabilities: []); break;
            case "dependency-as-root": fixture.Bundle.Manifest["roots"]!.AsArray().Add(new JsonObject
            {
                ["member"] = ReleaseBundleFixture.Key(1), ["content"] = ReleaseBundleFixture.Key(1), ["action"] = "activate-serving"
            }); break;
        }
        var changed = fixture.Bundle.Validate();
        if (change != "omitted-capability") Assert.NotEqual(original.Digest, changed.Digest);
        var result = await fixture.Verifier().VerifyAsync(changed);
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == "root_content_or_action_not_allowed");
    }

    [Fact]
    public async Task Unrelated_requested_content_never_becomes_an_independent_actual_graph_root()
    {
        var fixture = AssociatedSelection(3);
        fixture.Bundle.Manifest["edges"]!.AsArray().RemoveAt(1);
        fixture.Set(new("etl-artifact", "member-1"), edges: []);
        fixture.Bundle.Manifest["roots"]![0]!["content"] = ReleaseBundleFixture.Key(2);
        // The old structural envelope permits B as a content seed. Semantic owner facts only associate S with A.
        var transport = fixture.Bundle.Validate();
        var result = await fixture.Verifier().VerifyAsync(transport);
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == "root_content_or_action_not_allowed");
        Assert.Contains(result.Diagnostics, x => x.Code == "actual_unrelated_member");
    }

    [Fact]
    public async Task Allowed_nonself_content_still_requires_an_actual_edge_path_from_the_selection()
    {
        var fixture = AssociatedSelection(2); fixture.Set(fixture.First, edges: []);
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == "actual_unrelated_member");
    }

    [Fact]
    public async Task Same_member_root_requires_an_explicit_allowed_action()
    {
        var fixture = new ReleaseSemanticFixture(1); fixture.Set(fixture.First, rootCapabilities: []);
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == "root_content_or_action_not_allowed");
    }

    [Theory]
    [InlineData(ReleaseSemanticStatus.Incomplete)]
    [InlineData(ReleaseSemanticStatus.Unsupported)]
    public async Task Unverified_selected_owner_keeps_its_explicit_status_without_implied_root_success(ReleaseSemanticStatus status)
    {
        var fixture = new ReleaseSemanticFixture(1); fixture.Set(fixture.First, status: status, rootCapabilities: []);
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(status, result.Status);
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "root_content_or_action_not_allowed");
    }

    [Theory]
    [InlineData(ReleaseSemanticStatus.Verified)]
    [InlineData(ReleaseSemanticStatus.Incomplete)]
    [InlineData(ReleaseSemanticStatus.Unsupported)]
    public async Task Capability_to_missing_content_is_invalid_even_from_an_unverified_answer(ReleaseSemanticStatus status)
    {
        var fixture = new ReleaseSemanticFixture(1);
        fixture.Set(fixture.First, status: status, rootCapabilities: [new(new("missing", "unlisted"), ReleaseRootAction.ActivateServing)]);
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == "root_capability_content_missing");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("unknown")]
    [InlineData("null")]
    [InlineData("duplicate")]
    [InlineData("limit")]
    public async Task Invalid_duplicate_or_excessive_owner_capabilities_fail_closed(string change)
    {
        var fixture = new ReleaseSemanticFixture(1); var allowed = new ReleaseRootCapability(fixture.First, ReleaseRootAction.ActivateServing);
        fixture.Set(fixture.First, rootCapabilities: change switch
        {
            "none" => [allowed with { Action = ReleaseRootAction.None }],
            "unknown" => [allowed with { Action = (ReleaseRootAction)999 }],
            "null" => [null!],
            "limit" => [allowed, allowed with { Action = ReleaseRootAction.MakeAvailable }],
            _ => [allowed, allowed]
        });
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate(), change == "limit" ? new() { TransportLimits = new() { MaxEdges = 1 } } : null);
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
    }

    [Fact]
    public void Root_capability_collection_is_an_immutable_snapshot()
    {
        var member = new ReleaseMemberKey("selected", "S"); var content = new ReleaseMemberKey("content", "A");
        var values = new[] { new ReleaseRootCapability(content, ReleaseRootAction.MakeAvailable) };
        var facts = new ReleaseSemanticFacts(ReleaseSemanticStatus.Verified, member, null, true, rootCapabilities: values);
        values[0] = values[0] with { Action = ReleaseRootAction.ActivateServing };
        Assert.Equal(ReleaseRootAction.MakeAvailable, Assert.Single(facts.RootCapabilities).Action);
        Assert.Equal(content, facts.RootCapabilities[0].Content);
    }

    private static ReleaseSemanticFixture AssociatedSelection(int members)
    {
        var fixture = new ReleaseSemanticFixture(members); var selected = fixture.First; var content = new ReleaseMemberKey("etl-artifact", "member-1");
        fixture.Bundle.Manifest["edges"]![0]!["relation"] = "association";
        fixture.Set(selected, edges: fixture.Facts[selected].Edges.Select(x => x with { Relation = ReleaseEdgeRelation.Association }).ToArray(),
            rootCapabilities: [new(content, ReleaseRootAction.ActivateServing)]);
        fixture.Bundle.Manifest["roots"]![0]!["content"] = ReleaseBundleFixture.Key(1);
        return fixture;
    }
}
