using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Etl.Core;
using Elsa.Etl.Releases.Core;
using Elsa.Etl.Releases.Workflows;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Etl.Releases.Tests;

public sealed class RunEtlReleaseInspectorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Real_CLR_and_metadata_facets_validate_full_ETL_pin_but_emit_one_dependency_and_native_requirement(int format)
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var captured = new RunEtlReleaseFixture(scope.ServiceProvider, format: format);
        var inspector = Inspector(scope.ServiceProvider);
        var clr = await inspector.InspectAsync(captured.Site(false), captured);
        var metadata = await inspector.InspectAsync(captured.Site(true), captured);
        Assert.Equal(ReleaseSemanticStatus.Verified, clr.Status);
        Assert.Equal(ReleaseSemanticStatus.Verified, metadata.Status);
        Assert.Empty(clr.Edges); Assert.Empty(clr.Prerequisites);
        var edge = Assert.Single(metadata.Edges);
        Assert.Equal(new ReleaseSemanticEdge(captured.Containing, new(EtlReleaseCodecs.MemberKind, captured.Etl.ArtifactId), ReleaseEdgeRelation.Dependency, "node-etl"), edge);
        var requirement = Assert.Single(metadata.Prerequisites);
        Assert.Equal(captured.Containing, requirement.Member);
        Assert.Equal("runtime-capability", requirement.Kind); Assert.Equal("etl.native-workflows", requirement.Identity);
        Assert.Equal("exact", requirement.Scheme); Assert.Equal("1", requirement.Constraint);
        Assert.Empty(metadata.Resources); Assert.Empty(clr.Diagnostics); Assert.Empty(metadata.Diagnostics);
        Assert.Equal(1, captured.EtlPayloadReads);
    }

    [Theory]
    [InlineData("selection-version")]
    [InlineData("selection-hash")]
    [InlineData("selection-malformed")]
    [InlineData("pin-hash")]
    [InlineData("pin-other-version")]
    [InlineData("pin-duplicate")]
    [InlineData("captured-missing")]
    [InlineData("missing-pin")]
    [InlineData("scope-authority")]
    [InlineData("scope-tenant")]
    [InlineData("scope-partition")]
    [InlineData("contract-version")]
    [InlineData("input-required")]
    [InlineData("input-nullable")]
    [InlineData("result-type")]
    [InlineData("result-required")]
    [InlineData("result-projection")]
    [InlineData("outcomes")]
    [InlineData("replay-safe")]
    [InlineData("background-safe")]
    [InlineData("resume-missing")]
    [InlineData("resume-handler")]
    [InlineData("resume-node")]
    [InlineData("resume-local")]
    [InlineData("resume-map")]
    [InlineData("resume-extra")]
    [InlineData("resume-direct-foreign")]
    [InlineData("generic-conflict")]
    [InlineData("runtime.structuralFaultHandling")]
    [InlineData("runtime.acceptsRescheduledFaultedChild")]
    [InlineData("runtime.childControlConsumerKey")]
    [InlineData("runtime.childControlConsumerVersion")]
    public async Task Known_RunEtl_owner_refuses_conflicting_content_scope_leaf_result_and_resume_contracts(string change)
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var captured = new RunEtlReleaseFixture(scope.ServiceProvider, change);
        var result = await Inspector(scope.ServiceProvider).InspectAsync(captured.Site(change != "missing-pin"), captured);
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Diagnostics); Assert.Empty(result.Edges); Assert.Empty(result.Prerequisites);
        Assert.All(result.Diagnostics, value => { Assert.Equal(captured.Containing, value.Member); Assert.DoesNotContain("another-partition", value.Code, StringComparison.Ordinal); });
    }

    [Theory]
    [InlineData("dynamic", "run_etl_dynamic_selection_unsupported")]
    [InlineData("selection-format", "run_etl_selection_format_unsupported")]
    public async Task Dynamic_and_unknown_selector_protocols_remain_named_unsupported(string change, string code)
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var captured = new RunEtlReleaseFixture(scope.ServiceProvider, change);
        var result = await Inspector(scope.ServiceProvider).InspectAsync(captured.Site(false), captured);
        Assert.Equal(ReleaseSemanticStatus.Unsupported, result.Status);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code); Assert.Empty(result.Edges);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Static_reusable_template_without_embedded_pin_resolves_only_two_framed_captured_keys(int format)
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var captured = new RunEtlReleaseFixture(scope.ServiceProvider, "template-static", template: true, format: format) { RefuseCatalogEnumeration = true };
        var result = await Inspector(scope.ServiceProvider).InspectAsync(captured.Site(false), captured);
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status);
        Assert.Equal(captured.Etl.ArtifactId, Assert.Single(result.Edges).To.Identity);
        Assert.Single(result.Prerequisites); Assert.Equal(2, captured.EtlMemberLookups); Assert.Equal(1, captured.EtlPayloadReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_legacy_global_resume_key_and_identity_selection_conversion_remain_supported(bool template)
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        foreach (var change in new[] { "resume-legacy", "identity-conversion" })
        {
            var captured = new RunEtlReleaseFixture(scope.ServiceProvider, change, template);
            var result = await Inspector(scope.ServiceProvider).InspectAsync(captured.Site(true), captured);
            Assert.Equal(ReleaseSemanticStatus.Verified, result.Status); Assert.Single(result.Edges);
        }
    }

    [Fact]
    public async Task Reader_scoped_cache_does_not_reuse_a_good_scope_for_another_captured_reader()
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var inspector = Inspector(scope.ServiceProvider);
        var first = new RunEtlReleaseFixture(scope.ServiceProvider);
        var second = new RunEtlReleaseFixture(scope.ServiceProvider, "scope-partition");
        Assert.Equal(first.Containing, second.Containing);
        Assert.Equal(ReleaseSemanticStatus.Verified, (await inspector.InspectAsync(first.Site(true), first)).Status);
        var refused = await inspector.InspectAsync(second.Site(true), second);
        Assert.Equal(ReleaseSemanticStatus.Invalid, refused.Status);
        Assert.Equal("run_etl_source_scope_mismatch", Assert.Single(refused.Diagnostics).Code);
        Assert.Equal(1, first.EtlPayloadReads); Assert.Equal(1, second.EtlPayloadReads);
    }

    [Fact]
    public async Task Agreeing_handcrafted_generic_ETL_dependency_is_crosschecked_but_generic_owner_still_refuses_its_kind()
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var captured = new RunEtlReleaseFixture(scope.ServiceProvider, "generic-agree");
        var owner = await Inspector(scope.ServiceProvider).InspectAsync(captured.Site(true), captured);
        Assert.Equal(ReleaseSemanticStatus.Verified, owner.Status); Assert.Empty(owner.Edges);
        var generic = scope.ServiceProvider.GetServices<IReleaseArtifactSemanticVerifier>().Single(x => x.SupportedCodecs.Contains(captured.GetMember(captured.Containing).Codec));
        var facts = await generic.VerifyAsync(captured.GetMember(captured.Containing), captured.GetPayload(captured.Containing), captured);
        Assert.Equal(ReleaseSemanticStatus.Invalid, facts.Status);
        Assert.Contains(facts.Diagnostics, x => x.Code == "captured_runtime_pin_missing");
    }

    [Fact]
    public async Task Composed_native_and_ETL_owners_verify_the_complete_captured_metadata_dependency()
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var captured = new RunEtlReleaseFixture(scope.ServiceProvider);
        var bundle = await Bundle(scope.ServiceProvider, captured);
        var result = await scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(bundle);
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status); Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Edges.Count); Assert.Equal(4, result.Resources.Count);
        Assert.Contains(result.Edges, x => x.From == captured.Containing && x.To == new ReleaseMemberKey(EtlReleaseCodecs.MemberKind, captured.Etl.ArtifactId) && x.Occurrence == "node-etl");
        Assert.Single(result.Prerequisites, x => x.Identity == "etl.native-workflows");
        Assert.Contains(result.Capabilities, x => x.Identity.Name == "elsa.etl.native-workflows");

        using var withoutAddon = RunEtlReleaseFixture.Services(addon: false); using var otherScope = withoutAddon.CreateScope();
        var unsupported = await otherScope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(bundle);
        Assert.Equal(ReleaseSemanticStatus.Unsupported, unsupported.Status);
        Assert.NotEmpty(unsupported.Diagnostics);
    }

    [Fact]
    public async Task Declared_graph_cannot_omit_the_ETL_dependency_even_when_the_ETL_member_is_an_available_root()
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var captured = new RunEtlReleaseFixture(scope.ServiceProvider);
        var bundle = await Bundle(scope.ServiceProvider, captured);
        var manifest = JsonNode.Parse(bundle.Manifest.GetRawText())!;
        var edges = manifest["edges"]!.AsArray();
        edges.Remove(edges.Single(x => x!["to"]!["kind"]!.GetValue<string>() == EtlReleaseCodecs.MemberKind));
        var changed = scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>().Validate(Encoding.UTF8.GetBytes(manifest.ToJsonString()),
            captured.Members.ToDictionary(x => x.Descriptor.PayloadDigest, x => captured.GetPayload(x.Key)));
        var result = await scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(changed);
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status); Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Embedded_naked_owner_decoder_keeps_original_bytes_and_refuses_a_release_wrapper(int format)
    {
        using var provider = RunEtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        var artifact = format == 1 ? EtlReleaseFixture.Artifact(EtlReleaseFixture.V1()) : EtlReleaseFixture.Artifact(EtlReleaseFixture.V2());
        var raw = JsonSerializer.SerializeToUtf8Bytes(artifact, RunEtlReleaseFixture.OwnerJson);
        var decoded = codec.DecodeOwnerArtifact(raw);
        Assert.Equal(artifact.ArtifactId, decoded.ArtifactId); Assert.Equal(artifact.Pipeline.GetRawText(), decoded.Pipeline.GetRawText());
        Assert.Throws<EtlReleaseArtifactException>(() => codec.DecodeOwnerArtifact(codec.Encode(EtlReleaseFixture.Release(artifact)).GetPayloadBytes()));
        Assert.Throws<EtlReleaseArtifactException>(() => codec.DecodeOwnerArtifact(new byte[4 * 1024 * 1024]));
    }

    private static IReleaseOccurrenceInspector Inspector(IServiceProvider services) => services.GetServices<IReleaseOccurrenceInspector>()
        .Single(x => x.Identity.Name == "elsa.etl.native-workflows");

    private static async Task<IReleaseBundleTransport> Bundle(IServiceProvider services, RunEtlReleaseFixture captured)
    {
        var selection = captured.AddSelection(services);
        var manifest = EtlReleaseFixture.Manifest(captured.EtlBytes, captured.Etl);
        manifest["roots"]!.AsArray().Add(new JsonObject { ["member"] = Key(selection), ["content"] = Key(captured.Containing), ["action"] = "activate-serving" });
        var nativeMembers = captured.Members.Where(x => x.Key.Kind != EtlReleaseCodecs.MemberKind).ToArray();
        foreach (var member in nativeMembers)
        {
            manifest["members"]!.AsArray().Add(new JsonObject
            {
                ["key"] = Key(member.Key), ["codec"] = member.Codec.Codec, ["codecVersion"] = member.Codec.CodecVersion,
                ["ownerVersion"] = new JsonObject { ["scheme"] = member.OwnerVersion!.Scheme, ["value"] = member.OwnerVersion.TextValue },
                ["payloadDigest"] = member.Descriptor.PayloadDigest, ["byteLength"] = member.Descriptor.ByteLength
            });
            var owner = services.GetServices<IReleaseArtifactSemanticVerifier>().Single(x => x.SupportedCodecs.Contains(member.Codec));
            var facts = await owner.VerifyAsync(member, captured.GetPayload(member.Key), captured);
            Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
            foreach (var prerequisite in facts.Prerequisites) manifest["prerequisites"]!.AsArray().Add(Prerequisite(prerequisite));
        }
        manifest["edges"]!.AsArray().Add(new JsonObject { ["from"] = Key(selection), ["to"] = Key(captured.Containing), ["relation"] = "association", ["occurrence"] = "" });
        manifest["edges"]!.AsArray().Add(new JsonObject { ["from"] = Key(captured.Containing), ["to"] = Key(new(EtlReleaseCodecs.MemberKind, captured.Etl.ArtifactId)), ["relation"] = "dependency", ["occurrence"] = "node-etl" });
        var nativeFacts = await Inspector(services).InspectAsync(captured.Site(true), captured);
        var requirement = Assert.Single(nativeFacts.Prerequisites);
        Assert.Equal("etl.native-workflows", requirement.Identity); Assert.Equal("exact", requirement.Scheme); Assert.Equal("1", requirement.Constraint);
        manifest["prerequisites"]!.AsArray().Add(Prerequisite(requirement));
        return services.GetRequiredService<IReleaseBundleCodec>().Validate(Encoding.UTF8.GetBytes(manifest.ToJsonString()),
            captured.Members.ToDictionary(x => x.Descriptor.PayloadDigest, x => captured.GetPayload(x.Key)));
    }
    private static JsonObject Key(ReleaseMemberKey key) => new() { ["kind"] = key.Kind, ["identity"] = key.Identity };
    private static JsonObject Prerequisite(ReleaseSemanticPrerequisite value) => new()
    {
        ["key"] = value.Key, ["member"] = Key(value.Member), ["kind"] = value.Kind, ["identity"] = value.Identity,
        ["scheme"] = value.Scheme, ["constraint"] = value.Constraint
    };
}
