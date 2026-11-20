using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

internal sealed class RuntimeReleaseFixture : IReleaseContentReader
{
    public static ReleaseRuntimeContentScope Scope { get; } = new("source-authority", "source-tenant", null);
    public ReleaseRuntimeArtifactCodec Codec { get; } = new();
    private RuntimeCapturedContentCache? _cache;
    public RuntimeCapturedContentCache Cache => _cache ??= new(Codec);
    private readonly Dictionary<ReleaseMemberKey, ReleaseEncodedRuntimeMember> _encoded = [];
    public IReadOnlyList<ReleaseContentMember> Members => _encoded.Values.Select(x => new ReleaseContentMember(x.Descriptor, x.OwnerVersion)).ToArray();
    public ReleaseContentMember Add(ReleaseEncodedRuntimeMember value)
    {
        var key = new ReleaseMemberKey(value.Descriptor.Kind, value.Descriptor.Identity); _encoded.Add(key, value); return GetMember(key);
    }
    public ReleaseContentMember GetMember(ReleaseMemberKey key) => _encoded.TryGetValue(key, out var value) ? new(value.Descriptor, value.OwnerVersion) : throw new KeyNotFoundException();
    public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => _encoded[key].GetPayloadBytes();
    public ReleaseRuntimeArtifactVerifier Verifier => new(Cache, new WorkflowExecutableHasher());
    public ValueTask<ReleaseSemanticFacts> Verify(ReleaseContentMember member) => Verifier.VerifyAsync(member, GetPayload(member.Key), this);

    public static ExecutableNode Node(string id = "root", IReadOnlyDictionary<string, RuntimeInputBinding>? bindings = null,
        IReadOnlyDictionary<string, string>? metadata = null, IReadOnlyCollection<ExecutableChildSlot>? children = null,
        string? consumer = null, JsonElement? payload = null, WorkflowIntrinsicKind? intrinsic = WorkflowIntrinsicKind.Return,
        ExecutableActivityStructure? structure = null) => new(id, id, intrinsic is null ? "fixture.Module" : "elsa.intrinsic.return", "1.0.0",
        consumer ?? "intrinsic", payload ?? Json("{\"kind\":\"Return\",\"schemaVersion\":\"1.0.0\"}"), bindings ?? new Dictionary<string, RuntimeInputBinding>
        {
            ["value"] = new("value", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(new("String"), Json("\"unchanged\""), ValueProtectionPolicy.InstanceInline))
        }, metadata ?? new Dictionary<string, string> { ["authoredNodeId"] = id, ["executionType"] = "Action" }, children, structure,
        intrinsicKind: intrinsic);

    public static WorkflowExecutable Workflow(ExecutableNode? root = null, IReadOnlyCollection<WorkflowExecutableDependency>? dependencies = null,
        WorkflowExecutableIdentity? identity = null)
    {
        root ??= Node(); var contract = new WorkflowExecutableInputContract(1, []); var edges = dependencies ?? [];
        var hasher = new WorkflowExecutableHasher(); var hash = hasher.ComputeHash(root, contract, edges);
        return new(identity ?? new(hasher.CreateArtifactId("workflow", hash), "producer-definition", "producer-version", "1.0.0", hash), root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), contract, edges, IncidentStrategyBuiltIns.FaultReference);
    }
    public static ExecutableActivityTemplate Template(ExecutableNode? root = null, IReadOnlyCollection<ExecutableActivityTemplateDependency>? dependencies = null,
        IReadOnlyCollection<ExecutableActivityTemplateIdentity>? closed = null)
    {
        root ??= Node(); var resume = new Dictionary<string, WorkflowExecutableResumeTarget>();
        var requirements = root.DescendantsAndSelf().Select(x => new Elsa.Activities.Runtime.Core.Models.RuntimeRequirement(x.Descriptor.ConsumerKey, x.Descriptor.SchemaVersion)).Distinct().ToArray();
        if (root.DescendantsAndSelf().Any(x => x.InputBindings.Values.Any(binding => binding.Selection is not null)))
            requirements = requirements.Append(new(RuntimeReadSelection.RequirementKey, RuntimeReadSelection.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var hash = ActivityTemplateHasher.Compute(root, resume, dependencies ?? [], closed ?? [], requirements, [], "fixture-provider/compiler/1", new Dictionary<string, string>());
        return new("activity-template-" + hash[7..], hash, root, resume, dependencies ?? [], closed ?? [], requirements, "fixture-provider/compiler/1", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
    }
    public static RuntimeModelMaterial Model(string protocol = "fixture.protocol", int version = 1)
    {
        var pin = new WorkflowExecutableIdentity("original-model-id", "model-producer", "model-version", "1.0.0", "sha256:" + new string('0', 64));
        var model = new RuntimeModelMaterial(1, pin, new(protocol, version), "fixture-profile", Json("{\"innerFormat\":7}"), new(1, []), [], [], []);
        return new(1, pin with { ArtifactHash = model.ComputeContentHash() }, model.Protocol, model.Profile, model.Definition, model.InputContract, model.Outputs, model.Dependencies, model.RuntimeRequirements);
    }
    public static ReleaseWorkflowSelection Selection(WorkflowExecutable workflow) => new(Scope, "selected-definition", "selected-version", "2.0.0",
        "{\"rootActivity\":{\"opaque\":true}}", "WorkflowDefinitionVersion", "original-source-version", "1.0.0", "original-source-reference",
        WorkflowExecutableReferenceScope.Published, workflow.Identity);
    public static ReleaseActivityVersion ActivityVersion(ExecutableActivityTemplate template, string versionId = "activity-version", JsonElement? providerPayload = null) => new(Scope,
        "activity-definition", versionId, "1.0.0", "fixture.Activity", "ReusableTemplateBoundary", Json("{\"inputs\":[],\"outputs\":[]}"),
        "fixture-provider", "1", providerPayload ?? Json("{\"z\":1,\"a\":2}"), template.ProviderFingerprint, template.TemplateId, template.TemplateHash,
        "original-activity-source", "DesignActivity", "original-activity-source-id");
    public static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }

    public async Task<IReleaseBundleTransport> Transport(IReleaseBundleCodec transport, ReleaseMemberKey selection, ReleaseMemberKey content)
    {
        var facts = new List<ReleaseSemanticFacts>(); foreach (var member in Members) facts.Add(await Verify(member));
        var manifest = new JsonObject
        {
            ["format"] = "elsa.workflow-release", ["formatVersion"] = 1, ["profile"] = "native-bpmn-etl/1",
            ["roots"] = new JsonArray(new JsonObject { ["member"] = Key(selection), ["content"] = Key(content), ["action"] = "activate-serving" }),
            ["members"] = new JsonArray(Members.Select(x => (JsonNode)new JsonObject
            {
                ["key"] = Key(x.Key), ["codec"] = x.Codec.Codec, ["codecVersion"] = x.Codec.CodecVersion,
                ["ownerVersion"] = x.OwnerVersion is null ? null : new JsonObject { ["scheme"] = x.OwnerVersion.Scheme, ["value"] = x.OwnerVersion.TextValue },
                ["payloadDigest"] = x.Descriptor.PayloadDigest, ["byteLength"] = x.Descriptor.ByteLength
            }).ToArray()),
            ["edges"] = new JsonArray(facts.SelectMany(x => x.Edges).Select(x => (JsonNode)new JsonObject
            { ["from"] = Key(x.From), ["to"] = Key(x.To), ["relation"] = x.Relation == ReleaseEdgeRelation.Dependency ? "dependency" : "association", ["occurrence"] = x.Occurrence }).ToArray()),
            ["prerequisites"] = new JsonArray(facts.SelectMany(x => x.Prerequisites).Select(x => (JsonNode)new JsonObject
            { ["key"] = x.Key, ["member"] = Key(x.Member), ["kind"] = x.Kind, ["identity"] = x.Identity, ["scheme"] = x.Scheme, ["constraint"] = x.Constraint }).ToArray()),
            ["resourceRequirements"] = new JsonArray(facts.SelectMany(x => x.Resources).Select(x => (JsonNode)new JsonObject
            { ["key"] = new JsonObject { ["member"] = Key(x.Member), ["origin"] = new JsonArray(x.Origin.Select(y => (JsonNode)JsonValue.Create(y)!).ToArray()), ["reference"] = x.Reference, ["kind"] = x.Kind }, ["schemaVersion"] = x.SchemaVersion }).ToArray())
        };
        return transport.Validate(System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()), _encoded.Values.ToDictionary(x => x.Descriptor.PayloadDigest, x => (ReadOnlyMemory<byte>)x.GetPayloadBytes()));
    }
    private static JsonObject Key(ReleaseMemberKey value) => new() { ["kind"] = value.Kind, ["identity"] = value.Identity };
}
