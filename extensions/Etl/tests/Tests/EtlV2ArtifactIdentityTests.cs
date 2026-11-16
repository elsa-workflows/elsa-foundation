using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Etl.Tests;

public sealed class EtlV2ArtifactIdentityTests
{
    [Fact]
    public void Persisted_invocation_keeps_default_v2_pipeline_bytes_and_hash()
    {
        var compiled = new EtlArtifactCompiler().Compile("orders", 1, EtlV2TestPipelines.Create());
        Assert.Empty(compiled.Diagnostics);
        var artifact = Assert.IsType<EtlArtifact>(compiled.Artifact);
        Assert.False(artifact.Pipeline.GetProperty("http").TryGetProperty("maxRetainedMetadataBytes", out _));
        Assert.Equal(EtlArtifactJson.Hash(artifact.Pipeline), artifact.ContentHash);
        var invocation = EtlRuntimeFixtures.Admission("default-v2-persisted").Invocation with { Artifact = artifact };
        var persisted = EtlRuntimeRecordCodec.Write(invocation);

        var restored = EtlRuntimeRecordCodec.ReadInvocation(persisted);

        Assert.Equal(artifact.ContentHash, restored.Artifact.ContentHash);
        Assert.Equal(artifact.ArtifactId, restored.Artifact.ArtifactId);
        Assert.Equal(artifact.Pipeline.GetRawText(), restored.Artifact.Pipeline.GetRawText());
        Assert.Equal(64L * 1024 * 1024, EtlArtifactV2Json.Read(restored.Artifact.Pipeline).Http.MaxRetainedMetadataBytes);
        Assert.False(restored.Artifact.Pipeline.GetProperty("http").TryGetProperty("maxRetainedMetadataBytes", out _));
    }

    // Spec 1190 adds the "jsonl" source kind. These hashes of the canonical artifact bytes were verified on origin/main
    // before the addition, so they pin that CSV and HTTP JSON artifacts, and so their artifact ids, stay unchanged.
    [Theory]
    [InlineData("httpJson", "25cdcf9e8b97ac27d4f403d2158f0d468a47e8b47bfd677b69487ba13872dc21")]
    [InlineData("csv", "f2634391e2dd6fe33de1dff277c83562744e527ca4fac2c5800f9f1e280e5f5f")]
    public void Persisted_v2_artifacts_keep_their_canonical_hash_per_source_kind(string kind, string expectedHash)
    {
        var pipeline = EtlV2TestPipelines.Create();
        if (kind == "csv")
            pipeline = pipeline with
            {
                Source = new EtlCsvSource("source", [new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("name", EtlColumnType.String, true)]),
                Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors"))
            };

        var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 1, pipeline).Artifact);

        Assert.Equal(kind, artifact.Pipeline.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal(expectedHash, artifact.ContentHash);
    }
}
