using System.Text.Json;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

public sealed class RuntimeIntrinsicReleaseTests
{
    [Theory]
    [InlineData(null, "1")]
    [InlineData("elsa.bpmn", null)]
    [InlineData("", "1")]
    [InlineData("elsa.bpmn", "0")]
    [InlineData("elsa.bpmn", "01")]
    [InlineData("elsa.bpmn", "-1")]
    [InlineData("elsa.bpmn", "2147483648")]
    public async Task Incomplete_or_noncanonical_child_control_pairs_fail_closed(string? key, string? version)
    {
        var metadata = new Dictionary<string, string>();
        if (key is not null) metadata.Add(RuntimeMetadataKeys.ChildControlConsumerKey, key);
        if (version is not null) metadata.Add(RuntimeMetadataKeys.ChildControlConsumerVersion, version);
        var fixture = new RuntimeReleaseFixture(); var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope,
            RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(metadata: metadata, consumer: "fixture.consumer", intrinsic: null, payload: RuntimeReleaseFixture.Json("{}"))))));
        var facts = await fixture.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Invalid, facts.Status);
        Assert.Contains(facts.Diagnostics, diagnostic => diagnostic.Code == "invalid_child_control_consumer");
    }

    [Fact]
    public async Task Child_control_pair_emits_one_protocol_site_without_claiming_shared_metadata_keys()
    {
        var fixture = new RuntimeReleaseFixture(); var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope,
            RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(metadata: new Dictionary<string, string>
            { [RuntimeMetadataKeys.ChildControlConsumerKey] = "elsa.bpmn", [RuntimeMetadataKeys.ChildControlConsumerVersion] = "1" },
                consumer: "fixture.consumer", intrinsic: null, payload: RuntimeReleaseFixture.Json("{}"))))));
        var facts = await fixture.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
        var site = Assert.Single(facts.Sites, value => value.Site.SiteKind == ReleaseRuntimeSiteKinds.ChildControlConsumer);
        Assert.Equal(new ReleaseSiteKey(ReleaseRuntimeSiteKinds.ChildControlConsumer, "elsa.bpmn", 1), site.Site);
        Assert.Null(site.Input);
        Assert.DoesNotContain(facts.Sites, value => value.Site.Discriminator is RuntimeMetadataKeys.ChildControlConsumerKey or RuntimeMetadataKeys.ChildControlConsumerVersion);
        Assert.Equal("root", new ReleaseRuntimeSiteReader(fixture.Cache).ReadNode(site, fixture).Node.ExecutableNodeId);
    }

    [Theory]
    [InlineData(RuntimeMetadataKeys.StructuralFaultHandling, "child", true)]
    [InlineData(RuntimeMetadataKeys.StructuralFaultHandling, "descendant", true)]
    [InlineData(RuntimeMetadataKeys.StructuralFaultHandling, "arbitrary", false)]
    [InlineData(RuntimeMetadataKeys.AcceptsRescheduledFaultedChild, "true", true)]
    [InlineData(RuntimeMetadataKeys.AcceptsRescheduledFaultedChild, "True", false)]
    public async Task Shared_structural_hint_representation_is_explicit_and_does_not_replace_the_activity_owner(string key, string value, bool valid)
    {
        var fixture = new RuntimeReleaseFixture(); var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope,
            RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(metadata: new Dictionary<string, string> { [key] = value },
                consumer: "fixture.consumer", intrinsic: null, payload: RuntimeReleaseFixture.Json("{}"))))));
        var facts = await fixture.Verify(member); var site = facts.Sites.Single(value => value.Site.Discriminator == key);
        var inspector = new RuntimeIntrinsicReleaseInspector(new ReleaseRuntimeSiteReader(fixture.Cache));
        var result = await inspector.InspectAsync(site, fixture);
        Assert.Equal(valid ? ReleaseSemanticStatus.Verified : ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(facts.Sites, value => value.Site.SiteKind == ReleaseRuntimeSiteKinds.Descriptor && value.Site.Discriminator == "fixture.consumer");
        Assert.Empty(result.Edges);
    }

    [Theory]
    [InlineData("workflow-execution-vertical-slice", false, ReleaseSemanticStatus.Verified)]
    [InlineData("future-slice", false, ReleaseSemanticStatus.Unsupported)]
    [InlineData("workflow-execution-vertical-slice", true, ReleaseSemanticStatus.Unsupported)]
    public async Task Published_compatibility_slice_is_owned_explicitly_and_unknown_metadata_stays_unsupported(string value, bool extra, ReleaseSemanticStatus expected)
    {
        var fixture = new RuntimeReleaseFixture(); var original = RuntimeReleaseFixture.Workflow();
        var compatibility = new Dictionary<string, string> { ["slice"] = value };
        if (extra) compatibility.Add("unknown", "opaque");
        var workflow = new WorkflowExecutable(original.Identity, original.RootActivity, original.ResumeTargets, original.CreatedAt,
            compatibility, original.InputContract, original.Dependencies, original.RuntimeRequirements, original.StorageDriverRequirements, original.IncidentStrategy);
        var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow)));
        var facts = await fixture.Verify(member); var site = Assert.Single(facts.Sites);
        var result = await new RuntimeIntrinsicReleaseInspector(new ReleaseRuntimeSiteReader(fixture.Cache)).InspectAsync(site, fixture);
        Assert.Equal(expected, result.Status);
    }
}
