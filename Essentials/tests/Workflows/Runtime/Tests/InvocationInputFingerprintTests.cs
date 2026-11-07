using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class InvocationInputFingerprintTests
{
    [Fact]
    public void Equivalent_property_order_number_spelling_and_string_escapes_share_an_identity()
    {
        Assert.Equal(Hash("""{"kind":"Comment","payload":{"b":2e0,"a":1.00},"attributes":{"text":"\u0061"}}"""),
            Hash("""{"attributes":{"text":"a"},"payload":{"a":1e0,"b":2},"kind":"Comment"}"""));
    }

    [Theory]
    [InlineData("{}", "{\"payload\":null}")]
    [InlineData("{\"kind\":\"Event\"}", "{\"kind\":\"event\"}")]
    [InlineData("{\"payload\":[1,2]}", "{\"payload\":[2,1]}")]
    [InlineData("{\"attributes\":{\"a\":1}}", "{\"attributes\":{\"a\":2}}")]
    [InlineData("{\"payload\":\"é\"}", "{\"payload\":\"é\"}")]
    [InlineData("{\"a\":1}", "{\"A\":1}")]
    [InlineData("{\"payload\":0}", "{\"payload\":false}")]
    public void Every_semantic_difference_changes_identity(string first, string second) =>
        Assert.NotEqual(Hash(first), Hash(second));

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"payload\":{\"a\":1,\"a\":1}}")]
    [InlineData("{\"payload\":[{\"a\":1,\"a\":2}]}")]
    [InlineData("{\"attributes\":{\"a\":1,\"\\u0061\":2}}")]
    public void Duplicate_properties_refuse_instead_of_collapsing_into_a_different_guard_value(string content)
    {
        using var document = JsonDocument.Parse(content);
        Assert.Throws<InvalidDataException>(() => RuntimeCheckpointCommitFingerprint.ComputeInvocationInput(1, document.RootElement));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public void Semantic_envelope_must_be_an_object(string content)
    {
        using var document = JsonDocument.Parse(content);
        Assert.Throws<ArgumentException>(() => RuntimeCheckpointCommitFingerprint.ComputeInvocationInput(1, document.RootElement));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void Unknown_fingerprint_versions_refuse(int version)
    {
        using var document = JsonDocument.Parse("{}");
        Assert.Throws<NotSupportedException>(() => RuntimeCheckpointCommitFingerprint.ComputeInvocationInput(version, document.RootElement));
    }

    [Fact]
    public void New_identity_has_its_own_versioned_domain()
    {
        using var document = JsonDocument.Parse("{}");
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeInvocationInput(1, document.RootElement);
        Assert.Equal(64, fingerprint.Length);
        // Fixed independent UTF-8 canonical bytes pin the operation's domain and spelling.
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"Content\":{},\"Format\":\"elsa.invocation.input.v1\"}");
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), fingerprint);
    }

    private static string Hash(string content)
    {
        using var document = JsonDocument.Parse(content);
        return RuntimeCheckpointCommitFingerprint.ComputeInvocationInput(1, document.RootElement);
    }
}
