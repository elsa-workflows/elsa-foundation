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
}
