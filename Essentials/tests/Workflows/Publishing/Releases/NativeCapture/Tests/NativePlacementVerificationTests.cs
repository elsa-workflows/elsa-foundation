using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.NativeCapture.Tests;

public sealed class NativePlacementVerificationTests
{
    [Fact]
    public async Task Real_compiler_placement_is_bound_to_original_catalog_templates_and_all_child_frames()
    {
        await WithCaptured(async (f, reader, member) =>
        {
            var verifier = f.ServicesOf<IReleaseArtifactSemanticVerifier>().Single(value => value.SupportedCodecs.Contains(member.Codec));
            var facts = await verifier.VerifyAsync(member, reader.GetPayload(member.Key), reader);
            Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
            var inspector = f.ServicesOf<IReleaseOccurrenceInspector>().Single(value => value.Identity.Name == "elsa.runtime-placement-release-profile");
            var sites = facts.Sites.Where(site => inspector.SupportedSites.Contains(site.Site)).ToArray();
            Assert.Equal(18, sites.Length); // Graph and CLR boundary: eight metadata fields and one placement site each.
            foreach (var site in sites) Assert.Equal(ReleaseSemanticStatus.Verified, (await inspector.InspectAsync(site, reader)).Status);
            // Graph provider, Graph descriptor, and actual CLR capability owners remain mandatory independent sites.
            Assert.Contains(facts.Sites, site => site.Site.SiteKind == ReleaseRuntimeSiteKinds.Descriptor);
            Assert.Contains(facts.Sites, site => site.Site.SiteKind == ReleaseRuntimeSiteKinds.ClrActivity);
        });
    }

    [Theory]
    [InlineData("namespace", "placement_namespace_mismatch")]
    [InlineData("catalog", "placement_catalog_association_mismatch")]
    [InlineData("source-reference", "placement_catalog_association_mismatch")]
    [InlineData("clr-alias", "placement_clr_alias_mismatch")]
    [InlineData("orphan", "orphan_placement_origin")]
    [InlineData("missing-child", "placement_dependency_frame_mismatch")]
    [InlineData("producer-origin", "placement_workflow_origin_mismatch")]
    public async Task Actual_retained_placement_refuses_controlled_association_and_inventory_corruption(string change, string code)
    {
        await WithCaptured(async (f, reader, member) =>
        {
            var document = JsonNode.Parse(reader.GetPayload(member.Key).Span)!;
            var root = document["content"]!["executable"]!["rootActivity"]!;
            var child = root["childSlots"]![0]!["activities"]![0]!;
            switch (change)
            {
                case "namespace": root["metadata"]!["activity.placementNamespace"] = "wrong"; break;
                case "catalog":
                    root["metadata"]!["activity.definitionId"] = "other";
                    root["descriptor"]!["payload"]!["definitionId"] = "other"; break;
                case "source-reference":
                    root["metadata"]!["activity.sourceReferenceId"] = "other";
                    root["descriptor"]!["payload"]!["sourceReferenceId"] = "other"; break;
                case "clr-alias": child["descriptor"]!["payload"]!["typeAlias"] = "other"; break;
                case "orphan": child["metadata"]!.AsObject().Remove("activity.templateHash"); break;
                case "missing-child": root["childSlots"]![0]!["activities"] = new JsonArray(); break;
                case "producer-origin":
                    var origin = JsonNode.Parse(root["metadata"]!["activity.invocationOrigin"]!.GetValue<string>())!;
                    origin[0]!["Id"] = "other-producer";
                    root["metadata"]!["activity.invocationOrigin"] = origin.ToJsonString(); break;
            }
            // Recompute the transport descriptor so the placement guard, rather than a blob-digest check, is exercised.
            // Full artifact-hash verification remains a separate mandatory owner step.
            var altered = new Replaced(reader, member, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
            var nodeId = f.Codec.DecodeWorkflow(reader.GetPayload(member.Key)).Executable.RootActivity.ExecutableNodeId;
            var site = new ReleaseSemanticSite(member.Key, [], nodeId, null, new(ReleaseRuntimeSiteKinds.Placement, "activity-template", 1), JsonSerializer.SerializeToElement(new { nodeId }));
            var inspector = f.ServicesOf<IReleaseOccurrenceInspector>().Single(value => value.Identity.Name == "elsa.runtime-placement-release-profile");
            var result = await inspector.InspectAsync(site, altered);
            Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        });
    }

    private static async Task WithCaptured(Func<PublishedNativeFixture, IReleaseContentReader, ReleaseContentMember, Task> check)
    {
        await using var f = await PublishedNativeFixture.Create(); var child = await f.PublishClrCatalog(); var graph = await f.PublishReusableGraph(child);
        await f.SeedClrWorkflow(graph); await f.Publish("actual-clr-workflow-version");
        await f.Source().ExecuteAsync([new(Elsa.Workflows.Publishing.Core.Contracts.NativeReleaseCaptureSelections.WorkflowDefinitionVersion,
            "actual-clr-workflow-version", null, ReleaseRootAction.ActivateServing)], new(), async (root, token) =>
        {
            var member = Assert.Single(root.Content.Members.Where(value => value.Key.Kind == ReleaseRuntimeCodecs.WorkflowKind));
            await f.CatalogSource().ExecuteAsync([member], root.Content, new(), async (catalog, inner) =>
            {
                await check(f, new Joined(root.Content, catalog), member);
            }, token);
        });
    }
    private sealed class Joined(params IReleaseContentReader[] readers) : IReleaseContentReader
    {
        public IReadOnlyList<ReleaseContentMember> Members => readers.SelectMany(value => value.Members).ToArray();
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => Members.Single(value => value.Key == key);
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => readers.Single(value => value.Members.Any(member => member.Key == key)).GetPayload(key);
    }
    private sealed class Replaced(IReleaseContentReader actual, ReleaseContentMember member, byte[] bytes) : IReleaseContentReader
    {
        private readonly ReleaseContentMember _replacement = new(new(member.Descriptor.Kind, member.Descriptor.Identity, member.Descriptor.Codec,
            member.Descriptor.CodecVersion, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length), member.OwnerVersion);
        public IReadOnlyList<ReleaseContentMember> Members => actual.Members.Select(value => value.Key == member.Key ? _replacement : value).ToArray();
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => key == member.Key ? _replacement : actual.GetMember(key);
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => key == member.Key ? (byte[])bytes.Clone() : actual.GetPayload(key);
    }
}
