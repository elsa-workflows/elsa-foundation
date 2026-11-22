using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.NativeCapture.Tests;

public sealed class NativePrimitiveOwnerVerificationTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task Actual_Graph_and_WriteLine_publications_build_and_reverify_a_complete_frozen_bundle(string schema)
    {
        await WithCaptured(schema, true, async (f, content, roots) =>
        {
            var result = await f.Service<IReleaseBundleBuilder>().BuildAsync(content, roots);
            Assert.True(result.Verification.Status == ReleaseSemanticStatus.Verified,
                string.Join(",", result.Verification.Diagnostics.Select(value => value.Code)));
            var bundle = Assert.IsAssignableFrom<IReleaseBundleTransport>(result.Bundle);
            Assert.Equal(6, bundle.Members.Count); // Selection, workflow, and two exact catalog/template pairs.
            Assert.Contains(result.Verification.Prerequisites, value => value.Kind == "activity-module" && value.Identity == "ActivitiesPrimitives");
            var transport = f.Service<IReleaseBundleCodec>(); using var zip = new MemoryStream();
            await transport.WriteZipAsync(bundle, zip); zip.Position = 0; var reopened = await transport.ReadZipAsync(zip);
            Assert.Equal(bundle.Digest, reopened.Digest); Assert.Equal(bundle.GetManifestBytes(), reopened.GetManifestBytes());
            foreach (var member in bundle.Members) Assert.Equal(bundle.GetBlobBytes(member.PayloadDigest), reopened.GetBlobBytes(member.PayloadDigest));
            var verdict = await f.Service<IReleaseSemanticVerifier>().VerifyAsync(reopened, new() { ExpectedDigest = bundle.Digest });
            Assert.Equal(ReleaseSemanticStatus.Verified, verdict.Status); Assert.Empty(verdict.Diagnostics);
            // Complete captured closure and transport round trip only; owner import/start admission is separate.
        });
    }

    [Fact]
    public async Task Missing_primitive_owner_refuses_the_actual_closure_even_when_the_Graph_owner_is_installed()
    {
        await WithCaptured("1", false, async (f, content, roots) =>
        {
            var result = await f.Service<IReleaseBundleBuilder>().BuildAsync(content, roots);
            Assert.Null(result.Bundle); Assert.Equal(ReleaseSemanticStatus.Unsupported, result.Verification.Status);
            Assert.Equal(2, result.Verification.Diagnostics.Count); // Captured CLR template and placed CLR child.
            Assert.All(result.Verification.Diagnostics, value => Assert.Equal("unsupported_occurrence_site", value.Code));
        });
    }

    [Fact]
    public async Task Missing_placement_owner_refuses_the_actual_six_field_CLR_body()
    {
        await WithCaptured("1", true, async (f, content, roots) =>
        {
            var result = await f.Service<IReleaseBundleBuilder>().BuildAsync(content, roots);
            Assert.Null(result.Bundle); Assert.Equal(ReleaseSemanticStatus.Unsupported, result.Verification.Status);
            Assert.Equal(18, result.Verification.Diagnostics.Count); // Two placements, each with eight metadata claims.
            Assert.All(result.Verification.Diagnostics, value => Assert.Equal("unsupported_occurrence_site", value.Code));
        }, placementOwner: false);
    }

    [Theory]
    [InlineData("side-effect", "primitive_contract_mismatch")]
    [InlineData("absent-literal", "primitive_literal_missing")]
    public async Task Actual_WriteLine_owner_refuses_a_different_frozen_contract_or_absent_required_literal(string change, string code)
    {
        await WithCaptured("1", true, async (f, original, roots) =>
        {
            var member = original.Members.Single(value => value.Key.Kind == ReleaseRuntimeCodecs.WorkflowKind);
            var decoded = f.Codec.DecodeWorkflow(original.GetPayload(member.Key));
            var child = decoded.Executable.RootActivity.ChildSlots.Single().Activities.Single(); var contract = child.ActivityContract!;
            var alteredContract = new ActivityContract(contract.ActivityTypeKey, contract.ContractVersion, contract.DescriptorKind, contract.DescriptorPayload,
                contract.Inputs.Values, contract.Result, contract.Outcomes, contract.Activation, SideEffectProfile.External, contract.SupportsBackgroundExecution);
            var document = JsonNode.Parse(original.GetPayload(member.Key).Span)!;
            var node = document["content"]!["executable"]!["rootActivity"]!["childSlots"]![0]!["activities"]![0]!;
            if (change == "side-effect")
            {
                node["activityContract"]!.AsObject().Remove("sideEffectProfile"); // Default External is omitted by the actual wire contract.
                node["activityContract"]!["schemaFingerprint"] = alteredContract.SchemaFingerprint;
            }
            else
            {
                var binding = node["inputBindings"]!["text"]!;
                binding["literal"]!["presence"] = "Absent"; binding["literal"]!.AsObject().Remove("inlineValue");
                binding.AsObject().Remove("literalValue");
            }
            var bytes = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()); var changed = new Replaced(original, member, bytes);
            // First establish the controlled body is decodable; full artifact hash verification is independently mandatory.
            var changedNode = f.Codec.DecodeWorkflow(changed.GetPayload(member.Key)).Executable.RootActivity.ChildSlots.Single().Activities.Single();
            if (change == "side-effect") Assert.Equal(SideEffectProfile.External, changedNode.ActivityContract!.SideEffectProfile);
            else Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ValuePresence.Absent, changedNode.InputBindings["text"].Literal!.Presence);
            var inspector = f.ServicesOf<IReleaseOccurrenceInspector>().Single(value => value.Identity.Name == "elsa.primitive-release-profile");
            var result = await inspector.InspectAsync(new(member.Key, [], child.ExecutableNodeId, null,
                new(ReleaseRuntimeSiteKinds.ClrActivity, TypeAliasConvention.CanonicalAlias(typeof(WriteLine)), 1),
                JsonSerializer.SerializeToElement(new { nodeId = child.ExecutableNodeId })), changed);
            Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
            Assert.Contains(result.Diagnostics, value => value.Code == code);
        });
    }

    private static async Task WithCaptured(string schema, bool primitiveOwner,
        Func<PublishedNativeFixture, IReleaseContentReader, IReadOnlyList<ReleaseBundleRoot>, Task> check, bool placementOwner = true)
    {
        await using var f = await PublishedNativeFixture.Create(graphOwner: true, primitiveOwner: primitiveOwner, releaseVerification: true, placementOwner: placementOwner);
        var child = await f.PublishClrCatalog(); var graph = await f.PublishReusableGraph(child, schema);
        await f.SeedClrWorkflow(graph); await f.Publish("actual-clr-workflow-version");
        await f.Source().ExecuteAsync([new(NativeReleaseCaptureSelections.WorkflowDefinitionVersion, "actual-clr-workflow-version", null, ReleaseRootAction.ActivateServing)], new(), async (root, token) =>
        {
            var workflow = Assert.Single(root.Content.Members.Where(value => value.Key.Kind == ReleaseRuntimeCodecs.WorkflowKind));
            await f.CatalogSource().ExecuteAsync([workflow], root.Content, new(), async (catalog, inner) =>
                await check(f, new Joined(root.Content, catalog), root.Roots), token);
        });
    }

    private sealed class Replaced(IReleaseContentReader actual, ReleaseContentMember member, byte[] bytes) : IReleaseContentReader
    {
        private readonly ReleaseContentMember _replacement = new(new(member.Descriptor.Kind, member.Descriptor.Identity, member.Descriptor.Codec, member.Descriptor.CodecVersion,
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length), member.OwnerVersion);
        public IReadOnlyList<ReleaseContentMember> Members => actual.Members.Select(value => value.Key == member.Key ? _replacement : value).ToArray();
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => key == member.Key ? _replacement : actual.GetMember(key);
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => key == member.Key ? (byte[])bytes.Clone() : actual.GetPayload(key);
    }

    private sealed class Joined(params IReleaseContentReader[] readers) : IReleaseContentReader
    {
        public IReadOnlyList<ReleaseContentMember> Members => readers.SelectMany(value => value.Members).ToArray();
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => Members.SingleOrDefault(value => value.Key == key) ?? throw new KeyNotFoundException();
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => readers.Single(value => value.Members.Any(member => member.Key == key)).GetPayload(key);
    }
}
