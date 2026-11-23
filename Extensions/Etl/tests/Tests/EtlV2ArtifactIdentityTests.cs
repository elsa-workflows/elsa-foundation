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

    // The approved S-9 migration pins each source projection through the shared exact JSON value contract.
    [Theory]
    [InlineData("httpJson", "962310e12e298598a58e2846826db07a1369ec563821b67327d98896fb66f2cc")]
    [InlineData("csv", "c7bf1e707ac6dfe25872e8a5317b1bee1b30fe77467c893503dfc06b48f2450e")]
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
        Assert.True(expectedHash == artifact.ContentHash, "First-stable hash freeze: changes require an explicit identity/migration decision.");
    }
}
