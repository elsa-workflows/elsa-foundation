using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

/// <summary>Portable wire and independent catalog projection checks; no actual publication or import witness.</summary>
public sealed class RuntimeCatalogEvidenceTests
{
    [Fact]
    public void Profile_two_preserves_original_fragments_and_known_global_owner_without_rewriting_them()
    {
        var fixture = Create(); var encoded = fixture.Content.Codec.EncodeActivityVersion(fixture.Version);
        Assert.Equal(2, encoded.Descriptor.CodecVersion);
        var decoded = fixture.Content.Codec.DecodeActivityVersion(encoded.GetPayloadBytes());
        Assert.NotNull(decoded.Publication); Assert.True(decoded.Publication.OwnerTenantKnown); Assert.Null(decoded.Publication.OwnerTenantId);
        Assert.Equal(fixture.Version.Publication!.ContractJson, decoded.Publication.ContractJson);
        Assert.Equal(fixture.Version.Publication.ProviderJson, decoded.Publication.ProviderJson);
        Assert.Equal(fixture.Version.Publication.Metadata.GetRawText(), decoded.Publication.Metadata.GetRawText());
        Assert.Equal(encoded.GetPayloadBytes(), fixture.Content.Codec.EncodeActivityVersion(decoded).GetPayloadBytes());
    }

    [Fact]
    public void Legacy_profile_one_keeps_its_original_wire_without_a_null_publication_field()
    {
        // Fixed profile-1 envelope from the pre-profile-2 contract at 4055dbc536. The qualification
        // pass also checks this exact case against that production baseline before applying codec changes.
        const string historical = """{"formatVersion":1,"content":{"scope":{"authority":"source","tenantId":"tenant","partition":"partition"},"definitionId":"definition","versionId":"version","version":"1.0.0","activityTypeKey":"fixture.Activity","resolutionKind":"AuthorableActivity","contract":{},"providerKey":"fixture","providerSchemaVersion":"1","providerPayload":{},"compilerFingerprint":"fingerprint","templateId":"template","templateHash":"hash","sourceReferenceId":"reference","sourceKind":"fixture","sourceId":"source"}}""";
        var content = new RuntimeReleaseFixture();
        var legacy = new ReleaseActivityVersion(new("source", "tenant", "partition"), "definition", "version", "1.0.0", "fixture.Activity",
            "AuthorableActivity", RuntimeReleaseFixture.Json("{}"), "fixture", "1", RuntimeReleaseFixture.Json("{}"), "fingerprint", "template", "hash", "reference", "fixture", "source");
        var expected = System.Text.Encoding.UTF8.GetBytes(historical);
        var encoded = content.Codec.EncodeActivityVersion(legacy); Assert.Equal(1, encoded.Descriptor.CodecVersion);
        Assert.Equal(expected, encoded.GetPayloadBytes());
        var restored = content.Codec.DecodeActivityVersion(expected); Assert.Null(restored.Publication);
        Assert.Equal(expected, content.Codec.EncodeActivityVersion(restored).GetPayloadBytes());
    }

    [Theory]
    [InlineData("valid", ReleaseSemanticStatus.Verified)]
    [InlineData("deep-ui", ReleaseSemanticStatus.Verified)]
    [InlineData("contract-extra", ReleaseSemanticStatus.Unsupported)]
    [InlineData("contract-schema", ReleaseSemanticStatus.Unsupported)]
    [InlineData("provider-alias", ReleaseSemanticStatus.Invalid)]
    [InlineData("provider-fingerprint", ReleaseSemanticStatus.Invalid)]
    [InlineData("original-provider", ReleaseSemanticStatus.Invalid)]
    [InlineData("original-contract", ReleaseSemanticStatus.Invalid)]
    [InlineData("metadata-count", ReleaseSemanticStatus.Invalid)]
    [InlineData("unknown-owner", ReleaseSemanticStatus.Unsupported)]
    [InlineData("metadata-extra", ReleaseSemanticStatus.Unsupported)]
    [InlineData("requirements", ReleaseSemanticStatus.Invalid)]
    [InlineData("measurements", ReleaseSemanticStatus.Invalid)]
    public async Task Actual_captured_catalog_sites_check_public_contract_provider_and_original_evidence(string change, ReleaseSemanticStatus expected)
    {
        var fixture = Create(change); var encoded = fixture.Content.Codec.EncodeActivityVersion(fixture.Version); var member = fixture.Content.Add(encoded);
        var facts = await fixture.Content.Verify(member); Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
        var inspector = new RuntimeCatalogReleaseInspector(fixture.Content.Cache);
        var answers = new List<ReleaseOccurrenceFacts>();
        foreach (var site in facts.Sites) answers.Add(await inspector.InspectAsync(site, fixture.Content));
        Assert.Equal(2, answers.Count);
        if (expected == ReleaseSemanticStatus.Verified) Assert.All(answers, answer => Assert.Equal(expected, answer.Status));
        else Assert.Contains(answers, answer => answer.Status == expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manifest_profile_cannot_hide_or_invent_original_publication_evidence(bool downgrade)
    {
        var fixture = Create(); var value = fixture.Version;
        if (!downgrade) value = new(value.Scope, value.DefinitionId, value.VersionId, value.Version, value.ActivityTypeKey, value.ResolutionKind,
            value.Contract, value.ProviderKey, value.ProviderSchemaVersion, value.ProviderPayload, value.CompilerFingerprint, value.TemplateId,
            value.TemplateHash, value.SourceReferenceId, value.SourceKind, value.SourceId);
        var encoded = fixture.Content.Codec.EncodeActivityVersion(value); var d = encoded.Descriptor;
        var member = fixture.Content.Add(new(new(d.Kind, d.Identity, d.Codec, downgrade ? 1 : 2, d.PayloadDigest, d.ByteLength), encoded.OwnerVersion, encoded.GetPayloadBytes()));
        var result = await fixture.Content.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "captured_runtime_codec_profile_conflict");
    }

    private static JsonElement DeepJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.Clone();
    }

    private static (RuntimeReleaseFixture Content, ReleaseActivityVersion Version) Create(string change = "valid")
    {
        const string providerKey = "elsa.clr-activity"; const string alias = "fixture.Activity";
        var payload = RuntimeReleaseFixture.Json("{\"typeAlias\":\"fixture.Activity\"}");
        var runtimeInputs = change == "deep-ui" ? new[] { new ActivityInputContract("value", "Value", new("String"), false, false, false, null, ActivityValuePolicy.Default) } : [];
        var runtime = new ActivityContract("fixture.RuntimeKey", "1", "clr", payload, runtimeInputs,
            new(new("Object"), false, ActivityValuePolicy.Default, []), ["Done"], new("clr", alias));
        var root = new ExecutableNode("node", "node", alias, "1", new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, "1", payload),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: runtime);
        var fingerprint = ActivityTemplateHasher.ComputeCanonicalValueHash(new
        { ProviderKey = providerKey, ProviderSchemaVersion = "1", ConsumerKey = root.Descriptor.ConsumerKey, ConsumerSchemaVersion = root.Descriptor.SchemaVersion, descriptor = payload });
        if (change == "provider-fingerprint") fingerprint = "sha256:" + new string('0', 64);
        var requirements = new[] { new RuntimeRequirement(root.Descriptor.ConsumerKey, root.Descriptor.SchemaVersion) };
        var resume = new Dictionary<string, WorkflowExecutableResumeTarget>(); var compatibility = new Dictionary<string, string>();
        var hash = ActivityTemplateHasher.Compute(root, resume, [], [], requirements, [], fingerprint, compatibility);
        var template = new ExecutableActivityTemplate("activity-template-" + hash[7..], hash, root, resume, [], [], requirements, fingerprint, compatibility, DateTimeOffset.UnixEpoch);
        var contract = JsonNode.Parse("{\"contractSchemaVersion\":\"1\",\"inputs\":[],\"outputs\":[],\"outcomes\":[{\"referenceKey\":\"done\",\"name\":\"Done\",\"isEmitted\":true,\"description\":null}]}")!;
        if (change == "deep-ui")
        {
            JsonNode ui = new JsonObject { ["label"] = "opaque" };
            for (var i = 0; i < 70; i++) ui = new JsonObject { ["child"] = ui };
            contract["inputs"] = new JsonArray(new JsonObject
            {
                ["referenceKey"] = "value", ["name"] = "Value", ["type"] = new JsonObject { ["alias"] = "String" },
                ["isRequired"] = false, ["isNullable"] = false, ["storageDriverKey"] = "json", ["uiSpecifications"] = ui
            });
        }
        if (change == "contract-extra") contract["future"] = new JsonObject();
        if (change == "contract-schema") contract["contractSchemaVersion"] = "future";
        var typedProvider = change == "provider-alias" ? RuntimeReleaseFixture.Json("{\"typeAlias\":\"other.Activity\"}") : payload;
        var providerJson = JsonSerializer.Serialize(new { providerKey, schemaVersion = "1", payload = typedProvider });
        var originalProvider = change == "original-provider" ? providerJson.Replace("fixture.Activity", "other.Activity") : providerJson;
        var originalContract = change == "original-contract" ? contract.ToJsonString(new JsonSerializerOptions { MaxDepth = 128 }).Replace("Done", "Other") : contract.ToJsonString(new JsonSerializerOptions { MaxDepth = 128 });
        var metadata = JsonSerializer.SerializeToNode(new
        {
            schemaVersion = 1, publicationId = "original-publication", originalResolutionKind = "AuthorableActivity", sourceDraftId = (string?)null,
            sourceVersionId = "original-version", lifecycle = "Active", publishedAt = "1970-01-01T00:00:00+00:00", directDependencyCount = 0,
            closedTemplateCount = 0, resumeTargetCount = 0, runtimeRequirementsJson = JsonSerializer.Serialize(requirements, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            resourceMeasurementsJson = "{\"localNodeCount\":1,\"closedNodeCount\":1,\"dependencyCount\":0,\"maximumObservedAuthoredDepth\":1,\"descriptorBytes\":1,\"layoutBytes\":0,\"estimatedDurableBoundarySlots\":1}"
        })!;
        if (change == "metadata-count") metadata["directDependencyCount"] = 1;
        if (change == "metadata-extra") metadata["future"] = true;
        if (change == "requirements") metadata["runtimeRequirementsJson"] = "[]";
        if (change == "measurements") metadata["resourceMeasurementsJson"] = metadata["resourceMeasurementsJson"]!.GetValue<string>().Replace("\"localNodeCount\":1", "\"localNodeCount\":-1");
        var evidence = new ReleaseActivityPublicationEvidence(change != "unknown-owner", null, " " + originalContract + " ", " " + originalProvider + " ", RuntimeReleaseFixture.Json(metadata.ToJsonString()));
        var value = new ReleaseActivityVersion(RuntimeReleaseFixture.Scope, "definition", "version", "1.0.0", "fixture.CatalogKey", "AuthorableActivity",
            DeepJson(contract.ToJsonString(new JsonSerializerOptions { MaxDepth = 128 })), providerKey, "1", typedProvider, fingerprint, template.TemplateId, template.TemplateHash,
            "original-reference", "ClrActivityType", alias, evidence);
        var content = new RuntimeReleaseFixture(); content.Add(content.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, template))); return (content, value);
    }
}
