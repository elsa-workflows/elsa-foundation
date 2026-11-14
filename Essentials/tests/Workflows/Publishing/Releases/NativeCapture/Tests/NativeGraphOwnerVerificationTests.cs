using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Graph.Releases;
using WellKnownRuntimeActivityConsumers = Elsa.Activities.Runtime.Core.Models.WellKnownRuntimeActivityConsumers;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.NativeCapture.Tests;

public sealed class NativeGraphOwnerVerificationTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task Actual_Graph_publication_templates_and_placed_plan_pass_the_independent_Graph_owner(string schema)
    {
        await WithCaptured(schema, async (f, reader) =>
        {
            var inspector = Graph(f); var seen = new HashSet<ReleaseSiteKey>();
            foreach (var member in reader.Members)
            {
                var verifier = f.ServicesOf<IReleaseArtifactSemanticVerifier>().Single(value => value.SupportedCodecs.Contains(member.Codec));
                var facts = await verifier.VerifyAsync(member, reader.GetPayload(member.Key), reader); Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
                foreach (var site in facts.Sites.Where(site => inspector.SupportedSites.Contains(site.Site)))
                {
                    seen.Add(site.Site); var result = await inspector.InspectAsync(site, reader);
                    Assert.True(result.Status == ReleaseSemanticStatus.Verified, string.Join(",", result.Diagnostics.Select(value => value.Code)));
                }
            }
            Assert.True(seen.SetEquals(inspector.SupportedSites));
            // CLR capability inspection is still mandatory. This does not claim complete native bundle admission.
        });
    }

    [Theory]
    [InlineData("provider-extra", ReleaseSemanticStatus.Unsupported, "graph_field_unsupported")]
    [InlineData("provider-schema", ReleaseSemanticStatus.Unsupported, "graph_provider_profile_unsupported")]
    [InlineData("provider-null", ReleaseSemanticStatus.Invalid, "graph_null_input_mismatch")]
    [InlineData("schema-one-outcome", ReleaseSemanticStatus.Invalid, "graph_schema_one_outcome_mismatch")]
    [InlineData("placed-literal", ReleaseSemanticStatus.Invalid, "graph_placed_input_value_mismatch")]
    [InlineData("descriptor-stamp", ReleaseSemanticStatus.Invalid, "graph_placed_identity_mismatch")]
    public async Task Actual_Graph_owner_refuses_original_provider_and_placed_identity_changes(string change, ReleaseSemanticStatus status, string code)
    {
        await WithCaptured("1", async (f, original) =>
        {
            var kind = (change.StartsWith("provider-", StringComparison.Ordinal) || change == "schema-one-outcome") ? ReleaseRuntimeCodecs.ActivityVersionKind : ReleaseRuntimeCodecs.WorkflowKind;
            var member = original.Members.Single(value => value.Key.Kind == kind && IsGraphMember(value));
            bool IsGraphMember(ReleaseContentMember value)
            {
                if (kind == ReleaseRuntimeCodecs.WorkflowKind) return true;
                var body = JsonNode.Parse(original.GetPayload(value.Key).Span)!["content"]!;
                return body["providerKey"]!.GetValue<string>() == "elsa.activity-graph";
            }
            var document = JsonNode.Parse(original.GetPayload(member.Key).Span)!;
            if (change == "provider-extra") document["content"]!["providerPayload"]!["futureOwnerData"] = true;
            else if (change == "provider-null") document["content"]!["providerPayload"]!["rootActivity"]!["inputs"]![0]!["value"]!["value"] = null;
            else if (change == "schema-one-outcome")
            {
                var body = document["content"]!; body["contract"]!["outcomes"]![0]!["referenceKey"] = "other";
                body["publication"]!["contractJson"] = body["contract"]!.ToJsonString();
            }
            else if (change == "placed-literal")
            {
                var binding = document["content"]!["executable"]!["rootActivity"]!["childSlots"]![0]!["activities"]![0]!["inputBindings"]!["text"]!;
                binding["literal"]!["inlineValue"] = "altered child"; binding["literalValue"] = "altered child";
            }
            else if (change == "provider-schema") document["content"]!["providerSchemaVersion"] = "unknown";
            else
            {
                var root = document["content"]!["executable"]!["rootActivity"]!;
                root["descriptorPayload"]!["definitionId"] = "other";
            }
            IReleaseContentReader reader = new Replaced(original, member, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
            if (change == "provider-null")
            {
                var activity = f.Codec.DecodeActivityVersion(original.GetPayload(member.Key));
                var templateMember = original.GetMember(new(ReleaseRuntimeCodecs.TemplateKind, activity.TemplateId));
                reader = AlterTemplate(f, reader, templateMember, template =>
                {
                    template["directDependencies"]![0]!["occurrenceOverlay"]!["inputBindings"]![0]!["value"]!["value"] = null;
                    template["root"]!["descriptorPayload"]!["occurrences"]![0]!["inputBindings"]![0]!["value"]!["value"] = null;
                });
            }
            var providerSite = kind == ReleaseRuntimeCodecs.ActivityVersionKind;
            var inspector = Graph(f); var nodeId = providerSite ? "$catalog"
                : f.Codec.DecodeWorkflow(reader.GetPayload(member.Key)).Executable.RootActivity.ExecutableNodeId;
            var key = providerSite ? new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Provider, "elsa.activity-graph", 1)
                : new(ReleaseRuntimeSiteKinds.Descriptor, WellKnownRuntimeActivityConsumers.GraphActivity, 1);
            var payload = providerSite ? JsonSerializer.SerializeToElement(new { versionId = member.Key.Identity }) : JsonSerializer.SerializeToElement(new { nodeId });
            var result = await inspector.InspectAsync(new(member.Key, [], nodeId, null, key, payload), reader);
            Assert.Equal(status, result.Status); Assert.Contains(result.Diagnostics, value => value.Code == code);
        });
    }

    [Fact]
    public async Task Schema_two_plan_requires_every_emitted_outcome_to_remain_reachable()
    {
        await WithCaptured("2", async (f, original) =>
        {
            var member = original.Members.Single(value => value.Key.Kind == ReleaseRuntimeCodecs.TemplateKind &&
                f.Codec.DecodeTemplate(original.GetPayload(value.Key)).Template.Root.Descriptor.ConsumerKey == WellKnownRuntimeActivityConsumers.GraphActivity);
            var reader = AlterTemplate(f, original, member, template =>
                template["root"]!["descriptorPayload"]!["outcomeMappings"] = new JsonArray());
            var nodeId = f.Codec.DecodeTemplate(reader.GetPayload(member.Key)).Template.Root.ExecutableNodeId;
            var result = await Graph(f).InspectAsync(new(member.Key, [], nodeId, null,
                new(ReleaseRuntimeSiteKinds.Descriptor, WellKnownRuntimeActivityConsumers.GraphActivity, 1), JsonSerializer.SerializeToElement(new { nodeId })), reader);
            Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
            Assert.Contains(result.Diagnostics, value => value.Code == "graph_outcome_coverage_mismatch");
        });
    }

    [Fact]
    public async Task Graph_registration_resolves_independent_overlay_owners_and_refuses_unclaimed_sites()
    {
        await using var f = await PublishedNativeFixture.Create(graphOwner: true);
        var graph = Graph(f); var unrelated = new UnrelatedConsumer();
        var owners = f.ServicesOf<IReleaseOccurrenceInspector>().Append(unrelated).ToArray();
        Assert.Same(graph, Assert.Single(owners.Where(value => value.SupportedSites.Contains(
            new(ReleaseRuntimeSiteKinds.TemplateOverlay, WellKnownRuntimeActivityConsumers.GraphActivity, 1)))));
        var unrelatedKey = unrelated.SupportedSites.Single();
        Assert.Same(unrelated, Assert.Single(owners.Where(value => value.SupportedSites.Contains(unrelatedKey))));
        // An unrelated consumer must be refused before any captured body is read.
        var result = await graph.InspectAsync(new(new("fixture", "missing"), [], "root", "child", unrelatedKey,
            JsonSerializer.SerializeToElement(new { nodeId = "root" })), new Joined());
        Assert.Equal(ReleaseSemanticStatus.Unsupported, result.Status);
        Assert.Contains(result.Diagnostics, value => value.Code == "graph_site_unsupported");
    }

    private static IReleaseContentReader AlterTemplate(PublishedNativeFixture f, IReleaseContentReader original, ReleaseContentMember member, Action<JsonNode> change)
    {
        var source = f.Codec.DecodeTemplate(original.GetPayload(member.Key)).Template;
        var document = JsonNode.Parse(original.GetPayload(member.Key).Span)!; var template = document["content"]!["template"]!;
        change(template); var root = template["root"]!; var payload = root["descriptorPayload"]!;
        root["activityContract"]!["descriptorPayload"] = payload.DeepClone();
        var contract = source.Root.ActivityContract!;
        var changedContract = new Elsa.Activities.Runtime.Core.Models.ActivityContract(contract.ActivityTypeKey, contract.ContractVersion, contract.DescriptorKind,
            JsonSerializer.SerializeToElement(payload), contract.Inputs.Values, contract.Result, contract.Outcomes, contract.Activation, contract.SideEffectProfile, contract.SupportsBackgroundExecution);
        root["activityContract"]!["schemaFingerprint"] = changedContract.SchemaFingerprint;
        return new Replaced(original, member, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
    }

    private static IReleaseOccurrenceInspector Graph(PublishedNativeFixture f) => f.ServicesOf<IReleaseOccurrenceInspector>().Single(value => value.Identity.Name == "elsa.graph-release-profile");
    private static async Task WithCaptured(string schema, Func<PublishedNativeFixture, IReleaseContentReader, Task> check)
    {
        await using var f = await PublishedNativeFixture.Create(graphOwner: true); var child = await f.PublishClrCatalog(); var graph = await f.PublishReusableGraph(child, schema);
        await f.SeedClrWorkflow(graph); await f.Publish("actual-clr-workflow-version");
        await f.Source().ExecuteAsync([new(Elsa.Workflows.Publishing.Core.Contracts.NativeReleaseCaptureSelections.WorkflowDefinitionVersion, "actual-clr-workflow-version", null, ReleaseRootAction.ActivateServing)], new(), async (root, token) =>
        {
            var member = Assert.Single(root.Content.Members.Where(value => value.Key.Kind == ReleaseRuntimeCodecs.WorkflowKind));
            await f.CatalogSource().ExecuteAsync([member], root.Content, new(), async (catalog, inner) => await check(f, new Joined(root.Content, catalog)), token);
        });
    }
    private sealed class Joined(params IReleaseContentReader[] readers) : IReleaseContentReader
    {
        public IReadOnlyList<ReleaseContentMember> Members => readers.SelectMany(value => value.Members).ToArray();
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => Members.SingleOrDefault(value => value.Key == key) ?? throw new KeyNotFoundException();
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => readers.Single(value => value.Members.Any(member => member.Key == key)).GetPayload(key);
    }
    private sealed class Replaced(IReleaseContentReader actual, ReleaseContentMember member, byte[] bytes) : IReleaseContentReader
    {
        private readonly ReleaseContentMember _replacement = new(new(member.Descriptor.Kind, member.Descriptor.Identity, member.Descriptor.Codec, member.Descriptor.CodecVersion,
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length), member.OwnerVersion);
        public IReadOnlyList<ReleaseContentMember> Members => actual.Members.Select(value => value.Key == member.Key ? _replacement : value).ToArray();
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => key == member.Key ? _replacement : actual.GetMember(key);
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => key == member.Key ? (byte[])bytes.Clone() : actual.GetPayload(key);
    }
    private sealed class UnrelatedConsumer : IReleaseOccurrenceInspector
    {
        public ReleaseSemanticAdapterIdentity Identity { get; } = new("fixture.unrelated-overlay", "1");
        public IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; } = [new(ReleaseRuntimeSiteKinds.TemplateOverlay, "fixture.unrelated", 1)];
        public ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken token = default) => ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Unsupported));
    }
}
