using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Etl.Core;
using Elsa.Etl.Releases.Core;
using Elsa.Workflows.Publishing.Releases;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Releases.Tests;

internal static class EtlReleaseFixture
{
    public static EtlPipeline V1() => new(new("source", "lookup", "output", "errors"),
        [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, false)],
        [new("id", EtlColumnType.Int64, true), new("category", EtlColumnType.String, false)],
        "id", "id", [new("name", EtlFieldOrigin.Source, "name")], [], new());

    public static EtlPipelineV2 V2() => new(
        new EtlHttpJsonSource("source", "/records", [new("id", "/id", EtlColumnType.Int64, true), new("name", "/name", EtlColumnType.String, false)],
            new EtlTokenPagination("/next", "cursor")), null,
        new(new EtlHttpDestination("output"), new EtlJsonlDestination("errors")),
        [new("name", EtlFieldOrigin.Source, "name")], [], new(), new());

    public static EtlArtifact Artifact(EtlPipeline pipeline, int version = 37) => Artifact(1, EtlArtifactJson.Write(pipeline), version);
    public static EtlArtifact Artifact(EtlPipelineV2 pipeline, int version = 37) => Artifact(2, EtlArtifactV2Json.Write(pipeline), version);
    public static EtlArtifact Artifact(int format, JsonElement pipeline, int version = 37)
    {
        var hash = EtlArtifactJson.Hash(pipeline);
        return new(format, EtlArtifactJson.CreateArtifactId(format, "orders", version, hash), hash, "orders", version, pipeline);
    }
    public static EtlReleaseArtifact Release(EtlArtifact artifact) => new(new("source-environment", new("tenant-a", "partition-distinct")), artifact);
    public static ServiceProvider Services(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        var transport = new WorkflowReleaseTransportFeature(); transport.ConfigureServices(services); transport.PostConfigureServices(services);
        var verification = new WorkflowReleaseVerificationFeature(); verification.ConfigureServices(services); verification.PostConfigureServices(services);
        var etl = new EtlReleaseVerificationFeature(); etl.ConfigureServices(services); configure?.Invoke(services); etl.PostConfigureServices(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
    public static JsonObject Manifest(EtlEncodedReleaseArtifact encoded, EtlArtifact artifact)
    {
        var descriptor = encoded.Descriptor;
        var resources = new JsonArray();
        void Resource(string[] origin, string reference, string kind) => resources.Add(new JsonObject
        {
            ["key"] = new JsonObject { ["member"] = Key(artifact.ArtifactId), ["origin"] = new JsonArray(origin.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["reference"] = reference, ["kind"] = "etl.binding." + kind },
            ["schemaVersion"] = "1"
        });
        var capacities = new Dictionary<string, string>(StringComparer.Ordinal);
        var capabilities = new List<string>();
        if (artifact.FormatVersion == 1)
        {
            Resource(["bindings", "source"], "source", "csv.source"); Resource(["bindings", "lookup"], "lookup", "csv.lookup");
            Resource(["bindings", "output"], "output", "jsonl.main-output"); Resource(["bindings", "errors"], "errors", "jsonl.error-output");
            capabilities.AddRange(["etl.artifact.v1", "etl.connector.csv", "etl.connector.jsonl"]);
            capacities = new(StringComparer.Ordinal)
            {
                ["max-partitions"] = "2", ["capacity-records"] = "1000", ["capacity-input-bytes"] = "4194304", ["capacity-output-bytes"] = "8388608",
                ["max-lookup-records"] = "100000", ["max-lookup-cells"] = "200000", ["capacity-lookup-bytes"] = "16777216", ["max-snapshot-bytes"] = "1073741824"
            };
        }
        else
        {
            Resource(["source"], "source", "http.source"); Resource(["routes", "main"], "output", "http.main-output"); Resource(["routes", "errors"], "errors", "jsonl.error-output");
            capabilities.AddRange(["etl.artifact.v2", "etl.connector.http", "etl.connector.jsonl"]);
            capacities = new(StringComparer.Ordinal)
            {
                ["max-partitions"] = "32", ["capacity-records"] = "10000", ["capacity-input-bytes"] = "8388608", ["capacity-output-bytes"] = "8388608",
                ["max-snapshot-bytes"] = "1073741824", ["max-retained-output-bytes"] = "1073741824", ["http-max-page-bytes"] = "8388608",
                ["http-max-page-records"] = "10000", ["http-max-json-depth"] = "32", ["http-max-continuation-bytes"] = "8192", ["http-max-pages-per-partition"] = "10000",
                ["http-max-retained-input-bytes"] = "1073741824", ["http-max-retained-metadata-bytes"] = "67108864", ["http-max-attempts"] = "5",
                ["http-max-deadline-seconds"] = "900", ["http-max-request-timeout-seconds"] = "30", ["http-max-delay-seconds"] = "60"
            };
        }
        var prerequisites = new JsonArray();
        foreach (var capability in capabilities) prerequisites.Add(Prerequisite("etl/" + artifact.ArtifactId + "/" + capability, artifact.ArtifactId, "runtime-capability", capability, "exact", "1"));
        foreach (var (name, minimum) in capacities) prerequisites.Add(Prerequisite("etl/" + artifact.ArtifactId + "/etl.limit." + name, artifact.ArtifactId, "runtime-capacity", "etl." + name, "integer-range", "[" + minimum + ",)"));
        return new JsonObject
        {
            ["format"] = "elsa.workflow-release", ["formatVersion"] = 1, ["profile"] = "native-bpmn-etl/1",
            ["roots"] = new JsonArray(new JsonObject { ["member"] = Key(artifact.ArtifactId), ["content"] = Key(artifact.ArtifactId), ["action"] = "make-available" }),
            ["members"] = new JsonArray(new JsonObject
            {
                ["key"] = Key(artifact.ArtifactId), ["codec"] = descriptor.Codec, ["codecVersion"] = descriptor.CodecVersion,
                ["ownerVersion"] = new JsonObject { ["scheme"] = "integer", ["value"] = artifact.DefinitionVersion },
                ["payloadDigest"] = descriptor.PayloadDigest, ["byteLength"] = descriptor.ByteLength
            }),
            ["edges"] = new JsonArray(), ["resourceRequirements"] = resources, ["prerequisites"] = prerequisites
        };
    }
    public static JsonObject Key(string id) => new() { ["kind"] = EtlReleaseCodecs.MemberKind, ["identity"] = id };
    public static JsonObject Prerequisite(string key, string id, string kind, string identity, string scheme, string constraint) => new()
    { ["key"] = key, ["member"] = Key(id), ["kind"] = kind, ["identity"] = identity, ["scheme"] = scheme, ["constraint"] = constraint };
    public static IReleaseBundleTransport Bundle(IReleaseBundleCodec codec, JsonObject manifest, EtlEncodedReleaseArtifact encoded) =>
        codec.Validate(Encoding.UTF8.GetBytes(manifest.ToJsonString()), new Dictionary<string, ReadOnlyMemory<byte>> { [encoded.Descriptor.PayloadDigest] = encoded.GetPayloadBytes() });
    public static string Hash(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}
