using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

public sealed class RuntimeCatalogNullabilityEvidenceTests
{
    [Theory]
    [InlineData(false, false, false, ReleaseSemanticStatus.Verified)]
    [InlineData(false, true, false, ReleaseSemanticStatus.Verified)]
    [InlineData(true, false, false, ReleaseSemanticStatus.Invalid)]
    [InlineData(true, true, false, ReleaseSemanticStatus.Invalid)]
    [InlineData(false, false, true, ReleaseSemanticStatus.Invalid)]
    public async Task Public_contract_nullable_metadata_follows_its_owner_without_weakening_type_checks(
        bool clr, bool omitNullableFlag, bool wrongType, ReleaseSemanticStatus expected)
    {
        var fixture = NullabilityFixture(clr, omitNullableFlag, wrongType);
        var member = fixture.Content.Add(fixture.Content.Codec.EncodeActivityVersion(fixture.Version));
        var facts = await fixture.Content.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
        // The opaque authored provider has its own provider-site owner. This test scopes the shared public contract site.
        var site = Assert.Single(facts.Sites, value => value.Site.SiteKind == ReleaseRuntimeSiteKinds.PublicContract);
        var answer = await new RuntimeCatalogReleaseInspector(fixture.Content.Cache).InspectAsync(site, fixture.Content);
        Assert.Equal(expected, answer.Status);
        if (wrongType || clr && !omitNullableFlag)
            Assert.Contains(answer.Diagnostics, diagnostic => diagnostic.Code == "public_input_template_mismatch");
    }

    private static (RuntimeReleaseFixture Content, ReleaseActivityVersion Version) NullabilityFixture(
        bool clr, bool omitNullableFlag, bool wrongType)
    {
        var providerKey = clr ? "elsa.clr-activity" : "fixture.authored-provider";
        const string alias = "fixture.Activity";
        var payload = RuntimeReleaseFixture.Json("{\"typeAlias\":\"fixture.Activity\"}");
        var runtime = new ActivityContract(clr ? "fixture.RuntimeKey" : "fixture.CatalogKey", "1", "clr", payload,
            [new ActivityInputContract("value", "Value", new("String"), false, true, false, null, ActivityValuePolicy.Default)],
            new(new("Object"), false, ActivityValuePolicy.Default, []), ["Done"], new("clr", alias));
        var root = new ExecutableNode("node", "node", alias, "1",
            new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, "1", payload),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: runtime);
        var fingerprint = ActivityTemplateHasher.ComputeCanonicalValueHash(new
        {
            ProviderKey = providerKey, ProviderSchemaVersion = "1", ConsumerKey = root.Descriptor.ConsumerKey,
            ConsumerSchemaVersion = root.Descriptor.SchemaVersion, descriptor = payload
        });
        var requirements = new[] { new RuntimeRequirement(root.Descriptor.ConsumerKey, root.Descriptor.SchemaVersion) };
        var resume = new Dictionary<string, WorkflowExecutableResumeTarget>();
        var compatibility = new Dictionary<string, string>();
        var hash = ActivityTemplateHasher.Compute(root, resume, [], [], requirements, [], fingerprint, compatibility);
        var template = new ExecutableActivityTemplate("activity-template-" + hash[7..], hash, root, resume,
            [], [], requirements, fingerprint, compatibility, DateTimeOffset.UnixEpoch);
        var input = new JsonObject
        {
            ["referenceKey"] = "value", ["name"] = "Value",
            ["type"] = new JsonObject { ["alias"] = wrongType ? "Int32" : "String" },
            ["isRequired"] = false, ["storageDriverKey"] = "json"
        };
        if (!omitNullableFlag) input["isNullable"] = false;
        var contract = JsonNode.Parse("{\"contractSchemaVersion\":\"1\",\"inputs\":[],\"outputs\":[],\"outcomes\":[{\"referenceKey\":\"done\",\"name\":\"Done\",\"isEmitted\":true,\"description\":null}]}")!;
        contract["inputs"] = new JsonArray(input);
        var contractJson = contract.ToJsonString();
        var providerJson = JsonSerializer.Serialize(new { providerKey, schemaVersion = "1", payload });
        var metadata = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, publicationId = "original-publication", originalResolutionKind = "AuthorableActivity",
            sourceDraftId = (string?)null, sourceVersionId = "original-version", lifecycle = "Active",
            publishedAt = "1970-01-01T00:00:00+00:00", directDependencyCount = 0,
            closedTemplateCount = 0, resumeTargetCount = 0,
            runtimeRequirementsJson = JsonSerializer.Serialize(requirements, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            resourceMeasurementsJson = "{\"localNodeCount\":1,\"closedNodeCount\":1,\"dependencyCount\":0,\"maximumObservedAuthoredDepth\":1,\"descriptorBytes\":1,\"layoutBytes\":0,\"estimatedDurableBoundarySlots\":1}"
        });
        var evidence = new ReleaseActivityPublicationEvidence(true, null, " " + contractJson + " ", " " + providerJson + " ", metadata);
        var version = new ReleaseActivityVersion(RuntimeReleaseFixture.Scope, "definition", "version", "1.0.0",
            "fixture.CatalogKey", "AuthorableActivity", RuntimeReleaseFixture.Json(contractJson), providerKey, "1", payload,
            fingerprint, template.TemplateId, template.TemplateHash, "original-reference", "ClrActivityType", alias, evidence);
        var content = new RuntimeReleaseFixture();
        content.Add(content.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, template)));
        return (content, version);
    }
}
