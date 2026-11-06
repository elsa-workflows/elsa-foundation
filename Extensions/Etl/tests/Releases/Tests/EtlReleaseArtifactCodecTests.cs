using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Etl.Core;
using Elsa.Etl.Releases.Core;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Etl.Releases.Tests;

public sealed class EtlReleaseArtifactCodecTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Owner_wire_round_trip_keeps_original_identity_integer_version_pipeline_and_scope(int format)
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        var original = format == 1 ? EtlReleaseFixture.Artifact(EtlReleaseFixture.V1()) : EtlReleaseFixture.Artifact(EtlReleaseFixture.V2());
        var encoded = codec.Encode(EtlReleaseFixture.Release(original));
        Assert.Equal(original.ArtifactId, encoded.Descriptor.Identity);
        Assert.Equal(original.FormatVersion, encoded.Descriptor.CodecVersion);
        Assert.Equal(new ReleaseOwnerVersion("integer", null, 37), encoded.OwnerVersion);
        var bytes = encoded.GetPayloadBytes(); var read = codec.Decode(bytes);
        Array.Fill(bytes, (byte)'x');
        Assert.Equal(original.ArtifactId, read.Artifact.ArtifactId);
        Assert.Equal(original.ContentHash, read.Artifact.ContentHash);
        Assert.Equal(original.DefinitionId, read.Artifact.DefinitionId);
        Assert.Equal(37, read.Artifact.DefinitionVersion);
        Assert.Equal(original.Pipeline.GetRawText(), read.Artifact.Pipeline.GetRawText());
        Assert.Equal("tenant-a", read.Source.Scope.TenantId);
        Assert.Equal("partition-distinct", read.Source.Scope.PersistencePartition);
        Assert.Equal("source-environment", read.Source.Authority);
        Assert.Equal(encoded.GetPayloadBytes(), codec.Encode(read).GetPayloadBytes());
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("id")]
    [InlineData("integer-version")]
    [InlineData("pipeline-behavior")]
    [InlineData("noncanonical-pipeline")]
    [InlineData("duplicate-property")]
    [InlineData("unknown-envelope-property")]
    [InlineData("invalid-scope")]
    public void Rehashed_transport_cannot_hide_invalid_owner_content(string change)
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        var original = EtlReleaseFixture.Artifact(EtlReleaseFixture.V1());
        var bytes = codec.Encode(EtlReleaseFixture.Release(original)).GetPayloadBytes();
        var json = JsonNode.Parse(bytes)!;
        switch (change)
        {
            case "hash": json["artifact"]!["contentHash"] = new string('0', 64); break;
            case "id": json["artifact"]!["artifactId"] = "etl-v1-incorrect"; break;
            case "integer-version": json["artifact"]!["definitionVersion"] = 0; break;
            case "pipeline-behavior": json["artifact"]!["pipeline"]!["bindings"]!["source"] = "other-source"; break;
            case "unknown-envelope-property": json["unexpected"] = true; break;
            case "invalid-scope": json["source"]!["scope"]!["persistencePartition"] = " "; break;
        }
        var text = json.ToJsonString();
        if (change == "noncanonical-pipeline") text = text.Replace("\"bindings\":{", "\"bindings\": {", StringComparison.Ordinal);
        if (change == "duplicate-property") text = "{\"source\":null," + text[1..];
        var altered = Encoding.UTF8.GetBytes(text);
        Assert.NotEqual(EtlReleaseFixture.Hash(bytes), EtlReleaseFixture.Hash(altered));
        var refusal = Assert.Throws<EtlReleaseArtifactException>(() => codec.Decode(altered));
        Assert.False(refusal.IsUnsupported);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("pagination")]
    public void Self_consistent_unknown_connector_shapes_remain_unsupported(string change)
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        var original = EtlReleaseFixture.Artifact(EtlReleaseFixture.V2());
        var json = JsonNode.Parse(codec.Encode(EtlReleaseFixture.Release(original)).GetPayloadBytes())!;
        var pipeline = json["artifact"]!["pipeline"]!;
        if (change == "source") pipeline["source"]!["kind"] = "future-source";
        if (change == "destination") pipeline["routes"]!["main"]!["kind"] = "future-destination";
        if (change == "pagination") pipeline["source"]!["pagination"]!["kind"] = "future-pagination";
        var element = JsonSerializer.SerializeToElement(pipeline);
        var hash = EtlArtifactJson.Hash(element);
        json["artifact"]!["contentHash"] = hash;
        json["artifact"]!["artifactId"] = EtlArtifactJson.CreateArtifactId(2, original.DefinitionId, original.DefinitionVersion, hash);
        var refusal = Assert.Throws<EtlReleaseArtifactException>(() => codec.Decode(Encoding.UTF8.GetBytes(json.ToJsonString())));
        Assert.True(refusal.IsUnsupported);
        Assert.Equal("etl_connector_kind_unsupported", refusal.Code);
    }

    [Fact]
    public void Portable_validation_does_not_treat_default_host_capacity_as_artifact_semantics()
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        var pipeline = EtlReleaseFixture.V1();
        var original = EtlReleaseFixture.Artifact(pipeline with { Limits = pipeline.Limits with { MaxBatchRecords = 20_001, MaxOutstandingRecords = 20_001 } });
        EtlArtifactSemantics.Validate(original);
        Assert.Equal(original.ArtifactId, codec.Decode(codec.Encode(EtlReleaseFixture.Release(original)).GetPayloadBytes()).Artifact.ArtifactId);
    }

    [Theory]
    [InlineData("bad-schema")]
    [InlineData("missing-field")]
    [InlineData("invalid-limits")]
    public void Owner_transform_and_intrinsic_limits_are_still_required_for_portable_content(string change)
    {
        var pipeline = EtlReleaseFixture.V1();
        pipeline = change switch
        {
            "bad-schema" => pipeline with { SourceSchema = [new("id", EtlColumnType.Int64, true), new("id", EtlColumnType.String, false)] },
            "missing-field" => pipeline with { Mapping = [new("name", EtlFieldOrigin.Source, "absent")] },
            _ => pipeline with { Limits = pipeline.Limits with { MaxBatchBytes = 1 } }
        };
        var refusal = Assert.Throws<EtlArtifactSemanticException>(() => EtlArtifactSemantics.Validate(EtlReleaseFixture.Artifact(pipeline)));
        Assert.StartsWith("etl.executable.", refusal.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Codec_refuses_byte_depth_and_lossy_unicode_boundaries()
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        var original = EtlReleaseFixture.Release(EtlReleaseFixture.Artifact(EtlReleaseFixture.V1()));
        var encoded = codec.Encode(original);
        Assert.Throws<EtlReleaseArtifactException>(() => codec.Decode(encoded.GetPayloadBytes(), new() { MaxBlobBytes = 20 }));
        Assert.Throws<EtlReleaseArtifactException>(() => codec.Encode(original, new() { MaxJsonDepth = 2 }));
        var unsafeScope = new EtlReleaseArtifact(new("scope\ud800-sentinel", original.Source.Scope), original.Artifact);
        var refused = Assert.Throws<EtlReleaseArtifactException>(() => codec.Encode(unsafeScope));
        Assert.DoesNotContain("sentinel", refused.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writer_hints_do_not_reject_a_valid_artifact_at_its_exact_output_budget(bool escaped)
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        var pipeline = EtlReleaseFixture.V1();
        if (escaped) pipeline = pipeline with { Mapping = [new("<name>☃", EtlFieldOrigin.Source, "name")] };
        var original = EtlReleaseFixture.Release(EtlReleaseFixture.Artifact(pipeline));
        if (escaped) original = new(new("source-<environment>☃", original.Source.Scope), original.Artifact);
        var bytes = codec.Encode(original).GetPayloadBytes();
        Assert.Equal(bytes, codec.Encode(original, new() { MaxBlobBytes = bytes.Length }).GetPayloadBytes());
        Assert.Throws<EtlReleaseArtifactException>(() => codec.Encode(original, new() { MaxBlobBytes = bytes.Length - 1 }));
    }

    [Fact]
    public void Oversized_retained_pipeline_and_input_are_refused_before_large_copy_or_string_allocation()
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        using var document = JsonDocument.Parse("{\"padding\":\"" + new string('x', 4 * 1024 * 1024) + "\"}");
        var oversized = new EtlArtifact(1, "etl-v1-placeholder", new string('0', 64), "orders", 37, document.RootElement);
        var release = EtlReleaseFixture.Release(oversized);
        var payload = new byte[4 * 1024 * 1024];
        // Warm the refusal paths before measuring allocations on the current test thread.
        Assert.Throws<EtlReleaseArtifactException>(() => codec.Encode(release));
        Assert.Throws<EtlReleaseArtifactException>(() => codec.Decode(payload));
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<EtlReleaseArtifactException>(() => codec.Encode(release));
        Assert.Throws<EtlReleaseArtifactException>(() => codec.Decode(payload));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 256 * 1024);
    }
}
