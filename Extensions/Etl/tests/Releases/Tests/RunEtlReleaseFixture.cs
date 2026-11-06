using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Releases.Core;
using Elsa.Etl.Releases.Workflows;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Releases.Tests;

internal sealed class RunEtlReleaseFixture : IReleaseContentReader
{
    private readonly Dictionary<ReleaseMemberKey, (ReleaseContentMember Member, byte[] Payload)> _members = [];
    public static ReleaseRuntimeContentScope SourceScope { get; } = new("source-environment", "tenant-a", "partition-distinct");
    public static JsonSerializerOptions OwnerJson { get; } = new(JsonSerializerDefaults.Web);
    public ReleaseMemberKey Containing { get; }
    public EtlArtifact Etl { get; }
    public WorkflowExecutable? Workflow { get; }
    public ExecutableActivityTemplate? Template { get; }
    public ExecutableNode Node { get; }
    public EtlEncodedReleaseArtifact EtlBytes { get; }
    public int EtlPayloadReads { get; private set; }
    public int EtlMemberLookups { get; private set; }
    public bool RefuseCatalogEnumeration { get; set; }
    public IReadOnlyList<ReleaseContentMember> Members => RefuseCatalogEnumeration
        ? throw new InvalidOperationException("Captured lookup must use the bounded exact keys.")
        : _members.Values.Select(x => x.Member).ToArray();

    public RunEtlReleaseFixture(IServiceProvider services, string? change = null, bool template = false, int format = 1)
    {
        var codec = services.GetRequiredService<IEtlReleaseArtifactCodec>();
        var runtimeCodec = services.GetRequiredService<IReleaseRuntimeArtifactCodec>();
        Etl = format == 1 ? EtlReleaseFixture.Artifact(EtlReleaseFixture.V1()) : EtlReleaseFixture.Artifact(EtlReleaseFixture.V2());
        var source = EtlReleaseFixture.Release(Etl).Source;
        source = change switch
        {
            "scope-authority" => new("another-authority", source.Scope),
            "scope-tenant" => new(source.Authority, new("another-tenant", source.Scope.PersistencePartition)),
            "scope-partition" => new(source.Authority, new(source.Scope.TenantId, "another-partition")),
            _ => source
        };
        EtlBytes = codec.Encode(new(source, Etl));
        if (change != "captured-missing") Add(new(EtlBytes.Descriptor, EtlBytes.OwnerVersion), EtlBytes.GetPayloadBytes());
        var selection = new EtlPublishedSelection(Etl.DefinitionId, change == "selection-version" ? 38 : Etl.DefinitionVersion,
            change == "selection-hash" ? new string('a', 64) : Etl.ContentHash).ToToken();
        if (change == "selection-format") selection = "etl2:" + selection[5..];
        if (change == "selection-malformed") selection = "etl1:malformed";
        var metadata = new Dictionary<string, string>
        {
            ["authoredNodeId"] = "authored-etl", ["executionType"] = "Action",
            ["etl.pipeline.v1"] = JsonSerializer.Serialize(Etl, OwnerJson)
        };
        if (change is "missing-pin" or "template-static") metadata.Remove("etl.pipeline.v1");
        if (change == "pin-hash")
        {
            var pin = JsonNode.Parse(metadata["etl.pipeline.v1"])!; pin["contentHash"] = new string('a', 64);
            metadata["etl.pipeline.v1"] = pin.ToJsonString();
        }
        if (change == "pin-other-version") metadata["etl.pipeline.v1"] = JsonSerializer.Serialize(EtlReleaseFixture.Artifact(EtlReleaseFixture.V1(), 38), OwnerJson);
        if (change == "pin-duplicate") metadata["etl.pipeline.v1"] = "{\"definitionId\":\"orders\"," + metadata["etl.pipeline.v1"][1..];
        if (change is "runtime.structuralFaultHandling" or "runtime.acceptsRescheduledFaultedChild" or "runtime.childControlConsumerKey" or "runtime.childControlConsumerVersion")
            metadata[change] = change == "runtime.structuralFaultHandling" ? "child" : "true";
        var type = new ValueTypeDescriptor("String");
        var binding = change == "dynamic" ? new RuntimeInputBinding(nameof(RunEtl.Pipeline), type, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new("pipeline"))
            : new RuntimeInputBinding(nameof(RunEtl.Pipeline), type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(selection), ValueProtectionPolicy.InstanceInline),
                conversionPlan: change == "identity-conversion" ? ValueConversionPlan.Identity(type, ValueRepresentation.TextValue) : null);
        var nodeVersion = template ? "1" : "1.0.0";
        Node = new("node-etl", "authored-etl", RunEtl.ActivityType, nodeVersion, WellKnownRuntimeActivityConsumers.ClrActivity,
            JsonSerializer.SerializeToElement(new { typeAlias = RunEtl.ActivityType }), new Dictionary<string, RuntimeInputBinding> { [nameof(RunEtl.Pipeline)] = binding }, metadata,
            activityContract: Contract(nodeVersion, change));
        var target = new WorkflowExecutableResumeTarget("opaque-resume-placement", Node.ExecutableNodeId, "ResumeAsync", new Dictionary<string, string>(), "etl-completed");
        var resume = new Dictionary<string, WorkflowExecutableResumeTarget> { [target.ResumeTargetId] = target };
        switch (change)
        {
            case "resume-missing": resume.Clear(); break;
            case "resume-handler": resume[target.ResumeTargetId] = target with { HandlerKey = "OtherHandler" }; break;
            case "resume-node": resume[target.ResumeTargetId] = target with { ExecutableNodeId = "other-node" }; break;
            case "resume-local": resume[target.ResumeTargetId] = target with { LocalResumeTargetId = "other-local" }; break;
            case "resume-map": resume.Clear(); resume["wrong-map-key"] = target; break;
            case "resume-extra": resume["extra"] = target with { ResumeTargetId = "extra" }; break;
            case "resume-direct-foreign": resume["etl-completed"] = target with { ResumeTargetId = "etl-completed", ExecutableNodeId = "other-node" }; break;
            case "resume-legacy": resume.Clear(); resume["etl-completed"] = target with { ResumeTargetId = "etl-completed", LocalResumeTargetId = null }; break;
        }
        if (template)
        {
            var requirements = new[] { new RuntimeRequirement(WellKnownRuntimeActivityConsumers.ClrActivity, "1") };
            var hash = ActivityTemplateHasher.Compute(Node, resume, [], [], requirements, [], "fixture/RunEtl/compiler/1", new Dictionary<string, string>());
            Template = new("activity-template-" + hash[7..], hash, Node, resume, [], [], requirements, "fixture/RunEtl/compiler/1", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
            var encoded = runtimeCodec.EncodeTemplate(new(SourceScope, Template)); Containing = Add(new(encoded.Descriptor, encoded.OwnerVersion), encoded.GetPayloadBytes());
        }
        else
        {
            var dependencies = change is "generic-agree" or "generic-conflict"
                ? new[] { new WorkflowExecutableDependency(change == "generic-agree" ? Etl.ArtifactId : "another-id", Etl.ContentHash, [Node.ExecutableNodeId]) } : [];
            var inputContract = new WorkflowExecutableInputContract(1, []);
            var hasher = services.GetRequiredService<IWorkflowExecutableHasher>(); var hash = hasher.ComputeHash(Node, inputContract, dependencies);
            Workflow = new(new(hasher.CreateArtifactId("workflow", hash), "producer-definition", "producer-version", "1.0.0", hash), Node, resume,
                DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), inputContract, dependencies, IncidentStrategyBuiltIns.FaultReference);
            var encoded = runtimeCodec.EncodeWorkflow(new(SourceScope, Workflow)); Containing = Add(new(encoded.Descriptor, encoded.OwnerVersion), encoded.GetPayloadBytes());
        }
    }

    public ReleaseSemanticSite Site(bool metadata) => new(Containing, [], Node.ExecutableNodeId, null,
        new(metadata ? ReleaseRuntimeSiteKinds.Metadata : ReleaseRuntimeSiteKinds.ClrActivity,
            metadata ? "etl.pipeline.v1" : RunEtl.ActivityType, 1), JsonSerializer.SerializeToElement(new { nodeId = Node.ExecutableNodeId }));
    public ReleaseContentMember GetMember(ReleaseMemberKey key)
    {
        if (key.Kind == EtlReleaseCodecs.MemberKind) EtlMemberLookups++;
        return _members.TryGetValue(key, out var value) ? value.Member : throw new KeyNotFoundException();
    }
    public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key)
    {
        if (key.Kind == EtlReleaseCodecs.MemberKind) EtlPayloadReads++;
        return (byte[])_members[key].Payload.Clone();
    }
    private ReleaseMemberKey Add(ReleaseContentMember member, byte[] payload) { _members.Add(member.Key, (member, payload)); return member.Key; }

    public ReleaseMemberKey AddSelection(IServiceProvider services)
    {
        var value = new ReleaseWorkflowSelection(SourceScope, "selected-definition", "selected-version", "2.0.0", "{}", "WorkflowDefinitionVersion",
            "source-version", "1.0.0", "source-reference", WorkflowExecutableReferenceScope.Published, Workflow!.Identity);
        var bytes = services.GetRequiredService<IReleaseRuntimeArtifactCodec>().EncodeWorkflowSelection(value);
        return Add(new(bytes.Descriptor, bytes.OwnerVersion), bytes.GetPayloadBytes());
    }

    public static ServiceProvider Services(bool addon = true) => EtlReleaseFixture.Services(services =>
    {
        services.AddSingleton<IWorkflowExecutableHasher, WorkflowExecutableHasher>();
        var runtime = new WorkflowReleaseRuntimeFeature(); runtime.ConfigureServices(services); runtime.PostConfigureServices(services);
        if (addon) new EtlReleaseWorkflowsFeature().ConfigureServices(services);
    });

    private static ActivityContract Contract(string version, string? change)
    {
        var descriptor = typeof(ClrActivityDescriptor).FullName!;
        var input = new ActivityInputContract(nameof(RunEtl.Pipeline), nameof(RunEtl.Pipeline), new("String"),
            change != "input-required", change == "input-nullable", false, null, ActivityValuePolicy.Default);
        var resultType = new ValueTypeDescriptor(change == "result-type" ? "String" : typeof(RunEtlResult).FullName!);
        var projections = change == "result-projection" ? new[] { new ActivityResultProjectionContract("invented", "runId", new("String"), true, ActivityValuePolicy.Default) } : [];
        return new(RunEtl.ActivityType, change == "contract-version" ? "9" : version, descriptor, JsonSerializer.SerializeToElement(new { typeAlias = RunEtl.ActivityType }),
            [input], new ActivityResultContract(resultType, change != "result-required", ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result }, projections, ValueRepresentation.TypedValue),
            change == "outcomes" ? ["Done"] : ["Done", "BusinessError"], new(descriptor, RunEtl.ActivityType),
            change == "replay-safe" ? SideEffectProfile.ReplaySafe : SideEffectProfile.External, change == "background-safe");
    }
}
