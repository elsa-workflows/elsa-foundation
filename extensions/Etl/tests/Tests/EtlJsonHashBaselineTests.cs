using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Execution;

namespace Elsa.Etl.Tests;

public sealed class EtlJsonHashBaselineTests
{
    [Fact]
    public void Bounded_batch_counts_actual_bytes_and_hashes_the_exact_json_value()
    {
        const string first = "{ \"text\": \"\\u0061\", \"n\": 1.0 }";
        const string same = "{\"n\":1e0,\"text\":\"a\"}";
        using var a = new EtlBoundedHashingStream(1000);
        using var b = new EtlBoundedHashingStream(1000);
        a.Write(Encoding.UTF8.GetBytes(first)); b.Write(Encoding.UTF8.GetBytes(same));
        Assert.Equal(Encoding.UTF8.GetByteCount(first), a.ByteLength);
        Assert.NotEqual(a.ByteLength, b.ByteLength);
        Assert.Equal(a.ContentHash(), b.ContentHash());
        Assert.True(a.ContentHash() == "f14a446e28dada50496083f909c072c9c6042b62bfcd7bb550f2c78d26ff1970",
            $"First-stable hash freeze: change requires an explicit identity/migration decision; actual {a.ContentHash()}");
        using var limited = new EtlBoundedHashingStream(Encoding.UTF8.GetByteCount(first) - 1);
        Assert.Throws<EtlPayloadLimitExceededException>(() => limited.Write(Encoding.UTF8.GetBytes(first)));
    }

    [Theory]
    [InlineData("{\"n\":1.0,\"text\":\"\\u0061\"}", "{\"text\":\"a\",\"n\":1e0}", true)]
    [InlineData("{}", "{\"nil\":null}", false)]
    [InlineData("[1,2]", "[2,1]", false)]
    [InlineData("100000000000000000000000000000000000001", "100000000000000000000000000000000000002", false)]
    public void Invocation_and_result_envelope_hashes_follow_exact_json_equality(string left, string right, bool equal)
    {
        var invocation = EtlRuntimeFixtures.Admission().Invocation;
        EtlInvocation WithPipeline(string value)
        {
            using var json = JsonDocument.Parse(value);
            var artifact = invocation.Artifact;
            return invocation with { Artifact = new EtlArtifact(artifact.FormatVersion, artifact.ArtifactId, artifact.ContentHash,
                artifact.DefinitionId, artifact.DefinitionVersion, json.RootElement) };
        }
        Assert.Equal(equal, EtlRuntimeIdentity.InvocationFingerprint(WithPipeline(left)) == EtlRuntimeIdentity.InvocationFingerprint(WithPipeline(right)));
        Assert.Equal(equal, EtlRuntimeJson.ResultEnvelopeHash(left, "{}") == EtlRuntimeJson.ResultEnvelopeHash(right, "{}"));
        Assert.Equal(equal, EtlRuntimeJson.ResultEnvelopeHash("{}", left) == EtlRuntimeJson.ResultEnvelopeHash("{}", right));
    }
}
