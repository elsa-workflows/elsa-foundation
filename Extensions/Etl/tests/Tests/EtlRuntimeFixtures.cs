using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests;

internal static class EtlRuntimeFixtures
{
    internal static readonly string Hash = new('a', 64);

    public static EtlRunAdmission Admission(string runId = "run-1", EtlScope? scope = null)
    {
        var pipeline = EtlArtifactJson.Write(EtlTestPipelines.Create());
        var artifact = new EtlArtifact(1, "artifact-1", EtlArtifactJson.Hash(pipeline), "orders", 1, pipeline);
        var address = new EtlNativeResultAddress(1, "workflow-1", "activity-1", "executable-1", Hash,
            "node-1", "bookmark-1", "registration-1", "target-1", "etl-completion", Hash,
            "etl-result", 1, "once", EtlNativeResultAddress.CompletionProviderId);
        return new(new(scope ?? new EtlScope("tenant-a", "partition-a"), runId, artifact, address), new(
            new("source", "v1", Hash, Hash), new("lookup", "v1", Hash, Hash),
            new("output", "v1", Hash, null), new("errors", "v1", Hash, null)));
    }

    public static EtlPreparedRun Prepared(long recordCount = 4) => new(1,
        new(Hash + ".csv", Hash, 128), new(new string('b', 64) + ".csv", Hash, 64),
        new(new string('c', 64) + ".index", Hash, checked((recordCount + 1) * 24)), recordCount);
    public static EtlConnectorRunAdmission ConnectorAdmission(string runId, EtlScope scope,
        EtlPipelineV2? pipeline = null, IReadOnlyList<string>? httpShardIds = null)
    {
        pipeline ??= EtlV2TestPipelines.Create();
        var artifactDocument = EtlArtifactV2Json.Write(pipeline);
        var contentHash = EtlArtifactJson.Hash(artifactDocument);
        var artifact = new EtlArtifact(EtlArtifactV2Json.FormatVersion,
            EtlArtifactJson.CreateArtifactId(EtlArtifactV2Json.FormatVersion, "orders", 1, contentHash),
            contentHash, "orders", 1, artifactDocument);
        var invocation = EtlRuntimeFixtures.Admission(runId, scope).Invocation with { Artifact = artifact };
        var source = pipeline.Source switch
        {
            EtlCsvSource csv => ConnectorBinding(EtlConnectorKind.Csv, csv.Binding, EtlRuntimeFixtures.Hash),
            EtlHttpJsonSource http => ConnectorBinding(EtlConnectorKind.Http, http.Binding),
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline))
        };
        var lookup = pipeline.Lookup is { } declaredLookup
            ? ConnectorBinding(EtlConnectorKind.Csv, declaredLookup.Binding, EtlRuntimeFixtures.Hash)
            : null;
        var shardIds = httpShardIds ?? (pipeline.Source is EtlHttpJsonSource
            ? new[] { "shard-a", "shard-b" }
            : Array.Empty<string>());
        var bindings = new EtlConnectorRunBindings(source, lookup,
            ConnectorDestinationBinding(pipeline.Routes.Main), ConnectorDestinationBinding(pipeline.Routes.Errors), shardIds);
        return new(invocation, bindings);
    }

    private static EtlConnectorBindingRevision ConnectorBinding(EtlConnectorKind kind, string alias,
        string? expectedSha256 = null, string revision = "source-revision-1") =>
        new(kind, new EtlBindingRevision(alias, revision, $"{alias}-handle", expectedSha256));

    private static EtlConnectorBindingRevision ConnectorDestinationBinding(EtlDestination destination) => destination switch
    {
        EtlHttpDestination http => ConnectorBinding(EtlConnectorKind.Http, http.Binding, revision: "destination-revision-1"),
        EtlJsonlDestination jsonl => ConnectorBinding(EtlConnectorKind.Jsonl, jsonl.Binding, revision: "destination-revision-1"),
        _ => throw new ArgumentOutOfRangeException(nameof(destination))
    };

}
