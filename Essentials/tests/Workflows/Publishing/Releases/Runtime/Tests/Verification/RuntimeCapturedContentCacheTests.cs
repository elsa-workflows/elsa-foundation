using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

public sealed class RuntimeCapturedContentCacheTests
{
    [Fact]
    public async Task Many_dependencies_and_occurrence_facets_decode_each_captured_member_once_without_listing_content()
    {
        var fixture = new RuntimeReleaseFixture(); var child = RuntimeReleaseFixture.Workflow();
        var childMember = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, child)));
        var parents = Enumerable.Range(0, 32).Select(index =>
        {
            var node = RuntimeReleaseFixture.Node("parent-" + index, metadata: Enumerable.Range(0, 64).ToDictionary(x => "facet-" + x, _ => "retained"));
            var workflow = RuntimeReleaseFixture.Workflow(node, [new(child.Identity.ArtifactId, child.Identity.ArtifactHash, [node.ExecutableNodeId])]);
            return fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow)));
        }).ToArray();
        var codec = new CountingCodec(fixture.Codec); var content = new IndexedOnlyReader(fixture);
        var cache = new RuntimeCapturedContentCache(codec); var verifier = new ReleaseRuntimeArtifactVerifier(cache, new WorkflowExecutableHasher());
        var sites = new ReleaseRuntimeSiteReader(cache);
        foreach (var member in parents)
        {
            var facts = await verifier.VerifyAsync(member, fixture.GetPayload(member.Key), content);
            Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
            Assert.Equal(childMember.Key, Assert.Single(facts.Edges).To);
            Assert.Equal(64, facts.Sites.Count);
            foreach (var site in facts.Sites) Assert.Equal(site.Node, sites.ReadNode(site, content).Node.ExecutableNodeId);
            Assert.Equal(member.Key.Identity, sites.ReadWorkflow(member.Key, content).Executable.Identity.ArtifactId);
        }
        Assert.Equal(ReleaseSemanticStatus.Verified, (await verifier.VerifyAsync(childMember, fixture.GetPayload(childMember.Key), content)).Status);
        Assert.Equal(33, codec.WorkflowDecodes);
        Assert.Equal(33, content.PayloadReads.Values.Sum());
        Assert.All(content.PayloadReads.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public void Concurrent_sites_share_one_decode_and_identical_bytes_in_another_reader_do_not_share_cache()
    {
        var fixture = new RuntimeReleaseFixture();
        var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow())));
        var first = new IndexedOnlyReader(fixture); var second = new IndexedOnlyReader(fixture);
        var codec = new CountingCodec(fixture.Codec); var sites = new ReleaseRuntimeSiteReader(new RuntimeCapturedContentCache(codec));
        var site = new ReleaseSemanticSite(member.Key, ["root"], "root", null, new("fixture", "facet", 1), JsonSerializer.SerializeToElement(new { nodeId = "root" }));
        Parallel.For(0, 128, _ => Assert.Equal("root", sites.ReadNode(site, first).Node.ExecutableNodeId));
        Assert.Equal(1, codec.WorkflowDecodes);
        Assert.Equal("root", sites.ReadNode(site, second).Node.ExecutableNodeId);
        Assert.Equal(2, codec.WorkflowDecodes);
        Assert.Equal(1, first.PayloadReads[member.Key]); Assert.Equal(1, second.PayloadReads[member.Key]);
    }

    [Fact]
    public async Task Reading_a_dependency_pin_before_its_owner_verification_does_not_verify_its_owner_hash()
    {
        var fixture = new RuntimeReleaseFixture(); var original = RuntimeReleaseFixture.Workflow();
        var child = RuntimeReleaseFixture.Workflow(identity: original.Identity with { ArtifactHash = "sha256:" + new string('0', 64) });
        var childMember = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, child)));
        var parent = RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node("parent"), [new(child.Identity.ArtifactId, child.Identity.ArtifactHash, ["parent"])]);
        var parentMember = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, parent)));
        var codec = new CountingCodec(fixture.Codec); var cache = new RuntimeCapturedContentCache(codec); var verifier = new ReleaseRuntimeArtifactVerifier(cache, new WorkflowExecutableHasher());
        Assert.Equal(ReleaseSemanticStatus.Verified, (await verifier.VerifyAsync(parentMember, fixture.GetPayload(parentMember.Key), fixture)).Status);
        var facts = await verifier.VerifyAsync(childMember, fixture.GetPayload(childMember.Key), fixture);
        Assert.Equal(ReleaseSemanticStatus.Invalid, facts.Status);
        Assert.Contains(facts.Diagnostics, x => x.Code == "invalid_native_identity_or_hash");
        Assert.Equal(2, codec.WorkflowDecodes);
    }

    [Fact]
    public async Task A_cached_member_does_not_hide_a_different_payload_supplied_to_its_owner_verifier()
    {
        var fixture = new RuntimeReleaseFixture(); var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow())));
        var verifier = new ReleaseRuntimeArtifactVerifier(fixture.Cache, new WorkflowExecutableHasher());
        Assert.Equal(ReleaseSemanticStatus.Verified, (await verifier.VerifyAsync(member, fixture.GetPayload(member.Key), fixture)).Status);
        var facts = await verifier.VerifyAsync(member, "{}"u8.ToArray(), fixture);
        Assert.Equal(ReleaseSemanticStatus.Invalid, facts.Status);
        Assert.Contains(facts.Diagnostics, x => x.Code == "captured_runtime_identity_conflict");
    }

    [Fact]
    public void Fixed_typed_member_reads_share_cache_and_refuse_cross_kind_or_missing_content_without_lookup_fallback()
    {
        var fixture = new RuntimeReleaseFixture(); var template = RuntimeReleaseFixture.Template();
        var workflow = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow())));
        var model = fixture.Add(fixture.Codec.EncodeModel(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Model())));
        var templateMember = fixture.Add(fixture.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, template)));
        var activity = fixture.Add(fixture.Codec.EncodeActivityVersion(RuntimeReleaseFixture.ActivityVersion(template)));
        var content = new IndexedOnlyReader(fixture); var codec = new CountingCodec(fixture.Codec);
        IReleaseRuntimeSiteReader reader = new ReleaseRuntimeSiteReader(new RuntimeCapturedContentCache(codec));
        for (var index = 0; index < 32; index++)
        {
            Assert.Equal(workflow.Key.Identity, reader.ReadWorkflow(workflow.Key, content).Executable.Identity.ArtifactId);
            Assert.Equal("fixture.protocol", reader.ReadModelMember(model.Key, content).Model.Protocol.Key); // Neutral read grants no protocol verdict.
            Assert.Equal(templateMember.Key.Identity, reader.ReadTemplate(templateMember.Key, content).Template.TemplateId);
            Assert.Equal(activity.Key.Identity, reader.ReadActivityVersion(activity.Key, content).VersionId);
        }
        Assert.Equal(1, codec.WorkflowDecodes); Assert.Equal(1, codec.ModelDecodes);
        Assert.Equal(1, codec.TemplateDecodes); Assert.Equal(1, codec.ActivityVersionDecodes);
        Assert.All(content.PayloadReads.Values, count => Assert.Equal(1, count));
        Assert.Throws<ReleaseRuntimeArtifactException>(() => reader.ReadTemplate(workflow.Key, content));
        Assert.Throws<ReleaseRuntimeArtifactException>(() => reader.ReadWorkflow(model.Key, content));
        Assert.Throws<ReleaseRuntimeArtifactException>(() => reader.ReadModelMember(activity.Key, content));
        Assert.Throws<ReleaseRuntimeArtifactException>(() => reader.ReadActivityVersion(new(ReleaseRuntimeCodecs.ActivityVersionKind, "absent"), content));
    }

    private sealed class IndexedOnlyReader(IReleaseContentReader inner) : IReleaseContentReader
    {
        public ConcurrentDictionary<ReleaseMemberKey, int> PayloadReads { get; } = new();
        public IReadOnlyList<ReleaseContentMember> Members => throw new InvalidOperationException("Indexed reads must not enumerate the manifest.");
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => inner.GetMember(key);
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) { PayloadReads.AddOrUpdate(key, 1, (_, count) => count + 1); return inner.GetPayload(key); }
    }

    private sealed class CountingCodec(IReleaseRuntimeArtifactCodec inner) : IReleaseRuntimeArtifactCodec
    {
        private int _workflowDecodes;
        private int _modelDecodes;
        private int _templateDecodes;
        private int _activityVersionDecodes;
        public int WorkflowDecodes => Volatile.Read(ref _workflowDecodes);
        public int ModelDecodes => Volatile.Read(ref _modelDecodes);
        public int TemplateDecodes => Volatile.Read(ref _templateDecodes);
        public int ActivityVersionDecodes => Volatile.Read(ref _activityVersionDecodes);
        public ReleaseRuntimeWorkflow DecodeWorkflow(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) { Interlocked.Increment(ref _workflowDecodes); return inner.DecodeWorkflow(payload, limits); }
        public ReleaseRuntimeTemplate DecodeTemplate(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) { Interlocked.Increment(ref _templateDecodes); return inner.DecodeTemplate(payload, limits); }
        public ReleaseRuntimeModel DecodeModel(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) { Interlocked.Increment(ref _modelDecodes); return inner.DecodeModel(payload, limits); }
        public ReleaseWorkflowSelection DecodeWorkflowSelection(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) => inner.DecodeWorkflowSelection(payload, limits);
        public ReleaseActivityVersion DecodeActivityVersion(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) { Interlocked.Increment(ref _activityVersionDecodes); return inner.DecodeActivityVersion(payload, limits); }
        public ReleaseEncodedRuntimeMember EncodeWorkflow(ReleaseRuntimeWorkflow value, ReleaseBundleLimits? limits = null) => inner.EncodeWorkflow(value, limits);
        public ReleaseEncodedRuntimeMember EncodeTemplate(ReleaseRuntimeTemplate value, ReleaseBundleLimits? limits = null) => inner.EncodeTemplate(value, limits);
        public ReleaseEncodedRuntimeMember EncodeModel(ReleaseRuntimeModel value, ReleaseBundleLimits? limits = null) => inner.EncodeModel(value, limits);
        public ReleaseEncodedRuntimeMember EncodeWorkflowSelection(ReleaseWorkflowSelection value, ReleaseBundleLimits? limits = null) => inner.EncodeWorkflowSelection(value, limits);
        public ReleaseEncodedRuntimeMember EncodeActivityVersion(ReleaseActivityVersion value, ReleaseBundleLimits? limits = null) => inner.EncodeActivityVersion(value, limits);
    }
}
